// Device-agnostic input bindings on top of the Unity Input System (com.unity.inputsystem).
// Every action can be bound to any axis/button of any connected device (keyboard, gamepad, HID wheel/pedals/shifter/handbrake).
// Bindings are stored by device identity (product name + layout + index among identical devices) and re-resolved on reconnect.
// Calibration is explicit per binding (min/max/center capture, invert, deadzone, half-axis for combined pedals). No assists, no gains.
using System; using System.Collections.Generic; using UnityEngine;
using UnityEngine.InputSystem; using UnityEngine.InputSystem.Controls; using UnityEngine.InputSystem.LowLevel;

public enum InAction { Steer, Throttle, Brake, Clutch, Handbrake, ShiftUp, ShiftDown, Gear1, Gear2, Gear3, Gear4, Gear5, GearR, Reset, Menu, LookX, LookY, Recenter, CameraToggle, HudToggle, MenuUp, MenuDown, MenuLeft, MenuRight, MenuSelect, Ignition, Starter, MenuTabNext, MenuTabPrev, ResetToStart }   // saved as ints in the settings file; changing the order or removing a member requires bumping BindingProfile.CurrentVersion
public enum AxisHalf { Full, Positive, Negative }      // Positive/Negative: use one half of a combined pedal axis (centre -> max / centre -> min)
public enum SteerSource { Wheel, Stick }               // Wheel: absolute angle (user's wheel = steering column). Stick/keys: target lock, rate-limited like the keyboard
public enum SteerMapping { RealColumn1to1, ScaleToCarLock }

[Serializable] public class AxisCalibration
{
    public float min = 0f, max = 1f, center = 0f; public bool invert; public bool calibrated; public float deadzone = 0f; public AxisHalf half = AxisHalf.Full;
    public bool hasRest; public float rest;             // full-range pedal: raw value at rest (maps to 0); the far end of the captured range maps to 1
    public float PressedEnd => invert ? min : max;
    // @description - Apply a calibration capture (raw min/max seen while sweeping, raw value when Done was pressed). Works for any raw range (0..1, -1..1 bipolar HID axes like Heusinkveld on Windows, inverted axes whose rest is +1). Returns false if no travel was seen.
    // @param {float} capMin - cap min
    // @param {float} capMax - cap max
    // @param {float} restRaw - rest raw
    // @param {bool} bipolar - bipolar
    // @return {bool} - the apply capture
    public bool ApplyCapture(float capMin, float capMax, float restRaw, bool bipolar)
    {
        if (capMax - capMin < 0.05f) return false;      // pedal/wheel never moved: keep previous calibration
        min = capMin; max = capMax; calibrated = true;
        if (bipolar || half != AxisHalf.Full) { center = restRaw; hasRest = false; return true; }
        invert = Mathf.Abs(restRaw - max) < Mathf.Abs(restRaw - min);   // rest nearer max -> axis reads high when released -> inverted
        rest = restRaw; hasRest = true; return true;
    }
    // @description - bipolar (-1..1, uses centre) or unipolar (0..1) normalisation of a raw control value
    // @param {float} raw - raw
    // @param {bool} bipolar - bipolar
    // @return {float} - the normalize
    public float Normalize(float raw, bool bipolar)
    {
        float n;
        if (bipolar)
        {
            n = raw >= center ? (max > center ? (raw - center) / (max - center) : 0f) : (center > min ? -(center - raw) / (center - min) : 0f);
            if (invert) n = -n;
            float a = Mathf.Abs(n); n = a <= deadzone ? 0f : Mathf.Sign(n) * (a - deadzone) / Mathf.Max(1e-4f, 1f - deadzone);
            return Mathf.Clamp(n, -1f, 1f);
        }
        if (half == AxisHalf.Positive) n = max > center ? (raw - center) / (max - center) : 0f;
        else if (half == AxisHalf.Negative) n = center > min ? (center - raw) / (center - min) : 0f;
        else if (hasRest) { float e = PressedEnd; n = Mathf.Abs(e - rest) > 1e-4f ? (raw - rest) / (e - rest) : 0f; n = Mathf.Clamp01(n); goto dz; }   // rest -> 0, pressed end -> 1 (direction handles invert)
        else n = max != min ? (raw - min) / (max - min) : 0f;
        n = Mathf.Clamp01(n); if (invert) n = 1f - n;
        dz:
        n = n <= deadzone ? 0f : (n - deadzone) / Mathf.Max(1e-4f, 1f - deadzone);
        return Mathf.Clamp01(n);
    }
}

