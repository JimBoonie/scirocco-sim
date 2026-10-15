// VR / cockpit: eye point geometry, seat clamps, profile migration (no HMD needed).
using NUnit.Framework; using UnityEngine;
public class VrCockpitTests
{
    // @description - Verifies that the cockpit eye point is 0.85 to 1.16 m above ground (under the 1.28 m roof with head clearance), between the axles, left of the centreline (left-hand drive), and that the steering wheel is ahead of and below the eye.
    // @return {Void} - nothing
    [Test] public void EyePoint_Inside_Cabin()
    {
        var carParams = ScriptableObject.CreateInstance<CarParams>(); var eyePoint = Cockpit.EyePoint(carParams); float groundHeight = Cockpit.GroundY(carParams);
        float eyeH = eyePoint.y - groundHeight; Debug.Log($"eye above ground {eyeH:0.000} m, behind front axle {carParams.wheelbase * 0.5f - eyePoint.z:0.000} m, lateral {eyePoint.x:0.000} m");
        Assert.Greater(eyeH, 0.85f); Assert.Less(eyeH, 1.28f - 0.12f);          // below the 1.28 m roof (S) with head clearance
        Assert.Less(eyePoint.z, carParams.wheelbase * 0.5f); Assert.Greater(eyePoint.z, -carParams.wheelbase * 0.5f); // between the axles
        Assert.Less(eyePoint.x, 0f);                                                     // LHD: left of centreline
        var wheelCentre = Cockpit.WheelCentre(carParams); Assert.Greater(wheelCentre.z, eyePoint.z); Assert.Less(wheelCentre.y, eyePoint.y);   // wheel ahead of and below the eye
    }
    // @description - Verifies that leaving VR restores the flat camera: 60 deg field of view and the plain perspective matrix after a wide custom matrix, RestoreFlatCamera sets 75 deg and clamps 200 deg to the maximum, the 16:9 horizontal field of view is 91.5 deg, and the profile default is 60 deg.
    // @return {Void} - nothing
    [Test] public void Leaving_VR_Restores_Flat_Fov_And_Projection()
    {
        var cameraObject = new GameObject("cam"); var camera = cameraObject.AddComponent<Camera>(); camera.aspect = 16f / 9f;
        camera.fieldOfView = 105f; camera.projectionMatrix = Matrix4x4.Perspective(105f, 1f, 0.02f, 1000f);   // what VR left behind (wide, custom matrix)
        camera.SetStereoProjectionMatrix(Camera.StereoscopicEye.Left, Matrix4x4.Perspective(110f, 0.9f, 0.02f, 1000f));
        var vrManager = cameraObject.AddComponent<VrManager>(); vrManager.cam = camera;
        vrManager.StopVR("test");                                                    // no router -> default 60 deg
        Assert.AreEqual(60f, camera.fieldOfView, 1e-3);
        var expectedMatrix = Matrix4x4.Perspective(60f, camera.aspect, camera.nearClipPlane, camera.farClipPlane);
        for (int i = 0; i < 16; i++) Assert.AreEqual(expectedMatrix[i], camera.projectionMatrix[i], 1e-3, "projection matrix reset to the flat perspective");
        VrManager.RestoreFlatCamera(camera, 75f); Assert.AreEqual(75f, camera.fieldOfView, 1e-3);
        VrManager.RestoreFlatCamera(camera, 200f); Assert.AreEqual(BindingProfile.FovMax, camera.fieldOfView, 1e-3, "clamped");
        Assert.AreEqual(91.5f, VrManager.HorizontalFov(60f, 16f / 9f), 0.2f);
        Assert.AreEqual(60f, new BindingProfile().flatFovDeg);
        Object.DestroyImmediate(cameraObject);
    }
    // @description - Verifies that the tire volume setting defaults to 1 and is part of the saved profile JSON.
    // @return {Void} - nothing
    [Test] public void Tire_Volume_Saved()
    {
        Assert.AreEqual(1f, new BindingProfile().tireVolume); Assert.IsTrue(JsonUtility.ToJson(new BindingProfile()).Contains("tireVolume"));
    }
}
public class UncalibratedPedalTests : UnityEngine.InputSystem.InputTestFixture
{
    // @description - Verifies that a bound but never calibrated pedal resting at +1 reads 0 % (and 100 % when pressed to -1), and that its idle state does not hold back the keyboard throttle.
    // @return {Void} - nothing
    [Test] public void Uncalibrated_Clutch_Resting_At_Plus1_Reads_Zero_Keyboard_Throttle_Wins()
    {
        UnityEngine.InputSystem.InputSystem.RegisterLayout<UnityEngine.InputSystem.Joystick>("FakeSprint", new UnityEngine.InputSystem.Layouts.InputDeviceMatcher().WithProduct("Fake Sprint"));
        var pedalDevice = UnityEngine.InputSystem.InputSystem.AddDevice(new UnityEngine.InputSystem.Layouts.InputDeviceDescription { product = "Fake Sprint" });
        var keyboard = UnityEngine.InputSystem.InputSystem.AddDevice<UnityEngine.InputSystem.Keyboard>();
        var profile = InputMapper.Defaults(); var axis = (UnityEngine.InputSystem.Controls.AxisControl)pedalDevice["stick/x"];
        profile.Get(InAction.Clutch).Add(new Binding { layout = pedalDevice.layout, product = "Fake Sprint", control = "stick/x", calibration = new AxisCalibration { min = -1f, max = 1f } });   // bound, never calibrated
        profile.Get(InAction.Throttle).Add(new Binding { layout = pedalDevice.layout, product = "Fake Sprint", control = "stick/y", calibration = new AxisCalibration { min = -1f, max = 1f } });
        var mapper = new InputMapper(profile);
        Set(axis, 1f); InputSystemUpdate(); Assert.AreEqual(0f, mapper.Axis(InAction.Clutch), 1e-3f);   // rest at +1 -> 0 %, not 100 %
        Set(axis, -1f); InputSystemUpdate(); Assert.AreEqual(1f, mapper.Axis(InAction.Clutch), 1e-3f);  // fully pressed
        Press(keyboard.wKey); InputSystemUpdate(); Assert.AreEqual(1f, mapper.Axis(InAction.Throttle), 1e-3f); // idle pedal at rest does not hold keyboard throttle at 0
        mapper.Dispose();
    }
    // @description - Processes all pending Input System events so queued test input takes effect.
    // @return {Void} - nothing
    static void InputSystemUpdate() => UnityEngine.InputSystem.InputSystem.Update();
}
