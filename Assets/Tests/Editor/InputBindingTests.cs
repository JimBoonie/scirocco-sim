// Virtual-device tests (Input System InputTestFixture): wheel, pedals and shifter from different "brands" + a gamepad.
using NUnit.Framework; using UnityEngine; using UnityEngine.InputSystem; using UnityEngine.InputSystem.Controls; using UnityEngine.InputSystem.Layouts;
public class InputBindingTests : InputTestFixture
{
    InputDevice wheel, pedals, shifter; Gamepad pad;
    // @description - Registers fake layouts for a wheel, pedals and a shifter from three different brands, then adds one device of each plus a gamepad, before every test.
    // @return {Void} - nothing
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
    // @description - Creates an InputMapper with debounce switched off, so the edge-logic tests are not affected by it (debounce tests set it explicitly).
    // @param {BindingProfile} profile - binding profile to use; its debounce time is set to 0 ms
    // @return {InputMapper} - the mapper reading that profile
    static InputMapper CreateMapperWithoutDebounce(BindingProfile profile) { profile.debounceMs = 0f; return new InputMapper(profile); }   // edge-logic tests: debounce off (chatterCount tests below set it)
    double now;   // manual clock in seconds for the debounce tests
    // @description - Restores the real-time clock of InputMapper after each test, then lets the base fixture restore the input system.
    // @return {Void} - nothing
    [TearDown] public override void TearDown() { InputMapper.Clock = () => Time.realtimeSinceStartupAsDouble; base.TearDown(); }
    // @description - Gets an axis control of a device by path, for setting test values.
    // @param {InputDevice} device - device that owns the control
    // @param {string} path - child control path such as 'stick/y'
    // @return {AxisControl} - the axis control
    static AxisControl GetAxis(InputDevice device, string path) => (AxisControl)device.TryGetChildControl(path);

