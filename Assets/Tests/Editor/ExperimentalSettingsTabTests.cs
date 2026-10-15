// Experimental settings tab: the options moved here keep their defaults and behavior, round-trip through the settings file, and work with the VR cursor menu.
using NUnit.Framework; using UnityEngine; using System; using System.Reflection;
public class ExperimentalSettingsTabTests
{
    static readonly MethodInfo BeginGui = typeof(GUIUtility).GetMethod("BeginGUI", BindingFlags.NonPublic | BindingFlags.Static);
    static readonly MethodInfo EndGui = typeof(GUIUtility).GetMethod("EndGUI", BindingFlags.NonPublic | BindingFlags.Static);
    static readonly MethodInfo BeginContainer = typeof(GUIUtility).GetMethod("BeginContainer", BindingFlags.NonPublic | BindingFlags.Static);
    static readonly MethodInfo EndContainer = typeof(GUIUtility).GetMethod("Internal_EndContainer", BindingFlags.NonPublic | BindingFlags.Static);
    static readonly object State = Activator.CreateInstance(typeof(GUIUtility).Assembly.GetType("UnityEngine.ObjectGUIState"), true);
    const int ControlId = 515151;

    // @description - Verifies the defaults of the moved options are the previous behavior: thermal model off, 24.7 psi cold pressures, Default compound, and the new driveline placeholder off (in the profile and in CarParams).
    // @return {Void} - nothing
    [Test] public void DefaultsReproducePreviousBehavior()
    {
        var profile = new BindingProfile();
        Assert.IsFalse(profile.tireThermal); Assert.AreEqual(24.7f, profile.frontColdPressurePsi); Assert.AreEqual(24.7f, profile.rearColdPressurePsi);
        Assert.AreEqual(0, profile.tireCompound); Assert.IsFalse(profile.experimentalDrivelineEnabled); Assert.IsFalse(profile.experimentalTransmissionEnabled);
        var carParams = ScriptableObject.CreateInstance<CarParams>();
        Assert.IsFalse(carParams.tireThermal); Assert.AreEqual(CarParams.TireCompound.Default, carParams.tireCompound); Assert.IsFalse(carParams.experimentalDrivelineEnabled); Assert.IsFalse(carParams.experimentalTransmissionEnabled);
        Assert.AreEqual(ExperimentalSettingsTab.VolkswagenColdPressurePsi, profile.frontColdPressurePsi);
    }
    // @description - Verifies that every option changes through Adjust as the old Quick menu did: toggle on/off and flip, pressure 0.5 psi steps clamped to 15..45 with select = 24.7, compound select and left/right.
    // @return {Void} - nothing
    [Test] public void AdjustMatchesOldQuickMenuBehavior()
    {
        var profile = new BindingProfile();
        ExperimentalSettingsTab.Adjust(profile, 0, 1, false); Assert.IsTrue(profile.tireThermal);
        ExperimentalSettingsTab.Adjust(profile, 0, -1, false); Assert.IsFalse(profile.tireThermal);
        ExperimentalSettingsTab.Adjust(profile, 0, 0, true); Assert.IsTrue(profile.tireThermal); ExperimentalSettingsTab.Adjust(profile, 0, 0, true); Assert.IsFalse(profile.tireThermal);
        ExperimentalSettingsTab.Adjust(profile, 1, 1, false); Assert.AreEqual(25.2f, profile.frontColdPressurePsi, 1e-4f);
        ExperimentalSettingsTab.Adjust(profile, 2, -1, false); Assert.AreEqual(24.2f, profile.rearColdPressurePsi, 1e-4f);
        for (int stepIndex = 0; stepIndex < 100; stepIndex++) ExperimentalSettingsTab.Adjust(profile, 1, 1, false);
        Assert.AreEqual(45f, profile.frontColdPressurePsi);
        for (int stepIndex = 0; stepIndex < 100; stepIndex++) ExperimentalSettingsTab.Adjust(profile, 2, -1, false);
        Assert.AreEqual(15f, profile.rearColdPressurePsi);
        ExperimentalSettingsTab.Adjust(profile, 1, 0, true); ExperimentalSettingsTab.Adjust(profile, 2, 0, true); Assert.AreEqual(24.7f, profile.frontColdPressurePsi); Assert.AreEqual(24.7f, profile.rearColdPressurePsi);
        ExperimentalSettingsTab.Adjust(profile, 3, 1, false); Assert.AreEqual(1, profile.tireCompound); ExperimentalSettingsTab.Adjust(profile, 3, -1, false); Assert.AreEqual(0, profile.tireCompound);
        ExperimentalSettingsTab.Adjust(profile, 3, 0, true); Assert.AreEqual(1, profile.tireCompound); ExperimentalSettingsTab.Adjust(profile, 3, 0, true); Assert.AreEqual(0, profile.tireCompound);
        ExperimentalSettingsTab.Adjust(profile, ExperimentalSettingsTab.ItemCount, 1, true); Assert.IsFalse(profile.experimentalDrivelineEnabled, "the driveline placeholder cannot be changed from the menu");
    }
    // @description - Verifies keyboard/gamepad navigation: down and up wrap around the four options, and a change applies to the highlighted option.
    // @return {Void} - nothing
    [Test] public void NavigationWrapsAndAppliesToHighlightedOption()
    {
        var tab = new ExperimentalSettingsTab(); var profile = new BindingProfile();
        Assert.IsTrue(tab.Navigate(profile, true, false, 0, false)); Assert.AreEqual(ExperimentalSettingsTab.ItemCount - 1, tab.selectedItem);
        Assert.IsTrue(tab.Navigate(profile, false, true, 0, false)); Assert.AreEqual(0, tab.selectedItem);
        Assert.IsFalse(tab.Navigate(profile, false, false, 1, false)); Assert.IsTrue(profile.tireThermal);
    }
    // @description - Verifies the moved and new fields survive a JSON round trip, and that a settings file written before this tab (no driveline field) loads with the placeholder off and keeps its version and bindings.
    // @return {Void} - nothing
    [Test] public void SettingsRoundTripKeepsBindings()
    {
        var profile = InputMapper.Defaults(); profile.tireThermal = true; profile.frontColdPressurePsi = 27f; profile.rearColdPressurePsi = 31.5f; profile.tireCompound = 1; profile.experimentalDrivelineEnabled = true;
        var loaded = JsonUtility.FromJson<BindingProfile>(JsonUtility.ToJson(profile));
        Assert.IsTrue(loaded.tireThermal); Assert.AreEqual(27f, loaded.frontColdPressurePsi); Assert.AreEqual(31.5f, loaded.rearColdPressurePsi); Assert.AreEqual(1, loaded.tireCompound); Assert.IsTrue(loaded.experimentalDrivelineEnabled);
        var oldJson = System.Text.RegularExpressions.Regex.Replace(JsonUtility.ToJson(InputMapper.Defaults()), "\"experimentalDrivelineEnabled\":(true|false),?", "");
        StringAssert.DoesNotContain("experimentalDrivelineEnabled", oldJson);
        var oldLoaded = JsonUtility.FromJson<BindingProfile>(oldJson);
        Assert.AreEqual(BindingProfile.CurrentVersion, oldLoaded.version); Assert.IsFalse(oldLoaded.experimentalDrivelineEnabled);
        Assert.AreEqual(InputMapper.Defaults().actions.Count, oldLoaded.actions.Count, "bindings are not reset");
    }
    // @description - Verifies the driveline and transmission placeholders are independent settings: each round-trips through JSON without affecting the other, an old file without either field loads both as false, and the menu cannot change either.
    // @return {Void} - nothing
    [Test] public void DrivelineAndTransmissionPlaceholdersAreIndependent()
    {
        var drivelineOnly = new BindingProfile { experimentalDrivelineEnabled = true };
        var loadedDriveline = JsonUtility.FromJson<BindingProfile>(JsonUtility.ToJson(drivelineOnly));
        Assert.IsTrue(loadedDriveline.experimentalDrivelineEnabled); Assert.IsFalse(loadedDriveline.experimentalTransmissionEnabled);
        var transmissionOnly = new BindingProfile { experimentalTransmissionEnabled = true };
        var loadedTransmission = JsonUtility.FromJson<BindingProfile>(JsonUtility.ToJson(transmissionOnly));
        Assert.IsTrue(loadedTransmission.experimentalTransmissionEnabled); Assert.IsFalse(loadedTransmission.experimentalDrivelineEnabled);
        var oldJson = System.Text.RegularExpressions.Regex.Replace(JsonUtility.ToJson(InputMapper.Defaults()), "\"experimental(Driveline|Transmission)Enabled\":(true|false),?", "");
        var oldLoaded = JsonUtility.FromJson<BindingProfile>(oldJson);
        Assert.IsFalse(oldLoaded.experimentalDrivelineEnabled); Assert.IsFalse(oldLoaded.experimentalTransmissionEnabled); Assert.AreEqual(BindingProfile.CurrentVersion, oldLoaded.version);
        for (int item = 0; item <= ExperimentalSettingsTab.ItemCount + 1; item++) { ExperimentalSettingsTab.Adjust(drivelineOnly, item, 1, true); ExperimentalSettingsTab.Adjust(transmissionOnly, item, 1, true); }
        Assert.IsFalse(transmissionOnly.experimentalDrivelineEnabled, "menu actions never touch the driveline placeholder"); Assert.IsFalse(drivelineOnly.experimentalTransmissionEnabled, "menu actions never touch the transmission placeholder");
    }
    // @description - Runs one IMGUI pass headlessly as an IMGUIContainer would, through the VR mouse dispatch.
    // @param {EventType} eventType - IMGUI event type of this pass
    // @param {VrPointerQueue} queue - queued VR pointer events
    // @param {Vector2} cursor - VR cursor position in panel logical pixels
    // @param {Action} draw - the IMGUI drawing code under test
    // @return {Void} - nothing
    static void Pass(EventType eventType, VrPointerQueue queue, Vector2 cursor, Action draw)
    {
        Event.current = new Event { type = eventType, mousePosition = new Vector2(-999f, -999f) };
        BeginContainer.Invoke(null, new[] { State }); BeginGui.Invoke(null, new object[] { 0, ControlId, 1 });
        try
        {
            GUI.matrix = Matrix4x4.Scale(new Vector3(1.5f, 1.5f, 1f));
            if (VrMouse.Dispatch(queue, cursor, draw)) { }
            else if (VrMouse.IsPointerEvent(Event.current.type)) { }
            else VrMouse.WithMouse(cursor, draw);
        }
        finally { GUI.matrix = Matrix4x4.identity; EndGui.Invoke(null, new object[] { 1 }); EndContainer.Invoke(null, null); }
    }
    // @description - Runs a Layout pass then a Repaint pass (one IMGUI frame).
    // @param {VrPointerQueue} queue - queued VR pointer events
    // @param {Vector2} cursor - VR cursor position in panel logical pixels
    // @param {Action} draw - the IMGUI drawing code under test
    // @return {Void} - nothing
    static void Frame(VrPointerQueue queue, Vector2 cursor, Action draw) { Pass(EventType.Layout, queue, cursor, draw); Pass(EventType.Repaint, queue, cursor, draw); }
    // @description - Clicks (press and release) at a panel point through the VR pointer queue.
    // @param {VrPointerQueue} queue - queued VR pointer events
    // @param {Vector2} point - click position in panel logical pixels
    // @param {Action} draw - the IMGUI drawing code under test
    // @return {Void} - nothing
    static void Click(VrPointerQueue queue, Vector2 point, Action draw)
    {
        queue.Sample(true, point, 0f); Frame(queue, point, draw); queue.Sample(false, point, 0f); Frame(queue, point, draw);
    }
    // @description - Verifies the tab works with the VR cursor: sweeping VR clicks down the left column flips the thermal toggle and the compound selector, and a VR drag across the sliders changes a cold pressure; the disabled driveline toggle never changes.
    // @return {Void} - nothing
    [Test] public void VrCursorClicksAndSlidersWork()
    {
        var tab = new ExperimentalSettingsTab(); var profile = new BindingProfile(); var queue = new VrPointerQueue(); GUIStyle style = null;   // created inside the first OnGUI pass (GUI.skin is only available there)
        // @description - Draws the tab in the same kind of area the settings menu uses.
        // @return {Void} - nothing
        void Draw() { style = style ?? new GUIStyle(GUI.skin.label); GUILayout.BeginArea(new Rect(0, 0, 900, 700)); tab.Draw(profile, style); GUILayout.EndArea(); }
        Frame(queue, Vector2.zero, Draw);
        for (float y = 4f; y < 400f; y += 4f) { Click(queue, new Vector2(60f, y), Draw); if (profile.tireThermal) break; }
        Assert.IsTrue(profile.tireThermal, "a VR click on the thermal toggle must switch it on");
        for (float y = 4f; y < 600f && profile.tireCompound == 0; y += 4f) Click(queue, new Vector2(480f, y), Draw);
        Assert.AreEqual(1, profile.tireCompound, "a VR click on the compound selector must choose Experimental");
        for (float y = 4f; y < 600f && profile.frontColdPressurePsi == 24.7f && profile.rearColdPressurePsi == 24.7f; y += 4f)
        {
            var dragStart = new Vector2(330f, y); var dragEnd = new Vector2(530f, y);
            queue.Sample(true, dragStart, 0f); Frame(queue, dragStart, Draw); queue.Sample(true, dragEnd, 0f); Frame(queue, dragEnd, Draw); queue.Sample(false, dragEnd, 0f); Frame(queue, dragEnd, Draw);
        }
        Assert.IsTrue(profile.frontColdPressurePsi != 24.7f || profile.rearColdPressurePsi != 24.7f, "a VR drag on a pressure slider must change it");
        Assert.IsFalse(profile.experimentalDrivelineEnabled, "the disabled placeholder ignores clicks"); Assert.IsFalse(profile.experimentalTransmissionEnabled, "the disabled transmission placeholder ignores clicks");
    }
}
