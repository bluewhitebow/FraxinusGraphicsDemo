using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Fraxinus.SponzaDemo
{
    /// <summary>Small, asset-safe controls for the Sponza rendering example.</summary>
    public sealed class SponzaDemoController : MonoBehaviour
    {
        [Tooltip("The matching pipeline preset with Cached CSM enabled. The demo clones both presets at runtime.")]
        public UniversalRenderPipelineAsset cachedShadowPipeline;

        const float UpscaledRenderScale = 0.75f;
        static readonly string[] PresetNames = { "Native", "STP", "SGSR1", "SGSR2" };
        static readonly Vector3 CourtyardPosition = new Vector3(10.9988f, 0.8621f, -0.7983f);
        static readonly Quaternion CourtyardRotation = new Quaternion(0.1078188f, 0.6756859f, 0.1008727f, -0.7222523f);
        static readonly Vector3 BalconyPosition = new Vector3(-5.588f, 6.561f, 4.486f);
        static readonly Quaternion BalconyRotation = new Quaternion(0.0494804f, 0.8503124f, -0.0812044f, 0.5176161f);

        RenderPipelineAsset originalGraphicsPipeline;
        RenderPipelineAsset originalQualityPipeline;
        int originalQualityAntialiasing;
        UniversalRenderPipelineAsset normalPipeline;
        UniversalRenderPipelineAsset cachedPipeline;
        UniversalRenderPipelineAsset activePipeline;
        Camera demoCamera;
        UniversalAdditionalCameraData cameraData;
        bool addedCameraData;
        Vector3 initialCameraPosition;
        Quaternion initialCameraRotation;
        bool originalPostProcessing;
        AntialiasingMode originalAntialiasing;
        bool originalAllowMSAA;
        bool originalDynamicResolution;
        DepthTextureMode originalDepthTextureMode;
        Light sun;
        Quaternion initialSunRotation;
        float sunRotation;
        Volume volume;
        VolumeProfile originalInstanceProfile;
        VolumeProfile runtimeProfile;
        APRTOptions aprtOptions;
        float initialGIIntensity = 1;
        bool initialGIOverride;
        bool giEnabled = true;
        bool giOnly;
        bool showProbes;
        int preset;
        bool initialized;
        bool showPanel = true;
        bool isFlying;
        float yaw;
        float pitch;
        CursorLockMode originalCursorLock;
        bool originalCursorVisible;
        CursorLockMode flightCursorLock;
        bool flightCursorVisible;
        Vector2 scroll;
        GUIStyle titleStyle;
        GUIStyle bodyStyle;
        string setupMessage;

        DebugUI.Panel debugPanel;
        DebugUI.EnumField displayField;
        DebugUI.BoolField probesField;
        DebugUI.Value statusField;
        DebugUI.Value residentField;
        int lightingValue;
        int giOnlyValue;
        bool debugSnapshotCaptured;
        int originalDisplayMode;
        bool originalProbeDisplay;
        bool probeSizeSnapshotCaptured;
        float originalProbeSize;
        bool probeColorSnapshotCaptured;
        int originalProbeColorMode;

        void OnEnable()
        {
            setupMessage = null;
            giOnly = false;
            showProbes = false;
            sunRotation = 0;
            originalGraphicsPipeline = GraphicsSettings.renderPipelineAsset;
            originalQualityPipeline = QualitySettings.renderPipeline;
            originalQualityAntialiasing = QualitySettings.antiAliasing;
            originalCursorLock = Cursor.lockState;
            originalCursorVisible = Cursor.visible;
            var source = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            demoCamera = Camera.main;
            if (source == null || demoCamera == null)
            {
                setupMessage = "Assign the Fraxinus pipeline and a camera tagged MainCamera.";
                return;
            }

            normalPipeline = Instantiate(source);
            normalPipeline.name = source.name + " (Demo Runtime)";
            normalPipeline.hideFlags = HideFlags.HideAndDontSave;
            if (cachedShadowPipeline != null)
            {
                cachedPipeline = Instantiate(cachedShadowPipeline);
                cachedPipeline.name = cachedShadowPipeline.name + " (Demo Runtime)";
                cachedPipeline.hideFlags = HideFlags.HideAndDontSave;
            }

            cameraData = demoCamera.GetComponent<UniversalAdditionalCameraData>();
            addedCameraData = cameraData == null;
            if (addedCameraData) cameraData = demoCamera.gameObject.AddComponent<UniversalAdditionalCameraData>();
            originalPostProcessing = cameraData.renderPostProcessing;
            originalAntialiasing = cameraData.antialiasing;
            originalAllowMSAA = demoCamera.allowMSAA;
            originalDynamicResolution = demoCamera.allowDynamicResolution;
            originalDepthTextureMode = demoCamera.depthTextureMode;
            initialCameraPosition = demoCamera.transform.position;
            initialCameraRotation = demoCamera.transform.rotation;
            SyncLookAngles();

            sun = RenderSettings.sun;
            if (sun == null)
                foreach (var light in FindObjectsOfType<Light>())
                    if (light.type == LightType.Directional && light.isActiveAndEnabled)
                    {
                        sun = light;
                        break;
                    }
            if (sun != null) initialSunRotation = sun.transform.rotation;

            CloneVolumeProfile();
            initialized = true;
            ActivatePipeline(normalPipeline);
            ApplyPreset(0);
        }

        void CloneVolumeProfile()
        {
            foreach (var candidate in FindObjectsOfType<Volume>())
            {
                if (!candidate.isGlobal || candidate.weight <= 0) continue;
                if (volume == null || candidate.priority > volume.priority) volume = candidate;
            }
            if (volume == null)
            {
                setupMessage = "Add a global Volume to enable GI controls.";
                return;
            }

            originalInstanceProfile = volume.HasInstantiatedProfile() ? volume.profile : null;
            var source = originalInstanceProfile != null ? originalInstanceProfile : volume.sharedProfile;
            runtimeProfile = ScriptableObject.CreateInstance<VolumeProfile>();
            runtimeProfile.name = "Sponza Demo Runtime Profile";
            runtimeProfile.hideFlags = HideFlags.HideAndDontSave;
            // Cloning the profile alone keeps references to the source components.
            // Clone each component as well before changing any Volume parameters.
            if (source != null)
                foreach (var component in source.components)
                {
                    var clone = Instantiate(component);
                    clone.hideFlags = HideFlags.HideAndDontSave;
                    runtimeProfile.components.Add(clone);
                }
            if (!runtimeProfile.TryGet(out aprtOptions)) aprtOptions = runtimeProfile.Add<APRTOptions>();
            initialGIIntensity = aprtOptions.giIntensity.value;
            initialGIOverride = aprtOptions.giIntensity.overrideState;
            giEnabled = !initialGIOverride || initialGIIntensity > 0;
            volume.profile = runtimeProfile;
        }

        void ActivatePipeline(UniversalRenderPipelineAsset pipeline)
        {
            if (pipeline == null) return;
            if (activePipeline != null)
            {
                pipeline.renderScale = activePipeline.renderScale;
                pipeline.upscalingFilter = activePipeline.upscalingFilter;
            }
            activePipeline = pipeline;
            GraphicsSettings.renderPipelineAsset = pipeline;
            QualitySettings.renderPipeline = pipeline;
            if (cameraData != null) cameraData.resetHistory = true;
            debugPanel = null;
            displayField = null;
            probesField = null;
            statusField = null;
            residentField = null;
        }

        void ApplyPreset(int value)
        {
            if (activePipeline == null || cameraData == null) return;
            preset = value;
            activePipeline.renderScale = value == 0 ? 1 : UpscaledRenderScale;
            activePipeline.upscalingFilter = value == 1 ? UpscalingFilterSelection.STP
                : value == 2 ? UpscalingFilterSelection.SGSR1
                : value == 3 ? UpscalingFilterSelection.SGSR2 : UpscalingFilterSelection.Linear;
            demoCamera.allowMSAA = false;
            demoCamera.allowDynamicResolution = false;
            demoCamera.depthTextureMode |= DepthTextureMode.Depth | DepthTextureMode.MotionVectors;
            cameraData.renderPostProcessing = true;
            cameraData.antialiasing = value == 1 || value == 3
                ? AntialiasingMode.TemporalAntiAliasing : AntialiasingMode.None;
            cameraData.resetHistory = true;
        }

        void Update()
        {
            if (!initialized) return;
            RefreshDebugFields();
            if (Input.GetKeyDown(KeyCode.H)) showPanel = !showPanel;
            if (Input.GetKeyDown(KeyCode.Alpha1)) SetView(CourtyardPosition, CourtyardRotation);
            if (Input.GetKeyDown(KeyCode.Alpha2)) SetView(BalconyPosition, BalconyRotation);
            if (Input.GetKeyDown(KeyCode.Escape)) EndFlight();

            if (Input.GetMouseButtonDown(1))
            {
                isFlying = true;
                flightCursorLock = Cursor.lockState;
                flightCursorVisible = Cursor.visible;
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
                SyncLookAngles();
            }
            if (Input.GetMouseButtonUp(1)) EndFlight();
            if (!isFlying) return;

            yaw += Input.GetAxisRaw("Mouse X") * 2;
            pitch = Mathf.Clamp(pitch - Input.GetAxisRaw("Mouse Y") * 2, -89, 89);
            demoCamera.transform.rotation = Quaternion.Euler(pitch, yaw, 0);
            Vector3 motion = Vector3.zero;
            if (Input.GetKey(KeyCode.W)) motion.z += 1;
            if (Input.GetKey(KeyCode.S)) motion.z -= 1;
            if (Input.GetKey(KeyCode.D)) motion.x += 1;
            if (Input.GetKey(KeyCode.A)) motion.x -= 1;
            if (Input.GetKey(KeyCode.E)) motion.y += 1;
            if (Input.GetKey(KeyCode.Q)) motion.y -= 1;
            float speed = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift) ? 12 : 3;
            demoCamera.transform.position += demoCamera.transform.TransformDirection(Vector3.ClampMagnitude(motion, 1))
                * speed * Time.unscaledDeltaTime;
        }

        void RefreshDebugFields()
        {
            var current = DebugManager.instance.GetPanel("APRT Volumes");
            if (current == null || ReferenceEquals(current, debugPanel)) return;
            debugPanel = current;
            displayField = FindField<DebugUI.EnumField>(current, "Display view");
            probesField = FindField<DebugUI.BoolField>(current, "Show Probes");
            statusField = FindField<DebugUI.Value>(current, "Status");
            residentField = FindField<DebugUI.Value>(current, "Resident probes");
            var probeSize = FindField<DebugUI.FloatField>(current, "Probe Size");
            if (probeSize != null)
            {
                if (!probeSizeSnapshotCaptured)
                {
                    originalProbeSize = probeSize.GetValue();
                    probeSizeSnapshotCaptured = true;
                }
                probeSize.SetValue(0.09f);
            }
            var probeColor = FindField<DebugUI.EnumField>(current, "Color Mode");
            if (probeColor != null && TryEnumValue(probeColor, "Relit Lighting", out int relitLighting))
            {
                if (!probeColorSnapshotCaptured)
                {
                    originalProbeColorMode = probeColor.GetValue();
                    probeColorSnapshotCaptured = true;
                }
                probeColor.SetValue(relitLighting);
            }
            if (displayField != null && TryEnumValue(displayField, "Lighting", out lightingValue)
                && TryEnumValue(displayField, "GI Only", out giOnlyValue))
            {
                if (!debugSnapshotCaptured)
                {
                    originalDisplayMode = displayField.GetValue();
                    originalProbeDisplay = probesField != null && probesField.GetValue();
                    debugSnapshotCaptured = true;
                }
                displayField.SetValue(giOnly ? giOnlyValue : lightingValue);
            }
            else displayField = null;
            if (probesField != null) probesField.SetValue(showProbes);
        }

        static T FindField<T>(DebugUI.IContainer container, string fieldName) where T : DebugUI.Widget
        {
            foreach (var widget in container.children)
            {
                if (widget is T field && field.displayName == fieldName) return field;
                if (widget is DebugUI.IContainer children)
                {
                    var found = FindField<T>(children, fieldName);
                    if (found != null) return found;
                }
            }
            return null;
        }

        static bool TryEnumValue(DebugUI.EnumField field, string label, out int value)
        {
            for (int i = 0; field.enumNames != null && i < field.enumNames.Length; i++)
                if (field.enumNames[i].text == label)
                {
                    value = field.enumValues[i];
                    return true;
                }
            value = 0;
            return false;
        }

        void SetGI(bool enabled)
        {
            giEnabled = enabled;
            if (aprtOptions == null) return;
            aprtOptions.giIntensity.value = enabled ? initialGIIntensity : 0;
            aprtOptions.giIntensity.overrideState = enabled ? initialGIOverride : true;
            cameraData.resetHistory = true;
        }

        void SetGIOnly(bool enabled)
        {
            giOnly = enabled;
            if (displayField != null) displayField.SetValue(enabled ? giOnlyValue : lightingValue);
        }

        void SetProbeDisplay(bool enabled)
        {
            showProbes = enabled;
            if (probesField != null) probesField.SetValue(enabled);
        }

        void SetView(Vector3 position, Quaternion rotation)
        {
            demoCamera.transform.SetPositionAndRotation(position, rotation);
            SyncLookAngles();
            cameraData.resetHistory = true;
            cameraData.SetCachedCSMDirty();
        }

        void SyncLookAngles()
        {
            yaw = demoCamera.transform.eulerAngles.y;
            pitch = Mathf.DeltaAngle(0, demoCamera.transform.eulerAngles.x);
        }

        void EndFlight()
        {
            if (!isFlying) return;
            isFlying = false;
            Cursor.lockState = flightCursorLock;
            Cursor.visible = flightCursorVisible;
        }

        void OnApplicationFocus(bool focused)
        {
            if (!focused) EndFlight();
        }

        void OnGUI()
        {
            if (titleStyle == null)
            {
                titleStyle = new GUIStyle(GUI.skin.label) { fontSize = 19, fontStyle = FontStyle.Bold };
                bodyStyle = new GUIStyle(GUI.skin.label) { wordWrap = true };
            }
            if (!showPanel)
            {
                GUI.Box(new Rect(16, 16, 225, 30), "H - show Fraxinus demo controls");
                return;
            }

            GUILayout.BeginArea(new Rect(16, 16, Mathf.Min(365, Screen.width - 32), Screen.height - 32), GUI.skin.box);
            scroll = GUILayout.BeginScrollView(scroll);
            GUILayout.Label("FRAXINUS / SPONZA", titleStyle);
            GUILayout.Label("Lighting, shadows and reconstruction", bodyStyle);
            if (!string.IsNullOrEmpty(setupMessage)) GUILayout.Label(setupMessage, bodyStyle);
            if (initialized)
            {
                GUILayout.Space(8);
                GUI.enabled = aprtOptions != null;
                bool newGI = GUILayout.Toggle(giEnabled, "APRTV indirect lighting");
                if (newGI != giEnabled) SetGI(newGI);
                GUI.enabled = displayField != null;
                bool newGIOnly = GUILayout.Toggle(displayField != null && displayField.GetValue() == giOnlyValue, "GI only");
                if (displayField != null && newGIOnly != giOnly)
                    SetGIOnly(newGIOnly);
                GUI.enabled = probesField != null;
                bool newProbes = GUILayout.Toggle(probesField != null && probesField.GetValue(), "Show probes");
                if (probesField != null && newProbes != showProbes)
                    SetProbeDisplay(newProbes);
                GUI.enabled = true;
                if (statusField != null) GUILayout.Label("APRTV: " + statusField.GetValue(), bodyStyle);
                if (residentField != null) GUILayout.Label("Resident probes: " + residentField.GetValue());
                if (displayField == null) GUILayout.Label("Waiting for APRTV debug controls...", bodyStyle);

                GUILayout.Space(8);
                GUI.enabled = cachedPipeline != null && cachedPipeline.cachedMainlightShadowEnabled
                    && !normalPipeline.cachedMainlightShadowEnabled;
                bool cached = activePipeline.cachedMainlightShadowEnabled;
                bool newCached = GUILayout.Toggle(cached, "Cache distant shadow cascades");
                if (cached != newCached) ActivatePipeline(newCached ? cachedPipeline : normalPipeline);
                GUI.enabled = true;
                if (GUILayout.Button("Refresh shadow cache")) cameraData.SetCachedCSMDirty();

                GUILayout.Space(8);
                GUILayout.Label("Resolution preset");
                int selected = GUILayout.SelectionGrid(preset, PresetNames, 4);
                if (selected != preset) ApplyPreset(selected);
                GUILayout.Label($"Render scale: {activePipeline.renderScale:0.00}  |  MSAA: off");
                GUILayout.Label("Filter setting: " + activePipeline.upscalingFilter);
                if (activePipeline.upscalingFilter == UpscalingFilterSelection.STP && !STP.IsSupported())
                    GUILayout.Label("STP unavailable on this device; the pipeline uses SGSR1.", bodyStyle);
                if ((preset == 1 || preset == 3) && (cameraData.renderType != CameraRenderType.Base
                    || (cameraData.cameraStack != null && cameraData.cameraStack.Count > 0)))
                    GUILayout.Label("Temporal upscaling needs one Base camera without a camera stack.", bodyStyle);

                GUILayout.Space(8);
                GUI.enabled = sun != null;
                GUILayout.Label($"Sun rotation: {sunRotation:0} degrees");
                float newRotation = GUILayout.HorizontalSlider(sunRotation, -180, 180);
                if (!Mathf.Approximately(newRotation, sunRotation))
                {
                    sunRotation = newRotation;
                    sun.transform.rotation = Quaternion.AngleAxis(sunRotation, Vector3.up) * initialSunRotation;
                    cameraData.SetCachedCSMDirty();
                }
                GUI.enabled = true;
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("1 / Courtyard")) SetView(CourtyardPosition, CourtyardRotation);
                if (GUILayout.Button("2 / Balcony")) SetView(BalconyPosition, BalconyRotation);
                GUILayout.EndHorizontal();
                if (GUILayout.Button("Reset demo")) ResetDemo();
                GUILayout.Space(8);
                GUILayout.Label("Hold RMB + WASD to fly; Q/E down/up.\nShift: faster. H: hide panel. Esc: release mouse.", bodyStyle);
            }
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        void ResetDemo()
        {
            EndFlight();
            SetGI(!initialGIOverride || initialGIIntensity > 0);
            SetGIOnly(false);
            SetProbeDisplay(false);
            sunRotation = 0;
            if (sun != null) sun.transform.rotation = initialSunRotation;
            ActivatePipeline(normalPipeline);
            ApplyPreset(0);
            SetView(initialCameraPosition, initialCameraRotation);
        }

        void OnDisable()
        {
            EndFlight();
            if (!initialized) return;
            initialized = false;
            if (debugSnapshotCaptured || probeSizeSnapshotCaptured || probeColorSnapshotCaptured)
            {
                // Switching pipelines invalidates the cached widgets. Resolve
                // the registered panel again even if no Update ran since the switch.
                var currentPanel = DebugManager.instance.GetPanel("APRT Volumes");
                if (currentPanel != null)
                {
                    var currentDisplay = FindField<DebugUI.EnumField>(currentPanel, "Display view");
                    var currentProbes = FindField<DebugUI.BoolField>(currentPanel, "Show Probes");
                    if (debugSnapshotCaptured)
                    {
                        if (currentDisplay != null) currentDisplay.SetValue(originalDisplayMode);
                        if (currentProbes != null) currentProbes.SetValue(originalProbeDisplay);
                    }
                    var currentSize = FindField<DebugUI.FloatField>(currentPanel, "Probe Size");
                    var currentColor = FindField<DebugUI.EnumField>(currentPanel, "Color Mode");
                    if (probeSizeSnapshotCaptured && currentSize != null) currentSize.SetValue(originalProbeSize);
                    if (probeColorSnapshotCaptured && currentColor != null) currentColor.SetValue(originalProbeColorMode);
                }
            }
            if (sun != null) sun.transform.rotation = initialSunRotation;
            if (demoCamera != null)
            {
                demoCamera.transform.SetPositionAndRotation(initialCameraPosition, initialCameraRotation);
                demoCamera.allowMSAA = originalAllowMSAA;
                demoCamera.allowDynamicResolution = originalDynamicResolution;
                demoCamera.depthTextureMode = originalDepthTextureMode;
            }
            if (cameraData != null)
            {
                cameraData.renderPostProcessing = originalPostProcessing;
                cameraData.antialiasing = originalAntialiasing;
                cameraData.resetHistory = true;
                if (addedCameraData) Destroy(cameraData);
            }
            if (volume != null) volume.profile = originalInstanceProfile;
            if (GraphicsSettings.renderPipelineAsset == activePipeline) GraphicsSettings.renderPipelineAsset = originalGraphicsPipeline;
            if (QualitySettings.renderPipeline == activePipeline) QualitySettings.renderPipeline = originalQualityPipeline;
            QualitySettings.antiAliasing = originalQualityAntialiasing;
            if (runtimeProfile != null)
            {
                foreach (var component in runtimeProfile.components) CoreUtils.Destroy(component);
                CoreUtils.Destroy(runtimeProfile);
            }
            CoreUtils.Destroy(normalPipeline);
            CoreUtils.Destroy(cachedPipeline);
            Cursor.lockState = originalCursorLock;
            Cursor.visible = originalCursorVisible;
            normalPipeline = null;
            cachedPipeline = null;
            activePipeline = null;
            runtimeProfile = null;
            volume = null;
            debugPanel = null;
            displayField = null;
            probesField = null;
            debugSnapshotCaptured = false;
            probeSizeSnapshotCaptured = false;
            probeColorSnapshotCaptured = false;
        }
    }
}
