// Adapter A: the experimental transmission behind the SIMPLE drivetrain (transmission toggle on, driveline toggle off). The input-shaft node (clutch disc plus shaft assembly) is a
// real inertia between the engine and the gearbox; the clutch is the same Coulomb-capped coupling as the default path but retargeted to that node; the output side is the mean driven-wheel speed
// with the equal-split open differential. While the gear is tied (dogs in or cone stuck) the node is rigid with the wheels. Limits (documented): the clutch has no stick state (residual
// slip of a penalty coupling) and there is no lash in gear (the torsional chain owns lash when the driveline toggle is also on).
using UnityEngine;

public class TransmissionDriveAdapterA
{
    const int DrivenWheelCount = 2;
    const float CouplingSlipRemovalPerStep = 0.5f;   // same critically sized fraction as Drivetrain.Clutch
    readonly Engine engine; readonly CarParams carParams; readonly TransmissionModel model; readonly double nodeInertia;
    double nodeSpeed, lockedRatio, gearTrainTorque; bool started;
    public double NodeAngularVelocity => nodeSpeed;
    public double NodeInertia => nodeInertia;
    public double GearTrainTorque => gearTrainTorque;

    // @description - builds the adapter
    // @param {Engine} engine - engine model
    // @param {CarParams} carParams - car parameters
    // @param {TransmissionModel} model - transmission model
    // @param {double} nodeInertiaKilogramMetersSquared - disc plus input shaft assembly in kg m^2
    // @return {void} - constructor
    public TransmissionDriveAdapterA(Engine engine, CarParams carParams, TransmissionModel model, double nodeInertiaKilogramMetersSquared)
    { this.engine = engine; this.carParams = carParams; this.model = model; nodeInertia = nodeInertiaKilogramMetersSquared; }

    // @description - forgets the node state; the next step restarts it from the measured speeds (called when the model is switched on)
    // @return {Void}
    public void Restart() { started = false; }

    // @description - one outer physics substep of engine, clutch, transmission and the wheel drive torques
    // @param {float} substep - s
    // @param {float} throttle - accelerator 0..1
    // @param {float} clutchClampFraction - clamp load fraction 0 (open) .. 1 (fully clamped), from the pedal curve and the auto-clutch law
    // @param {Drivetrain} drivetrain - HUD slip is written here
    // @param {WheelCorner} leftWheel - driven wheel (driveTorque, DriveInertia written)
    // @param {WheelCorner} rightWheel - driven wheel
    // @return {Void}
    public void Advance(float substep, float throttle, float clutchClampFraction, Drivetrain drivetrain, WheelCorner leftWheel, WheelCorner rightWheel)
    {
        double wheelSpeed = 0.5 * (leftWheel.angularVelocity + rightWheel.angularVelocity);
        if (!started) { nodeSpeed = engine.Omega; lockedRatio = 0.0; gearTrainTorque = 0.0; started = true; }
        double inputSpeed = lockedRatio != 0.0 ? lockedRatio * wheelSpeed : nodeSpeed;
        var input = new TransmissionStepInput { inputShaftAngularVelocity = inputSpeed, carrierAngularVelocity = wheelSpeed, gearTrainInputTorque = gearTrainTorque, inputNodeInertiaKilogramMetersSquared = nodeInertia, ambientTemperatureKelvin = carParams.ambientTemperature, dt = substep };
        TransmissionStepResult result = model.Advance(input);
        if (result.lockedTotalRatioSigned != 0.0) StepTied(substep, throttle, clutchClampFraction, wheelSpeed, result, drivetrain, leftWheel, rightWheel);
        else StepFree(substep, throttle, clutchClampFraction, inputSpeed, result, drivetrain, leftWheel, rightWheel);
        lockedRatio = result.lockedTotalRatioSigned;
    }

    // @description - sets the node speed (test and restart helper)
    // @param {double} speedRadiansPerSecond - rad/s
    // @return {Void}
    public void SetNodeSpeed(double speedRadiansPerSecond) { nodeSpeed = speedRadiansPerSecond; started = true; }

