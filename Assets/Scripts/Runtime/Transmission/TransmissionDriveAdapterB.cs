// Adapter B: the experimental transmission inside the torsional chain of the experimental driveline (both toggles on). The chain integrates the input-shaft node and the carrier;
// the transmission supplies the kinematic tie (locked total ratio, 0 = free), the external torques on both ports, the mesh efficiency and the lash (the only owner of gear lash).
// The chain is built from a private copy of the driveline parameters whose input-shaft inertia is the transmission's (one owner per inertia).
using UnityEngine;

public class TransmissionDriveAdapterB
{
    readonly Engine engine; readonly CarParams carParams; readonly TransmissionModel model; readonly ExperimentalDriveline driveline; readonly double nodeInertia;
    bool started; double gearTrainTorque;
    public ExperimentalDriveline Driveline => driveline;
    public double NodeInertia => nodeInertia;

    // @description - builds the adapter and its private driveline
    // @param {Engine} engine - engine model
    // @param {CarParams} carParams - car parameters
    // @param {TransmissionModel} model - transmission model
    // @param {TransmissionParameters} transmissionParameters - supplies the input-shaft assembly inertia
    public TransmissionDriveAdapterB(Engine engine, CarParams carParams, TransmissionModel model, TransmissionParameters transmissionParameters)
    {
        this.engine = engine; this.carParams = carParams; this.model = model;
        ShuffleDrivelineParameters chainParameters = ShuffleDrivelineParameters.LoadOrDefault();
        chainParameters.inputShaftInertiaKilogramMetersSquared = transmissionParameters.inputShaftAssemblyInertiaKilogramMetersSquared;
        nodeInertia = chainParameters.clutchDiscInertiaKilogramMetersSquared + chainParameters.inputShaftInertiaKilogramMetersSquared;
        driveline = new ExperimentalDriveline(engine, carParams, chainParameters);
    }

    // @description - forgets the chain state; the next step restarts it from the measured speeds
    // @return {Void}
    public void Restart() { started = false; driveline.Deactivate(); }

    // @description - one outer physics substep: transmission step, then the chain step with the transmission's tie and torques
    // @param {float} substep - s
    // @param {float} throttle - 0..1
    // @param {float} manualClutchPedal - 0..1
    // @param {float} assistClutchPedal - keyboard auto-clutch output 0..1 (after the transmission assist)
    // @param {Drivetrain} drivetrain - HUD slip written
    // @param {WheelCorner} leftWheel - driven wheel
    // @param {WheelCorner} rightWheel - driven wheel
    // @return {Void}
    public void Advance(float substep, float throttle, float manualClutchPedal, float assistClutchPedal, Drivetrain drivetrain, WheelCorner leftWheel, WheelCorner rightWheel)
    {
        TorsionalDriveline chain = driveline.Chain; double meanWheelSpeed = 0.5 * (leftWheel.angularVelocity + rightWheel.angularVelocity);
        if (!started) { chain.Initialize(engine.Omega, meanWheelSpeed); started = true; gearTrainTorque = 0.0; }
        var input = new TransmissionStepInput { inputShaftAngularVelocity = chain.InputShaftAngularVelocity, carrierAngularVelocity = chain.CarrierAngularVelocity, gearTrainInputTorque = gearTrainTorque, inputNodeInertiaKilogramMetersSquared = nodeInertia, ambientTemperatureKelvin = carParams.ambientTemperature, dt = substep };
        TransmissionStepResult result = model.Advance(input);
        driveline.TorqueSource.BeginInterval(throttle, substep);
        var chainInput = new DrivelineStepInput
        {
            crankshaftTorqueSource = driveline.TorqueSource, clutchClampFraction = driveline.ClampFraction(manualClutchPedal, assistClutchPedal), totalRatioSigned = result.lockedTotalRatioSigned,
            wheelInertiaKilogramMetersSquared = carParams.wheelInertia + leftWheel.DriveInertia, leftWheelAngularVelocity = leftWheel.angularVelocity, rightWheelAngularVelocity = rightWheel.angularVelocity,
            leftTireResistingTorque = leftWheel.force.x * carParams.wheelRadius, rightTireResistingTorque = rightWheel.force.x * carParams.wheelRadius, drivetrainEfficiency = result.meshEfficiency,
            inputShaftExternalTorque = result.inputShaftExternalTorque, carrierExternalTorque = result.carrierExternalTorque, overrideMeshLash = result.lashOwnedByTransmission, meshHalfLashAtInputShaftRadians = result.halfLashAtInputShaftRadians
        };
        DrivelineStepResult chainResult = chain.Advance(chainInput, substep);
        leftWheel.driveTorque = (float)chainResult.leftWheelAverageTorque; rightWheel.driveTorque = (float)chainResult.rightWheelAverageTorque;
        drivetrain.clutchSlip = (float)chainResult.clutchSlipAngularVelocity;
        gearTrainTorque = chainResult.clutchFrictionTorque + result.inputShaftExternalTorque;
    }
}
