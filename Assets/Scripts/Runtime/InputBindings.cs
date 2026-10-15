// Device-agnostic input bindings on top of the Unity Input System (com.unity.inputsystem).
// Every action can be bound to any axis/button of any connected device (keyboard, gamepad, HID wheel/pedals/shifter/handbrake).
// Bindings are stored by device identity (product name + layout + index among identical devices) and re-resolved on reconnect.
// Calibration is explicit per binding (min/max/center capture, invert, deadzone, half-axis for combined pedals). No assists, no gains.
using System; using System.Collections.Generic; using UnityEngine;
using UnityEngine.InputSystem; using UnityEngine.InputSystem.Controls; using UnityEngine.InputSystem.LowLevel;

public enum InAction { Steer, Throttle, Brake, Clutch, Handbrake, ShiftUp, ShiftDown, Gear1, Gear2, Gear3, Gear4, Gear5, GearR, Reset, Menu, LookX, LookY, Recenter, CameraToggle, HudToggle, MenuUp, MenuDown, MenuLeft, MenuRight, MenuSelect, Ignition, Starter, MenuTabNext, MenuTabPrev, ResetToStart }   // saved as ints in the settings file; changing the order or removing a member requires bumping BindingProfile.CurrentVersion
public enum AxisHalf { Full, Positive, Negative }      // Positive/Negative: use one half of a combined pedal axis (center -> max / center -> min)
public enum SteerSource { Wheel, Stick }               // Wheel: absolute angle (user's wheel = steering column). Stick/keys: target lock, rate-limited like the keyboard
public enum SteerMapping { RealColumn1to1, ScaleToCarLock }

