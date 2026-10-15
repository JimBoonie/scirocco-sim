// Force feedback: physical steering-column torque (CarController.columnTorque from SteeringTorque) + steering-system
// friction/damping (E) + rack end stop (soft lock at the car's lock, stiffness = steering-system stiffness referred to the
// column) -> normalised by the user's wheelbase maximum torque -> DirectInput constant force via SciroccoFFB.dll (Windows).
// Output = column torque [N m] / wheelbase max torque [N m] x user strength % (100 % = 1:1), clamped to the user's cap.
// Safety: zero output when FFB off, -noffb, menu/pause, focus lost, or during reset; 1 s ramp-in on (re)start.
using System; using System.Runtime.InteropServices; using UnityEngine;
public class FfbController : MonoBehaviour
{
    public CarController car; public InputRouter router;
    public string status = "FFB off"; public bool active; public float totalTorque, command, clippingPercent; public string deviceName = "";
    public float stopTorque, frictionTorque, dampingTorque;
    const float rampTime = 1f;          // s, safety envelope on (re)start (not part of the physics)
    float ramp, lastWheelDeg; bool haveLastWheelAngle, forceFeedbackDisabledByFlag, focused = true, pluginOk = true;
    readonly float[] clipHistory = new float[1000]; int clipHistoryIndex;   // 1 s at 1 kHz
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
    [DllImport("SciroccoFFB")] static extern int FFB_Enumerate();
    [DllImport("SciroccoFFB")] static extern int FFB_DeviceName(int deviceIndex, byte[] buffer, int length);
    [DllImport("SciroccoFFB")] static extern int FFB_Start(int deviceIndex, IntPtr windowHandle);
    [DllImport("SciroccoFFB")] static extern void FFB_Set(float command);
    [DllImport("SciroccoFFB")] static extern void FFB_Stop();
    [DllImport("SciroccoFFB")] static extern void FFB_Shutdown();
    [DllImport("SciroccoFFB")] static extern IntPtr FFB_LastError();
    const bool platformOk = true;
#else
    // @description - Non-Windows stand-ins for the native SciroccoFFB.dll (DirectInput force feedback exists on Windows only): enumerate reports "unavailable" and device name/start do nothing.
    // @return {int} - -1 (no force-feedback support on this platform)
    static int FFB_Enumerate() => -1; static int FFB_DeviceName(int deviceIndex, byte[] buffer, int length) => 0; static int FFB_Start(int deviceIndex, IntPtr windowHandle) => 0;
    // @description - Non-Windows no-op stand-ins for FFB_Set, FFB_Stop, FFB_Shutdown and FFB_LastError of the native plugin.
    // @param {float} command - constant-force command, -1..1 (ignored)
    // @return {Void} - nothing
    static void FFB_Set(float command) { } static void FFB_Stop() { } static void FFB_Shutdown() { } static IntPtr FFB_LastError() => IntPtr.Zero;
    const bool platformOk = false;
#endif
    public string[] devices = new string[0];
    // @description - Reads the -noffb / -smoketest command-line flags and tries to open the force-feedback device.
    // @return {Void} - nothing
    void Start()
    {
        forceFeedbackDisabledByFlag = Array.IndexOf(Environment.GetCommandLineArgs(), "-noffb") >= 0 || Array.IndexOf(Environment.GetCommandLineArgs(), "-smoketest") >= 0;
        TryOpen();
    }
    // @description - Closes any open device, then opens the configured force-feedback wheel. Stays off (with a status text explaining why) when disabled by flag or settings, on non-Windows platforms, when no device or the wheelbase max torque is missing, or when the plugin fails.
    // @return {Void} - nothing; sets status, devices, active
    public void TryOpen()
    {
        Close(); devices = new string[0];
        var profile = router != null ? router.map?.profile : null;
        if (forceFeedbackDisabledByFlag) { status = "FFB off (-noffb)"; return; }
        if (!platformOk) { status = "FFB off (DirectInput FFB is Windows-only)"; return; }
        if (profile == null || !profile.forceFeedbackEnabled) { status = "FFB off (settings)"; return; }
        try
        {
            int deviceCount = FFB_Enumerate();
            if (deviceCount < 0) { status = "FFB off: " + Err(); return; }
            devices = new string[deviceCount]; var nameBuffer = new byte[256];
            for (int deviceIndex = 0; deviceIndex < deviceCount; deviceIndex++) { int nameLength = FFB_DeviceName(deviceIndex, nameBuffer, nameBuffer.Length); devices[deviceIndex] = nameLength > 1 ? System.Text.Encoding.UTF8.GetString(nameBuffer, 0, nameLength - 1) : $"device {deviceIndex}"; }
            if (deviceCount == 0) { status = "FFB off: no force-feedback device found"; return; }
            int selectedIndex = 0; if (!string.IsNullOrEmpty(profile.forceFeedbackDevice)) { selectedIndex = Array.FindIndex(devices, deviceName => deviceName.IndexOf(profile.forceFeedbackDevice, StringComparison.OrdinalIgnoreCase) >= 0); if (selectedIndex < 0) { status = $"FFB off: '{profile.forceFeedbackDevice}' not connected"; return; } }
            if (profile.wheelbaseMaxTorqueNm <= 0f) { status = "FFB off: enter your wheelbase max torque (N m) in settings"; return; }
            if (FFB_Start(selectedIndex, IntPtr.Zero) == 0) { status = "FFB off: " + Err(); return; }
            deviceName = devices[selectedIndex]; active = true; ramp = 0f; status = "FFB on: " + deviceName;
        }
        catch (Exception exception) { pluginOk = false; active = false; status = "FFB off (plugin unavailable: " + exception.GetType().Name + ")"; }
    }
    // @description - Reads the last error message of the native plugin.
    // @return {string} - the error text, or "error" if unavailable
    static string Err() { try { var messagePointer = FFB_LastError(); return messagePointer == IntPtr.Zero ? "error" : Marshal.PtrToStringAnsi(messagePointer); } catch { return "error"; } }
    // @description - Zeroes the force and stops the device effect (if one is running).
    // @return {Void} - nothing
    public void Close() { if (active) { try { FFB_Set(0f); FFB_Stop(); } catch { } } active = false; }
    // @description - Unity callback: when the game window loses focus the force is cut and ramped up again later, except in VR where the desktop window often loses focus to SteamVR.
    // @param {bool} hasFocus - true when the window has focus
    // @return {Void} - nothing
    void OnApplicationFocus(bool hasFocus) { focused = hasFocus; if (VrManager.Active) return; if (!hasFocus) Send(0f); ramp = 0f; }   // in VR the desktop window often loses focus to SteamVR: do not cut FFB for that
    // @description - Unity callback: releases the device.
    // @return {Void} - nothing
    void OnDisable() { Close(); }
    // @description - Unity callback: releases the device and shuts the native plugin down.
    // @return {Void} - nothing
    void OnApplicationQuit() { Close(); try { if (pluginOk && platformOk) FFB_Shutdown(); } catch { } }
    // @description - Sends a constant-force command to the device; a plugin exception switches force feedback off.
    // @param {float} forceCommand - force command, -1..1 (+ = turns the wheel right)
    // @return {Void} - nothing
    void Send(float forceCommand) { if (!active) return; try { FFB_Set(forceCommand); } catch { active = false; status = "FFB off (plugin error)"; } }
    // @description - Restarts the force fade-in (after a teleport or reset), so the wheel does not jerk.
    // @return {Void} - nothing
    public void ResetRamp() { ramp = 0f; }

