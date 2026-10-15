// Camera toggle: the CameraToggle action, pressed from a keyboard, gamepad or wheel button, switches the flat-screen camera between chase and cockpit views through InputRouter and VrManager.
using NUnit.Framework; using UnityEngine; using UnityEngine.InputSystem; using UnityEngine.InputSystem.Layouts;
public class CameraToggleTests : InputTestFixture
{
    GameObject carObject, cameraObject; Cockpit cockpit; VrManager vrManager; InputRouter router; FollowCam followCam; Camera camera; InputMapper mapper;

    // @description - Builds a minimal scene: a car with a built cockpit, a camera with a FollowCam, a VrManager wired to them and an InputRouter whose mapper uses the given profile.
    // @param {BindingProfile} profile - settings profile (bindings) the router reads
    // @return {Void} - nothing
    void BuildRig(BindingProfile profile)
    {
        carObject = new GameObject("car"); var carController = carObject.AddComponent<CarController>(); carController.carParams = ScriptableObject.CreateInstance<CarParams>();
        cockpit = carObject.AddComponent<Cockpit>(); cockpit.car = carController;
        UnityEngine.TestTools.LogAssert.ignoreFailingMessages = true;   // Cockpit.Build removes primitive colliders with Destroy, which edit mode logs as an error (harmless; play mode destroys at end of frame)
        cockpit.Build(); UnityEngine.TestTools.LogAssert.ignoreFailingMessages = false;
        cameraObject = new GameObject("camera"); camera = cameraObject.AddComponent<Camera>(); followCam = cameraObject.AddComponent<FollowCam>();
        vrManager = carObject.AddComponent<VrManager>(); vrManager.car = carController; vrManager.cockpit = cockpit; vrManager.cam = camera; vrManager.follow = followCam;
        router = carObject.AddComponent<InputRouter>(); router.car = carController; mapper = new InputMapper(profile); router.map = mapper; router.vrManager = vrManager; vrManager.router = router;
        VrManager.Active = false; vrManager.mode = CamMode.Chase; followCam.enabled = true;
    }
    // @description - Destroys the objects and the input mapper created by BuildRig, then resets the input fixture.
    // @return {Void} - nothing
    public override void TearDown()
    {
        mapper?.Dispose(); Object.DestroyImmediate(carObject); Object.DestroyImmediate(cameraObject); base.TearDown();
    }
    // @description - Processes all pending Input System events so queued test input takes effect.
    // @return {Void} - nothing
    static void Tick() { InputSystem.Update(); }
    // @description - Presses a control for one input update, applies the router's camera actions, then releases it.
    // @param {UnityEngine.InputSystem.Controls.ButtonControl} button - control to press
    // @return {Void} - nothing
    void PressAndApply(UnityEngine.InputSystem.Controls.ButtonControl button)
    {
        Press(button); Tick(); router.ApplyCameraActions(); Release(button); Tick(); router.ApplyCameraActions();
    }
    // @description - Asserts the rig shows the cockpit view: mode, camera parented to the cockpit recenter anchor, chase camera disabled, 0.02 m near plane.
    // @return {Void} - nothing
    void AssertCockpitView()
    {
        Assert.AreEqual(CamMode.Cockpit, vrManager.mode); Assert.AreSame(cockpit.recenter, camera.transform.parent); Assert.IsFalse(followCam.enabled, "chase camera must be off in the cockpit"); Assert.AreEqual(0.02f, camera.nearClipPlane, 1e-4f);
    }
    // @description - Asserts the rig shows the chase view: mode, camera detached from the cockpit, chase camera enabled, 0.3 m near plane.
    // @return {Void} - nothing
    void AssertChaseView()
    {
        Assert.AreEqual(CamMode.Chase, vrManager.mode); Assert.AreNotSame(cockpit.recenter, camera.transform.parent); Assert.IsTrue(followCam.enabled, "chase camera must be on"); Assert.AreEqual(0.3f, camera.nearClipPlane, 1e-4f);
    }
    // @description - Verifies the default profile binds CameraToggle to the keyboard C key.
    // @return {Void} - nothing
    [Test] public void DefaultBindingExists()
    {
        var bindings = InputMapper.Defaults().Get(InAction.CameraToggle);
        Assert.IsTrue(bindings.Exists(binding => binding.layout == "Keyboard" && binding.control == "c"), "CameraToggle must default to the C key");
    }
    // @description - Verifies the cockpit builds the recenter anchor the camera is parented to (without it the toggle falls back to the chase view).
    // @return {Void} - nothing
    [Test] public void CockpitBuildCreatesRecenterAnchor()
    {
        BuildRig(InputMapper.Defaults()); Assert.IsNotNull(cockpit.recenter); Assert.IsNotNull(cockpit.eyeAnchor);
    }
    // @description - Verifies pressing the default keyboard key through InputRouter switches chase to cockpit and back to chase on the next press.
    // @return {Void} - nothing
    [Test] public void KeyboardPressTogglesBetweenChaseAndCockpit()
    {
        var keyboard = InputSystem.AddDevice<Keyboard>(); BuildRig(InputMapper.Defaults()); AssertChaseView();
        PressAndApply(keyboard.cKey); AssertCockpitView();
        PressAndApply(keyboard.cKey); AssertChaseView();
    }
    // @description - Verifies a rebound gamepad button toggles the camera.
    // @return {Void} - nothing
    [Test] public void GamepadButtonTogglesCamera()
    {
        var gamepad = InputSystem.AddDevice<Gamepad>(); var profile = InputMapper.Defaults();
        profile.Get(InAction.CameraToggle).Add(new Binding { layout = "Gamepad", control = "buttonNorth", calibration = new AxisCalibration() });
        BuildRig(profile); PressAndApply(gamepad.buttonNorth); AssertCockpitView(); PressAndApply(gamepad.buttonNorth); AssertChaseView();
    }
    // @description - Verifies a button on a wheel or button-box device (a generic joystick layout) toggles the camera.
    // @return {Void} - nothing
    [Test] public void WheelButtonTogglesCamera()
    {
        InputSystem.RegisterLayout<Joystick>("FakeWheelForCamera", new InputDeviceMatcher().WithProduct("Fake Wheel Camera"));
        var wheel = InputSystem.AddDevice(new InputDeviceDescription { product = "Fake Wheel Camera" }); var profile = InputMapper.Defaults();
        profile.Get(InAction.CameraToggle).Add(new Binding { layout = wheel.layout, product = "Fake Wheel Camera", control = "trigger", calibration = new AxisCalibration() });
        BuildRig(profile); var button = (UnityEngine.InputSystem.Controls.ButtonControl)wheel["trigger"];
        PressAndApply(button); AssertCockpitView(); PressAndApply(button); AssertChaseView();
    }
    // @description - Verifies the toggle is stored in the profile (cockpitView) so the next launch starts in the same view.
    // @return {Void} - nothing
    [Test] public void ToggleIsRememberedInProfile()
    {
        var keyboard = InputSystem.AddDevice<Keyboard>(); var profile = InputMapper.Defaults(); BuildRig(profile);
        PressAndApply(keyboard.cKey); Assert.IsTrue(profile.cockpitView); PressAndApply(keyboard.cKey); Assert.IsFalse(profile.cockpitView);
    }
    // @description - Verifies that with no VrManager assigned (the scene reference lost) the press does nothing and does not throw, which is the failure that left the camera stuck in the chase view.
    // @return {Void} - nothing
    [Test] public void MissingVrManagerReferenceIsHarmlessButDetectable()
    {
        var keyboard = InputSystem.AddDevice<Keyboard>(); BuildRig(InputMapper.Defaults()); router.vrManager = null;
        Assert.DoesNotThrow(() => PressAndApply(keyboard.cKey)); AssertChaseView();
    }
}
