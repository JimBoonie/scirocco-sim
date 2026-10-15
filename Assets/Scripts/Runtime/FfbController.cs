// Force feedback: physical steering-column torque (CarController.columnTorque from SteeringTorque) + steering-system
// friction/damping (E) + rack end stop (soft lock at the car's lock, stiffness = steering-system stiffness referred to the
// column) -> normalised by the user's wheelbase maximum torque -> DirectInput constant force via SciroccoFFB.dll (Windows).
// Output = column torque [N m] / wheelbase max torque [N m] x user strength % (100 % = 1:1), clamped to the user's cap.
// Safety: zero output when FFB off, -noffb, menu/pause, focus lost, or during reset; 1 s ramp-in on (re)start.
using System; using System.Runtime.InteropServices; using UnityEngine;
public class FfbController : MonoBehaviour
{
    public CarController car; public InputRouter router;
    public string status = "FFB off"; public bool active; public float totalTorque, command, clipPct; public string deviceName = "";
    public float stopTorque, frictionTorque, dampingTorque;
    const float rampTime = 1f;          // s, safety envelope on (re)start (not part of the physics)
    float ramp, lastWheelDeg; bool haveLast, noffbFlag, focused = true, pluginOk = true;
    readonly float[] clipHist = new float[1000]; int clipIdx;   // 1 s at 1 kHz
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
    [DllImport("SciroccoFFB")] static extern int FFB_Enumerate();
    [DllImport("SciroccoFFB")] static extern int FFB_DeviceName(int i, byte[] buf, int len);
    [DllImport("SciroccoFFB")] static extern int FFB_Start(int i, IntPtr hwnd);
    [DllImport("SciroccoFFB")] static extern void FFB_Set(float v);
    [DllImport("SciroccoFFB")] static extern void FFB_Stop();
    [DllImport("SciroccoFFB")] static extern void FFB_Shutdown();
    [DllImport("SciroccoFFB")] static extern IntPtr FFB_LastError();
    const bool platformOk = true;
#else
    // @description - Ffb enumerate.
    // @return {int} - the ffb enumerate
    static int FFB_Enumerate() => -1; static int FFB_DeviceName(int i, byte[] b, int l) => 0; static int FFB_Start(int i, IntPtr h) => 0;
    // @description - Ffb set.
    // @param {float} v - value of type float
    // @return {Void} - nothing
    static void FFB_Set(float v) { } static void FFB_Stop() { } static void FFB_Shutdown() { } static IntPtr FFB_LastError() => IntPtr.Zero;
    const bool platformOk = false;
#endif
    public string[] devices = new string[0];
    // @description - Start (see the body for details).
    // @return {Void} - nothing
    void Start()
    {
        noffbFlag = Array.IndexOf(Environment.GetCommandLineArgs(), "-noffb") >= 0 || Array.IndexOf(Environment.GetCommandLineArgs(), "-smoketest") >= 0;
        TryOpen();
    }
    // @description - Tries to get the open.
    // @return {Void} - nothing
    public void TryOpen()
    {
        Close(); devices = new string[0];
        var pr = router != null ? router.map?.profile : null;
        if (noffbFlag) { status = "FFB off (-noffb)"; return; }
        if (!platformOk) { status = "FFB off (DirectInput FFB is Windows-only)"; return; }
        if (pr == null || !pr.forceFeedbackEnabled) { status = "FFB off (settings)"; return; }
        try
        {
            int n = FFB_Enumerate();
            if (n < 0) { status = "FFB off: " + Err(); return; }
            devices = new string[n]; var buf = new byte[256];
            for (int i = 0; i < n; i++) { int len = FFB_DeviceName(i, buf, buf.Length); devices[i] = len > 1 ? System.Text.Encoding.UTF8.GetString(buf, 0, len - 1) : $"device {i}"; }
            if (n == 0) { status = "FFB off: no force-feedback device found"; return; }
            int pick = 0; if (!string.IsNullOrEmpty(pr.forceFeedbackDevice)) { pick = Array.FindIndex(devices, d => d.IndexOf(pr.forceFeedbackDevice, StringComparison.OrdinalIgnoreCase) >= 0); if (pick < 0) { status = $"FFB off: '{pr.forceFeedbackDevice}' not connected"; return; } }
            if (pr.wheelbaseMaxTorqueNm <= 0f) { status = "FFB off: enter your wheelbase max torque (N m) in settings"; return; }
            if (FFB_Start(pick, IntPtr.Zero) == 0) { status = "FFB off: " + Err(); return; }
            deviceName = devices[pick]; active = true; ramp = 0f; status = "FFB on: " + deviceName;
        }
        catch (Exception e) { pluginOk = false; active = false; status = "FFB off (plugin unavailable: " + e.GetType().Name + ")"; }
    }
    // @description - Err.
    // @return {string} - the err
    static string Err() { try { var p = FFB_LastError(); return p == IntPtr.Zero ? "error" : Marshal.PtrToStringAnsi(p); } catch { return "error"; } }
    // @description - Close.
    // @return {Void} - nothing
    public void Close() { if (active) { try { FFB_Set(0f); FFB_Stop(); } catch { } } active = false; }
    // @description - Handles the application focus.
    // @param {bool} f - value of type bool
    // @return {Void} - nothing
    void OnApplicationFocus(bool f) { focused = f; if (VrManager.Active) return; if (!f) Send(0f); ramp = 0f; }   // in VR the desktop window often loses focus to SteamVR: do not cut FFB for that
    // @description - Handles the disable.
    // @return {Void} - nothing
    void OnDisable() { Close(); }
    // @description - Handles the application quit.
    // @return {Void} - nothing
    void OnApplicationQuit() { Close(); try { if (pluginOk && platformOk) FFB_Shutdown(); } catch { } }
    // @description - Send.
    // @param {float} v - value of type float
    // @return {Void} - nothing
    void Send(float v) { if (!active) return; try { FFB_Set(v); } catch { active = false; status = "FFB off (plugin error)"; } }
    // @description - Resets the ramp.
    // @return {Void} - nothing
    public void ResetRamp() { ramp = 0f; }

