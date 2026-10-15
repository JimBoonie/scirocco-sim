// Feeds the car from InputMapper bindings (all devices) and draws the in-game input settings menu (F1 / Esc / gamepad Start).
// Disabled automatically in -smoketest runs (SmokeTest drives the car directly).
using System.Collections.Generic; using UnityEngine; using UnityEngine.InputSystem; using UnityEngine.InputSystem.Controls;
public class InputRouter : MonoBehaviour
{
    public CarController car; public FollowCam cam; public InputMapper map; public bool menuOpen; public FfbController ffb; public VrManager vrManager; public VrPanel panel; public int navIndex;
    // capture / calibration state
    InAction? capturing; float captureT; Binding calibrating; float calMin, calMax; Vector2 scroll; string status = "";
    // @description - Creates the input mapper from the saved bindings. Disables the whole router in -smoketest runs because SmokeTest drives the car directly.
    // @return {Void} - nothing
    void Awake()
    {
        if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-smoketest") >= 0) { enabled = false; return; }
        map = new InputMapper(InputMapper.LoadOrDefault());
    }
    // @description - Releases the mapper's device-change subscription when the router is destroyed.
    // @return {Void} - nothing
    void OnDestroy() { map?.Dispose(); }
    // @description - Applies the "start with engine off" setting once the car exists, so a session can begin with the engine stopped.
    // @return {Void} - nothing
    void Start() { if (map != null && car && car.engine != null && map.profile.startEngineOff) car.engine.SetRunning(false); }
    // @description - Per-frame input pass. Handles Escape and the menu toggle, pauses time while the menu is open, and otherwise copies pedals, handbrake, steering, shifting, ignition, reset, camera and look input from the bound devices to the car. Also applies the saved audio, lighting and tire options each frame.
    // @return {Void} - nothing
    void Update()
    {
        if (map == null || !car || car.externalInput) return;
        map.MenuOpen = menuOpen;   // sync (GUI Close button / quick item / -shotmenu change the field directly); a change flushes pending presses
        map.SampleButtons();   // debounce sampled every frame in Update (real time), not per physics tick
        bool menuPress = map.Pressed(InAction.Menu);
        var keyboard = Keyboard.current; bool escapePressed = keyboard != null && keyboard.escapeKey.wasPressedThisFrame;   // hard-wired: works whatever the bindings are
        switch (EscapeAction(escapePressed, capturing != null, calibrating != null, menuOpen))
        {
            case EscResult.CancelCapture:
                var cancelledAction = capturing.Value; capturing = null; map.EndCapture();
                if (keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed) status = $"cleared {map.ClearDeviceBindings(cancelledAction)} device binding(s) of {cancelledAction}"; else status = "capture cancelled (Shift+Esc clears this action's device bindings)";
                return;
            case EscResult.CancelCalibration: calibrating = null; map.Flush(); status = "calibration cancelled"; return;
            case EscResult.CloseMenu: menuOpen = false; map.MenuOpen = false; InputMapper.Save(map.profile); status = "saved"; if (ffb) ffb.TryOpen(); Time.timeScale = 1f; return;
            case EscResult.OpenMenu: menuOpen = true; map.MenuOpen = true; return;
        }
        if (capturing == null && calibrating == null && menuPress) { menuOpen = !menuOpen; map.MenuOpen = menuOpen; if (!menuOpen) { InputMapper.Save(map.profile); status = "saved"; if (ffb) ffb.TryOpen(); } }
        Time.timeScale = menuOpen ? 0f : 1f;
        if (menuOpen)
        {
            car.engine.starter = false;   // gameplay suppressed: no cranking / shifting while the menu is open
            bool wasCapturing = capturing != null; UpdateCapture();
            if (!wasCapturing && capturing == null && calibrating == null) Navigate();   // the frame that finishes a capture navigates nothing
            return;
        }
        var carParams = car.carParams;
        car.throttle = map.Axis(InAction.Throttle); car.brake = map.Axis(InAction.Brake); car.clutch = map.Axis(InAction.Clutch);
        car.handbrakeInput = map.Axis(InAction.Handbrake);
        car.autoClutch = map.profile.autoClutch; carParams.tireThermal = map.profile.tireThermal; carParams.tireCompound = map.profile.tireCompound == 1 ? CarParams.TireCompound.ExperimentalViscoelastic : CarParams.TireCompound.Default; carParams.frontColdPressure = map.profile.frontColdPressurePsi * (float)TireThermal.PaPerPsi; carParams.rearColdPressure = map.profile.rearColdPressurePsi * (float)TireThermal.PaPerPsi; CarAudio.Volume = map.profile.audioVolume; CarAudio.TireVolume = map.profile.tireVolume; CarAudio.Mute = map.profile.audioMute; AmbientLighting.Apply(map.profile.ambientBrightness);
        if (map.Pressed(InAction.Ignition)) car.engine.ignition = !car.engine.ignition;
        bool interlockOk = !map.profile.starterInterlock || car.drivetrain.gear == 0 || car.ClutchFullyIn;
        car.engine.starter = map.Held(InAction.Starter) && interlockOk;
        hintNeutral = car.drivetrain.gear == 0 && car.throttle > 0.05f;
        if (map.assist == null) { map.assist = new GamepadSteering(map.profile.gamepadSteer); map.meanToHandwheel = meanRoadAngleDeg => SteeringLinkage.HandwheelForMean(car.carParams, meanRoadAngleDeg); }
        map.assistCtx = GamepadSteering.Context(car);
        car.handwheelDeg = map.StepSteer(Time.deltaTime, carParams.handwheelLockDeg, carParams.steeringRateDegPerSecond, SteeringLinkage.CentreRatio(carParams));
        car.handwheelInput = true;                                            // column -> pinion -> rack -> per-wheel kinematics (CarController)
        if (map.Pressed(InAction.ShiftUp)) car.drivetrain.Shift(1);
        if (map.Pressed(InAction.ShiftDown)) car.drivetrain.Shift(-1);
        // H-pattern: a held gear button selects that gear; a gear button on a shifter device (non-keyboard) returning to centre = neutral
        InAction[] gears = { InAction.GearR, InAction.Gear1, InAction.Gear2, InAction.Gear3, InAction.Gear4, InAction.Gear5 };
        bool anyShifterHeld = false, haveShifter = false;
        for (int i = 0; i < gears.Length; i++)
        {
            int gearNumber = i == 0 ? -1 : i; if (map.Pressed(gears[i])) car.drivetrain.SelectGear(gearNumber);
            if (map.HasNonKeyboard(gears[i])) { haveShifter = true; if (map.HeldFromNonKeyboard(gears[i])) anyShifterHeld = true; }
        }
        if (haveShifter && !anyShifterHeld && shifterWasHeld) car.drivetrain.SelectGear(0);
        shifterWasHeld = anyShifterHeld;
        if (map.Pressed(InAction.Reset)) { car.ResetCar(); car.drivetrain.SelectGear(0); car.engine.SetRunning(!map.profile.startEngineOff);   // reset = respawn: neutral, engine as at spawn
 if (ffb) ffb.ResetRamp(); }
        if (map.Pressed(InAction.ResetToStart)) ResetToStart();
        if (map.Pressed(InAction.Recenter) && vrManager) vrManager.Recenter();
        if (map.Pressed(InAction.CameraToggle) && vrManager) vrManager.ToggleCamera();
        if (map.Pressed(InAction.HudToggle)) { if (panel) panel.showHud = !panel.showHud; map.profile.hudVisible = !map.profile.hudVisible; InputMapper.Save(map.profile); status = map.profile.hudVisible ? "info HUD shown (H)" : "info HUD hidden (H)"; }
        float lookStickX = InputMapper.LookDeadzone(map.Axis(InAction.LookX)), lookStickY = InputMapper.LookDeadzone(map.Axis(InAction.LookY)); LookX = lookStickX;
        bool lookOn = Mathf.Abs(lookStickX) > 0.3f || Mathf.Abs(lookStickY) > 0.3f;
        if (lookOn && !lookWasOn) { LookSource = map.Source(Mathf.Abs(lookStickX) >= Mathf.Abs(lookStickY) ? InAction.LookX : InAction.LookY); Debug.Log($"[Input] look active: yaw {lookStickX * 120f:0} deg cockpit / {lookStickX * 180f:0} deg chase, from {LookSource}"); }
        if (!lookOn && lookWasOn) Debug.Log("[Input] look released"); lookWasOn = lookOn; if (!lookOn) LookSource = "";
        if (vrManager) vrManager.FlatLook(lookStickX * 120f, lookStickY * 45f);   // flat cockpit head look (deg)
        if (cam) { cam.lookYaw = lookStickX * 180f; cam.lookPitch = lookStickY * 30f; }   // camera orbit only (deg)
    }
    /// Reset to start: current track's start/finish spawn (yaw, zero velocity), neutral, engine as at spawn, FFB output cut and ramped back in
    bool lookWasOn; [HideInInspector] public float LookX; [HideInInspector] public string LookSource = "";
    // @description - Respawns the car at the current track's start/finish (heading, zero velocity), selects neutral, restores the engine state used at spawn and ramps the force feedback back in.
    // @return {Void} - nothing
    public void ResetToStart()
    {
        if (ffb) ffb.ResetRamp();
        if (TrackManager.Instance) TrackManager.Instance.Respawn(); else car.ResetCar();
        car.drivetrain.SelectGear(0); car.engine.SetRunning(!map.profile.startEngineOff); status = "reset to start";
    }
    public enum EscResult { None, CancelCapture, CancelCalibration, CloseMenu, OpenMenu }
    // @description - Decides what the Escape key does from the current state, with priority: cancel a running capture, cancel a calibration, close the menu, open the menu. Pure function so it can be unit tested.
    // @param {bool} escapePressed - true on the frame Escape was pressed
    // @param {bool} capturing - true while a new binding is being captured
    // @param {bool} calibrating - true while an axis calibration is running
    // @param {bool} menuOpen - true while the settings menu is open
    // @return {EscResult} - the action to take (None if Escape was not pressed)
    public static EscResult EscapeAction(bool escapePressed, bool capturing, bool calibrating, bool menuOpen) =>
        !escapePressed ? EscResult.None : capturing ? EscResult.CancelCapture : calibrating ? EscResult.CancelCalibration : menuOpen ? EscResult.CloseMenu : EscResult.OpenMenu;
    bool shifterWasHeld; public bool hintNeutral; Rect navRect; bool scrollToNav; float viewH;
    // @description - Advances a binding capture (waits 0.3 s so the click that started it is not captured, then polls for the moved control) or records the min and max raw values while an axis is being calibrated.
    // @return {Void} - nothing
    void UpdateCapture()
    {
        if (capturing != null)
        {
            captureT += Time.unscaledDeltaTime;
            if (captureT < 0.3f) return;
            var action = capturing.Value; var control = map.PollCapture(InputMapper.IsAxisAction(action));
            if (control != null)
            {
                var binding = map.CompleteCapture(action, control); capturing = null;   // finishes on the first press; that press is consumed (no tab flip / menu toggle / shift)
                status = $"bound {action} -> {binding.Label}" + (control is ButtonControl ? "" : "  (press Calibrate)");
            }
        }
        if (calibrating != null && calibrating.resolved != null)
        { float rawValue = InputMapper.Raw(calibrating.resolved); calMin = Mathf.Min(calMin, rawValue); calMax = Mathf.Max(calMax, rawValue); }
    }
    // @description - Ends an axis calibration: stores the captured min, max and rest value in the binding, or reports that the axis never moved and leaves the calibration unchanged.
    // @param {InAction} action - action whose binding was calibrated (decides whether the axis is centred or a pedal)
    // @return {Void} - nothing
    void FinishCalibration(InAction action)
    {
        var binding = calibrating; float rest = InputMapper.Raw(binding.resolved); calibrating = null;
        if (!binding.calibration.ApplyCapture(calMin, calMax, rest, InputMapper.IsBipolar(action))) { status = $"{action}: no travel seen ({calMin:0.000}..{calMax:0.000}) - calibration not changed"; return; }
        status = $"calibrated {action}: raw {binding.calibration.min:0.000}..{binding.calibration.max:0.000}  rest {(binding.calibration.hasRest ? binding.calibration.rest : binding.calibration.center):0.000}  invert {binding.calibration.invert}";
    }
    // ---------------- IMGUI settings menu ----------------
    GUIStyle labelStyle;
    // @description - Draws the settings menu with the IMGUI system, unless the VR panel is showing it instead.
    // @return {Void} - nothing
    void OnGUI() { if (VrPanel.Active) return; UiSurface.Begin(out var surfaceState); DrawMenu(); UiSurface.End(surfaceState); }
    public int tab; static readonly string[] Tabs = { "Quick", "Bindings", "Calibration", "FFB / Steering", "VR / Seat", "Gamepad Steering" };
    // @description - Draws the settings window: title, tab bar, status line, the selected tab page in a scroll view, and the Save, Reload, Reset defaults and Close buttons.
    // @return {Void} - nothing
    public void DrawMenu()
    {
        if (map == null || !menuOpen) return;
        GUI.depth = -10;   // draw above the HUD
        if (labelStyle == null) { labelStyle = new GUIStyle(GUI.skin.label) { fontSize = 13 }; labelStyle.normal.textColor = Color.white; }
        var menuRect = new Rect(20, 16, UiSurface.Width - 40, UiSurface.Height - 32); GUI.color = new Color(0.18f, 0.2f, 0.24f, 1f); GUI.DrawTexture(menuRect, Texture2D.whiteTexture); GUI.color = Color.white; GUI.Box(menuRect, GUIContent.none);
        GUILayout.BeginArea(new Rect(menuRect.x + 10, menuRect.y + 8, menuRect.width - 20, menuRect.height - 16));
        var profile = map.profile; var carParams = car.carParams;
        GUILayout.Label("SETTINGS  (F1/Esc/Start closes and saves; PgDn/Tab / PgUp or MenuTabNext/Prev = next/previous tab; gameplay buttons never switch tabs)   " + InputMapper.DefaultPath, labelStyle);
        int newTab = GUILayout.Toolbar(tab, Tabs, GUILayout.Height(28)); if (newTab != tab) { tab = newTab; scroll = Vector2.zero; }
        GUILayout.Label(status, labelStyle);
        viewH = menuRect.height - 16f - 120f;
        if (scrollToNav && Event.current.type == EventType.Layout && navRect.height > 0f) { if (navRect.yMin < scroll.y) scroll.y = navRect.yMin; else if (navRect.yMax > scroll.y + viewH) scroll.y = navRect.yMax - viewH; scrollToNav = false; }
        scroll = GUILayout.BeginScrollView(scroll, GUILayout.ExpandHeight(true));   // full-height page: mouse wheel, scrollbar drag, navigation auto-scrolls
        switch (tab)
        {
            case 0: DrawQuick(profile); break;
            case 1: DrawBindings(profile); break;
            case 2: DrawCalibration(profile); break;
            case 3: DrawFfb(profile, carParams); break;
            case 4: DrawVr(profile, carParams); break;
            case 5: DrawGamepad(profile); break;
        }
        GUILayout.EndScrollView();
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Save", GUILayout.Width(100))) { InputMapper.Save(profile); status = "saved"; }
        if (GUILayout.Button("Reload", GUILayout.Width(100))) { map.profile = InputMapper.LoadOrDefault(); map.MarkDirty(); status = "reloaded"; }
        if (GUILayout.Button("Reset defaults", GUILayout.Width(120))) { map.profile = InputMapper.Defaults(); map.MarkDirty(); status = "defaults (not saved yet)"; }
        if (GUILayout.Button("Close", GUILayout.Width(100))) { menuOpen = false; InputMapper.Save(profile); if (ffb) ffb.TryOpen(); }
        GUILayout.EndHorizontal();
        if (TrackManager.Instance && TrackManager.Instance.Credit.Length > 0) GUILayout.Label(TrackManager.Instance.Credit, labelStyle);
        GUILayout.EndArea();
    }
    // @description - Draws the Bindings tab: every action with its live value, a "+ Bind" button, and each bound control with its half-axis, steer source, negate and remove buttons.
    // @param {BindingProfile} profile - settings profile being edited
    // @return {Void} - nothing
    void DrawBindings(BindingProfile profile)
    {
        GUILayout.Label("Devices: " + string.Join(" | ", DeviceNames()), labelStyle);
        foreach (InAction action in System.Enum.GetValues(typeof(InAction)))
        {
            var slots = profile.Get(action); bool isBipolar = InputMapper.IsBipolar(action);
            GUILayout.BeginHorizontal();
            GUILayout.Label($"{action}  = {(InputMapper.IsAxisAction(action) ? map.Axis(action).ToString("0.00") : (map.Held(action) ? "ON" : "off"))}", labelStyle, GUILayout.Width(190));
            if (GUILayout.Button(capturing == action ? "move/press... (Esc / Shift+Esc clear)" : "+ Bind", GUILayout.Width(140))) { capturing = action; captureT = 0f; map.BeginCapture(); status = "move the axis fully / press the button"; }
            GUILayout.EndHorizontal();
            for (int i = 0; i < slots.Count; i++)
            {
                var binding = slots[i]; GUILayout.BeginHorizontal(); GUILayout.Space(30);
                GUILayout.Label($"{binding.Label}  raw {(binding.resolved != null ? InputMapper.Raw(binding.resolved).ToString("0.000") : "MISSING")}", labelStyle, GUILayout.Width(420));
                if (!(binding.resolved is ButtonControl) && !isBipolar && GUILayout.Button(binding.calibration.half.ToString(), GUILayout.Width(70))) binding.calibration.half = (AxisHalf)(((int)binding.calibration.half + 1) % 3);
                if (action == InAction.Steer && GUILayout.Button(binding.steerSource.ToString(), GUILayout.Width(60))) binding.steerSource = binding.steerSource == SteerSource.Wheel ? SteerSource.Stick : SteerSource.Wheel;
                if (action == InAction.Steer) binding.negative = GUILayout.Toggle(binding.negative, "neg", GUILayout.Width(50));
                if (GUILayout.Button("X", GUILayout.Width(25))) { slots.RemoveAt(i); i--; }
                GUILayout.EndHorizontal();
            }
        }
    }
    // @description - Draws the Calibration tab: raw versus mapped value for every analogue binding, invert and dead zone controls, and the Calibrate / Done buttons.
    // @param {BindingProfile} profile - settings profile being edited
    // @return {Void} - nothing
    void DrawCalibration(BindingProfile profile)
    {
        GUILayout.Label("Analog axes: raw device value vs mapped value. Pedals: Calibrate, press fully, release, Done (any raw range incl. -1..1 and inverted is auto-detected). Uncalibrated pedal axes read 0 at their rest position until calibrated. Load-cell brake: calibrate fully; the SmartControl curve passes through linearly.", labelStyle);
        DrawButtonDebounce(profile);
        diag = GUILayout.Toggle(diag, " Input diagnostics (every device/control feeding each action, raw values, HID report filter)");
        if (diag) DrawDiagnostics(profile);
        foreach (InAction action in System.Enum.GetValues(typeof(InAction)))
        {
            if (!InputMapper.IsAxisAction(action)) continue; bool isBipolar = InputMapper.IsBipolar(action);
            foreach (var binding in profile.Get(action))
            {
                if (binding.resolved is ButtonControl || binding.layout == "Keyboard") continue;
                GUILayout.BeginHorizontal();
                bool uncalibrated = binding.resolved != null && InputMapper.UncalibratedPedal(binding, isBipolar);
                float mappedValue = binding.resolved == null ? 0f : uncalibrated ? InputMapper.RestRelative(binding) : binding.calibration.Normalize(InputMapper.Raw(binding.resolved), isBipolar);
                GUILayout.Label($"{action}: {binding.Label}", labelStyle, GUILayout.Width(330));
                GUILayout.Label($"raw {(binding.resolved != null ? InputMapper.Raw(binding.resolved).ToString("+0.000;-0.000") : "MISSING")}  mapped {(isBipolar ? mappedValue.ToString("+0.00;-0.00") : mappedValue.ToString("0.00"))} ({mappedValue * 100f,4:0}%)  {(binding.calibration.calibrated ? (binding.calibration.hasRest ? $"cal {binding.calibration.rest:0.00}->{binding.calibration.PressedEnd:0.00}" : $"cal {binding.calibration.min:0.00}..{binding.calibration.max:0.00}") : uncalibrated ? "NOT calibrated (rest-relative)" : "default range")}", labelStyle, GUILayout.Width(400));
                binding.calibration.invert = GUILayout.Toggle(binding.calibration.invert, "inv", GUILayout.Width(45));   // with hasRest, toggling swaps which end counts as pressed
                GUILayout.Label($"dz {binding.calibration.deadzone:0.00}", labelStyle, GUILayout.Width(55)); binding.calibration.deadzone = GUILayout.HorizontalSlider(binding.calibration.deadzone, 0f, 0.3f, GUILayout.Width(70));
                if (calibrating == binding) { GUILayout.Label($"[{calMin:0.00} .. {calMax:0.00}]", labelStyle, GUILayout.Width(110)); if (GUILayout.Button(isBipolar ? "Done (centred)" : "Done (released)", GUILayout.Width(120))) FinishCalibration(action); }
                else if (binding.resolved != null && GUILayout.Button("Calibrate", GUILayout.Width(80))) { calibrating = binding; calMin = calMax = InputMapper.Raw(binding.resolved); status = isBipolar ? "turn fully both ways, centre, press Done" : "press fully, release, press Done"; }
                GUILayout.EndHorizontal();
            }
        }
    }
    bool diag;
    // @description - Draws the input diagnostics list: which device and control feeds each action, its raw value, and a warning when more than one non-keyboard device feeds the same action.
    // @param {BindingProfile} profile - settings profile being read
    // @return {Void} - nothing
    void DrawDiagnostics(BindingProfile profile)
    {
        GUILayout.Label(HidReportFilter.DeviceReport(), labelStyle);
        foreach (var actionSlots in profile.actions)
        {
            int liveCount = 0; foreach (var binding in actionSlots.slots) if (binding.resolved != null) liveCount++;
            if (liveCount == 0) continue;
            var seen = new System.Collections.Generic.HashSet<InputDevice>(); bool multiDev = false;
            foreach (var binding in actionSlots.slots) if (binding.resolved != null && !(binding.resolved.device is Keyboard)) { if (seen.Count > 0 && !seen.Contains(binding.resolved.device)) multiDev = true; seen.Add(binding.resolved.device); }
            GUILayout.Label($"{actionSlots.action}{(multiDev ? "   [fed by more than one non-keyboard device]" : "")}", labelStyle);
            foreach (var binding in actionSlots.slots)
            {
                if (binding.resolved == null) { GUILayout.Label($"     {binding.Label}  -> not connected", labelStyle); continue; }
                var device = binding.resolved.device; bool isBipolar = InputMapper.IsBipolar(actionSlots.action);
                string valueText = InputMapper.IsAxisAction(actionSlots.action) ? binding.calibration.Normalize(InputMapper.Raw(binding.resolved), isBipolar && !(binding.resolved is ButtonControl)).ToString("+0.000;-0.000") : (binding.debounceStableState ? "1" : "0");
                GUILayout.Label($"     dev#{device.deviceId} '{device.displayName}'  {binding.resolved.path}  raw {InputMapper.Raw(binding.resolved):+0.0000;-0.0000}  -> {valueText}", labelStyle);
            }
        }
    }
    // @description - Draws the optional button debounce slider (0-30 ms) and, for each non-keyboard button binding, its raw and debounced state and chatter count.
    // @param {BindingProfile} profile - settings profile being edited
    // @return {Void} - nothing
    void DrawButtonDebounce(BindingProfile profile)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label($"Optional button debounce {profile.debounceMs:0} ms (0 = off, default)", labelStyle, GUILayout.Width(420));
        profile.debounceMs = Mathf.Round(GUILayout.HorizontalSlider(profile.debounceMs, 0f, 30f, GUILayout.Width(160)));
        if (GUILayout.Button("Reset chatter", GUILayout.Width(110))) foreach (var actionSlots in profile.actions) foreach (var binding in actionSlots.slots) binding.chatterCount = 0;
        GUILayout.EndHorizontal();
        foreach (var actionSlots in profile.actions)
        {
            if (InputMapper.IsAxisAction(actionSlots.action)) continue;
            foreach (var binding in actionSlots.slots)
            {
                if (binding.resolved == null || binding.layout == "Keyboard") continue;
                GUILayout.Label($"   {actionSlots.action}: {binding.Label}   raw {(binding.debounceRawState ? "1" : "0")}  debounced {(binding.debounceStableState ? "1" : "0")}  chatter {binding.chatterCount}", labelStyle);
            }
        }
    }
    // @description - Draws the force feedback and steering page: wheel rotation lock-to-lock, column mapping, the resulting road-wheel angle at the wheel's limit, wheelbase torque, strength, output cap, invert, and the device picker.
    // @param {BindingProfile} profile - settings profile being edited
    // @param {CarParams} carParams - car parameters (steering lock and linkage geometry)
    // @return {Void} - nothing
    void DrawFfb(BindingProfile profile, CarParams carParams)
    {
        GUILayout.BeginHorizontal();
        float rotationDeg = profile.RotationDeg(carParams.handwheelLockDeg);
        GUILayout.Label($"Wheel rotation lock-to-lock (= True Drive setting): {rotationDeg:0} deg {(profile.wheelRotationDeg <= 0f ? "(car default)" : "")}", labelStyle, GUILayout.Width(420));
        float sliderRotationDeg = GUILayout.HorizontalSlider(rotationDeg, 180f, 2520f, GUILayout.Width(250)); if (sliderRotationDeg != rotationDeg) profile.wheelRotationDeg = Mathf.Round(sliderRotationDeg / 10f) * 10f;   // only when dragged (car value 1269 is not a multiple of 10)
        if (GUILayout.Button($"Car {2f * carParams.handwheelLockDeg:0}", GUILayout.Width(90))) profile.wheelRotationDeg = 0f;
        GUILayout.EndHorizontal();
        if (GUILayout.Toggle(profile.mapping == SteerMapping.RealColumn1to1, " 1:1 column (wheel angle = car steering-wheel angle, soft lock at your wheel's limit)")) profile.mapping = SteerMapping.RealColumn1to1;
        if (GUILayout.Toggle(profile.mapping == SteerMapping.ScaleToCarLock, " Scale to car lock")) profile.mapping = SteerMapping.ScaleToCarLock;
        float halfRotationDeg = profile.RotationDeg(carParams.handwheelLockDeg) * 0.5f, colAtLimit = profile.mapping == SteerMapping.RealColumn1to1 ? Mathf.Min(halfRotationDeg, carParams.handwheelLockDeg) : carParams.handwheelLockDeg;
        GUILayout.Label($"  -> at your wheel's limit (±{halfRotationDeg:0}°): car column ±{colAtLimit:0}° = mean road wheel ±{SteeringLinkage.MeanAngle(carParams, colAtLimit):0.0}°; effective on-centre ratio {halfRotationDeg / colAtLimit * SteeringLinkage.CentreRatio(carParams):0.00}:1", labelStyle);
        GUILayout.Space(8);
        profile.forceFeedbackEnabled = GUILayout.Toggle(profile.forceFeedbackEnabled, " Force feedback");
        GUILayout.BeginHorizontal(); GUILayout.Label("Wheelbase max torque N m (SC2 Sport 17 / Pro 25 / Ultimate 32):", labelStyle, GUILayout.Width(420));
        string torqueText = GUILayout.TextField(profile.wheelbaseMaxTorqueNm > 0f ? profile.wheelbaseMaxTorqueNm.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) : "", GUILayout.Width(60));
        if (float.TryParse(torqueText, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float torqueValue)) profile.wheelbaseMaxTorqueNm = Mathf.Max(0f, torqueValue); else if (torqueText == "") profile.wheelbaseMaxTorqueNm = 0f;
        GUILayout.EndHorizontal();
        GUILayout.BeginHorizontal(); GUILayout.Label($"Strength {profile.forceFeedbackStrengthPercent:0}%", labelStyle, GUILayout.Width(150)); profile.forceFeedbackStrengthPercent = Mathf.Round(GUILayout.HorizontalSlider(profile.forceFeedbackStrengthPercent, 0f, 100f, GUILayout.Width(200))); GUILayout.EndHorizontal();
        GUILayout.BeginHorizontal(); GUILayout.Label($"Max output cap {profile.forceFeedbackMaxOutputPercent:0}%", labelStyle, GUILayout.Width(150)); profile.forceFeedbackMaxOutputPercent = Mathf.Round(GUILayout.HorizontalSlider(profile.forceFeedbackMaxOutputPercent, 0f, 100f, GUILayout.Width(200))); GUILayout.EndHorizontal();
        profile.forceFeedbackInvert = GUILayout.Toggle(profile.forceFeedbackInvert, " Invert direction");
        GUILayout.BeginHorizontal(); GUILayout.Label("FFB device (name contains, empty = first):", labelStyle, GUILayout.Width(300)); profile.forceFeedbackDevice = GUILayout.TextField(profile.forceFeedbackDevice ?? "", GUILayout.Width(220)); GUILayout.EndHorizontal();
        if (ffb != null) { foreach (var deviceName in ffb.devices) if (GUILayout.Button(deviceName, GUILayout.Width(260))) profile.forceFeedbackDevice = deviceName; GUILayout.Label(ffb.status, labelStyle); }
    }
    // @description - Draws the VR and seat page: VR on/off, recentre, camera mode, seat position sliders (millimetre offsets within the car's seat travel), the VR debug HUD toggle and the flat-screen field of view.
    // @param {BindingProfile} profile - settings profile being edited
    // @param {CarParams} carParams - car parameters (seat travel limits)
    // @return {Void} - nothing
    void DrawVr(BindingProfile profile, CarParams carParams)
    {
        GUILayout.Label(vrManager ? vrManager.status : "no VR manager", labelStyle);
        bool vrOn = GUILayout.Toggle(profile.vrEnabled, " VR (OpenXR / SteamVR)"); if (vrOn != profile.vrEnabled) { profile.vrEnabled = vrOn; if (vrManager) vrManager.SetEnabled(vrOn); }
        if (GUILayout.Button("Recenter view", GUILayout.Width(160)) && vrManager) vrManager.Recenter();
        if (GUILayout.Button(vrManager && vrManager.mode == CamMode.Cockpit ? "Camera: cockpit (switch to chase)" : "Camera: chase (switch to cockpit)", GUILayout.Width(280)) && vrManager) vrManager.ToggleCamera();
        Cockpit cockpit = vrManager ? vrManager.cockpit : null;
        GUILayout.BeginHorizontal(); GUILayout.Label($"Seat fore/aft {profile.seatForwardOffset * 1000f:+0;-0;0} mm", labelStyle, GUILayout.Width(180)); float seatForward = GUILayout.HorizontalSlider(profile.seatForwardOffset, -carParams.seatTravelAft, carParams.seatTravelFore, GUILayout.Width(250)); GUILayout.EndHorizontal();
        GUILayout.BeginHorizontal(); GUILayout.Label($"Seat up/down {profile.seatUpOffset * 1000f:+0;-0;0} mm", labelStyle, GUILayout.Width(180)); float seatUp = GUILayout.HorizontalSlider(profile.seatUpOffset, -carParams.seatTravelDown, carParams.seatTravelUp, GUILayout.Width(250)); GUILayout.EndHorizontal();
        if (seatForward != profile.seatForwardOffset || seatUp != profile.seatUpOffset) { profile.seatForwardOffset = seatForward; profile.seatUpOffset = seatUp; if (cockpit) cockpit.ApplySeat(seatForward, seatUp); }
        if (panel) panel.showHud = GUILayout.Toggle(panel.showHud, " Debug HUD panel in VR");
        { var activeCamera = vrManager && vrManager.cam ? vrManager.cam : Camera.main; float aspect = activeCamera ? activeCamera.aspect : 16f / 9f;
          GUILayout.BeginHorizontal(); GUILayout.Label($"Flat-screen FOV {profile.flatFovDeg:0}° vertical = {VrManager.HorizontalFov(profile.flatFovDeg, aspect):0}° horizontal", labelStyle, GUILayout.Width(330));
          float newFov = Mathf.Round(GUILayout.HorizontalSlider(profile.flatFovDeg, BindingProfile.FovMin, BindingProfile.FovMax, GUILayout.Width(250))); GUILayout.EndHorizontal();
          if (newFov != profile.flatFovDeg) { profile.flatFovDeg = newFov; if (vrManager) vrManager.ApplyFlatFov(); } }
    }
    // ---------------- quick settings: navigable with Menu Up/Down/Left/Right/Select (keyboard arrows+Enter, gamepad d-pad+A, or bound wheel buttons) ----------------
    static readonly float[] TorquePresets = { 0f, 17f, 25f, 32f };   // Simucube 2 Sport / Pro / Ultimate peak torque (S, manufacturer); 0 = not set
    // @description - Builds the text of every Quick settings line (VR, camera, seat, force feedback, audio, tire options, FOV, track, reset, close) with current values. The order must match the case numbers in Adjust.
    // @param {BindingProfile} profile - settings profile to describe
    // @return {string[]} - one display line per quick item
    public string[] QuickItems(BindingProfile profile)
    {
        var carParams = car.carParams; Cockpit cockpit = vrManager ? vrManager.cockpit : null;
        return new[] {
            $"VR: {(VrManager.Active ? "ON" : "off")}   [{(vrManager ? vrManager.status : "no VR manager")}]",
            "Recenter VR view (also F12 / Home / right-stick click)",
            $"Camera (flat screen): {(vrManager && vrManager.mode == CamMode.Cockpit ? "cockpit" : "chase")}",
            $"Seat fore/aft: {profile.seatForwardOffset * 1000f:+0;-0;0} mm  (range -{carParams.seatTravelAft * 1000f:0}..+{carParams.seatTravelFore * 1000f:0})",
            $"Seat up/down: {profile.seatUpOffset * 1000f:+0;-0;0} mm  (range -{carParams.seatTravelDown * 1000f:0}..+{carParams.seatTravelUp * 1000f:0})",
            $"Force feedback: {(profile.forceFeedbackEnabled ? "ON" : "off")}   [{(ffb ? ffb.status : "")}]",
            $"Wheelbase max torque: {(profile.wheelbaseMaxTorqueNm > 0f ? profile.wheelbaseMaxTorqueNm.ToString("0.#") + " N m" : "not set")}",
            $"FFB strength: {profile.forceFeedbackStrengthPercent:0}%",
            $"FFB max output cap: {profile.forceFeedbackMaxOutputPercent:0}%",
            $"FFB invert: {(profile.forceFeedbackInvert ? "yes" : "no")}",
            $"Wheel rotation: {profile.RotationDeg(carParams.handwheelLockDeg):0} deg {(profile.wheelRotationDeg <= 0f ? "(car)" : "")}  (Select = car {2f * carParams.handwheelLockDeg:0})",
            $"Volume: {profile.audioVolume * 100f:0}%",
            $"Mute: {(profile.audioMute ? "yes" : "no")}",
            $"Auto clutch (keyboard / no clutch pedal): {(profile.autoClutch ? "on" : "off")}",
            $"Start with engine off: {(profile.startEngineOff ? "yes" : "no")}",
            $"Starter interlock (clutch in or neutral): {(profile.starterInterlock ? "on" : "off (period-correct)")}",
            $"Tire thermal model (temperature + pressure): {(profile.tireThermal ? "ON" : "off")}",
            $"Cold tire pressure front: {profile.frontColdPressurePsi:0.0} psi  (VW 25 psi)",
            $"Cold tire pressure rear: {profile.rearColdPressurePsi:0.0} psi  (VW 25 psi)",
            $"Tyre compound: {(profile.tireCompound == 1 ? "Experimental (viscoelastic)" : "Default")}  (Left/Right/Select)",
            $"VR debug HUD panel: {(panel && panel.showHud ? "shown" : "hidden")}  (H)",
            $"Flat-screen FOV: {profile.flatFovDeg:0}° vertical ({VrManager.HorizontalFov(profile.flatFovDeg, Camera.main ? Camera.main.aspect : 16f / 9f):0}° horizontal)",
            $"Track: {(TrackManager.Instance ? TrackManager.Names[TrackManager.Instance.current] + (TrackManager.Instance.Loading ? " (loading...)" : "") : "-")}   (Left/Right/Select = change, loads now)",
            "Reset car to start/finish (Select)   [Backspace / bindable 'ResetToStart']",
            "Close and save" };
    }
    // @description - Handles menu navigation from bound buttons: next and previous tab, and up/down/left/right/select on the Quick and Gamepad pages.
    // @return {Void} - nothing
    public void Navigate()
    {
        if (map.Pressed(InAction.MenuTabNext)) { tab = (tab + 1) % Tabs.Length; scroll = Vector2.zero; }
        if (map.Pressed(InAction.MenuTabPrev)) { tab = (tab + Tabs.Length - 1) % Tabs.Length; scroll = Vector2.zero; }
        if (tab == 5) { NavigateGamepad(map.profile); return; }
        if (tab != 0) return;
        var profile = map.profile; int itemCount = QuickItems(profile).Length;
        if (map.Pressed(InAction.MenuUp)) { navIndex = (navIndex + itemCount - 1) % itemCount; scrollToNav = true; }
        if (map.Pressed(InAction.MenuDown)) { navIndex = (navIndex + 1) % itemCount; scrollToNav = true; }
        int dir = (map.Pressed(InAction.MenuRight) ? 1 : 0) - (map.Pressed(InAction.MenuLeft) ? 1 : 0); bool sel = map.Pressed(InAction.MenuSelect);
        if (dir != 0 || sel) Adjust(profile, navIndex, sel ? 0 : dir, sel);
    }
    // @description - Applies one left, right or select action to a Quick settings item (toggle, step a value, cycle a preset or run a command). Step sizes: seat 10 mm, percentages 5 %, wheel rotation 10 degrees.
    // @param {BindingProfile} profile - settings profile to change
    // @param {int} item - index of the item, as listed by QuickItems
    // @param {int} direction - -1 for left, +1 for right, 0 for none
    // @param {bool} isSelect - true if Select was pressed (resets, toggles or runs the item)
    // @return {Void} - nothing
    public void Adjust(BindingProfile profile, int item, int direction, bool isSelect)
    {
        var carParams = car.carParams; Cockpit cockpit = vrManager ? vrManager.cockpit : null; const float seatStep = 0.01f, pctStep = 5f, rotStep = 10f;   // UI step sizes
        switch (item)
        {
            case 0: profile.vrEnabled = isSelect ? !profile.vrEnabled : direction > 0; if (vrManager) vrManager.SetEnabled(profile.vrEnabled); break;
            case 1: if (vrManager) vrManager.Recenter(); break;
            case 2: if (vrManager) vrManager.ToggleCamera(); break;
            case 3: profile.seatForwardOffset = Mathf.Clamp(profile.seatForwardOffset + direction * seatStep, -carParams.seatTravelAft, carParams.seatTravelFore); if (cockpit) cockpit.ApplySeat(profile.seatForwardOffset, profile.seatUpOffset); break;
            case 4: profile.seatUpOffset = Mathf.Clamp(profile.seatUpOffset + direction * seatStep, -carParams.seatTravelDown, carParams.seatTravelUp); if (cockpit) cockpit.ApplySeat(profile.seatForwardOffset, profile.seatUpOffset); break;
            case 5: profile.forceFeedbackEnabled = isSelect ? !profile.forceFeedbackEnabled : direction > 0; break;
            case 6: { int i = System.Array.IndexOf(TorquePresets, profile.wheelbaseMaxTorqueNm); if (i < 0) i = 0; i = Mathf.Clamp(i + (isSelect ? 1 : direction), 0, TorquePresets.Length - 1); profile.wheelbaseMaxTorqueNm = TorquePresets[i]; break; }
            case 7: profile.forceFeedbackStrengthPercent = Mathf.Clamp(profile.forceFeedbackStrengthPercent + direction * pctStep, 0f, 100f); break;
            case 8: profile.forceFeedbackMaxOutputPercent = Mathf.Clamp(profile.forceFeedbackMaxOutputPercent + direction * pctStep, 0f, 100f); break;
            case 9: profile.forceFeedbackInvert = isSelect ? !profile.forceFeedbackInvert : direction > 0; break;
            case 10: if (isSelect) profile.wheelRotationDeg = 0f; else profile.wheelRotationDeg = Mathf.Clamp(profile.RotationDeg(carParams.handwheelLockDeg) + direction * rotStep, 180f, 2520f); break;
            case 11: profile.audioVolume = Mathf.Clamp01(profile.audioVolume + direction * pctStep * 0.01f); break;
            case 12: profile.audioMute = isSelect ? !profile.audioMute : direction > 0; break;
            case 13: profile.autoClutch = isSelect ? !profile.autoClutch : direction > 0; break;
            case 14: profile.startEngineOff = isSelect ? !profile.startEngineOff : direction > 0; break;
            case 15: profile.starterInterlock = isSelect ? !profile.starterInterlock : direction > 0; break;
            case 16: profile.tireThermal = isSelect ? !profile.tireThermal : direction > 0; break;
            case 17: profile.frontColdPressurePsi = isSelect ? 24.7f : Mathf.Clamp(profile.frontColdPressurePsi + direction * 0.5f, 15f, 45f); break;   // 0.5 psi steps (gauge resolution); Select = VW 1.7 bar
            case 18: profile.rearColdPressurePsi = isSelect ? 24.7f : Mathf.Clamp(profile.rearColdPressurePsi + direction * 0.5f, 15f, 45f); break;
            case 19: profile.tireCompound = isSelect ? 1 - profile.tireCompound : (direction > 0 ? 1 : 0); break;
            case 20: if (panel) panel.showHud = isSelect ? !panel.showHud : direction > 0; break;
            case 21: profile.flatFovDeg = isSelect ? 60f : Mathf.Clamp(profile.flatFovDeg + direction * 5f, BindingProfile.FovMin, BindingProfile.FovMax); if (vrManager) vrManager.ApplyFlatFov(); break;
            case 22: { var trackManager = TrackManager.Instance; if (trackManager) { int trackCount = TrackManager.Names.Length; trackManager.Select((trackManager.current + (isSelect ? 1 : direction) + trackCount) % trackCount); } break; }
            case 23: if (isSelect) { ResetToStart(); menuOpen = false; } break;
            case 24: if (isSelect) { menuOpen = false; InputMapper.Save(profile); status = "saved"; if (ffb) ffb.TryOpen(); } break;
        }
    }
    // @description - Draws the Quick settings page: the highlighted item list plus the master volume, tire volume and ambient brightness sliders.
    // @param {BindingProfile} profile - settings profile being edited
    // @return {Void} - nothing
    void DrawQuick(BindingProfile profile)
    {
        var items = QuickItems(profile); var highlightStyle = new GUIStyle(labelStyle) { fontStyle = FontStyle.Bold }; highlightStyle.normal.textColor = new Color(1f, 0.85f, 0.3f);
        GUILayout.Label("QUICK SETTINGS  (Up/Down select, Left/Right change, Enter/A/bound wheel button = select)", labelStyle);
        for (int i = 0; i < items.Length; i++)
        {
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(i == navIndex ? ">" : " ", GUILayout.Width(24))) navIndex = i;
            GUILayout.Label(items[i], i == navIndex ? highlightStyle : labelStyle);
            GUILayout.EndHorizontal();
            if (i == navIndex && Event.current.type == EventType.Repaint) navRect = GUILayoutUtility.GetLastRect();
        }
        GUILayout.Space(6);
        GUILayout.BeginHorizontal(); GUILayout.Label($"Master {profile.audioVolume * 100f:0}%", labelStyle, GUILayout.Width(110)); profile.audioVolume = GUILayout.HorizontalSlider(profile.audioVolume, 0f, 1f, GUILayout.Width(250)); profile.audioMute = GUILayout.Toggle(profile.audioMute, " mute", GUILayout.Width(80)); GUILayout.EndHorizontal();
        GUILayout.BeginHorizontal(); GUILayout.Label($"Tires {profile.tireVolume * 100f:0}%", labelStyle, GUILayout.Width(110)); profile.tireVolume = GUILayout.HorizontalSlider(profile.tireVolume, 0f, 2f, GUILayout.Width(250)); GUILayout.Label("(tire scrub level relative to engine; 100% = default)", labelStyle); GUILayout.EndHorizontal();
        GUILayout.BeginHorizontal(); GUILayout.Label($"Ambient brightness {profile.ambientBrightness * 100f:0}%", labelStyle, GUILayout.Width(170)); profile.ambientBrightness = GUILayout.HorizontalSlider(profile.ambientBrightness, 0.5f, 2f, GUILayout.Width(190)); GUILayout.Label("(sky/bounce ambient + fill light; visual only, default 130%)", labelStyle); GUILayout.EndHorizontal();
    }
    // ---------------- Gamepad steering preferences (input assist; never applies to a wheel bound as SteerSource.Wheel) ----------------
    public struct GpSlider { public string label, fmt, help; public float min, max, step; public System.Func<GamepadSteerSettings, float> get; public System.Action<GamepadSteerSettings, float> set; }
    public static readonly GpSlider[] GpSliders = {
        new GpSlider { label = "Steering speed", fmt = "{0:0} deg/s road", help = "how fast the wheel turns towards the stick target (lower = calmer)", min = 5f, max = 120f, step = 5f, get = settings => settings.steerSpeed, set = (settings, value) => settings.steerSpeed = value },
        new GpSlider { label = "Response curve (gamma)", fmt = "{0:0.0}", help = "1 = linear; higher = less sensitive near centre", min = 1f, max = 4f, step = 0.1f, get = settings => settings.gamma, set = (settings, value) => settings.gamma = value },
        new GpSlider { label = "Deadzone", fmt = "{0:P0}", help = "stick travel ignored around centre (rest rescaled)", min = 0f, max = 0.3f, step = 0.01f, get = settings => settings.deadzone, set = (settings, value) => settings.deadzone = value },
        new GpSlider { label = "Smoothing", fmt = "{0:0.00} s", help = "filter time constant on the stick (0 = off)", min = 0f, max = 0.3f, step = 0.01f, get = settings => settings.smoothing, set = (settings, value) => settings.smoothing = value },
        new GpSlider { label = "Speed sensitivity", fmt = "{0:P0}", help = "0 = full lock at any speed; 100% = full stick limited to peak tire grip at speed", min = 0f, max = 1f, step = 0.05f, get = settings => settings.speedSensitivity, set = (settings, value) => settings.speedSensitivity = value },
        new GpSlider { label = "Grip limit", fmt = "{0:P0} of peak slip", help = "full stick at speed puts the front tires at this fraction of their peak-grip slip angle", min = 0.5f, max = 2f, step = 0.05f, get = settings => settings.gripLimit, set = (settings, value) => settings.gripLimit = value },
        new GpSlider { label = "Countersteer speed", fmt = "x{0:0.0}", help = "steering-speed factor when unwinding / catching a rear slide", min = 1f, max = 4f, step = 0.1f, get = settings => settings.countersteer, set = (settings, value) => settings.countersteer = value },
        new GpSlider { label = "Self-centre (caster follow)", fmt = "{0:P0}", help = "released stick: wheels follow the front-axle travel direction (0 = straight ahead)", min = 0f, max = 1f, step = 0.05f, get = settings => settings.casterFollow, set = (settings, value) => settings.casterFollow = value },
    };
    public static int GpItemCount => GpSliders.Length + 2;   // [0] assist on/off, sliders, [last] reset all
    // @description - Applies a left, right or select action to a gamepad steering menu item: item 0 toggles the assist, the last item resets everything, the others step one slider (rounded to its step and clamped to its range, select resets it to the default).
    // @param {GamepadSteerSettings} settings - gamepad steering settings to change
    // @param {int} item - menu item index (0 = assist on/off, 1..N = sliders, last = reset all)
    // @param {int} direction - -1 for left, +1 for right
    // @param {bool} isSelect - true if Select was pressed
    // @return {Void} - nothing
    public static void GpAdjust(GamepadSteerSettings settings, int item, int direction, bool isSelect)
    {
        var defaults = GamepadSteerSettings.Default;
        if (item == 0) { settings.enabled = isSelect ? !settings.enabled : direction > 0 ? true : direction < 0 ? false : settings.enabled; return; }
        if (item == GpItemCount - 1) { if (isSelect) JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(defaults), settings); return; }
        var slider = GpSliders[item - 1];
        if (isSelect) { slider.set(settings, slider.get(defaults)); return; }   // Select / A = reset this slider to default
        float value = Mathf.Clamp(Mathf.Round((slider.get(settings) + direction * slider.step) / slider.step) * slider.step, slider.min, slider.max); slider.set(settings, value);
    }
    // @description - Handles menu navigation on the Gamepad Steering tab: up and down move the selection, left, right and select change the highlighted item.
    // @param {BindingProfile} profile - settings profile whose gamepad settings are edited
    // @return {Void} - nothing
    void NavigateGamepad(BindingProfile profile)
    {
        int itemCount = GpItemCount;
        if (map.Pressed(InAction.MenuUp)) { navIndex = (navIndex + itemCount - 1) % itemCount; scrollToNav = true; }
        if (map.Pressed(InAction.MenuDown)) { navIndex = (navIndex + 1) % itemCount; scrollToNav = true; }
        navIndex = Mathf.Clamp(navIndex, 0, itemCount - 1);
        int direction = (map.Pressed(InAction.MenuRight) ? 1 : 0) - (map.Pressed(InAction.MenuLeft) ? 1 : 0); bool isSelect = map.Pressed(InAction.MenuSelect);
        if (direction != 0 || isSelect) GpAdjust(profile.gamepadSteer, navIndex, isSelect ? 0 : direction, isSelect);
    }
    // @description - Draws the Gamepad Steering page: assist toggle, one row per slider with value, reset and help text, a reset-all button, and a live line with the assist's target, steering angle and tire slip data.
    // @param {BindingProfile} profile - settings profile being edited
    // @return {Void} - nothing
    void DrawGamepad(BindingProfile profile)
    {
        var settings = profile.gamepadSteer; var defaults = GamepadSteerSettings.Default; var highlightStyle = new GUIStyle(labelStyle) { fontStyle = FontStyle.Bold }; highlightStyle.normal.textColor = new Color(1f, 0.85f, 0.3f);
        GUILayout.Label("GAMEPAD / KEYBOARD STEERING  (input preferences, not physics; a wheel bound as 'Wheel' is never affected)", labelStyle);
        GUILayout.Label("Up/Down select, Left/Right change, Select/A = reset that item to default", labelStyle);
        void Row(int itemIndex, System.Action body)
        {
            if (navIndex >= GpItemCount && tab == 5) navIndex = 0;
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(itemIndex == navIndex ? ">" : " ", GUILayout.Width(24))) navIndex = itemIndex;
            body(); GUILayout.EndHorizontal();
            if (itemIndex == navIndex && Event.current.type == EventType.Repaint) navRect = GUILayoutUtility.GetLastRect();
        }
        Row(0, () => { settings.enabled = GUILayout.Toggle(settings.enabled, settings.enabled ? " Steering assist ON (stick sets a target, wheel follows)" : " Steering assist OFF (direct: stick = lock fraction)", navIndex == 0 ? highlightStyle : labelStyle); });
        for (int k = 0; k < GpSliders.Length; k++)
        {
            var slider = GpSliders[k]; int itemIndex = k + 1;
            Row(itemIndex, () =>
            {
                GUILayout.Label(slider.label, itemIndex == navIndex ? highlightStyle : labelStyle, GUILayout.Width(200));
                GUILayout.Label(string.Format(System.Globalization.CultureInfo.InvariantCulture, slider.fmt, slider.get(settings)), labelStyle, GUILayout.Width(130));
                float newValue = GUILayout.HorizontalSlider(slider.get(settings), slider.min, slider.max, GUILayout.Width(200)); if (newValue != slider.get(settings)) slider.set(settings, Mathf.Round(newValue / slider.step) * slider.step);
                if (GUILayout.Button("Reset", GUILayout.Width(60))) slider.set(settings, slider.get(defaults));
                GUILayout.Label($"(default {string.Format(System.Globalization.CultureInfo.InvariantCulture, slider.fmt, slider.get(defaults))}; {slider.help})", labelStyle);
            });
        }
        Row(GpItemCount - 1, () => { if (GUILayout.Button("Reset all gamepad steering settings to defaults", GUILayout.Width(360))) GpAdjust(settings, GpItemCount - 1, 0, true); GUILayout.Label(navIndex == GpItemCount - 1 ? "<- Select/A" : "", highlightStyle); });
        var assist = map.assist;
        if (assist != null && map.assistCtx.HasValue) { var context = map.assistCtx.Value; GUILayout.Label($"live: target {assist.lastTarget:0.0} deg road, steering {assist.roadDeg:0.0} deg, front tire peak slip {context.peakSlipDeg:0.0} deg, front travel {context.frontTravelDeg:0.0} deg, rear travel {context.rearTravelDeg:0.0} deg{(assist.countersteering ? "  [countersteer rate]" : "")}", labelStyle); }
    }
    // @description - Lists the connected input devices (excluding mouse, pointer and sensors) with their category, for the Bindings tab header.
    // @return {IEnumerable<string>} - one text entry per device, "name [category]"
    static IEnumerable<string> DeviceNames() { foreach (var device in InputSystem.devices) if (!InputMapper.Skip(device)) yield return $"{(string.IsNullOrEmpty(device.description.product) ? device.layout : device.description.product)} [{InputMapper.Category(device)}]"; }
}
