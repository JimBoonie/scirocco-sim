#if UNITY_EDITOR
// Configures XR Plugin Management + OpenXR for Windows standalone (Valve Index via SteamVR's OpenXR runtime). Idempotent; run before builds.
using UnityEditor; using UnityEngine; using UnityEditor.XR.Management; using UnityEditor.XR.Management.Metadata; using UnityEngine.XR.Management;
using UnityEngine.XR.OpenXR; using UnityEngine.XR.OpenXR.Features.Interactions;
public static class XrSetup
{
    [MenuItem("Scirocco/Configure XR (OpenXR)")]
    public static void Configure() => Configure(true);
    // Standalone XR settings are shared by Windows and Linux; OpenXR only supports Windows x64 standalone, so Linux builds unassign the loader.
    public static void Configure(bool openxr)
    {
        const string dir = "Assets/XR";
        if (!AssetDatabase.IsValidFolder(dir)) AssetDatabase.CreateFolder("Assets", "XR");
        if (!EditorBuildSettings.TryGetConfigObject(XRGeneralSettings.k_SettingsKey, out XRGeneralSettingsPerBuildTarget per) || per == null)
        {
            per = ScriptableObject.CreateInstance<XRGeneralSettingsPerBuildTarget>(); AssetDatabase.CreateAsset(per, dir + "/XRGeneralSettingsPerBuildTarget.asset");
            EditorBuildSettings.AddConfigObject(XRGeneralSettings.k_SettingsKey, per, true);
        }
        var grp = BuildTargetGroup.Standalone;
        if (!per.HasSettingsForBuildTarget(grp)) per.CreateDefaultSettingsForBuildTarget(grp);
        if (!per.HasManagerSettingsForBuildTarget(grp)) per.CreateDefaultManagerSettingsForBuildTarget(grp);
        var gs = per.SettingsForBuildTarget(grp);
        gs.InitManagerOnStart = false;    // VrManager starts XR on demand (-vr / settings) and falls back to flat
        bool ok = openxr ? XRPackageMetadataStore.AssignLoader(gs.AssignedSettings, "UnityEngine.XR.OpenXR.OpenXRLoader", grp)
                     : XRPackageMetadataStore.RemoveLoader(gs.AssignedSettings, "UnityEngine.XR.OpenXR.OpenXRLoader", grp);
        EditorUtility.SetDirty(gs); EditorUtility.SetDirty(gs.AssignedSettings); EditorUtility.SetDirty(per);
        var ox = OpenXRSettings.GetSettingsForBuildTargetGroup(grp);
        if (ox != null)
        {
            ox.renderMode = OpenXRSettings.RenderMode.SinglePassInstanced;   // perf: one pass for both eyes
            ox.depthSubmissionMode = OpenXRSettings.DepthSubmissionMode.Depth16Bit;   // lets SteamVR reprojection use depth
            foreach (var f in ox.GetFeatures())
                if (f is ValveIndexControllerProfile || f is KHRSimpleControllerProfile) f.enabled = true;   // Index controllers optional (menu also works without them)
            EditorUtility.SetDirty(ox);
        }
        // shaders created from code at runtime must be in the build
        var gfx = AssetDatabase.LoadAssetAtPath<Object>("ProjectSettings/GraphicsSettings.asset"); var so = new SerializedObject(gfx); var arr = so.FindProperty("m_AlwaysIncludedShaders");
        foreach (var sn in new[] { "Unlit/Texture" })
        {
            var sh = Shader.Find(sn); bool has = false; for (int i = 0; i < arr.arraySize; i++) if (arr.GetArrayElementAtIndex(i).objectReferenceValue == sh) has = true;
            if (sh && !has) { arr.InsertArrayElementAtIndex(arr.arraySize); arr.GetArrayElementAtIndex(arr.arraySize - 1).objectReferenceValue = sh; }
        }
        // 'Standard' is NOT force-included (all variants = very slow builds); scene primitives already pull in the variant used by the cockpit
        var std = Shader.Find("Standard"); for (int i = arr.arraySize - 1; i >= 0; i--) if (std && arr.GetArrayElementAtIndex(i).objectReferenceValue == std) { arr.GetArrayElementAtIndex(i).objectReferenceValue = null; arr.DeleteArrayElementAtIndex(i); }
        so.ApplyModifiedProperties();
        AssetDatabase.SaveAssets();
        Debug.Log($"XR SETUP: OpenXR loader assigned={ok} loaders={gs.AssignedSettings.activeLoaders.Count} initOnStart={gs.InitManagerOnStart} render={(ox != null ? ox.renderMode.ToString() : "n/a")}");
    }
}
#endif
