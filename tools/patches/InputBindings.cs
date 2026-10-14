// Device-agnostic input bindings on top of the Unity Input System (com.unity.inputsystem).
// Every action can be bound to any axis/button of any connected device (keyboard, gamepad, HID wheel/pedals/shifter/handbrake).
// Bindings are stored by device identity (product name + layout + index among identical devices) and re-resolved on reconnect.
// Calibration is explicit per binding (min/max/center capture, invert, deadzone, half-axis for combined pedals). No assists, no gains.
using System; using System.Collections.Generic; using UnityEngine;
using UnityEngine.InputSystem; using UnityEngine.InputSystem.Controls; using UnityEngine.InputSystem.LowLevel;

public enum InAction { Steer, Throttle, Brake, Clutch, Handbrake, ShiftUp, ShiftDown, Gear1, Gear2, Gear3, Gear4, Gear5, GearR, Reset, ProfileToggle, Menu, LookX, LookY, Recenter, CameraToggle, HudToggle, MenuUp, MenuDown, MenuLeft, MenuRight, MenuSelect, Ignition, Starter, MenuTabNext, MenuTabPrev, ResetToStart }   // append only (saved as ints)
public enum AxisHalf { Full, Positive, Negative }      // Positive/Negative: use one half of a combined pedal axis (centre -> max / centre -> min)
public enum SteerSource { Wheel, Stick }               // Wheel: absolute angle (user's wheel = steering column). Stick/keys: target lock, rate-limited like the keyboard
public enum SteerMapping { RealColumn1to1, ScaleToCarLock }

