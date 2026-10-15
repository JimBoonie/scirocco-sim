// Feeds the car from InputMapper bindings (all devices) and draws the in-game input settings menu (F1 / Esc / gamepad Start).
// Disabled automatically in -smoketest runs (SmokeTest drives the car directly).
using System.Collections.Generic; using UnityEngine; using UnityEngine.InputSystem; using UnityEngine.InputSystem.Controls;
public class InputRouter : MonoBehaviour
{
    public CarController car; public FollowCam cam; public InputMapper map; public bool menuOpen; public FfbController ffb; public VrManager vr; public VrPanel panel; public int navIndex;
    // capture / calibration state
    InAction? capturing; float captureT; Binding calibrating; float calMin, calMax; Vector2 scroll; string status = "";
    // @description - Awake.
    // @return {Void} - nothing
    void Awake()
    {
        if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-smoketest") >= 0) { enabled = false; return; }
        map = new InputMapper(InputMapper.LoadOrDefault());
    }
    // @description - Handles the destroy.
    // @return {Void} - nothing
    void OnDestroy() { map?.Dispose(); }
    // @description - Start (see the body for details).
    // @return {Void} - nothing
    void Start() { if (map != null && car && car.engine != null && map.profile.startEngineOff) car.engine.SetRunning(false); }
    // @description - Update (see the body for details).
    // @return {Void} - nothing
    void Update()
    {
        if (map == null || !car || car.externalInput) return;
        map.MenuOpen = menuOpen;   // sync (GUI Close button / quick item / -shotmenu change the field directly); a change flushes pending presses
        map.SampleButtons();   // debounce sampled every frame in Update (real time), not per physics tick
        bool menuPress = map.Pressed(InAction.Menu);
        var kb = Keyboard.current; bool esc = kb != null && kb.escapeKey.wasPressedThisFrame;   // hard-wired: works whatever the bindings are
        switch (EscapeAction(esc, capturing != null, calibrating != null, menuOpen))
        {
            case EscResult.CancelCapture:
                var ca = capturing.Value; capturing = null; map.EndCapture();
                if (kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed) status = $"cleared {map.ClearDeviceBindings(ca)} device binding(s) of {ca}"; else status = "capture cancelled (Shift+Esc clears this action's device bindings)";
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
        var p = car.carParams;
        car.throttle = map.Axis(InAction.Throttle); car.brake = map.Axis(InAction.Brake); car.clutch = map.Axis(InAction.Clutch);
        car.handbrakeInput = map.Axis(InAction.Handbrake);
        car.autoClutch = map.profile.autoClutch; p.tireThermal = map.profile.tireThermal; p.tireCompound = map.profile.tireCompound == 1 ? CarParams.TireCompound.ExperimentalViscoelastic : CarParams.TireCompound.Default; p.frontColdPressure = map.profile.frontColdPressurePsi * (float)TireThermal.PaPerPsi; p.rearColdPressure = map.profile.rearColdPressurePsi * (float)TireThermal.PaPerPsi; CarAudio.Volume = map.profile.audioVolume; CarAudio.TireVolume = map.profile.tireVolume; CarAudio.Mute = map.profile.audioMute; AmbientLighting.Apply(map.profile.ambientBrightness);
        if (map.Pressed(InAction.Ignition)) car.engine.ignition = !car.engine.ignition;
        bool interlockOk = !map.profile.starterInterlock || car.drivetrain.gear == 0 || car.ClutchFullyIn;
        car.engine.starter = map.Held(InAction.Starter) && interlockOk;
        hintNeutral = car.drivetrain.gear == 0 && car.throttle > 0.05f;
        if (map.assist == null) { map.assist = new GamepadSteering(map.profile.gamepadSteer); map.meanToHandwheel = m => SteeringLinkage.HandwheelForMean(car.carParams, m); }
        map.assistCtx = GamepadSteering.Context(car);
        car.handwheelDeg = map.StepSteer(Time.deltaTime, p.handwheelLockDeg, p.steeringRateDegPerSecond, SteeringLinkage.CentreRatio(p));
        car.handwheelInput = true;                                            // column -> pinion -> rack -> per-wheel kinematics (CarController)
        if (map.Pressed(InAction.ShiftUp)) car.drivetrain.Shift(1);
        if (map.Pressed(InAction.ShiftDown)) car.drivetrain.Shift(-1);
        // H-pattern: a held gear button selects that gear; a gear button on a shifter device (non-keyboard) returning to centre = neutral
        InAction[] gears = { InAction.GearR, InAction.Gear1, InAction.Gear2, InAction.Gear3, InAction.Gear4, InAction.Gear5 };
        bool anyShifterHeld = false, haveShifter = false;
        for (int i = 0; i < gears.Length; i++)
        {
            int g = i == 0 ? -1 : i; if (map.Pressed(gears[i])) car.drivetrain.SelectGear(g);
            if (map.HasNonKeyboard(gears[i])) { haveShifter = true; if (map.HeldFromNonKeyboard(gears[i])) anyShifterHeld = true; }
        }
        if (haveShifter && !anyShifterHeld && shifterWasHeld) car.drivetrain.SelectGear(0);
        shifterWasHeld = anyShifterHeld;
        if (map.Pressed(InAction.Reset)) { car.ResetCar(); car.drivetrain.SelectGear(0); car.engine.SetRunning(!map.profile.startEngineOff);   // reset = respawn: neutral, engine as at spawn
 if (ffb) ffb.ResetRamp(); }
        if (map.Pressed(InAction.ResetToStart)) ResetToStart();
        if (map.Pressed(InAction.Recenter) && vr) vr.Recenter();
        if (map.Pressed(InAction.CameraToggle) && vr) vr.ToggleCamera();
        if (map.Pressed(InAction.HudToggle)) { if (panel) panel.showHud = !panel.showHud; map.profile.hudVisible = !map.profile.hudVisible; InputMapper.Save(map.profile); status = map.profile.hudVisible ? "info HUD shown (H)" : "info HUD hidden (H)"; }
        float lx = InputMapper.LookDeadzone(map.Axis(InAction.LookX)), ly = InputMapper.LookDeadzone(map.Axis(InAction.LookY)); LookX = lx;
        bool lookOn = Mathf.Abs(lx) > 0.3f || Mathf.Abs(ly) > 0.3f;
        if (lookOn && !lookWasOn) { LookSource = map.Source(Mathf.Abs(lx) >= Mathf.Abs(ly) ? InAction.LookX : InAction.LookY); Debug.Log($"[Input] look active: yaw {lx * 120f:0} deg cockpit / {lx * 180f:0} deg chase, from {LookSource}"); }
        if (!lookOn && lookWasOn) Debug.Log("[Input] look released"); lookWasOn = lookOn; if (!lookOn) LookSource = "";
        if (vr) vr.FlatLook(lx * 120f, ly * 45f);   // flat cockpit head look (deg)
        if (cam) { cam.lookYaw = lx * 180f; cam.lookPitch = ly * 30f; }   // camera orbit only (deg)
    }
    /// Reset to start: current track's start/finish spawn (yaw, zero velocity), neutral, engine as at spawn, FFB output cut and ramped back in
    bool lookWasOn; [HideInInspector] public float LookX; [HideInInspector] public string LookSource = "";
    // @description - Resets the to start.
    // @return {Void} - nothing
    public void ResetToStart()
    {
        if (ffb) ffb.ResetRamp();
        if (TrackManager.Instance) TrackManager.Instance.Respawn(); else car.ResetCar();
        car.drivetrain.SelectGear(0); car.engine.SetRunning(!map.profile.startEngineOff); status = "reset to start";
    }
    public enum EscResult { None, CancelCapture, CancelCalibration, CloseMenu, OpenMenu }
    // @description - Esc priority: cancel capture > cancel calibration > close menu > open menu. Independent of the binding profile.
    // @param {bool} esc - esc
    // @param {bool} capturing - capturing
    // @param {bool} calibrating - calibrating
    // @param {bool} menuOpen - menu open
    // @return {EscResult} - the escape action
    public static EscResult EscapeAction(bool esc, bool capturing, bool calibrating, bool menuOpen) =>
        !esc ? EscResult.None : capturing ? EscResult.CancelCapture : calibrating ? EscResult.CancelCalibration : menuOpen ? EscResult.CloseMenu : EscResult.OpenMenu;
    bool shifterWasHeld; public bool hintNeutral; Rect navRect; bool scrollToNav; float viewH;
    // @description - Updates the capture.
    // @return {Void} - nothing
    void UpdateCapture()
    {
        if (capturing != null)
        {
            captureT += Time.unscaledDeltaTime;
            if (captureT < 0.3f) return;
            var a = capturing.Value; var c = map.PollCapture(InputMapper.IsAxisAction(a));
            if (c != null)
            {
                var b = map.CompleteCapture(a, c); capturing = null;   // finishes on the first press; that press is consumed (no tab flip / menu toggle / shift)
                status = $"bound {a} -> {b.Label}" + (c is ButtonControl ? "" : "  (press Calibrate)");
            }
        }
        if (calibrating != null && calibrating.resolved != null)
        { float r = InputMapper.Raw(calibrating.resolved); calMin = Mathf.Min(calMin, r); calMax = Mathf.Max(calMax, r); }
    }
    // @description - Finish calibration.
    // @param {InAction} action - action
    // @return {Void} - nothing
    void FinishCalibration(InAction action)
    {
        var b = calibrating; float rest = InputMapper.Raw(b.resolved); calibrating = null;
        if (!b.calibration.ApplyCapture(calMin, calMax, rest, InputMapper.IsBipolar(action))) { status = $"{action}: no travel seen ({calMin:0.000}..{calMax:0.000}) - calibration not changed"; return; }
        status = $"calibrated {action}: raw {b.calibration.min:0.000}..{b.calibration.max:0.000}  rest {(b.calibration.hasRest ? b.calibration.rest : b.calibration.center):0.000}  invert {b.calibration.invert}";
    }
    // ---------------- IMGUI settings menu ----------------
    GUIStyle st;
    // @description - Handles the gui.
    // @return {Void} - nothing
    void OnGUI() { if (VrPanel.Active) return; UiSurface.Begin(out var m); DrawMenu(); UiSurface.End(m); }
    public int tab; static readonly string[] Tabs = { "Quick", "Bindings", "Calibration", "FFB / Steering", "VR / Seat", "Gamepad Steering" };
    // @description - Draws the menu.
    // @return {Void} - nothing
    public void DrawMenu()
    {
        if (map == null || !menuOpen) return;
        GUI.depth = -10;   // draw above the HUD
        if (st == null) { st = new GUIStyle(GUI.skin.label) { fontSize = 13 }; st.normal.textColor = Color.white; }
        var r = new Rect(20, 16, UiSurface.Width - 40, UiSurface.Height - 32); GUI.color = new Color(0.18f, 0.2f, 0.24f, 1f); GUI.DrawTexture(r, Texture2D.whiteTexture); GUI.color = Color.white; GUI.Box(r, GUIContent.none);
        GUILayout.BeginArea(new Rect(r.x + 10, r.y + 8, r.width - 20, r.height - 16));
        var pr = map.profile; var p = car.carParams;
        GUILayout.Label("SETTINGS  (F1/Esc/Start closes and saves; PgDn/Tab / PgUp or MenuTabNext/Prev = next/previous tab; gameplay buttons never switch tabs)   " + InputMapper.DefaultPath, st);
        int nt = GUILayout.Toolbar(tab, Tabs, GUILayout.Height(28)); if (nt != tab) { tab = nt; scroll = Vector2.zero; }
        GUILayout.Label(status, st);
        viewH = r.height - 16f - 120f;
        if (scrollToNav && Event.current.type == EventType.Layout && navRect.height > 0f) { if (navRect.yMin < scroll.y) scroll.y = navRect.yMin; else if (navRect.yMax > scroll.y + viewH) scroll.y = navRect.yMax - viewH; scrollToNav = false; }
        scroll = GUILayout.BeginScrollView(scroll, GUILayout.ExpandHeight(true));   // full-height page: mouse wheel, scrollbar drag, navigation auto-scrolls
        switch (tab)
        {
            case 0: DrawQuick(pr); break;
            case 1: DrawBindings(pr); break;
            case 2: DrawCalibration(pr); break;
            case 3: DrawFfb(pr, p); break;
            case 4: DrawVr(pr, p); break;
            case 5: DrawGamepad(pr); break;
        }
        GUILayout.EndScrollView();
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Save", GUILayout.Width(100))) { InputMapper.Save(pr); status = "saved"; }
        if (GUILayout.Button("Reload", GUILayout.Width(100))) { map.profile = InputMapper.LoadOrDefault(); map.MarkDirty(); status = "reloaded"; }
        if (GUILayout.Button("Reset defaults", GUILayout.Width(120))) { map.profile = InputMapper.Defaults(); map.MarkDirty(); status = "defaults (not saved yet)"; }
        if (GUILayout.Button("Close", GUILayout.Width(100))) { menuOpen = false; InputMapper.Save(pr); if (ffb) ffb.TryOpen(); }
        GUILayout.EndHorizontal();
        if (TrackManager.Instance && TrackManager.Instance.Credit.Length > 0) GUILayout.Label(TrackManager.Instance.Credit, st);
        GUILayout.EndArea();
    }
    // @description - Draws the bindings.
    // @param {BindingProfile} pr - value of type BindingProfile
    // @return {Void} - nothing
    void DrawBindings(BindingProfile pr)
    {
        GUILayout.Label("Devices: " + string.Join(" | ", DeviceNames()), st);
        foreach (InAction a in System.Enum.GetValues(typeof(InAction)))
        {
            var slots = pr.Get(a); bool bip = InputMapper.IsBipolar(a);
            GUILayout.BeginHorizontal();
            GUILayout.Label($"{a}  = {(InputMapper.IsAxisAction(a) ? map.Axis(a).ToString("0.00") : (map.Held(a) ? "ON" : "off"))}", st, GUILayout.Width(190));
            if (GUILayout.Button(capturing == a ? "move/press... (Esc / Shift+Esc clear)" : "+ Bind", GUILayout.Width(140))) { capturing = a; captureT = 0f; map.BeginCapture(); status = "move the axis fully / press the button"; }
            GUILayout.EndHorizontal();
            for (int i = 0; i < slots.Count; i++)
            {
                var b = slots[i]; GUILayout.BeginHorizontal(); GUILayout.Space(30);
                GUILayout.Label($"{b.Label}  raw {(b.resolved != null ? InputMapper.Raw(b.resolved).ToString("0.000") : "MISSING")}", st, GUILayout.Width(420));
                if (!(b.resolved is ButtonControl) && !bip && GUILayout.Button(b.calibration.half.ToString(), GUILayout.Width(70))) b.calibration.half = (AxisHalf)(((int)b.calibration.half + 1) % 3);
                if (a == InAction.Steer && GUILayout.Button(b.steerSource.ToString(), GUILayout.Width(60))) b.steerSource = b.steerSource == SteerSource.Wheel ? SteerSource.Stick : SteerSource.Wheel;
                if (a == InAction.Steer) b.negative = GUILayout.Toggle(b.negative, "neg", GUILayout.Width(50));
                if (GUILayout.Button("X", GUILayout.Width(25))) { slots.RemoveAt(i); i--; }
                GUILayout.EndHorizontal();
            }
        }
    }
    // @description - Draws the calibration.
    // @param {BindingProfile} pr - value of type BindingProfile
    // @return {Void} - nothing
    void DrawCalibration(BindingProfile pr)
    {
        GUILayout.Label("Analog axes: raw device value vs mapped value. Pedals: Calibrate, press fully, release, Done (any raw range incl. -1..1 and inverted is auto-detected). Uncalibrated pedal axes read 0 at their rest position until calibrated. Load-cell brake: calibrate fully; the SmartControl curve passes through linearly.", st);
        DrawButtonDebounce(pr);
        diag = GUILayout.Toggle(diag, " Input diagnostics (every device/control feeding each action, raw values, HID report filter)");
        if (diag) DrawDiagnostics(pr);
        foreach (InAction a in System.Enum.GetValues(typeof(InAction)))
        {
            if (!InputMapper.IsAxisAction(a)) continue; bool bip = InputMapper.IsBipolar(a);
            foreach (var b in pr.Get(a))
            {
                if (b.resolved is ButtonControl || b.layout == "Keyboard") continue;
                GUILayout.BeginHorizontal();
                bool unc = b.resolved != null && InputMapper.UncalibratedPedal(b, bip);
                float v = b.resolved == null ? 0f : unc ? InputMapper.RestRelative(b) : b.calibration.Normalize(InputMapper.Raw(b.resolved), bip);
                GUILayout.Label($"{a}: {b.Label}", st, GUILayout.Width(330));
                GUILayout.Label($"raw {(b.resolved != null ? InputMapper.Raw(b.resolved).ToString("+0.000;-0.000") : "MISSING")}  mapped {(bip ? v.ToString("+0.00;-0.00") : v.ToString("0.00"))} ({v * 100f,4:0}%)  {(b.calibration.calibrated ? (b.calibration.hasRest ? $"cal {b.calibration.rest:0.00}->{b.calibration.PressedEnd:0.00}" : $"cal {b.calibration.min:0.00}..{b.calibration.max:0.00}") : unc ? "NOT calibrated (rest-relative)" : "default range")}", st, GUILayout.Width(400));
                b.calibration.invert = GUILayout.Toggle(b.calibration.invert, "inv", GUILayout.Width(45));   // with hasRest, toggling swaps which end counts as pressed
                GUILayout.Label($"dz {b.calibration.deadzone:0.00}", st, GUILayout.Width(55)); b.calibration.deadzone = GUILayout.HorizontalSlider(b.calibration.deadzone, 0f, 0.3f, GUILayout.Width(70));
                if (calibrating == b) { GUILayout.Label($"[{calMin:0.00} .. {calMax:0.00}]", st, GUILayout.Width(110)); if (GUILayout.Button(bip ? "Done (centred)" : "Done (released)", GUILayout.Width(120))) FinishCalibration(a); }
                else if (b.resolved != null && GUILayout.Button("Calibrate", GUILayout.Width(80))) { calibrating = b; calMin = calMax = InputMapper.Raw(b.resolved); status = bip ? "turn fully both ways, centre, press Done" : "press fully, release, press Done"; }
                GUILayout.EndHorizontal();
            }
        }
    }
    bool diag;
    // @description - Draws the diagnostics.
    // @param {BindingProfile} pr - value of type BindingProfile
    // @return {Void} - nothing
    void DrawDiagnostics(BindingProfile pr)
    {
        GUILayout.Label(HidReportFilter.DeviceReport(), st);
        foreach (var s in pr.actions)
        {
            int live = 0; foreach (var b in s.slots) if (b.resolved != null) live++;
            if (live == 0) continue;
            var seen = new System.Collections.Generic.HashSet<InputDevice>(); bool multiDev = false;
            foreach (var b in s.slots) if (b.resolved != null && !(b.resolved.device is Keyboard)) { if (seen.Count > 0 && !seen.Contains(b.resolved.device)) multiDev = true; seen.Add(b.resolved.device); }
            GUILayout.Label($"{s.action}{(multiDev ? "   [fed by more than one non-keyboard device]" : "")}", st);
            foreach (var b in s.slots)
            {
                if (b.resolved == null) { GUILayout.Label($"     {b.Label}  -> not connected", st); continue; }
                var d = b.resolved.device; bool bip = InputMapper.IsBipolar(s.action);
                string val = InputMapper.IsAxisAction(s.action) ? b.calibration.Normalize(InputMapper.Raw(b.resolved), bip && !(b.resolved is ButtonControl)).ToString("+0.000;-0.000") : (b.debounceStableState ? "1" : "0");
                GUILayout.Label($"     dev#{d.deviceId} '{d.displayName}'  {b.resolved.path}  raw {InputMapper.Raw(b.resolved):+0.0000;-0.0000}  -> {val}", st);
            }
        }
    }
    // @description - Draws the button debounce.
    // @param {BindingProfile} pr - value of type BindingProfile
    // @return {Void} - nothing
    void DrawButtonDebounce(BindingProfile pr)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label($"Optional button debounce {pr.debounceMs:0} ms (0 = off, default)", st, GUILayout.Width(420));
        pr.debounceMs = Mathf.Round(GUILayout.HorizontalSlider(pr.debounceMs, 0f, 30f, GUILayout.Width(160)));
        if (GUILayout.Button("Reset chatter", GUILayout.Width(110))) foreach (var s in pr.actions) foreach (var b in s.slots) b.chatterCount = 0;
        GUILayout.EndHorizontal();
        foreach (var s in pr.actions)
        {
            if (InputMapper.IsAxisAction(s.action)) continue;
            foreach (var b in s.slots)
            {
                if (b.resolved == null || b.layout == "Keyboard") continue;
                GUILayout.Label($"   {s.action}: {b.Label}   raw {(b.debounceRawState ? "1" : "0")}  debounced {(b.debounceStableState ? "1" : "0")}  chatter {b.chatterCount}", st);
            }
        }
    }
    // @description - Draws the ffb.
    // @param {BindingProfile} pr - value of type BindingProfile
    // @param {CarParams} carParams - car params
    // @return {Void} - nothing
    void DrawFfb(BindingProfile pr, CarParams carParams)
    {
        GUILayout.BeginHorizontal();
        float rot = pr.RotationDeg(carParams.handwheelLockDeg);
        GUILayout.Label($"Wheel rotation lock-to-lock (= True Drive setting): {rot:0} deg {(pr.wheelRotationDeg <= 0f ? "(car default)" : "")}", st, GUILayout.Width(420));
        float sv = GUILayout.HorizontalSlider(rot, 180f, 2520f, GUILayout.Width(250)); if (sv != rot) pr.wheelRotationDeg = Mathf.Round(sv / 10f) * 10f;   // only when dragged (car value 1269 is not a multiple of 10)
        if (GUILayout.Button($"Car {2f * carParams.handwheelLockDeg:0}", GUILayout.Width(90))) pr.wheelRotationDeg = 0f;
        GUILayout.EndHorizontal();
        if (GUILayout.Toggle(pr.mapping == SteerMapping.RealColumn1to1, " 1:1 column (wheel angle = car steering-wheel angle, soft lock at your wheel's limit)")) pr.mapping = SteerMapping.RealColumn1to1;
        if (GUILayout.Toggle(pr.mapping == SteerMapping.ScaleToCarLock, " Scale to car lock")) pr.mapping = SteerMapping.ScaleToCarLock;
        float half = pr.RotationDeg(carParams.handwheelLockDeg) * 0.5f, colAtLimit = pr.mapping == SteerMapping.RealColumn1to1 ? Mathf.Min(half, carParams.handwheelLockDeg) : carParams.handwheelLockDeg;
        GUILayout.Label($"  -> at your wheel's limit (±{half:0}°): car column ±{colAtLimit:0}° = mean road wheel ±{SteeringLinkage.MeanAngle(carParams, colAtLimit):0.0}°; effective on-centre ratio {half / colAtLimit * SteeringLinkage.CentreRatio(carParams):0.00}:1", st);
        GUILayout.Space(8);
        pr.forceFeedbackEnabled = GUILayout.Toggle(pr.forceFeedbackEnabled, " Force feedback");
        GUILayout.BeginHorizontal(); GUILayout.Label("Wheelbase max torque N m (SC2 Sport 17 / Pro 25 / Ultimate 32):", st, GUILayout.Width(420));
        string tq = GUILayout.TextField(pr.wheelbaseMaxTorqueNm > 0f ? pr.wheelbaseMaxTorqueNm.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) : "", GUILayout.Width(60));
        if (float.TryParse(tq, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float tv)) pr.wheelbaseMaxTorqueNm = Mathf.Max(0f, tv); else if (tq == "") pr.wheelbaseMaxTorqueNm = 0f;
        GUILayout.EndHorizontal();
        GUILayout.BeginHorizontal(); GUILayout.Label($"Strength {pr.forceFeedbackStrengthPercent:0}%", st, GUILayout.Width(150)); pr.forceFeedbackStrengthPercent = Mathf.Round(GUILayout.HorizontalSlider(pr.forceFeedbackStrengthPercent, 0f, 100f, GUILayout.Width(200))); GUILayout.EndHorizontal();
        GUILayout.BeginHorizontal(); GUILayout.Label($"Max output cap {pr.forceFeedbackMaxOutputPercent:0}%", st, GUILayout.Width(150)); pr.forceFeedbackMaxOutputPercent = Mathf.Round(GUILayout.HorizontalSlider(pr.forceFeedbackMaxOutputPercent, 0f, 100f, GUILayout.Width(200))); GUILayout.EndHorizontal();
        pr.forceFeedbackInvert = GUILayout.Toggle(pr.forceFeedbackInvert, " Invert direction");
        GUILayout.BeginHorizontal(); GUILayout.Label("FFB device (name contains, empty = first):", st, GUILayout.Width(300)); pr.forceFeedbackDevice = GUILayout.TextField(pr.forceFeedbackDevice ?? "", GUILayout.Width(220)); GUILayout.EndHorizontal();
        if (ffb != null) { foreach (var d in ffb.devices) if (GUILayout.Button(d, GUILayout.Width(260))) pr.forceFeedbackDevice = d; GUILayout.Label(ffb.status, st); }
    }
    // @description - Draws the vr.
    // @param {BindingProfile} pr - value of type BindingProfile
    // @param {CarParams} carParams - car params
    // @return {Void} - nothing
    void DrawVr(BindingProfile pr, CarParams carParams)
    {
        GUILayout.Label(vr ? vr.status : "no VR manager", st);
        bool vrOn = GUILayout.Toggle(pr.vrEnabled, " VR (OpenXR / SteamVR)"); if (vrOn != pr.vrEnabled) { pr.vrEnabled = vrOn; if (vr) vr.SetEnabled(vrOn); }
        if (GUILayout.Button("Recenter view", GUILayout.Width(160)) && vr) vr.Recenter();
        if (GUILayout.Button(vr && vr.mode == CamMode.Cockpit ? "Camera: cockpit (switch to chase)" : "Camera: chase (switch to cockpit)", GUILayout.Width(280)) && vr) vr.ToggleCamera();
        Cockpit ck = vr ? vr.cockpit : null;
        GUILayout.BeginHorizontal(); GUILayout.Label($"Seat fore/aft {pr.seatForwardOffset * 1000f:+0;-0;0} mm", st, GUILayout.Width(180)); float f = GUILayout.HorizontalSlider(pr.seatForwardOffset, -carParams.seatTravelAft, carParams.seatTravelFore, GUILayout.Width(250)); GUILayout.EndHorizontal();
        GUILayout.BeginHorizontal(); GUILayout.Label($"Seat up/down {pr.seatUpOffset * 1000f:+0;-0;0} mm", st, GUILayout.Width(180)); float u = GUILayout.HorizontalSlider(pr.seatUpOffset, -carParams.seatTravelDown, carParams.seatTravelUp, GUILayout.Width(250)); GUILayout.EndHorizontal();
        if (f != pr.seatForwardOffset || u != pr.seatUpOffset) { pr.seatForwardOffset = f; pr.seatUpOffset = u; if (ck) ck.ApplySeat(f, u); }
        if (panel) panel.showHud = GUILayout.Toggle(panel.showHud, " Debug HUD panel in VR");
        { var c = vr && vr.cam ? vr.cam : Camera.main; float asp = c ? c.aspect : 16f / 9f;
          GUILayout.BeginHorizontal(); GUILayout.Label($"Flat-screen FOV {pr.flatFovDeg:0}° vertical = {VrManager.HorizontalFov(pr.flatFovDeg, asp):0}° horizontal", st, GUILayout.Width(330));
          float nf = Mathf.Round(GUILayout.HorizontalSlider(pr.flatFovDeg, BindingProfile.FovMin, BindingProfile.FovMax, GUILayout.Width(250))); GUILayout.EndHorizontal();
          if (nf != pr.flatFovDeg) { pr.flatFovDeg = nf; if (vr) vr.ApplyFlatFov(); } }
    }
    // ---------------- quick settings: navigable with Menu Up/Down/Left/Right/Select (keyboard arrows+Enter, gamepad d-pad+A, or bound wheel buttons) ----------------
    static readonly float[] TorquePresets = { 0f, 17f, 25f, 32f };   // Simucube 2 Sport / Pro / Ultimate peak torque (S, manufacturer); 0 = not set
    // @description - Quick items.
    // @param {BindingProfile} pr - value of type BindingProfile
    // @return {string[]} - the quick items
    public string[] QuickItems(BindingProfile pr)
    {
        var p = car.carParams; Cockpit ck = vr ? vr.cockpit : null;
        return new[] {
            $"VR: {(VrManager.Active ? "ON" : "off")}   [{(vr ? vr.status : "no VR manager")}]",
            "Recenter VR view (also F12 / Home / right-stick click)",
            $"Camera (flat screen): {(vr && vr.mode == CamMode.Cockpit ? "cockpit" : "chase")}",
            $"Seat fore/aft: {pr.seatForwardOffset * 1000f:+0;-0;0} mm  (range -{p.seatTravelAft * 1000f:0}..+{p.seatTravelFore * 1000f:0})",
            $"Seat up/down: {pr.seatUpOffset * 1000f:+0;-0;0} mm  (range -{p.seatTravelDown * 1000f:0}..+{p.seatTravelUp * 1000f:0})",
            $"Force feedback: {(pr.forceFeedbackEnabled ? "ON" : "off")}   [{(ffb ? ffb.status : "")}]",
            $"Wheelbase max torque: {(pr.wheelbaseMaxTorqueNm > 0f ? pr.wheelbaseMaxTorqueNm.ToString("0.#") + " N m" : "not set")}",
            $"FFB strength: {pr.forceFeedbackStrengthPercent:0}%",
            $"FFB max output cap: {pr.forceFeedbackMaxOutputPercent:0}%",
            $"FFB invert: {(pr.forceFeedbackInvert ? "yes" : "no")}",
            $"Wheel rotation: {pr.RotationDeg(p.handwheelLockDeg):0} deg {(pr.wheelRotationDeg <= 0f ? "(car)" : "")}  (Select = car {2f * p.handwheelLockDeg:0})",
            $"Volume: {pr.audioVolume * 100f:0}%",
            $"Mute: {(pr.audioMute ? "yes" : "no")}",
            $"Auto clutch (keyboard / no clutch pedal): {(pr.autoClutch ? "on" : "off")}",
            $"Start with engine off: {(pr.startEngineOff ? "yes" : "no")}",
            $"Starter interlock (clutch in or neutral): {(pr.starterInterlock ? "on" : "off (period-correct)")}",
            $"Tire thermal model (temperature + pressure): {(pr.tireThermal ? "ON" : "off")}",
            $"Cold tire pressure front: {pr.frontColdPressurePsi:0.0} psi  (VW 25 psi)",
            $"Cold tire pressure rear: {pr.rearColdPressurePsi:0.0} psi  (VW 25 psi)",
            $"Tyre compound: {(pr.tireCompound == 1 ? "Experimental (viscoelastic)" : "Default")}  (Left/Right/Select)",
            $"VR debug HUD panel: {(panel && panel.showHud ? "shown" : "hidden")}  (H)",
            $"Flat-screen FOV: {pr.flatFovDeg:0}° vertical ({VrManager.HorizontalFov(pr.flatFovDeg, Camera.main ? Camera.main.aspect : 16f / 9f):0}° horizontal)",
            $"Track: {(TrackManager.Instance ? TrackManager.Names[TrackManager.Instance.current] + (TrackManager.Instance.Loading ? " (loading...)" : "") : "-")}   (Left/Right/Select = change, loads now)",
            "Reset car to start/finish (Select)   [Backspace / bindable 'ResetToStart']",
            "Close and save" };
    }
    // @description - Navigate.
    // @return {Void} - nothing
    public void Navigate()
    {
        if (map.Pressed(InAction.MenuTabNext)) { tab = (tab + 1) % Tabs.Length; scroll = Vector2.zero; }
        if (map.Pressed(InAction.MenuTabPrev)) { tab = (tab + Tabs.Length - 1) % Tabs.Length; scroll = Vector2.zero; }
        if (tab == 5) { NavigateGamepad(map.profile); return; }
        if (tab != 0) return;
        var pr = map.profile; int n = QuickItems(pr).Length;
        if (map.Pressed(InAction.MenuUp)) { navIndex = (navIndex + n - 1) % n; scrollToNav = true; }
        if (map.Pressed(InAction.MenuDown)) { navIndex = (navIndex + 1) % n; scrollToNav = true; }
        int dir = (map.Pressed(InAction.MenuRight) ? 1 : 0) - (map.Pressed(InAction.MenuLeft) ? 1 : 0); bool sel = map.Pressed(InAction.MenuSelect);
        if (dir != 0 || sel) Adjust(pr, navIndex, sel ? 0 : dir, sel);
    }
    // @description - Adjust.
    // @param {BindingProfile} pr - value of type BindingProfile
    // @param {int} item - item
    // @param {int} dir - dir
    // @param {bool} sel - sel
    // @return {Void} - nothing
    public void Adjust(BindingProfile pr, int item, int dir, bool sel)
    {
        var p = car.carParams; Cockpit ck = vr ? vr.cockpit : null; const float seatStep = 0.01f, pctStep = 5f, rotStep = 10f;   // UI step sizes
        switch (item)
        {
            case 0: pr.vrEnabled = sel ? !pr.vrEnabled : dir > 0; if (vr) vr.SetEnabled(pr.vrEnabled); break;
            case 1: if (vr) vr.Recenter(); break;
            case 2: if (vr) vr.ToggleCamera(); break;
            case 3: pr.seatForwardOffset = Mathf.Clamp(pr.seatForwardOffset + dir * seatStep, -p.seatTravelAft, p.seatTravelFore); if (ck) ck.ApplySeat(pr.seatForwardOffset, pr.seatUpOffset); break;
            case 4: pr.seatUpOffset = Mathf.Clamp(pr.seatUpOffset + dir * seatStep, -p.seatTravelDown, p.seatTravelUp); if (ck) ck.ApplySeat(pr.seatForwardOffset, pr.seatUpOffset); break;
            case 5: pr.forceFeedbackEnabled = sel ? !pr.forceFeedbackEnabled : dir > 0; break;
            case 6: { int i = System.Array.IndexOf(TorquePresets, pr.wheelbaseMaxTorqueNm); if (i < 0) i = 0; i = Mathf.Clamp(i + (sel ? 1 : dir), 0, TorquePresets.Length - 1); pr.wheelbaseMaxTorqueNm = TorquePresets[i]; break; }
            case 7: pr.forceFeedbackStrengthPercent = Mathf.Clamp(pr.forceFeedbackStrengthPercent + dir * pctStep, 0f, 100f); break;
            case 8: pr.forceFeedbackMaxOutputPercent = Mathf.Clamp(pr.forceFeedbackMaxOutputPercent + dir * pctStep, 0f, 100f); break;
            case 9: pr.forceFeedbackInvert = sel ? !pr.forceFeedbackInvert : dir > 0; break;
            case 10: if (sel) pr.wheelRotationDeg = 0f; else pr.wheelRotationDeg = Mathf.Clamp(pr.RotationDeg(p.handwheelLockDeg) + dir * rotStep, 180f, 2520f); break;
            case 11: pr.audioVolume = Mathf.Clamp01(pr.audioVolume + dir * pctStep * 0.01f); break;
            case 12: pr.audioMute = sel ? !pr.audioMute : dir > 0; break;
            case 13: pr.autoClutch = sel ? !pr.autoClutch : dir > 0; break;
            case 14: pr.startEngineOff = sel ? !pr.startEngineOff : dir > 0; break;
            case 15: pr.starterInterlock = sel ? !pr.starterInterlock : dir > 0; break;
            case 16: pr.tireThermal = sel ? !pr.tireThermal : dir > 0; break;
            case 17: pr.frontColdPressurePsi = sel ? 24.7f : Mathf.Clamp(pr.frontColdPressurePsi + dir * 0.5f, 15f, 45f); break;   // 0.5 psi steps (gauge resolution); Select = VW 1.7 bar
            case 18: pr.rearColdPressurePsi = sel ? 24.7f : Mathf.Clamp(pr.rearColdPressurePsi + dir * 0.5f, 15f, 45f); break;
            case 19: pr.tireCompound = sel ? 1 - pr.tireCompound : (dir > 0 ? 1 : 0); break;
            case 20: if (panel) panel.showHud = sel ? !panel.showHud : dir > 0; break;
            case 21: pr.flatFovDeg = sel ? 60f : Mathf.Clamp(pr.flatFovDeg + dir * 5f, BindingProfile.FovMin, BindingProfile.FovMax); if (vr) vr.ApplyFlatFov(); break;
            case 22: { var tm = TrackManager.Instance; if (tm) { int n = TrackManager.Names.Length; tm.Select((tm.current + (sel ? 1 : dir) + n) % n); } break; }
            case 23: if (sel) { ResetToStart(); menuOpen = false; } break;
            case 24: if (sel) { menuOpen = false; InputMapper.Save(pr); status = "saved"; if (ffb) ffb.TryOpen(); } break;
        }
    }
    // @description - Draws the quick.
    // @param {BindingProfile} pr - value of type BindingProfile
    // @return {Void} - nothing
    void DrawQuick(BindingProfile pr)
    {
        var items = QuickItems(pr); var hl = new GUIStyle(st) { fontStyle = FontStyle.Bold }; hl.normal.textColor = new Color(1f, 0.85f, 0.3f);
        GUILayout.Label("QUICK SETTINGS  (Up/Down select, Left/Right change, Enter/A/bound wheel button = select)", st);
        for (int i = 0; i < items.Length; i++)
        {
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(i == navIndex ? ">" : " ", GUILayout.Width(24))) navIndex = i;
            GUILayout.Label(items[i], i == navIndex ? hl : st);
            GUILayout.EndHorizontal();
            if (i == navIndex && Event.current.type == EventType.Repaint) navRect = GUILayoutUtility.GetLastRect();
        }
        GUILayout.Space(6);
        GUILayout.BeginHorizontal(); GUILayout.Label($"Master {pr.audioVolume * 100f:0}%", st, GUILayout.Width(110)); pr.audioVolume = GUILayout.HorizontalSlider(pr.audioVolume, 0f, 1f, GUILayout.Width(250)); pr.audioMute = GUILayout.Toggle(pr.audioMute, " mute", GUILayout.Width(80)); GUILayout.EndHorizontal();
        GUILayout.BeginHorizontal(); GUILayout.Label($"Tires {pr.tireVolume * 100f:0}%", st, GUILayout.Width(110)); pr.tireVolume = GUILayout.HorizontalSlider(pr.tireVolume, 0f, 2f, GUILayout.Width(250)); GUILayout.Label("(tire scrub level relative to engine; 100% = default)", st); GUILayout.EndHorizontal();
        GUILayout.BeginHorizontal(); GUILayout.Label($"Ambient brightness {pr.ambientBrightness * 100f:0}%", st, GUILayout.Width(170)); pr.ambientBrightness = GUILayout.HorizontalSlider(pr.ambientBrightness, 0.5f, 2f, GUILayout.Width(190)); GUILayout.Label("(sky/bounce ambient + fill light; visual only, default 130%)", st); GUILayout.EndHorizontal();
    }
    // ---------------- Gamepad steering preferences (input assist; never applies to a wheel bound as SteerSource.Wheel) ----------------
    public struct GpSlider { public string label, fmt, help; public float min, max, step; public System.Func<GamepadSteerSettings, float> get; public System.Action<GamepadSteerSettings, float> set; }
    public static readonly GpSlider[] GpSliders = {
        new GpSlider { label = "Steering speed", fmt = "{0:0} deg/s road", help = "how fast the wheel turns towards the stick target (lower = calmer)", min = 5f, max = 120f, step = 5f, get = g => g.steerSpeed, set = (g, v) => g.steerSpeed = v },
        new GpSlider { label = "Response curve (gamma)", fmt = "{0:0.0}", help = "1 = linear; higher = less sensitive near centre", min = 1f, max = 4f, step = 0.1f, get = g => g.gamma, set = (g, v) => g.gamma = v },
        new GpSlider { label = "Deadzone", fmt = "{0:P0}", help = "stick travel ignored around centre (rest rescaled)", min = 0f, max = 0.3f, step = 0.01f, get = g => g.deadzone, set = (g, v) => g.deadzone = v },
        new GpSlider { label = "Smoothing", fmt = "{0:0.00} s", help = "filter time constant on the stick (0 = off)", min = 0f, max = 0.3f, step = 0.01f, get = g => g.smoothing, set = (g, v) => g.smoothing = v },
        new GpSlider { label = "Speed sensitivity", fmt = "{0:P0}", help = "0 = full lock at any speed; 100% = full stick limited to peak tire grip at speed", min = 0f, max = 1f, step = 0.05f, get = g => g.speedSensitivity, set = (g, v) => g.speedSensitivity = v },
        new GpSlider { label = "Grip limit", fmt = "{0:P0} of peak slip", help = "full stick at speed puts the front tires at this fraction of their peak-grip slip angle", min = 0.5f, max = 2f, step = 0.05f, get = g => g.gripLimit, set = (g, v) => g.gripLimit = v },
        new GpSlider { label = "Countersteer speed", fmt = "x{0:0.0}", help = "steering-speed factor when unwinding / catching a rear slide", min = 1f, max = 4f, step = 0.1f, get = g => g.countersteer, set = (g, v) => g.countersteer = v },
        new GpSlider { label = "Self-centre (caster follow)", fmt = "{0:P0}", help = "released stick: wheels follow the front-axle travel direction (0 = straight ahead)", min = 0f, max = 1f, step = 0.05f, get = g => g.casterFollow, set = (g, v) => g.casterFollow = v },
    };
    public static int GpItemCount => GpSliders.Length + 2;   // [0] assist on/off, sliders, [last] reset all
    // @description - Gp adjust.
    // @param {GamepadSteerSettings} g - value of type GamepadSteerSettings
    // @param {int} item - item
    // @param {int} dir - dir
    // @param {bool} sel - sel
    // @return {Void} - nothing
    public static void GpAdjust(GamepadSteerSettings g, int item, int dir, bool sel)
    {
        var d = GamepadSteerSettings.Default;
        if (item == 0) { g.enabled = sel ? !g.enabled : dir > 0 ? true : dir < 0 ? false : g.enabled; return; }
        if (item == GpItemCount - 1) { if (sel) JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(d), g); return; }
        var sl = GpSliders[item - 1];
        if (sel) { sl.set(g, sl.get(d)); return; }   // Select / A = reset this slider to default
        float v = Mathf.Clamp(Mathf.Round((sl.get(g) + dir * sl.step) / sl.step) * sl.step, sl.min, sl.max); sl.set(g, v);
    }
    // @description - Navigate gamepad.
    // @param {BindingProfile} pr - value of type BindingProfile
    // @return {Void} - nothing
    void NavigateGamepad(BindingProfile pr)
    {
        int n = GpItemCount;
        if (map.Pressed(InAction.MenuUp)) { navIndex = (navIndex + n - 1) % n; scrollToNav = true; }
        if (map.Pressed(InAction.MenuDown)) { navIndex = (navIndex + 1) % n; scrollToNav = true; }
        navIndex = Mathf.Clamp(navIndex, 0, n - 1);
        int dir = (map.Pressed(InAction.MenuRight) ? 1 : 0) - (map.Pressed(InAction.MenuLeft) ? 1 : 0); bool sel = map.Pressed(InAction.MenuSelect);
        if (dir != 0 || sel) GpAdjust(pr.gamepadSteer, navIndex, sel ? 0 : dir, sel);
    }
    // @description - Draws the gamepad.
    // @param {BindingProfile} pr - value of type BindingProfile
    // @return {Void} - nothing
    void DrawGamepad(BindingProfile pr)
    {
        var g = pr.gamepadSteer; var d = GamepadSteerSettings.Default; var hl = new GUIStyle(st) { fontStyle = FontStyle.Bold }; hl.normal.textColor = new Color(1f, 0.85f, 0.3f);
        GUILayout.Label("GAMEPAD / KEYBOARD STEERING  (input preferences, not physics; a wheel bound as 'Wheel' is never affected)", st);
        GUILayout.Label("Up/Down select, Left/Right change, Select/A = reset that item to default", st);
        void Row(int i, System.Action body)
        {
            if (navIndex >= GpItemCount && tab == 5) navIndex = 0;
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(i == navIndex ? ">" : " ", GUILayout.Width(24))) navIndex = i;
            body(); GUILayout.EndHorizontal();
            if (i == navIndex && Event.current.type == EventType.Repaint) navRect = GUILayoutUtility.GetLastRect();
        }
        Row(0, () => { g.enabled = GUILayout.Toggle(g.enabled, g.enabled ? " Steering assist ON (stick sets a target, wheel follows)" : " Steering assist OFF (direct: stick = lock fraction)", navIndex == 0 ? hl : st); });
        for (int k = 0; k < GpSliders.Length; k++)
        {
            var sl = GpSliders[k]; int i = k + 1;
            Row(i, () =>
            {
                GUILayout.Label(sl.label, i == navIndex ? hl : st, GUILayout.Width(200));
                GUILayout.Label(string.Format(System.Globalization.CultureInfo.InvariantCulture, sl.fmt, sl.get(g)), st, GUILayout.Width(130));
                float nv = GUILayout.HorizontalSlider(sl.get(g), sl.min, sl.max, GUILayout.Width(200)); if (nv != sl.get(g)) sl.set(g, Mathf.Round(nv / sl.step) * sl.step);
                if (GUILayout.Button("Reset", GUILayout.Width(60))) sl.set(g, sl.get(d));
                GUILayout.Label($"(default {string.Format(System.Globalization.CultureInfo.InvariantCulture, sl.fmt, sl.get(d))}; {sl.help})", st);
            });
        }
        Row(GpItemCount - 1, () => { if (GUILayout.Button("Reset all gamepad steering settings to defaults", GUILayout.Width(360))) GpAdjust(g, GpItemCount - 1, 0, true); GUILayout.Label(navIndex == GpItemCount - 1 ? "<- Select/A" : "", hl); });
        var a = map.assist;
        if (a != null && map.assistCtx.HasValue) { var c = map.assistCtx.Value; GUILayout.Label($"live: target {a.lastTarget:0.0} deg road, steering {a.roadDeg:0.0} deg, front tire peak slip {c.peakSlipDeg:0.0} deg, front travel {c.frontTravelDeg:0.0} deg, rear travel {c.rearTravelDeg:0.0} deg{(a.countersteering ? "  [countersteer rate]" : "")}", st); }
    }
    // @description - Device names.
    // @return {IEnumerable<string>} - the device names
    static IEnumerable<string> DeviceNames() { foreach (var d in InputSystem.devices) if (!InputMapper.Skip(d)) yield return $"{(string.IsNullOrEmpty(d.description.product) ? d.layout : d.description.product)} [{InputMapper.Category(d)}]"; }
}