[Serializable] public class Binding
{
    public string product = "", layout = ""; public int index; public string control = "";
    public bool negative;                               // contributes in the negative direction (e.g. 'A' key for steer left)
    public SteerSource steerSource = SteerSource.Stick;
    public AxisCalibration calibration = new AxisCalibration();
    [NonSerialized] public InputControl resolved; [NonSerialized] public float restRaw = float.NaN;
    // debounce state (button actions): raw sample, debounced state, candidate state + real time since it has been stable, chatterCount counter
    [NonSerialized] public bool debounceRawState, debounceStableState, debounceCandidateState, debounceInitialised; [NonSerialized] public double debounceCandidateSince; [NonSerialized] public uint debounceLastUpdateId = uint.MaxValue; [NonSerialized] public int chatterCount;   // first reading of an uncalibrated pedal axis = its rest position
    public string Label => (string.IsNullOrEmpty(product) ? layout : product) + (index > 0 ? $"#{index}" : "") + ": " + control + (negative ? " (-)" : "");
}
[Serializable] public class ActionSlots { public InAction action; public List<Binding> slots = new List<Binding>(); }
[Serializable] public class BindingProfile
{
    public const int CurrentVersion = 10;   // bump when a field is renamed or its meaning changes; files of any other version are discarded (no migration)
    public int version = CurrentVersion;
    public GamepadSteerSettings gamepadSteer = new GamepadSteerSettings();   // v7: gamepad/keyboard steering assist preferences (input layer)
    public bool startEngineOff = false, starterInterlock = false, autoClutch = true;
    public int tireCompound = 0;   // v8: 0 = Default (sourced model), 1 = Experimental (viscoelastic) (CarParams.TireCompound)
    public bool tireThermal = false; public float frontColdPressurePsi = 24.7f, rearColdPressurePsi = 24.7f;   // tire thermal model option; cold pressures in psi (VW 1.7 bar = 24.7 psi)
    public float audioVolume = 0.8f, tireVolume = 1f, ambientBrightness = 1.3f; public bool audioMute; public bool hudVisible = true;   // H: grey info boxes (telemetry / FFB / joints) shown   // master volume 0..1   // autoClutch: for keyboard/gamepad drivers without a clutch pedal (option)   // spawn with engine stopped; starter only with clutch fully in or neutral (1984 car had no interlock)
    public bool vrEnabled = false, cockpitView = false;   // VR on/off (launch flags -vr / -novr override); flat-screen camera
    public float seatForwardOffset = 0f, seatUpOffset = 0f;             // seat adjustment from the estimated eye point (m), range in CarParams
    public float wheelRotationDeg = 0f;                 // user's wheel lock-to-lock rotation; 0 = the car's own lock-to-lock (2 x handwheelLockDeg = 1269 deg, D from sourced lock + ratio)
    // force feedback (FfbController). wheelbaseMaxTorqueNm must be entered by the user (0 = not set -> FFB stays off).
    public bool forceFeedbackEnabled = true; public string forceFeedbackDevice = ""; public float wheelbaseMaxTorqueNm = 0f;
    public float forceFeedbackStrengthPercent = 100f;                 // user preference; 100 % = 1:1 physical column torque
    public float forceFeedbackMaxOutputPercent = 100f;
    public float flatFovDeg = 60f;                      // flat-screen (non-VR) vertical field of view, degrees
    public const float FovMin = 30f, FovMax = 110f;
    public string track = "";                          // selected track scene ('' = flat test area)
    public float debounceMs = 0f;                       // OPTIONAL button debounce safety (0 = off): a raw change registers only after it is stable this long (real time). Not the fix for twitching: see HidReportFilter                // user safety cap on the device command
    public bool forceFeedbackInvert;                              // device direction convention (calibration, not a gain)
    // @description - Rotation deg.
    // @param {float} carLockDeg - car lock deg
    // @return {float} - the rotation deg
    public float RotationDeg(float carLockDeg) => wheelRotationDeg > 0f ? wheelRotationDeg : 2f * carLockDeg;
    public SteerMapping mapping = SteerMapping.RealColumn1to1;
    public List<ActionSlots> actions = new List<ActionSlots>();
    // @description - Get (see the body for details).
    // @param {InAction} a - value of type InAction
    // @return {List<Binding>} - the get
    public List<Binding> Get(InAction a) { var s = actions.Find(x => x.action == a); if (s == null) { s = new ActionSlots { action = a }; actions.Add(s); } return s.slots; }
}

public class InputMapper
{
    public BindingProfile profile; bool dirty = true;
    readonly Dictionary<InAction, bool> prevPressed = new Dictionary<InAction, bool>();
    // @description - Tests whether bipolar.
    // @param {InAction} action - action
    // @return {bool} - the is bipolar
    public static bool IsBipolar(InAction action) => action == InAction.Steer || action == InAction.LookX || action == InAction.LookY;
    // @description - Tests whether axis action.
    // @param {InAction} action - action
    // @return {bool} - the is axis action
    public static bool IsAxisAction(InAction action) => action <= InAction.Handbrake || action == InAction.LookX || action == InAction.LookY;

    // @description - Input mapper.
    // @param {BindingProfile} p - value of type BindingProfile
    // @return {Void} - nothing
    public InputMapper(BindingProfile p) { profile = p; InputSystem.onDeviceChange += OnDeviceChange; }
    // @description - Dispose.
    // @return {Void} - nothing
    public void Dispose() { InputSystem.onDeviceChange -= OnDeviceChange; }
    // @description - Handles the device change.
    // @param {InputDevice} device - device
    // @param {InputDeviceChange} c - value of type InputDeviceChange
    // @return {Void} - nothing
    void OnDeviceChange(InputDevice device, InputDeviceChange c) { dirty = true; }   // added/removed/reconnected -> re-resolve all bindings
    // @description - Mark dirty.
    // @return {Void} - nothing
    public void MarkDirty() { dirty = true; }