[Serializable] public class AxisCal
{
    public float min = 0f, max = 1f, center = 0f; public bool invert; public bool calibrated; public float deadzone = 0f; public AxisHalf half = AxisHalf.Full;
    public bool hasRest; public float rest;             // full-range pedal: raw value at rest (maps to 0); the far end of the captured range maps to 1
    public float PressedEnd => invert ? min : max;
    /// Apply a calibration capture (raw min/max seen while sweeping, raw value when Done was pressed). Works for any raw range
    /// (0..1, -1..1 bipolar HID axes like Heusinkveld on Windows, inverted axes whose rest is +1). Returns false if no travel was seen.
    public bool ApplyCapture(float capMin, float capMax, float restRaw, bool bipolar)
    {
        if (capMax - capMin < 0.05f) return false;      // pedal/wheel never moved: keep previous calibration
        min = capMin; max = capMax; calibrated = true;
        if (bipolar || half != AxisHalf.Full) { center = restRaw; hasRest = false; return true; }
        invert = Mathf.Abs(restRaw - max) < Mathf.Abs(restRaw - min);   // rest nearer max -> axis reads high when released -> inverted
        rest = restRaw; hasRest = true; return true;
    }
    /// bipolar (-1..1, uses centre) or unipolar (0..1) normalisation of a raw control value
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
    public AxisCal cal = new AxisCal();
    [NonSerialized] public InputControl resolved; [NonSerialized] public float restRaw = float.NaN;
    // debounce state (button actions): raw sample, debounced state, candidate state + real time since it has been stable, chatter counter
    [NonSerialized] public bool dbRaw, dbStable, dbCand, dbInit; [NonSerialized] public double dbSince; [NonSerialized] public uint dbUpdate = uint.MaxValue; [NonSerialized] public int chatter;   // first reading of an uncalibrated pedal axis = its rest position
    public string Label => (string.IsNullOrEmpty(product) ? layout : product) + (index > 0 ? $"#{index}" : "") + ": " + control + (negative ? " (-)" : "");
}
[Serializable] public class ActionSlots { public InAction action; public List<Binding> slots = new List<Binding>(); }
[Serializable] public class BindingProfile
{
    public int version = 5;
    public bool startEngineOff = false, starterInterlock = false, autoClutch = true;
    public bool tireThermal = false; public float coldPsiFront = 24.7f, coldPsiRear = 24.7f;   // tire thermal model option; cold pressures in psi (VW 1.7 bar = 24.7 psi)
    public float audioVolume = 0.8f, tireVolume = 1f, ambientBrightness = 1.3f; public bool audioMute; public bool hudVisible = true;   // H: grey info boxes (telemetry / FFB / joints) shown   // master volume 0..1   // autoClutch: for keyboard/gamepad drivers without a clutch pedal (option)   // spawn with engine stopped; starter only with clutch fully in or neutral (1984 car had no interlock)
    public bool vrEnabled = false, cockpitView = false;   // VR on/off (launch flags -vr / -novr override); flat-screen camera
    public float seatFwd = 0f, seatUp = 0f;             // seat adjustment from the estimated eye point (m), range in CarParams
    public float wheelRotationDeg = 0f;                 // user's wheel lock-to-lock rotation; 0 = the car's own lock-to-lock (2 x handwheelLockDeg = 1269 deg, D from sourced lock + ratio)
    // force feedback (FfbController). wheelbaseMaxTorqueNm must be entered by the user (0 = not set -> FFB stays off).
    public bool ffbEnabled = true; public string ffbDevice = ""; public float wheelbaseMaxTorqueNm = 0f;
    public float ffbStrengthPct = 100f;                 // user preference; 100 % = 1:1 physical column torque
    public float ffbMaxOutputPct = 100f;
    public float flatFovDeg = 60f;                      // flat-screen (non-VR) vertical field of view, degrees
    public const float FovMin = 30f, FovMax = 110f;
    public string track = "";                          // selected track scene ('' = flat test area)
    public float debounceMs = 0f;                       // OPTIONAL button debounce safety (0 = off): a raw change registers only after it is stable this long (real time). Not the fix for twitching: see HidReportFilter                // user safety cap on the device command
    public bool ffbInvert;                              // device direction convention (calibration, not a gain)
    public float RotationDeg(float carLockDeg) => wheelRotationDeg > 0f ? wheelRotationDeg : 2f * carLockDeg;
    public SteerMapping mapping = SteerMapping.RealColumn1to1;
    public List<ActionSlots> actions = new List<ActionSlots>();
    public List<Binding> Get(InAction a) { var s = actions.Find(x => x.action == a); if (s == null) { s = new ActionSlots { action = a }; actions.Add(s); } return s.slots; }
}

public class InputMapper
{
    public BindingProfile profile; bool dirty = true;
    readonly Dictionary<InAction, bool> prevPressed = new Dictionary<InAction, bool>();
    public static bool IsBipolar(InAction a) => a == InAction.Steer || a == InAction.LookX || a == InAction.LookY;
    public static bool IsAxisAction(InAction a) => a <= InAction.Handbrake || a == InAction.LookX || a == InAction.LookY;

    public InputMapper(BindingProfile p) { profile = p; InputSystem.onDeviceChange += OnDeviceChange; }
    public void Dispose() { InputSystem.onDeviceChange -= OnDeviceChange; }
    void OnDeviceChange(InputDevice d, InputDeviceChange c) { dirty = true; }   // added/removed/reconnected -> re-resolve all bindings
    public void MarkDirty() { dirty = true; }

