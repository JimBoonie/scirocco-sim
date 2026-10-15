// Gamepad steering assist (input layer) + settings v7 migration + VR menu mouse mapping.
using NUnit.Framework; using UnityEngine;
public class GamepadSteeringTests
{
    // @description - Creates a CarParams asset with filled suspension tables, as the assist needs them to find the tire's peak slip angle.
    // @return {CarParams} - the car parameters
    static CarParams CreateCarParams() { var carParams = ScriptableObject.CreateInstance<CarParams>(); carParams.suspensionTables = ScriptableObject.CreateInstance<SuspensionTables>(); SuspensionTablesData.Fill(carParams.suspensionTables); return carParams; }
    // @description - Builds the steering context for the assist at a given speed (a plain data struct the assist reads each step).
    // @param {float} speed - car speed in metres per second
    // @param {float} lockRoadDeg - road-wheel angle at full lock in degrees
    // @param {float} peakSlipDeg - slip angle at peak front tire force in degrees
    // @param {float} frontTravelDeg - front-axle travel direction relative to the car in degrees (negative = sliding to the right)
    // @param {float} rearTravelDeg - rear-axle travel direction relative to the car in degrees
    // @return {GamepadSteerContext} - the filled context
    static GamepadSteerContext CreateSteerContext(float speed, float lockRoadDeg = 36f, float peakSlipDeg = 6f, float frontTravelDeg = 0f, float rearTravelDeg = 0f) => new GamepadSteerContext { speed = speed, lockRoadDeg = lockRoadDeg, peakSlipDeg = peakSlipDeg, frontTravelDeg = frontTravelDeg, rearTravelDeg = rearTravelDeg };