    // @description - clutch torque capacity from the clamp fraction
    // @param {float} clutchClampFraction - 0..1
    // @return {float} - N m
    float ClutchCapacity(float clutchClampFraction) => carParams.clutchMaxTorque * Mathf.Clamp01(clutchClampFraction);

    // @description - tied gear: the node is rigid with the wheels; the clutch drags the engine against the wheels and the transmission losses act on the gear train
    // @param {float} substep - s
    // @param {float} throttle - 0..1
    // @param {float} clutchClampFraction - 0..1
    // @param {double} wheelSpeed - mean wheel speed in rad/s
    // @param {TransmissionStepResult} result - transmission result
    // @param {Drivetrain} drivetrain - HUD slip written
    // @param {WheelCorner} leftWheel - wheel
    // @param {WheelCorner} rightWheel - wheel
    // @return {Void}
    void StepTied(float substep, float throttle, float clutchClampFraction, double wheelSpeed, TransmissionStepResult result, Drivetrain drivetrain, WheelCorner leftWheel, WheelCorner rightWheel)
    {
        float ratio = (float)result.lockedTotalRatioSigned, slip = engine.Omega - ratio * (float)wheelSpeed, capacity = ClutchCapacity(clutchClampFraction);
        float efficiency = Mathf.Max((float)result.meshEfficiency, Mathf.Epsilon), wheelInertia = carParams.wheelInertia + 0.5f * (float)nodeInertia * ratio * ratio;
        float torqueScale = slip >= 0f ? efficiency : 1f / efficiency;
        float inverseReflectedInertia = 1f / carParams.engineInertia + ratio * ratio * torqueScale / (DrivenWheelCount * wheelInertia);
        float clutchTorque = Mathf.Clamp(slip * CouplingSlipRemovalPerStep / (substep * inverseReflectedInertia), -capacity, capacity);
        double intoGearTrain = clutchTorque + result.inputShaftExternalTorque, scale = intoGearTrain >= 0.0 ? efficiency : 1.0 / efficiency;
        leftWheel.driveTorque = rightWheel.driveTorque = (float)((intoGearTrain * ratio * scale + result.carrierExternalTorque) / DrivenWheelCount);
        leftWheel.DriveInertia = rightWheel.DriveInertia = 0.5f * (float)nodeInertia * ratio * ratio;
        engine.Step(throttle, clutchTorque, substep);
        gearTrainTorque = intoGearTrain; nodeSpeed = ratio * wheelSpeed; drivetrain.clutchSlip = slip;
    }

    // @description - free node (neutral, traveling, synchronizing): the clutch accelerates the node together with the engine; the cone, drag and rattle act on the node, and the cone reaction reaches the wheels
    // @param {float} substep - s
    // @param {float} throttle - 0..1
    // @param {float} clutchClampFraction - 0..1
    // @param {double} inputSpeed - node speed in rad/s
    // @param {TransmissionStepResult} result - transmission result
    // @param {Drivetrain} drivetrain - HUD slip written
    // @param {WheelCorner} leftWheel - wheel
    // @param {WheelCorner} rightWheel - wheel
    // @return {Void}
    void StepFree(float substep, float throttle, float clutchClampFraction, double inputSpeed, TransmissionStepResult result, Drivetrain drivetrain, WheelCorner leftWheel, WheelCorner rightWheel)
    {
        float slip = engine.Omega - (float)inputSpeed, capacity = ClutchCapacity(clutchClampFraction);
        float couplingGain = CouplingSlipRemovalPerStep / (substep * (1f / carParams.engineInertia + 1f / (float)nodeInertia));
        float clutchTorque = Mathf.Clamp(slip * couplingGain, -capacity, capacity);
        nodeSpeed = inputSpeed + (clutchTorque + result.inputShaftExternalTorque) / nodeInertia * substep;
        leftWheel.driveTorque = rightWheel.driveTorque = (float)(result.carrierExternalTorque / DrivenWheelCount); leftWheel.DriveInertia = rightWheel.DriveInertia = 0f;
        engine.Step(throttle, clutchTorque, substep);
        gearTrainTorque = clutchTorque + result.inputShaftExternalTorque; drivetrain.clutchSlip = slip;
    }
}