    // ---------- device identity ----------
    // @description - Category.
    // @param {InputDevice} device - device
    // @return {string} - the category
    public static string Category(InputDevice device) => device is Keyboard ? "Keyboard" : device is Gamepad ? "Gamepad" : device is Joystick ? "Joystick" : device.layout;
    // @description - Matches.
    // @param {InputDevice} device - device
    // @param {Binding} binding - binding
    // @return {bool} - the matches
    static bool Matches(InputDevice device, Binding binding)
    {
        if (string.IsNullOrEmpty(binding.product)) return Category(device) == binding.layout || device.layout == binding.layout;    // wildcard: any keyboard / any gamepad
        return device.description.product == binding.product && device.layout == binding.layout;
    }
    // @description - Skip.
    // @param {InputDevice} device - device
    // @return {bool} - the skip
    public static bool Skip(InputDevice device) => device is Mouse || device is Pointer || device is Sensor;
    // @description - From control.
    // @param {InputControl} c - value of type InputControl
    // @param {bool} wildcard - wildcard
    // @return {Binding} - the from control
    public static Binding FromControl(InputControl c, bool wildcard)
    {
        var d = c.device; var b = new Binding { control = c.path.Length > d.path.Length + 1 && c.path.StartsWith(d.path + "/") ? c.path.Substring(d.path.Length + 1) : c.name };   // guard: a control path that is not "<device path>/<control>" (e.g. the device itself) threw ArgumentOutOfRangeException (startIndex)
        if (wildcard && (d is Keyboard || d is Gamepad)) { b.layout = Category(d); }
        else
        {
            b.product = d.description.product ?? ""; b.layout = d.layout;
            int k = 0; foreach (var o in InputSystem.devices) { if (o == d) break; if (o.layout == d.layout && o.description.product == d.description.product) k++; }
            b.index = k; if (string.IsNullOrEmpty(b.product)) { b.layout = d.layout; }
        }
        b.steerSource = (d is Keyboard || d is Gamepad) ? SteerSource.Stick : SteerSource.Wheel;
        b.calibration = DefaultCalibration(c);
        return b;
    }
    // @description - Default calibration.
    // @param {InputControl} control - control
    // @return {AxisCalibration} - the default calibration
    public static AxisCalibration DefaultCalibration(InputControl control)
    {
        if (control is ButtonControl) return new AxisCalibration { min = 0f, max = 1f, center = 0f };
        if (control is AxisControl ax && ax.parent is StickControl) return new AxisCalibration { min = -1f, max = 1f, center = 0f };
        return new AxisCalibration { min = -1f, max = 1f, center = 0f };   // generic HID axis: assume -1..1 until calibrated
    }
    // @description - Resolve.
    // @return {Void} - nothing
    public void Resolve()
    {
        dirty = false;
        foreach (var s in profile.actions) foreach (var b in s.slots)
        {
            b.resolved = null; int k = 0;
            foreach (var d in InputSystem.devices)
            {
                if (!d.added || !Matches(d, b)) continue;
                if (!string.IsNullOrEmpty(b.product) && k++ != b.index) continue;
                var c = d.TryGetChildControl(b.control); if (c != null) { b.resolved = c; b.restRaw = float.NaN; break; }
            }
        }
    }
    // @description - Raw.
    // @param {InputControl} control - control
    // @return {float} - the raw
    public static float Raw(InputControl control) => control is AxisControl a ? a.ReadUnprocessedValue() : 0f;   // skip layout processors (e.g. stick deadzone); calibration is explicit