    // ---------- device identity ----------
    public static string Category(InputDevice d) => d is Keyboard ? "Keyboard" : d is Gamepad ? "Gamepad" : d is Joystick ? "Joystick" : d.layout;
    static bool Matches(InputDevice d, Binding b)
    {
        if (string.IsNullOrEmpty(b.product)) return Category(d) == b.layout || d.layout == b.layout;    // wildcard: any keyboard / any gamepad
        return d.description.product == b.product && d.layout == b.layout;
    }
    public static bool Skip(InputDevice d) => d is Mouse || d is Pointer || d is Sensor;
    public static Binding FromControl(InputControl c, bool wildcard)
    {
        var d = c.device; var b = new Binding { control = c.path.Substring(d.path.Length + 1) };
        if (wildcard && (d is Keyboard || d is Gamepad)) { b.layout = Category(d); }
        else
        {
            b.product = d.description.product ?? ""; b.layout = d.layout;
            int k = 0; foreach (var o in InputSystem.devices) { if (o == d) break; if (o.layout == d.layout && o.description.product == d.description.product) k++; }
            b.index = k; if (string.IsNullOrEmpty(b.product)) { b.layout = d.layout; }
        }
        b.steerSource = (d is Keyboard || d is Gamepad) ? SteerSource.Stick : SteerSource.Wheel;
        b.cal = DefaultCal(c);
        return b;
    }
    public static AxisCal DefaultCal(InputControl c)
    {
        if (c is ButtonControl) return new AxisCal { min = 0f, max = 1f, center = 0f };
        if (c is AxisControl ax && ax.parent is StickControl) return new AxisCal { min = -1f, max = 1f, center = 0f };
        return new AxisCal { min = -1f, max = 1f, center = 0f };   // generic HID axis: assume -1..1 until calibrated
    }
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
    public static float Raw(InputControl c) => c is AxisControl a ? a.ReadUnprocessedValue() : 0f;   // skip layout processors (e.g. stick deadzone); calibration is explicit