[Serializable] public class AxisCalibration
{
    public float min = 0f, max = 1f, center = 0f; public bool invert; public bool calibrated; public float deadzone = 0f; public AxisHalf half = AxisHalf.Full;
    public bool hasRest; public float rest;             // full-range pedal: raw value at rest (maps to 0); the far end of the captured range maps to 1
    public float PressedEnd => invert ? min : max;
    // @description - Stores a calibration capture (the user moves a pedal or wheel through its full travel). Rejects a capture whose travel is under 0.05 so a pedal that never moved keeps its old calibration. For a combined (bipolar) axis the rest value becomes the center; for a full-range pedal the end nearer the rest value is treated as released and the axis is inverted if that end is the maximum.
    // @param {float} capturedMin - smallest raw axis value seen during the capture (raw device units, normally -1..1)
    // @param {float} capturedMax - largest raw axis value seen during the capture (raw device units)
    // @param {float} restRaw - raw axis value when the control was at rest (raw device units)
    // @param {bool} bipolar - true for wheels and sticks that are centered at rest, false for pedals that rest at one end
    // @return {bool} - true if the capture was accepted, false if the control barely moved and the old calibration was kept
    public bool ApplyCapture(float capturedMin, float capturedMax, float restRaw, bool bipolar)
    {
        if (capturedMax - capturedMin < 0.05f) return false;      // pedal/wheel never moved: keep previous calibration
        min = capturedMin; max = capturedMax; calibrated = true;
        if (bipolar || half != AxisHalf.Full) { center = restRaw; hasRest = false; return true; }
        invert = Mathf.Abs(restRaw - max) < Mathf.Abs(restRaw - min);   // rest nearer max -> axis reads high when released -> inverted
        rest = restRaw; hasRest = true; return true;
    }
    // @description - Converts a raw device axis value into the 0..1 (pedal) or -1..1 (wheel, stick) range the sim uses, applying the stored calibration: center, half-axis selection, rest position, inversion and a dead zone that is subtracted and the remainder rescaled to full range.
    // @param {float} rawValue - raw axis value from the device (raw device units, normally -1..1)
    // @param {bool} bipolar - true for a centered axis returning -1..1, false for a pedal returning 0..1
    // @return {float} - calibrated value, -1..1 if bipolar, otherwise 0..1 (dimensionless)
    public float Normalize(float rawValue, bool bipolar)
    {
        float normalized;
        if (bipolar)
        {
            normalized = rawValue >= center ? (max > center ? (rawValue - center) / (max - center) : 0f) : (center > min ? -(center - rawValue) / (center - min) : 0f);
            if (invert) normalized = -normalized;
            float magnitude = Mathf.Abs(normalized); normalized = magnitude <= deadzone ? 0f : Mathf.Sign(normalized) * (magnitude - deadzone) / Mathf.Max(1e-4f, 1f - deadzone);
            return Mathf.Clamp(normalized, -1f, 1f);
        }
        if (half == AxisHalf.Positive) normalized = max > center ? (rawValue - center) / (max - center) : 0f;
        else if (half == AxisHalf.Negative) normalized = center > min ? (center - rawValue) / (center - min) : 0f;
        else if (hasRest) { float pressedEnd = PressedEnd; normalized = Mathf.Abs(pressedEnd - rest) > 1e-4f ? (rawValue - rest) / (pressedEnd - rest) : 0f; normalized = Mathf.Clamp01(normalized); goto dz; }   // rest -> 0, pressed end -> 1 (direction handles invert)
        else normalized = max != min ? (rawValue - min) / (max - min) : 0f;
        normalized = Mathf.Clamp01(normalized); if (invert) normalized = 1f - normalized;
        dz:
        normalized = normalized <= deadzone ? 0f : (normalized - deadzone) / Mathf.Max(1e-4f, 1f - deadzone);
        return Mathf.Clamp01(normalized);
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
    [NonSerialized] public bool debounceRawState, debounceStableState, debounceCandidateState, debounceInitialized; [NonSerialized] public double debounceCandidateSince; [NonSerialized] public uint debounceLastUpdateId = uint.MaxValue; [NonSerialized] public int chatterCount;   // first reading of an uncalibrated pedal axis = its rest position
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
    public bool experimentalDrivelineEnabled = false;   // Experimental tab: driveline shuffle model toggle (Driveline/*.cs). A new JSON field: files without it load as false, so no version bump and no binding reset
    public bool experimentalTransmissionEnabled = false;   // Experimental tab: transmission and synchronizer model toggle (Transmission/*.cs). Independent of experimentalDrivelineEnabled. New JSON field: absent in old files = false, no version bump
    public bool experimentalBrakesEnabled = false;   // Experimental tab: physics-derived braking model toggle (Brakes/*.cs). New JSON field: absent in old files = false, no version bump
    public bool loadCellBrakePedalEnabled = false;   // Experimental tab: "Load-cell brake pedal" checkbox. New JSON field, default false
    public float loadCellBrakePedalMaxForcePounds = 70f;   // lbf pedal force at brake axis 1, entered by the user (U: user-reported default for a Heusinkveld Sprint, about 311 N)
    public bool tireThermal = false; public float frontColdPressurePsi = 24.7f, rearColdPressurePsi = 24.7f;   // tire thermal model option; cold pressures in psi (VW 1.7 bar = 24.7 psi)
    public float audioVolume = 0.8f, tireVolume = 1f, ambientBrightness = 1.3f; public bool audioMute; public bool hudVisible = true;   // H: gray info boxes (telemetry / FFB / joints) shown   // master volume 0..1   // autoClutch: for keyboard/gamepad drivers without a clutch pedal (option)   // spawn with engine stopped; starter only with clutch fully in or neutral (1984 car had no interlock)
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
    // @description - Wheel rotation the user's wheel covers lock to lock. Uses the value from the settings, or twice the car's lock when the setting is 0 (the car's own range).
    // @param {float} carLockDeg - car steering lock from center to one side, in degrees of handwheel rotation
    // @return {float} - lock-to-lock rotation of the user's wheel in degrees
    public float RotationDeg(float carLockDeg) => wheelRotationDeg > 0f ? wheelRotationDeg : 2f * carLockDeg;
    public SteerMapping mapping = SteerMapping.RealColumn1to1;
    public List<ActionSlots> actions = new List<ActionSlots>();
    // @description - Returns the list of bindings for an action, creating an empty slot list the first time an action is asked for.
    // @param {InAction} action - action whose bindings are wanted
    // @return {List<Binding>} - the mutable list of bindings for that action (never null)
    public List<Binding> Get(InAction action) { var actionSlots = actions.Find(entry => entry.action == action); if (actionSlots == null) { actionSlots = new ActionSlots { action = action }; actions.Add(actionSlots); } return actionSlots.slots; }
}

public class InputMapper
{
    public BindingProfile profile; bool dirty = true;
    readonly Dictionary<InAction, bool> prevPressed = new Dictionary<InAction, bool>();
    // @description - Tells whether an action is a centered axis (steering and the two look axes, -1..1) rather than a pedal or button (0..1).
    // @param {InAction} action - action to test
    // @return {bool} - true for Steer, LookX and LookY
    public static bool IsBipolar(InAction action) => action == InAction.Steer || action == InAction.LookX || action == InAction.LookY;
    // @description - Tells whether an action is read as an analog axis (pedals, handbrake, steering and look) rather than as a button.
    // @param {InAction} action - action to test
    // @return {bool} - true for analog actions, false for button actions such as shifting and menu keys
    public static bool IsAxisAction(InAction action) => action <= InAction.Handbrake || action == InAction.LookX || action == InAction.LookY;

    // @description - Creates the mapper for a binding profile and starts listening for devices being plugged or unplugged so bindings are re-resolved.
    // @param {BindingProfile} initialProfile - binding profile this mapper reads
    // @return {Void} - nothing (constructor)
    public InputMapper(BindingProfile initialProfile) { profile = initialProfile; InputSystem.onDeviceChange += OnDeviceChange; }
    // @description - Stops listening for device changes. Call when the mapper is no longer needed so the static event does not keep it alive.
    // @return {Void} - nothing
    public void Dispose() { InputSystem.onDeviceChange -= OnDeviceChange; }
    // @description - Marks all bindings for re-resolution when a device is added, removed or reconnected.
    // @param {InputDevice} device - device that changed
    // @param {InputDeviceChange} change - kind of change (added, removed, reconnected, ...)
    // @return {Void} - nothing
    void OnDeviceChange(InputDevice device, InputDeviceChange change) { dirty = true; }   // added/removed/reconnected -> re-resolve all bindings
    // @description - Forces every binding to be matched to a live device control again on the next read, for example after the bindings were edited.
    // @return {Void} - nothing
    public void MarkDirty() { dirty = true; }

    // ---------- device identity ----------
    // @description - Names the broad kind of a device so wildcard bindings ("any keyboard", "any gamepad") can match it.
    // @param {InputDevice} device - input device to classify
    // @return {string} - "Keyboard", "Gamepad", "Joystick", or the device's own layout name
    public static string Category(InputDevice device) => device is Keyboard ? "Keyboard" : device is Gamepad ? "Gamepad" : device is Joystick ? "Joystick" : device.layout;
    // @description - Tells whether a device is the one a binding refers to: by product name and layout for a specific device, or by category for a wildcard binding with no product.
    // @param {InputDevice} device - connected input device
    // @param {Binding} binding - binding to test
    // @return {bool} - true if the binding applies to this device
    static bool Matches(InputDevice device, Binding binding)
    {
        if (string.IsNullOrEmpty(binding.product)) return Category(device) == binding.layout || device.layout == binding.layout;    // wildcard: any keyboard / any gamepad
        return device.description.product == binding.product && device.layout == binding.layout;
    }
    // @description - Tells whether a device should be ignored when capturing a new binding (mouse, pointer, sensors).
    // @param {InputDevice} device - input device to test
    // @return {bool} - true if the device must not be offered for binding
    public static bool Skip(InputDevice device) => device is Mouse || device is Pointer || device is Sensor;
    // @description - Builds a binding for a control the user just moved. Keyboards and gamepads become wildcard bindings if requested; other devices are stored by product, layout and index among identical devices so two identical wheels stay distinct. Guards against a control path that does not start with the device path.
    // @param {InputControl} control - control that was moved
    // @param {bool} wildcard - true to bind any keyboard or gamepad instead of this exact device
    // @return {Binding} - a new binding with default calibration for that control
    public static Binding FromControl(InputControl control, bool wildcard)
    {
        var device = control.device; var binding = new Binding { control = control.path.Length > device.path.Length + 1 && control.path.StartsWith(device.path + "/") ? control.path.Substring(device.path.Length + 1) : control.name };   // guard: a control path that is not "<device path>/<control>" (e.g. the device itself) threw ArgumentOutOfRangeException (startIndex)
        if (wildcard && (device is Keyboard || device is Gamepad)) { binding.layout = Category(device); }
        else
        {
            binding.product = device.description.product ?? ""; binding.layout = device.layout;
            int sameDeviceIndex = 0; foreach (var otherDevice in InputSystem.devices) { if (otherDevice == device) break; if (otherDevice.layout == device.layout && otherDevice.description.product == device.description.product) sameDeviceIndex++; }
            binding.index = sameDeviceIndex; if (string.IsNullOrEmpty(binding.product)) { binding.layout = device.layout; }
        }
        binding.steerSource = (device is Keyboard || device is Gamepad) ? SteerSource.Stick : SteerSource.Wheel;
        binding.calibration = DefaultCalibration(control);
        return binding;
    }
    // @description - Starting calibration for a freshly bound control: buttons run 0..1, sticks and generic HID axes -1..1 until the user calibrates them.
    // @param {InputControl} control - control being bound
    // @return {AxisCalibration} - default calibration for that control
    public static AxisCalibration DefaultCalibration(InputControl control)
    {
        if (control is ButtonControl) return new AxisCalibration { min = 0f, max = 1f, center = 0f };
        if (control is AxisControl axis && axis.parent is StickControl) return new AxisCalibration { min = -1f, max = 1f, center = 0f };
        return new AxisCalibration { min = -1f, max = 1f, center = 0f };   // generic HID axis: assume -1..1 until calibrated
    }
    // @description - Matches every binding to a live device control (by product, layout, index and control path) and clears the cached rest values. Run after devices change or bindings are edited.
    // @return {Void} - nothing
    public void Resolve()
    {
        dirty = false;
        foreach (var actionSlots in profile.actions) foreach (var binding in actionSlots.slots)
        {
            binding.resolved = null; int matchingDeviceCount = 0;
            foreach (var device in InputSystem.devices)
            {
                if (!device.added || !Matches(device, binding)) continue;
                if (!string.IsNullOrEmpty(binding.product) && matchingDeviceCount++ != binding.index) continue;
                var control = device.TryGetChildControl(binding.control); if (control != null) { binding.resolved = control; binding.restRaw = float.NaN; break; }
            }
        }
    }
    // @description - Reads the unprocessed value of an axis control, skipping Input System processors such as the stick dead zone because calibration here is explicit.
    // @param {InputControl} control - control to read
    // @return {float} - raw axis value in device units (0 if the control is not an axis)
    public static float Raw(InputControl control) => control is AxisControl axisControl ? axisControl.ReadUnprocessedValue() : 0f;   // skip layout processors (e.g. stick deadzone); calibration is explicit

    // ---------- reading ----------
    // @description - Reads an action as a single value from all of its bindings. For centered axes the strongest analog value wins unless the summed digital keys are stronger; for pedals the largest value wins. A binding with steer source Wheel reports separately so the wheel can bypass the gamepad assist.
    // @param {InAction} action - action to read
    // @param {bool} anyWheel - set to true if a wheel-source binding is bound and resolved
    // @param {float} wheel - wheel-source value, -1..1 (0 if none)
    // @param {float} digital - summed key and button contribution, clamped to -1..1
    // @return {float} - combined value, -1..1 for centered axes and 0..1 for pedals (dimensionless)
    public float Axis(InAction action, out bool anyWheel, out float wheel, out float digital)
    {
        if (dirty) Resolve();
        bool isBipolar = IsBipolar(action); float strongestAnalog = 0f; anyWheel = false; wheel = 0f; digital = 0f;
        foreach (var binding in profile.Get(action))
        {
            if (binding.resolved == null) continue;
            float value = UncalibratedPedal(binding, isBipolar) ? RestRelative(binding) : binding.calibration.Normalize(Raw(binding.resolved), isBipolar && !(binding.resolved is ButtonControl)); if (binding.negative) value = -value;
            if (action == InAction.Steer && binding.steerSource == SteerSource.Wheel && !(binding.resolved is ButtonControl)) { anyWheel = true; wheel = value; continue; }
            if (isBipolar) { if (binding.resolved is ButtonControl) digital += value; else if (Mathf.Abs(value) > Mathf.Abs(strongestAnalog)) strongestAnalog = value; }
            else strongestAnalog = Mathf.Max(strongestAnalog, value);
        }
        if (isBipolar) { digital = Mathf.Clamp(digital, -1f, 1f); return Mathf.Abs(digital) > Mathf.Abs(strongestAnalog) ? digital : strongestAnalog; }
        return strongestAnalog;
    }
    // @description - Short form of Axis for callers that only need the combined value and not the wheel and digital breakdown.
    // @param {InAction} action - action to read
    // @return {float} - combined value, -1..1 for centered axes and 0..1 for pedals (dimensionless)
    public float Axis(InAction action) => Axis(action, out _, out _, out _);
    // @description - Describes which control is currently driving an action (device, control and value), for the bindings menu live display.
    // @param {InAction} action - action to describe
    // @return {string} - text such as "Wheel rz = 0.42" for the strongest active binding, or an empty string if none is active
    public string Source(InAction action)
    {
        if (dirty) Resolve(); string description = ""; float strongestValue = 0f; bool isBipolar = IsBipolar(action);
        foreach (var binding in profile.Get(action)) { if (binding.resolved == null) continue; float value = binding.calibration.Normalize(Raw(binding.resolved), isBipolar && !(binding.resolved is ButtonControl)); if (binding.negative) value = -value;
            if (Mathf.Abs(value) > Mathf.Abs(strongestValue)) { strongestValue = value; description = $"{(string.IsNullOrEmpty(binding.product) ? binding.layout : binding.product)} {binding.control} = {value:0.00}"; } }
        return description;
    }
    // @description - Applies the Input System default dead zone to a look-axis value and rescales the remainder so the output still reaches 1.
    // @param {float} axisValue - look axis value, -1..1
    // @return {float} - value with the dead zone removed, -1..1
    public static float LookDeadzone(float axisValue) { float deadzone = InputSystem.settings.defaultDeadzoneMin; float magnitude = Mathf.Abs(axisValue); return magnitude <= deadzone ? 0f : Mathf.Sign(axisValue) * Mathf.Clamp01((magnitude - deadzone) / (1f - deadzone)); }
    // @description - Detects a HID pedal axis the user has not calibrated yet (still at the default -1..1 range, not a gamepad or keyboard) so it can be read relative to its rest position instead of a wrong fixed range.
    // @param {Binding} binding - binding to test
    // @param {bool} isBipolar - true if the action is a centered axis (such bindings are never treated as pedals)
    // @return {bool} - true if the binding should use rest-relative reading
    public static bool UncalibratedPedal(Binding binding, bool isBipolar) => !isBipolar && !binding.calibration.calibrated && binding.calibration.half == AxisHalf.Full && binding.resolved is AxisControl
        && !(binding.resolved is ButtonControl) && !(binding.resolved.device is Gamepad) && !(binding.resolved.device is Keyboard) && binding.calibration.min == -1f && binding.calibration.max == 1f;
    // @description - Reads an uncalibrated pedal as its distance from the first value seen (assumed rest), divided by the axis span, clamped to 0..1.
    // @param {Binding} binding - uncalibrated pedal binding; its rest value is captured on the first call
    // @return {float} - pedal position, 0 (rest) to 1 (fully pressed)
    public static float RestRelative(Binding binding)
    {
        float rawValue = Raw(binding.resolved); if (float.IsNaN(binding.restRaw)) binding.restRaw = rawValue;
        return Mathf.Clamp01(Mathf.Abs(rawValue - binding.restRaw) / (binding.calibration.max - binding.calibration.min));
    }
    // @description - Counts bound and connected pedal axes that still need calibrating, for the menu warning.
    // @return {int} - number of uncalibrated pedal bindings
    public int UncalibratedCount() { int count = 0; foreach (var actionSlots in profile.actions) foreach (var binding in actionSlots.slots) if (binding.resolved != null && UncalibratedPedal(binding, IsBipolar(actionSlots.action))) count++; return count; }
    // @description - Tells whether an action is currently held: a debounced button, or an analog axis past half travel.
    // @param {InAction} action - action to test
    // @return {bool} - true while the action is held
    public bool Held(InAction action)
    {
        if (dirty) Resolve();
        if (!IsAxisAction(action)) { foreach (var binding in profile.Get(action)) if (binding.resolved != null && Debounced(binding)) return true; return false; }
        foreach (var binding in profile.Get(action)) if (binding.resolved != null && (UncalibratedPedal(binding, false) ? RestRelative(binding) : binding.calibration.Normalize(Raw(binding.resolved), false)) > 0.5f) return true;
        return false;
    }
    // @description - Same as Held but ignoring keyboard bindings, so the HUD can tell wheel and gamepad input from key input.
    // @param {InAction} action - action to test
    // @return {bool} - true if a non-keyboard binding holds the action
    public bool HeldFromNonKeyboard(InAction action)
    {
        foreach (var binding in profile.Get(action)) if (binding.resolved != null && !(binding.resolved.device is Keyboard) && (IsAxisAction(action) ? binding.calibration.Normalize(Raw(binding.resolved), false) > 0.5f : Debounced(binding))) return true;
        return false;
    }
    // @description - Tells whether any non-keyboard control is bound and connected for an action.
    // @param {InAction} action - action to test
    // @return {bool} - true if a wheel, pedal or gamepad control is bound
    public bool HasNonKeyboard(InAction action) { foreach (var binding in profile.Get(action)) if (binding.resolved != null && !(binding.resolved.device is Keyboard)) return true; return false; }
    // ---------- edge detection / menu gating ----------
    // One physical press = exactly one Pressed() == true, regardless of how often Pressed() is called per input update.
    // latched[a] stays true until every binding of a has been released (and no sub-frame tap arrived) for one update.
    class Edge { public uint update = uint.MaxValue; public bool result, latched; }
    readonly Dictionary<InAction, Edge> edges = new Dictionary<InAction, Edge>();
    uint flushUpdate = uint.MaxValue; bool menuOpen;
    // @description - Tells whether an action belongs to the menu (menu open/close, up/down/left/right/select, tab next/previous) as opposed to driving, so gameplay input can be suppressed while the menu is open.
    // @param {InAction} action - input action to classify
    // @return {bool} - true for menu actions, false for gameplay actions
    public static bool IsMenuAction(InAction action) => action == InAction.Menu || (action >= InAction.MenuUp && action <= InAction.MenuSelect) || action == InAction.MenuTabNext || action == InAction.MenuTabPrev;
    // @description - Tells whether an action is a gameplay (driving) action, i.e. not a menu action; these are ignored while the menu is open.
    // @param {InAction} action - input action to classify
    // @return {bool} - true for gameplay actions, false for menu actions
    public static bool IsGameplay(InAction action) => !IsMenuAction(action);
    /// While true, Pressed() of every gameplay action returns false (shift, gears, reset, ignition, ...). Opening or closing flushes all edges:
    /// a button held (or tapped) at that moment fires nothing until it has been released and pressed again -> no queued presses.
    public bool MenuOpen { get => menuOpen; set { if (value != menuOpen) { menuOpen = value; Flush(); } } }
    // @description - Throws away pending button edges and marks everything currently held as already used, so a button held when the menu opens or closes does not fire later.
    // @return {Void} - nothing
    public void Flush()
    {
        if (dirty) Resolve();
        flushUpdate = InputState.updateCount;
        foreach (InAction action in Enum.GetValues(typeof(InAction))) { var edge = GetEdgeState(action); edge.latched = Held(action); edge.update = InputState.updateCount; edge.result = false; }
    }
    // @description - Returns the stored press-edge state for an action, creating it on first use.
    // @param {InAction} action - action to look up
    // @return {Edge} - the edge state object for that action
    Edge GetEdgeState(InAction action) { if (!edges.TryGetValue(action, out var edge)) edges[action] = edge = new Edge(); return edge; }
    // @description - Detects a press that began and ended inside one frame (a tap) on a button binding, which a per-frame held check would miss.
    // @param {InAction} action - action to test
    // @param {HashSet<InputControl>} exclude - controls to ignore (used to keep gameplay keys from driving menu tabs), may be null
    // @return {bool} - true if a bound button was pressed this frame
    bool Tap(InAction action, HashSet<InputControl> exclude)
    {
        foreach (var binding in profile.Get(action)) if (binding.resolved is ButtonControl buttonControl && (exclude == null || !exclude.Contains(buttonControl)) && buttonControl.wasPressedThisFrame) return true;   // catches sub-frame taps
        return false;
    }
    // @description - Tells whether an analog binding of an action is past half travel, skipping the controls in an exclusion set.
    // @param {InAction} action - action to test
    // @param {HashSet<InputControl>} exclude - controls to ignore, may be null
    // @return {bool} - true if any non-excluded binding is past 0.5
    bool HeldExcluding(InAction action, HashSet<InputControl> exclude)
    {
        foreach (var binding in profile.Get(action)) if (binding.resolved != null && (exclude == null || !exclude.Contains(binding.resolved)) && (UncalibratedPedal(binding, false) ? RestRelative(binding) : binding.calibration.Normalize(Raw(binding.resolved), false)) > 0.5f) return true;
        return false;
    }
    public const float PressOn = 0.65f, PressOff = 0.35f;   // hysteresis for analog controls driving button actions: noise at a threshold never re-triggers
    // @description - Reads a binding as a 0..1 pressed level for button actions: a button gives 0 or 1, a calibrated pedal its calibrated value, an uncalibrated axis its distance from rest.
    // @param {Binding} binding - binding to read
    // @return {float} - pressed level, 0 (released) to 1 (fully pressed)
    public static float ButtonLevel(Binding binding)
    {
        if (binding.resolved is ButtonControl buttonControl) return buttonControl.isPressed ? 1f : 0f;
        if (binding.calibration.hasRest) return binding.calibration.Normalize(Raw(binding.resolved), false);
        float rawValue = Raw(binding.resolved); if (float.IsNaN(binding.restRaw)) binding.restRaw = rawValue;
        return Mathf.Clamp01(Mathf.Abs(rawValue - binding.restRaw));   // full travel of a -1..1 axis away from rest = 2 -> clamped; half travel = pressed
    }
    // @description - Tells whether an action is active, using the axis threshold for analog actions and the debounced state for buttons, skipping excluded controls.
    // @param {InAction} action - action to test
    // @param {HashSet<InputControl>} exclude - controls to ignore, may be null
    // @param {bool} latched - whether the action was already latched (kept for the caller's signature, not used in the decision)
    // @return {bool} - true if the action is active
    bool LevelExcluding(InAction action, HashSet<InputControl> exclude, bool latched)
    {
        if (IsAxisAction(action)) return HeldExcluding(action, exclude);
        foreach (var binding in profile.Get(action)) if (binding.resolved != null && (exclude == null || !exclude.Contains(binding.resolved)) && Debounced(binding)) return true;
        return false;
    }
    /// real-time clock for debounce (seconds); unscaled, independent of physics ticks and timeScale (menu pauses). Replaceable in tests.
    public static Func<double> Clock = () => Time.realtimeSinceStartupAsDouble;
    // @description - Returns the debounced pressed state of a button binding. A change must stay stable for the profile's debounce time (real seconds) before it is accepted; analog controls use hysteresis (on at 0.65, off at 0.35). Counts chatter (a change that reverts before being accepted). Evaluated once per input update.
    // @param {Binding} binding - binding to read
    // @return {bool} - debounced pressed state
    public bool Debounced(Binding binding)
    {
        uint updateId = InputState.updateCount; if (binding.debounceLastUpdateId == updateId) return binding.debounceStableState; binding.debounceLastUpdateId = updateId;
        double now = Clock(), debounceSeconds = Mathf.Max(0f, profile.debounceMs) * 1e-3;
        bool rawPressed = ButtonLevel(binding) > (binding.debounceStableState ? PressOff : PressOn);
        if (!binding.debounceInitialized) { binding.debounceInitialized = true; binding.debounceRawState = binding.debounceStableState = binding.debounceCandidateState = rawPressed; binding.debounceCandidateSince = now; return rawPressed; }
        if (binding.resolved is ButtonControl buttonControl && rawPressed == binding.debounceRawState && (buttonControl.wasPressedThisFrame || buttonControl.wasReleasedThisFrame)) binding.chatterCount++;   // bounced and came back within one update
        if (rawPressed != binding.debounceRawState) { if (rawPressed == binding.debounceStableState) binding.chatterCount++; binding.debounceRawState = rawPressed; }   // went back before the change was accepted -> chatterCount
        if (rawPressed != binding.debounceCandidateState) { binding.debounceCandidateState = rawPressed; binding.debounceCandidateSince = now; }
        if (binding.debounceCandidateState != binding.debounceStableState && (debounceSeconds <= 0 || now - binding.debounceCandidateSince >= debounceSeconds)) binding.debounceStableState = binding.debounceCandidateState;
        return binding.debounceStableState;
    }
    // @description - Updates the debounce state of every button binding once; call every frame so the debounce timers keep running even when nobody asks for a press.
    // @return {Void} - nothing
    public void SampleButtons() { if (dirty) Resolve(); foreach (var actionSlots in profile.actions) if (!IsAxisAction(actionSlots.action)) foreach (var binding in actionSlots.slots) if (binding.resolved != null) Debounced(binding); }
    // @description - Collects every control bound to a gameplay action, so menu-tab navigation can ignore keys that are also used for driving.
    // @return {HashSet<InputControl>} - set of resolved controls used by gameplay actions
    public HashSet<InputControl> GameplayControls()
    {
        if (dirty) Resolve(); var set = new HashSet<InputControl>();
        foreach (var actionSlots in profile.actions) if (IsGameplay(actionSlots.action)) foreach (var binding in actionSlots.slots) if (binding.resolved != null) set.Add(binding.resolved);
        return set;
    }
    // @description - Returns true exactly once per physical press of an action (rising edge), however often it is called in an update. Gameplay actions return false while the menu is open. Sub-frame taps count when debounce is off.
    // @param {InAction} action - action to test
    // @return {bool} - true once on the update where the press starts
    public bool Pressed(InAction action)
    {
        if (dirty) Resolve();
        var edge = GetEdgeState(action); uint updateId = InputState.updateCount;
        if (edge.update != updateId)
        {
            var excludedControls = (action == InAction.MenuTabNext || action == InAction.MenuTabPrev) ? GameplayControls() : null;
            bool active = LevelExcluding(action, excludedControls, edge.latched);
            bool tap = profile.debounceMs <= 0f && updateId != flushUpdate && Tap(action, excludedControls);   // debounce OFF (0 ms) only: raw sub-frame taps count, as before
            edge.result = (active && !edge.latched) || tap;   // debounce ON: rising edge of the DEBOUNCED state only -> one physical press = one shift, chatterCount ignored
            active |= tap;   // a tap = a new press transition in this update (release+press within one frame still counts once)
            edge.latched = active; edge.update = updateId;
        }
        if (menuOpen && IsGameplay(action)) return false;
        bool pressed = edge.result; edge.result = false; return pressed;   // consumed: a second query in the same update gets false
    }

    // ---------- steering: user wheel angle -> steering column -> rack (constant pinion) ----------
    /// Returns handwheel (column) angle in degrees. Wheel source: column angle = user's wheel angle (1:1, soft lock at the user's
    /// rotation limit) or scaled so the user's lock reaches the car's lock. Stick/keys: rate-limited towards full lock (as before).
    public float handwheelDeg, wheelDegUnclamped;
    public GamepadSteering assist; public GamepadSteerContext? assistCtx; public Func<float, float> meanToHandwheel;   // set by InputRouter each frame   // unclamped: physical wheel angle beyond the car's rack stop (FFB soft lock)
    // @description - Advances the steering column by one frame. A real wheel (source Wheel, no stick or key input) drives the column directly, 1:1 or scaled to the car's lock. Stick or keys go through the gamepad assist if it is enabled, otherwise move towards the target at the rate limit. The result is clamped to the car's rack end stop; the unclamped wheel angle is kept for force feedback soft lock.
    // @param {float} deltaTime - frame time in seconds
    // @param {float} carLockDeg - car steering lock from center to one side, in degrees of handwheel rotation
    // @param {float} steerRateRoadDegPerS - steering rate limit in road-wheel degrees per second
    // @param {float} centerRatio - handwheel degrees per road-wheel degree at center (dimensionless)
    // @return {float} - handwheel angle in degrees (positive = right), within plus or minus carLockDeg
    public float StepSteer(float deltaTime, float carLockDeg, float steerRateRoadDegPerS, float centerRatio)
    {
        float steerInput = Axis(InAction.Steer, out bool anyWheel, out float wheel, out float digital);
        float halfWheelRotationDeg = 0.5f * profile.RotationDeg(carLockDeg);
        if (Mathf.Abs(steerInput) > 0f || !anyWheel)
        {
            if (assist != null && profile.gamepadSteer.enabled && assistCtx.HasValue && meanToHandwheel != null)
            {   // gamepad/keyboard assist (road-angle space); never reached by a real wheel (anyWheel && v == 0)
                assist.settings = profile.gamepadSteer; handwheelDeg = meanToHandwheel(assist.Step(deltaTime, steerInput, assistCtx.Value));
            }
            else
            {
                float target = steerInput * carLockDeg;
                handwheelDeg = Mathf.MoveTowards(handwheelDeg, target, steerRateRoadDegPerS * centerRatio * deltaTime);
                if (assist != null) { assist.roadDeg = handwheelDeg / centerRatio; assist.filtered = steerInput; }
            }
        }
        else
        {   // real wheel (SteerSource.Wheel) and no stick/key input: column follows the wheel directly (1:1 / scaled), never the gamepad assist.
            // (2026-10-14 fix: this branch used to be an `else if (assist != null)` that only synced the assist state, so once InputRouter
            // created the assist the wheel angle was never applied and wheel steering froze, whether the assist was on or off.)
            handwheelDeg = profile.mapping == SteerMapping.RealColumn1to1 ? wheel * halfWheelRotationDeg : wheel * carLockDeg;
            if (assist != null) { assist.roadDeg = handwheelDeg / centerRatio; assist.filtered = 0f; }   // keep the assist bumpless if a stick takes over
        }
        wheelDegUnclamped = anyWheel && Mathf.Abs(steerInput) == 0f ? (profile.mapping == SteerMapping.RealColumn1to1 ? wheel * halfWheelRotationDeg : wheel * carLockDeg) : handwheelDeg;
        handwheelDeg = Mathf.Clamp(handwheelDeg, -carLockDeg, carLockDeg);   // car's rack end stop
        return handwheelDeg;
    }

    // ---------- capture (rebinding) ----------
    readonly Dictionary<InputControl, float> snap = new Dictionary<InputControl, float>();
    // @description - Starts rebinding: records the current value of every axis and button on every device so the control that changes most can be detected.
    // @return {Void} - nothing
    public void BeginCapture()
    {
        snap.Clear();
        foreach (var device in InputSystem.devices) { if (Skip(device)) continue; foreach (var control in device.allControls) if (control is AxisControl axisControl && !control.synthetic && !control.noisy) snap[control] = axisControl.ReadUnprocessedValue(); }
    }
    // @description - Finds the control the user is moving during rebinding: the largest change from the snapshot above a threshold (0.4 for axes, 0.5 for buttons). Escape and anyKey are never captured; a button already held at the start is ignored until released.
    // @param {bool} wantAxis - true when binding an axis action (small changes accepted), false for a button action (axes need a change of at least 0.6)
    // @return {InputControl} - the control that moved, or null if nothing qualifies yet
    public InputControl PollCapture(bool wantAxis)
    {
        InputControl bestControl = null; float bestChange = 0f;
        foreach (var snapshotEntry in snap)
        {
            var control = snapshotEntry.Key; if (!control.device.added) continue; float currentValue = ((AxisControl)control).ReadUnprocessedValue(), change = Mathf.Abs(currentValue - snapshotEntry.Value);
            bool isButton = control is ButtonControl;
            if (control.name == "anyKey" || control.name == "escape") continue;                   // Esc always cancels, never binds
            if (isButton) { if (snapshotEntry.Value >= 0.5f) continue;                               // already pressed/held at capture start: rejected (must be released first -> new snapshot)
                       if (((ButtonControl)control).isPressed) change = Mathf.Max(change, 1f); }
            float threshold = isButton ? 0.5f : 0.4f;
            if (!wantAxis && !isButton && change < 0.6f) continue;   // button action from an axis: needs a big change from its rest baseline (rest may be -1/+1)
            if (change > threshold && change > bestChange) { bestChange = change; bestControl = control; }
        }
        return bestControl;
    }
    // @description - Returns the value a control had when rebinding started (its rest position).
    // @param {InputControl} control - control to look up
    // @return {float} - the snapshot value, or NaN if the control was not in the snapshot
    public float CaptureBaseline(InputControl control) => snap.TryGetValue(control, out var baseline) ? baseline : float.NaN;
    // @description - Removes all non-keyboard bindings of an action (wheels, pedals, gamepads), keeping the keyboard keys.
    // @param {InAction} action - action to clear
    // @return {int} - number of bindings removed
    public int ClearDeviceBindings(InAction action) { int removedCount = profile.Get(action).RemoveAll(binding => binding.layout != "Keyboard"); MarkDirty(); Flush(); return removedCount; }
    // @description - Ends rebinding: discards the snapshot, re-resolves the bindings and flushes pending edges.
    // @return {Void} - nothing
    public void EndCapture() { snap.Clear(); MarkDirty(); Flush(); }
    // @description - Stores the captured control as a binding for an action, setting a suitable calibration (rest-based for a pedal axis on a button action, -1..1 for pedal axes), and reuses an identical existing binding instead of adding a duplicate.
    // @param {InAction} action - action being bound
    // @param {InputControl} control - control the user moved
    // @return {Binding} - the binding that now exists for that control
    public Binding CompleteCapture(InAction action, InputControl control)
    {
        var binding = FromControl(control, true);
        if (IsBipolar(action) && !(control is ButtonControl) && !(control.parent is StickControl)) binding.calibration.center = 0f;
        if (!IsBipolar(action) && !(control is ButtonControl) && !(control.parent is StickControl)) binding.calibration = new AxisCalibration { min = -1f, max = 1f, center = 0f };   // pedal axis: calibrate!
        if (!IsAxisAction(action) && !(control is ButtonControl))   // axis on a button action (e.g. pedal as Gear Up): rest = capture baseline, pressed = direction it moved
        { float baselineValue = CaptureBaseline(control), currentValue = Raw(control); if (!float.IsNaN(baselineValue)) { binding.calibration = new AxisCalibration(); binding.calibration.ApplyCapture(Mathf.Min(baselineValue, currentValue), Mathf.Max(baselineValue, currentValue), baselineValue, false); } }
        var slots = profile.Get(action); var existingBinding = slots.Find(slot => slot.product == binding.product && slot.layout == binding.layout && slot.index == binding.index && slot.control == binding.control);
        if (existingBinding == null) slots.Add(binding); else binding = existingBinding;
        EndCapture(); return binding;
    }

    // ---------- defaults & persistence ----------
    // @description - Builds the default profile: keyboard keys and gamepad controls for driving, menu navigation, camera, ignition, tab navigation and reset-to-start.
    // @return {BindingProfile} - a new profile with all default bindings
    public static BindingProfile Defaults()
    {
        var profile = new BindingProfile();
        // @description - Adds a keyboard binding with default calibration to an action of the default profile.
        // @param {InAction} action - action that receives the binding
        // @param {string} keyName - Input System key control name
        // @param {bool} isNegative - true when the key drives the negative direction of an axis action
        // @return {Void} - nothing
        void AddKeyboardBinding(InAction action, string keyName, bool isNegative = false) => profile.Get(action).Add(new Binding { layout = "Keyboard", control = keyName, negative = isNegative, calibration = new AxisCalibration() });
        // @description - Adds a gamepad binding to an action of the default profile; sticks get a 0.1 dead zone against drift.
        // @param {InAction} action - action that receives the binding
        // @param {string} controlPath - gamepad control path, for example leftStick/x
        // @param {bool} isStick - true for a stick axis (calibrated -1..1 with dead zone)
        // @param {bool} isNegative - true when the control drives the negative direction of an axis action
        // @return {Void} - nothing
        void AddGamepadBinding(InAction action, string controlPath, bool isStick = false, bool isNegative = false) => profile.Get(action).Add(new Binding { layout = "Gamepad", control = controlPath, negative = isNegative, steerSource = SteerSource.Stick,
            calibration = isStick ? new AxisCalibration { min = -1f, max = 1f, center = 0f, deadzone = 0.1f } : new AxisCalibration() });   // stick deadzone (user-editable) against stick drift
        AddKeyboardBinding(InAction.Steer, "a", true); AddKeyboardBinding(InAction.Steer, "leftArrow", true); AddKeyboardBinding(InAction.Steer, "d"); AddKeyboardBinding(InAction.Steer, "rightArrow");
        AddKeyboardBinding(InAction.Throttle, "w"); AddKeyboardBinding(InAction.Throttle, "upArrow"); AddKeyboardBinding(InAction.Brake, "s"); AddKeyboardBinding(InAction.Brake, "downArrow");
        AddKeyboardBinding(InAction.Clutch, "leftShift"); AddKeyboardBinding(InAction.Handbrake, "space"); AddKeyboardBinding(InAction.ShiftUp, "e"); AddKeyboardBinding(InAction.ShiftDown, "q");
        AddKeyboardBinding(InAction.Gear1, "digit1"); AddKeyboardBinding(InAction.Gear2, "digit2"); AddKeyboardBinding(InAction.Gear3, "digit3"); AddKeyboardBinding(InAction.Gear4, "digit4"); AddKeyboardBinding(InAction.Gear5, "digit5"); AddKeyboardBinding(InAction.GearR, "digit6");
        AddKeyboardBinding(InAction.Reset, "r"); AddKeyboardBinding(InAction.Menu, "f1"); AddKeyboardBinding(InAction.Menu, "escape");
        AddGamepadBinding(InAction.Steer, "leftStick/x", true); AddGamepadBinding(InAction.Throttle, "rightTrigger"); AddGamepadBinding(InAction.Brake, "leftTrigger"); AddGamepadBinding(InAction.Clutch, "leftShoulder");
        AddGamepadBinding(InAction.ShiftUp, "rightShoulder"); AddGamepadBinding(InAction.ShiftDown, "buttonWest"); AddGamepadBinding(InAction.Handbrake, "buttonEast"); AddGamepadBinding(InAction.Reset, "select");
        AddVrAndMenuDefaults(profile); AddMenuTabDefaults(profile); AddResetToStartDefaults(profile);
        AddGamepadBinding(InAction.Menu, "start"); AddGamepadBinding(InAction.LookX, "rightStick/x", true); AddGamepadBinding(InAction.LookY, "rightStick/y", true);
        return profile;
    }
    // @description - Adds the default keys and gamepad controls for recentering, camera and HUD toggles, menu navigation, ignition and starter.
    // @param {BindingProfile} profile - profile to add the bindings to
    // @return {Void} - nothing
    static void AddVrAndMenuDefaults(BindingProfile profile)   // defaults for actions added in profile version 2 (VR / menu navigation); wheel buttons can be bound to them in the menu
    {
        // @description - Adds a keyboard binding with default calibration to an action of the default profile.
        // @param {InAction} action - action that receives the binding
        // @param {string} keyName - Input System key control name
        // @param {bool} isNegative - true when the key drives the negative direction of an axis action
        // @return {Void} - nothing
        void AddKeyboardBinding(InAction action, string keyName) => profile.Get(action).Add(new Binding { layout = "Keyboard", control = keyName, calibration = new AxisCalibration() });
        // @description - Adds a gamepad binding to an action of the default profile; sticks get a 0.1 dead zone against drift.
        // @param {InAction} action - action that receives the binding
        // @param {string} controlPath - gamepad control path, for example leftStick/x
        // @param {bool} isStick - true for a stick axis (calibrated -1..1 with dead zone)
        // @param {bool} isNegative - true when the control drives the negative direction of an axis action
        // @return {Void} - nothing
        void AddGamepadBinding(InAction action, string controlPath) => profile.Get(action).Add(new Binding { layout = "Gamepad", control = controlPath, steerSource = SteerSource.Stick, calibration = new AxisCalibration() });
        AddKeyboardBinding(InAction.Recenter, "f12"); AddKeyboardBinding(InAction.Recenter, "home"); AddGamepadBinding(InAction.Recenter, "rightStickPress"); AddKeyboardBinding(InAction.CameraToggle, "c"); AddKeyboardBinding(InAction.HudToggle, "h");
        AddKeyboardBinding(InAction.MenuUp, "upArrow"); AddKeyboardBinding(InAction.MenuDown, "downArrow"); AddKeyboardBinding(InAction.MenuLeft, "leftArrow"); AddKeyboardBinding(InAction.MenuRight, "rightArrow"); AddKeyboardBinding(InAction.MenuSelect, "enter"); AddKeyboardBinding(InAction.MenuSelect, "numpadEnter");
        AddKeyboardBinding(InAction.Ignition, "i"); AddKeyboardBinding(InAction.Starter, "t"); AddGamepadBinding(InAction.Ignition, "leftStickPress"); AddGamepadBinding(InAction.Starter, "buttonNorth");
        AddGamepadBinding(InAction.MenuUp, "dpad/up"); AddGamepadBinding(InAction.MenuDown, "dpad/down"); AddGamepadBinding(InAction.MenuLeft, "dpad/left"); AddGamepadBinding(InAction.MenuRight, "dpad/right"); AddGamepadBinding(InAction.MenuSelect, "buttonSouth");
    }
    // @description - Adds the default menu tab navigation keys (page down and tab for next, page up for previous), which are not used by driving.
    // @param {BindingProfile} profile - profile to add the bindings to
    // @return {Void} - nothing
    static void AddMenuTabDefaults(BindingProfile profile)   // v4: dedicated menu tab navigation (was ShiftUp/ShiftDown -> gear paddles flipped tabs). Keys not used by gameplay.
    {
        // @description - Adds a keyboard binding with default calibration to an action of the default profile.
        // @param {InAction} action - action that receives the binding
        // @param {string} keyName - Input System key control name
        // @param {bool} isNegative - true when the key drives the negative direction of an axis action
        // @return {Void} - nothing
        void AddKeyboardBinding(InAction action, string keyName) => profile.Get(action).Add(new Binding { layout = "Keyboard", control = keyName, calibration = new AxisCalibration() });
        AddKeyboardBinding(InAction.MenuTabNext, "pageDown"); AddKeyboardBinding(InAction.MenuTabNext, "tab"); AddKeyboardBinding(InAction.MenuTabPrev, "pageUp");
    }
    // @description - Adds the default key (backspace) for resetting the car to the start/finish line.
    // @param {BindingProfile} profile - profile to add the binding to
    // @return {Void} - nothing
    static void AddResetToStartDefaults(BindingProfile profile) { profile.Get(InAction.ResetToStart).Add(new Binding { layout = "Keyboard", control = "backspace", calibration = new AxisCalibration() }); }   // v5: reset to start/finish
    // Full path of the saved input bindings file (input_bindings.json) in the per-user persistent data folder.
    public static string DefaultPath => System.IO.Path.Combine(Application.persistentDataPath, "input_bindings.json");
    // @description - Writes a profile to disk as JSON and remembers the path in the player preferences.
    // @param {BindingProfile} profile - profile to save
    // @param {string} path - file path, or null for the default path in the persistent data folder
    // @return {Void} - nothing
    public static void Save(BindingProfile profile, string path = null) { System.IO.File.WriteAllText(path ?? DefaultPath, JsonUtility.ToJson(profile, true)); PlayerPrefs.SetString("input_bindings_path", path ?? DefaultPath); PlayerPrefs.Save(); }
    // @description - Loads a saved profile. A missing file, unreadable file, empty profile or a different settings version gives the default profile instead (a warning is logged); there is no migration of old files.
    // @param {string} path - file path, or null for the default path
    // @return {BindingProfile} - the loaded profile, or the defaults
    public static BindingProfile LoadOrDefault(string path = null)
    {
        path = path ?? DefaultPath;
        try { if (System.IO.File.Exists(path)) { var loadedProfile = JsonUtility.FromJson<BindingProfile>(System.IO.File.ReadAllText(path)); if (loadedProfile != null && loadedProfile.actions.Count > 0 && loadedProfile.version == BindingProfile.CurrentVersion) return loadedProfile; Debug.LogWarning("input bindings: " + path + " has settings version " + (loadedProfile != null ? loadedProfile.version : -1) + ", expected " + BindingProfile.CurrentVersion + "; using defaults"); } }
        catch (Exception exception) { Debug.LogWarning("input bindings: could not load " + path + ": " + exception.Message); }
        return Defaults();
    }
}
