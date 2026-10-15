// VR (OpenXR; SteamVR as the OpenXR runtime for the Valve Index) with flat-screen fallback.
// XR is NOT initialised on startup (XR settings: InitManagerOnStart = false); this component starts it when wanted:
//   -vr forces on, -novr forces off, otherwise the saved profile setting (menu). -smoketest always off.
// No HMD / no runtime / XR not configured -> stays flat, status says why. HMD lost while running -> back to flat.
// Physics is unaffected (fixed 500 Hz step, interpolated body); the HMD only sets the render rate.
using System.Collections; using System.Collections.Generic; using UnityEngine; using UnityEngine.InputSystem;
#if ENABLE_VR
using UnityEngine.XR; using UnityEngine.XR.Management;
#endif
public enum CamMode { Chase, Cockpit }
public class VrManager : MonoBehaviour
{
    public static bool Active; public static VrManager I;
    public CarController car; public Cockpit cockpit; public Camera cam; public FollowCam follow; public InputRouter router;
    public string status = "VR off"; public CamMode mode = CamMode.Chase;
    bool forceOn, forceOff, smoke, busy; Behaviour tpd; Transform camParent;
    // @description - Arg.
    // @param {string} a - value of type string
    // @return {bool} - the arg
    static bool Arg(string a) => System.Array.IndexOf(System.Environment.GetCommandLineArgs(), a) >= 0;
    // @description - Awake.
    // @return {Void} - nothing
    void Awake()
    {
        I = this; forceOn = Arg("-vr"); forceOff = Arg("-novr"); smoke = Arg("-smoketest");
        if (!cam) cam = Camera.main; if (cam) { follow = cam.GetComponent<FollowCam>(); camParent = cam.transform.parent; }
    }
    // @description - Start (see the body for details).
    // @return {IEnumerator} - the start
    IEnumerator Start()
    {
        if (smoke) { status = "VR off (-smoketest)"; yield break; }
        yield return null;   // InputRouter loads the profile in Awake
        var pr = router != null ? router.map?.profile : null;
        ApplyFlatFov();   // saved flat-screen FOV at launch
        if (cockpit) { cockpit.Build(); if (pr != null) cockpit.ApplySeat(pr.seatForwardOffset, pr.seatUpOffset); }
        if ((pr != null && pr.cockpitView) || Arg("-cockpit")) SetMode(CamMode.Cockpit);   // -cockpit: flat cockpit camera from launch
        bool want = forceOn || (!forceOff && pr != null && pr.vrEnabled);
        if (forceOff) status = "VR off (-novr)";
        if (want) yield return StartVR();
        Shot();
    }
    // @description - Sets the enabled.
    // @param {bool} on - value of type bool
    // @return {Void} - nothing
    public void SetEnabled(bool on) { if (busy) return; if (on && !Active) StartCoroutine(StartVR()); else if (!on && Active) StopVR("VR off (settings)"); }
    // @description - Starts the vr.
    // @return {IEnumerator} - the start vr
    IEnumerator StartVR()
    {
        busy = true; status = "VR: starting OpenXR...";
#if ENABLE_VR
        var gs = XRGeneralSettings.Instance;
        if (gs == null || gs.Manager == null) { status = "VR off: XR not configured in this build (flat screen)"; busy = false; yield break; }
        if (gs.Manager.activeLoader == null) yield return gs.Manager.InitializeLoader();
        if (gs.Manager.activeLoader == null) { status = "VR off: no OpenXR runtime / HMD found - is SteamVR the active OpenXR runtime and the Index connected? (flat screen)"; busy = false; yield break; }
        gs.Manager.StartSubsystems(); yield return null; yield return null;
        if (!DisplayRunning()) { gs.Manager.StopSubsystems(); gs.Manager.DeinitializeLoader(); status = "VR off: HMD display did not start (flat screen)"; busy = false; yield break; }
        Active = true;
        Application.runInBackground = true;                                                  // SteamVR may own window focus; keep simulating
        InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus; // wheel/pedals keep working when the desktop window is unfocused
        QualitySettings.vSyncCount = 0; Application.targetFrameRate = -1;                       // the HMD compositor paces frames (90/120/144 Hz)
        SetMode(CamMode.Cockpit);
        status = $"VR on: {gs.Manager.activeLoader.name}, {XRSettings.loadedDeviceName}, refresh {RefreshRate():0} Hz, stereo {XRSettings.stereoRenderingMode}";
        yield return null; Recenter();
#else
        status = "VR off: XR not available on this platform (flat screen)"; yield return null;
#endif
        busy = false;
    }
    // @description - Stops the vr.
    // @param {string} why - why
    // @return {Void} - nothing
    public void StopVR(string why)
    {
#if ENABLE_VR
        var gs = XRGeneralSettings.Instance;
        if (gs != null && gs.Manager != null && gs.Manager.activeLoader != null) { gs.Manager.StopSubsystems(); gs.Manager.DeinitializeLoader(); }
#endif
        Active = false; status = why; if (tpd) tpd.enabled = false;
        var pr = router != null ? router.map?.profile : null; SetMode(pr != null && pr.cockpitView ? CamMode.Cockpit : CamMode.Chase);
        ApplyFlatFov(); if (Application.isPlaying && isActiveAndEnabled) StartCoroutine(FovNextFrame());   // XR may still touch the camera this frame: restore again next frame
    }
#if ENABLE_VR
    // @description - Display running.
    // @return {bool} - the display running
    static bool DisplayRunning() { var l = new List<XRDisplaySubsystem>(); SubsystemManager.GetInstances(l); foreach (var d in l) if (d.running) return true; return false; }
    // @description - Refreshes the rate.
    // @return {float} - the refresh rate
    static float RefreshRate() { var l = new List<XRDisplaySubsystem>(); SubsystemManager.GetInstances(l); foreach (var d in l) if (d.running && d.TryGetDisplayRefreshRate(out float r)) return r; return 0f; }
#endif
    // @description - Update (see the body for details).
    // @return {Void} - nothing
    void Update()
    {
#if ENABLE_VR
        if (Active && !busy && !DisplayRunning()) StopVR("VR off: HMD/runtime lost (flat screen)");
#endif
    }
    // @description - Fov next frame.
    // @return {System.Collections.IEnumerator} - the fov next frame
    System.Collections.IEnumerator FovNextFrame() { yield return null; ApplyFlatFov(); }
    public float FlatFov => router != null && router.map != null ? router.map.profile.flatFovDeg : 60f;
    // @description - flat-screen camera: drop any XR stereo/projection overrides and use the user's vertical FOV (no-op while VR runs; XR owns the projection then)
    // @return {Void} - nothing
    public void ApplyFlatFov() { if (!Active && cam) RestoreFlatCamera(cam, FlatFov); }
    // @description - Restore flat camera.
    // @param {Camera} c - value of type Camera
    // @param {float} vFovDeg - v fov deg
    // @return {Void} - nothing
    public static void RestoreFlatCamera(Camera c, float vFovDeg)
    {
        c.ResetStereoProjectionMatrices(); c.ResetStereoViewMatrices(); c.ResetProjectionMatrix(); c.ResetWorldToCameraMatrix(); c.ResetAspect();
        c.usePhysicalProperties = false; c.fieldOfView = Mathf.Clamp(vFovDeg, BindingProfile.FovMin, BindingProfile.FovMax);
    }
    // @description - Horizontal fov.
    // @param {float} vFovDeg - v fov deg
    // @param {float} aspect - aspect
    // @return {float} - the horizontal fov
    public static float HorizontalFov(float vFovDeg, float aspect) => 2f * Mathf.Atan(Mathf.Tan(vFovDeg * 0.5f * Mathf.Deg2Rad) * aspect) * Mathf.Rad2Deg;
    // @description - Handles the application quit.
    // @return {Void} - nothing
    void OnApplicationQuit() { if (Active) StopVR("quit"); }
    // @description - Toggles the camera.
    // @return {Void} - nothing
    public void ToggleCamera() { if (Active) return; var pr = router?.map?.profile; SetMode(mode == CamMode.Chase ? CamMode.Cockpit : CamMode.Chase); if (pr != null) pr.cockpitView = mode == CamMode.Cockpit; }
    // @description - Sets the mode.
    // @param {CamMode} m - value of type CamMode
    // @return {Void} - nothing
    public void SetMode(CamMode m)
    {
        mode = m; if (!cam) return;
        if (m == CamMode.Cockpit && cockpit && cockpit.recenter)
        {
            if (follow) follow.enabled = false;
            cam.transform.SetParent(cockpit.recenter, false); cam.transform.localPosition = Vector3.zero; cam.transform.localRotation = Quaternion.identity;
            cam.nearClipPlane = 0.02f; EnsureTracking(Active);
        }
        else
        {
            EnsureTracking(false); cam.transform.SetParent(camParent, true); cam.nearClipPlane = 0.3f; if (follow) follow.enabled = true;
        }
    }
    // @description - Ensure tracking.
    // @param {bool} on - value of type bool
    // @return {Void} - nothing
    void EnsureTracking(bool on)
    {
#if ENABLE_VR
        if (on && tpd == null)
        {
            var t = cam.gameObject.AddComponent<UnityEngine.InputSystem.XR.TrackedPoseDriver>();
            t.positionInput = new InputActionProperty(new InputAction("HeadPos", InputActionType.Value, "<XRHMD>/centerEyePosition", expectedControlType: "Vector3"));
            t.rotationInput = new InputActionProperty(new InputAction("HeadRot", InputActionType.Value, "<XRHMD>/centerEyeRotation", expectedControlType: "Quaternion"));
            t.trackingType = UnityEngine.InputSystem.XR.TrackedPoseDriver.TrackingType.RotationAndPosition;
            t.updateType = UnityEngine.InputSystem.XR.TrackedPoseDriver.UpdateType.UpdateAndBeforeRender;
            t.positionInput.action.Enable(); t.rotationInput.action.Enable(); tpd = t;
        }
#endif
        if (tpd) tpd.enabled = on;
        if (!on && cam && cockpit && cam.transform.parent == cockpit.recenter) { cam.transform.localPosition = Vector3.zero; cam.transform.localRotation = Quaternion.identity; }
    }
    // @description - Recenter.
    // @return {Void} - nothing
    public void Recenter() { if (cockpit && cam) cockpit.Recenter(cam.transform); }
    // @description - Handles the enable.
    // @return {Void} - nothing
    void OnEnable() { CarController.Teleported += OnTeleport; } void OnDisable() { CarController.Teleported -= OnTeleport; }
    // @description - Handles the teleport.
    // @param {CarController} car - car
    // @return {Void} - nothing
    void OnTeleport(CarController car) { if (!Active && cam && mode == CamMode.Cockpit) cam.transform.localRotation = Quaternion.identity; }   // flat: straight ahead (VR head pose stays the HMD's)
    // @description - Flat look.
    // @param {float} yaw - yaw
    // @param {float} pitch - pitch
    // @return {Void} - nothing
    public void FlatLook(float yaw, float pitch) { if (!Active && mode == CamMode.Cockpit && cam) cam.transform.localRotation = Quaternion.Euler(-pitch, yaw, 0f); }
    // @description - -screenshot <path> [-shotdelay s] [-shotmenu]: verification aid (renders, saves a PNG, quits)
    // @return {Void} - nothing
    void Shot()
    {
        var a = System.Environment.GetCommandLineArgs(); int i = System.Array.IndexOf(a, "-screenshot"); if (i < 0 || i + 1 >= a.Length) return;
        float d = 3f; int j = System.Array.IndexOf(a, "-shotdelay"); if (j >= 0 && j + 1 < a.Length) float.TryParse(a[j + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out d);
        StartCoroutine(ShotCo(a[i + 1], d));
    }
    // @description - Shot co.
    // @param {string} path - path
    // @param {float} d - value of type float
    // @return {IEnumerator} - the shot co
    IEnumerator ShotCo(string path, float d)
    {
        if (Arg("-shotmenu") && router) router.menuOpen = true;
        yield return new WaitForSecondsRealtime(d); ScreenCapture.CaptureScreenshot(path); yield return new WaitForSecondsRealtime(1f); Application.Quit();
    }
}