    // ---------- reading ----------
    // @description - Axis.
    // @param {InAction} action - action
    // @param {out bool} anyWheel - any wheel
    // @param {out float} wheel - wheel
    // @param {out float} digital - digital
    // @return {float} - the axis
    public float Axis(InAction action, out bool anyWheel, out float wheel, out float digital)
    {
        if (dirty) Resolve();
        bool bip = IsBipolar(action); float best = 0f; anyWheel = false; wheel = 0f; digital = 0f;
        foreach (var b in profile.Get(action))
        {
            if (b.resolved == null) continue;
            float v = UncalibratedPedal(b, bip) ? RestRelative(b) : b.calibration.Normalize(Raw(b.resolved), bip && !(b.resolved is ButtonControl)); if (b.negative) v = -v;
            if (action == InAction.Steer && b.steerSource == SteerSource.Wheel && !(b.resolved is ButtonControl)) { anyWheel = true; wheel = v; continue; }
            if (bip) { if (b.resolved is ButtonControl) digital += v; else if (Mathf.Abs(v) > Mathf.Abs(best)) best = v; }
            else best = Mathf.Max(best, v);
        }
        if (bip) { digital = Mathf.Clamp(digital, -1f, 1f); return Mathf.Abs(digital) > Mathf.Abs(best) ? digital : best; }
        return best;
    }
    // @description - Axis.
    // @param {InAction} action - action
    // @return {float} - the axis
    public float Axis(InAction action) => Axis(action, out _, out _, out _);
    // @description - which binding currently drives an axis action (strongest |value|), e.g. "Gamepad rightStick/x = 0.75" (diagnostics: look snaps)
    // @param {InAction} action - action
    // @return {string} - the source
    public string Source(InAction action)
    {
        if (dirty) Resolve(); string s = ""; float best = 0f; bool bip = IsBipolar(action);
        foreach (var b in profile.Get(action)) { if (b.resolved == null) continue; float v = b.calibration.Normalize(Raw(b.resolved), bip && !(b.resolved is ButtonControl)); if (b.negative) v = -v;
            if (Mathf.Abs(v) > Mathf.Abs(best)) { best = v; s = $"{(string.IsNullOrEmpty(b.product) ? b.layout : b.product)} {b.control} = {v:0.00}"; } }
        return s;
    }
    // @description - look axes: Input System default stick deadzone (InputSystem.settings.defaultDeadzoneMin, Unity's own default), rescaled; raw reads skip layout processors
    // @param {float} v - value of type float
    // @return {float} - the look deadzone
    public static float LookDeadzone(float v) { float dz = InputSystem.settings.defaultDeadzoneMin; float m = Mathf.Abs(v); return m <= dz ? 0f : Mathf.Sign(v) * Mathf.Clamp01((m - dz) / (1f - dz)); }
    // @description - A pedal axis that was bound but never calibrated still has the generic -1..1 guess; if its rest end is +1 it would read 100 % (e.g. clutch held in -> car won't move). Until calibrated, read it relative to its first (rest) reading: |raw - rest| / range.
    // @param {Binding} binding - binding
    // @param {bool} bip - bip
    // @return {bool} - the uncalibrated pedal
    public static bool UncalibratedPedal(Binding binding, bool bip) => !bip && !binding.calibration.calibrated && binding.calibration.half == AxisHalf.Full && binding.resolved is AxisControl
        && !(binding.resolved is ButtonControl) && !(binding.resolved.device is Gamepad) && !(binding.resolved.device is Keyboard) && binding.calibration.min == -1f && binding.calibration.max == 1f;
    // @description - Rest relative.
    // @param {Binding} b - value of type Binding
    // @return {float} - the rest relative
    public static float RestRelative(Binding binding)
    {
        float r = Raw(binding.resolved); if (float.IsNaN(binding.restRaw)) binding.restRaw = r;
        return Mathf.Clamp01(Mathf.Abs(r - binding.restRaw) / (binding.calibration.max - binding.calibration.min));
    }
    // @description - Uncalibrated count.
    // @return {int} - the uncalibrated count
    public int UncalibratedCount() { int n = 0; foreach (var s in profile.actions) foreach (var b in s.slots) if (b.resolved != null && UncalibratedPedal(b, IsBipolar(s.action))) n++; return n; }
    // @description - Held.
    // @param {InAction} action - action
    // @return {bool} - the held
    public bool Held(InAction action)
    {
        if (dirty) Resolve();
        if (!IsAxisAction(action)) { foreach (var b in profile.Get(action)) if (b.resolved != null && Debounced(b)) return true; return false; }
        foreach (var b in profile.Get(action)) if (b.resolved != null && (UncalibratedPedal(b, false) ? RestRelative(b) : b.calibration.Normalize(Raw(b.resolved), false)) > 0.5f) return true;
        return false;
    }
    // @description - Held from non keyboard.
    // @param {InAction} action - action
    // @return {bool} - the held from non keyboard
    public bool HeldFromNonKeyboard(InAction action)
    {
        foreach (var b in profile.Get(action)) if (b.resolved != null && !(b.resolved.device is Keyboard) && (IsAxisAction(action) ? b.calibration.Normalize(Raw(b.resolved), false) > 0.5f : Debounced(b))) return true;
        return false;
    }
    // @description - Tests whether it has non keyboard.
    // @param {InAction} action - action
    // @return {bool} - the has non keyboard
    public bool HasNonKeyboard(InAction action) { foreach (var b in profile.Get(action)) if (b.resolved != null && !(b.resolved.device is Keyboard)) return true; return false; }
    // ---------- edge detection / menu gating ----------
    // One physical press = exactly one Pressed() == true, regardless of how often Pressed() is called per input update.
    // latched[a] stays true until every binding of a has been released (and no sub-frame tap arrived) for one update.
    class Edge { public uint update = uint.MaxValue; public bool result, latched; }
    readonly Dictionary<InAction, Edge> edges = new Dictionary<InAction, Edge>();
    uint flushUpdate = uint.MaxValue; bool menuOpen;
    // @description - Tests whether menu action.
    // @param {InAction} action - action
    // @return {bool} - the is menu action
    public static bool IsMenuAction(InAction action) => action == InAction.Menu || (action >= InAction.MenuUp && action <= InAction.MenuSelect) || action == InAction.MenuTabNext || action == InAction.MenuTabPrev;
    // @description - Tests whether gameplay.
    // @param {InAction} action - action
    // @return {bool} - the is gameplay
    public static bool IsGameplay(InAction action) => !IsMenuAction(action);
    /// While true, Pressed() of every gameplay action returns false (shift, gears, reset, ignition, ...). Opening or closing flushes all edges:
    /// a button held (or tapped) at that moment fires nothing until it has been released and pressed again -> no queued presses.
    public bool MenuOpen { get => menuOpen; set { if (value != menuOpen) { menuOpen = value; Flush(); } } }
    // @description - Discard pending presses: every action is latched to its current state, taps of the current input update are ignored.
    // @return {Void} - nothing
    public void Flush()
    {
        if (dirty) Resolve();
        flushUpdate = InputState.updateCount;
        foreach (InAction a in Enum.GetValues(typeof(InAction))) { var e = E(a); e.latched = Held(a); e.update = InputState.updateCount; e.result = false; }
    }
    // @description - E.
    // @param {InAction} action - action
    // @return {Edge} - the e
    Edge E(InAction action) { if (!edges.TryGetValue(action, out var e)) edges[action] = e = new Edge(); return e; }
    // @description - Tap.
    // @param {InAction} action - action
    // @param {HashSet<InputControl>} exclude - exclude
    // @return {bool} - the tap
    bool Tap(InAction action, HashSet<InputControl> exclude)
    {
        foreach (var b in profile.Get(action)) if (b.resolved is ButtonControl bc && (exclude == null || !exclude.Contains(bc)) && bc.wasPressedThisFrame) return true;   // catches sub-frame taps
        return false;
    }
    // @description - Held excluding.
    // @param {InAction} action - action
    // @param {HashSet<InputControl>} exclude - exclude
    // @return {bool} - the held excluding
    bool HeldExcluding(InAction action, HashSet<InputControl> exclude)
    {
        foreach (var b in profile.Get(action)) if (b.resolved != null && (exclude == null || !exclude.Contains(b.resolved)) && (UncalibratedPedal(b, false) ? RestRelative(b) : b.calibration.Normalize(Raw(b.resolved), false)) > 0.5f) return true;
        return false;
    }
    public const float PressOn = 0.65f, PressOff = 0.35f;   // hysteresis for analog controls driving button actions: noise at a threshold never re-triggers
    // @description - 0..1 'how pressed' of one binding for a BUTTON action. An axis bound to a button action is read relative to its rest (calibrated rest, else the first reading after resolve), so an axis resting at -1 or +1 never reads as held.
    // @param {Binding} binding - binding
    // @return {float} - the button level
    public static float ButtonLevel(Binding binding)
    {
        if (binding.resolved is ButtonControl bc) return bc.isPressed ? 1f : 0f;
        if (binding.calibration.hasRest) return binding.calibration.Normalize(Raw(binding.resolved), false);
        float r = Raw(binding.resolved); if (float.IsNaN(binding.restRaw)) binding.restRaw = r;
        return Mathf.Clamp01(Mathf.Abs(r - binding.restRaw));   // full travel of a -1..1 axis away from rest = 2 -> clamped; half travel = pressed
    }
    // @description - Level excluding.
    // @param {InAction} action - action
    // @param {HashSet<InputControl>} exclude - exclude
    // @param {bool} latched - latched
    // @return {bool} - the level excluding
    bool LevelExcluding(InAction action, HashSet<InputControl> exclude, bool latched)
    {
        if (IsAxisAction(action)) return HeldExcluding(action, exclude);
        foreach (var b in profile.Get(action)) if (b.resolved != null && (exclude == null || !exclude.Contains(b.resolved)) && Debounced(b)) return true;
        return false;
    }
    /// real-time clock for debounce (seconds); unscaled, independent of physics ticks and timeScale (menu pauses). Replaceable in tests.
    public static Func<double> Clock = () => Time.realtimeSinceStartupAsDouble;
    // @description - Debounced pressed state of one binding of a BUTTON action. Sampled once per input update (Update, never FixedUpdate). A raw change registers only after the raw state has stayed the same for profile.debounceMs of real time. Chatter = raw changes that were rejected (incl. sub-frame release/press pairs
    // @param {Binding} binding - binding
    // @return {bool} - the debounced
    public bool Debounced(Binding binding)
    {
        uint u = InputState.updateCount; if (binding.debounceLastUpdateId == u) return binding.debounceStableState; binding.debounceLastUpdateId = u;
        double now = Clock(), T = Mathf.Max(0f, profile.debounceMs) * 1e-3;
        bool raw = ButtonLevel(binding) > (binding.debounceStableState ? PressOff : PressOn);
        if (!binding.debounceInitialised) { binding.debounceInitialised = true; binding.debounceRawState = binding.debounceStableState = binding.debounceCandidateState = raw; binding.debounceCandidateSince = now; return raw; }
        if (binding.resolved is ButtonControl bc && raw == binding.debounceRawState && (bc.wasPressedThisFrame || bc.wasReleasedThisFrame)) binding.chatterCount++;   // bounced and came back within one update
        if (raw != binding.debounceRawState) { if (raw == binding.debounceStableState) binding.chatterCount++; binding.debounceRawState = raw; }   // went back before the change was accepted -> chatterCount
        if (raw != binding.debounceCandidateState) { binding.debounceCandidateState = raw; binding.debounceCandidateSince = now; }
        if (binding.debounceCandidateState != binding.debounceStableState && (T <= 0 || now - binding.debounceCandidateSince >= T)) binding.debounceStableState = binding.debounceCandidateState;
        return binding.debounceStableState;
    }
    // @description - call every input update (InputRouter.Update) so debounce timing never depends on which actions get queried
    // @return {Void} - nothing
    public void SampleButtons() { if (dirty) Resolve(); foreach (var s in profile.actions) if (!IsAxisAction(s.action)) foreach (var b in s.slots) if (b.resolved != null) Debounced(b); }
    // @description - controls bound to any gameplay action: these never drive menu tab navigation (a wheel button bound to Gear Up must not flip tabs)
    // @return {HashSet<InputControl>} - the gameplay controls
    public HashSet<InputControl> GameplayControls()
    {
        if (dirty) Resolve(); var set = new HashSet<InputControl>();
        foreach (var s in profile.actions) if (IsGameplay(s.action)) foreach (var b in s.slots) if (b.resolved != null) set.Add(b.resolved);
        return set;
    }
    // @description - edge-triggered press (rising edge only, never repeats while held). Safe to call several times per frame.
    // @param {InAction} action - action
    // @return {bool} - the pressed
    public bool Pressed(InAction action)
    {
        if (dirty) Resolve();
        var e = E(action); uint u = InputState.updateCount;
        if (e.update != u)
        {
            var ex = (action == InAction.MenuTabNext || action == InAction.MenuTabPrev) ? GameplayControls() : null;
            bool active = LevelExcluding(action, ex, e.latched);
            bool tap = profile.debounceMs <= 0f && u != flushUpdate && Tap(action, ex);   // debounce OFF (0 ms) only: raw sub-frame taps count, as before
            e.result = (active && !e.latched) || tap;   // debounce ON: rising edge of the DEBOUNCED state only -> one physical press = one shift, chatterCount ignored
            active |= tap;   // a tap = a new press transition in this update (release+press within one frame still counts once)
            e.latched = active; e.update = u;
        }
        if (menuOpen && IsGameplay(action)) return false;
        bool r = e.result; e.result = false; return r;   // consumed: a second query in the same update gets false
    }