    // @description - Fixed update.
    // @return {Void} - nothing
    void FixedUpdate()
    {
        if (!car || car.carParams == null) return;
        var p = car.carParams; var pr = router != null ? router.map?.profile : null;
        float dt = Time.fixedDeltaTime;
        // column angle & rate from the physical wheel (unclamped beyond the car's rack stop)
        float wheelDeg = router != null && router.map != null ? router.map.wheelDegUnclamped : car.handwheelDeg;
        float w = haveLast ? (wheelDeg - lastWheelDeg) * Mathf.Deg2Rad / dt : 0f; lastWheelDeg = wheelDeg; haveLast = true;
        // rack end stop: overtravel against the steering-system stiffness referred to the column (k_road / ratio^2)
        float over = Mathf.Abs(wheelDeg) - p.handwheelLockDeg, cr = SteeringLinkage.CentreRatio(p), kCol = p.steeringStiffness / (cr * cr);   // on-centre linkage ratio (D)
        stopTorque = over > 0f ? -Mathf.Sign(wheelDeg) * kCol * over * Mathf.Deg2Rad : 0f;
        frictionTorque = -p.columnFriction * Mathf.Clamp(w / p.columnFrictionRegularisationSpeed, -1f, 1f);
        dampingTorque = -p.columnDamping * w;
        totalTorque = car.columnTorque + stopTorque + frictionTorque + dampingTorque;   // + = turns the wheel clockwise (right)
        if (pr == null || pr.wheelbaseMaxTorqueNm <= 0f) { command = 0f; return; }
        float want = totalTorque / pr.wheelbaseMaxTorqueNm * pr.forceFeedbackStrengthPercent * 0.01f, cap = pr.forceFeedbackMaxOutputPercent * 0.01f;
        clipHist[clipIdx] = Mathf.Abs(want) > cap ? 1f : 0f; clipIdx = (clipIdx + 1) % clipHist.Length;
        float sum = 0f; for (int i = 0; i < clipHist.Length; i += 10) sum += clipHist[i]; clipPct = sum / (clipHist.Length / 10) * 100f;
        bool hold = !active || !(focused || VrManager.Active) || Time.timeScale == 0f || (router != null && router.menuOpen) || car.externalInput;
        if (hold) { ramp = 0f; command = 0f; Send(0f); return; }
        ramp = Mathf.Min(1f, ramp + dt / rampTime);
        command = Mathf.Clamp(want, -cap, cap) * ramp * (pr.forceFeedbackInvert ? -1f : 1f);
        Send(command);
    }
}