    // @description - Verifies stick shaping: input inside the deadzone gives 0, full deflection gives +-1, the rest of the range is rescaled and raised to the gamma power ((0.475/0.95)^2 = 0.25), and gamma 1 with no deadzone is linear.
    // @return {Void} - nothing
    [Test] public void Deadzone_Rescales_And_Gamma()
    {
        Assert.AreEqual(0f, GamepadSteering.Shape(0.04f, 0.05f, 2f), 1e-6);
        Assert.AreEqual(1f, GamepadSteering.Shape(1f, 0.05f, 2f), 1e-5);
        Assert.AreEqual(-1f, GamepadSteering.Shape(-1f, 0.05f, 2f), 1e-5);
        Assert.AreEqual(0.25f, GamepadSteering.Shape(0.525f, 0.05f, 2f), 1e-4);   // (0.475/0.95)^2
        Assert.AreEqual(0.5f, GamepadSteering.Shape(0.5f, 0f, 1f), 1e-6);          // linear, no deadzone
    }
    // @description - Verifies that the steering angle moves towards the target at the configured steering speed (30 deg/s gives 3 deg in 0.1 s) and settles at full lock (36 deg) at standstill.
    // @return {Void} - nothing
    [Test] public void RateLimited_TowardsTarget()
    {
        var steering = new GamepadSteering(new GamepadSteerSettings { smoothing = 0f, deadzone = 0f, gamma = 1f, steerSpeed = 30f });
        float steerAngleDeg = steering.Step(0.1f, 1f, CreateSteerContext(0f)); Assert.AreEqual(3f, steerAngleDeg, 1e-4);           // 30 deg/s x 0.1 s, standstill target = full lock
        for (int i = 0; i < 20; i++) steerAngleDeg = steering.Step(0.1f, 1f, CreateSteerContext(0f)); Assert.AreEqual(36f, steerAngleDeg, 1e-4);
    }
    // @description - Verifies that the steering target at full stick is full lock (36 deg) when parked, limited to the peak slip angle (6 deg) at 30 m/s, and full lock again when speed sensitivity is 0.
    // @return {Void} - nothing
    [Test] public void SpeedSensitive_GripLimit()
    {
        var steering = new GamepadSteering(new GamepadSteerSettings());
        Assert.AreEqual(36f, steering.Target(1f, CreateSteerContext(0f)), 1e-4);                  // parking: full lock
        Assert.AreEqual(6f, steering.Target(1f, CreateSteerContext(30f)), 1e-4);                  // at speed: full stick = peak slip angle (gripLimit 1)
        steering.settings.speedSensitivity = 0f; Assert.AreEqual(36f, steering.Target(1f, CreateSteerContext(30f)), 1e-4);   // off: full lock at any speed
    }
    // @description - Verifies that with caster follow 0.5 and the stick released the target is half the front-axle travel angle (-4 deg for -8 deg) at speed, and 0 at parking speed where the travel angle is undefined.
    // @return {Void} - nothing
    [Test] public void CasterFollow_ReleasedStick_TracksTravelDirection()
    {
        var steering = new GamepadSteering(new GamepadSteerSettings { casterFollow = 0.5f });
        Assert.AreEqual(-4f, steering.Target(0f, CreateSteerContext(30f, frontTravelDeg: -8f)), 1e-4);       // car sliding: wheels point half-way along the front-axle velocity
        Assert.AreEqual(0f, steering.Target(0f, CreateSteerContext(0.5f, frontTravelDeg: -8f)), 1e-4);       // parking speed: no follow (travel angle undefined)
    }
    // @description - Verifies that countersteer doubles the steering speed (60 deg/s instead of 30) when unwinding or steering into a rear slide beyond the peak slip angle, and not for normal turn-in or rear slip below the peak.
    // @return {Void} - nothing
    [Test] public void Countersteer_FasterUnwind_And_IntoSlide()
    {
        var settings = new GamepadSteerSettings { smoothing = 0f, deadzone = 0f, gamma = 1f, steerSpeed = 30f, countersteer = 2f, casterFollow = 0f };
        var steering = new GamepadSteering(settings) { roadDeg = 5f };
        steering.Step(0.01f, 0f, CreateSteerContext(30f)); Assert.IsTrue(steering.countersteering); Assert.AreEqual(4.4f, steering.roadDeg, 1e-4);   // unwinding: 60 deg/s
        steering = new GamepadSteering(settings) { roadDeg = 0f };
        steering.Step(0.01f, 1f, CreateSteerContext(30f)); Assert.IsFalse(steering.countersteering); Assert.AreEqual(0.3f, steering.roadDeg, 1e-4);   // turning in: 30 deg/s
        steering = new GamepadSteering(settings) { roadDeg = 0f };
        steering.Step(0.01f, -1f, CreateSteerContext(30f, rearTravelDeg: -10f)); Assert.IsTrue(steering.countersteering); Assert.AreEqual(-0.6f, steering.roadDeg, 1e-4);   // into a rear slide beyond peak slip
        steering = new GamepadSteering(settings) { roadDeg = 0f };
        steering.Step(0.01f, -1f, CreateSteerContext(30f, rearTravelDeg: -2f)); Assert.IsFalse(steering.countersteering);   // normal cornering rear slip (below peak): not a slide
    }
    // @description - Verifies that for a noisy stick (0.3 +-0.5, 1000 steps of 0.01 s) the assisted steering travels less than 20 % of the distance of direct stick-to-angle steering.
    // @return {Void} - nothing
    [Test] public void NoisyStick_AssistedIsSmootherThanDirect()
    {
        var rng = new System.Random(7); var steering = new GamepadSteering(new GamepadSteerSettings()); float direct = 0f, directTravel = 0f, assistedTravel = 0f, previousAssisted = 0f; var context = CreateSteerContext(25f);
        for (int i = 0; i < 1000; i++)
        {
            float stick = Mathf.Clamp(0.3f + (float)(rng.NextDouble() * 2 - 1) * 0.5f, -1f, 1f);
            float directNext = Mathf.MoveTowards(direct, stick * 36f, 90f * 0.01f); directTravel += Mathf.Abs(directNext - direct); direct = directNext;
            float assistedNext = steering.Step(0.01f, stick, context); assistedTravel += Mathf.Abs(assistedNext - previousAssisted); previousAssisted = assistedNext;
        }
        Assert.Less(assistedTravel, 0.2f * directTravel, $"assisted travel {assistedTravel} vs direct {directTravel}");
    }
    // @description - Verifies that the peak slip angle derived from the Pacejka tire model at nominal load is between 3 and 15 deg and that the lateral force 2 deg beyond it is not larger.
    // @return {Void} - nothing
    [Test] public void PeakSlip_FromTireModel_IsPlausible()
    {
        var carParams = CreateCarParams(); float peakSlipDeg = GamepadSteering.PeakSlipDeg(carParams, carParams.nominalVerticalLoad);
        Assert.Greater(peakSlipDeg, 3f); Assert.Less(peakSlipDeg, 15f);
        float peakForce = PacejkaTire.PureLat(carParams, peakSlipDeg * Mathf.Deg2Rad, carParams.nominalVerticalLoad, 0f), forceBeyondPeak = PacejkaTire.PureLat(carParams, (peakSlipDeg + 2f) * Mathf.Deg2Rad, carParams.nominalVerticalLoad, 0f);
        Assert.GreaterOrEqual(Mathf.Abs(peakForce), Mathf.Abs(forceBeyondPeak));
    }
    // @description - Verifies that changed gamepad steering settings (gamma 3.1, assist off) survive a JSON save and load round trip.
    // @return {Void} - nothing
    [Test] public void Settings_V7_RoundTrip_KeepsUserValues()
    {
        var profile = InputMapper.Defaults(); profile.gamepadSteer.gamma = 3.1f; profile.gamepadSteer.enabled = false;
        var roundTripped = JsonUtility.FromJson<BindingProfile>(JsonUtility.ToJson(profile));
        Assert.AreEqual(3.1f, roundTripped.gamepadSteer.gamma, 1e-6); Assert.IsFalse(roundTripped.gamepadSteer.enabled);
    }
    // @description - Verifies the settings-menu adjust function: one step right adds 5 deg/s to the steering speed, Select resets an item to its default, repeated left steps clamp the deadzone at its minimum 0, the enable toggle flips, and the shared defaults object is untouched.
    // @return {Void} - nothing
    [Test] public void Menu_Adjust_Step_Reset()
    {
        var settings = new GamepadSteerSettings();
        InputRouter.GpAdjust(settings, 1, 1, false); Assert.AreEqual(35f, settings.steerSpeed, 1e-4);
        InputRouter.GpAdjust(settings, 1, 0, true); Assert.AreEqual(30f, settings.steerSpeed, 1e-4);         // Select = reset this item
        for (int i = 0; i < 100; i++) InputRouter.GpAdjust(settings, 3, -1, false); Assert.AreEqual(0f, settings.deadzone, 1e-5);   // clamped at min
        InputRouter.GpAdjust(settings, 0, 0, true); Assert.IsFalse(settings.enabled);
        InputRouter.GpAdjust(settings, InputRouter.GpItemCount - 1, 0, true); Assert.IsTrue(settings.enabled); Assert.AreEqual(0.05f, settings.deadzone, 1e-6);
        Assert.AreEqual(30f, GamepadSteerSettings.Default.steerSpeed);   // defaults object untouched
    }
    // @description - Verifies that with no device input the assist path yields a handwheel angle of 0 deg (stick 0 gives assist target 0), so a real wheel source is never overridden by the assist.
    // @return {Void} - nothing
    [Test] public void RealWheel_NeverAssisted()
    {   // StepSteer with no stick input and a wheel source would bypass the assist; here: assist unset -> legacy path, set -> stick path only
        var mapper = new InputMapper(InputMapper.Defaults()); mapper.assist = new GamepadSteering(mapper.profile.gamepadSteer); mapper.assistCtx = CreateSteerContext(30f); mapper.meanToHandwheel = meanRoadDeg => meanRoadDeg * 17f;
        float handwheelDeg = mapper.StepSteer(0.02f, 630f, 90f, 17f);   // no device input: stick = 0 -> assist target 0
        Assert.AreEqual(0f, handwheelDeg, 1e-4); mapper.Dispose();
    }
    // @description - Verifies that VrMouse maps a window cursor position to panel pixels (centre of a 1920x1080 window to 426.667, 240 on a 1280x720 panel at 1.5 scale, top-left to 0,0, out-of-window positions clamped to the panel edge) and that the mouse is enabled only when both flags are true.
    // @return {Void} - nothing
    [Test] public void VrMouse_MapsWindowToPanel()
    {
        var panelPoint = VrMouse.MapToPanel(new Vector2(960f, 540f), 1920f, 1080f, 1280f, 720f, 1.5f); Assert.AreEqual(426.667f, panelPoint.x, 1e-2); Assert.AreEqual(240f, panelPoint.y, 1e-3);
        var topLeftPoint = VrMouse.MapToPanel(new Vector2(0f, 1080f), 1920f, 1080f, 1280f, 720f, 1.5f); Assert.AreEqual(0f, topLeftPoint.x, 1e-4); Assert.AreEqual(0f, topLeftPoint.y, 1e-4);   // top-left
        var outsidePoint = VrMouse.MapToPanel(new Vector2(5000f, -20f), 1920f, 1080f, 1280f, 720f, 1.5f); Assert.AreEqual(853.333f, outsidePoint.x, 1e-2); Assert.AreEqual(480f, outsidePoint.y, 1e-3);   // clamped
        Assert.IsTrue(VrMouse.Enabled(true, true)); Assert.IsFalse(VrMouse.Enabled(false, true)); Assert.IsFalse(VrMouse.Enabled(true, false));
        Assert.IsNotNull(VrMouse.CursorTexture());
    }
}
