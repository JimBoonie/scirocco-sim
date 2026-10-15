// Orchestrator of the EXPERIMENTAL transmission and synchronizer model: owns the parameter set, the transmission model, both drive adapters and the keyboard auto-clutch assist.
// CarController is the only caller. While CarParams.experimentalTransmissionEnabled is false nothing here runs. The adapter follows the driveline toggle:
// driveline off = Adapter A (simple drivetrain), driveline on = Adapter B (torsional chain).
using UnityEngine;

public class ExperimentalTransmission
{
    readonly Engine engine; readonly CarParams carParams; readonly TransmissionParameters parameters; readonly TransmissionModel model;
    readonly ShuffleDrivelineParameters pedalParameters;   // read only: the physical clutch pedal to clamp curve (cable release, diaphragm spring) shared with the driveline model
    readonly TransmissionDriveAdapterA adapterA; readonly TransmissionDriveAdapterB adapterB;
    bool active, chainActive, assistStarted; float assistPedal;
    public TransmissionModel Model => model;
    public TransmissionParameters Parameters => parameters;
    public TransmissionDriveAdapterA AdapterA => adapterA;
    public TransmissionDriveAdapterB AdapterB => adapterB;
    public bool UsesTorsionalChain => chainActive;

    // @description - builds the model, both adapters and the parameter set (null = load the asset or the defaults)
    // @param {Engine} engine - engine model
    // @param {CarParams} carParams - car parameters
    // @param {TransmissionParameters} parameters - model parameters
    public ExperimentalTransmission(Engine engine, CarParams carParams, TransmissionParameters parameters)
    {
        this.engine = engine; this.carParams = carParams; this.parameters = parameters != null ? parameters : TransmissionParameters.LoadOrDefault();
        model = new TransmissionModel(this.parameters, carParams.finalDrive, carParams.ambientTemperature);
        pedalParameters = ShuffleDrivelineParameters.LoadOrDefault();
        double nodeInertia = pedalParameters.clutchDiscInertiaKilogramMetersSquared + this.parameters.inputShaftAssemblyInertiaKilogramMetersSquared;
        adapterA = new TransmissionDriveAdapterA(engine, carParams, model, nodeInertia); adapterB = new TransmissionDriveAdapterB(engine, carParams, model, this.parameters);
    }

    // @description - switches the model off: removes the inertia it put on the wheels and forgets adapter state (called every step while the toggle is off; cheap)
    // @param {WheelCorner} leftWheel - driven wheel
    // @param {WheelCorner} rightWheel - driven wheel
    // @return {Void}
    public void Deactivate(WheelCorner leftWheel, WheelCorner rightWheel)
    {
        if (!active) return;
        active = false; leftWheel.DriveInertia = rightWheel.DriveInertia = 0f; adapterA.Restart(); adapterB.Restart(); assistStarted = false;
    }

    // @description - keyboard auto-clutch assist: the existing law gives the floor; while a shift is in progress the pedal opens at the pedal rate and stays open until the gear is in
    //   (long enough for the synchronizer to finish), then closes at the slower release rate
    // @param {float} lawPedal - existing auto-clutch law output 0..1 (unchanged launch law)
    // @param {float} dt - s
    // @return {float} - assisted pedal 0..1
    public float AssistedAutoClutch(float lawPedal, float dt)
    {
        if (!assistStarted) { assistPedal = lawPedal; assistStarted = true; }
        float target = model.ShiftInProgress ? 1f : lawPedal, rate = target > assistPedal ? 1f / parameters.autoClutchOpenSeconds : 1f / parameters.autoClutchCloseSeconds;
        assistPedal = Mathf.MoveTowards(assistPedal, target, rate * dt);
        return Mathf.Max(assistPedal, lawPedal);
    }

    // @description - one outer physics substep: reads the lever request, applies the assist, and runs the adapter that matches the driveline toggle
    // @param {float} substep - s
    // @param {float} throttle - accelerator 0..1
    // @param {float} manualClutchPedal - clutch pedal 0..1
    // @param {bool} autoClutchEnabled - keyboard auto-clutch option
    // @param {float} autoClutchLawPedal - existing auto-clutch law output 0..1
    // @param {Drivetrain} drivetrain - lever state (gear = the player's request)
    // @param {WheelCorner} leftWheel - driven wheel
    // @param {WheelCorner} rightWheel - driven wheel
    // @return {Void}
    public void Advance(float substep, float throttle, float manualClutchPedal, bool autoClutchEnabled, float autoClutchLawPedal, Drivetrain drivetrain, WheelCorner leftWheel, WheelCorner rightWheel)
    {
        bool wantsChain = carParams.experimentalDrivelineEnabled;
        if (!active || wantsChain != chainActive) { if (wantsChain) adapterB.Restart(); else adapterA.Restart(); }
        active = true; chainActive = wantsChain; model.RequestedGear = drivetrain.gear;
        float assisted = autoClutchEnabled ? AssistedAutoClutch(autoClutchLawPedal, substep) : autoClutchLawPedal;
        if (chainActive) adapterB.Advance(substep, throttle, manualClutchPedal, assisted, drivetrain, leftWheel, rightWheel);
        else adapterA.Advance(substep, throttle, Mathf.Min(ClutchEngagementCurve.ClampFraction(pedalParameters, manualClutchPedal), 1f - Mathf.Clamp01(assisted)), drivetrain, leftWheel, rightWheel);
    }
}
