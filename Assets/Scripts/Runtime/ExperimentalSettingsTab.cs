// The "Experimental" tab of the settings menu: options that may change how the car handles. Each option lives only here (they were on the Quick tab before).
// Settings are stored in BindingProfile (tireThermal, frontColdPressurePsi, rearColdPressurePsi, tireCompound, experimentalDrivelineEnabled, experimentalTransmissionEnabled) and applied to CarParams by InputRouter.
// Drawing uses IMGUI, so the VR cursor menu (clicks and slider drags) works unchanged.
using UnityEngine;
public class ExperimentalSettingsTab
{
    public const int TabIndex = 6;   // position of "Experimental" in the settings tab bar
    public const int ItemCount = 6;   // navigable options: 0 tire temperature model, 1 front cold pressure, 2 rear cold pressure, 3 tire compound, 4 driveline shuffle model, 5 transmission and synchronizer model
    public const float VolkswagenColdPressurePsi = 24.7f;   // psi, VW 1.7 bar cold tire pressure (S): the default and the Select-reset value
    public const float MinimumColdPressurePsi = 15f, MaximumColdPressurePsi = 45f, ColdPressureStepPsi = 0.5f;   // psi, slider range; 0.5 psi = gauge resolution
    public const string Warning = "Experimental options are work in progress and may change how the car handles. The defaults reproduce the standard handling exactly.";
    public int selectedItem;   // highlighted option for keyboard / gamepad navigation
    public Rect selectedRowRect;   // layout rectangle of the highlighted row (lets the menu scroll to it)
    GUIStyle labelStyle, highlightStyle;

