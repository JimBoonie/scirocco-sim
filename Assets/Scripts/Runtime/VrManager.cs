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
    public static bool Active; public static VrManager Instance;
    public CarController car; public Cockpit cockpit; public Camera cam; public FollowCam follow; public InputRouter router;
    public string status = "VR off"; public CamMode mode = CamMode.Chase;
    bool forceOn, forceOff, smoke, busy; Behaviour tpd; Transform camParent;
    // @description - Tells whether a command line flag was passed to the player.
    // @param {string} flag - flag to look for, for example "-vr"
    // @return {bool} - true if the flag is present
    static bool Arg(string flag) => System.Array.IndexOf(System.Environment.GetCommandLineArgs(), flag) >= 0;
    // @description - Registers the singleton, reads the -vr, -novr and -smoketest flags and finds the camera, its FollowCam and original parent so chase mode can be restored.
    // @return {Void} - nothing
    void Awake()
    {
        Instance = this; forceOn = Arg("-vr"); forceOff = Arg("-novr"); smoke = Arg("-smoketest");
        if (!cam) cam = Camera.main; if (cam) { follow = cam.GetComponent<FollowCam>(); camParent = cam.transform.parent; }
    }
    // @description - Start-up coroutine: waits one frame for the input profile, applies the saved flat-screen FOV and seat position, picks cockpit or chase camera, then starts VR if requested by -vr or the saved setting (never in -smoketest, never with -novr), and finally handles the screenshot flags.
    // @return {IEnumerator} - coroutine enumerator
    IEnumerator Start()
    {
        if (smoke) { status = "VR off (-smoketest)"; yield break; }
        yield return null;   // InputRouter loads the profile in Awake
        var profile = router != null ? router.map?.profile : null;
        ApplyFlatFov();   // saved flat-screen FOV at launch
        if (cockpit) { cockpit.Build(); if (profile != null) cockpit.ApplySeat(profile.seatForwardOffset, profile.seatUpOffset); }
        if ((profile != null && profile.cockpitView) || Arg("-cockpit")) SetMode(CamMode.Cockpit);   // -cockpit: flat cockpit camera from launch
        bool want = forceOn || (!forceOff && profile != null && profile.vrEnabled);
        if (forceOff) status = "VR off (-novr)";
        if (want) yield return StartVR();
        Shot();
    }
    // @description - Switches VR on or off from the settings menu (ignored while a start is in progress).
    // @param {bool} on - true to start VR, false to stop it
    // @return {Void} - nothing
    public void SetEnabled(bool on) { if (busy) return; if (on && !Active) StartCoroutine(StartVR()); else if (!on && Active) StopVR("VR off (settings)"); }
    // @description - Coroutine that initialises OpenXR and starts the HMD display. On every failure (XR not configured, no runtime or HMD, display did not start) it stays on the flat screen and writes the reason to the status text. On success it keeps simulating when unfocused, disables vsync (the compositor paces frames), switches to the cockpit camera and recentres.
    // @return {IEnumerator} - coroutine enumerator
    IEnumerator StartVR()
    {
        busy = true; status = "VR: starting OpenXR...";
#if ENABLE_VR
        var xrSettings = XRGeneralSettings.Instance;
        if (xrSettings == null || xrSettings.Manager == null) { status = "VR off: XR not configured in this build (flat screen)"; busy = false; yield break; }
        if (xrSettings.Manager.activeLoader == null) yield return xrSettings.Manager.InitializeLoader();
        if (xrSettings.Manager.activeLoader == null) { status = "VR off: no OpenXR runtime / HMD found - is SteamVR the active OpenXR runtime and the Index connected? (flat screen)"; busy = false; yield break; }
        xrSettings.Manager.StartSubsystems(); yield return null; yield return null;
        if (!DisplayRunning()) { xrSettings.Manager.StopSubsystems(); xrSettings.Manager.DeinitializeLoader(); status = "VR off: HMD display did not start (flat screen)"; busy = false; yield break; }
        Active = true;
        Application.runInBackground = true;                                                  // SteamVR may own window focus; keep simulating
        InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus; // wheel/pedals keep working when the desktop window is unfocused
        QualitySettings.vSyncCount = 0; Application.targetFrameRate = -1;                       // the HMD compositor paces frames (90/120/144 Hz)
        SetMode(CamMode.Cockpit);
        status = $"VR on: {xrSettings.Manager.activeLoader.name}, {XRSettings.loadedDeviceName}, refresh {RefreshRate():0} Hz, stereo {XRSettings.stereoRenderingMode}";
        yield return null; Recenter();
#else
        status = "VR off: XR not available on this platform (flat screen)"; yield return null;
#endif
        busy = false;
    }
    // @description - Shuts down XR, returns to the flat screen camera mode saved in the profile and restores the flat-screen FOV, again next frame because XR may still touch the camera this frame.
    // @param {string} why - status text explaining why VR stopped
    // @return {Void} - nothing
    public void StopVR(string why)
    {
#if ENABLE_VR
        var xrSettings = XRGeneralSettings.Instance;
        if (xrSettings != null && xrSettings.Manager != null && xrSettings.Manager.activeLoader != null) { xrSettings.Manager.StopSubsystems(); xrSettings.Manager.DeinitializeLoader(); }
#endif
        Active = false; status = why; if (tpd) tpd.enabled = false;
        var profile = router != null ? router.map?.profile : null; SetMode(profile != null && profile.cockpitView ? CamMode.Cockpit : CamMode.Chase);
        ApplyFlatFov(); if (Application.isPlaying && isActiveAndEnabled) StartCoroutine(FovNextFrame());   // XR may still touch the camera this frame: restore again next frame
    }
