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
    public SuspensionTables suspensionTables;
    // Single handling baseline (2026-10-13). The former 'ProposedUndersteer' profile is now the only set of inputs; there is no profile
    // switch. Its values are ESTIMATES (E) inside the estimated uncertainty, chosen by Taylor for the understeer balance; not sourced data.
    public CarParams RuntimeCopy() => Instantiate(this).ApplyTirePreset();   // runtime copy (tire preset applied); asset untouched                            // baked kinematic tables (kinematics/solve_kinematics.py)
    public float hubDropDesign = 0.30f;                     // corner transform -> wheel centre at design ride height (layout only)
    public float frontWheelRate = 15500f, rearWheelRate = 11900f;   // N/m (D from SD/E spring rates x MR^2)
    public float frontBumpTravel = 0.08f, frontReboundTravel = 0.09f, rearBumpTravel = 0.085f, rearReboundTravel = 0.09f; // m (E)
    public float bumpStopGap = 0.06f;                       // m of bump travel before contact (E)
    public float[] bumpStopCompressionPoints = { 0f, 0.01f, 0.025f, 0.035f }, bumpStopRatePoints = { 0f, 30000f, 150000f, 600000f }; // m -> N/m (E)
    // damper curves at the damper: shaft velocity (m/s) -> force (N) (E, OE Boge twin-tube type S)
    public float[] damperVelocityPoints = { 0f, 0.05f, 0.1f, 0.3f, 1.0f };
    public float[] frontDamperBumpForces = { 0f, 90f, 170f, 330f, 700f }, frontDamperReboundForces = { 0f, 200f, 380f, 700f, 1300f };
    public float[] rearDamperBumpForces = { 0f, 70f, 130f, 260f, 560f }, rearDamperReboundForces = { 0f, 160f, 300f, 560f, 1050f };
    public float frontAntiRollBarRate = 7200f;                          // N/m per unit travel difference (E: within the estimated range, chosen by Taylor for the understeer balance; 16 mm SD bar derivation gave 4500, 18 mm SD reported on some trims ~7200)
    public float twistBeamRollRate = 8520f;                     // N/m equiv. (E: 0.8 x the D/E 10650 = 350 Nm/deg beam + 19 mm SD bar / track^2; within the estimated range, chosen by Taylor for the understeer balance)
    [Header("Alignment (VW table, S)")]
    public float frontStaticCamberDeg = 0.333f, rearStaticCamberDeg = -1.25f;   // deg (+20', -1d15')
    public float frontTotalToeDeg = -0.25f, rearTotalToeDeg = 0.333f;           // deg total, + = toe-in (-15', +20')
    public bool useJacking = true;
    public enum RearModel { SimpleTable, FullTwistBeam }
    public enum FrontModel { KinematicTable, CompliantMultibody }
    public RearModel rearModel = RearModel.FullTwistBeam;    // FullTwistBeam: beam torsion/shear centre, bush compliance & roll steer, solved RC, anti-lift
    public FrontModel frontModel = FrontModel.CompliantMultibody; // CompliantMultibody: tables + bush compliance toe/camber (Fx, Fy) + geometric anti-dive/anti-squat; KinematicTable = fallback
    [Header("Unsprung masses (suspension_params.json, E)")]
    public bool useUnsprungMass = true;                      // separate vertical wheel DOF + tire spring; false = massless wheels (fallback)
    public float frontUnsprungMass = 34f, rearUnsprungMass = 27f;    // kg per corner (E)
    public float tireVerticalStiffness = 180000f, tireVerticalDamping = 150f;     // N/m (E, 185/60R14 at ~2.0 bar), N s/m (E)
    [Header("Tire 185/60R14 summer (E)")] public float wheelRadius = 0.289f, wheelInertia = 0.9f;
    public float nominalVerticalLoad = 3000f, lateralShapeCoefficient = 1.30f, lateralPeakFrictionCoefficient = 1.10f, lateralPeakFrictionLoadSlope = -0.10f, lateralCurvatureCoefficient = -0.8f, lateralStiffnessCoefficient = 16f, lateralStiffnessLoadCoefficient = 1.8f;
    public float longitudinalShapeCoefficient = 1.60f, longitudinalPeakFrictionCoefficient = 1.15f, longitudinalPeakFrictionLoadSlope = -0.08f, longitudinalCurvatureCoefficient = 0.2f, longitudinalStiffnessCoefficient = 20f;
    public float combinedSlipLongitudinalCoefficient = 12f, combinedSlipLateralCoefficient = 9f, relaxationLength = 0.45f, camberThrustCoefficient = 0.9f, rollingResistanceCoefficient = 0.012f;
    public bool tireThermal = false;      // tire thermal + pressure model (settings option; off = original handling)
    public float frontColdPressure = 170000f, rearColdPressure = 170000f;   // Pa gauge, set cold at 18.9 C: VW 1.7 bar for 185/60R14 (SD: 1.7 x3 sources, 1.8 x1)
    public float rollingResistanceSpeedSquaredCoefficient = 4e-8f;   // per (km/h)^2: radial passenger tire f = f0 + 0.4e-7 v^2 (Wong, Theory of Ground Vehicles; E). Added for top-speed check
    public enum TireCompound { Default, ExperimentalViscoelastic }
    public TireCompound tireCompound = TireCompound.Default;   // Quick menu "Tyre compound"; Default = sourced Pacejka/thermal model, untouched
    // Experimental (viscoelastic) compound physics switches (ViscoTire.cs). All on in the game; the smoke harness turns single ones off
    // (-overrides p.<flagName>=false) for ablation tests. Ignored by the Default compound.
    public bool viscoHysteresisEnabled = true, viscoAdhesionEnabled = true, viscoWilliamsLandelFerryEnabled = true, viscoFlashTemperatureEnabled = true, viscoPayneEffectEnabled = true;
    public bool viscoRateStiffnessEnabled = true, viscoLoadRelaxationEnabled = true, viscoRollingResistanceEnabled = true, viscoBulkHeatEnabled = true;
    public enum TirePreset { ModernSummer, PeriodMXV }
    public TirePreset tirePreset = TirePreset.ModernSummer;   // PeriodMXV: ESTIMATED Michelin MXV-era 185/60HR14 (see ApplyTirePreset); default stays modern
    // Period tire (E): mu ~1.0 (lambda_mu ~0.91 of modern 1.10), ~20 % lower cornering/long. stiffness, slightly longer relaxation length.
    // Not tire data; chosen to represent 1980s radial compounds, validated only against C/D 0.79 g skidpad / ~46 m braking.
    public CarParams ApplyTirePreset()
    {
        if (tirePreset == TirePreset.ModernSummer) return this;
        var c = Instantiate(this); c.tirePreset = TirePreset.ModernSummer; c.lateralPeakFrictionCoefficient = 1.00f; c.longitudinalPeakFrictionCoefficient = 1.05f; c.lateralStiffnessCoefficient = 13f; c.longitudinalStiffnessCoefficient = 16f; c.relaxationLength = 0.55f; return c;
    }
    [Header("Engine 1.8 8v DX 112PS (S peak, E curve)")]
    public float[] rpmPoints = { 800, 1500, 2000, 3000, 3500, 4000, 5000, 5800, 6300, 6700 };
    public float[] torquePoints = { 95, 120, 135, 150, 153, 152, 146, 135.6f, 125, 0 }; // Nm WOT; 5800 point 138->135.6 (D) so peak power = 112 PS (S) exactly (was 114 PS)
    public float idleRpm = 900f, revLimit = 6500f, stallRpm = 350f, engineInertia = 0.13f; // idleRpm 900 (S: DX with idle boost 900+-100, VW Golf Mk1 K-Jet tech data); kg m^2 incl flywheel (E); stallRpm = lowest speed with stable combustion when running (E)
    [Header("Starter (Bosch EF 12 V 0.95 kW, 0 001 208 416, listed for Scirocco 53B 1.8)")]
    public float starterPowerW = 950f;          // W rated (S: Bosch listing)
    public int starterPinionTeeth = 9;          // (S: Bosch listing)
    public int ringGearTeeth = 130;             // (SD: VW 12 V flywheel ring gear count; count not confirmed for the 1.8 020 flywheel)
    public float starterNoLoadRpm = 5000f;      // motor no-load speed at 12 V (E: 0.9-1 kW Bosch EF/DW class)
    public float starterArmatureInertia = 0.0006f; // kg m^2 armature + pinion (E), reflected x ratio^2 while engaged
    public float minFiringRpm = 150f;           // cranking speed above which K-Jetronic start enrichment + spark fire the engine (E)
    // Linear DC-motor model: T_motor = T0 (1 - w/w0), max power at w0/2 -> T0 = 4 P / w0 (D). Overrunning clutch: no negative torque.
    [Header("Air path: throttle body, manifold, cylinders (see Engine.cs; sources in diagnostics/throttle_model/SOURCES.md)")]
    public float displacement = 1.781e-3f;     // m^3 swept (S: Bentley 09 Engine, 1781 cc; 81.0 x 86.4 mm)
    public float ambientPressure = 101325f;   // Pa (S: ISA sea level)
    public float ambientTemperature = 293.15f; // K intake/manifold air, isothermal manifold (E)
    public float airGasConstant = 287.05f, airGamma = 1.4f;   // J/(kg K), cp/cv dry air (S)
    public float primaryBore = 0.035f;        // m, 1st-stage butterfly (SD: 35/44 mm two-stage body, measured Scirocco 1.8 8V, prodracing.com)
    public float secondaryBore = 0.044f;      // m, 2nd-stage butterfly (SD: same; Golf A2 GTI/RD/HT bodies listed 35/52)
    public float throttleShaftDia = 0.008f;   // m (E)
    public float closedPlateDeg = 7f;         // plate angle from bore-normal when closed on its stop (E: not published; Bentley: stop screw = paper + 1/4 turn)
    public float fullOpenPlateDeg = 90f;      // plate parallel to bore at full-throttle stop (E; Bentley: pedal floor -> plate at stop within 1 mm)
    public float secondaryStartDeg = 60f;     // primary angle at which the progressive link starts the secondary (E: delayed opening is S-forum, angle not published)
    public float throttleDischargeCoeff = 0.8f; // C_d of the plate orifice (E)
    public float manifoldVolume = 2.5e-3f;    // m^3 throttle-to-valve volume (E)
    public float indicatedEfficiency = 0.40f; // gross indicated fuel conversion efficiency (E)
    public float fuelLowerHeatingValue = 43.4e6f, airFuelRatio = 14.7f;   // J/kg gasoline LHV (S: Heywood App. D), stoichiometric AFR (S; K-Jet WOT enrichment ignored, E)
    // Chen-Flynn FMEP = A + B*Pmax + C*N + D*N^2 (N in krpm). A,C,D from Heywood (1988) 4-cyl SI total-friction correlation 0.97 + 0.15 N + 0.05 N^2 bar (WOT, incl. WOT pumping) (S-SD);
    // B = 0.005 (Chen & Flynn 1965 typical, E) with Pmax scaled by MAP, A reduced by B*Pmax_WOT so WOT FMEP is unchanged (D).
    public float frictionMepConstant = 0.67e5f, frictionMepPeakPressureCoefficient = 0.005f, frictionMepSpeedCoefficientPerKrpm = 0.15e5f, frictionMepSpeedSquaredCoefficientPerKrpm2 = 0.05e5f; // Pa
    public float peakCylinderPressureWideOpenThrottle = 60e5f;  // Pa (E)
    // Closed-throttle leakage + residual gas (2026-10-13 PT; Engine.cs, diagnostics/throttle_model/model_v2.py, SOURCES.md)
    public float stopScrewPitch = 0.7e-3f;     // m, plate stop screw thread pitch (E: M4 coarse; screw size not published)
    public float stopScrewTurns = 0.25f;       // turns past "paper just gripped" (S: Bentley 02_fuel.txt throttle valve basic adjustment)
    public float stopLeverArm = 0.015f;        // m, stop-screw contact radius from the primary shaft (E)
    public float plateBoreClearance = 0.02e-3f; // m, radial plate-to-bore machining clearance, both plates (E)
    public float compressionRatio = 10.0f;     // (S: DX 10.0:1, VW Golf/Scirocco GTI 1.8 tech data)
    public float residualExpansionExponent = 1.3f; // polytropic exponent of residual gas expanding into the intake at IVO (E; Heywood ideal cycle uses gamma)
    public float exhaustBackPressure = 101325f; // Pa, exhaust port pressure at low flow (E: ~ambient on overrun/idle)
    public float overlapGasTemperature = 600f; // K, exhaust-port gas flowing back during valve overlap (E)
    public float exhaustValveDia = 0.033f;     // m (SD: 1.8 8V head 40/33 mm valves, Bentley 1.8 cylinder head data)
    public float valveLiftAtTdc = 0.6e-3f;     // m, both valves at overlap TDC (E: stock-cam scale of the 268 cam's 1.2 mm, golfgtiforum cam table)
    public float valveOverlapDeg = 20f;        // crank deg, seat-to-seat overlap (E: between stock ~6 deg @0.050" and 268 cam 42 deg @0.004")
    public int cylinders = 4;                  // (S)
    public float mapZeroTorque = 0.22f;       // audio-only load scaling (CarAudio); no longer used by the engine physics
    [Header("Drivetrain")] public float[] gearRatios = { 3.455f, 2.118f, 1.444f, 1.129f, 0.894f }; // (S) 4Y box
    public float reverseRatio = 3.17f, finalDrive = 3.667f, drivetrainEfficiency = 0.90f;
    public float clutchMaxTorque = 260f;                    // (E) ~1.7x peak
    public float shiftTime = 0.25f;                          // s neutral during shift (E)
    [Header("Brakes/Steering")] public float brakeTorqueFront = 1400f, brakeTorqueRear = 300f; // rear reduced 500->300 (E): rear drums + pressure regulator; 500 locked rears first // per wheel (E)
    public float handwheelLockDeg = 634.7f;                 // 3.53 turns lock-to-lock (S quotes '3.5'; 634.7 D, kept from the sourced lock data)
    public float rackPerHandwheelDeg = 0.1081e-3f;          // m rack per handwheel deg, constant pinion (D: symmetric +-68.6 mm rack stop at 634.7 deg,
                                                            // the travel that gives the sourced lock 40d22'/32d55' with the fitted tie rods, proposed_geometry_fix.md)
    // per-wheel road angles come from the kinematics (SteeringLinkage); derived for the test driver / keyboard (D):
    public float MeanLockDeg => SteeringLinkage.MeanAngle(this, handwheelLockDeg);
    // Steering-system compliance: the kingpin moments of both front tires (SteeringTorque: full vector model, geometry from hardpoints)
    // twists column/rack/tie rods against the driver-held handwheel and reduces road-wheel angle. 0 stiffness = off.
    public float steeringStiffness = 12000f;   // Nm/rad at the road wheels, both sides combined (E: low end, within the estimated range, chosen by Taylor for the understeer balance; was 20000 E. C/D 1982 'slightly rubbery'. Estimate basis: column ~150 Nm/rad x on-centre linkage ratio ~19.3^2 ~ 56k in series with rack/tie-rod mounts)
    public float pneumaticTrailAtZeroSlip = 0.035f;     // m at zero slip (E: period-tire-typical, within the estimated range, chosen by Taylor for the understeer balance; was 0.025 E = ~1/6 of a ~0.14 m contact patch), falls linearly to 0 at peakSlipTrail
    public float peakSlipTrail = 0.15f;        // rad (E)
    // Steering system at the column (E, manual rack; used for force feedback only): Coulomb friction and viscous damping
    public float columnFriction = 0.6f;       // N m at the handwheel (E: column bearings + rack/pinion + tie-rod joints)
    public float columnFrictionRegularisationSpeed = 0.05f;   // rad/s: numerical regularisation of Coulomb friction (flagged, not physical)
    public float columnDamping = 0.03f;       // N m s/rad at the handwheel (E)
    public float steeringRateDegPerSecond = 90f, handbrakeTorque = 1500f;   // road-wheel deg/s (E)
    [Header("Driver / cockpit (VR eye point; ALL estimates, no Scirocco II package drawing found)")]
    public float hipPointBehindFrontAxle = 1.20f;  // m (E: A1-platform coupe, driver hip roughly mid-wheelbase on a 2.40 m wheelbase)
    public float hipPointHeight = 0.30f;           // m above ground (E: low coupe, overall height 1.28 m S)
    public float driverLateral = 0.36f;          // m left of centreline, LHD (E: ~1.35 m interior width, seat centred in left half)
    public float driverStature = 1.75f;          // m (E: 50th-percentile adult male)
    public float sittingEyeRatio = 0.45f;        // sitting eye height / stature (E: anthropometric tables, ~0.79 m for 1.75 m)
    public float hipPointAboveSeat = 0.09f;        // m, H-point above compressed seat surface (E: SAE J826 manikin)
    public float seatBackAngle = 25f;            // deg from vertical (E: typical 1980s sports seat)
    public float wheelCentreAheadOfHipPoint = 0.45f, wheelCentreAboveHipPoint = 0.33f, wheelDiameter = 0.37f, wheelTiltDeg = 25f;   // m, m, m, deg from vertical (E: 370 mm sports wheel)
    public float seatTravelFore = 0.10f, seatTravelAft = 0.10f, seatTravelUp = 0.04f, seatTravelDown = 0.04f;  // m adjuster range (E; up/down is a VR viewing offset)
    [Header("Aero")] public float dragCoefficient = 0.38f, frontalArea = 1.75f; // (S Cd; C/D lists 0.38-0.39; E area)
    public float airDensity = 1.2f;   // kg/m^3 (E, ~20 C sea level; ISA 1.225)
}
