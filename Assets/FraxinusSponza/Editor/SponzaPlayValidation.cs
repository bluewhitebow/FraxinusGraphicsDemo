using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Fraxinus.SponzaDemo.Editor
{
    // Batch entry point: exercises the actual demo and checks that Play mode leaves assets intact.
    [InitializeOnLoad]
    public static class SponzaPlayValidation
    {
        const string Active = "Fraxinus.Sponza.Validation.Active";
        const string Snapshot = "Fraxinus.Sponza.Validation.Snapshot";
        const string Result = "Fraxinus.Sponza.Validation.Result";
        const BindingFlags Methods = BindingFlags.Instance | BindingFlags.NonPublic;
        static double started;
        static int stage;
        static int frames;
        static bool stopping;
        static Color32[] litPixels;
        static Color32[] cachedPixels;
        static Color32[] probePixels;
        static readonly List<string> errors = new List<string>();

        static SponzaPlayValidation()
        {
            EditorApplication.playModeStateChanged += StateChanged;
            EditorApplication.update += Update;
        }

        public static void Run()
        {
            if (File.Exists("Library/SponzaPlayValidation.txt")) File.Delete("Library/SponzaPlayValidation.txt");
            EditorSceneManager.OpenScene("Assets/FraxinusSponza/Scenes/Sponza.unity");
            SessionState.SetString(Snapshot, AssetFingerprint());
            SessionState.SetString(Result, "");
            SessionState.SetBool(Active, true);
            EditorApplication.isPlaying = true;
        }

        static void StateChanged(PlayModeStateChange state)
        {
            if (!SessionState.GetBool(Active, false)) return;
            if (state == PlayModeStateChange.EnteredPlayMode)
            {
                started = EditorApplication.timeSinceStartup;
                stage = frames = 0;
                stopping = false;
                errors.Clear();
                Application.logMessageReceived += CaptureError;
            }
            if (state == PlayModeStateChange.EnteredEditMode)
            {
                if (AssetFingerprint() != SessionState.GetString(Snapshot, ""))
                    errors.Add("Play mode changed serialized scene or rendering assets.");
                var expected = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>("Assets/FraxinusSponza/Rendering/SponzaPipeline.asset");
                if (GraphicsSettings.defaultRenderPipeline != expected || QualitySettings.renderPipeline != null)
                    errors.Add("Original pipeline assignments were not restored.");
                var result = SessionState.GetString(Result, "") + string.Join("\n", errors);
                Directory.CreateDirectory("Library");
                File.WriteAllText("Library/SponzaPlayValidation.txt", string.IsNullOrEmpty(result)
                    ? "PASS: native, STP, SGSR1, SGSR2, cached shadows, GI response, probe overlay, reset and asset restoration.\n"
                    : result);
                SessionState.SetBool(Active, false);
                EditorApplication.Exit(string.IsNullOrEmpty(result) ? 0 : 1);
            }
        }

        static void Update()
        {
            if (!SessionState.GetBool(Active, false) || !EditorApplication.isPlaying || stopping) return;
            if (started == 0) started = EditorApplication.timeSinceStartup;
            if (EditorApplication.timeSinceStartup - started > 180)
            {
                Finish("Play validation timed out.");
                return;
            }
            if (++frames < 20) return;
            frames = 0;
            try
            {
                var controller = UnityEngine.Object.FindObjectOfType<SponzaDemoController>();
                if (controller == null) throw new InvalidOperationException("Demo controller is missing.");
                var pipeline = (UniversalRenderPipelineAsset)GraphicsSettings.currentRenderPipeline;
                switch (stage++)
                {
                    case 0:
                        if (!pipeline.name.Contains("Demo Runtime")) throw new InvalidOperationException("The demo did not instantiate its runtime pipeline.");
                        litPixels = Capture("Native");
                        Invoke(controller, "SetGI", false);
                        break;
                    case 1:
                        var dark = Capture("GI-Off");
                        double difference = 0;
                        for (int i = 0; i < dark.Length; i++)
                            difference += Math.Abs(dark[i].r - litPixels[i].r) + Math.Abs(dark[i].g - litPixels[i].g) + Math.Abs(dark[i].b - litPixels[i].b);
                        difference /= dark.Length * 3.0 * 255.0;
                        if (difference < .001) throw new InvalidOperationException("GI switch produced no measurable image change.");
                        File.WriteAllText("Library/SponzaGIDifference.txt", difference.ToString("F6", System.Globalization.CultureInfo.InvariantCulture));
                        Invoke(controller, "SetGI", true);
                        Invoke(controller, "ApplyPreset", 1);
                        break;
                    case 2:
                        Capture("STP");
                        if (pipeline.upscalingFilter != UpscalingFilterSelection.STP) throw new InvalidOperationException("STP selection failed.");
                        Invoke(controller, "ApplyPreset", 2);
                        break;
                    case 3:
                        Capture("SGSR1");
                        if (pipeline.upscalingFilter != UpscalingFilterSelection.SGSR1) throw new InvalidOperationException("SGSR1 selection failed.");
                        Invoke(controller, "ApplyPreset", 3);
                        break;
                    case 4:
                        Capture("SGSR2");
                        if (pipeline.upscalingFilter != UpscalingFilterSelection.SGSR2) throw new InvalidOperationException("SGSR2 selection failed.");
                        var cached = typeof(SponzaDemoController).GetField("cachedPipeline", Methods).GetValue(controller);
                        Invoke(controller, "ActivatePipeline", cached);
                        break;
                    case 5:
                        cachedPixels = Capture("Cached-Shadows");
                        if (!pipeline.cachedMainlightShadowEnabled) throw new InvalidOperationException("Shadow cache preset failed.");
                        Invoke(controller, "SetProbeDisplay", true);
                        break;
                    case 6:
                        probePixels = Capture("Probes");
                        if (Difference(probePixels, cachedPixels) < .005) throw new InvalidOperationException("Probe display produced no visible overlay.");
                        Invoke(controller, "SetProbeDisplay", false);
                        Invoke(controller, "SetGIOnly", true);
                        break;
                    case 7:
                        if (Difference(Capture("GI-Only"), cachedPixels) < .01) throw new InvalidOperationException("GI Only produced no visible diagnostic view.");
                        Invoke(controller, "ResetDemo");
                        break;
                    default:
                        Capture("Reset");
                        if (pipeline.renderScale != 1 || pipeline.cachedMainlightShadowEnabled) throw new InvalidOperationException("Reset failed.");
                        Finish(null);
                        break;
                }
            }
            catch (Exception e) { Finish(e.ToString()); }
        }

        static void Finish(string error)
        {
            stopping = true;
            Application.logMessageReceived -= CaptureError;
            SessionState.SetString(Result, (error ?? "") + string.Join("\n", errors));
            errors.Clear();
            EditorApplication.isPlaying = false;
        }

        static void CaptureError(string message, string stackTrace, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
                errors.Add(message);
        }

        static object Invoke(SponzaDemoController controller, string name, object arg = null)
            => typeof(SponzaDemoController).GetMethod(name, Methods).Invoke(controller, arg == null ? null : new[] { arg });

        static double Difference(Color32[] a, Color32[] b)
        {
            double sum = 0;
            for (int i = 0; i < a.Length; i++)
                sum += Math.Abs(a[i].r - b[i].r) + Math.Abs(a[i].g - b[i].g) + Math.Abs(a[i].b - b[i].b);
            return sum / (a.Length * 3.0 * 255.0);
        }

        static Color32[] Capture(string name)
        {
            SponzaProjectSetup.Capture();
            var bytes = File.ReadAllBytes("Library/SponzaPreview.png");
            File.WriteAllBytes("Library/Sponza-" + name + ".png", bytes);
            var texture = new Texture2D(2, 2);
            try { texture.LoadImage(bytes); return texture.GetPixels32(); }
            finally { UnityEngine.Object.DestroyImmediate(texture); }
        }

        static string AssetFingerprint()
        {
            using (var hash = SHA256.Create())
                return string.Join("\n", Directory.GetFiles("Assets/FraxinusSponza", "*", SearchOption.AllDirectories)
                    .Where(path => path.EndsWith(".asset") || path.EndsWith(".unity"))
                    .OrderBy(path => path).Select(path => path + " " + Convert.ToBase64String(hash.ComputeHash(File.ReadAllBytes(path)))));
        }
    }
}
