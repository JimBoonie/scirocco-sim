using NUnit.Framework; using UnityEngine; using UnityEditor;
public class TrackTests
{
    // @description - Verifies that TrackManager.Find maps names to track indices: empty and unknown names to 0, 'laguna' and 'LagunaSeca' to 1.
    // @return {Void} - nothing
    [Test] public void Track_Selector_Names() { Assert.AreEqual(0, TrackManager.Find("")); Assert.AreEqual(1, TrackManager.Find("laguna")); Assert.AreEqual(1, TrackManager.Find("LagunaSeca")); Assert.AreEqual(0, TrackManager.Find("nope")); }
    // @description - Verifies the Laguna Seca centreline: lap length 3602 m +-15, spawn yaw equal to the race direction at start/finish, a corkscrew drop of 18 m +-1, and positive lateral offset for a point 3 m to the right of the race direction.
    // @return {Void} - nothing
    [Test] public void Laguna_Centreline_Length_Spawn_And_Corkscrew()
    {
        var trackObject = new GameObject("ti"); var trackInfo = trackObject.AddComponent<TrackInfo>();
        trackInfo.centrelineCsv = AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Tracks/LagunaSeca/centreline.csv"); Assert.NotNull(trackInfo.centrelineCsv);
        Assert.AreEqual(3602f, trackInfo.LapLength(), 15f, "lap length");
        var centreline = trackInfo.Centreline; var dir = (centreline[3] - centreline[0]); dir.y = 0f; float yaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg; if (yaw < 0) yaw += 360f;
        Assert.AreEqual(TrackSetup.SpawnYaw, yaw, 3f, "spawn yaw = race direction at S/F (+X East, +Z North)");
        float largestDrop = 0f; for (int i = 0; i < centreline.Count; i++) for (int k = 1; k <= 69; k++) largestDrop = Mathf.Max(largestDrop, centreline[i].y - centreline[(i + k) % centreline.Count].y);
        Assert.AreEqual(18f, largestDrop, 1f, "Corkscrew drop");
        trackInfo.Nearest(new Vector3(-0.813f * 3f, 0f, 0.583f * 3f), out float lat); Assert.Greater(lat, 2f, "point 3 m right of race direction at S/F has +lateral");
        Object.DestroyImmediate(trackObject);
    }
    // @description - Verifies the fall-reset threshold: a car is below the world only when it is more than 5 m under the lowest track point (-92.27 m).
    // @return {Void} - nothing
    [Test] public void FallSafety_Threshold()
    {
        Assert.IsFalse(TrackManager.BelowWorld(-92f, -92.27f)); Assert.IsFalse(TrackManager.BelowWorld(-97f, -92.27f));
        Assert.IsTrue(TrackManager.BelowWorld(-98f, -92.27f)); Assert.IsTrue(TrackManager.BelowWorld(-1000f, -92.27f));
    }
    // @description - Verifies that SurfaceTag maps Blender collider names (COL_Road, COL_Kerbs, COL_Gravel, Walls_Barriers, TerrainCollider and others) to the right surface type and unknown names to Unknown.
    // @return {Void} - nothing
    [Test] public void SurfaceTag_FromBlenderNames()
    {
        Assert.AreEqual(SurfaceType.Asphalt, SurfaceTag.FromName("COL_Road_03")); Assert.AreEqual(SurfaceType.Kerb, SurfaceTag.FromName("COL_Kerbs")); Assert.AreEqual(SurfaceType.Asphalt, SurfaceTag.FromName("COL_PitLane"));
        Assert.AreEqual(SurfaceType.RunoffPaved, SurfaceTag.FromName("COL_Runoff_Paved")); Assert.AreEqual(SurfaceType.Gravel, SurfaceTag.FromName("COL_Gravel"));
        Assert.AreEqual(SurfaceType.Grass, SurfaceTag.FromName("COL_Grass")); Assert.AreEqual(SurfaceType.Wall, SurfaceTag.FromName("Walls_Barriers_02"));
        Assert.AreEqual(SurfaceType.Terrain, SurfaceTag.FromName("TerrainCollider_DEM")); Assert.AreEqual(SurfaceType.Unknown, SurfaceTag.FromName("Bld_01"));
    }
    // @description - Verifies that the look-stick deadzone equals the Input System default minimum (below it 0, full deflection +-1, rescaled in between).
    // @return {Void} - nothing
    [Test] public void LookDeadzone_UsesInputSystemDefault()
    {
        float deadzone = UnityEngine.InputSystem.InputSystem.settings.defaultDeadzoneMin;
        Assert.AreEqual(0f, InputMapper.LookDeadzone(deadzone * 0.9f)); Assert.AreEqual(0f, InputMapper.LookDeadzone(-deadzone * 0.9f));
        Assert.AreEqual(1f, InputMapper.LookDeadzone(1f), 1e-5f); Assert.AreEqual(-1f, InputMapper.LookDeadzone(-1f), 1e-5f); Assert.Greater(InputMapper.LookDeadzone(0.75f), 0.6f);
    }
    // @description - Verifies that ambient lighting uses trilight mode, brightens with the brightness setting, adds a shadowless fill light, and that the saved profile default brightness is 1.3.
    // @return {Void} - nothing
    [Test] public void AmbientLighting_Trilight_Scales_And_Fill_Has_No_Shadows()
    {
        AmbientLighting.Apply(1.3f); Assert.AreEqual(UnityEngine.Rendering.AmbientMode.Trilight, RenderSettings.ambientMode);
        float baselineSkyRed = RenderSettings.ambientSkyColor.r; AmbientLighting.Apply(2f); Assert.Greater(RenderSettings.ambientSkyColor.r, baselineSkyRed);
        var fill = GameObject.Find("FillLight (ambient)"); Assert.IsNotNull(fill); Assert.AreEqual(LightShadows.None, fill.GetComponent<Light>().shadows);
        Assert.AreEqual(1.3f, new BindingProfile().ambientBrightness, 1e-6f, "saved profile default");
        Object.DestroyImmediate(fill);
    }
}
