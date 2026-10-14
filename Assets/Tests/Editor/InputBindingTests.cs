// Virtual-device tests (Input System InputTestFixture): wheel, pedals and shifter from different "brands" + a gamepad.
using NUnit.Framework; using UnityEngine; using UnityEngine.InputSystem; using UnityEngine.InputSystem.Controls; using UnityEngine.InputSystem.Layouts;
public class InputBindingTests : InputTestFixture
{
    InputDevice wheel, pedals, shifter; Gamepad pad;
    [SetUp] public override void Setup()
    {
        base.Setup();
        InputSystem.RegisterLayout<Joystick>("FakeWheel", new InputDeviceMatcher().WithProduct("BrandA Wheel 900"));
        InputSystem.RegisterLayout<Joystick>("FakePedals", new InputDeviceMatcher().WithProduct("BrandB Pedals"));
        InputSystem.RegisterLayout<Joystick>("FakeShifter", new InputDeviceMatcher().WithProduct("BrandC Shifter"));
        wheel = InputSystem.AddDevice(new InputDeviceDescription { product = "BrandA Wheel 900" });
        pedals = InputSystem.AddDevice(new InputDeviceDescription { product = "BrandB Pedals" });
        shifter = InputSystem.AddDevice(new InputDeviceDescription { product = "BrandC Shifter" });
        pad = InputSystem.AddDevice<Gamepad>();
    }
    static InputMapper NM(BindingProfile p) { p.debounceMs = 0f; return new InputMapper(p); }   // edge-logic tests: debounce off (chatter tests below set it)
    double now; [TearDown] public override void TearDown() { InputMapper.Clock = () => Time.realtimeSinceStartupAsDouble; base.TearDown(); }
    static AxisControl A(InputDevice d, string path) => (AxisControl)d.TryGetChildControl(path);