    // ---------- steering: user wheel angle -> steering column -> rack (constant pinion) ----------
    /// Returns handwheel (column) angle in degrees. Wheel source: column angle = user's wheel angle (1:1, soft lock at the user's
    /// rotation limit) or scaled so the user's lock reaches the car's lock. Stick/keys: rate-limited towards full lock (as before).
    public float handwheelDeg, wheelDegUnclamped;
    public GamepadSteering assist; public GamepadSteerContext? assistCtx; public Func<float, float> meanToHandwheel;   // set by InputRouter each frame   // unclamped: physical wheel angle beyond the car's rack stop (FFB soft lock)
    // @description - Advances the steer.
    // @param {float} deltaTime - delta time
    // @param {float} carLockDeg - car lock deg
    // @param {float} steerRateRoadDegPerS - steer rate road deg per s
    // @param {float} centreRatio - centre ratio
    // @return {float} - the step steer
    public float StepSteer(float deltaTime, float carLockDeg, float steerRateRoadDegPerS, float centreRatio)
    {
        float v = Axis(InAction.Steer, out bool anyWheel, out float wheel, out float digital);
        float half = 0.5f * profile.RotationDeg(carLockDeg);
        if (Mathf.Abs(v) > 0f || !anyWheel)
        {
            if (assist != null && profile.gamepadSteer.enabled && assistCtx.HasValue && meanToHandwheel != null)
            {   // gamepad/keyboard assist (road-angle space); never reached by a real wheel (anyWheel && v == 0)
                assist.s = profile.gamepadSteer; handwheelDeg = meanToHandwheel(assist.Step(deltaTime, v, assistCtx.Value));
            }
            else
            {
                float target = v * carLockDeg;
                handwheelDeg = Mathf.MoveTowards(handwheelDeg, target, steerRateRoadDegPerS * centreRatio * deltaTime);
                if (assist != null) { assist.roadDeg = handwheelDeg / centreRatio; assist.filtered = v; }
            }
        }
        else
        {   // real wheel (SteerSource.Wheel) and no stick/key input: column follows the wheel directly (1:1 / scaled), never the gamepad assist.
            // (2026-10-14 fix: this branch used to be an `else if (assist != null)` that only synced the assist state, so once InputRouter
            // created the assist the wheel angle was never applied and wheel steering froze, whether the assist was on or off.)
            handwheelDeg = profile.mapping == SteerMapping.RealColumn1to1 ? wheel * half : wheel * carLockDeg;
            if (assist != null) { assist.roadDeg = handwheelDeg / centreRatio; assist.filtered = 0f; }   // keep the assist bumpless if a stick takes over
        }
        wheelDegUnclamped = anyWheel && Mathf.Abs(v) == 0f ? (profile.mapping == SteerMapping.RealColumn1to1 ? wheel * half : wheel * carLockDeg) : handwheelDeg;
        handwheelDeg = Mathf.Clamp(handwheelDeg, -carLockDeg, carLockDeg);   // car's rack end stop
        return handwheelDeg;
    }

