// Gamepad steering assist (input layer) + settings v7 migration + VR menu mouse mapping.
using NUnit.Framework; using UnityEngine;
public class GamepadSteeringTests
{
    // @description - P.
    // @return {CarParams} - the p
    static CarParams P() { var p = ScriptableObject.CreateInstance<CarParams>(); p.suspensionTables = ScriptableObject.CreateInstance<SuspensionTables>(); SuspensionTablesData.Fill(p.suspensionTables); return p; }
    // @description - Ctx.
    // @param {float} v - value of type float
    // @param {float} lockDeg - lock deg
    // @param {float} peak - peak
    // @param {float} ft - value of type float
    // @param {float} rt - value of type float
    // @return {GamepadSteerContext} - the ctx
    static GamepadSteerContext Ctx(float v, float lockDeg = 36f, float peak = 6f, float ft = 0f, float rt = 0f) => new GamepadSteerContext { speed = v, lockRoadDeg = lockDeg, peakSlipDeg = peak, frontTravelDeg = ft, rearTravelDeg = rt };

    // @description - Deadzone rescales and gamma.
    // @return {Void} - nothing
    [Test] public void Deadzone_Rescales_And_Gamma()
    {
        Assert.AreEqual(0f, GamepadSteering.Shape(0.04f, 0.05f, 2f), 1e-6);
        Assert.AreEqual(1f, GamepadSteering.Shape(1f, 0.05f, 2f), 1e-5);
        Assert.AreEqual(-1f, GamepadSteering.Shape(-1f, 0.05f, 2f), 1e-5);
        Assert.AreEqual(0.25f, GamepadSteering.Shape(0.525f, 0.05f, 2f), 1e-4);   // (0.475/0.95)^2
        Assert.AreEqual(0.5f, GamepadSteering.Shape(0.5f, 0f, 1f), 1e-6);          // linear, no deadzone
    }
    // @description - Rate limited towards target.
    // @return {Void} - nothing
    [Test] public void RateLimited_TowardsTarget()
    {
        var g = new GamepadSteering(new GamepadSteerSettings { smoothing = 0f, deadzone = 0f, gamma = 1f, steerSpeed = 30f });
        float a = g.Step(0.1f, 1f, Ctx(0f)); Assert.AreEqual(3f, a, 1e-4);           // 30 deg/s x 0.1 s, standstill target = full lock
        for (int i = 0; i < 20; i++) a = g.Step(0.1f, 1f, Ctx(0f)); Assert.AreEqual(36f, a, 1e-4);
    }
    // @description - Speed sensitive grip limit.
    // @return {Void} - nothing
    [Test] public void SpeedSensitive_GripLimit()
    {
        var g = new GamepadSteering(new GamepadSteerSettings());
        Assert.AreEqual(36f, g.Target(1f, Ctx(0f)), 1e-4);                  // parking: full lock
        Assert.AreEqual(6f, g.Target(1f, Ctx(30f)), 1e-4);                  // at speed: full stick = peak slip angle (gripLimit 1)
        g.s.speedSensitivity = 0f; Assert.AreEqual(36f, g.Target(1f, Ctx(30f)), 1e-4);   // off: full lock at any speed
    }
    // @description - Caster follow released stick tracks travel direction.
    // @return {Void} - nothing
    [Test] public void CasterFollow_ReleasedStick_TracksTravelDirection()
    {
        var g = new GamepadSteering(new GamepadSteerSettings { casterFollow = 0.5f });
        Assert.AreEqual(-4f, g.Target(0f, Ctx(30f, ft: -8f)), 1e-4);       // car sliding: wheels point half-way along the front-axle velocity
        Assert.AreEqual(0f, g.Target(0f, Ctx(0.5f, ft: -8f)), 1e-4);       // parking speed: no follow (travel angle undefined)
    }
    // @description - Countersteer faster unwind and into slide.
    // @return {Void} - nothing
    [Test] public void Countersteer_FasterUnwind_And_IntoSlide()
    {
        var s = new GamepadSteerSettings { smoothing = 0f, deadzone = 0f, gamma = 1f, steerSpeed = 30f, countersteer = 2f, casterFollow = 0f };
        var g = new GamepadSteering(s) { roadDeg = 5f };
        g.Step(0.01f, 0f, Ctx(30f)); Assert.IsTrue(g.countersteering); Assert.AreEqual(4.4f, g.roadDeg, 1e-4);   // unwinding: 60 deg/s
        g = new GamepadSteering(s) { roadDeg = 0f };
        g.Step(0.01f, 1f, Ctx(30f)); Assert.IsFalse(g.countersteering); Assert.AreEqual(0.3f, g.roadDeg, 1e-4);   // turning in: 30 deg/s
        g = new GamepadSteering(s) { roadDeg = 0f };
        g.Step(0.01f, -1f, Ctx(30f, rt: -10f)); Assert.IsTrue(g.countersteering); Assert.AreEqual(-0.6f, g.roadDeg, 1e-4);   // into a rear slide beyond peak slip
        g = new GamepadSteering(s) { roadDeg = 0f };
        g.Step(0.01f, -1f, Ctx(30f, rt: -2f)); Assert.IsFalse(g.countersteering);   // normal cornering rear slip (below peak): not a slide
    }
    // @description - Noisy stick assisted is smoother than direct.
    // @return {Void} - nothing
    [Test] public void NoisyStick_AssistedIsSmootherThanDirect()
    {
        var rng = new System.Random(7); var g = new GamepadSteering(new GamepadSteerSettings()); float direct = 0f, dSum = 0f, aSum = 0f, aPrev = 0f; var c = Ctx(25f);
        for (int i = 0; i < 1000; i++)
        {
            float stick = Mathf.Clamp(0.3f + (float)(rng.NextDouble() * 2 - 1) * 0.5f, -1f, 1f);
            float dn = Mathf.MoveTowards(direct, stick * 36f, 90f * 0.01f); dSum += Mathf.Abs(dn - direct); direct = dn;
            float an = g.Step(0.01f, stick, c); aSum += Mathf.Abs(an - aPrev); aPrev = an;
        }
        Assert.Less(aSum, 0.2f * dSum, $"assisted travel {aSum} vs direct {dSum}");
    }
    // @description - Peak slip from tire model is plausible.
    // @return {Void} - nothing
    [Test] public void PeakSlip_FromTireModel_IsPlausible()
    {
        var p = P(); float a = GamepadSteering.PeakSlipDeg(p, p.nominalVerticalLoad);
        Assert.Greater(a, 3f); Assert.Less(a, 15f);
        float fPk = PacejkaTire.PureLat(p, a * Mathf.Deg2Rad, p.nominalVerticalLoad, 0f), fMore = PacejkaTire.PureLat(p, (a + 2f) * Mathf.Deg2Rad, p.nominalVerticalLoad, 0f);
        Assert.GreaterOrEqual(Mathf.Abs(fPk), Mathf.Abs(fMore));
    }
    // @description - Settings v7 round trip keeps user values.
    // @return {Void} - nothing
    [Test] public void Settings_V7_RoundTrip_KeepsUserValues()
    {
        var p = InputMapper.Defaults(); p.gamepadSteer.gamma = 3.1f; p.gamepadSteer.enabled = false;
        var q = JsonUtility.FromJson<BindingProfile>(JsonUtility.ToJson(p));
        Assert.AreEqual(3.1f, q.gamepadSteer.gamma, 1e-6); Assert.IsFalse(q.gamepadSteer.enabled);
    }
    // @description - Menu adjust step reset.
    // @return {Void} - nothing
    [Test] public void Menu_Adjust_Step_Reset()
    {
        var g = new GamepadSteerSettings();
        InputRouter.GpAdjust(g, 1, 1, false); Assert.AreEqual(35f, g.steerSpeed, 1e-4);
        InputRouter.GpAdjust(g, 1, 0, true); Assert.AreEqual(30f, g.steerSpeed, 1e-4);         // Select = reset this item
        for (int i = 0; i < 100; i++) InputRouter.GpAdjust(g, 3, -1, false); Assert.AreEqual(0f, g.deadzone, 1e-5);   // clamped at min
        InputRouter.GpAdjust(g, 0, 0, true); Assert.IsFalse(g.enabled);
        InputRouter.GpAdjust(g, InputRouter.GpItemCount - 1, 0, true); Assert.IsTrue(g.enabled); Assert.AreEqual(0.05f, g.deadzone, 1e-6);
        Assert.AreEqual(30f, GamepadSteerSettings.Default.steerSpeed);   // defaults object untouched
    }
    // @description - Real wheel never assisted.
    // @return {Void} - nothing
    [Test] public void RealWheel_NeverAssisted()
    {   // StepSteer with no stick input and a wheel source would bypass the assist; here: assist unset -> legacy path, set -> stick path only
        var m = new InputMapper(InputMapper.Defaults()); m.assist = new GamepadSteering(m.profile.gamepadSteer); m.assistCtx = Ctx(30f); m.meanToHandwheel = x => x * 17f;
        float hw = m.StepSteer(0.02f, 630f, 90f, 17f);   // no device input: stick = 0 -> assist target 0
        Assert.AreEqual(0f, hw, 1e-4); m.Dispose();
    }
    // @description - Vr mouse maps window to panel.
    // @return {Void} - nothing
    [Test] public void VrMouse_MapsWindowToPanel()
    {
        var c = VrMouse.MapToPanel(new Vector2(960f, 540f), 1920f, 1080f, 1280f, 720f, 1.5f); Assert.AreEqual(426.667f, c.x, 1e-2); Assert.AreEqual(240f, c.y, 1e-3);
        var tl = VrMouse.MapToPanel(new Vector2(0f, 1080f), 1920f, 1080f, 1280f, 720f, 1.5f); Assert.AreEqual(0f, tl.x, 1e-4); Assert.AreEqual(0f, tl.y, 1e-4);   // top-left
        var o = VrMouse.MapToPanel(new Vector2(5000f, -20f), 1920f, 1080f, 1280f, 720f, 1.5f); Assert.AreEqual(853.333f, o.x, 1e-2); Assert.AreEqual(480f, o.y, 1e-3);   // clamped
        Assert.IsTrue(VrMouse.Enabled(true, true)); Assert.IsFalse(VrMouse.Enabled(false, true)); Assert.IsFalse(VrMouse.Enabled(true, false));
        Assert.IsNotNull(VrMouse.CursorTexture());
    }
}
