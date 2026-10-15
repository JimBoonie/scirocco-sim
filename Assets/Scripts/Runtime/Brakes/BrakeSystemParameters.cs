// Parameter set of the EXPERIMENTAL physics-derived braking model (tandem master cylinder, vacuum booster with pneumatic lag, diagonal circuits, rear pressure limiter,
// front disc and rear drum brakes, foot model). Nothing here is read while CarParams.experimentalBrakesEnabled is false.
// Every value carries a tag: S sourced, SD sourced but sources disagree, D derived, E estimate (bounds given), U user-reported. Full list, sources and bounds: docs/BrakeSources.md.
using UnityEngine;

[CreateAssetMenu(fileName = "BrakeSystemParameters", menuName = "Scirocco/Brake system parameters")]
public class BrakeSystemParameters : ScriptableObject
{
    [Header("Units")]
    public double pascalsPerBar = 1.0e5;                                   // Pa/bar (S: definition of the bar)
    public const double NewtonsPerPoundForce = 4.4482216152605;            // N/lbf (S: definition of the pound-force)

    [Header("Pedal and foot")]
    public double pedalRatio = 4.0;                                        // - pedal pad force to booster input rod force (E: bounds 3.5..5.0; measure pivot-to-pad over pivot-to-clevis)
    public double pedalFreePlayMeters = 0.004;                             // m pedal pad travel before the rod moves (E: bounds 0.002..0.008)
    public double footApplySpeedMetersPerSecond = 0.30;                    // m/s pedal pad speed of a hurried foot, position inputs only (E: bounds 0.10..0.60; 96 % of drivers exceed 0.10 m/s in an emergency stop, UNECE GRRF-62-inf11; brake-assist triggers sit at 0.05..0.25 m/s)
    public double footReleaseSpeedMetersPerSecond = 0.60;                  // m/s pedal pad speed when the foot lifts, position inputs only (E: bounds 0.30..1.2; the foot lifts faster than it presses, the pedal spring helps)
    public double loadCellDefaultMaxForcePounds = 70.0;                    // lbf default entry for a load-cell pedal's full-scale force (U: user-reported, Heusinkveld Sprint; about 311 N)

    [Header("Master cylinder and hydraulics (per circuit)")]
    public double masterCylinderBoreMeters = 0.02064;                      // m tandem master cylinder bore, 13/16 in (SD: ATE 20.6, Brembo 20.64, Heritage 171 611 019 E; 22.2 on the 16V)
    public double masterCylinderLostTravelMeters = 0.001;                  // m travel before the compensation port closes (E: bounds 0.0005..0.002)
    public double hydraulicComplianceCubicMetersPerPascal = 4.0e-13;       // m^3/Pa fluid, hoses, caliper, wheel cylinder and master cylinder together (E: bounds 2.5e-13..6.0e-13)

    [Header("Rear pressure limiter (one per circuit)")]
    public double rearLimiterKneePressurePascals = 35.0e5;                 // Pa (D: fit to Bentley Table b, ATE "3/35"; bounds 33e5..37e5)
    public double rearLimiterSlopeRatio = 3.0;                             // - master pressure increase per rear pressure increase above the knee (D: same fit; bounds 2.5..3.5)

    [Header("Front brake: ventilated disc 239 x 20 mm, single-piston floating caliper")]
    public double frontDiscOuterDiameterMeters = 0.239;                    // m (S: 239 x 20 vented, race.fi and vwgolfmk2.co.uk)
    public double frontDiscThicknessMeters = 0.020;                        // m new (S: Bentley p. 11-23); the minimum is 0.018 m
    public double frontCaliperPistonDiameterMeters = 0.048;                // m (S: Budweg and Cool Air VW, 48.0 mm)
    public double frontEffectiveFrictionRadiusMeters = 0.091;              // m (E: bounds 0.085..0.097; measure the pad position on the disc)
    public double frontPadFrictionCoefficient = 0.40;                      // - cold organic pad on cast iron (E: bounds 0.30..0.50)
    public double frontCaliperEfficiency = 1.0;                            // - slide-pin friction loss factor (D: bounds 0.90..1.00)
    public double frontPadRunningClearanceMeters = 0.2e-3;                 // m total pad to disc clearance (E: bounds 0.1e-3..0.3e-3)
    public double frontBitePressurePascals = 0.5e5;                        // Pa pressure below which the pads do not clamp (seal roll-back; E: bounds 0.1e5..1.0e5)

