// Parameter asset of the EXPERIMENTAL driveline shuffle model (clutch judder, shuffle, lash clonk, firing pulses). Nothing here is read while
// CarParams.experimentalDrivelineEnabled is false. Every value carries a tag: S sourced, SD sourced-derived, D derived, E estimate (bounds given;
// full source list and bounds in docs/DrivelineShuffleSources.md). Sourced values override estimates.
using UnityEngine;

[CreateAssetMenu(fileName = "ShuffleDrivelineParameters", menuName = "Scirocco/Shuffle driveline parameters")]
public class ShuffleDrivelineParameters : ScriptableObject
{
    [Header("Engine geometry and firing pulses (minimal Wiebe burn, no ignition system / knock / mixture model)")]
    public bool firingPulsesEnabled = true;                    // include gas and reciprocating-inertia torque pulsation
    public float boreMeters = 0.081f;                          // m (S: VW EA827 1.8 8V 81.0 mm, motor-car.net / rwcmotorsport.com)
    public float strokeMeters = 0.0864f;                       // m (S: same sources, 86.4 mm)
    public float connectingRodLengthMeters = 0.144f;           // m (SD: 144 mm, piston-vendor listing for 1.8 8V, Wiseco)
    public float reciprocatingMassPerCylinderKilograms = 0.64f; // kg piston+pin+rings+rod small end (E: bounds 0.50..0.80; forged aftermarket piston 0.298-0.318 kg, pin 0.116 kg)
    public float wiebeEfficiencyFactor = 5f;                   // a (S: Heywood 1988 / Johansson et al. 2009 typical, 99.3 % burned at the end of the duration)
    public float wiebeShapeFactor = 2f;                        // m (S: same sources, S-shaped heat release)
    public float burnDurationDegrees = 60f;                    // crank deg 0-100 % burn (E: bounds 45..80; 10-90 % duration then 0.496 x this)
    public float halfBurnAngleAfterTopDeadCenterDegrees = 9f;  // crank deg ATDC of 50 % burned at the standard (MBT) spark timing (SD: 8-10 deg, SAE 2012-36-0498)
    public float cylinderHeatCapacityRatio = 1.3f;             // effective gamma of the burning charge (E: bounds 1.25..1.38; Heywood mid-cycle value)

    [Header("Clutch friction")]
    public float frictionFaceOuterRadiusMeters = 0.210f * 0.5f;  // m (S: 210 mm disc, 1.8 8V / 020 gearbox)
    public float frictionFaceInnerToOuterRadiusRatio = 0.70f;  // - (E: bounds 0.62..0.76, typical organic facing)
    public int frictionFaceCount = 2;                          // - (S: one disc has two facings)
    public float kineticFrictionCoefficient = 0.35f;           // - at reference temperature (E: bounds 0.25..0.45, organic facing on cast iron)
    public float staticToKineticFrictionRatio = 1.10f;         // - (E: bounds 1.00..1.30)
    public float frictionSlopePerMeterPerSecond = 0f;          // 1/(m/s) relative change of kinetic friction with rubbing speed (E: bounds -0.01..+0.01; 0 = no speed dependence)
    public float fadeOnsetTemperatureKelvin = 573f;            // K (E: bounds 520..620)
    public float fadeCompleteTemperatureKelvin = 673f;         // K (E: bounds 620..750)
    public float maximumFadeFraction = 0.30f;                  // - friction lost at complete fade (S: up to 30 %, Haramina et al.)
    public float flywheelMassKilograms = 5.4f;                 // kg (SD: ~12 lb 1.8 8V flywheel listing)
    public float pressurePlateMassKilograms = 2.0f;            // kg (E: bounds 1.5..3.0)
    public float rubbingMassSpecificHeatJoulesPerKilogramKelvin = 490f; // J/(kg K) steel/cast iron (S: textbook, 460..500)
    public float clutchCoolingTimeConstantSeconds = 240f;      // s (E: bounds 60..600)

    [Header("Clutch pedal to clamp load (cable release, diaphragm spring)")]
    public float releaseLeverTravelAtFullPedalMeters = 0.016f; // m release-lever motion at the pedal stop (E: bounds 0.012..0.022)
    public float releaseLeverFreePlayMeters = 0.006f;          // m (S: Bentley release-lever free play 6 mm)
    public float diaphragmFingerLeverRatio = 2.0f;             // - plate lift per release-lever travel inverse (E: bounds 1.5..3.0), plate lift = lever travel / ratio
    public float cushionSpringTravelMeters = 0.001f;           // m plate lift over which clamp load falls from full to zero (S: Sachs facing cushion up to about 1 mm)

    [Header("Clutch disc damper (three stages, drive and coast)")]
    public float[] damperStageStiffnessNewtonMetersPerDegree = { 0.5f, 24f, 291f };   // N m/deg (E: stage 1 0.2..1.5, stage 2 10..56, stage 3 150..400)
    public float[] damperDriveStageEndDegrees = { 5f, 16f };    // deg relative twist at the end of stage 1 and 2, drive side (E: bounds 3..8 and 10..22)
    public float[] damperCoastStageEndDegrees = { 5f, 12f };    // deg coast side (E: bounds 3..8 and 8..16)
    public float[] damperStageHysteresisNewtonMeters = { 1f, 10f, 20f };   // N m friction torque per stage (E: bounds 0.3..4, 4..36, 10..60)
    public float clutchDiscInertiaKilogramMetersSquared = 0.0077f;   // kg m^2 hub+disc, engine side of damper not included (E: bounds 0.002..0.030)
    public float inputShaftInertiaKilogramMetersSquared = 0.020f;    // kg m^2 input shaft and gears at gearbox input speed (E: bounds 0.005..0.040)

