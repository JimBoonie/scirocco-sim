// Parameter asset of the EXPERIMENTAL transmission and synchronizer model (single-cone synchros, blocking rings, dog teeth, reverse clash, shift lever, loose-gear
// rattle, oil drag and mesh losses). Nothing here is read while CarParams.experimentalTransmissionEnabled is false. Every value carries a tag: S sourced, SD sourced-derived,
// D derived, E estimate (bounds given; full source list and bounds in docs/TransmissionSources.md). Sourced values override estimates.
using UnityEngine;

[CreateAssetMenu(fileName = "TransmissionParameters", menuName = "Scirocco/Transmission parameters")]
public class TransmissionParameters : ScriptableObject
{
    // Index convention of every per-gear array: element 0 = 1st gear ... element 4 = 5th gear.
    [Header("Gear set (tooth counts, driven : driving) and shafts")]
    public int[] drivenToothCount = { 38, 36, 39, 35, 42 };         // - (SD: brokevw 020 ratios, match Bentley table 8-44 rounded ratios)
    public int[] drivingToothCount = { 11, 17, 27, 31, 47 };        // - (SD: same)
    public int reverseInputToothCount = 12, reverseIdlerToothCount = 20, reverseDrivenToothCount = 38;   // - (SD: brokevw; idler 20, sleeve gear 38 on the 1st/2nd sleeve)
    public int finalDrivePinionToothCount = 18, finalDriveRingToothCount = 66;   // - (SD: 66:18 = 3.667, Bentley FD 3.67)
    public bool[] gearIsLooseOnInputShaft = { false, false, true, true, true };   // - (S: Mk1 pages: 1st/2nd driven gears loose on the output shaft, 3rd-5th drive gears loose on the input shaft)
    public float helixAngleDegrees = 20f;                           // deg (E: bounds 15..30; helical forward gears, VW 020 family)
    public float normalModuleMillimeters = 2.0f;                    // mm (E: bounds 1.75..2.5; every pair the same)
    public float gearFaceWidthMillimeters = 18f;                    // mm (E: bounds 14..24)
    public float normalPressureAngleDegrees = 20f;                  // deg (S: standard transmission gears; E for the 020, bounds 17.5..22.5)
    public float normalBacklashMillimeters = 0.12f;                 // mm normal backlash of every mesh (E: bounds 0.05..0.20; measure with a dial indicator)
    public float gearFillFactor = 0.6f;                             // - fraction of the solid disc inertia that a web gear has (E: bounds 0.5..1.0)
    public float steelDensityKilogramsPerCubicMeter = 7850f;        // kg/m^3 (S: textbook)

    [Header("Synchronizer geometry and friction (single cone, one blocking ring per gear)")]
    public float coneHalfAngleDegrees = 6.5f;                       // deg (E: bounds 6.0..7.0; family S 6..7 deg, Schaeffler API 06 p. 18)
    public float[] meanConeRadiusMeters = { 0.038f, 0.036f, 0.034f, 0.032f, 0.030f };   // m (E: bounds 0.025..0.040)
    public float blockingToothToConeRadiusRatio = 1.176f;          // - R_S / R_C, mean radius of the ring/sleeve roof teeth over the cone radius (E: bounds 1.0..1.43, i.e. R_C/R_S 0.7..1.0)
    public float[] roofIncludedAngleDegrees = { 110f, 110f, 110f, 110f, 110f };   // deg (S for 1st: Mk1 fig. 2 "110 deg tooth angle"; E for 2nd..5th, bounds 100..120)
    public float blockingToothFrictionCoefficient = 0.10f;          // - roof tooth / sleeve chamfer friction (E: bounds 0.08..0.15)
    public float[] coneFrictionSlidingSpeedsMetersPerSecond = { 0f, 0.5f, 1.0f, 3.5f };   // m/s (S: INA/Schaeffler API 06 plot abscissa)
    public float[] coneFrictionCoefficientAtSpeed = { 0.17f, 0.13f, 0.115f, 0.11f };       // - (SD: base mineral oil band read from the plot, +-0.01; brass ring on case-hardened cone)
    public float coneFrictionScale = 1.0f;                          // - multiplies the curve for the actual oil/ring (E: bounds 0.55..1.10; 0.085/0.115 = friction-modified oil, 1.0 = INA base-oil curve)
    public float[] ringToConeAxialGapMeters = { 0.0014f, 0.0014f, 0.00145f, 0.0016f, 0.0016f };   // m (S: Mk1 new 1.1..1.7 / 1.15..1.75 / 1.3..1.9 mm; mid-range of the new band, wear limit 0.5 mm)
    public float ringGapWearLimitMeters = 0.0005f;                  // m (S: Mk1, all gears)

