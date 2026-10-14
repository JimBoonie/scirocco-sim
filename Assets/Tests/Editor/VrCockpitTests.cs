// VR / cockpit: eye point geometry, seat clamps, profile migration (no HMD needed).
using NUnit.Framework; using UnityEngine;
public class VrCockpitTests
{
    [Test] public void EyePoint_Inside_Cabin()
    {
        var p = ScriptableObject.CreateInstance<CarParams>(); var e = Cockpit.EyePoint(p); float g = Cockpit.GroundY(p);
        float eyeH = e.y - g; Debug.Log($"eye above ground {eyeH:0.000} m, behind front axle {p.wheelbase * 0.5f - e.z:0.000} m, lateral {e.x:0.000} m");
        Assert.Greater(eyeH, 0.85f); Assert.Less(eyeH, 1.28f - 0.12f);          // below the 1.28 m roof (S) with head clearance
        Assert.Less(e.z, p.wheelbase * 0.5f); Assert.Greater(e.z, -p.wheelbase * 0.5f); // between the axles
        Assert.Less(e.x, 0f);                                                     // LHD: left of centreline
        var w = Cockpit.WheelCentre(p); Assert.Greater(w.z, e.z); Assert.Less(w.y, e.y);   // wheel ahead of and below the eye
    }
    [Test] public void Leaving_VR_Restores_Flat_Fov_And_Projection()
    {
        var go = new GameObject("cam"); var c = go.AddComponent<Camera>(); c.aspect = 16f / 9f;
        c.fieldOfView = 105f; c.projectionMatrix = Matrix4x4.Perspective(105f, 1f, 0.02f, 1000f);   // what VR left behind (wide, custom matrix)
        c.SetStereoProjectionMatrix(Camera.StereoscopicEye.Left, Matrix4x4.Perspective(110f, 0.9f, 0.02f, 1000f));
        var vr = go.AddComponent<VrManager>(); vr.cam = c;
        vr.StopVR("test");                                                    // no router -> default 60 deg
        Assert.AreEqual(60f, c.fieldOfView, 1e-3);
        var expect = Matrix4x4.Perspective(60f, c.aspect, c.nearClipPlane, c.farClipPlane);
        for (int i = 0; i < 16; i++) Assert.AreEqual(expect[i], c.projectionMatrix[i], 1e-3, "projection matrix reset to the flat perspective");
        VrManager.RestoreFlatCamera(c, 75f); Assert.AreEqual(75f, c.fieldOfView, 1e-3);
        VrManager.RestoreFlatCamera(c, 200f); Assert.AreEqual(BindingProfile.FovMax, c.fieldOfView, 1e-3, "clamped");
        Assert.AreEqual(91.5f, VrManager.HorizontalFov(60f, 16f / 9f), 0.2f);
        Assert.AreEqual(60f, new BindingProfile().flatFovDeg);
        Object.DestroyImmediate(go);
    }
    [Test] public void Tire_Volume_Saved()
    {
        Assert.AreEqual(1f, new BindingProfile().tireVolume); Assert.IsTrue(JsonUtility.ToJson(new BindingProfile()).Contains("tireVolume"));
    }
    [Test] public void Profile_Migration_Adds_V2_Actions_Without_Overwriting()
    {
        var p = InputMapper.Defaults(); p.version = 1; p.Get(InAction.Recenter).Clear(); p.Get(InAction.MenuSelect).Clear();
        p.Get(InAction.MenuSelect).Add(new Binding { layout = "Joystick", control = "button5" });
        var q = JsonUtility.FromJson<BindingProfile>(JsonUtility.ToJson(p)); InputMapper.Migrate(q);
        Assert.AreEqual(6, q.version); Assert.Greater(q.Get(InAction.Starter).Count, 0); Assert.IsTrue(q.autoClutch); Assert.Greater(q.Get(InAction.Recenter).Count, 0);
        Assert.AreEqual(1, q.Get(InAction.MenuSelect).Count); Assert.AreEqual("button5", q.Get(InAction.MenuSelect)[0].control);
        Assert.IsFalse(q.vrEnabled);
    }
}
public class UncalibratedPedalTests : UnityEngine.InputSystem.InputTestFixture
{
    [Test] public void Uncalibrated_Clutch_Resting_At_Plus1_Reads_Zero_Keyboard_Throttle_Wins()
    {
        UnityEngine.InputSystem.InputSystem.RegisterLayout<UnityEngine.InputSystem.Joystick>("FakeSprint", new UnityEngine.InputSystem.Layouts.InputDeviceMatcher().WithProduct("Fake Sprint"));
        var ped = UnityEngine.InputSystem.InputSystem.AddDevice(new UnityEngine.InputSystem.Layouts.InputDeviceDescription { product = "Fake Sprint" });
        var kb = UnityEngine.InputSystem.InputSystem.AddDevice<UnityEngine.InputSystem.Keyboard>();
        var pr = InputMapper.Defaults(); var ax = (UnityEngine.InputSystem.Controls.AxisControl)ped["stick/x"];
        pr.Get(InAction.Clutch).Add(new Binding { layout = ped.layout, product = "Fake Sprint", control = "stick/x", cal = new AxisCal { min = -1f, max = 1f } });   // bound, never calibrated
        pr.Get(InAction.Throttle).Add(new Binding { layout = ped.layout, product = "Fake Sprint", control = "stick/y", cal = new AxisCal { min = -1f, max = 1f } });
        var m = new InputMapper(pr);
        Set(ax, 1f); InputSystemUpdate(); Assert.AreEqual(0f, m.Axis(InAction.Clutch), 1e-3f);   // rest at +1 -> 0 %, not 100 %
        Set(ax, -1f); InputSystemUpdate(); Assert.AreEqual(1f, m.Axis(InAction.Clutch), 1e-3f);  // fully pressed
        Press(kb.wKey); InputSystemUpdate(); Assert.AreEqual(1f, m.Axis(InAction.Throttle), 1e-3f); // idle pedal at rest does not hold keyboard throttle at 0
        m.Dispose();
    }
    static void InputSystemUpdate() => UnityEngine.InputSystem.InputSystem.Update();
}
