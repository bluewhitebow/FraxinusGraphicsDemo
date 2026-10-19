using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Fraxinus.SponzaDemo.Editor
{
    public static class SponzaProjectSetup
    {
        const string Root = "Assets/FraxinusSponza/";
        const string ScenePath = Root + "Scenes/Sponza.unity";
        const string BakingPath = Root + "Scenes/Sponza/Sponza Baking Set.asset";

        [MenuItem("Fraxinus/Sponza/Open Demo")]
        public static void OpenDemo()
        {
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                EditorSceneManager.OpenScene(ScenePath);
        }

        // Explicit setup for recreating the checked-in configuration; never runs on import.
        public static void Prepare()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath);
            var pipeline = Required<UniversalRenderPipelineAsset>(Root + "Rendering/SponzaPipeline.asset");
            GraphicsSettings.defaultRenderPipeline = pipeline;
            int quality = QualitySettings.GetQualityLevel();
            for (int index = 0; index < QualitySettings.names.Length; ++index)
            {
                QualitySettings.SetQualityLevel(index, false);
                QualitySettings.renderPipeline = null;
            }
            QualitySettings.SetQualityLevel(quality, false);

            var camera = Camera.main;
            if (camera == null) throw new InvalidOperationException("Sponza has no Main Camera.");
            camera.transform.SetPositionAndRotation(new Vector3(10.9988f, .8621f, -.7983f),
                new Quaternion(.1078188f, .6756859f, .1008727f, -.7222523f));
            camera.fieldOfView = 60;
            camera.allowMSAA = false;
            camera.allowDynamicResolution = false;
            camera.GetUniversalAdditionalCameraData().renderPostProcessing = true;
            RenderSettings.sun = SceneObjects<Light>(scene).FirstOrDefault(light => light.type == LightType.Directional);
            if (RenderSettings.sun == null) throw new InvalidOperationException("Sponza has no directional light.");

            string previousBakingPath = Root + "Scenes/Sponza/Example Baking Set.asset";
            if (AssetDatabase.LoadMainAssetAtPath(previousBakingPath) != null)
            {
                string error = AssetDatabase.MoveAsset(previousBakingPath, BakingPath);
                if (!string.IsNullOrEmpty(error)) throw new InvalidOperationException(error);
            }
            var baking = Required<ProbeVolumeBakingSet>(BakingPath);
            baking.name = "Sponza Baking Set";
            baking.aprtUseLightingScenarios = false;
            EditorUtility.SetDirty(baking);
            foreach (var apv in SceneObjects<ProbeVolume>(scene)) apv.gameObject.SetActive(false);

            string lightingPath = Root + "Rendering/SponzaLighting.asset";
            var lighting = AssetDatabase.LoadAssetAtPath<LightingSettings>(lightingPath);
            if (lighting == null)
            {
                lighting = new LightingSettings { name = "Sponza Lighting" };
                AssetDatabase.CreateAsset(lighting, lightingPath);
            }
            lighting.autoGenerate = false;
            lighting.bakedGI = false;
            lighting.realtimeGI = false;
            Lightmapping.lightingSettings = lighting;
            EditorUtility.SetDirty(lighting);

            var controller = SceneObjects<SponzaDemoController>(scene).FirstOrDefault();
            if (controller == null) controller = new GameObject("Demo Controls").AddComponent<SponzaDemoController>();
            using (var serialized = new SerializedObject(controller))
            {
                serialized.FindProperty("cachedShadowPipeline").objectReferenceValue =
                    Required<UniversalRenderPipelineAsset>(Root + "Rendering/SponzaCachedShadows.asset");
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Could not save Sponza scene.");
            AssetDatabase.SaveAssets();
            AssetDatabase.ForceReserializeAssets(new[] { ScenePath, BakingPath,
                Root + "Scenes/Sponza/Global Volume Profile.asset", Root + "Rendering/DefaultVolumeProfile.asset" });
            Validate();
        }

        [MenuItem("Fraxinus/Sponza/Validate Demo")]
        public static void Validate()
        {
            var scene = SceneManager.GetActiveScene();
            if (scene.path != ScenePath) throw new InvalidOperationException("Open the Sponza demo before validating.");
            var report = new ValidationReport { unityVersion = Application.unityVersion, scene = ScenePath };
            foreach (var transform in SceneObjects<Transform>(scene))
                report.missingScripts += GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(transform.gameObject);
            var renderers = SceneObjects<Renderer>(scene);
            report.renderers = renderers.Length;
            foreach (var renderer in renderers)
                foreach (var material in renderer.sharedMaterials)
                    if (material == null || material.shader == null || material.shader.name == "Hidden/InternalErrorShader")
                        ++report.missingMaterials;
            foreach (var mesh in SceneObjects<MeshFilter>(scene))
                if (mesh.sharedMesh == null) ++report.missingMeshes;
            foreach (var mesh in SceneObjects<SkinnedMeshRenderer>(scene))
                if (mesh.sharedMesh == null) ++report.missingMeshes;
            if (report.missingScripts + report.missingMaterials + report.missingMeshes > 0)
                report.errors.Add("Scene has missing scripts, materials, shaders or meshes.");
            if (report.renderers == 0) report.errors.Add("Scene contains no renderers.");
            if (GraphicsSettings.currentRenderPipeline != Required<UniversalRenderPipelineAsset>(Root + "Rendering/SponzaPipeline.asset"))
                report.errors.Add("The demo pipeline is not active.");
            string[] ids = { "com.unity.render-pipelines.core", "com.unity.render-pipelines.universal",
                "com.unity.render-pipelines.universal-config", "com.fraxinus.rendering.light-transport" };
            string[] folders = { "Fraxinus-RPCore", "Fraxinus-URPExtensions", "Fraxinus-URPGlobal", "Fraxinus-Light-Transport" };
            string sharedRevision = null;
            for (int i = 0; i < ids.Length; ++i)
            {
                var package = PackageInfo.FindForAssetPath("Packages/" + ids[i] + "/package.json");
                string prefix = ids[i] + "@https://github.com/bluewhitebow/FraxinusGraphics.git?path=Packages/" + folders[i] + "#";
                if (package == null || package.source != UnityEditor.PackageManager.PackageSource.Git ||
                    !package.packageId.StartsWith(prefix, StringComparison.Ordinal))
                {
                    report.errors.Add(ids[i] + " is not resolved from its FraxinusGraphics Git subdirectory.");
                    continue;
                }
                string revision = package.packageId.Substring(prefix.Length);
                report.packages.Add(package.packageId + " (resolved " + package.git?.hash + ")");
                if (revision.Length != 40 || !revision.All(Uri.IsHexDigit) || package.git?.hash != revision)
                    report.errors.Add(ids[i] + " must resolve to the full commit pinned in its Git URL.");
                if (sharedRevision != null && sharedRevision != revision)
                    report.errors.Add("All four Fraxinus packages must use the same commit.");
                sharedRevision = revision;
            }
            var baking = Required<ProbeVolumeBakingSet>(BakingPath);
            if (baking.aprtUseLightingScenarios) report.errors.Add("Select Live Scene Lighting for this demo.");
            try
            {
                report.meanBrightness = RenderPreview();
                var panel = DebugManager.instance.GetPanel("APRT Volumes");
                var resident = FindValue(panel, "Resident probes");
                var status = FindValue(panel, "Status");
                report.residentProbes = resident == null ? 0 : Convert.ToInt32(resident.GetValue());
                report.aprtStatus = status?.GetValue()?.ToString() ?? "APRT debug panel unavailable";
                if (report.residentProbes != 4563) report.errors.Add("Expected 4563 resident APRTV probes.");
                if (report.aprtStatus != "Ready") report.errors.Add("APRTV is not ready: " + report.aprtStatus);
            }
            catch (Exception exception) { report.errors.Add(exception.Message); }
            report.passed = report.errors.Count == 0;
            Directory.CreateDirectory("Library");
            File.WriteAllText("Library/SponzaValidation.json", JsonUtility.ToJson(report, true));
            if (!report.passed) throw new InvalidOperationException(string.Join("\n", report.errors));
            Debug.Log("Sponza validated: no missing scene references; four Git packages at one pinned commit; 4563 resident probes. Report: Library/SponzaValidation.json");
        }

        [MenuItem("Fraxinus/Sponza/Capture Preview")]
        public static void Capture()
        {
            RenderPreview();
            Debug.Log("Saved Library/SponzaPreview.png");
        }

        static float RenderPreview()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                throw new InvalidOperationException("Rendering validation needs a graphics device; omit -nographics.");
            var camera = Camera.main;
            if (camera == null) throw new InvalidOperationException("No Main Camera to render.");
            var oldTarget = camera.targetTexture;
            var oldActive = RenderTexture.active;
            float oldAspect = camera.aspect;
            var target = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32);
            var image = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            try
            {
                target.Create();
                camera.targetTexture = target;
                camera.aspect = 1280f / 720f;
                if (RenderPipelineManager.currentPipeline == null) camera.Render();
                for (int frame = 0; frame < 8; ++frame)
                    RenderPipeline.SubmitRenderRequest(camera, new RenderPipeline.StandardRequest { destination = target });
                RenderTexture.active = target;
                image.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
                image.Apply();
                var pixels = image.GetPixels32();
                long brightness = 0;
                foreach (var pixel in pixels) brightness += pixel.r + pixel.g + pixel.b;
                float mean = brightness / (pixels.Length * 3f * 255f);
                Directory.CreateDirectory("Library");
                File.WriteAllBytes("Library/SponzaPreview.png", image.EncodeToPNG());
                if (mean < .005f) throw new InvalidOperationException("The captured frame is black; inspect Library/SponzaPreview.png.");
                return mean;
            }
            finally
            {
                camera.targetTexture = oldTarget;
                camera.aspect = oldAspect;
                RenderTexture.active = oldActive;
                target.Release();
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(image);
            }
        }

        static T Required<T>(string path) where T : Object => AssetDatabase.LoadAssetAtPath<T>(path)
            ?? throw new FileNotFoundException("Required demo asset is missing: " + path);

        static T[] SceneObjects<T>(Scene scene) where T : Component => scene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<T>(true)).ToArray();

        static DebugUI.Value FindValue(DebugUI.IContainer container, string name)
        {
            if (container == null) return null;
            foreach (var child in container.children)
            {
                if (child is DebugUI.Value value && value.displayName == name) return value;
                if (child is DebugUI.IContainer nested && FindValue(nested, name) is DebugUI.Value found) return found;
            }
            return null;
        }

        [Serializable]
        sealed class ValidationReport
        {
            public bool passed;
            public string unityVersion, scene, aprtStatus;
            public int renderers, missingScripts, missingMaterials, missingMeshes, residentProbes;
            public float meanBrightness;
            public List<string> packages = new List<string>();
            public List<string> errors = new List<string>();
        }
    }
}