    // @description - Verifies AxisCalibration.Normalize: a -1..+1 pedal maps -1 to 0 and +1 to 1, inversion flips it, a half-range calibration (negative or positive half) and a centered 0..1 wheel axis normalize as expected, and the deadzone rescales the remainder.
    // @return {Void} - nothing
    [Test] public void Calibration_Math()
    {
        var calibration = new AxisCalibration { min = -1f, max = 1f };                       // pedal reporting -1 released .. +1 pressed
        Assert.AreEqual(0f, calibration.Normalize(-1f, false), 1e-5); Assert.AreEqual(0.5f, calibration.Normalize(0f, false), 1e-5); Assert.AreEqual(1f, calibration.Normalize(1f, false), 1e-5);
        calibration.invert = true; Assert.AreEqual(1f, calibration.Normalize(-1f, false), 1e-5);  // inverted pedal (released = +1)
        var halfCalibration = new AxisCalibration { min = -1f, max = 1f, center = 0f, half = AxisHalf.Negative }; Assert.AreEqual(0.6f, halfCalibration.Normalize(-0.6f, false), 1e-5); Assert.AreEqual(0f, halfCalibration.Normalize(0.6f, false), 1e-5);
        var wheelCalibration = new AxisCalibration { min = 0f, max = 1f, center = 0.5f }; Assert.AreEqual(-1f, wheelCalibration.Normalize(0f, true), 1e-5); Assert.AreEqual(0.5f, wheelCalibration.Normalize(0.75f, true), 1e-5);   // 0..1 wheel axis
        var deadzoneCalibration = new AxisCalibration { min = 0f, max = 1f, deadzone = 0.1f }; Assert.AreEqual(0f, deadzoneCalibration.Normalize(0.05f, false), 1e-5); Assert.AreEqual(0.5f, deadzoneCalibration.Normalize(0.55f, false), 1e-5);
    }
    // @description - Verifies that capturing a pedal that reports -1 released to +1 pressed is not inverted, records its rest value, normalizes to 0..1 and clamps readings beyond the captured range.
    // @return {Void} - nothing
    [Test] public void Calibration_FullRangeBipolarPedal()   // Heusinkveld Sprint on Windows: -1 released .. +1 pressed
    {
        var calibration = new AxisCalibration(); Assert.IsTrue(calibration.ApplyCapture(-1f, 1f, -1f, false));
        Assert.IsFalse(calibration.invert); Assert.IsTrue(calibration.hasRest);
        Assert.AreEqual(0f, calibration.Normalize(-1f, false), 1e-5); Assert.AreEqual(0.5f, calibration.Normalize(0f, false), 1e-5); Assert.AreEqual(1f, calibration.Normalize(1f, false), 1e-5);
        Assert.AreEqual(0f, calibration.Normalize(-1.2f, false), 1e-5); Assert.AreEqual(1f, calibration.Normalize(1.2f, false), 1e-5);   // clamped
    }
    // @description - Verifies that capturing a pedal that reports +1 released to -1 pressed sets invert and normalizes it so released is 0 and fully pressed is 1.
    // @return {Void} - nothing
    [Test] public void Calibration_InvertedBipolarPedal()     // +1 released .. -1 pressed
    {
        var calibration = new AxisCalibration(); Assert.IsTrue(calibration.ApplyCapture(-1f, 1f, 1f, false));
        Assert.IsTrue(calibration.invert);
        Assert.AreEqual(0f, calibration.Normalize(1f, false), 1e-5); Assert.AreEqual(0.25f, calibration.Normalize(0.5f, false), 1e-5); Assert.AreEqual(1f, calibration.Normalize(-1f, false), 1e-5);
    }
    // @description - Verifies inverted 0..1 pedal capture, and that a pedal resting slightly off its end (-0.9) still reads 0 at rest, 0 at the end and 1 fully pressed, with the deadzone applied.
    // @return {Void} - nothing
    [Test] public void Calibration_InvertedUnipolar_And_RestOffset()
    {
        var calibration = new AxisCalibration(); Assert.IsTrue(calibration.ApplyCapture(0f, 1f, 1f, false)); Assert.IsTrue(calibration.invert);
        Assert.AreEqual(0f, calibration.Normalize(1f, false), 1e-5); Assert.AreEqual(1f, calibration.Normalize(0f, false), 1e-5);
        var restOffsetCalibration = new AxisCalibration(); Assert.IsTrue(restOffsetCalibration.ApplyCapture(-1f, 1f, -0.9f, false));   // rest slightly off the end: rest still reads 0
        Assert.AreEqual(0f, restOffsetCalibration.Normalize(-0.9f, false), 1e-5); Assert.AreEqual(0f, restOffsetCalibration.Normalize(-1f, false), 1e-5); Assert.AreEqual(1f, restOffsetCalibration.Normalize(1f, false), 1e-5);
        restOffsetCalibration.deadzone = 0.1f; Assert.AreEqual(0f, restOffsetCalibration.Normalize(-0.8f, false), 1e-5);
    }
    // @description - Verifies that a capture with almost no travel (-1 to -0.99) is rejected and leaves the calibration unchanged and not calibrated.
    // @return {Void} - nothing
    [Test] public void Calibration_NoTravel_KeepsPrevious()
    {
        var calibration = new AxisCalibration { min = -1f, max = 1f }; Assert.IsFalse(calibration.ApplyCapture(-1f, -0.99f, -1f, false)); Assert.IsFalse(calibration.calibrated); Assert.IsFalse(calibration.hasRest);
    }
    // @description - Verifies that capturing a wheel axis or a combined half-range axis records no rest position and normalizes relative to its center.
    // @return {Void} - nothing
    [Test] public void Calibration_WheelAndCombinedAxis_UseCenter()
    {
        var wheelCalibration = new AxisCalibration(); wheelCalibration.ApplyCapture(-1f, 1f, 0.02f, true); Assert.IsFalse(wheelCalibration.hasRest); Assert.AreEqual(0f, wheelCalibration.Normalize(0.02f, true), 1e-5); Assert.AreEqual(1f, wheelCalibration.Normalize(1f, true), 1e-5);
        var halfCalibration = new AxisCalibration { half = AxisHalf.Positive }; halfCalibration.ApplyCapture(-1f, 1f, 0f, false); Assert.IsFalse(halfCalibration.hasRest); Assert.AreEqual(0.5f, halfCalibration.Normalize(0.5f, false), 1e-5);
    }
    // @description - Builds a profile with steering on a wheel axis, throttle and brake sharing one pedal axis (positive and negative halves) and gear 1 on a shifter button, the layout used by the multi-device tests.
    // @return {BindingProfile} - profile with the multi-device bindings
    BindingProfile MultiDevice()
    {
        var profile = InputMapper.Defaults();
        profile.Get(InAction.Steer).Add(InputMapper.FromControl(GetAxis(wheel, "stick/x"), true));
        var throttleBinding = InputMapper.FromControl(GetAxis(pedals, "stick/y"), true); throttleBinding.calibration = new AxisCalibration { min = -1f, max = 1f, center = 0f, half = AxisHalf.Positive }; profile.Get(InAction.Throttle).Add(throttleBinding);
        var brakeBinding = InputMapper.FromControl(GetAxis(pedals, "stick/y"), true); brakeBinding.calibration = new AxisCalibration { min = -1f, max = 1f, center = 0f, half = AxisHalf.Negative }; profile.Get(InAction.Brake).Add(brakeBinding);
        profile.Get(InAction.Gear1).Add(InputMapper.FromControl(shifter.TryGetChildControl("trigger"), true));
        return profile;
    }
    // @description - Verifies that bindings across a wheel, a combined pedal axis, a shifter button and the default gamepad all work at once: wheel steer source and product are stored, the pedal axis splits into throttle and brake, the shifter reads as non-keyboard input, and the gamepad trigger coexists with the pedals.
    // @return {Void} - nothing
    [Test] public void MultiDevice_Wheel_Pedals_Shifter_Gamepad()
    {
        var mapper = CreateMapperWithoutDebounce(MultiDevice());
        Assert.AreEqual(SteerSource.Wheel, mapper.profile.Get(InAction.Steer)[^1].steerSource);
        Assert.AreEqual("BrandA Wheel 900", mapper.profile.Get(InAction.Steer)[^1].product);
        Set(GetAxis(pedals, "stick/y"), 0.8f); Assert.AreEqual(0.8f, mapper.Axis(InAction.Throttle), 1e-3); Assert.AreEqual(0f, mapper.Axis(InAction.Brake), 1e-3);
        Set(GetAxis(pedals, "stick/y"), -0.5f); Assert.AreEqual(0.5f, mapper.Axis(InAction.Brake), 1e-3); Assert.AreEqual(0f, mapper.Axis(InAction.Throttle), 1e-3);
        Press((ButtonControl)shifter.TryGetChildControl("trigger")); Assert.IsTrue(mapper.HeldFromNonKeyboard(InAction.Gear1));
        Set(pad.rightTrigger, 1f); Assert.AreEqual(1f, mapper.Axis(InAction.Throttle), 1e-3);   // gamepad default binding coexists (max of slots)
        mapper.Dispose();
    }
    // @description - Verifies the steering mappings: a 900 deg wheel gives 450 deg of column angle 1:1 (about 26 deg at the road wheel), scale-to-car-lock reaches the full 630 deg, the default car range gives 630 deg, and a 1440 deg wheel stops at the rack while the unclamped value reports 720 deg of overtravel.
    // @return {Void} - nothing
    [Test] public void Steering_1to1_SoftLock_And_Scaled()
    {
        var mapper = CreateMapperWithoutDebounce(MultiDevice()); mapper.profile.wheelRotationDeg = 900f;
        Set(GetAxis(wheel, "stick/x"), 1f);
        float handwheelDeg = mapper.StepSteer(0.01f, 630f, 90f, 17.32f); Assert.AreEqual(450f, handwheelDeg, 0.5f);          // 900 deg wheel: +-450 deg column, 1:1
        Assert.AreEqual(25.98f, handwheelDeg / 17.32f, 0.05f);                                               // road wheel at the user's soft lock
        mapper.profile.mapping = SteerMapping.ScaleToCarLock; Assert.AreEqual(630f, mapper.StepSteer(0.01f, 630f, 90f, 17.32f), 0.5f);
        mapper.profile.wheelRotationDeg = 0f; mapper.profile.mapping = SteerMapping.RealColumn1to1; Assert.AreEqual(630f, mapper.StepSteer(0.01f, 630f, 90f, 17.32f), 0.5f);   // default = car's 1260 deg
        mapper.profile.mapping = SteerMapping.RealColumn1to1; mapper.profile.wheelRotationDeg = 1440f; Assert.AreEqual(630f, mapper.StepSteer(0.01f, 630f, 90f, 17.32f), 0.5f); Assert.AreEqual(720f, mapper.wheelDegUnclamped, 0.5f);   // rack stop; FFB soft lock sees overtravel
        mapper.Dispose();
    }
    // @description - Verifies that binding capture picks the axis that moved (pedals Y at 0.9) and reports its device and name.
    // @return {Void} - nothing
    [Test] public void Capture_Finds_Moved_Axis_On_Any_Device()
    {
        var mapper = CreateMapperWithoutDebounce(InputMapper.Defaults()); mapper.BeginCapture();
        Set(GetAxis(pedals, "stick/y"), 0.9f);
        var capturedControl = mapper.PollCapture(true); Assert.NotNull(capturedControl); Assert.AreEqual(pedals, capturedControl.device); Assert.AreEqual("y", capturedControl.name);
        mapper.Dispose();
    }
    // ---------- gear-up spam regression (pedal axis resting at -1/+1 captured as a button) ----------
    // @description - Regression for gear-up spam: verifies that capturing a button binding ignores an axis resting deflected at +1 with jitter and a button already held when capture started, and binds only a fresh button press.
    // @return {Void} - nothing
    [Test] public void Capture_ButtonAction_Ignores_Axis_Resting_Deflected_And_Held_Buttons()
    {
        var pedalAxis = GetAxis(pedals, "stick/y"); Set(pedalAxis, 1f);                                      // pedal resting at +1 (inverted Heusinkveld style)
        Press((ButtonControl)shifter.TryGetChildControl("trigger"));                   // a button already held when capture starts
        var mapper = CreateMapperWithoutDebounce(InputMapper.Defaults()); mapper.BeginCapture();
        Set(pedalAxis, 0.98f); InputSystem.Update();                                          // jitter at rest
        Assert.IsNull(mapper.PollCapture(false), "deflected-at-rest axis / held button must not bind");
        Press(WheelButton); var capturedControl = mapper.PollCapture(false); Assert.AreEqual(WheelButton, capturedControl, "a fresh button press binds");
        mapper.Dispose();
    }
    // @description - Verifies that a pedal axis captured for a button action records its rest value (-1), never fires while resting with noise or wobbling around the middle while held, and fires again only after a release then a press.
    // @return {Void} - nothing
    [Test] public void Capture_ButtonAction_From_Pedal_Uses_Rest_Baseline_No_Spam()
    {
        var pedalAxis = GetAxis(pedals, "stick/y"); Set(pedalAxis, -1f);                                     // rests at -1
        var mapper = CreateMapperWithoutDebounce(InputMapper.Defaults()); mapper.BeginCapture();
        Set(pedalAxis, 1f); var capturedControl = mapper.PollCapture(false); Assert.AreEqual(pedalAxis, capturedControl);
        var boundBinding = mapper.CompleteCapture(InAction.ShiftUp, capturedControl); Assert.IsTrue(boundBinding.calibration.hasRest); Assert.AreEqual(-1f, boundBinding.calibration.rest, 1e-5);
        Set(pedalAxis, -1f); InputSystem.Update(); mapper.Pressed(InAction.ShiftUp);
        int fires = 0;
        for (int i = 0; i < 30; i++) { Set(pedalAxis, -1f + 0.02f * (i % 3)); if (mapper.Pressed(InAction.ShiftUp)) fires++; }   // resting with noise
        Assert.AreEqual(0, fires, "rest must never fire");
        Set(pedalAxis, 1f); Assert.IsTrue(mapper.Pressed(InAction.ShiftUp));
        for (int i = 0; i < 30; i++) { Set(pedalAxis, (i % 2 == 0) ? 0.0f : 0.6f); if (mapper.Pressed(InAction.ShiftUp)) fires++; }   // held, wobbling around the middle
        Assert.AreEqual(0, fires, "held/wobbling must not repeat (hysteresis + release required)");
        Set(pedalAxis, -1f); Assert.IsFalse(mapper.Pressed(InAction.ShiftUp)); Set(pedalAxis, 1f); Assert.IsTrue(mapper.Pressed(InAction.ShiftUp), "release then press fires again");
        mapper.Dispose();
    }
    // @description - Verifies that an old saved profile with a pedal axis on a button action and no recorded rest (resting at +1 with 3 % noise) does not fire.
    // @return {Void} - nothing
    [Test] public void Legacy_Axis_On_Button_Action_Reads_Relative_To_Rest()
    {
        var pedalAxis = GetAxis(pedals, "stick/y"); Set(pedalAxis, 1f);                                      // old saved profile: pedal axis on ShiftUp, no rest recorded, resting at +1
        var profile = InputMapper.Defaults(); profile.Get(InAction.ShiftUp).Add(InputMapper.FromControl(pedalAxis, true)); var mapper = CreateMapperWithoutDebounce(profile);
        int fires = 0; for (int i = 0; i < 20; i++) { Set(pedalAxis, 1f - 0.03f * (i % 2)); if (mapper.Pressed(InAction.ShiftUp)) fires++; }
        Assert.AreEqual(0, fires);
        mapper.Dispose();
    }
    // @description - Verifies that a held wheel button fires the shift action exactly once over 50 further input updates.
    // @return {Void} - nothing
    [Test] public void Button_Held_Fires_Once()
    {
        var mapper = WheelShiftUp(); mapper.Pressed(InAction.ShiftUp); Press(WheelButton); Assert.IsTrue(mapper.Pressed(InAction.ShiftUp));
        for (int i = 0; i < 50; i++) { InputSystem.Update(); Assert.IsFalse(mapper.Pressed(InAction.ShiftUp)); }
        mapper.Dispose();
    }
    // @description - Verifies the Escape key priority (cancel capture, then cancel calibration, then close menu, then open menu, nothing if not pressed), that Escape is never bound during capture, and that clearing device bindings removes all non-keyboard bindings of the action.
    // @return {Void} - nothing
    [Test] public void Escape_Always_Works_And_Clear_Bindings()
    {
        Assert.AreEqual(InputRouter.EscResult.CancelCapture, InputRouter.EscapeAction(true, true, true, true));
        Assert.AreEqual(InputRouter.EscResult.CancelCalibration, InputRouter.EscapeAction(true, false, true, true));
        Assert.AreEqual(InputRouter.EscResult.CloseMenu, InputRouter.EscapeAction(true, false, false, true));
        Assert.AreEqual(InputRouter.EscResult.OpenMenu, InputRouter.EscapeAction(true, false, false, false));
        Assert.AreEqual(InputRouter.EscResult.None, InputRouter.EscapeAction(false, true, false, true));
        var keyboard = InputSystem.AddDevice<Keyboard>(); var mapper = WheelShiftUp(); mapper.BeginCapture(); Press(keyboard.escapeKey);
        Assert.IsNull(mapper.PollCapture(false), "Esc never gets bound");
        int deviceBindingCount = mapper.profile.Get(InAction.ShiftUp).FindAll(binding => binding.layout != "Keyboard").Count; Assert.GreaterOrEqual(deviceBindingCount, 1); Assert.AreEqual(deviceBindingCount, mapper.ClearDeviceBindings(InAction.ShiftUp)); Assert.IsTrue(mapper.profile.Get(InAction.ShiftUp).TrueForAll(binding => binding.layout == "Keyboard")); Assert.IsNotEmpty(mapper.profile.Get(InAction.ShiftUp));
        mapper.Dispose();
    }
    // ---------- debounce (Simucube 2 paddles chattering 0/1 while held) ----------
    // @description - Creates a mapper whose shift-up button is the wheel button with the given debounce time and a manual clock starting at 0, so tests control time exactly.
    // @param {float} debounceMs - debounce time in milliseconds
    // @return {InputMapper} - the mapper
    InputMapper CreateChatteryMapper(float debounceMs) { now = 0; InputMapper.Clock = () => now; var profile = InputMapper.Defaults(); profile.debounceMs = debounceMs; profile.Get(InAction.ShiftUp).Add(InputMapper.FromControl(WheelButton, true)); var mapper = new InputMapper(profile); mapper.Pressed(InAction.ShiftUp); return mapper; }
    // @description - Advances the manual clock, presses or releases the wheel button and asks the mapper whether shift-up fired.
    // @param {InputMapper} mapper - mapper to query
    // @param {bool} isDown - true to press the wheel button, false to release it
    // @param {double} milliseconds - time to advance the manual clock in milliseconds
    // @return {int} - 1 if shift-up fired in this step, otherwise 0
    int StepDebounceClock(InputMapper mapper, bool isDown, double milliseconds) { now += milliseconds * 1e-3; if (isDown) Press(WheelButton); else Release(WheelButton); return mapper.Pressed(InAction.ShiftUp) ? 1 : 0; }
    // @description - Verifies with 8 ms debounce that a button fires only after being stable for 8 ms, fires once while 0/1 chatter continues with 2 ms gaps (and the chatter is counted), is released after a stable 0, and fires a second time for a second physical press.
    // @return {Void} - nothing
    [Test] public void Debounce_Chatter_While_Held_Fires_Exactly_Once()
    {
        var mapper = CreateChatteryMapper(8f); int fires = 0;
        fires += StepDebounceClock(mapper, true, 1); Assert.AreEqual(0, fires, "not before stable for 8 ms");
        fires += StepDebounceClock(mapper, true, 4); fires += StepDebounceClock(mapper, true, 5); Assert.AreEqual(1, fires, "registers after 8 ms stable");
        for (int i = 0; i < 200; i++) fires += StepDebounceClock(mapper, i % 3 != 0, 2);    // held with 0/1 twitches every few ms (gaps of 2 ms)
        Assert.AreEqual(1, fires, "chatter while held must not re-shift");
        Assert.Greater(mapper.profile.Get(InAction.ShiftUp)[^1].chatterCount, 50, "chatter counted");
        for (int i = 0; i < 6; i++) fires += StepDebounceClock(mapper, false, 2); Assert.IsFalse(mapper.Held(InAction.ShiftUp), "released after stable 0");
        for (int i = 0; i < 6; i++) fires += StepDebounceClock(mapper, true, 2); Assert.AreEqual(2, fires, "second physical press = second shift");
        mapper.Dispose();
    }
    // @description - Verifies that contact bounces shorter than the 8 ms debounce time never fire, and that bounce while releasing does not add extra shifts.
    // @return {Void} - nothing
    [Test] public void Debounce_Release_Chatter_And_Short_Glitch_Ignored()
    {
        var mapper = CreateChatteryMapper(8f); int fires = 0;
        fires += StepDebounceClock(mapper, true, 3); fires += StepDebounceClock(mapper, false, 3); fires += StepDebounceClock(mapper, true, 3); fires += StepDebounceClock(mapper, false, 3);   // contact bounce shorter than 8 ms, ends released
        for (int i = 0; i < 5; i++) fires += StepDebounceClock(mapper, false, 3);
        Assert.AreEqual(0, fires, "glitches shorter than the debounce time never shift");
        for (int i = 0; i < 5; i++) fires += StepDebounceClock(mapper, true, 3);
        for (int i = 0; i < 20; i++) fires += StepDebounceClock(mapper, i % 2 == 0, 1);                                                  // release bounce
        for (int i = 0; i < 10; i++) fires += StepDebounceClock(mapper, false, 3);
        Assert.AreEqual(1, fires);
        mapper.Dispose();
    }
    // @description - Verifies that debounce counts elapsed time, not input updates (30 updates in 15 ms do not fire a 20 ms debounce), and that the debounce setting is saved to and loaded from a file.
    // @return {Void} - nothing
    [Test] public void Debounce_Uses_Real_Time_Not_Update_Count_And_Is_Saved()
    {
        var mapper = CreateChatteryMapper(20f); int fires = 0;
        for (int i = 0; i < 30; i++) fires += StepDebounceClock(mapper, true, 0.5);   // 30 updates (1 kHz-ish) but only 15 ms of real time
        Assert.AreEqual(0, fires); fires += StepDebounceClock(mapper, true, 6); Assert.AreEqual(1, fires);
        mapper.profile.debounceMs = 13f; string path = System.IO.Path.Combine(Application.temporaryCachePath, "deb_test.json"); InputMapper.Save(mapper.profile, path);
        Assert.AreEqual(13f, InputMapper.LoadOrDefault(path).debounceMs, 1e-4); Assert.AreEqual(0f, new BindingProfile().debounceMs, 1e-4);
        mapper.Dispose();
    }
    // @description - Verifies that a release and press inside one input update while the button is held does not fire again and is counted as chatter.
    // @return {Void} - nothing
    [Test] public void Debounce_SubFrame_Chatter_Does_Not_Retrigger()   // release+press inside one input update while held (old 'tap' path re-fired)
    {
        var mapper = CreateChatteryMapper(8f); int fires = 0; fires += StepDebounceClock(mapper, true, 1); fires += StepDebounceClock(mapper, true, 10); Assert.AreEqual(1, fires);
        for (int i = 0; i < 20; i++)
        { now += 0.004; Release(WheelButton, queueEventOnly: true); Press(WheelButton, queueEventOnly: true); InputSystem.Update(); if (mapper.Pressed(InAction.ShiftUp)) fires++; }
        Assert.AreEqual(1, fires); Assert.Greater(mapper.profile.Get(InAction.ShiftUp)[^1].chatterCount, 10);
        mapper.Dispose();
    }
    // @description - Verifies that saved bindings survive removal and re-adding of a device: with the pedals removed the axis reads 0 without error, and after reconnecting a new device instance with the same product the binding works again.
    // @return {Void} - nothing
    [Test] public void Persistence_And_Reconnect_By_Product()
    {
        string path = System.IO.Path.Combine(Application.temporaryCachePath, "bind_test.json");
        InputMapper.Save(MultiDevice(), path);
        InputSystem.RemoveDevice(pedals);
        var mapper = CreateMapperWithoutDebounce(InputMapper.LoadOrDefault(path));
        Assert.AreEqual(0f, mapper.Axis(InAction.Throttle), 1e-5);                                     // pedals missing -> no input, no crash
        pedals = InputSystem.AddDevice(new InputDeviceDescription { product = "BrandB Pedals" });  // reconnect (new device instance)
        Set(GetAxis(pedals, "stick/y"), 0.7f); Assert.AreEqual(0.7f, mapper.Axis(InAction.Throttle), 1e-3);
        mapper.Dispose();
    }
    // ---------- menu / binding-capture regressions (wheel button bound to Gear Up flipped tabs and queued 4 upshifts) ----------
    ButtonControl WheelButton => (ButtonControl)wheel.TryGetChildControl("trigger");
    // @description - Creates a mapper with the wheel button bound to shift-up (debounce off).
    // @return {InputMapper} - the mapper
    InputMapper WheelShiftUp() { var profile = InputMapper.Defaults(); profile.Get(InAction.ShiftUp).Add(InputMapper.FromControl(WheelButton, true)); return CreateMapperWithoutDebounce(profile); }
    // @description - Verifies that while the menu is open gameplay buttons never fire, that a press held while the menu closes does not fire on close, that a fresh press afterwards fires once, and that holding does not repeat.
    // @return {Void} - nothing
    [Test] public void Menu_Suppresses_Gameplay_And_Discards_Queued_Presses()
    {
        var mapper = WheelShiftUp(); mapper.Pressed(InAction.ShiftUp);
        mapper.MenuOpen = true;
        for (int i = 0; i < 4; i++) { Press(WheelButton); Assert.IsFalse(mapper.Pressed(InAction.ShiftUp), "shift while menu open"); Release(WheelButton); Assert.IsFalse(mapper.Pressed(InAction.ShiftUp)); }
        Press(WheelButton); mapper.Pressed(InAction.ShiftUp); InputSystem.Update();
        mapper.MenuOpen = false;                                                         // closed with the button still held
        Assert.IsFalse(mapper.Pressed(InAction.ShiftUp), "queued/held press fired on close"); InputSystem.Update(); Assert.IsFalse(mapper.Pressed(InAction.ShiftUp));
        Release(WheelButton); Assert.IsFalse(mapper.Pressed(InAction.ShiftUp));
        Press(WheelButton); Assert.IsTrue(mapper.Pressed(InAction.ShiftUp), "fresh press after close");
        InputSystem.Update(); Assert.IsFalse(mapper.Pressed(InAction.ShiftUp), "held must not repeat");
        mapper.Dispose();
    }
    // @description - Verifies that a wheel button also bound to a gameplay action does not switch menu tabs, that a keyboard key switches exactly one tab per press even when held for 30 frames, and that a non-gameplay wheel button may switch tabs.
    // @return {Void} - nothing
    [Test] public void Menu_Tab_Navigation_Ignores_Gameplay_Buttons_And_Is_EdgeTriggered()
    {
        var keyboard = InputSystem.AddDevice<Keyboard>();
        var mapper = WheelShiftUp(); mapper.profile.Get(InAction.MenuTabNext).Add(InputMapper.FromControl(WheelButton, true));   // same wheel button also on tab-next
        var free = (ButtonControl)shifter.TryGetChildControl("trigger"); mapper.profile.Get(InAction.MenuTabPrev).Add(InputMapper.FromControl(free, true)); mapper.MarkDirty();
        mapper.MenuOpen = true; mapper.Pressed(InAction.MenuTabNext); mapper.Pressed(InAction.MenuTabPrev);
        Press(WheelButton); Assert.IsFalse(mapper.Pressed(InAction.MenuTabNext), "gameplay-bound button switched tab"); Release(WheelButton); Assert.IsFalse(mapper.Pressed(InAction.MenuTabNext));
        int tabs = 0; Press(keyboard.pageDownKey); if (mapper.Pressed(InAction.MenuTabNext)) tabs++; Assert.IsFalse(mapper.Pressed(InAction.MenuTabNext), "same-update double query");
        for (int i = 0; i < 30; i++) { InputSystem.Update(); if (mapper.Pressed(InAction.MenuTabNext)) tabs++; }   // held for 30 frames
        Assert.AreEqual(1, tabs, "one press = one tab"); Release(keyboard.pageDownKey); Assert.IsFalse(mapper.Pressed(InAction.MenuTabNext));
        Press(free); Assert.IsTrue(mapper.Pressed(InAction.MenuTabPrev), "non-gameplay wheel button may switch tabs"); InputSystem.Update(); Assert.IsFalse(mapper.Pressed(InAction.MenuTabPrev));
        mapper.Dispose();
    }
    // @description - Verifies that binding capture finishes on the first press and that press is consumed: it is bound exactly once, does not leak to the menu actions or fire shift-up after the menu closes, the new binding then works, and capturing the same control again does not add a duplicate.
    // @return {Void} - nothing
    [Test] public void Capture_Finishes_On_First_Press_And_Consumes_It()
    {
        var mapper = CreateMapperWithoutDebounce(InputMapper.Defaults()); mapper.profile.Get(InAction.MenuSelect).Add(InputMapper.FromControl(WheelButton, true)); mapper.MarkDirty();
        mapper.MenuOpen = true; mapper.Pressed(InAction.MenuSelect); mapper.BeginCapture();
        Press(WheelButton); var capturedControl = mapper.PollCapture(false); Assert.AreEqual(WheelButton, capturedControl);
        int before = mapper.profile.Get(InAction.ShiftUp).Count; mapper.CompleteCapture(InAction.ShiftUp, capturedControl);
        Assert.AreEqual(before + 1, mapper.profile.Get(InAction.ShiftUp).Count, "bound exactly once");
        Assert.IsFalse(mapper.Pressed(InAction.MenuSelect), "capture press leaked to another action"); Assert.IsFalse(mapper.Pressed(InAction.MenuTabNext));
        InputSystem.Update(); Assert.IsFalse(mapper.Pressed(InAction.MenuSelect));
        mapper.MenuOpen = false; Assert.IsFalse(mapper.Pressed(InAction.ShiftUp), "capture press shifted after close");
        Release(WheelButton); Press(WheelButton); Assert.IsTrue(mapper.Pressed(InAction.ShiftUp), "new binding works");
        mapper.CompleteCapture(InAction.ShiftUp, capturedControl); Assert.AreEqual(before + 1, mapper.profile.Get(InAction.ShiftUp).Count, "re-capture of same control does not duplicate");
        mapper.Dispose();
    }
    // @description - Verifies that the default menu tab bindings resolve to controls that no gameplay action uses, and that default tab-next and tab-previous bindings exist.
    // @return {Void} - nothing
    [Test] public void Defaults_Tab_Keys_Not_Shared_With_Gameplay()
    {
        var mapper = CreateMapperWithoutDebounce(InputMapper.Defaults()); InputSystem.AddDevice<Keyboard>(); mapper.MarkDirty();
        var gameplayControls = mapper.GameplayControls(); foreach (var action in new[] { InAction.MenuTabNext, InAction.MenuTabPrev }) foreach (var binding in mapper.profile.Get(action)) { Assert.NotNull(binding.resolved); Assert.IsFalse(gameplayControls.Contains(binding.resolved), binding.Label); }
        Assert.Greater(InputMapper.Defaults().Get(InAction.MenuTabNext).Count, 0); Assert.Greater(InputMapper.Defaults().Get(InAction.MenuTabPrev).Count, 0);
        mapper.Dispose();
    }
    // @description - Verifies that a saved settings file with another version number is discarded with a warning and defaults are used, and that a current-version file round-trips.
    // @return {Void} - nothing
    [Test] public void Settings_File_Of_Another_Version_Is_Discarded_And_Defaults_Used()
    {
        var old = InputMapper.Defaults(); old.version = BindingProfile.CurrentVersion - 1; old.audioVolume = 0.11f;
        string path = System.IO.Path.Combine(Application.temporaryCachePath, "other_version_bindings.json"); System.IO.File.WriteAllText(path, JsonUtility.ToJson(old));
        UnityEngine.TestTools.LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("settings version"));
        var loaded = InputMapper.LoadOrDefault(path);
        Assert.AreEqual(BindingProfile.CurrentVersion, loaded.version); Assert.AreEqual(InputMapper.Defaults().audioVolume, loaded.audioVolume, 1e-6);
        loaded.audioVolume = 0.22f; InputMapper.Save(loaded, path); Assert.AreEqual(0.22f, InputMapper.LoadOrDefault(path).audioVolume, 1e-6, "current-version files round-trip");
    }
    // @description - Verifies the default bindings: reset-to-start is on Backspace, the R reset is kept, the info HUD is visible by default, and the hidden-HUD setting persists through JSON.
    // @return {Void} - nothing
    [Test] public void ResetToStart_DefaultBackspace()
    {
        var defaults = InputMapper.Defaults(); Assert.IsTrue(defaults.Get(InAction.ResetToStart).Exists(binding => binding.layout == "Keyboard" && binding.control == "backspace"));
        Assert.IsTrue(defaults.Get(InAction.Reset).Count > 0, "R reset kept");
        Assert.IsTrue(defaults.hudVisible, "info HUD visible by default");
        var roundTripped = JsonUtility.FromJson<BindingProfile>(JsonUtility.ToJson(new BindingProfile { hudVisible = false })); Assert.IsFalse(roundTripped.hudVisible, "H hide setting persists");
    }

    // @description - 2026-10-14 regression (acdb0e1): with the gamepad assist object present (InputRouter always creates it) a device bound as 'wheel' never moved the column, assist on or off. Simulates the wheel axis with the router's assist wiring.
    // @param {InputMapper} mapper - mapper that receives the assist, its handwheel conversion (17.32 column degrees per road degree) and a 20 m/s steering context
    // @return {Void} - nothing
    static void WireAssist(InputMapper mapper)
    {
        mapper.assist = new GamepadSteering(mapper.profile.gamepadSteer); mapper.meanToHandwheel = mean => mean * 17.32f;
        mapper.assistCtx = new GamepadSteerContext { speed = 20f, lockRoadDeg = 630f / 17.32f, peakSlipDeg = 6f };
    }
    // @description - Verifies that a device bound as the wheel steers the column directly 1:1 (half right 315 deg, -0.25 gives -157.5 deg instantly, full gives the 630 deg rack stop) whether the gamepad assist is on or off, and that the assist state follows the wheel.
    // @return {Void} - nothing
    [Test] public void Wheel_Steers_Directly_With_Assist_On_And_Off()
    {
        foreach (bool assistOn in new[] { true, false })
        {
            var mapper = CreateMapperWithoutDebounce(MultiDevice()); mapper.profile.wheelRotationDeg = 0f; mapper.profile.mapping = SteerMapping.RealColumn1to1;   // 0 = car's 1260 deg
            mapper.profile.gamepadSteer.enabled = assistOn; WireAssist(mapper);
            Set(GetAxis(wheel, "stick/x"), 0.5f); Assert.AreEqual(315f, mapper.StepSteer(0.01f, 630f, 90f, 17.32f), 0.5f, $"half right, assist {assistOn}: 1:1 column");
            Set(GetAxis(wheel, "stick/x"), -0.25f); Assert.AreEqual(-157.5f, mapper.StepSteer(0.01f, 630f, 90f, 17.32f), 0.5f, $"instant, no rate limit / grip limit, assist {assistOn}");
            Set(GetAxis(wheel, "stick/x"), 1f); Assert.AreEqual(630f, mapper.StepSteer(0.01f, 630f, 90f, 17.32f), 0.5f, "full 1260 deg mapping reaches the rack stop");
            Assert.AreEqual(630f / 17.32f, mapper.assist.roadDeg, 0.1f, "assist state follows the wheel (bumpless)");
            mapper.Dispose();
        }
    }
    // @description - Verifies that the gamepad assist limits the stick steering rate and grip when enabled, and that with the assist off the stick turns the column at the plain rate limit.
    // @return {Void} - nothing
    [Test] public void Gamepad_Assist_Applies_Only_To_Stick()
    {
        var mapper = CreateMapperWithoutDebounce(InputMapper.Defaults()); WireAssist(mapper); mapper.profile.gamepadSteer.enabled = true;
        Set(pad.leftStick, new Vector2(1f, 0f));
        float assistedColumnDeg = mapper.StepSteer(0.01f, 630f, 90f, 17.32f);
        Assert.Less(assistedColumnDeg, 0.5f * 90f * 17.32f * 0.01f, "assist: rate/grip limited target");
        var secondMapper = CreateMapperWithoutDebounce(InputMapper.Defaults()); WireAssist(secondMapper); secondMapper.profile.gamepadSteer.enabled = false;
        Assert.AreEqual(90f * 17.32f * 0.01f, secondMapper.StepSteer(0.01f, 630f, 90f, 17.32f), 0.5f, "assist off: direct rate-limited stick");
        mapper.Dispose(); secondMapper.Dispose();
    }
}