    [Test] public void Calibration_Math()
    {
        var c = new AxisCal { min = -1f, max = 1f };                       // pedal reporting -1 released .. +1 pressed
        Assert.AreEqual(0f, c.Normalize(-1f, false), 1e-5); Assert.AreEqual(0.5f, c.Normalize(0f, false), 1e-5); Assert.AreEqual(1f, c.Normalize(1f, false), 1e-5);
        c.invert = true; Assert.AreEqual(1f, c.Normalize(-1f, false), 1e-5);  // inverted pedal (released = +1)
        var h = new AxisCal { min = -1f, max = 1f, center = 0f, half = AxisHalf.Negative }; Assert.AreEqual(0.6f, h.Normalize(-0.6f, false), 1e-5); Assert.AreEqual(0f, h.Normalize(0.6f, false), 1e-5);
        var w = new AxisCal { min = 0f, max = 1f, center = 0.5f }; Assert.AreEqual(-1f, w.Normalize(0f, true), 1e-5); Assert.AreEqual(0.5f, w.Normalize(0.75f, true), 1e-5);   // 0..1 wheel axis
        var dz = new AxisCal { min = 0f, max = 1f, deadzone = 0.1f }; Assert.AreEqual(0f, dz.Normalize(0.05f, false), 1e-5); Assert.AreEqual(0.5f, dz.Normalize(0.55f, false), 1e-5);
    }
    [Test] public void Calibration_FullRangeBipolarPedal()   // Heusinkveld Sprint on Windows: -1 released .. +1 pressed
    {
        var c = new AxisCal(); Assert.IsTrue(c.ApplyCapture(-1f, 1f, -1f, false));
        Assert.IsFalse(c.invert); Assert.IsTrue(c.hasRest);
        Assert.AreEqual(0f, c.Normalize(-1f, false), 1e-5); Assert.AreEqual(0.5f, c.Normalize(0f, false), 1e-5); Assert.AreEqual(1f, c.Normalize(1f, false), 1e-5);
        Assert.AreEqual(0f, c.Normalize(-1.2f, false), 1e-5); Assert.AreEqual(1f, c.Normalize(1.2f, false), 1e-5);   // clamped
    }
    [Test] public void Calibration_InvertedBipolarPedal()     // +1 released .. -1 pressed
    {
        var c = new AxisCal(); Assert.IsTrue(c.ApplyCapture(-1f, 1f, 1f, false));
        Assert.IsTrue(c.invert);
        Assert.AreEqual(0f, c.Normalize(1f, false), 1e-5); Assert.AreEqual(0.25f, c.Normalize(0.5f, false), 1e-5); Assert.AreEqual(1f, c.Normalize(-1f, false), 1e-5);
    }
    [Test] public void Calibration_InvertedUnipolar_And_RestOffset()
    {
        var c = new AxisCal(); Assert.IsTrue(c.ApplyCapture(0f, 1f, 1f, false)); Assert.IsTrue(c.invert);
        Assert.AreEqual(0f, c.Normalize(1f, false), 1e-5); Assert.AreEqual(1f, c.Normalize(0f, false), 1e-5);
        var r = new AxisCal(); Assert.IsTrue(r.ApplyCapture(-1f, 1f, -0.9f, false));   // rest slightly off the end: rest still reads 0
        Assert.AreEqual(0f, r.Normalize(-0.9f, false), 1e-5); Assert.AreEqual(0f, r.Normalize(-1f, false), 1e-5); Assert.AreEqual(1f, r.Normalize(1f, false), 1e-5);
        r.deadzone = 0.1f; Assert.AreEqual(0f, r.Normalize(-0.8f, false), 1e-5);
    }
    [Test] public void Calibration_NoTravel_KeepsPrevious()
    {
        var c = new AxisCal { min = -1f, max = 1f }; Assert.IsFalse(c.ApplyCapture(-1f, -0.99f, -1f, false)); Assert.IsFalse(c.calibrated); Assert.IsFalse(c.hasRest);
    }
    [Test] public void Calibration_WheelAndCombinedAxis_UseCentre()
    {
        var w = new AxisCal(); w.ApplyCapture(-1f, 1f, 0.02f, true); Assert.IsFalse(w.hasRest); Assert.AreEqual(0f, w.Normalize(0.02f, true), 1e-5); Assert.AreEqual(1f, w.Normalize(1f, true), 1e-5);
        var h = new AxisCal { half = AxisHalf.Positive }; h.ApplyCapture(-1f, 1f, 0f, false); Assert.IsFalse(h.hasRest); Assert.AreEqual(0.5f, h.Normalize(0.5f, false), 1e-5);
    }
    BindingProfile MultiDevice()
    {
        var p = InputMapper.Defaults();
        p.Get(InAction.Steer).Add(InputMapper.FromControl(A(wheel, "stick/x"), true));
        var thr = InputMapper.FromControl(A(pedals, "stick/y"), true); thr.cal = new AxisCal { min = -1f, max = 1f, center = 0f, half = AxisHalf.Positive }; p.Get(InAction.Throttle).Add(thr);
        var brk = InputMapper.FromControl(A(pedals, "stick/y"), true); brk.cal = new AxisCal { min = -1f, max = 1f, center = 0f, half = AxisHalf.Negative }; p.Get(InAction.Brake).Add(brk);
        p.Get(InAction.Gear1).Add(InputMapper.FromControl(shifter.TryGetChildControl("trigger"), true));
        return p;
    }
    [Test] public void MultiDevice_Wheel_Pedals_Shifter_Gamepad()
    {
        var m = NM(MultiDevice());
        Assert.AreEqual(SteerSource.Wheel, m.profile.Get(InAction.Steer)[^1].steerSource);
        Assert.AreEqual("BrandA Wheel 900", m.profile.Get(InAction.Steer)[^1].product);
        Set(A(pedals, "stick/y"), 0.8f); Assert.AreEqual(0.8f, m.Axis(InAction.Throttle), 1e-3); Assert.AreEqual(0f, m.Axis(InAction.Brake), 1e-3);
        Set(A(pedals, "stick/y"), -0.5f); Assert.AreEqual(0.5f, m.Axis(InAction.Brake), 1e-3); Assert.AreEqual(0f, m.Axis(InAction.Throttle), 1e-3);
        Press((ButtonControl)shifter.TryGetChildControl("trigger")); Assert.IsTrue(m.HeldFromNonKeyboard(InAction.Gear1));
        Set(pad.rightTrigger, 1f); Assert.AreEqual(1f, m.Axis(InAction.Throttle), 1e-3);   // gamepad default binding coexists (max of slots)
        m.Dispose();
    }
    [Test] public void Steering_1to1_SoftLock_And_Scaled()
    {
        var m = NM(MultiDevice()); m.profile.wheelRotationDeg = 900f;
        Set(A(wheel, "stick/x"), 1f);
        float hw = m.StepSteer(0.01f, 630f, 90f, 17.32f); Assert.AreEqual(450f, hw, 0.5f);          // 900 deg wheel: +-450 deg column, 1:1
        Assert.AreEqual(25.98f, hw / 17.32f, 0.05f);                                               // road wheel at the user's soft lock
        m.profile.mapping = SteerMapping.ScaleToCarLock; Assert.AreEqual(630f, m.StepSteer(0.01f, 630f, 90f, 17.32f), 0.5f);
        m.profile.wheelRotationDeg = 0f; m.profile.mapping = SteerMapping.RealColumn1to1; Assert.AreEqual(630f, m.StepSteer(0.01f, 630f, 90f, 17.32f), 0.5f);   // default = car's 1260 deg
        m.profile.mapping = SteerMapping.RealColumn1to1; m.profile.wheelRotationDeg = 1440f; Assert.AreEqual(630f, m.StepSteer(0.01f, 630f, 90f, 17.32f), 0.5f); Assert.AreEqual(720f, m.wheelDegUnclamped, 0.5f);   // rack stop; FFB soft lock sees overtravel
        m.Dispose();
    }
    [Test] public void Capture_Finds_Moved_Axis_On_Any_Device()
    {
        var m = NM(InputMapper.Defaults()); m.BeginCapture();
        Set(A(pedals, "stick/y"), 0.9f);
        var c = m.PollCapture(true); Assert.NotNull(c); Assert.AreEqual(pedals, c.device); Assert.AreEqual("y", c.name);
        m.Dispose();
    }
    // ---------- gear-up spam regression (pedal axis resting at -1/+1 captured as a button) ----------
    [Test] public void Capture_ButtonAction_Ignores_Axis_Resting_Deflected_And_Held_Buttons()
    {
        var y = A(pedals, "stick/y"); Set(y, 1f);                                      // pedal resting at +1 (inverted Heusinkveld style)
        Press((ButtonControl)shifter.TryGetChildControl("trigger"));                   // a button already held when capture starts
        var m = NM(InputMapper.Defaults()); m.BeginCapture();
        Set(y, 0.98f); InputSystem.Update();                                          // jitter at rest
        Assert.IsNull(m.PollCapture(false), "deflected-at-rest axis / held button must not bind");
        Press(WheelBtn); var c = m.PollCapture(false); Assert.AreEqual(WheelBtn, c, "a fresh button press binds");
        m.Dispose();
    }
    [Test] public void Capture_ButtonAction_From_Pedal_Uses_Rest_Baseline_No_Spam()
    {
        var y = A(pedals, "stick/y"); Set(y, -1f);                                     // rests at -1
        var m = NM(InputMapper.Defaults()); m.BeginCapture();
        Set(y, 1f); var c = m.PollCapture(false); Assert.AreEqual(y, c);
        var b = m.CompleteCapture(InAction.ShiftUp, c); Assert.IsTrue(b.cal.hasRest); Assert.AreEqual(-1f, b.cal.rest, 1e-5);
        Set(y, -1f); InputSystem.Update(); m.Pressed(InAction.ShiftUp);
        int fires = 0;
        for (int i = 0; i < 30; i++) { Set(y, -1f + 0.02f * (i % 3)); if (m.Pressed(InAction.ShiftUp)) fires++; }   // resting with noise
        Assert.AreEqual(0, fires, "rest must never fire");
        Set(y, 1f); Assert.IsTrue(m.Pressed(InAction.ShiftUp));
        for (int i = 0; i < 30; i++) { Set(y, (i % 2 == 0) ? 0.0f : 0.6f); if (m.Pressed(InAction.ShiftUp)) fires++; }   // held, wobbling around the middle
        Assert.AreEqual(0, fires, "held/wobbling must not repeat (hysteresis + release required)");
        Set(y, -1f); Assert.IsFalse(m.Pressed(InAction.ShiftUp)); Set(y, 1f); Assert.IsTrue(m.Pressed(InAction.ShiftUp), "release then press fires again");
        m.Dispose();
    }
    [Test] public void Legacy_Axis_On_Button_Action_Reads_Relative_To_Rest()
    {
        var y = A(pedals, "stick/y"); Set(y, 1f);                                      // old saved profile: pedal axis on ShiftUp, no rest recorded, resting at +1
        var p = InputMapper.Defaults(); p.Get(InAction.ShiftUp).Add(InputMapper.FromControl(y, true)); var m = NM(p);
        int fires = 0; for (int i = 0; i < 20; i++) { Set(y, 1f - 0.03f * (i % 2)); if (m.Pressed(InAction.ShiftUp)) fires++; }
        Assert.AreEqual(0, fires);
        m.Dispose();
    }
    [Test] public void Button_Held_Fires_Once()
    {
        var m = WheelShiftUp(); m.Pressed(InAction.ShiftUp); Press(WheelBtn); Assert.IsTrue(m.Pressed(InAction.ShiftUp));
        for (int i = 0; i < 50; i++) { InputSystem.Update(); Assert.IsFalse(m.Pressed(InAction.ShiftUp)); }
        m.Dispose();
    }
    [Test] public void Escape_Always_Works_And_Clear_Bindings()
    {
        Assert.AreEqual(InputRouter.EscResult.CancelCapture, InputRouter.EscapeAction(true, true, true, true));
        Assert.AreEqual(InputRouter.EscResult.CancelCalibration, InputRouter.EscapeAction(true, false, true, true));
        Assert.AreEqual(InputRouter.EscResult.CloseMenu, InputRouter.EscapeAction(true, false, false, true));
        Assert.AreEqual(InputRouter.EscResult.OpenMenu, InputRouter.EscapeAction(true, false, false, false));
        Assert.AreEqual(InputRouter.EscResult.None, InputRouter.EscapeAction(false, true, false, true));
        var kb = InputSystem.AddDevice<Keyboard>(); var m = WheelShiftUp(); m.BeginCapture(); Press(kb.escapeKey);
        Assert.IsNull(m.PollCapture(false), "Esc never gets bound");
        int dev = m.profile.Get(InAction.ShiftUp).FindAll(b => b.layout != "Keyboard").Count; Assert.GreaterOrEqual(dev, 1); Assert.AreEqual(dev, m.ClearDeviceBindings(InAction.ShiftUp)); Assert.IsTrue(m.profile.Get(InAction.ShiftUp).TrueForAll(b => b.layout == "Keyboard")); Assert.IsNotEmpty(m.profile.Get(InAction.ShiftUp));
        m.Dispose();
    }
    // ---------- debounce (Simucube 2 paddles chattering 0/1 while held) ----------
    InputMapper Chattery(float ms) { now = 0; InputMapper.Clock = () => now; var p = InputMapper.Defaults(); p.debounceMs = ms; p.Get(InAction.ShiftUp).Add(InputMapper.FromControl(WheelBtn, true)); var m = new InputMapper(p); m.Pressed(InAction.ShiftUp); return m; }
    int Step(InputMapper m, bool down, double dtMs) { now += dtMs * 1e-3; if (down) Press(WheelBtn); else Release(WheelBtn); return m.Pressed(InAction.ShiftUp) ? 1 : 0; }
    [Test] public void Debounce_Chatter_While_Held_Fires_Exactly_Once()
    {
        var m = Chattery(8f); int fires = 0;
        fires += Step(m, true, 1); Assert.AreEqual(0, fires, "not before stable for 8 ms");
        fires += Step(m, true, 4); fires += Step(m, true, 5); Assert.AreEqual(1, fires, "registers after 8 ms stable");
        for (int i = 0; i < 200; i++) fires += Step(m, i % 3 != 0, 2);    // held with 0/1 twitches every few ms (gaps of 2 ms)
        Assert.AreEqual(1, fires, "chatter while held must not re-shift");
        Assert.Greater(m.profile.Get(InAction.ShiftUp)[^1].chatter, 50, "chatter counted");
        for (int i = 0; i < 6; i++) fires += Step(m, false, 2); Assert.IsFalse(m.Held(InAction.ShiftUp), "released after stable 0");
        for (int i = 0; i < 6; i++) fires += Step(m, true, 2); Assert.AreEqual(2, fires, "second physical press = second shift");
        m.Dispose();
    }
    [Test] public void Debounce_Release_Chatter_And_Short_Glitch_Ignored()
    {
        var m = Chattery(8f); int fires = 0;
        fires += Step(m, true, 3); fires += Step(m, false, 3); fires += Step(m, true, 3); fires += Step(m, false, 3);   // contact bounce shorter than 8 ms, ends released
        for (int i = 0; i < 5; i++) fires += Step(m, false, 3);
        Assert.AreEqual(0, fires, "glitches shorter than the debounce time never shift");
        for (int i = 0; i < 5; i++) fires += Step(m, true, 3);
        for (int i = 0; i < 20; i++) fires += Step(m, i % 2 == 0, 1);                                                  // release bounce
        for (int i = 0; i < 10; i++) fires += Step(m, false, 3);
        Assert.AreEqual(1, fires);
        m.Dispose();
    }
    [Test] public void Debounce_Uses_Real_Time_Not_Update_Count_And_Is_Saved()
    {
        var m = Chattery(20f); int fires = 0;
        for (int i = 0; i < 30; i++) fires += Step(m, true, 0.5);   // 30 updates (1 kHz-ish) but only 15 ms of real time
        Assert.AreEqual(0, fires); fires += Step(m, true, 6); Assert.AreEqual(1, fires);
        m.profile.debounceMs = 13f; string path = System.IO.Path.Combine(Application.temporaryCachePath, "deb_test.json"); InputMapper.Save(m.profile, path);
        Assert.AreEqual(13f, InputMapper.LoadOrDefault(path).debounceMs, 1e-4); Assert.AreEqual(0f, new BindingProfile().debounceMs, 1e-4);
        m.Dispose();
    }
    [Test] public void Debounce_SubFrame_Chatter_Does_Not_Retrigger()   // release+press inside one input update while held (old 'tap' path re-fired)
    {
        var m = Chattery(8f); int fires = 0; fires += Step(m, true, 1); fires += Step(m, true, 10); Assert.AreEqual(1, fires);
        for (int i = 0; i < 20; i++)
        { now += 0.004; Release(WheelBtn, queueEventOnly: true); Press(WheelBtn, queueEventOnly: true); InputSystem.Update(); if (m.Pressed(InAction.ShiftUp)) fires++; }
        Assert.AreEqual(1, fires); Assert.Greater(m.profile.Get(InAction.ShiftUp)[^1].chatter, 10);
        m.Dispose();
    }
    [Test] public void Persistence_And_Reconnect_By_Product()
    {
        string path = System.IO.Path.Combine(Application.temporaryCachePath, "bind_test.json");
        InputMapper.Save(MultiDevice(), path);
        InputSystem.RemoveDevice(pedals);
        var m = NM(InputMapper.LoadOrDefault(path));
        Assert.AreEqual(0f, m.Axis(InAction.Throttle), 1e-5);                                     // pedals missing -> no input, no crash
        pedals = InputSystem.AddDevice(new InputDeviceDescription { product = "BrandB Pedals" });  // reconnect (new device instance)
        Set(A(pedals, "stick/y"), 0.7f); Assert.AreEqual(0.7f, m.Axis(InAction.Throttle), 1e-3);
        m.Dispose();
    }
    // ---------- menu / binding-capture regressions (wheel button bound to Gear Up flipped tabs and queued 4 upshifts) ----------
    ButtonControl WheelBtn => (ButtonControl)wheel.TryGetChildControl("trigger");
    InputMapper WheelShiftUp() { var p = InputMapper.Defaults(); p.Get(InAction.ShiftUp).Add(InputMapper.FromControl(WheelBtn, true)); return NM(p); }
    [Test] public void Menu_Suppresses_Gameplay_And_Discards_Queued_Presses()
    {
        var m = WheelShiftUp(); m.Pressed(InAction.ShiftUp);
        m.MenuOpen = true;
        for (int i = 0; i < 4; i++) { Press(WheelBtn); Assert.IsFalse(m.Pressed(InAction.ShiftUp), "shift while menu open"); Release(WheelBtn); Assert.IsFalse(m.Pressed(InAction.ShiftUp)); }
        Press(WheelBtn); m.Pressed(InAction.ShiftUp); InputSystem.Update();
        m.MenuOpen = false;                                                         // closed with the button still held
        Assert.IsFalse(m.Pressed(InAction.ShiftUp), "queued/held press fired on close"); InputSystem.Update(); Assert.IsFalse(m.Pressed(InAction.ShiftUp));
        Release(WheelBtn); Assert.IsFalse(m.Pressed(InAction.ShiftUp));
        Press(WheelBtn); Assert.IsTrue(m.Pressed(InAction.ShiftUp), "fresh press after close");
        InputSystem.Update(); Assert.IsFalse(m.Pressed(InAction.ShiftUp), "held must not repeat");
        m.Dispose();
    }
    [Test] public void Menu_Tab_Navigation_Ignores_Gameplay_Buttons_And_Is_EdgeTriggered()
    {
        var kb = InputSystem.AddDevice<Keyboard>();
        var m = WheelShiftUp(); m.profile.Get(InAction.MenuTabNext).Add(InputMapper.FromControl(WheelBtn, true));   // same wheel button also on tab-next
        var free = (ButtonControl)shifter.TryGetChildControl("trigger"); m.profile.Get(InAction.MenuTabPrev).Add(InputMapper.FromControl(free, true)); m.MarkDirty();
        m.MenuOpen = true; m.Pressed(InAction.MenuTabNext); m.Pressed(InAction.MenuTabPrev);
        Press(WheelBtn); Assert.IsFalse(m.Pressed(InAction.MenuTabNext), "gameplay-bound button switched tab"); Release(WheelBtn); Assert.IsFalse(m.Pressed(InAction.MenuTabNext));
        int tabs = 0; Press(kb.pageDownKey); if (m.Pressed(InAction.MenuTabNext)) tabs++; Assert.IsFalse(m.Pressed(InAction.MenuTabNext), "same-update double query");
        for (int i = 0; i < 30; i++) { InputSystem.Update(); if (m.Pressed(InAction.MenuTabNext)) tabs++; }   // held for 30 frames
        Assert.AreEqual(1, tabs, "one press = one tab"); Release(kb.pageDownKey); Assert.IsFalse(m.Pressed(InAction.MenuTabNext));
        Press(free); Assert.IsTrue(m.Pressed(InAction.MenuTabPrev), "non-gameplay wheel button may switch tabs"); InputSystem.Update(); Assert.IsFalse(m.Pressed(InAction.MenuTabPrev));
        m.Dispose();
    }
    [Test] public void Capture_Finishes_On_First_Press_And_Consumes_It()
    {
        var m = NM(InputMapper.Defaults()); m.profile.Get(InAction.MenuSelect).Add(InputMapper.FromControl(WheelBtn, true)); m.MarkDirty();
        m.MenuOpen = true; m.Pressed(InAction.MenuSelect); m.BeginCapture();
        Press(WheelBtn); var c = m.PollCapture(false); Assert.AreEqual(WheelBtn, c);
        int before = m.profile.Get(InAction.ShiftUp).Count; m.CompleteCapture(InAction.ShiftUp, c);
        Assert.AreEqual(before + 1, m.profile.Get(InAction.ShiftUp).Count, "bound exactly once");
        Assert.IsFalse(m.Pressed(InAction.MenuSelect), "capture press leaked to another action"); Assert.IsFalse(m.Pressed(InAction.MenuTabNext));
        InputSystem.Update(); Assert.IsFalse(m.Pressed(InAction.MenuSelect));
        m.MenuOpen = false; Assert.IsFalse(m.Pressed(InAction.ShiftUp), "capture press shifted after close");
        Release(WheelBtn); Press(WheelBtn); Assert.IsTrue(m.Pressed(InAction.ShiftUp), "new binding works");
        m.CompleteCapture(InAction.ShiftUp, c); Assert.AreEqual(before + 1, m.profile.Get(InAction.ShiftUp).Count, "re-capture of same control does not duplicate");
        m.Dispose();
    }
    [Test] public void Defaults_Tab_Keys_Not_Shared_With_Gameplay_And_Migration()
    {
        var m = NM(InputMapper.Defaults()); InputSystem.AddDevice<Keyboard>(); m.MarkDirty();
        var gp = m.GameplayControls(); foreach (var a in new[] { InAction.MenuTabNext, InAction.MenuTabPrev }) foreach (var b in m.profile.Get(a)) { Assert.NotNull(b.resolved); Assert.IsFalse(gp.Contains(b.resolved), b.Label); }
        var old = new BindingProfile { version = 3 }; old.Get(InAction.ShiftUp).Add(new Binding { layout = "Keyboard", control = "e" }); InputMapper.Migrate(old);
        Assert.AreEqual(6, old.version); Assert.Greater(old.Get(InAction.MenuTabNext).Count, 0); Assert.Greater(old.Get(InAction.MenuTabPrev).Count, 0);
        m.Dispose();
    }
    [Test] public void ResetToStart_DefaultBackspace_AndMigratedIntoOldProfiles()
    {
        var d = InputMapper.Defaults(); Assert.IsTrue(d.Get(InAction.ResetToStart).Exists(b => b.layout == "Keyboard" && b.control == "backspace"));
        Assert.IsTrue(d.Get(InAction.Reset).Count > 0, "R reset kept");
        var old = new BindingProfile { version = 4 }; InputMapper.Migrate(old);
        Assert.IsTrue(old.Get(InAction.ResetToStart).Exists(b => b.control == "backspace")); Assert.AreEqual(6, old.version);
        Assert.IsTrue(d.hudVisible, "info HUD visible by default");
        var q = JsonUtility.FromJson<BindingProfile>(JsonUtility.ToJson(new BindingProfile { hudVisible = false })); Assert.IsFalse(q.hudVisible, "H hide setting persists");
    }
}