    // ---------- capture (rebinding) ----------
    readonly Dictionary<InputControl, float> snap = new Dictionary<InputControl, float>();
    // @description - Begin capture.
    // @return {Void} - nothing
    public void BeginCapture()
    {
        snap.Clear();
        foreach (var d in InputSystem.devices) { if (Skip(d)) continue; foreach (var c in d.allControls) if (c is AxisControl ax && !c.synthetic && !c.noisy) snap[c] = ax.ReadUnprocessedValue(); }
    }
    // @description - returns the control that moved/was pressed most since BeginCapture (axis: change > 0.4 of a -1..1 range; button: pressed), else null
    // @param {bool} wantAxis - want axis
    // @return {InputControl} - the poll capture
    public InputControl PollCapture(bool wantAxis)
    {
        InputControl best = null; float bestD = 0f;
        foreach (var kv in snap)
        {
            var c = kv.Key; if (!c.device.added) continue; float v = ((AxisControl)c).ReadUnprocessedValue(), dlt = Mathf.Abs(v - kv.Value);
            bool btn = c is ButtonControl;
            if (c.name == "anyKey" || c.name == "escape") continue;                   // Esc always cancels, never binds
            if (btn) { if (kv.Value >= 0.5f) continue;                               // already pressed/held at capture start: rejected (must be released first -> new snapshot)
                       if (((ButtonControl)c).isPressed) dlt = Mathf.Max(dlt, 1f); }
            float thr = btn ? 0.5f : 0.4f;
            if (!wantAxis && !btn && dlt < 0.6f) continue;   // button action from an axis: needs a big change from its rest baseline (rest may be -1/+1)
            if (dlt > thr && dlt > bestD) { bestD = dlt; best = c; }
        }
        return best;
    }
    // @description - rest baseline of a control recorded at BeginCapture (NaN if not snapshotted)
    // @param {InputControl} control - control
    // @return {float} - the capture baseline
    public float CaptureBaseline(InputControl control) => snap.TryGetValue(control, out var v) ? v : float.NaN;
    // @description - remove every non-keyboard binding of an action (keyboard defaults stay so the action is never unreachable)
    // @param {InAction} action - action
    // @return {int} - the clear device bindings
    public int ClearDeviceBindings(InAction action) { int n = profile.Get(action).RemoveAll(b => b.layout != "Keyboard"); MarkDirty(); Flush(); return n; }
    // @description - call when a capture finished (bound or cancelled): the press that was captured is consumed and fires no action
    // @return {Void} - nothing
    public void EndCapture() { snap.Clear(); MarkDirty(); Flush(); }
    // @description - bind the captured control to action a (no duplicate slot) and end the capture
    // @param {InAction} a - value of type InAction
    // @param {InputControl} control - control
    // @return {Binding} - the complete capture
    public Binding CompleteCapture(InAction a, InputControl control)
    {
        var b = FromControl(control, true);
        if (IsBipolar(a) && !(control is ButtonControl) && !(control.parent is StickControl)) b.calibration.center = 0f;
        if (!IsBipolar(a) && !(control is ButtonControl) && !(control.parent is StickControl)) b.calibration = new AxisCalibration { min = -1f, max = 1f, center = 0f };   // pedal axis: calibrate!
        if (!IsAxisAction(a) && !(control is ButtonControl))   // axis on a button action (e.g. pedal as Gear Up): rest = capture baseline, pressed = direction it moved
        { float r0 = CaptureBaseline(control), v = Raw(control); if (!float.IsNaN(r0)) { b.calibration = new AxisCalibration(); b.calibration.ApplyCapture(Mathf.Min(r0, v), Mathf.Max(r0, v), r0, false); } }
        var slots = profile.Get(a); var same = slots.Find(x => x.product == b.product && x.layout == b.layout && x.index == b.index && x.control == b.control);
        if (same == null) slots.Add(b); else b = same;
        EndCapture(); return b;
    }