    [Header("Dog teeth (positive engagement) and reverse idler")]
    public float dogToothRadiusMeters = 0.030f;                     // m mean radius of the sleeve/gear dog teeth (E: bounds 0.025..0.040)
    public int dogToothCount = 30;                                  // - teeth around the hub (E: bounds 24..40)
    public float dogFlankFrictionCoefficient = 0.10f;               // - steel on steel in oil (E: bounds 0.06..0.15)
    public float dogRetentionUndercutDegrees = 3.0f;                // deg back-taper that holds the gear in (E: bounds 0..5)
    public float dogAngularLashDegrees = 1.5f;                      // deg total dog play at the hub (E: bounds 1..4)
    public float dogChamferCaptureFraction = 0.25f;                 // - fraction of the tooth pitch the chamfers can index while the sleeve crosses (E: bounds 0.1..0.4)
    public float dogChamferTravelMeters = 0.003f;                   // m sleeve travel through the chamfer (E: bounds 0.002..0.005)
    public float postUnblockSleeveTravelMeters = 0.002f;            // m sleeve travel from the unblocked ring to the dog chamfer (E: bounds 0.001..0.004)
    public float reverseToothCaptureFraction = 0.25f;               // - fraction of the idler tooth pitch the straight-tooth chamfers can index (E: bounds 0.1..0.4)
    public float reverseChamferTravelMeters = 0.004f;               // m idler slide through the chamfer (E: bounds 0.002..0.006)
    public float impactRestitution = 0.3f;                          // - coefficient of restitution of a tooth impact in oil (E: bounds 0.1..0.6)

    [Header("Shift linkage and driver")]
    public float handToSleeveMechanicalAdvantage = 8f;              // - sleeve force / hand force and knob travel / sleeve travel (E: bounds 5..15)
    public float linkageEfficiency = 0.8f;                          // - (E: bounds 0.6..0.95; plastic rod ends, bushings)
    public float driverPushForceNewtons = 30f;                      // N hand force while pushing into gear (E: bounds 10..60; Giacomin and Mackenzie perceptual anchors 4.9..49 N)
    public float driverPullForceNewtons = 20f;                      // N hand force while pulling out of gear, the assumed driver arm force (E: bounds 10..50)
    public float knobPushSpeedMetersPerSecond = 0.4f;               // m/s lever speed of a normal shift (E: bounds 0.2..0.8)
    public float neutralToRingContactKnobTravelMeters = 0.03f;      // m knob travel from the neutral gate to the blocking ring contact (E: bounds 0.02..0.05)
    public float railSpacingKnobMeters = 0.04f;                     // m knob travel between the 1/2, 3/4 and 5/R gates (E: bounds 0.03..0.06)
    public float detentForceAtSleeveNewtons = 40f;                  // N spring-ball detent force the driver also overcomes when leaving a gear (E: bounds 15..80)

    [Header("Input side inertia and sleeves")]
    public float inputShaftAssemblyInertiaKilogramMetersSquared = 0.012f;   // kg m^2 shaft, fixed gears, hubs, reverse gear at the input speed, WITHOUT the loose 1st/2nd gears and WITHOUT the clutch disc (E: bounds 0.004..0.030)
    public float carrierSideDragInertiaKilogramMetersSquared = 0.5f;        // kg m^2 inertia that the output-side bearing drag clamp assumes (D: two driven wheels, 2 x 0.25 reflected to the wheels; only limits the clamp, not the dynamics)

