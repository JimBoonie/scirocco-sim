using UnityEngine;
// Values: see report.md section 6. (S)=sourced, (E)=estimate.
[CreateAssetMenu(menuName = "Sim/Car Params")]
public class CarParams : ScriptableObject
{
    [Header("Mass")] public float mass = 995f; // (S) 920 kg unladen EU injection + 75 kg driver
    public Vector3 inertiaTensor = new Vector3(1350f, 1450f, 330f); // (E) pitch,yaw,roll-ish (x,y,z local)
    public float cgHeight = 0.50f;                         // (E)
    public float cgFromFront = 0.95f;                      // (E) ~60/40 F/R
    [Header("Geometry")] public float wheelbase = 2.400f;  // (S)
    public float trackFront = 1.404f, trackRear = 1.372f;  // (S)
    [Header("Suspension (suspension_params.json; S/SD/D/E tags)")]
    public SuspensionTables kin;
    public enum HandlingProfile { Baseline, ProposedUndersteer }
    [Header("Handling profile (runtime toggle: P key)")]
    public HandlingProfile profile = HandlingProfile.Baseline;   // Baseline = best estimate of the real car. ProposedUndersteer = PROPOSAL (see README)
    public SuspensionTables kinProposed;                          // tables/compliance re-solved with the proposed hardpoints/bush rates
    // Proposed-understeer physical inputs (set from sensitivity study; each one documented in README/RESULTS). Not real data.
    public float propArbFront = 7200f, propTwistBeamRoll = 8520f, propWheelRateFront = 15500f, propWheelRateRear = 11900f;
    public float propSteeringStiffness = 12000f, propPneumaticTrail0 = 0.035f;
    public CarParams ForProfile(HandlingProfile h)
    {
        var c = Instantiate(this); c.profile = h;
        if (h == HandlingProfile.ProposedUndersteer)
        {
            if (kinProposed) c.kin = kinProposed;
            c.arbFront = propArbFront; c.twistBeamRoll = propTwistBeamRoll; c.wheelRateFront = propWheelRateFront; c.wheelRateRear = propWheelRateRear;
            c.steeringStiffness = propSteeringStiffness; c.pneumaticTrail0 = propPneumaticTrail0;
        }
        return c.ApplyTirePreset();
    }                            // baked kinematic tables (kinematics/solve_kinematics.py)
    public float hubDropDesign = 0.30f;                     // corner transform -> wheel centre at design ride height (layout only)
    public float wheelRateFront = 15500f, wheelRateRear = 11900f;   // N/m (D from SD/E spring rates x MR^2)
    public float bumpTravelF = 0.08f, reboundTravelF = 0.09f, bumpTravelR = 0.085f, reboundTravelR = 0.09f; // m (E)
    public float bumpStopGap = 0.06f;                       // m of bump travel before contact (E)
    public float[] bumpStopX = { 0f, 0.01f, 0.025f, 0.035f }, bumpStopRate = { 0f, 30000f, 150000f, 600000f }; // m -> N/m (E)
    // damper curves at the damper: shaft velocity (m/s) -> force (N) (E, OE Boge twin-tube type S)
    public float[] damperV = { 0f, 0.05f, 0.1f, 0.3f, 1.0f };
    public float[] damperBumpF = { 0f, 90f, 170f, 330f, 700f }, damperReboundF = { 0f, 200f, 380f, 700f, 1300f };
    public float[] damperBumpR = { 0f, 70f, 130f, 260f, 560f }, damperReboundR = { 0f, 160f, 300f, 560f, 1050f };
    public float arbFront = 4500f;                          // N/m per unit travel difference (D: 16 mm bar)
    public float twistBeamRoll = 10650f;                    // N/m equiv. (D: 350 Nm/deg beam + 19 mm bar / track^2)
    [Header("Alignment (VW table, S)")]
    public float staticCamberF = 0.333f, staticCamberR = -1.25f;   // deg (+20', -1d15')
    public float toeTotalF = -0.25f, toeTotalR = 0.333f;           // deg total, + = toe-in (-15', +20')
    public bool useJacking = true;
    public enum RearModel { SimpleTable, FullTwistBeam }
    public enum FrontModel { KinematicTable, CompliantMultibody }
    public RearModel rearModel = RearModel.FullTwistBeam;    // FullTwistBeam: beam torsion/shear centre, bush compliance & roll steer, solved RC, anti-lift
    public FrontModel frontModel = FrontModel.CompliantMultibody; // CompliantMultibody: tables + bush compliance toe/camber (Fx, Fy) + geometric anti-dive/anti-squat; KinematicTable = fallback
    [Header("Unsprung masses (suspension_params.json, E)")]
    public bool useUnsprungMass = true;                      // separate vertical wheel DOF + tire spring; false = massless wheels (fallback)
    public float unsprungFront = 34f, unsprungRear = 27f;    // kg per corner (E)
    public float tireRate = 180000f, tireDamping = 150f;     // N/m (E, 185/60R14 at ~2.0 bar), N s/m (E)
    [Header("Tire 185/60R14 summer (E)")] public float wheelRadius = 0.289f, wheelInertia = 0.9f;
    public float Fz0 = 3000f, pCy1 = 1.30f, pDy1 = 1.10f, pDy2 = -0.10f, pEy1 = -0.8f, pKy1 = 16f, pKy2 = 1.8f;
    public float pCx1 = 1.60f, pDx1 = 1.15f, pDx2 = -0.08f, pEx1 = 0.2f, pKx1 = 20f;
    public float rBx1 = 12f, rBy1 = 9f, relaxLength = 0.45f, camberThrust = 0.9f, rollingRes = 0.012f;
    public bool tireThermal = false;      // tire thermal + pressure model (settings option; off = original handling)
    public float coldPressureFront = 170000f, coldPressureRear = 170000f;   // Pa gauge, set cold at 18.9 C: VW 1.7 bar for 185/60R14 (SD: 1.7 x3 sources, 1.8 x1)
    public float rollingResV2 = 4e-8f;   // per (km/h)^2: radial passenger tire f = f0 + 0.4e-7 v^2 (Wong, Theory of Ground Vehicles; E). Added for top-speed check
    public enum TirePreset { ModernSummer, PeriodMXV }
    public TirePreset tirePreset = TirePreset.ModernSummer;   // PeriodMXV: ESTIMATED Michelin MXV-era 185/60HR14 (see ApplyTirePreset); default stays modern
    // Period tire (E): mu ~1.0 (lambda_mu ~0.91 of modern 1.10), ~20 % lower cornering/long. stiffness, slightly longer relaxation length.
    // Not tire data; chosen to represent 1980s radial compounds, validated only against C/D 0.79 g skidpad / ~46 m braking.
    public CarParams ApplyTirePreset()
    {
        if (tirePreset == TirePreset.ModernSummer) return this;
        var c = Instantiate(this); c.tirePreset = TirePreset.ModernSummer; c.pDy1 = 1.00f; c.pDx1 = 1.05f; c.pKy1 = 13f; c.pKx1 = 16f; c.relaxLength = 0.55f; return c;
    }
    [Header("Engine 1.8 8v DX 112PS (S peak, E curve)")]
    public float[] rpmPoints = { 800, 1500, 2000, 3000, 3500, 4000, 5000, 5800, 6300, 6700 };
    public float[] torquePoints = { 95, 120, 135, 150, 153, 152, 146, 135.6f, 125, 0 }; // Nm WOT; 5800 point 138->135.6 (D) so peak power = 112 PS (S) exactly (was 114 PS)
    public float idleRpm = 850f, revLimit = 6500f, stallRpm = 350f, engineInertia = 0.13f; // kg m^2 incl flywheel (E); stallRpm = lowest speed with stable combustion when running (E)
    [Header("Starter (Bosch EF 12 V 0.95 kW, 0 001 208 416, listed for Scirocco 53B 1.8)")]
    public float starterPowerW = 950f;          // W rated (S: Bosch listing)
    public int starterPinionTeeth = 9;          // (S: Bosch listing)
    public int ringGearTeeth = 130;             // (SD: VW 12 V flywheel ring gear count; count not confirmed for the 1.8 020 flywheel)
    public float starterNoLoadRpm = 5000f;      // motor no-load speed at 12 V (E: 0.9-1 kW Bosch EF/DW class)
    public float starterArmatureInertia = 0.0006f; // kg m^2 armature + pinion (E), reflected x ratio^2 while engaged
    public float minFiringRpm = 150f;           // cranking speed above which K-Jetronic start enrichment + spark fire the engine (E)
    // Linear DC-motor model: T_motor = T0 (1 - w/w0), max power at w0/2 -> T0 = 4 P / w0 (D). Overrunning clutch: no negative torque.
    [Header("Throttle / manifold model (E, see Engine.cs)")]
    public float throttleFlowCoeff = 4.7f;   // choked-flow capacity / max engine demand (E: ~50 mm throttle body vs 1.8 L @ 6500)
    public float throttleLeakArea = 0.004f;  // closed-throttle leak
    public float idleMaxPedal = 0.12f;       // idle valve authority
    public float manifoldRate = 25f;         // manifold filling speed (1/s, normalised)
    public float mapZeroTorque = 0.22f;      // MAP (bar-ish) at which indicated torque reaches zero
    public float pumpingTorque = 10f;        // extra closed-throttle pumping loss at MAP=0 (Nm)
    public float frictionTorqueBase = 12f, frictionTorquePerKrpm = 4f;   // closed-throttle drag (E)
    [Header("Drivetrain")] public float[] gearRatios = { 3.455f, 2.118f, 1.444f, 1.129f, 0.894f }; // (S) 4Y box
    public float reverseRatio = 3.17f, finalDrive = 3.667f, drivetrainEff = 0.90f;
    public float clutchMaxTorque = 260f;                    // (E) ~1.7x peak
    public float shiftTime = 0.25f;                          // s neutral during shift (E)
    [Header("Brakes/Steering")] public float brakeTorqueFront = 1400f, brakeTorqueRear = 300f; // rear reduced 500->300 (E): rear drums + pressure regulator; 500 locked rears first // per wheel (E)
    public float steeringRatio = 17.32f, handwheelLockDeg = 634.7f; // ratio S [A2]; lock = mean sourced road-wheel lock 36.645 x 17.32 (D) = 3.53 turns (S quotes '3.5', rounded)
    public float maxSteerAngle = 36.645f;                   // nominal road-wheel lock = mean of sourced 40d22'/32d55' (S [A1]); was 630/17.32 = 36.37, which gave 40.04/32.70 (sourced lock wins)
    public float ackA = -0.01043f, ackB = 0.003059f;        // half inner/outer difference g(d)=A d + B d^2, fitted to S data: 20->18d10', lock 40d22'/32d55'
    // Steering-system compliance: the kingpin moments of both front tires (SteeringTorque: full vector model, geometry from hardpoints)
    // twists column/rack/tie rods against the driver-held handwheel and reduces road-wheel angle. 0 stiffness = off.
    public float steeringStiffness = 20000f;   // Nm/rad at the road wheels, both sides combined (E: column ~150 Nm/rad x 17.32^2 ~ 45k in series with rack/tie-rod mounts)
    public float pneumaticTrail0 = 0.025f;     // m at zero slip (E: ~1/6 of a ~0.14 m contact patch), falls linearly to 0 at peakSlipTrail
    public float peakSlipTrail = 0.15f;        // rad (E)
    // Steering system at the column (E, manual rack; used for force feedback only): Coulomb friction and viscous damping
    public float columnFriction = 0.6f;       // N m at the handwheel (E: column bearings + rack/pinion + tie-rod joints)
    public float columnFrictionVel = 0.05f;   // rad/s: numerical regularisation of Coulomb friction (flagged, not physical)
    public float columnDamping = 0.03f;       // N m s/rad at the handwheel (E)
    public float steerRate = 90f, handbrakeTorque = 1500f;   // road-wheel deg/s (E)
    [Header("Driver / cockpit (VR eye point; ALL estimates, no Scirocco II package drawing found)")]
    public float hPointBehindFrontAxle = 1.20f;  // m (E: A1-platform coupe, driver hip roughly mid-wheelbase on a 2.40 m wheelbase)
    public float hPointHeight = 0.30f;           // m above ground (E: low coupe, overall height 1.28 m S)
    public float driverLateral = 0.36f;          // m left of centreline, LHD (E: ~1.35 m interior width, seat centred in left half)
    public float driverStature = 1.75f;          // m (E: 50th-percentile adult male)
    public float sittingEyeRatio = 0.45f;        // sitting eye height / stature (E: anthropometric tables, ~0.79 m for 1.75 m)
    public float hPointAboveSeat = 0.09f;        // m, H-point above compressed seat surface (E: SAE J826 manikin)
    public float seatBackAngle = 25f;            // deg from vertical (E: typical 1980s sports seat)
    public float wheelCentreAheadOfH = 0.45f, wheelCentreAboveH = 0.33f, wheelDiameter = 0.37f, wheelTiltDeg = 25f;   // m, m, m, deg from vertical (E: 370 mm sports wheel)
    public float seatTravelFore = 0.10f, seatTravelAft = 0.10f, seatTravelUp = 0.04f, seatTravelDown = 0.04f;  // m adjuster range (E; up/down is a VR viewing offset)
    [Header("Aero")] public float cd = 0.38f, frontalArea = 1.75f; // (S Cd; C/D lists 0.38-0.39; E area)
    public float airDensity = 1.2f;   // kg/m^3 (E, ~20 C sea level; ISA 1.225)
}
