using NUnit.Framework; using UnityEngine; using UnityEditor;
public class TrackTests
{
    // @description - Track selector names.
    // @return {Void} - nothing
    [Test] public void Track_Selector_Names() { Assert.AreEqual(0, TrackManager.Find("")); Assert.AreEqual(1, TrackManager.Find("laguna")); Assert.AreEqual(1, TrackManager.Find("LagunaSeca")); Assert.AreEqual(0, TrackManager.Find("nope")); }
    [Test] public void Laguna_Centreline_Length_Spawn_And_Corkscrew()
    {
        var go = new GameObject("ti"); var ti = go.AddComponent<TrackInfo>();
        ti.centrelineCsv = AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Tracks/LagunaSeca/centreline.csv"); Assert.NotNull(ti.centrelineCsv);
        Assert.AreEqual(3602f, ti.LapLength(), 15f, "lap length");
        var c = ti.Centreline; var dir = (c[3] - c[0]); dir.y = 0f; float yaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg; if (yaw < 0) yaw += 360f;
        Assert.AreEqual(TrackSetup.SpawnYaw, yaw, 3f, "spawn yaw = race direction at S/F (+X East, +Z North)");
        float best = 0f; for (int i = 0; i < c.Count; i++) for (int k = 1; k <= 69; k++) best = Mathf.Max(best, c[i].y - c[(i + k) % c.Count].y);
        Assert.AreEqual(18f, best, 1f, "Corkscrew drop");
        ti.Nearest(new Vector3(-0.813f * 3f, 0f, 0.583f * 3f), out float lat); Assert.Greater(lat, 2f, "point 3 m right of race direction at S/F has +lateral");
        Object.DestroyImmediate(go);
    }
    // @description - Fall safety threshold.
    // @return {Void} - nothing
    [Test] public void FallSafety_Threshold()
    {
        Assert.IsFalse(TrackManager.BelowWorld(-92f, -92.27f)); Assert.IsFalse(TrackManager.BelowWorld(-97f, -92.27f));
        Assert.IsTrue(TrackManager.BelowWorld(-98f, -92.27f)); Assert.IsTrue(TrackManager.BelowWorld(-1000f, -92.27f));
    }
    // @description - Surface tag from blender names.
    // @return {Void} - nothing
    [Test] public void SurfaceTag_FromBlenderNames()
    {
        Assert.AreEqual(SurfaceType.Asphalt, SurfaceTag.FromName("COL_Road_03")); Assert.AreEqual(SurfaceType.Kerb, SurfaceTag.FromName("COL_Kerbs")); Assert.AreEqual(SurfaceType.Asphalt, SurfaceTag.FromName("COL_PitLane"));
        Assert.AreEqual(SurfaceType.RunoffPaved, SurfaceTag.FromName("COL_Runoff_Paved")); Assert.AreEqual(SurfaceType.Gravel, SurfaceTag.FromName("COL_Gravel"));
        Assert.AreEqual(SurfaceType.Grass, SurfaceTag.FromName("COL_Grass")); Assert.AreEqual(SurfaceType.Wall, SurfaceTag.FromName("Walls_Barriers_02"));
        Assert.AreEqual(SurfaceType.Terrain, SurfaceTag.FromName("TerrainCollider_DEM")); Assert.AreEqual(SurfaceType.Unknown, SurfaceTag.FromName("Bld_01"));
    }
    // @description - Look deadzone uses input system default.
    // @return {Void} - nothing
    [Test] public void LookDeadzone_UsesInputSystemDefault()
    {
        float dz = UnityEngine.InputSystem.InputSystem.settings.defaultDeadzoneMin;
        Assert.AreEqual(0f, InputMapper.LookDeadzone(dz * 0.9f)); Assert.AreEqual(0f, InputMapper.LookDeadzone(-dz * 0.9f));
        Assert.AreEqual(1f, InputMapper.LookDeadzone(1f), 1e-5f); Assert.AreEqual(-1f, InputMapper.LookDeadzone(-1f), 1e-5f); Assert.Greater(InputMapper.LookDeadzone(0.75f), 0.6f);
    }
    // @description - Ambient lighting trilight scales and fill has no shadows.
    // @return {Void} - nothing
    [Test] public void AmbientLighting_Trilight_Scales_And_Fill_Has_No_Shadows()
    {
        AmbientLighting.Apply(1.3f); Assert.AreEqual(UnityEngine.Rendering.AmbientMode.Trilight, RenderSettings.ambientMode);
        float a = RenderSettings.ambientSkyColor.r; AmbientLighting.Apply(2f); Assert.Greater(RenderSettings.ambientSkyColor.r, a);
        var fill = GameObject.Find("FillLight (ambient)"); Assert.IsNotNull(fill); Assert.AreEqual(LightShadows.None, fill.GetComponent<Light>().shadows);
        Assert.AreEqual(1.3f, new BindingProfile().ambientBrightness, 1e-6f, "saved profile default");
        Object.DestroyImmediate(fill);
    }
}
