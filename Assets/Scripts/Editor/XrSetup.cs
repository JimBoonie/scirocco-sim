#if UNITY_EDITOR
// Configures XR Plugin Management + OpenXR for Windows standalone (Valve Index via SteamVR's OpenXR runtime). Idempotent; run before builds.
using UnityEditor; using UnityEngine; using UnityEditor.XR.Management; using UnityEditor.XR.Management.Metadata; using UnityEngine.XR.Management;
using UnityEngine.XR.OpenXR; using UnityEngine.XR.OpenXR.Features.Interactions;
public static class XrSetup
{
    [MenuItem("Scirocco/Configure XR (OpenXR)")]
    // @description - Menu entry "Scirocco > Configure XR (OpenXR)": configures XR for a Windows standalone build with the OpenXR loader.
    // @return {Void} - nothing
    public static void Configure() => Configure(true);
    // @description - Configures XR Plugin Management and OpenXR for standalone builds (idempotent, run before builds): creates the settings assets, makes XR start on demand only (VrManager starts it), assigns or removes the OpenXR loader, sets single-pass instanced rendering and 16-bit depth submission, enables the Valve Index and simple controller profiles and makes sure shaders created from code are included in the build. Standalone XR settings are shared by Windows and Linux, and OpenXR only supports Windows x64, so Linux builds pass false to unassign the loader.
    // @param {bool} openxr - true to assign the OpenXR loader (Windows build), false to remove it (Linux build)
    // @return {Void} - nothing
    public static void Configure(bool openxr)
    {
        const string dir = "Assets/XR";
        if (!AssetDatabase.IsValidFolder(dir)) AssetDatabase.CreateFolder("Assets", "XR");
        if (!EditorBuildSettings.TryGetConfigObject(XRGeneralSettings.k_SettingsKey, out XRGeneralSettingsPerBuildTarget perBuildTargetSettings) || perBuildTargetSettings == null)
        {
            perBuildTargetSettings = ScriptableObject.CreateInstance<XRGeneralSettingsPerBuildTarget>(); AssetDatabase.CreateAsset(perBuildTargetSettings, dir + "/XRGeneralSettingsPerBuildTarget.asset");
            EditorBuildSettings.AddConfigObject(XRGeneralSettings.k_SettingsKey, perBuildTargetSettings, true);
        }
        var buildTargetGroup = BuildTargetGroup.Standalone;
        if (!perBuildTargetSettings.HasSettingsForBuildTarget(buildTargetGroup)) perBuildTargetSettings.CreateDefaultSettingsForBuildTarget(buildTargetGroup);
        if (!perBuildTargetSettings.HasManagerSettingsForBuildTarget(buildTargetGroup)) perBuildTargetSettings.CreateDefaultManagerSettingsForBuildTarget(buildTargetGroup);
        var generalSettings = perBuildTargetSettings.SettingsForBuildTarget(buildTargetGroup);
        generalSettings.InitManagerOnStart = false;    // VrManager starts XR on demand (-vr / settings) and falls back to flat
        bool loaderUpdated = openxr ? XRPackageMetadataStore.AssignLoader(generalSettings.AssignedSettings, "UnityEngine.XR.OpenXR.OpenXRLoader", buildTargetGroup)
                     : XRPackageMetadataStore.RemoveLoader(generalSettings.AssignedSettings, "UnityEngine.XR.OpenXR.OpenXRLoader", buildTargetGroup);
        EditorUtility.SetDirty(generalSettings); EditorUtility.SetDirty(generalSettings.AssignedSettings); EditorUtility.SetDirty(perBuildTargetSettings);
        var openXrSettings = OpenXRSettings.GetSettingsForBuildTargetGroup(buildTargetGroup);
        if (openXrSettings != null)
        {
            openXrSettings.renderMode = OpenXRSettings.RenderMode.SinglePassInstanced;   // perf: one pass for both eyes
            openXrSettings.depthSubmissionMode = OpenXRSettings.DepthSubmissionMode.Depth16Bit;   // lets SteamVR reprojection use depth
            foreach (var feature in openXrSettings.GetFeatures())
                if (feature is ValveIndexControllerProfile || feature is KHRSimpleControllerProfile) feature.enabled = true;   // Index controllers optional (menu also works without them)
            EditorUtility.SetDirty(openXrSettings);
        }
        // shaders created from code at runtime must be in the build
        var graphicsSettingsAsset = AssetDatabase.LoadAssetAtPath<Object>("ProjectSettings/GraphicsSettings.asset"); var serializedSettings = new SerializedObject(graphicsSettingsAsset); var alwaysIncludedShaders = serializedSettings.FindProperty("m_AlwaysIncludedShaders");
        foreach (var shaderName in new[] { "Unlit/Texture" })
        {
            var shader = Shader.Find(shaderName); bool alreadyIncluded = false; for (int i = 0; i < alwaysIncludedShaders.arraySize; i++) if (alwaysIncludedShaders.GetArrayElementAtIndex(i).objectReferenceValue == shader) alreadyIncluded = true;
            if (shader && !alreadyIncluded) { alwaysIncludedShaders.InsertArrayElementAtIndex(alwaysIncludedShaders.arraySize); alwaysIncludedShaders.GetArrayElementAtIndex(alwaysIncludedShaders.arraySize - 1).objectReferenceValue = shader; }
        }
        // 'Standard' is NOT force-included (all variants = very slow builds); scene primitives already pull in the variant used by the cockpit
        var standardShader = Shader.Find("Standard"); for (int i = alwaysIncludedShaders.arraySize - 1; i >= 0; i--) if (standardShader && alwaysIncludedShaders.GetArrayElementAtIndex(i).objectReferenceValue == standardShader) { alwaysIncludedShaders.GetArrayElementAtIndex(i).objectReferenceValue = null; alwaysIncludedShaders.DeleteArrayElementAtIndex(i); }
        serializedSettings.ApplyModifiedProperties();
        AssetDatabase.SaveAssets();
        Debug.Log($"XR SETUP: OpenXR loader assigned={loaderUpdated} loaders={generalSettings.AssignedSettings.activeLoaders.Count} initOnStart={generalSettings.InitManagerOnStart} render={(openXrSettings != null ? openXrSettings.renderMode.ToString() : "n/a")}");
    }
}
#endif
