// Vacuum brake booster with a reaction disc, pneumatic lag and a vacuum reservoir. Two chamber pressures are integrated: the working chamber (air side of the diaphragm) and the vacuum chamber (supplied from the
// intake manifold through a check valve). Assist = (working - vacuum pressure) x diaphragm area, never more than (ambient - vacuum) x area (run-out). The lag is pneumatic: the working chamber fills through the
// air valve (compressible orifice) and empties into the vacuum chamber through the vacuum valve, so repeated hard pumping with a weak vacuum supply leaves less assist.
// Zero vacuum leaves assist 0: the pushrod is connected directly to the master cylinder, so a dead booster only raises the pedal effort (Bentley 11, section 4.3).
using System;

public class VacuumBoosterModel
{
    const int InversionIterations = 60;   // bisection steps of the static inverse (resolution below 1e-15 of the bracket)
    readonly BrakeSystemParameters parameters; readonly AirProperties air; readonly double diaphragmArea;
    double workingPressure, vacuumPressure;

    // @description - builds the booster in its released state at ambient pressure
    // @param {BrakeSystemParameters} parameters - brake parameters
    // @param {AirProperties} air - ambient air state
    // @return {void} - constructor
    public VacuumBoosterModel(BrakeSystemParameters parameters, AirProperties air)
    {
        this.parameters = parameters; this.air = air; diaphragmArea = parameters.BoosterEffectiveAreaSquareMeters;
        Reset(air.ambientPressurePascals);
    }

    // @description - working chamber pressure
    // @return {double} - Pa absolute
    public double WorkingPressurePascals => workingPressure;

    // @description - vacuum chamber pressure
    // @return {double} - Pa absolute
    public double VacuumPressurePascals => vacuumPressure;

    // @description - force that the diaphragm adds to the input rod force right now
    // @return {double} - N, never negative
    public double AssistForceNewtons => Math.Max((workingPressure - vacuumPressure) * diaphragmArea, 0.0);

    // @description - puts both chambers at one pressure (released booster)
    // @param {double} chamberPressurePascals - absolute pressure of both chambers, Pa
    // @return {Void} - nothing
    public void Reset(double chamberPressurePascals) { workingPressure = vacuumPressure = chamberPressurePascals; }

    // @description - largest assist the diaphragm can give at a vacuum chamber pressure (run-out limit)
    // @param {double} vacuumChamberPressurePascals - absolute vacuum chamber pressure, Pa
    // @return {double} - N
    public double MaximumAssistForceNewtons(double vacuumChamberPressurePascals) => Math.Max(air.ambientPressurePascals - vacuumChamberPressurePascals, 0.0) * diaphragmArea;

    // @description - input rod force at which the assist reaches run-out (the power slope assist (J-1) F_in + F_jump - J F_cut equals the maximum assist)
    // @param {double} vacuumChamberPressurePascals - absolute vacuum chamber pressure, Pa
    // @return {double} - input rod force in N
    public double RunOutRodForceNewtons(double vacuumChamberPressurePascals)
    {
        double servoRatio = parameters.boosterServoRatio;
        return (MaximumAssistForceNewtons(vacuumChamberPressurePascals) - parameters.boosterJumpInOutputForceNewtons + servoRatio * parameters.boosterCutInRodForceNewtons) / (servoRatio - 1.0);
    }

    // @description - steady-state assist for an input rod force (reaction disc characteristic: cut-in, jump-in, power slope, run-out)
    // @param {double} rodForceNewtons - input rod force, N
    // @param {double} vacuumChamberPressurePascals - absolute vacuum chamber pressure, Pa
    // @return {double} - assist force in N
    public double StaticAssistForce(double rodForceNewtons, double vacuumChamberPressurePascals)
    {
        if (rodForceNewtons <= parameters.boosterCutInRodForceNewtons) return 0.0;
        double powerSlopeOutput = parameters.boosterJumpInOutputForceNewtons + parameters.boosterServoRatio * (rodForceNewtons - parameters.boosterCutInRodForceNewtons);
        double rampFraction = Math.Min((rodForceNewtons - parameters.boosterCutInRodForceNewtons) / parameters.boosterCutInRampWidthNewtons, 1.0);
        double assist = Math.Max(powerSlopeOutput - rodForceNewtons, 0.0) * rampFraction;
        return Math.Min(assist, MaximumAssistForceNewtons(vacuumChamberPressurePascals));
    }