    [Header("Losses")]
    public float looseGearRollingDragNewtonMeters = 0.05f;          // N m needle bearing drag floor at a loose gear (E: bounds 0.02..0.30)
    public float looseGearViscousDragNewtonMetersSecondPerRadian = 2.0e-4f;   // N m s/rad at the reference oil viscosity (E: bounds 5e-5..1e-3)
    public float meshFrictionCoefficient = 0.05f;                   // - mean tooth friction mu_mz (E: bounds 0.03..0.10)
    public float meshContactRatio = 1.4f;                           // - transverse contact ratio of every pair (E: bounds 1.2..1.8)
    public float outputPinionBearingTurningTorqueNewtonMeters = 0.30f;     // N m used taper pair at the pinion shaft (S: Mk1, used >= 30 N cm)
    public float differentialBearingTurningTorqueNewtonMeters = 0.30f;     // N m used taper pair of the differential (S: Mk1, used >= 30 N cm)
    public float spinLossReferenceTorqueNewtonMeters = 1.43f;       // N m unloaded input torque of a six-speed FWD box at the reference speed and oil temperature (SD: Szweda 2008, 0.15 kW at 1000 rpm in 2nd, 80 C)
    public float spinLossReferenceSpeedRevolutionsPerMinute = 1000f;   // rpm (SD: same)
    public float spinLossSecondSpeedRevolutionsPerMinute = 4000f;   // rpm (SD: same)
    public float spinLossSecondTorqueNewtonMeters = 2.91f;          // N m (SD: same, 1.22 kW at 4000 rpm)
    public float spinLossReferenceOilTemperatureKelvin = 353f;      // K (SD: same, 80 C)
    public float spinLossColdOilTemperatureKelvin = 303f;           // K (SD: same, 30 C)
    public float spinLossDropFromColdToReference = 0.465f;          // - fraction of the spin loss gone between 30 and 80 C (SD: Szweda, 44..49 %)
    public float spinLossScaleForThisGearbox = 0.35f;               // - 020 has two shafts and 2.0 L of oil versus the six-speed 3.1 L box (E: bounds 0.2..1.0)

    [Header("Gear oil (75W-90 GL-4, 2.0 L)")]
    public float oilKinematicViscosityAt40CMillimetersSquaredPerSecond = 100f;    // mm^2/s (E: bounds 85..120, typical 75W-90)
    public float oilKinematicViscosityAt100CMillimetersSquaredPerSecond = 15f;    // mm^2/s (E: bounds 13..17)
    public float oilMassKilograms = 1.76f;                          // kg (SD: 2.0 L at 0.88 kg/L)
    public float oilSpecificHeatJoulesPerKilogramKelvin = 2000f;    // J/(kg K) (S: mineral oil, 1900..2100)
    public float casingThermalConductanceWattsPerKelvin = 10f;      // W/K oil to ambient through the casing (E: bounds 5..25)

    [Header("Auto-clutch assist (keyboard) while the transmission model is on")]
    public float autoClutchOpenSeconds = 0.08f;                     // s pedal travel time when a shift starts (E: bounds 0.05..0.2)
    public float autoClutchCloseSeconds = 0.35f;                    // s pedal travel time after the gear is in (E: bounds 0.15..0.8)

    [Header("Numerics")]
    public float stickDeadBandSafetyFactor = 1f;                    // - multiplies the Karnopp dead band T dt / J (D: 1 = the largest speed change one step can produce)

    // @description - transverse tooth numbers to an overall ratio, driven over driving
    // @param {int} gearIndex - 0..4 for 1st..5th
    // @return {float} - gearbox ratio (input turns per output shaft turn)
    public float GearRatio(int gearIndex) => (float)drivenToothCount[gearIndex] / drivingToothCount[gearIndex];

    // @description - reverse ratio through the idler: (idler / input) x (driven / idler), magnitude only
    // @return {float} - reverse gearbox ratio
    public float ReverseRatio() => ((float)reverseIdlerToothCount / reverseInputToothCount) * ((float)reverseDrivenToothCount / reverseIdlerToothCount);

    // @description - builds a runtime copy with the documented default values
    // @return {TransmissionParameters} - new instance, not saved to disk
    public static TransmissionParameters CreateDefault() => CreateInstance<TransmissionParameters>();

    // @description - loads the parameter asset from Resources, or the defaults when the asset is missing
    // @return {TransmissionParameters} - parameter set
    public static TransmissionParameters LoadOrDefault()
    {
        var loaded = Resources.Load<TransmissionParameters>("TransmissionParameters");
        return loaded != null ? Instantiate(loaded) : CreateDefault();
    }
}