    // ---------- defaults & persistence ----------
    // @description - Defaults.
    // @return {BindingProfile} - the defaults
    public static BindingProfile Defaults()
    {
        var p = new BindingProfile();
        void K(InAction a, string key, bool neg = false) => p.Get(a).Add(new Binding { layout = "Keyboard", control = key, negative = neg, calibration = new AxisCalibration() });
        void G(InAction a, string ctl, bool stick = false, bool neg = false) => p.Get(a).Add(new Binding { layout = "Gamepad", control = ctl, negative = neg, steerSource = SteerSource.Stick,
            calibration = stick ? new AxisCalibration { min = -1f, max = 1f, center = 0f, deadzone = 0.1f } : new AxisCalibration() });   // stick deadzone (user-editable) against stick drift
        K(InAction.Steer, "a", true); K(InAction.Steer, "leftArrow", true); K(InAction.Steer, "d"); K(InAction.Steer, "rightArrow");
        K(InAction.Throttle, "w"); K(InAction.Throttle, "upArrow"); K(InAction.Brake, "s"); K(InAction.Brake, "downArrow");
        K(InAction.Clutch, "leftShift"); K(InAction.Handbrake, "space"); K(InAction.ShiftUp, "e"); K(InAction.ShiftDown, "q");
        K(InAction.Gear1, "digit1"); K(InAction.Gear2, "digit2"); K(InAction.Gear3, "digit3"); K(InAction.Gear4, "digit4"); K(InAction.Gear5, "digit5"); K(InAction.GearR, "digit6");
        K(InAction.Reset, "r"); K(InAction.Menu, "f1"); K(InAction.Menu, "escape");
        G(InAction.Steer, "leftStick/x", true); G(InAction.Throttle, "rightTrigger"); G(InAction.Brake, "leftTrigger"); G(InAction.Clutch, "leftShoulder");
        G(InAction.ShiftUp, "rightShoulder"); G(InAction.ShiftDown, "buttonWest"); G(InAction.Handbrake, "buttonEast"); G(InAction.Reset, "select");
        AddVrAndMenuDefaults(p); AddMenuTabDefaults(p); AddResetToStartDefaults(p);
        G(InAction.Menu, "start"); G(InAction.LookX, "rightStick/x", true); G(InAction.LookY, "rightStick/y", true);
        return p;
    }
    // @description - Adds the vr and menu defaults.
    // @param {BindingProfile} p - value of type BindingProfile
    // @return {Void} - nothing
    static void AddVrAndMenuDefaults(BindingProfile p)   // defaults for actions added in profile version 2 (VR / menu navigation); wheel buttons can be bound to them in the menu
    {
        void K(InAction a, string key) => p.Get(a).Add(new Binding { layout = "Keyboard", control = key, calibration = new AxisCalibration() });
        void G(InAction a, string ctl) => p.Get(a).Add(new Binding { layout = "Gamepad", control = ctl, steerSource = SteerSource.Stick, calibration = new AxisCalibration() });
        K(InAction.Recenter, "f12"); K(InAction.Recenter, "home"); G(InAction.Recenter, "rightStickPress"); K(InAction.CameraToggle, "c"); K(InAction.HudToggle, "h");
        K(InAction.MenuUp, "upArrow"); K(InAction.MenuDown, "downArrow"); K(InAction.MenuLeft, "leftArrow"); K(InAction.MenuRight, "rightArrow"); K(InAction.MenuSelect, "enter"); K(InAction.MenuSelect, "numpadEnter");
        K(InAction.Ignition, "i"); K(InAction.Starter, "t"); G(InAction.Ignition, "leftStickPress"); G(InAction.Starter, "buttonNorth");
        G(InAction.MenuUp, "dpad/up"); G(InAction.MenuDown, "dpad/down"); G(InAction.MenuLeft, "dpad/left"); G(InAction.MenuRight, "dpad/right"); G(InAction.MenuSelect, "buttonSouth");
    }
    // @description - Adds the menu tab defaults.
    // @param {BindingProfile} profile - profile
    // @return {Void} - nothing
    static void AddMenuTabDefaults(BindingProfile profile)   // v4: dedicated menu tab navigation (was ShiftUp/ShiftDown -> gear paddles flipped tabs). Keys not used by gameplay.
    {
        void K(InAction a, string key) => profile.Get(a).Add(new Binding { layout = "Keyboard", control = key, calibration = new AxisCalibration() });
        K(InAction.MenuTabNext, "pageDown"); K(InAction.MenuTabNext, "tab"); K(InAction.MenuTabPrev, "pageUp");
    }
    // @description - Adds the reset to start defaults.
    // @param {BindingProfile} profile - profile
    // @return {Void} - nothing
    static void AddResetToStartDefaults(BindingProfile profile) { profile.Get(InAction.ResetToStart).Add(new Binding { layout = "Keyboard", control = "backspace", calibration = new AxisCalibration() }); }   // v5: reset to start/finish
    // @description - Tests whether retired.
    // @param {InAction} action - action
    // @return {bool} - the is retired
    public static string DefaultPath => System.IO.Path.Combine(Application.persistentDataPath, "input_bindings.json");
    // @description - Save (see the body for details).
    // @param {BindingProfile} profile - profile
    // @param {string} path - path
    // @return {Void} - nothing
    public static void Save(BindingProfile profile, string path = null) { System.IO.File.WriteAllText(path ?? DefaultPath, JsonUtility.ToJson(profile, true)); PlayerPrefs.SetString("input_bindings_path", path ?? DefaultPath); PlayerPrefs.Save(); }
    // @description - Loads the or default.
    // @param {string} path - path
    // @return {BindingProfile} - the load or default
    public static BindingProfile LoadOrDefault(string path = null)
    {
        path = path ?? DefaultPath;
        try { if (System.IO.File.Exists(path)) { var p = JsonUtility.FromJson<BindingProfile>(System.IO.File.ReadAllText(path)); if (p != null && p.actions.Count > 0 && p.version == BindingProfile.CurrentVersion) return p; Debug.LogWarning("input bindings: " + path + " has settings version " + (p != null ? p.version : -1) + ", expected " + BindingProfile.CurrentVersion + "; using defaults"); } }
        catch (Exception e) { Debug.LogWarning("input bindings: could not load " + path + ": " + e.Message); }
        return Defaults();
    }
}
