// Orchestrator of the EXPERIMENTAL physics-derived braking model. Chain per step: pedal (force or rate-limited travel) -> input rod (pedal ratio) -> vacuum booster (pneumatic lag, run-out, vacuum supply)
// -> master cylinder pressure -> two diagonal circuits (front caliper line, rear line through the pressure limiter) -> front disc and rear drum torques.
// Not included: ABS, pad or drum temperature (fade), booster hysteresis, hydraulic line inertia. The handbrake stays outside (the car takes the larger of handbrake and hydraulic torque).
// CarController is the only caller, and only while CarParams.experimentalBrakesEnabled is true; the default fixed-torque path is untouched.
using System;

public class ExperimentalBrakes : IBrakeSystem
{
    readonly BrakeSystemParameters parameters; readonly VacuumBoosterModel booster; readonly BrakePedalModel pedal;
    readonly HydraulicBrakeCircuit circuitA, circuitB;   // diagonal split: A feeds front left and rear right, B feeds front right and rear left
    readonly DiscBrakeModel discBrake; readonly DrumBrakeModel drumBrake;
    bool started; BrakeStepResult lastResult;

    // @description - builds the brake system; the pedal travel at axis 1 is derived from the legacy full-pedal front torque
    // @param {CarParams} carParams - car parameters (air state and the legacy full-pedal front torque)
    // @param {BrakeSystemParameters} parameters - brake parameters (null = load the asset or the defaults)
    // @return {void} - constructor
    public ExperimentalBrakes(CarParams carParams, BrakeSystemParameters parameters)
    {
        this.parameters = parameters != null ? parameters : BrakeSystemParameters.LoadOrDefault();
        booster = new VacuumBoosterModel(this.parameters, AirProperties.FromCarParams(carParams));
        circuitA = new HydraulicBrakeCircuit(this.parameters); circuitB = new HydraulicBrakeCircuit(this.parameters);
        discBrake = new DiscBrakeModel(this.parameters); drumBrake = new DrumBrakeModel(this.parameters);
        double fullPedalMasterPressure = discBrake.PressureForTorque(carParams.brakeTorqueFront);
        pedal = new BrakePedalModel(this.parameters, circuitA.PedalTravelAtPressure(fullPedalMasterPressure));
    }

    public VacuumBoosterModel Booster => booster;
    public BrakePedalModel Pedal => pedal;
    public DiscBrakeModel Disc => discBrake;
    public DrumBrakeModel Drum => drumBrake;
    public HydraulicBrakeCircuit CircuitA => circuitA;
    public BrakeSystemParameters Parameters => parameters;
    public BrakeStepResult LastResult => lastResult;

    // @description - forgets all state; the next step starts the booster from the manifold vacuum of that moment
    // @return {Void} - nothing
    public void Reset() { started = false; pedal.Reset(); lastResult = default; }

    // @description - one brake step
    // @param {BrakeStepInput} input - pedal and manifold state
    // @param {double} stepSeconds - step length in s
    // @return {BrakeStepResult} - brake torques and the manifold air mass returned
    public BrakeStepResult Step(BrakeStepInput input, double stepSeconds)
    {
        if (!started) { booster.Reset(input.manifoldPressurePascals); started = true; }
        double pedalForce = PedalForceNewtons(input, stepSeconds), rodForce = pedalForce * parameters.pedalRatio;
        double returnedMass = booster.Step(rodForce, input.manifoldPressurePascals, input.manifoldVolumeCubicMeters, input.boosterFault, stepSeconds);
        double masterPressure = circuitA.MasterPressure(rodForce + booster.AssistForceNewtons), rearPressure = circuitA.RearPressure(masterPressure);
        lastResult = new BrakeStepResult
        {
            frontLeftTorqueNewtonMeters = discBrake.Torque(circuitA.FrontPressure(masterPressure)), frontRightTorqueNewtonMeters = discBrake.Torque(circuitB.FrontPressure(masterPressure)),
            rearLeftTorqueNewtonMeters = drumBrake.Torque(circuitB.RearPressure(masterPressure)), rearRightTorqueNewtonMeters = drumBrake.Torque(circuitA.RearPressure(masterPressure)),
            manifoldMassReturnedKilograms = returnedMass, pedalForceNewtons = pedalForce, masterPressurePascals = masterPressure, boosterAssistForceNewtons = booster.AssistForceNewtons
        };
        return lastResult;
    }

    // @description - pedal pad force for this step: measured force for a load-cell pedal, otherwise the force the car needs for the rate-limited foot travel (booster static characteristic at the present vacuum)
    // @param {BrakeStepInput} input - pedal state
    // @param {double} stepSeconds - step length in s
    // @return {double} - pedal pad force in N
    double PedalForceNewtons(BrakeStepInput input, double stepSeconds)
    {
        if (input.loadCellPedal) { pedal.FollowAxis(input.pedalAxis); return BrakePedalModel.LoadCellForceNewtons(input.pedalAxis, input.loadCellMaxForceNewtons); }
        return PedalForceForTravelNewtons(pedal.AdvanceFootTravelMeters(input.pedalAxis, stepSeconds));
    }

    // @description - steady-state pedal force that holds a pedal pad travel (travel -> master pressure -> output force -> input rod force -> pedal force)
    // @param {double} pedalTravelMeters - pedal pad travel, m
    // @return {double} - pedal pad force in N
    public double PedalForceForTravelNewtons(double pedalTravelMeters)
    {
        double outputForce = circuitA.PressureAtPedalTravel(pedalTravelMeters) * circuitA.MasterArea;
        return booster.RodForceForOutputForce(outputForce, booster.VacuumPressurePascals) / parameters.pedalRatio;
    }
}