    // ---------- reading ----------
    public float Axis(InAction a, out bool anyWheel, out float wheel, out float digital)
    {
        if (dirty) Resolve();
        bool bip = IsBipolar(a); float best = 0f; anyWheel = false; wheel = 0f; digital = 0f;
        foreach (var b in profile.Get(a))
        {
            if (b.resolved == null) continue;
            float v = UncalibratedPedal(b, bip) ? RestRelative(b) : b.cal.Normalize(Raw(b.resolved), bip && !(b.resolved is ButtonControl)); if (b.negative) v = -v;
            if (a == InAction.Steer && b.steerSource == SteerSource.Wheel && !(b.resolved is ButtonControl)) { anyWheel = true; wheel = v; continue; }
            if (bip) { if (b.resolved is ButtonControl) digital += v; else if (Mathf.Abs(v) > Mathf.Abs(best)) best = v; }
            else best = Mathf.Max(best, v);
        }
        if (bip) { digital = Mathf.Clamp(digital, -1f, 1f); return Mathf.Abs(digital) > Mathf.Abs(best) ? digital : best; }
        return best;
    }
    public float Axis(InAction a) => Axis(a, out _, out _, out _);
    /// which binding currently drives an axis action (strongest |value|), e.g. "Gamepad rightStick/x = 0.75" (diagnostics: look snaps)
    public string Source(InAction a)
    {
        if (dirty) Resolve(); string s = ""; float best = 0f; bool bip = IsBipolar(a);
        foreach (var b in profile.Get(a)) { if (b.resolved == null) continue; float v = b.cal.Normalize(Raw(b.resolved), bip && !(b.resolved is ButtonControl)); if (b.negative) v = -v;
            if (Mathf.Abs(v) > Mathf.Abs(best)) { best = v; s = $"{(string.IsNullOrEmpty(b.product) ? b.layout : b.product)} {b.control} = {v:0.00}"; } }
        return s;
    }
    /// look axes: Input System default stick deadzone (InputSystem.settings.defaultDeadzoneMin, Unity's own default), rescaled; raw reads skip layout processors
    public static float LookDeadzone(float v) { float dz = InputSystem.settings.defaultDeadzoneMin; float m = Mathf.Abs(v); return m <= dz ? 0f : Mathf.Sign(v) * Mathf.Clamp01((m - dz) / (1f - dz)); }
    // A pedal axis that was bound but never calibrated still has the generic -1..1 guess; if its rest end is +1 it would read
    // 100 % (e.g. clutch held in -> car won't move). Until calibrated, read it relative to its first (rest) reading: |raw - rest| / range.
    public static bool UncalibratedPedal(Binding b, bool bip) => !bip && !b.cal.calibrated && b.cal.half == AxisHalf.Full && b.resolved is AxisControl
        && !(b.resolved is ButtonControl) && !(b.resolved.device is Gamepad) && !(b.resolved.device is Keyboard) && b.cal.min == -1f && b.cal.max == 1f;
    public static float RestRelative(Binding b)
    {
        float r = Raw(b.resolved); if (float.IsNaN(b.restRaw)) b.restRaw = r;
        return Mathf.Clamp01(Mathf.Abs(r - b.restRaw) / (b.cal.max - b.cal.min));
    }
    public int UncalibratedCount() { int n = 0; foreach (var s in profile.actions) foreach (var b in s.slots) if (b.resolved != null && UncalibratedPedal(b, IsBipolar(s.action))) n++; return n; }
    public bool Held(InAction a)
    {
        if (dirty) Resolve();
        if (!IsAxisAction(a)) { foreach (var b in profile.Get(a)) if (b.resolved != null && Debounced(b)) return true; return false; }
        foreach (var b in profile.Get(a)) if (b.resolved != null && (UncalibratedPedal(b, false) ? RestRelative(b) : b.cal.Normalize(Raw(b.resolved), false)) > 0.5f) return true;
        return false;
    }
    public bool HeldFromNonKeyboard(InAction a)
    {
        foreach (var b in profile.Get(a)) if (b.resolved != null && !(b.resolved.device is Keyboard) && (IsAxisAction(a) ? b.cal.Normalize(Raw(b.resolved), false) > 0.5f : Debounced(b))) return true;
        return false;
    }
    public bool HasNonKeyboard(InAction a) { foreach (var b in profile.Get(a)) if (b.resolved != null && !(b.resolved.device is Keyboard)) return true; return false; }
    // ---------- edge detection / menu gating ----------
    // One physical press = exactly one Pressed() == true, regardless of how often Pressed() is called per input update.
    // latched[a] stays true until every binding of a has been released (and no sub-frame tap arrived) for one update.
    class Edge { public uint update = uint.MaxValue; public bool result, latched; }
    readonly Dictionary<InAction, Edge> edges = new Dictionary<InAction, Edge>();
    uint flushUpdate = uint.MaxValue; bool menuOpen;
    public static bool IsMenuAction(InAction a) => a == InAction.Menu || (a >= InAction.MenuUp && a <= InAction.MenuSelect) || a == InAction.MenuTabNext || a == InAction.MenuTabPrev;
    public static bool IsGameplay(InAction a) => !IsMenuAction(a);
    /// While true, Pressed() of every gameplay action returns false (shift, gears, reset, ignition, ...). Opening or closing flushes all edges:
    /// a button held (or tapped) at that moment fires nothing until it has been released and pressed again -> no queued presses.
    public bool MenuOpen { get => menuOpen; set { if (value != menuOpen) { menuOpen = value; Flush(); } } }
    /// Discard pending presses: every action is latched to its current state, taps of the current input update are ignored.
    public void Flush()
    {
        if (dirty) Resolve();
        flushUpdate = InputState.updateCount;
        foreach (InAction a in Enum.GetValues(typeof(InAction))) { var e = E(a); e.latched = Held(a); e.update = InputState.updateCount; e.result = false; }
    }
    Edge E(InAction a) { if (!edges.TryGetValue(a, out var e)) edges[a] = e = new Edge(); return e; }
    bool Tap(InAction a, HashSet<InputControl> exclude)
    {
        foreach (var b in profile.Get(a)) if (b.resolved is ButtonControl bc && (exclude == null || !exclude.Contains(bc)) && bc.wasPressedThisFrame) return true;   // catches sub-frame taps
        return false;
    }
    bool HeldExcluding(InAction a, HashSet<InputControl> exclude)
    {
        foreach (var b in profile.Get(a)) if (b.resolved != null && (exclude == null || !exclude.Contains(b.resolved)) && (UncalibratedPedal(b, false) ? RestRelative(b) : b.cal.Normalize(Raw(b.resolved), false)) > 0.5f) return true;
        return false;
    }
    public const float PressOn = 0.65f, PressOff = 0.35f;   // hysteresis for analog controls driving button actions: noise at a threshold never re-triggers
    /// 0..1 'how pressed' of one binding for a BUTTON action. An axis bound to a button action is read relative to its rest
    /// (calibrated rest, else the first reading after resolve), so an axis resting at -1 or +1 never reads as held.
    public static float ButtonLevel(Binding b)
    {
        if (b.resolved is ButtonControl bc) return bc.isPressed ? 1f : 0f;
        if (b.cal.hasRest) return b.cal.Normalize(Raw(b.resolved), false);
        float r = Raw(b.resolved); if (float.IsNaN(b.restRaw)) b.restRaw = r;
        return Mathf.Clamp01(Mathf.Abs(r - b.restRaw));   // full travel of a -1..1 axis away from rest = 2 -> clamped; half travel = pressed
    }
    bool LevelExcluding(InAction a, HashSet<InputControl> exclude, bool latched)
    {
        if (IsAxisAction(a)) return HeldExcluding(a, exclude);
        foreach (var b in profile.Get(a)) if (b.resolved != null && (exclude == null || !exclude.Contains(b.resolved)) && Debounced(b)) return true;
        return false;
    }
    /// real-time clock for debounce (seconds); unscaled, independent of physics ticks and timeScale (menu pauses). Replaceable in tests.
    public static Func<double> Clock = () => Time.realtimeSinceStartupAsDouble;
    /// Debounced pressed state of one binding of a BUTTON action. Sampled once per input update (Update, never FixedUpdate).
    /// A raw change registers only after the raw state has stayed the same for profile.debounceMs of real time.
    /// Chatter = raw changes that were rejected (incl. sub-frame release/press pairs seen through the Input System event stream).
    public bool Debounced(Binding b)
    {
        uint u = InputState.updateCount; if (b.dbUpdate == u) return b.dbStable; b.dbUpdate = u;
        double now = Clock(), T = Mathf.Max(0f, profile.debounceMs) * 1e-3;
        bool raw = ButtonLevel(b) > (b.dbStable ? PressOff : PressOn);
        if (!b.dbInit) { b.dbInit = true; b.dbRaw = b.dbStable = b.dbCand = raw; b.dbSince = now; return raw; }
        if (b.resolved is ButtonControl bc && raw == b.dbRaw && (bc.wasPressedThisFrame || bc.wasReleasedThisFrame)) b.chatter++;   // bounced and came back within one update
        if (raw != b.dbRaw) { if (raw == b.dbStable) b.chatter++; b.dbRaw = raw; }   // went back before the change was accepted -> chatter
        if (raw != b.dbCand) { b.dbCand = raw; b.dbSince = now; }
        if (b.dbCand != b.dbStable && (T <= 0 || now - b.dbSince >= T)) b.dbStable = b.dbCand;
        return b.dbStable;
    }
    /// call every input update (InputRouter.Update) so debounce timing never depends on which actions get queried
    public void SampleButtons() { if (dirty) Resolve(); foreach (var s in profile.actions) if (!IsAxisAction(s.action)) foreach (var b in s.slots) if (b.resolved != null) Debounced(b); }
    /// controls bound to any gameplay action: these never drive menu tab navigation (a wheel button bound to Gear Up must not flip tabs)
    public HashSet<InputControl> GameplayControls()
    {
        if (dirty) Resolve(); var set = new HashSet<InputControl>();
        foreach (var s in profile.actions) if (IsGameplay(s.action)) foreach (var b in s.slots) if (b.resolved != null) set.Add(b.resolved);
        return set;
    }
    /// edge-triggered press (rising edge only, never repeats while held). Safe to call several times per frame.
    public bool Pressed(InAction a)
    {
        if (dirty) Resolve();
        var e = E(a); uint u = InputState.updateCount;
        if (e.update != u)
        {
            var ex = (a == InAction.MenuTabNext || a == InAction.MenuTabPrev) ? GameplayControls() : null;
            bool active = LevelExcluding(a, ex, e.latched);
            bool tap = profile.debounceMs <= 0f && u != flushUpdate && Tap(a, ex);   // debounce OFF (0 ms) only: raw sub-frame taps count, as before
            e.result = (active && !e.latched) || tap;   // debounce ON: rising edge of the DEBOUNCED state only -> one physical press = one shift, chatter ignored
            active |= tap;   // a tap = a new press transition in this update (release+press within one frame still counts once)
            e.latched = active; e.update = u;
        }
        if (menuOpen && IsGameplay(a)) return false;
        bool r = e.result; e.result = false; return r;   // consumed: a second query in the same update gets false
    }