    [Header("Rear brake: self-adjusting drum 180 mm, dual-opposed wheel cylinder")]
    public double rearDrumInsideDiameterMeters = 0.180;                    // m (S: Bentley p. 11-23, 180.00 new)
    public double rearWheelCylinderBoreMeters = 0.01746;                   // m 11/16 in (SD: Brembo 17.46, Parts Place 17)
    public double rearBrakeFactorOfShoePair = 2.4;                         // - drum brake factor, torque / (wheel cylinder force x drum radius) (E: bounds 1.8..3.0 until the shoe geometry is known)
    public double rearShoeClearanceTravelAtPistonMeters = 0.4e-3;          // m travel of each wheel cylinder piston before the shoe touches (E: bounds 0.2e-3..0.8e-3)
    public double rearBitePressurePascals = 1.5e5;                         // Pa return spring pressure the wheel cylinder must exceed (E: bounds 0.5e5..3.0e5)

    [Header("Vacuum booster (single, check valve in the vacuum hose)")]
    public double boosterShellDiameterMeters = 0.203;                      // m nominal 8 in shell (SD: 7, 8 and 9 in claimed for the Golf family; no Scirocco II figure; bounds 0.178..0.229)
    public double boosterEffectiveAreaFraction = 0.70;                     // - effective diaphragm area / shell frontal area (E: bounds 0.55..0.85)
    public double boosterServoRatio = 5.0;                                 // - output force / input force on the power slope (E: bounds 3..8)
    public double boosterCutInRodForceNewtons = 40.0;                      // N input rod force before any assist (E: bounds 20..80)
    public double boosterJumpInOutputForceNewtons = 150.0;                 // N output force at cut-in, the jump-in (E: bounds 50..300)
    public double boosterCutInRampWidthNewtons = 5.0;                      // N input force over which the jump-in is smoothed (E: numerical smoothing, bounds 2..20)
    public double boosterValveDischargeCoefficient = 0.7;                  // - discharge coefficient of the air, vacuum and check valve orifices (E: bounds 0.5..0.9)
    public double boosterAirValveFlowAreaSquareMeters = 30.0e-6;           // m^2 air valve fully open (E: bounds 10e-6..60e-6; sets the apply time)
    public double boosterVacuumValveFlowAreaSquareMeters = 15.0e-6;        // m^2 vacuum valve fully open (E: bounds 5e-6..40e-6; sets the release time)
    public double boosterValveFullOpenPressureErrorPascals = 3000.0;       // Pa working chamber pressure error at which a valve is fully open (E: control gain, bounds 1000..10000)
    public double boosterVacuumChamberVolumeCubicMeters = 3.0e-3;          // m^3 (E: bounds 2.0e-3..4.5e-3)
    public double boosterWorkingChamberVolumeCubicMeters = 0.5e-3;         // m^3 dead plus swept volume, held constant (E: bounds 0.3e-3..0.9e-3)
    public double checkValveAndHoseFlowAreaSquareMeters = 40.0e-6;         // m^2 effective area of the check valve and hose (E: bounds 20e-6..80e-6; sets the recharge time)
    public double boosterLeakFlowAreaSquareMeters = 0.05e-6;               // m^2 healthy leak into the vacuum chamber (E: bounds 0.01e-6..0.2e-6; about 3.5 kPa per 15 s at 70 kPa vacuum)
    public double minimumChamberPressurePascals = 1000.0;                  // Pa numerical floor of a chamber pressure (D: avoids a zero pressure in the flow law)

    // @description - builds a runtime copy with the documented default values
    // @return {BrakeSystemParameters} - new instance, not saved to disk
    public static BrakeSystemParameters CreateDefault() => CreateInstance<BrakeSystemParameters>();

    // @description - loads the parameter asset from Resources, or the defaults when the asset is missing
    // @return {BrakeSystemParameters} - parameter set
    public static BrakeSystemParameters LoadOrDefault()
    {
        var loaded = Resources.Load<BrakeSystemParameters>("BrakeSystemParameters");
        return loaded != null ? Instantiate(loaded) : CreateDefault();
    }

    // @description - circle area from a diameter
    // @param {double} diameterMeters - diameter in m
    // @return {double} - area in m^2
    public static double CircleArea(double diameterMeters) => System.Math.PI * 0.25 * diameterMeters * diameterMeters;

    // @description - effective booster diaphragm area (shell frontal area times the effective fraction)
    // @return {double} - area in m^2
    public double BoosterEffectiveAreaSquareMeters => CircleArea(boosterShellDiameterMeters) * boosterEffectiveAreaFraction;
}
