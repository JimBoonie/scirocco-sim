// One of the two diagonal brake circuits: a master cylinder chamber feeding one front caliper and the diagonally opposite rear wheel cylinder through the rear pressure limiter.
// Quasi-static (no fluid inertia): the pressure follows from the master cylinder force; the pedal travel follows from the fluid volume that the pads, shoes and the line compliance take.
using System;

public class HydraulicBrakeCircuit
{
    readonly BrakeSystemParameters parameters; readonly double masterArea; readonly double takeUpVolumeCubicMeters;

    // @description - builds the circuit from the parameters (master area and take-up volume of one caliper and one wheel cylinder)
    // @param {BrakeSystemParameters} parameters - brake parameters
    // @return {void} - constructor
    public HydraulicBrakeCircuit(BrakeSystemParameters parameters)
    {
        const double PistonsPerWheelCylinder = 2.0;   // - dual-opposed wheel cylinder (S: Bentley 11)
        this.parameters = parameters; masterArea = BrakeSystemParameters.CircleArea(parameters.masterCylinderBoreMeters);
        double frontTakeUp = BrakeSystemParameters.CircleArea(parameters.frontCaliperPistonDiameterMeters) * parameters.frontPadRunningClearanceMeters;
        double rearTakeUp = PistonsPerWheelCylinder * BrakeSystemParameters.CircleArea(parameters.rearWheelCylinderBoreMeters) * parameters.rearShoeClearanceTravelAtPistonMeters;
        takeUpVolumeCubicMeters = frontTakeUp + rearTakeUp;
    }

    // @description - master cylinder area
    // @return {double} - m^2
    public double MasterArea => masterArea;

    // @description - master cylinder pressure from the pushrod force (balanced tandem: both chambers carry the same pressure)
    // @param {double} pushrodForceNewtons - force from the booster output rod, N
    // @return {double} - pressure in Pa gauge, never negative
    public double MasterPressure(double pushrodForceNewtons) => Math.Max(pushrodForceNewtons, 0.0) / masterArea;

    // @description - front line pressure (the front line is unaffected by the limiter)
    // @param {double} masterPressurePascals - master pressure, Pa
    // @return {double} - Pa
    public double FrontPressure(double masterPressurePascals) => masterPressurePascals;

    // @description - rear line pressure after the pressure limiter
    // @param {double} masterPressurePascals - master pressure, Pa
    // @return {double} - Pa
    public double RearPressure(double masterPressurePascals) => RearPressureLimiter.RearPressure(masterPressurePascals, parameters.rearLimiterKneePressurePascals, parameters.rearLimiterSlopeRatio);

    // @description - pedal pad travel that holds a given master pressure: free play, lost travel, take-up volume, then the line compliance
    // @param {double} masterPressurePascals - master pressure, Pa
    // @return {double} - pedal pad travel in m (0 at zero pressure)
    public double PedalTravelAtPressure(double masterPressurePascals)
    {
        if (masterPressurePascals <= 0.0) return 0.0;
        double displacedVolume = takeUpVolumeCubicMeters + parameters.hydraulicComplianceCubicMetersPerPascal * masterPressurePascals;
        return parameters.pedalFreePlayMeters + parameters.pedalRatio * (parameters.masterCylinderLostTravelMeters + displacedVolume / masterArea);
    }

    // @description - master pressure that a pedal pad travel produces (inverse of PedalTravelAtPressure)
    // @param {double} pedalTravelMeters - pedal pad travel, m
    // @return {double} - pressure in Pa gauge, 0 while the travel is only free play, lost travel and take-up
    public double PressureAtPedalTravel(double pedalTravelMeters)
    {
        double masterTravel = (pedalTravelMeters - parameters.pedalFreePlayMeters) / parameters.pedalRatio - parameters.masterCylinderLostTravelMeters;
        double compressedVolume = masterTravel * masterArea - takeUpVolumeCubicMeters;
        return Math.Max(compressedVolume, 0.0) / parameters.hydraulicComplianceCubicMetersPerPascal;
    }
}