    // ---------- steering: user wheel angle -> steering column -> rack (fixed ratio) ----------
    /// Returns handwheel (column) angle in degrees. Wheel source: column angle = user's wheel angle (1:1, soft lock at the user's
    /// rotation limit) or scaled so the user's lock reaches the car's lock. Stick/keys: rate-limited towards full lock (as before).
    public float handwheelDeg, wheelDegUnclamped;   // unclamped: physical wheel angle beyond the car's rack stop (FFB soft lock)
    public float StepSteer(float dt, float carLockDeg, float steerRateRoadDegPerS, float steeringRatio)
    {
        float v = Axis(InAction.Steer, out bool anyWheel, out float wheel, out float digital);
        float half = 0.5f * profile.RotationDeg(carLockDeg);
        if (Mathf.Abs(v) > 0f || !anyWheel)
        {
            float target = v * carLockDeg;
            handwheelDeg = Mathf.MoveTowards(handwheelDeg, target, steerRateRoadDegPerS * steeringRatio * dt);
        }
        else handwheelDeg = profile.mapping == SteerMapping.RealColumn1to1 ? wheel * half : wheel * carLockDeg;
        wheelDegUnclamped = anyWheel && Mathf.Abs(v) == 0f ? (profile.mapping == SteerMapping.RealColumn1to1 ? wheel * half : wheel * carLockDeg) : handwheelDeg;
        handwheelDeg = Mathf.Clamp(handwheelDeg, -carLockDeg, carLockDeg);   // car's rack end stop
        return handwheelDeg;
    }