    // @description - input rod force that gives a wanted output force in steady state (inverse of rod + StaticAssistForce, found by bisection; the output rises monotonically with the input)
    // @param {double} outputForceNewtons - wanted output force on the master cylinder, N
    // @param {double} vacuumChamberPressurePascals - absolute vacuum chamber pressure, Pa
    // @return {double} - input rod force in N (the output force itself when there is no assist)
    public double RodForceForOutputForce(double outputForceNewtons, double vacuumChamberPressurePascals)
    {
        if (outputForceNewtons <= 0.0) return 0.0;
        double low = 0.0, high = outputForceNewtons;
        for (int iteration = 0; iteration < InversionIterations; iteration++)
        {
            double middle = 0.5 * (low + high);
            if (middle + StaticAssistForce(middle, vacuumChamberPressurePascals) < outputForceNewtons) low = middle; else high = middle;
        }
        return high;
    }

    // @description - advances both chamber pressures by one step: valve flows, check valve flow to the manifold and leak
    // @param {double} rodForceNewtons - input rod force from the pedal, N
    // @param {double} manifoldPressurePascals - intake manifold absolute pressure, Pa
    // @param {double} manifoldVolumeCubicMeters - intake manifold volume, m^3
    // @param {BoosterFault} fault - booster fault state
    // @param {double} stepSeconds - step length in s
    // @return {double} - mass of air the vacuum chamber pushed into the manifold during the step, kg
    public double Step(double rodForceNewtons, double manifoldPressurePascals, double manifoldVolumeCubicMeters, BoosterFault fault, double stepSeconds)
    {
        double targetWorkingPressure = vacuumPressure + StaticAssistForce(rodForceNewtons, vacuumPressure) / diaphragmArea, error = targetWorkingPressure - workingPressure;
        if (error > 0.0) ApplyAirToWorkingChamber(error, stepSeconds); else if (error < 0.0) ReleaseWorkingChamberToVacuum(-error, stepSeconds);
        double returnedMass = PumpVacuumChamberToManifold(manifoldPressurePascals, manifoldVolumeCubicMeters, 1.0 - Math.Min(Math.Max(fault.vacuumSupplyBlockedFraction, 0.0), 1.0), stepSeconds);
        LeakIntoVacuumChamber(fault.extraLeakAreaSquareMeters, stepSeconds);
        workingPressure = Math.Min(Math.Max(workingPressure, parameters.minimumChamberPressurePascals), air.ambientPressurePascals);
        vacuumPressure = Math.Min(Math.Max(vacuumPressure, parameters.minimumChamberPressurePascals), air.ambientPressurePascals);
        return returnedMass;
    }

    // @description - fraction that a valve is open for a working chamber pressure error (proportional, fully open at the configured error)
    // @param {double} pressureErrorPascals - size of the error, Pa
    // @return {double} - 0..1
    double ValveOpening(double pressureErrorPascals) => Math.Min(pressureErrorPascals / parameters.boosterValveFullOpenPressureErrorPascals, 1.0);

    // @description - lets ambient air into the working chamber through the air valve, never past the target
    // @param {double} pressureErrorPascals - target minus working pressure, Pa (positive)
    // @param {double} stepSeconds - step length in s
    // @return {Void} - nothing
    void ApplyAirToWorkingChamber(double pressureErrorPascals, double stepSeconds)
    {
        double flow = CompressibleOrifice.MassFlow(parameters.boosterValveDischargeCoefficient, parameters.boosterAirValveFlowAreaSquareMeters * ValveOpening(pressureErrorPascals), air.ambientPressurePascals, workingPressure, air.gasConstant, air.temperatureKelvin, air.heatCapacityRatio);
        double mass = Math.Min(flow * stepSeconds, pressureErrorPascals * parameters.boosterWorkingChamberVolumeCubicMeters / air.GasConstantTimesTemperature);
        workingPressure += mass * air.GasConstantTimesTemperature / parameters.boosterWorkingChamberVolumeCubicMeters;
    }

