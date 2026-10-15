// Gamepad / keyboard steering assist (INPUT layer, not vehicle physics). Never used for a real wheel (SteerSource.Wheel).
// The stick sets a TARGET road-wheel angle; the steering moves towards it rate-limited, like a driver turning a wheel.
//   stick -> deadzone with rescale -> response gamma -> low-pass smoothing -> target
//   target range: full mechanical lock at parking speed, blended (speed sensitivity) towards a physics-aware limit at speed:
//     road angle = (caster follow) x front-axle travel angle + stick x (grip limit x tire peak slip angle)
//   i.e. full stick puts the front tires at their peak-grip slip angle (BeamNG "understeer reduction" / Advanced Steering idea),
//   released stick lets the wheels follow the direction the front axle is travelling (self-aligning / caster, BeamNG "oversteer reduction").
//   rate: steering speed when winding on; x countersteer factor when unwinding / crossing centre or steering into a rear slide
//   (rear axle travel angle beyond the tire's peak slip angle).
// All numbers here are user preferences (GamepadSteerSettings, saved in the bindings file); the peak slip angle comes from the tire model.
using System; using UnityEngine;
[Serializable] public class GamepadSteerSettings
{
    public bool enabled = true;           // off = previous direct behaviour (stick position -> full-lock target at CarParams.steeringRateDegPerSecond)
    public float steerSpeed = 30f;        // road-wheel deg/s when turning in (~500 deg/s at the handwheel on this car's ~17:1 centre ratio)
    public float gamma = 2.0f;            // response exponent: target = |s|^gamma (1 = linear)
    public float deadzone = 0.05f;        // stick fraction ignored around centre, the rest rescaled to 0..1
    public float smoothing = 0.04f;       // low-pass time constant on the stick (s), 0 = off
    public float speedSensitivity = 1f;   // 0 = full mechanical lock at any speed, 1 = fully grip-limited at speed
    public float gripLimit = 1.0f;        // full stick at speed = this x the front tire's peak-grip slip angle
    public float countersteer = 2.0f;     // rate factor when unwinding / countersteering
    public float casterFollow = 0.3f;     // 0..1: how far a released stick lets the wheels follow the front-axle travel direction
    public static readonly GamepadSteerSettings Default = new GamepadSteerSettings();
}
public struct GamepadSteerContext
{
    public float speed;          // m/s (forward)
    public float lockRoadDeg;    // mean road angle at the rack stop
    public float frontTravelDeg; // direction of the front-axle velocity relative to the body (deg, + right) = steer + wheel slip
    public float rearTravelDeg;  // same at the rear axle (body slip at the rear)
    public float peakSlipDeg;    // front tire's peak-lateral-force slip angle (tire model, current load)
}
public class GamepadSteering
{
    public GamepadSteerSettings settings;
    public float roadDeg, filtered, lastTarget; public bool countersteering;
    // speed band over which the grip limit fades in (m/s): below ~2 m/s the travel angle is undefined (slip-regularisation floor); by 6 m/s
    // the kinematic angle at full lock already exceeds the peak slip angle, so the blend is invisible at parking speed and complete before it matters
    public const float BlendLo = 2f, BlendHi = 6f;
    // @description - Creates the steering assist with a set of user preferences (deadzone, response curve, speed sensitivity, countersteer factor, ...).
    // @param {GamepadSteerSettings} settings - user preferences saved in the bindings file; the object is kept by reference, so menu edits take effect immediately
    // @return {Void} - the new instance
    public GamepadSteering(GamepadSteerSettings settings) { this.settings = settings; }
    // @description - Turns raw stick deflection into a shaped steering demand: removes the deadzone, rescales the rest to 0..1 and applies the response exponent |s|^gamma (gamma > 1 gives fine control near centre).
    // @param {float} stick - raw stick deflection, -1..1 (+ = right)
    // @param {float} deadzone - fraction of deflection ignored around centre, 0..1
    // @param {float} gamma - response exponent, dimensionless (1 = linear; clamped to >= 0.1)
    // @return {float} - shaped demand, -1..1 (same sign as the stick)
    public static float Shape(float stick, float deadzone, float gamma)
    {
        float stickMagnitude = Mathf.Abs(stick); if (stickMagnitude <= deadzone) return 0f;
        float rescaledMagnitude = Mathf.Clamp01((stickMagnitude - deadzone) / Mathf.Max(1e-4f, 1f - deadzone));
        return Mathf.Sign(stick) * Mathf.Pow(rescaledMagnitude, Mathf.Max(0.1f, gamma));
    }
    // @description - Converts the shaped demand into a target mean road-wheel angle. The reachable range shrinks from full mechanical lock towards the front tire peak-grip slip angle as speed rises (so a full stick at speed cannot over-steer the tires), and the front-axle travel direction is added back in proportion to the caster-follow setting (released stick lets the wheels self-align).
    // @param {float} shaped - shaped stick demand, -1..1
    // @param {GamepadSteerContext} context - current vehicle state (speed, lock angle, travel angles, peak slip angle)
    // @return {float} - target mean road-wheel angle in deg (+ = points right), limited to the mechanical lock
    public float Target(float shaped, in GamepadSteerContext context)
    {
        float speedBlend = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(BlendLo, BlendHi, context.speed));
        float range = Mathf.Lerp(context.lockRoadDeg, Mathf.Min(context.lockRoadDeg, settings.gripLimit * context.peakSlipDeg), speedBlend * Mathf.Clamp01(settings.speedSensitivity));
        float targetAngleDeg = shaped * range + speedBlend * Mathf.Clamp01(settings.casterFollow) * context.frontTravelDeg;
        return Mathf.Clamp(targetAngleDeg, -context.lockRoadDeg, context.lockRoadDeg);
    }
    // @description - Advances the assist by one frame: low-pass filters the stick, finds the target angle and moves the commanded angle towards it at the steering speed (faster when unwinding or catching a rear slide).
    // @param {float} deltaTime - frame time in s
    // @param {float} stick - raw stick deflection, -1..1 (+ = right)
    // @param {GamepadSteerContext} context - current vehicle state
    // @return {float} - commanded mean road-wheel angle in deg (+ = points right)
    public float Step(float deltaTime, float stick, in GamepadSteerContext context)
    {
        float shapedDemand = Shape(stick, settings.deadzone, settings.gamma);
        filtered = settings.smoothing > 0f ? filtered + (shapedDemand - filtered) * (1f - Mathf.Exp(-deltaTime / settings.smoothing)) : shapedDemand;
        float target = lastTarget = Target(filtered, context);
        float angleError = target - roadDeg;
        bool unwinding = Mathf.Abs(target) < Mathf.Abs(roadDeg) || target * roadDeg < 0f;
        bool intoSlide = Mathf.Abs(context.rearTravelDeg) > context.peakSlipDeg && angleError * context.rearTravelDeg > 0f && context.speed > BlendHi;
        countersteering = unwinding || intoSlide;
        float rate = settings.steerSpeed * (countersteering ? Mathf.Max(1f, settings.countersteer) : 1f);
        roadDeg = Mathf.Clamp(Mathf.MoveTowards(roadDeg, target, rate * deltaTime), -context.lockRoadDeg, context.lockRoadDeg);
        return roadDeg;
    }
    // @description - Reads the vehicle state the assist needs from the simulated car. Travel angle of an axle = wheel steer angle + tire slip angle, i.e. the direction the axle actually moves relative to the body.
    // @param {CarController} car - the car to sample
    // @return {GamepadSteerContext} - speed (m/s), lock angle (deg), front and rear travel angles (deg) and the front peak slip angle (deg)
    public static GamepadSteerContext Context(CarController car)
    {
        var carParams = car.carParams; var body = car.GetComponent<Rigidbody>();
        float TravelAngleDeg(WheelCorner wheel) => wheel.steerDeg + wheel.slipAngle * Mathf.Rad2Deg;
        return new GamepadSteerContext
        {
            speed = body ? Vector3.Dot(body.velocity, car.transform.forward) : 0f, lockRoadDeg = carParams.MeanLockDeg,
            frontTravelDeg = 0.5f * (TravelAngleDeg(car.frontLeftWheel) + TravelAngleDeg(car.frontRightWheel)), rearTravelDeg = 0.5f * (TravelAngleDeg(car.rearLeftWheel) + TravelAngleDeg(car.rearRightWheel)),
            peakSlipDeg = PeakSlipDeg(carParams, 0.5f * (car.frontLeftWheel.verticalLoad + car.frontRightWheel.verticalLoad)),
        };
    }
    // @description - Finds the slip angle at which the tire model produces its largest lateral force, by scanning 0..20 deg in 0.1 deg steps at the given load.
    // @param {CarParams} carParams - tire parameters
    // @param {float} verticalLoad - wheel vertical load in N (clamped to at least 200 N)
    // @return {float} - slip angle of peak lateral force in deg
    public static float PeakSlipDeg(CarParams carParams, float verticalLoad)
    {
        float peakForce = 0f, peakAngleDeg = 0f; verticalLoad = Mathf.Max(verticalLoad, 200f);
        for (int i = 1; i <= 200; i++) { float slipAngleDeg = i * 0.1f; float lateralForce = Mathf.Abs(PacejkaTire.PureLat(carParams, slipAngleDeg * Mathf.Deg2Rad, verticalLoad, 0f)); if (lateralForce > peakForce) { peakForce = lateralForce; peakAngleDeg = slipAngleDeg; } }
        return peakAngleDeg;
    }
}