    // ---------- capture (rebinding) ----------
    readonly Dictionary<InputControl, float> snap = new Dictionary<InputControl, float>();
    public void BeginCapture()
    {
        snap.Clear();
        foreach (var d in InputSystem.devices) { if (Skip(d)) continue; foreach (var c in d.allControls) if (c is AxisControl ax && !c.synthetic && !c.noisy) snap[c] = ax.ReadUnprocessedValue(); }
    }
    /// returns the control that moved/was pressed most since BeginCapture (axis: change > 0.4 of a -1..1 range; button: pressed), else null
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
    /// rest baseline of a control recorded at BeginCapture (NaN if not snapshotted)
    public float CaptureBaseline(InputControl c) => snap.TryGetValue(c, out var v) ? v : float.NaN;
    /// remove every non-keyboard binding of an action (keyboard defaults stay so the action is never unreachable)
    public int ClearDeviceBindings(InAction a) { int n = profile.Get(a).RemoveAll(b => b.layout != "Keyboard"); MarkDirty(); Flush(); return n; }
    /// call when a capture finished (bound or cancelled): the press that was captured is consumed and fires no action
    public void EndCapture() { snap.Clear(); MarkDirty(); Flush(); }
    /// bind the captured control to action a (no duplicate slot) and end the capture
    public Binding CompleteCapture(InAction a, InputControl c)
    {
        var b = FromControl(c, true);
        if (IsBipolar(a) && !(c is ButtonControl) && !(c.parent is StickControl)) b.cal.center = 0f;
        if (!IsBipolar(a) && !(c is ButtonControl) && !(c.parent is StickControl)) b.cal = new AxisCal { min = -1f, max = 1f, center = 0f };   // pedal axis: calibrate!
        if (!IsAxisAction(a) && !(c is ButtonControl))   // axis on a button action (e.g. pedal as Gear Up): rest = capture baseline, pressed = direction it moved
        { float r0 = CaptureBaseline(c), v = Raw(c); if (!float.IsNaN(r0)) { b.cal = new AxisCal(); b.cal.ApplyCapture(Mathf.Min(r0, v), Mathf.Max(r0, v), r0, false); } }
        var slots = profile.Get(a); var same = slots.Find(x => x.product == b.product && x.layout == b.layout && x.index == b.index && x.control == b.control);
        if (same == null) slots.Add(b); else b = same;
        EndCapture(); return b;
    }