    // @description - moves air from the working chamber into the vacuum chamber through the vacuum valve, never past the target or the vacuum chamber pressure
    // @param {double} pressureErrorPascals - working pressure minus target, Pa (positive)
    // @param {double} stepSeconds - step length in s
    // @return {Void} - nothing
    void ReleaseWorkingChamberToVacuum(double pressureErrorPascals, double stepSeconds)
    {
        double flow = CompressibleOrifice.MassFlow(parameters.boosterValveDischargeCoefficient, parameters.boosterVacuumValveFlowAreaSquareMeters * ValveOpening(pressureErrorPascals), workingPressure, vacuumPressure, air.gasConstant, air.temperatureKelvin, air.heatCapacityRatio);
        double workingVolume = parameters.boosterWorkingChamberVolumeCubicMeters, vacuumVolume = parameters.boosterVacuumChamberVolumeCubicMeters;
        double toTarget = pressureErrorPascals * workingVolume / air.GasConstantTimesTemperature;
        double toEqualPressure = Math.Max(workingPressure - vacuumPressure, 0.0) * workingVolume * vacuumVolume / ((workingVolume + vacuumVolume) * air.GasConstantTimesTemperature);
        double mass = Math.Min(flow * stepSeconds, Math.Min(toTarget, toEqualPressure));
        workingPressure -= mass * air.GasConstantTimesTemperature / workingVolume; vacuumPressure += mass * air.GasConstantTimesTemperature / vacuumVolume;
    }

    // @description - lets the vacuum chamber drain into the intake manifold through the check valve (only while the chamber pressure is above the manifold pressure)
    // @param {double} manifoldPressurePascals - intake manifold absolute pressure, Pa
    // @param {double} manifoldVolumeCubicMeters - intake manifold volume, m^3
    // @param {double} supplyAreaScale - multiplies the check valve and hose area (fault: 0 = blocked)
    // @param {double} stepSeconds - step length in s
    // @return {double} - mass moved into the manifold, kg
    double PumpVacuumChamberToManifold(double manifoldPressurePascals, double manifoldVolumeCubicMeters, double supplyAreaScale, double stepSeconds)
    {
        double flow = CompressibleOrifice.MassFlow(parameters.boosterValveDischargeCoefficient, parameters.checkValveAndHoseFlowAreaSquareMeters * supplyAreaScale, vacuumPressure, manifoldPressurePascals, air.gasConstant, air.temperatureKelvin, air.heatCapacityRatio);
        double vacuumVolume = parameters.boosterVacuumChamberVolumeCubicMeters;
        double toEqualPressure = Math.Max(vacuumPressure - manifoldPressurePascals, 0.0) * vacuumVolume * manifoldVolumeCubicMeters / ((vacuumVolume + manifoldVolumeCubicMeters) * air.GasConstantTimesTemperature);
        double mass = Math.Min(flow * stepSeconds, toEqualPressure);
        vacuumPressure -= mass * air.GasConstantTimesTemperature / vacuumVolume;
        return mass;
    }

    // @description - lets ambient air leak into the vacuum chamber (healthy leak plus the fault leak)
    // @param {double} extraLeakAreaSquareMeters - fault leak area, m^2
    // @param {double} stepSeconds - step length in s
    // @return {Void} - nothing
    void LeakIntoVacuumChamber(double extraLeakAreaSquareMeters, double stepSeconds)
    {
        double flow = CompressibleOrifice.MassFlow(parameters.boosterValveDischargeCoefficient, parameters.boosterLeakFlowAreaSquareMeters + extraLeakAreaSquareMeters, air.ambientPressurePascals, vacuumPressure, air.gasConstant, air.temperatureKelvin, air.heatCapacityRatio);
        double toAmbient = Math.Max(air.ambientPressurePascals - vacuumPressure, 0.0) * parameters.boosterVacuumChamberVolumeCubicMeters / air.GasConstantTimesTemperature;
        vacuumPressure += Math.Min(flow * stepSeconds, toAmbient) * air.GasConstantTimesTemperature / parameters.boosterVacuumChamberVolumeCubicMeters;
    }
}
