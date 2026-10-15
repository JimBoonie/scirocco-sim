// Orchestrator of the EXPERIMENTAL driveline shuffle model: owns the parameter set, the engine-side torque source and the torsional chain, and replaces the
// default Drivetrain.Clutch + Engine.Step pair while CarParams.experimentalDrivelineEnabled is true. CarController is the only caller. When the toggle is
// off nothing in this class runs, and it reads no state that the default path writes except the shared physical state (engine speed, wheel speeds).
using UnityEngine;

public class ExperimentalDriveline
{
    readonly Engine engine; readonly CarParams carParams; readonly ShuffleDrivelineParameters parameters;
    readonly EngineCrankTorqueSource torqueSource; readonly TorsionalDriveline chain;
    bool initialized;

    public TorsionalDriveline Chain => chain;
    public EngineCrankTorqueSource TorqueSource => torqueSource;
    public ShuffleDrivelineParameters Parameters => parameters;

    // @description - builds the model
    // @param {Engine} engine - engine model
    // @param {CarParams} carParams - car parameters
    // @param {ShuffleDrivelineParameters} parameters - model parameters (null = load the asset or the defaults)
    public ExperimentalDriveline(Engine engine, CarParams carParams, ShuffleDrivelineParameters parameters)
    {
        this.engine = engine; this.carParams = carParams; this.parameters = parameters != null ? parameters : ShuffleDrivelineParameters.LoadOrDefault();
        torqueSource = new EngineCrankTorqueSource(engine, carParams, this.parameters); chain = new TorsionalDriveline(this.parameters, carParams);
    }

    // @description - forgets the chain state; the next Advance restarts it from the measured speeds (called when the toggle goes off)
    // @return {Void}
    public void Deactivate() { initialized = false; }

    // @description - clamp load from the physical pedal curve and the auto-clutch law (the more released-limiting of the two wins)
    // @param {float} manualClutchPedal - clutch pedal 0..1
    // @param {float} autoClutchPedal - existing auto-clutch law output 0 (engaged) .. 1 (open), unchanged launch law
    // @return {float} - clamp load fraction 0..1
    public float ClampFraction(float manualClutchPedal, float autoClutchPedal) => Mathf.Min(ClutchEngagementCurve.ClampFraction(parameters, manualClutchPedal), 1f - Mathf.Clamp01(autoClutchPedal));

    // @description - one outer physics substep of engine + driveline; sets the driven-wheel torques
    // @param {float} substep - substep in s
    // @param {float} throttle - accelerator pedal 0..1
    // @param {float} manualClutchPedal - clutch pedal 0..1
    // @param {float} autoClutchPedal - auto-clutch law output 0..1
    // @param {Drivetrain} drivetrain - gearbox state (gear, shift timer); clutchSlip is written for the HUD
    // @param {WheelCorner} leftWheel - left driven wheel (driveTorque is written)
    // @param {WheelCorner} rightWheel - right driven wheel (driveTorque is written)
    // @return {DrivelineStepResult} - chain result
    public DrivelineStepResult Advance(float substep, float throttle, float manualClutchPedal, float autoClutchPedal, Drivetrain drivetrain, WheelCorner leftWheel, WheelCorner rightWheel)
    {
        double meanWheelSpeed = 0.5 * (leftWheel.angularVelocity + rightWheel.angularVelocity);
        if (!initialized) { chain.Initialize(engine.Omega, meanWheelSpeed); initialized = true; }
        torqueSource.BeginInterval(throttle, substep);
        bool gearEngaged = drivetrain.gear != 0 && drivetrain.shiftTimer <= 0f;
        var input = new DrivelineStepInput
        {
            crankshaftTorqueSource = torqueSource, clutchClampFraction = ClampFraction(manualClutchPedal, autoClutchPedal), totalRatioSigned = gearEngaged ? drivetrain.TotalRatio : 0.0,
            wheelInertiaKilogramMetersSquared = carParams.wheelInertia + leftWheel.DriveInertia, leftWheelAngularVelocity = leftWheel.angularVelocity, rightWheelAngularVelocity = rightWheel.angularVelocity,
            leftTireResistingTorque = leftWheel.force.x * carParams.wheelRadius, rightTireResistingTorque = rightWheel.force.x * carParams.wheelRadius, drivetrainEfficiency = carParams.drivetrainEfficiency
        };
        DrivelineStepResult result = chain.Advance(input, substep);
        leftWheel.driveTorque = (float)result.leftWheelAverageTorque; rightWheel.driveTorque = (float)result.rightWheelAverageTorque;
        drivetrain.clutchSlip = (float)result.clutchSlipAngularVelocity;
        return result;
    }
}