    // @description - Computes the column torque the driver should feel and sends it as a command. Total torque = steering-system torque from the tire forces + rack end-stop spring (overtravel x column stiffness) + Coulomb friction + viscous damping; it is scaled to the wheelbase maximum torque, clipped to the user cap, ramped in and held at zero when paused, unfocused or in a menu.
    // @return {Void} - nothing; sets command and the torque diagnostics
    void FixedUpdate()
    {
        if (!car || car.carParams == null) return;
        var carParams = car.carParams; var profile = router != null ? router.map?.profile : null;
        float deltaTime = Time.fixedDeltaTime;
        // column angle & rate from the physical wheel (unclamped beyond the car's rack stop)
        float wheelDeg = router != null && router.map != null ? router.map.wheelDegUnclamped : car.handwheelDeg;
        float columnRate = haveLastWheelAngle ? (wheelDeg - lastWheelDeg) * Mathf.Deg2Rad / deltaTime : 0f; lastWheelDeg = wheelDeg; haveLastWheelAngle = true;
        // rack end stop: overtravel against the steering-system stiffness referred to the column (k_road / ratio^2)
        float overtravelDeg = Mathf.Abs(wheelDeg) - carParams.handwheelLockDeg, centreRatio = SteeringLinkage.CentreRatio(carParams), columnStiffness = carParams.steeringStiffness / (centreRatio * centreRatio);   // on-centre linkage ratio (D)
        stopTorque = overtravelDeg > 0f ? -Mathf.Sign(wheelDeg) * columnStiffness * overtravelDeg * Mathf.Deg2Rad : 0f;
        frictionTorque = -carParams.columnFriction * Mathf.Clamp(columnRate / carParams.columnFrictionRegularisationSpeed, -1f, 1f);
        dampingTorque = -carParams.columnDamping * columnRate;
        totalTorque = car.columnTorque + stopTorque + frictionTorque + dampingTorque;   // + = turns the wheel clockwise (right)
        if (profile == null || profile.wheelbaseMaxTorqueNm <= 0f) { command = 0f; return; }
        float requestedOutput = totalTorque / profile.wheelbaseMaxTorqueNm * profile.forceFeedbackStrengthPercent * 0.01f, outputCap = profile.forceFeedbackMaxOutputPercent * 0.01f;
        clipHistory[clipHistoryIndex] = Mathf.Abs(requestedOutput) > outputCap ? 1f : 0f; clipHistoryIndex = (clipHistoryIndex + 1) % clipHistory.Length;
        float clippedSamples = 0f; for (int i = 0; i < clipHistory.Length; i += 10) clippedSamples += clipHistory[i]; clippingPercent = clippedSamples / (clipHistory.Length / 10) * 100f;
        bool holdOutput = !active || !(focused || VrManager.Active) || Time.timeScale == 0f || (router != null && router.menuOpen) || car.externalInput;
        if (holdOutput) { ramp = 0f; command = 0f; Send(0f); return; }
        ramp = Mathf.Min(1f, ramp + deltaTime / rampTime);
        command = Mathf.Clamp(requestedOutput, -outputCap, outputCap) * ramp * (profile.forceFeedbackInvert ? -1f : 1f);
        Send(command);
    }
}
