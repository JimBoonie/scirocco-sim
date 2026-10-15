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
    public bool enabled = true;           // off = previous direct behaviour (stick position -> full-lock target at CarParams.steerRate)
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
    public GamepadSteerSettings s;
    public float roadDeg, filtered, lastTarget; public bool countersteering;
    // speed band over which the grip limit fades in (m/s): below ~2 m/s the travel angle is undefined (slip-regularisation floor); by 6 m/s
    // the kinematic angle at full lock already exceeds the peak slip angle, so the blend is invisible at parking speed and complete before it matters
    public const float BlendLo = 2f, BlendHi = 6f;
    public GamepadSteering(GamepadSteerSettings settings) { s = settings; }
    public static float Shape(float stick, float deadzone, float gamma)
    {
        float m = Mathf.Abs(stick); if (m <= deadzone) return 0f;
        float r = Mathf.Clamp01((m - deadzone) / Mathf.Max(1e-4f, 1f - deadzone));
        return Mathf.Sign(stick) * Mathf.Pow(r, Mathf.Max(0.1f, gamma));
    }
    public float Target(float shaped, in GamepadSteerContext c)
    {
        float w = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(BlendLo, BlendHi, c.speed));
        float range = Mathf.Lerp(c.lockRoadDeg, Mathf.Min(c.lockRoadDeg, s.gripLimit * c.peakSlipDeg), w * Mathf.Clamp01(s.speedSensitivity));
        float t = shaped * range + w * Mathf.Clamp01(s.casterFollow) * c.frontTravelDeg;
        return Mathf.Clamp(t, -c.lockRoadDeg, c.lockRoadDeg);
    }
    /// one step; returns the commanded mean road-wheel angle (deg)
    public float Step(float dt, float stick, in GamepadSteerContext c)
    {
        float shaped = Shape(stick, s.deadzone, s.gamma);
        filtered = s.smoothing > 0f ? filtered + (shaped - filtered) * (1f - Mathf.Exp(-dt / s.smoothing)) : shaped;
        float target = lastTarget = Target(filtered, c);
        float d = target - roadDeg;
        bool unwinding = Mathf.Abs(target) < Mathf.Abs(roadDeg) || target * roadDeg < 0f;
        bool intoSlide = Mathf.Abs(c.rearTravelDeg) > c.peakSlipDeg && d * c.rearTravelDeg > 0f && c.speed > BlendHi;
        countersteering = unwinding || intoSlide;
        float rate = s.steerSpeed * (countersteering ? Mathf.Max(1f, s.countersteer) : 1f);
        roadDeg = Mathf.Clamp(Mathf.MoveTowards(roadDeg, target, rate * dt), -c.lockRoadDeg, c.lockRoadDeg);
        return roadDeg;
    }
    /// vehicle state for the assist, read from the simulated tires (travel angle = wheel steer angle + its slip angle, see WheelCorner)
    public static GamepadSteerContext Context(CarController car)
    {
        var p = car.carParams; var rb = car.GetComponent<Rigidbody>();
        float Tr(WheelCorner w) => w.steerDeg + w.slipAngle * Mathf.Rad2Deg;
        return new GamepadSteerContext
        {
            speed = rb ? Vector3.Dot(rb.velocity, car.transform.forward) : 0f, lockRoadDeg = p.MeanLockDeg,
            frontTravelDeg = 0.5f * (Tr(car.frontLeftWheel) + Tr(car.frontRightWheel)), rearTravelDeg = 0.5f * (Tr(car.rearLeftWheel) + Tr(car.rearRightWheel)),
            peakSlipDeg = PeakSlipDeg(p, 0.5f * (car.frontLeftWheel.verticalLoad + car.frontRightWheel.verticalLoad)),
        };
    }
    /// peak lateral-force slip angle (deg) of the tire model at load Fz (scan 0..20 deg at 0.1 deg)
    public static float PeakSlipDeg(CarParams p, float Fz)
    {
        float best = 0f, at = 0f; Fz = Mathf.Max(Fz, 200f);
        for (int i = 1; i <= 200; i++) { float a = i * 0.1f; float f = Mathf.Abs(PacejkaTire.PureLat(p, a * Mathf.Deg2Rad, Fz, 0f)); if (f > best) { best = f; at = a; } }
        return at;
    }
}