    [Header("Gearbox, final drive and differential")]
    public float carrierInertiaKilogramMetersSquared = 0.015f;       // kg m^2 ring gear, carrier, side gears and output-side gear train at carrier speed (E: bounds 0.005..0.030)
    public float gearMeshStiffnessAtCarrierNewtonMetersPerRadian = 1.0e6f; // N m/rad whole mesh chain referred to the carrier (E: bounds 1e5..1e7)
    public float gearMeshDampingRatio = 0.05f;                       // - damping ratio of the mesh chain (E: bounds 0.01..0.20)
    public float gearLashAtInputShaftDegrees = 1.6f;                 // deg total backlash referred to the gearbox input shaft (E: bounds 0.5..5)

    [Header("Halfshafts (unequal length, open differential)")]
    public float leftShaftLengthMeters = 0.447f;                     // m (SD: Golf II GTI 447 mm, left)
    public float rightShaftLengthMeters = 0.6812f;                   // m (SD: Golf II GTI 681.2 mm, right)
    public float leftShaftDiameterMeters = 0.022f;                   // m solid equivalent diameter (E: bounds 0.021..0.025)
    public float rightToLeftTorsionalStiffnessRatio = 0.83f;         // - (E: bounds 0.656..1.0; 0.656 = same solid diameter, 1.0 = right shaft sized to match left stiffness)
    public float shaftShearModulusPascals = 79.3e9f;                 // Pa (S: steel)
    public float shaftStructuralLossFactor = 0.01f;                  // - joints + shafts (E: bounds 0.002..0.05)
    public float dampingReferenceFrequencyHertz = 4f;                // Hz frequency where the loss factor equals viscous damping (E: shuffle band 2..8 Hz)
    public float shaftAndJointLashAtWheelDegrees = 0.3f;             // deg total play at the wheel: splines and CV joints, both sides (E: bounds 0.1..1.0)

    [Header("Engine mounts (engine roll about the crank axis)")]
    public bool engineRollEnabled = true;                            // include the engine block roll degree of freedom
    public float engineBlockRollInertiaKilogramMetersSquared = 4.2f; // kg m^2 engine+gearbox about the roll axis (E: bounds 3..6)
    public float engineRollNaturalFrequencyHertz = 11.6f;            // Hz rigid-body roll mode on the mounts (E: bounds 7..20; Gaudin-class value)
    public float engineRollDampingRatio = 0.15f;                     // - (E: bounds 0.05..0.30)

    [Header("Numerics")]
    public float maximumPhaseAdvancePerMicrostepRadians = 0.1f;      // rad of the fastest mode per micro-step (D: set by the step-size convergence test)

    // @description - clutch effective friction radius for uniform wear (worn-in facing), R_eff = (Ro + Ri) / 2
    // @return {float} - effective radius in m
    public float ClutchEffectiveRadiusMeters() => 0.5f * frictionFaceOuterRadiusMeters * (1f + frictionFaceInnerToOuterRadiusRatio);

    // @description - torsional stiffness of the left halfshaft from G J / L with J = pi d^4 / 32
    // @return {float} - stiffness in N m/rad
    public float LeftShaftStiffness()
    {
        float polarMoment = Mathf.PI * Mathf.Pow(leftShaftDiameterMeters, 4f) / 32f;
        return shaftShearModulusPascals * polarMoment / leftShaftLengthMeters;
    }

    // @description - torsional stiffness of the right halfshaft: the left stiffness scaled by the estimated ratio
    // @return {float} - stiffness in N m/rad
    public float RightShaftStiffness() => LeftShaftStiffness() * rightToLeftTorsionalStiffnessRatio;

    // @description - stiffness of the two halfshafts combined for the open differential: the carrier torque splits equally, so the carrier
    //   deflection relative to the mean wheel angle is T (1/kL + 1/kR)/4 and the combined stiffness is 4 kL kR / (kL + kR) (D)
    // @return {float} - combined stiffness in N m/rad referred to the carrier
    public float CombinedShaftStiffness()
    {
        float left = LeftShaftStiffness(), right = RightShaftStiffness();
        return 4f * left * right / (left + right);
    }

    // @description - builds a runtime copy with the documented default values
    // @return {ShuffleDrivelineParameters} - new instance, not saved to disk
    public static ShuffleDrivelineParameters CreateDefault() => CreateInstance<ShuffleDrivelineParameters>();

    // @description - loads the parameter asset from Resources, or the defaults when the asset is missing
    // @return {ShuffleDrivelineParameters} - parameter set
    public static ShuffleDrivelineParameters LoadOrDefault()
    {
        var loaded = Resources.Load<ShuffleDrivelineParameters>("ShuffleDrivelineParameters");
        return loaded != null ? Instantiate(loaded) : CreateDefault();
    }
}