    // ---------- defaults & persistence ----------
    public static BindingProfile Defaults()
    {
        var p = new BindingProfile();
        void K(InAction a, string key, bool neg = false) => p.Get(a).Add(new Binding { layout = "Keyboard", control = key, negative = neg, cal = new AxisCal() });
        void G(InAction a, string ctl, bool stick = false, bool neg = false) => p.Get(a).Add(new Binding { layout = "Gamepad", control = ctl, negative = neg, steerSource = SteerSource.Stick,
            cal = stick ? new AxisCal { min = -1f, max = 1f, center = 0f, deadzone = 0.1f } : new AxisCal() });   // stick deadzone (user-editable) against stick drift
        K(InAction.Steer, "a", true); K(InAction.Steer, "leftArrow", true); K(InAction.Steer, "d"); K(InAction.Steer, "rightArrow");
        K(InAction.Throttle, "w"); K(InAction.Throttle, "upArrow"); K(InAction.Brake, "s"); K(InAction.Brake, "downArrow");
        K(InAction.Clutch, "leftShift"); K(InAction.Handbrake, "space"); K(InAction.ShiftUp, "e"); K(InAction.ShiftDown, "q");
        K(InAction.Gear1, "digit1"); K(InAction.Gear2, "digit2"); K(InAction.Gear3, "digit3"); K(InAction.Gear4, "digit4"); K(InAction.Gear5, "digit5"); K(InAction.GearR, "digit6");
        K(InAction.Reset, "r"); K(InAction.ProfileToggle, "p"); K(InAction.Menu, "f1"); K(InAction.Menu, "escape");
        G(InAction.Steer, "leftStick/x", true); G(InAction.Throttle, "rightTrigger"); G(InAction.Brake, "leftTrigger"); G(InAction.Clutch, "leftShoulder");
        G(InAction.ShiftUp, "rightShoulder"); G(InAction.ShiftDown, "buttonWest"); G(InAction.Handbrake, "buttonEast"); G(InAction.Reset, "select");
        AddV2(p); AddV4(p); AddV5(p);
        G(InAction.Menu, "start"); G(InAction.ProfileToggle, "dpad/up"); G(InAction.LookX, "rightStick/x", true); G(InAction.LookY, "rightStick/y", true);
        return p;
    }
    public static readonly InAction[] V2Actions = { InAction.Recenter, InAction.CameraToggle, InAction.HudToggle, InAction.MenuUp, InAction.MenuDown, InAction.MenuLeft, InAction.MenuRight, InAction.MenuSelect, InAction.Ignition, InAction.Starter };
    static void AddV2(BindingProfile p)   // defaults for actions added in profile version 2 (VR / menu navigation); wheel buttons can be bound to them in the menu
    {
        void K(InAction a, string key) => p.Get(a).Add(new Binding { layout = "Keyboard", control = key, cal = new AxisCal() });
        void G(InAction a, string ctl) => p.Get(a).Add(new Binding { layout = "Gamepad", control = ctl, steerSource = SteerSource.Stick, cal = new AxisCal() });
        K(InAction.Recenter, "f12"); K(InAction.Recenter, "home"); G(InAction.Recenter, "rightStickPress"); K(InAction.CameraToggle, "c"); K(InAction.HudToggle, "h");
        K(InAction.MenuUp, "upArrow"); K(InAction.MenuDown, "downArrow"); K(InAction.MenuLeft, "leftArrow"); K(InAction.MenuRight, "rightArrow"); K(InAction.MenuSelect, "enter"); K(InAction.MenuSelect, "numpadEnter");
        K(InAction.Ignition, "i"); K(InAction.Starter, "t"); G(InAction.Ignition, "leftStickPress"); G(InAction.Starter, "buttonNorth");
        G(InAction.MenuUp, "dpad/up"); G(InAction.MenuDown, "dpad/down"); G(InAction.MenuLeft, "dpad/left"); G(InAction.MenuRight, "dpad/right"); G(InAction.MenuSelect, "buttonSouth");
    }
    public static readonly InAction[] V4Actions = { InAction.MenuTabNext, InAction.MenuTabPrev };
    static void AddV4(BindingProfile p)   // v4: dedicated menu tab navigation (was ShiftUp/ShiftDown -> gear paddles flipped tabs). Keys not used by gameplay.
    {
        void K(InAction a, string key) => p.Get(a).Add(new Binding { layout = "Keyboard", control = key, cal = new AxisCal() });
        K(InAction.MenuTabNext, "pageDown"); K(InAction.MenuTabNext, "tab"); K(InAction.MenuTabPrev, "pageUp");
    }
    public static readonly InAction[] V5Actions = { InAction.ResetToStart };
    static void AddV5(BindingProfile p) { p.Get(InAction.ResetToStart).Add(new Binding { layout = "Keyboard", control = "backspace", cal = new AxisCal() }); }   // v5: reset to start/finish
    public static void Migrate(BindingProfile p)   // older saved profiles: add new defaults only to actions that have no binding yet
    {
        if (p.version >= 5) return;
        if (p.version == 4) { var d5 = new BindingProfile(); AddV5(d5); foreach (var a in V5Actions) if (p.Get(a).Count == 0) p.Get(a).AddRange(d5.Get(a)); p.version = 5; return; }
        if (p.version < 3) { var d = new BindingProfile(); AddV2(d); foreach (var a in V2Actions) if (p.Get(a).Count == 0) p.Get(a).AddRange(d.Get(a)); }
        var d4 = new BindingProfile(); AddV4(d4); foreach (var a in V4Actions) if (p.Get(a).Count == 0) p.Get(a).AddRange(d4.Get(a));
        { var d5 = new BindingProfile(); AddV5(d5); foreach (var a in V5Actions) if (p.Get(a).Count == 0) p.Get(a).AddRange(d5.Get(a)); }
        p.version = 5;
    }
    public static string DefaultPath => System.IO.Path.Combine(Application.persistentDataPath, "input_bindings.json");
    public static void Save(BindingProfile p, string path = null) { System.IO.File.WriteAllText(path ?? DefaultPath, JsonUtility.ToJson(p, true)); PlayerPrefs.SetString("input_bindings_path", path ?? DefaultPath); PlayerPrefs.Save(); }
    public static BindingProfile LoadOrDefault(string path = null)
    {
        path = path ?? DefaultPath;
        try { if (System.IO.File.Exists(path)) { var p = JsonUtility.FromJson<BindingProfile>(System.IO.File.ReadAllText(path)); if (p != null && p.actions.Count > 0) { Migrate(p); return p; } } }
        catch (Exception e) { Debug.LogWarning("input bindings: could not load " + path + ": " + e.Message); }
        return Defaults();
    }
}