    // @description - Applies one left, right or select action to an option. Item 0 toggles the tire temperature model (select flips, right = on, left = off); items 1 and 2 step a cold pressure by 0.5 psi within 15..45 psi (select = VW 24.7 psi); item 3 chooses the tire compound (select flips); item 4 toggles the experimental driveline shuffle model (select flips, right = on, left = off). Other items are ignored (the driveline placeholder is disabled).
    // @param {BindingProfile} profile - settings profile to change
    // @param {int} item - option index, 0..ItemCount-1
    // @param {int} direction - -1 for left, +1 for right, 0 for none
    // @param {bool} isSelect - true if Select was pressed
    // @return {Void} - nothing
    public static void Adjust(BindingProfile profile, int item, int direction, bool isSelect)
    {
        switch (item)
        {
            case 0: profile.tireThermal = isSelect ? !profile.tireThermal : direction > 0; break;
            case 1: profile.frontColdPressurePsi = isSelect ? VolkswagenColdPressurePsi : StepColdPressure(profile.frontColdPressurePsi, direction); break;
            case 2: profile.rearColdPressurePsi = isSelect ? VolkswagenColdPressurePsi : StepColdPressure(profile.rearColdPressurePsi, direction); break;
            case 3: profile.tireCompound = isSelect ? 1 - profile.tireCompound : (direction > 0 ? 1 : 0); break;
            case 4: profile.experimentalDrivelineEnabled = isSelect ? !profile.experimentalDrivelineEnabled : direction > 0; break;
            case 5: profile.experimentalTransmissionEnabled = isSelect ? !profile.experimentalTransmissionEnabled : direction > 0; break;
        }
    }
    // @description - Steps a cold tire pressure by one gauge step and clamps it to the allowed range.
    // @param {float} pressurePsi - current cold pressure, psi
    // @param {int} direction - -1 to lower, +1 to raise, 0 to keep
    // @return {float} - new cold pressure, psi
    static float StepColdPressure(float pressurePsi, int direction) => Mathf.Clamp(pressurePsi + direction * ColdPressureStepPsi, MinimumColdPressurePsi, MaximumColdPressurePsi);
    // @description - Handles one frame of menu navigation: up and down move the highlight (wrapping), left, right and select change the highlighted option.
    // @param {BindingProfile} profile - settings profile being edited
    // @param {bool} up - Menu Up pressed this frame
    // @param {bool} down - Menu Down pressed this frame
    // @param {int} direction - -1 for Menu Left, +1 for Menu Right, 0 for neither
    // @param {bool} isSelect - Menu Select pressed this frame
    // @return {bool} - true if the highlight moved (the menu should scroll to it)
    public bool Navigate(BindingProfile profile, bool up, bool down, int direction, bool isSelect)
    {
        bool moved = up || down;
        if (up) selectedItem = (selectedItem + ItemCount - 1) % ItemCount;
        if (down) selectedItem = (selectedItem + 1) % ItemCount;
        selectedItem = Mathf.Clamp(selectedItem, 0, ItemCount - 1);
        if (direction != 0 || isSelect) Adjust(profile, selectedItem, isSelect ? 0 : direction, isSelect);
        return moved;
    }
    // @description - Draws the whole tab: warning note, tire temperature model toggle, front and rear cold pressure sliders, tire compound selector and the driveline shuffle model toggle; each option followed by a plain-language description.
    // @param {BindingProfile} profile - settings profile being edited
    // @param {GUIStyle} baseLabelStyle - the menu's normal label style
    // @return {Void} - nothing
    public void Draw(BindingProfile profile, GUIStyle baseLabelStyle)
    {
        PrepareStyles(baseLabelStyle);
        selectedItem = Mathf.Clamp(selectedItem, 0, ItemCount - 1);
        GUILayout.Label("EXPERIMENTAL  (Up/Down select, Left/Right change, Select = toggle / reset)", labelStyle);
        GUILayout.Label(Warning, labelStyle);
        GUILayout.Space(6);
        DrawThermalToggle(profile);
        DrawColdPressureRow(1, "Cold tire pressure front", profile.frontColdPressurePsi, value => profile.frontColdPressurePsi = value, "Starting pressure of the front tires before they warm up. Only used when the temperature model is on. VW spec 24.7 psi.");
        DrawColdPressureRow(2, "Cold tire pressure rear", profile.rearColdPressurePsi, value => profile.rearColdPressurePsi = value, "Starting pressure of the rear tires before they warm up. Only used when the temperature model is on. VW spec 24.7 psi.");
        DrawCompoundSelector(profile);
        DrawDrivelineToggle(profile);
        DrawTransmissionToggle(profile);
    }
    // @description - Creates the normal and highlighted (bold yellow) label styles from the menu's label style, once.
    // @param {GUIStyle} baseLabelStyle - the menu's normal label style
    // @return {Void} - nothing
    void PrepareStyles(GUIStyle baseLabelStyle)
    {
        if (labelStyle == baseLabelStyle && highlightStyle != null) return;
        labelStyle = baseLabelStyle;
        highlightStyle = new GUIStyle(baseLabelStyle) { fontStyle = FontStyle.Bold }; highlightStyle.normal.textColor = new Color(1f, 0.85f, 0.3f);
    }
    // @description - Chooses the label style for an option row: highlighted when it is the selected option.
    // @param {int} itemIndex - option index
    // @return {GUIStyle} - highlight or normal label style
    GUIStyle StyleFor(int itemIndex) => itemIndex == selectedItem ? highlightStyle : labelStyle;
    // @description - Draws one option row: a selection marker button, the option's controls, and a short description under it. Remembers the selected row's rectangle for scrolling.
    // @param {int} itemIndex - option index
    // @param {System.Action} drawControls - draws the option's controls on one horizontal line
    // @param {string} description - short plain-language explanation shown under the option
    // @return {Void} - nothing
    void DrawRow(int itemIndex, System.Action drawControls, string description)
    {
        GUILayout.BeginHorizontal();
        if (GUILayout.Button(itemIndex == selectedItem ? ">" : " ", GUILayout.Width(24))) selectedItem = itemIndex;
        drawControls(); GUILayout.EndHorizontal();
        if (itemIndex == selectedItem && Event.current.type == EventType.Repaint) selectedRowRect = GUILayoutUtility.GetLastRect();
        GUILayout.BeginHorizontal(); GUILayout.Space(30); GUILayout.Label(description, labelStyle); GUILayout.EndHorizontal();
        GUILayout.Space(6);
    }
    // @description - Draws the tire temperature (thermal) model toggle with its description.
    // @param {BindingProfile} profile - settings profile being edited
    // @return {Void} - nothing
    void DrawThermalToggle(BindingProfile profile)
    {
        DrawRow(0, () => { profile.tireThermal = GUILayout.Toggle(profile.tireThermal, profile.tireThermal ? " Tire temperature model ON" : " Tire temperature model off", StyleFor(0)); },
            "Simulates tire heating and inflation pressure rise while driving, so grip depends on temperature and pressure. Off = the standard constant-grip tires.");
    }
    // @description - Draws one cold tire pressure row: label with value, a slider (15..45 psi, 0.5 psi steps) and a reset-to-VW button.
    // @param {int} itemIndex - option index
    // @param {string} label - row label
    // @param {float} pressurePsi - current cold pressure, psi
    // @param {System.Action<float>} storePressurePsi - stores a new cold pressure, psi
    // @param {string} description - short explanation shown under the option
    // @return {Void} - nothing
    void DrawColdPressureRow(int itemIndex, string label, float pressurePsi, System.Action<float> storePressurePsi, string description)
    {
        DrawRow(itemIndex, () =>
        {
            GUILayout.Label($"{label}: {pressurePsi:0.0} psi", StyleFor(itemIndex), GUILayout.Width(280));
            float sliderPressure = GUILayout.HorizontalSlider(pressurePsi, MinimumColdPressurePsi, MaximumColdPressurePsi, GUILayout.Width(220));
            if (sliderPressure != pressurePsi) storePressurePsi(Mathf.Round(sliderPressure / ColdPressureStepPsi) * ColdPressureStepPsi);
            if (GUILayout.Button("VW 24.7", GUILayout.Width(70))) storePressurePsi(VolkswagenColdPressurePsi);
        }, description);
    }
    // @description - Draws the tire compound selector (Default / Experimental viscoelastic) with its description.
    // @param {BindingProfile} profile - settings profile being edited
    // @return {Void} - nothing
    void DrawCompoundSelector(BindingProfile profile)
    {
        DrawRow(3, () =>
        {
            GUILayout.Label("Tire compound:", StyleFor(3), GUILayout.Width(120));
            profile.tireCompound = GUILayout.Toolbar(profile.tireCompound, new[] { "Default", "Experimental (viscoelastic)" }, GUILayout.Width(380));
        }, "Default = the sourced Pacejka tire model. Experimental = a viscoelastic rubber model (hysteresis, adhesion, temperature and rate effects) that changes grip and feel.");
    }
    // @description - Draws the experimental driveline shuffle model toggle with its description.
    // @param {BindingProfile} profile - settings profile being edited
    // @return {Void} - nothing
    void DrawDrivelineToggle(BindingProfile profile)
    {
        DrawRow(4, () => { profile.experimentalDrivelineEnabled = GUILayout.Toggle(profile.experimentalDrivelineEnabled, profile.experimentalDrivelineEnabled ? " Driveline shuffle model ON" : " Driveline shuffle model off", StyleFor(4)); },
            "Models clutch bite, judder, driveline wind-up and lash clonk (lurch when you get on or off the throttle) with a torsional chain and engine firing pulses. Estimated parameters: you must test-drive it. Off = the standard driveline exactly.");
    }
    // @description - Draws the experimental transmission and synchronizer model toggle with its description. Independent of the driveline toggle: any of the four combinations works.
    // @param {BindingProfile} profile - settings profile being edited
    // @return {Void} - nothing
    void DrawTransmissionToggle(BindingProfile profile)
    {
        DrawRow(5, () => { profile.experimentalTransmissionEnabled = GUILayout.Toggle(profile.experimentalTransmissionEnabled, profile.experimentalTransmissionEnabled ? " Transmission and synchronizer model ON" : " Transmission and synchronizer model off", StyleFor(5)); },
            "Models the gearbox: synchronizer cones and blocking rings, grinding and crunching when you shift without matching speeds, gears that only leave under light torque, oil drag, mesh losses and neutral rattle. Estimated parameters: you must test-drive it. The keyboard auto-clutch then opens longer for each shift. Separate from the driveline model above (any combination works). Off = the standard gearbox exactly.");
    }
}