#if ENABLE_VR
    // @description - Tells whether an XR display subsystem is running (the HMD is alive).
    // @return {bool} - true if any XR display is running
    static bool DisplayRunning() { var displays = new List<XRDisplaySubsystem>(); SubsystemManager.GetInstances(displays); foreach (var display in displays) if (display.running) return true; return false; }
    // @description - Reads the refresh rate of the running XR display.
    // @return {float} - refresh rate in hertz, or 0 if no display is running
    static float RefreshRate() { var displays = new List<XRDisplaySubsystem>(); SubsystemManager.GetInstances(displays); foreach (var display in displays) if (display.running && display.TryGetDisplayRefreshRate(out float refreshRate)) return refreshRate; return 0f; }
#endif
    // @description - Falls back to the flat screen if the HMD or runtime disappears while VR is active.
    // @return {Void} - nothing
    void Update()
    {
#if ENABLE_VR
        if (Active && !busy && !DisplayRunning()) StopVR("VR off: HMD/runtime lost (flat screen)");
#endif
    }
    // @description - Coroutine that re-applies the flat-screen FOV one frame later, after XR has released the camera.
    // @return {System.Collections.IEnumerator} - coroutine enumerator
    System.Collections.IEnumerator FovNextFrame() { yield return null; ApplyFlatFov(); }
    public float FlatFov => router != null && router.map != null ? router.map.profile.flatFovDeg : 60f;
    // @description - Restores the flat-screen camera projection and field of view from the settings, unless VR is active.
    // @return {Void} - nothing
    public void ApplyFlatFov() { if (!Active && cam) RestoreFlatCamera(cam, FlatFov); }
    // @description - Resets everything XR may have changed on a camera (stereo and projection matrices, aspect, physical camera) and sets the vertical field of view, clamped to the allowed range.
    // @param {Camera} targetCamera - camera to restore
    // @param {float} vFovDeg - vertical field of view in degrees
    // @return {Void} - nothing
    public static void RestoreFlatCamera(Camera targetCamera, float vFovDeg)
    {
        targetCamera.ResetStereoProjectionMatrices(); targetCamera.ResetStereoViewMatrices(); targetCamera.ResetProjectionMatrix(); targetCamera.ResetWorldToCameraMatrix(); targetCamera.ResetAspect();
        targetCamera.usePhysicalProperties = false; targetCamera.fieldOfView = Mathf.Clamp(vFovDeg, BindingProfile.FovMin, BindingProfile.FovMax);
    }
    // @description - Converts a vertical field of view to a horizontal one: 2 atan(tan(vertical / 2) x aspect).
    // @param {float} vFovDeg - vertical field of view in degrees
    // @param {float} aspect - screen width divided by height (dimensionless)
    // @return {float} - horizontal field of view in degrees
    public static float HorizontalFov(float vFovDeg, float aspect) => 2f * Mathf.Atan(Mathf.Tan(vFovDeg * 0.5f * Mathf.Deg2Rad) * aspect) * Mathf.Rad2Deg;
    // @description - Shuts VR down cleanly when the application quits.
    // @return {Void} - nothing
    void OnApplicationQuit() { if (Active) StopVR("quit"); }
    // @description - Switches between chase and cockpit camera on the flat screen and stores the choice in the profile. Ignored in VR, where the cockpit camera is always used.
    // @return {Void} - nothing
    public void ToggleCamera() { if (Active) return; var profile = router?.map?.profile; SetMode(mode == CamMode.Chase ? CamMode.Cockpit : CamMode.Chase); if (profile != null) profile.cockpitView = mode == CamMode.Cockpit; }
    // @description - Selects the camera mode. Cockpit parents the camera to the cockpit recentre anchor with a near plane of 0.02 m and disables the chase camera; chase restores the original parent, 0.3 m near plane and the chase camera.
    // @param {CamMode} newMode - camera mode to use
    // @return {Void} - nothing
    public void SetMode(CamMode newMode)
    {
        mode = newMode; if (!cam) return;
        if (newMode == CamMode.Cockpit && cockpit && cockpit.recenter)
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
    // @description - Adds (once) and enables or disables the head tracking driver that moves the camera with the HMD. When tracking turns off in the cockpit the camera is put back at the anchor origin.
    // @param {bool} on - true to track the headset pose, false to stop
    // @return {Void} - nothing
    void EnsureTracking(bool on)
    {
#if ENABLE_VR
        if (on && tpd == null)
        {
            var trackedPoseDriver = cam.gameObject.AddComponent<UnityEngine.InputSystem.XR.TrackedPoseDriver>();
            trackedPoseDriver.positionInput = new InputActionProperty(new InputAction("HeadPos", InputActionType.Value, "<XRHMD>/centerEyePosition", expectedControlType: "Vector3"));
            trackedPoseDriver.rotationInput = new InputActionProperty(new InputAction("HeadRot", InputActionType.Value, "<XRHMD>/centerEyeRotation", expectedControlType: "Quaternion"));
            trackedPoseDriver.trackingType = UnityEngine.InputSystem.XR.TrackedPoseDriver.TrackingType.RotationAndPosition;
            trackedPoseDriver.updateType = UnityEngine.InputSystem.XR.TrackedPoseDriver.UpdateType.UpdateAndBeforeRender;
            trackedPoseDriver.positionInput.action.Enable(); trackedPoseDriver.rotationInput.action.Enable(); tpd = trackedPoseDriver;
        }
#endif
        if (tpd) tpd.enabled = on;
        if (!on && cam && cockpit && cam.transform.parent == cockpit.recenter) { cam.transform.localPosition = Vector3.zero; cam.transform.localRotation = Quaternion.identity; }
    }
    // @description - Recentres the cockpit view on the current head position so the driver looks straight ahead.
    // @return {Void} - nothing
    public void Recenter() { if (cockpit && cam) cockpit.Recenter(cam.transform); }
    // @description - Subscribes to the car teleport event so the flat cockpit view can be reset after a respawn.
    // @return {Void} - nothing
    void OnEnable() { CarController.Teleported += OnTeleport; }
    // @description - Unsubscribes from the car teleport event.
    // @return {Void} - nothing
    void OnDisable() { CarController.Teleported -= OnTeleport; }
    // @description - After the car is respawned, points the flat-screen cockpit camera straight ahead (the VR head pose is left alone).
    // @param {CarController} car - car that was teleported
    // @return {Void} - nothing
    void OnTeleport(CarController car) { if (!Active && cam && mode == CamMode.Cockpit) cam.transform.localRotation = Quaternion.identity; }   // flat: straight ahead (VR head pose stays the HMD's)
    // @description - Turns the flat-screen cockpit camera to look around.
    // @param {float} yaw - look left or right in degrees (positive = right)
    // @param {float} pitch - look up or down in degrees (positive = up)
    // @return {Void} - nothing
    public void FlatLook(float yaw, float pitch) { if (!Active && mode == CamMode.Cockpit && cam) cam.transform.localRotation = Quaternion.Euler(-pitch, yaw, 0f); }
    // @description - Reads the -screenshot and -shotdelay flags and starts the screenshot coroutine (used for automated screenshots).
    // @return {Void} - nothing
    void Shot()
    {
        var arguments = System.Environment.GetCommandLineArgs(); int screenshotIndex = System.Array.IndexOf(arguments, "-screenshot"); if (screenshotIndex < 0 || screenshotIndex + 1 >= arguments.Length) return;
        float delaySeconds = 3f; int delayIndex = System.Array.IndexOf(arguments, "-shotdelay"); if (delayIndex >= 0 && delayIndex + 1 < arguments.Length) float.TryParse(arguments[delayIndex + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out delaySeconds);
        StartCoroutine(ShotCo(arguments[screenshotIndex + 1], delaySeconds));
    }
    // @description - Coroutine that waits, saves a screenshot to a file and quits the application. With -shotmenu the settings menu is opened first.
    // @param {string} path - file path of the screenshot
    // @param {float} delaySeconds - seconds to wait before the capture
    // @return {IEnumerator} - coroutine enumerator
    IEnumerator ShotCo(string path, float delaySeconds)
    {
        if (Arg("-shotmenu") && router) router.menuOpen = true;
        yield return new WaitForSecondsRealtime(delaySeconds); ScreenCapture.CaptureScreenshot(path); yield return new WaitForSecondsRealtime(1f); Application.Quit();
    }
}
