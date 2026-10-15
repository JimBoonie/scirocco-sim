using UnityEngine;
[RequireComponent(typeof(Rigidbody))]
public class CarController : MonoBehaviour
{
    public CarParams carParams;
    public WheelCorner frontLeftWheel;
    public WheelCorner frontRightWheel;
    public WheelCorner rearLeftWheel;
    public WheelCorner rearRightWheel;
    ExperimentalTransmission experimentalTransmission;   // created on first use of the experimental transmission toggle; null while it was never on
    ExperimentalDriveline experimentalDriveline;   // created on first use of the experimental driveline toggle; null while it was never on
    [Tooltip("Tire/drivetrain substeps per FixedUpdate (FixedUpdate itself runs at 500 Hz)")] public int substeps = 2;
    public Engine engine; public Drivetrain drivetrain; Rigidbody body;
    [HideInInspector] public float sprungMass; 
    [HideInInspector] public CarParams sourceParams;
    [HideInInspector] public float steeringCompliance;
    [HideInInspector] public float leftRoadTorque;
    [HideInInspector] public float rightRoadTorque;
    [HideInInspector] public float columnTorque;
    SteeringTorque.Geo steerGeo; public float throttle, brake, clutch, steer; public bool handbrake, externalInput; public bool autoClutch; [HideInInspector] public float autoClutchPedal; [HideInInspector] public float handbrakeInput, handwheelDeg; [HideInInspector] public bool handwheelInput; public InputRouter router;
    WheelCorner[] allWheels;
    // @description - Tests whether a command-line flag is present.
    // @param {string} flagName - flag text, e.g. -thermal
    // @return {bool} - true if the flag was passed
    static bool Arg(string flagName) => System.Array.IndexOf(System.Environment.GetCommandLineArgs(), flagName) >= 0;
    // -thermal forces the tire thermal model on (tests); -tirewarm starts tread at the grip optimum, carcass/gas/rim at TireWarmCarcass
    public static readonly bool ThermalForced = Arg("-thermal"), TireWarmStart = Arg("-tirewarm");
    public const double TireWarmCarcass = 40.0;   // degC carcass/gas/rim for -tirewarm (D: highway steady state from the thermal tests)
    // @description - Runtime setup: fixes the physics step to 1 kHz, takes a runtime copy of the parameters, configures the Rigidbody and builds the engine and drivetrain.
    // @return {Void} - nothing
    void Start()
    {
        if (Time.fixedDeltaTime > 0.00101f) Time.fixedDeltaTime = 0.001f;     // 1 kHz suspension/body, 2 kHz tire/drivetrain
        sourceParams = carParams; carParams = sourceParams.RuntimeCopy();   // runtime copy (tire preset); asset untouched. Single handling baseline: no profile switch
        body = GetComponent<Rigidbody>(); body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;   // chassis box vs thin track MeshColliders at speed: no tunneling
        ApplyParams();
        engine = new Engine(carParams); drivetrain = new Drivetrain(carParams);
    }
    // @description - Applies CarParams to the Rigidbody (sprung mass, center of mass shifted for unsprung masses, inertia) and to each wheel (static sag, half track, unsprung load).
    // @return {Void} - nothing
    void ApplyParams()
    {
        float groundY = -(carParams.hubDropDesign + carParams.wheelRadius);                     // ground in local space at design ride height
        // unsprung masses: Rigidbody carries the sprung mass; its CG is shifted so the total-vehicle CG (cgHeight, cgFromFront) is unchanged
        float frontUnsprungMass = carParams.useUnsprungMass ? carParams.frontUnsprungMass : 0f, rearUnsprungMass = carParams.useUnsprungMass ? carParams.rearUnsprungMass : 0f, sprungMassKg = carParams.mass - 2f * (frontUnsprungMass + rearUnsprungMass);
        float sprungCgHeight = (carParams.mass * carParams.cgHeight - 2f * (frontUnsprungMass + rearUnsprungMass) * carParams.wheelRadius) / sprungMassKg, sprungCgFromFront = (carParams.mass * carParams.cgFromFront - 2f * rearUnsprungMass * carParams.wheelbase) / sprungMassKg;
        body.mass = sprungMassKg; sprungMass = sprungMassKg;
        body.centerOfMass = new Vector3(0f, groundY + sprungCgHeight, carParams.wheelbase * 0.5f - sprungCgFromFront);
        body.inertiaTensor = carParams.inertiaTensor; body.inertiaTensorRotation = Quaternion.identity;
        body.maxAngularVelocity = 50f;
        allWheels = new[] { frontLeftWheel, frontRightWheel, rearLeftWheel, rearRightWheel }; foreach (var wheel in allWheels) wheel.carParams = carParams;
        steerGeo = SteeringTorque.FromTables(carParams.suspensionTables);
        float gravity = 9.81f, frontCornerLoad = sprungMassKg * gravity * (carParams.wheelbase - sprungCgFromFront) / carParams.wheelbase * 0.5f, rearCornerLoad = sprungMassKg * gravity * sprungCgFromFront / carParams.wheelbase * 0.5f;   // sprung corner loads
        foreach (var wheel in allWheels)
        {
            wheel.staticSag = (wheel.front ? frontCornerLoad : rearCornerLoad) / wheel.WheelRate; wheel.halfTrack = 0.5f * (wheel.front ? carParams.trackFront : carParams.trackRear);
            wheel.unsprungStaticLoad = (wheel.front ? frontCornerLoad + frontUnsprungMass * gravity : rearCornerLoad + rearUnsprungMass * gravity);
        }
    }
    // @description - Per-frame legacy-input driving controls (used only when no InputRouter is active): pedals, clutch, handbrake, steering command, shifts and reset.
    // @return {Void} - nothing
    void Update()
    {
        if (externalInput || (router && router.isActiveAndEnabled)) return;   // InputRouter (Input System, allWheels devices) takes over when present
        // Legacy Input Manager: "Vertical"/"Horizontal" map to WASD/arrows and gamepad left stick by default.
        float verticalAxis = Input.GetAxis("Vertical");
        float throttleAxis = Axis("Throttle"), brakeAxis = Axis("Brake");   // optional axes (see README); 0 if absent
        throttle = Mathf.Clamp01(Mathf.Max(verticalAxis, throttleAxis)); brake = Mathf.Clamp01(Mathf.Max(-verticalAxis, brakeAxis));
        clutch = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.JoystickButton4) ? 1f : 0f;
        handbrake = Input.GetKey(KeyCode.Space) || Input.GetKey(KeyCode.JoystickButton1);
        float targetRoadAngleDeg = Input.GetAxis("Horizontal") * carParams.MeanLockDeg;
        steer = Mathf.MoveTowards(steer, targetRoadAngleDeg, carParams.steeringRateDegPerSecond * Time.deltaTime); handwheelInput = false;   // road-angle command -> FixedUpdate inverts the linkage
        if (Input.GetKeyDown(KeyCode.E) || Input.GetKeyDown(KeyCode.JoystickButton5)) drivetrain.Shift(1);
        if (Input.GetKeyDown(KeyCode.Q) || Input.GetKeyDown(KeyCode.JoystickButton2)) drivetrain.Shift(-1);
        if (Input.GetKeyDown(KeyCode.R) || Input.GetKeyDown(KeyCode.JoystickButton6))
            ResetCar();
    }
    public bool ClutchFullyIn => carParams.clutchMaxTorque * Mathf.Clamp01(1f - clutch) < engine.FrictionTorque(0f);   // clutch can no longer transmit the engine's own drag
    // @description - Lifts and levels the car, zeroes its velocity and wheel state, and raises the Teleported event.
    // @return {Void} - nothing
    public void ResetCar()
    { transform.position += Vector3.up; transform.rotation = Quaternion.Euler(0f, transform.eulerAngles.y, 0f); body.velocity = Vector3.zero; body.angularVelocity = Vector3.zero; foreach (var wheel in new[] { frontLeftWheel, frontRightWheel, rearLeftWheel, rearRightWheel }) if (wheel) wheel.ResetState(); Teleported?.Invoke(this); }
    /// raised after any reset / teleport (R, reset to start, fall-safety respawn): cameras snap, look offsets cleared
    float cachedPeakTorqueRpm = -1f;
    float PeakTorqueRpm { get { if (cachedPeakTorqueRpm < 0f) { int peakIndex = 0; for (int i = 1; i < carParams.torquePoints.Length; i++) if (carParams.torquePoints[i] > carParams.torquePoints[peakIndex]) peakIndex = i; cachedPeakTorqueRpm = carParams.rpmPoints[peakIndex]; } return cachedPeakTorqueRpm; } }
    public static event System.Action<CarController> Teleported; public static void RaiseTeleported(CarController NONE) => Teleported?.Invoke(NONE);
    // @description - Reads an optional legacy input axis, returning 0 when the axis is not defined in the Input Manager.
    // @param {string} axisName - Input Manager axis name
    // @return {float} - axis value, or 0 if absent
    float Axis(string axisName) { try { return Input.GetAxis(axisName); } catch { return 0f; } }
    // @description - Physics step: steering from the linkage, steering torque, camber/toe compliance, suspension forces, then the tire/drivetrain substeps, thermal update and aerodynamic drag.
    // @return {Void} - nothing
    void FixedUpdate()
    {
        float fixedStep = Time.fixedDeltaTime;
        // Steering: handwheel -> constant pinion -> rack -> per-wheel angle from the suspension kinematics at each wheel's travel
        // (SteeringLinkage; Ackermann and bump steer from the tie-rod geometry). handwheelInput: the driver's wheel sets handwheelDeg;
        // otherwise car.steer is a mean road-angle command (test driver / keyboard) and the linkage is inverted at design height.
        var tables = carParams.suspensionTables;
        if (!handwheelInput) handwheelDeg = SteeringLinkage.HandwheelForMean(carParams, steer);
        handwheelDeg = Mathf.Clamp(handwheelDeg, -carParams.handwheelLockDeg, carParams.handwheelLockDeg);
        float rack = SteeringLinkage.Rack(carParams, handwheelDeg);
        SteeringLinkage.WheelAngles(tables, rack, frontLeftWheel.travel, frontRightWheel.travel, out float steerL, out float steerR);
        if (handwheelInput) steer = SteeringLinkage.MeanAngle(carParams, handwheelDeg);   // mean road angle at design height (HUD / logs)
        float frontToeDeg = 0.5f * carParams.frontTotalToeDeg, rearToeDeg = 0.5f * carParams.rearTotalToeDeg;     // per side, + = toe-in
        frontLeftWheel.steerDeg = steerL + frontToeDeg; frontRightWheel.steerDeg = steerR - frontToeDeg;      // toe-in: left points right(+), right points left(-)
        rearLeftWheel.steerDeg = rearToeDeg; rearRightWheel.steerDeg = -rearToeDeg;
        // Steering torque (physical): tire forces/moments about each kingpin axis (SteeringTorque), previous-step forces.
        // Column torque = sum(road-wheel torque x d(road steer)/d(column angle)); steering compliance = road-wheel torque / system stiffness.
        {
            bool leftBraking = frontLeftWheel.brakeTorque > 0f && frontLeftWheel.force.x < 0f, rightBraking = frontRightWheel.brakeTorque > 0f && frontRightWheel.force.x < 0f;
            leftRoadTorque = SteeringTorque.RoadTorque(steerGeo, true, frontLeftWheel.steerDeg, frontLeftWheel.force.x, leftBraking, frontLeftWheel.force.y, frontLeftWheel.verticalLoad, SteeringTorque.PneumaticTrail(carParams, frontLeftWheel.relaxedSlipAngle));
            rightRoadTorque = SteeringTorque.RoadTorque(steerGeo, false, frontRightWheel.steerDeg, frontRightWheel.force.x, rightBraking, frontRightWheel.force.y, frontRightWheel.verticalLoad, SteeringTorque.PneumaticTrail(carParams, frontRightWheel.relaxedSlipAngle));
            columnTorque = leftRoadTorque * SteeringTorque.Lever(carParams, true, rack, frontLeftWheel.travel) + rightRoadTorque * SteeringTorque.Lever(carParams, false, rack, frontRightWheel.travel);
            steeringCompliance = carParams.steeringStiffness > 0f ? (leftRoadTorque + rightRoadTorque) / carParams.steeringStiffness * Mathf.Rad2Deg : 0f;
            frontLeftWheel.steerDeg += steeringCompliance; frontRightWheel.steerDeg += steeringCompliance;
        }
        // camber to ground = static + kinematic change vs body + body roll (roll>0 = rolls left; outer/left wheel top-out)
        float roll = Vector3.SignedAngle(Vector3.ProjectOnPlane(Vector3.up, transform.forward), transform.up, transform.forward);
        float leftKinematicCamber = tables ? tables.FrontCamber(frontLeftWheel.travel, frontLeftWheel.steerDeg) : 0f, rightKinematicCamber = tables ? tables.FrontCamber(frontRightWheel.travel, -frontRightWheel.steerDeg) : 0f;
        frontLeftWheel.camberDeg = carParams.frontStaticCamberDeg + leftKinematicCamber + roll; frontRightWheel.camberDeg = carParams.frontStaticCamberDeg + rightKinematicCamber - roll;
        if (tables && carParams.frontModel == CarParams.FrontModel.CompliantMultibody)
        {   // bush compliance (multibody_compliance.py, kinematic part removed) from previous-step tire forces + geometric anti-dive/anti-squat
            float leftOutwardLateralForce = -frontLeftWheel.force.y, rightOutwardLateralForce = frontRightWheel.force.y;                     // outward lateral force
            bool leftBraking = frontLeftWheel.brakeTorque > 0f && frontLeftWheel.force.x < 0f, rightBraking = frontRightWheel.brakeTorque > 0f && frontRightWheel.force.x < 0f;
            float leftToeChange = tables.frontToePerLateralForce * leftOutwardLateralForce + (leftBraking ? tables.frontToePerBrakeForce : tables.frontToePerDriveForce) * frontLeftWheel.force.x, rightToeChange = tables.frontToePerLateralForce * rightOutwardLateralForce + (rightBraking ? tables.frontToePerBrakeForce : tables.frontToePerDriveForce) * frontRightWheel.force.x;
            float leftBushCamberChange = tables.frontCamberPerLateralForce * leftOutwardLateralForce + (leftBraking ? tables.frontCamberPerBrakeForce : tables.frontCamberPerDriveForce) * frontLeftWheel.force.x, rightBushCamberChange = tables.frontCamberPerLateralForce * rightOutwardLateralForce + (rightBraking ? tables.frontCamberPerBrakeForce : tables.frontCamberPerDriveForce) * frontRightWheel.force.x;
            frontLeftWheel.steerDeg += leftToeChange; frontRightWheel.steerDeg -= rightToeChange; frontLeftWheel.camberDeg += leftBushCamberChange; frontRightWheel.camberDeg += rightBushCamberChange;
            frontLeftWheel.antiCoef = leftBraking ? tables.frontAntiDiveCoefficient : tables.frontAntiSquatCoefficient; frontRightWheel.antiCoef = rightBraking ? tables.frontAntiDiveCoefficient : tables.frontAntiSquatCoefficient;
        }
        else frontLeftWheel.antiCoef = frontRightWheel.antiCoef = 0f;
        if (tables && carParams.rearModel == CarParams.RearModel.FullTwistBeam)
        {   // full twist beam: geometry (own/other travel) + bush compliance from outward lateral and +x forces (previous step)
            float leftOutwardLateralForce = -rearLeftWheel.force.y, rightOutwardLateralForce = rearRightWheel.force.y;          // outward lateral force (Unity +y_right)
            float leftBeamCamber = tables.rearBeamCamberPerOwnTravel * rearLeftWheel.travel + tables.rearBeamCamberPerOtherTravel * rearRightWheel.travel + tables.rearBeamCamberPerOwnLateralForce * leftOutwardLateralForce + tables.rearBeamCamberPerOtherLateralForce * rightOutwardLateralForce;
            float rightBeamCamber = tables.rearBeamCamberPerOwnTravel * rearRightWheel.travel + tables.rearBeamCamberPerOtherTravel * rearLeftWheel.travel + tables.rearBeamCamberPerOwnLateralForce * rightOutwardLateralForce + tables.rearBeamCamberPerOtherLateralForce * leftOutwardLateralForce;
            float leftToeChange = tables.rearBeamToePerOwnTravel * rearLeftWheel.travel + tables.rearBeamToePerOtherTravel * rearRightWheel.travel + tables.rearBeamToePerOwnLateralForce * leftOutwardLateralForce + tables.rearBeamToePerOtherLateralForce * rightOutwardLateralForce + tables.rearBeamToePerOwnLongitudinalForce * rearLeftWheel.force.x + tables.rearBeamToePerOtherLongitudinalForce * rearRightWheel.force.x;
            float rightToeChange = tables.rearBeamToePerOwnTravel * rearRightWheel.travel + tables.rearBeamToePerOtherTravel * rearLeftWheel.travel + tables.rearBeamToePerOwnLateralForce * rightOutwardLateralForce + tables.rearBeamToePerOtherLateralForce * leftOutwardLateralForce + tables.rearBeamToePerOwnLongitudinalForce * rearRightWheel.force.x + tables.rearBeamToePerOtherLongitudinalForce * rearLeftWheel.force.x;
            rearLeftWheel.camberDeg = carParams.rearStaticCamberDeg + leftBeamCamber + roll; rearRightWheel.camberDeg = carParams.rearStaticCamberDeg + rightBeamCamber - roll;
            rearLeftWheel.steerDeg = rearToeDeg + leftToeChange; rearRightWheel.steerDeg = -(rearToeDeg + rightToeChange);
            rearLeftWheel.antiCoef = rearRightWheel.antiCoef = tables.rearBeamAntiLiftCoefficient;
        }
        else
        {
            float beam = tables ? -tables.rearCamberRollFraction * Mathf.Rad2Deg * (rearLeftWheel.travel - rearRightWheel.travel) / carParams.trackRear : 0f;   // beam twist counter-tilt
            rearLeftWheel.camberDeg = carParams.rearStaticCamberDeg + beam + roll; rearRightWheel.camberDeg = carParams.rearStaticCamberDeg + beam - roll;
            rearLeftWheel.antiCoef = rearRightWheel.antiCoef = 0f;
        }
        // ARB / twist beam: k * (travel difference)
        float frontAntiRollForce = (frontLeftWheel.travel - frontRightWheel.travel) * carParams.frontAntiRollBarRate, rearBeamRollForce = (rearLeftWheel.travel - rearRightWheel.travel) * carParams.twistBeamRollRate;
        frontLeftWheel.UpdateSuspension(fixedStep, frontAntiRollForce); frontRightWheel.UpdateSuspension(fixedStep, -frontAntiRollForce); rearLeftWheel.UpdateSuspension(fixedStep, rearBeamRollForce); rearRightWheel.UpdateSuspension(fixedStep, -rearBeamRollForce);
        float substep = fixedStep / substeps, substepForceScale = 1f / substeps;
        foreach (var wheel in allWheels) wheel.longitudinalRelaxationEnabled = carParams.experimentalDrivelineEnabled;   // experimental driveline: longitudinal slip relaxation
        for (int i = 0; i < substeps; i++)
        {
            drivetrain.Tick(substep);
            // auto clutch (option, for keyboard / no clutch pedal): disengages as the engine falls from idle towards its stall speed
            autoClutchPedal = autoClutch ? Mathf.Clamp01((carParams.idleRpm - engine.rpm) / (carParams.idleRpm - carParams.stallRpm)) : 0f;
            // launch metering (2026-10-13, needed once the tires have real static grip): while the gearbox input shaft turns slower than idle the
            // clutch must slip; its capacity is metered by engine speed above idle, full at the WOT peak-torque rpm (centrifugal / clutch-by-wire
            // behavior), and closes as the input shaft approaches idle. Input >= idle: the law above, unchanged (locked unless rpm < idle).
            if (autoClutch && drivetrain.gear != 0)
            {
                float gearboxInputRpm = Mathf.Abs((frontLeftWheel.angularVelocity + frontRightWheel.angularVelocity) * 0.5f * drivetrain.TotalRatio) * 30f / Mathf.PI;
                float launchMeter = Mathf.Clamp01((engine.rpm - carParams.idleRpm) / (PeakTorqueRpm - carParams.idleRpm)), inputShaftMeter = Mathf.Clamp01(gearboxInputRpm / carParams.idleRpm);
                autoClutchPedal = 1f - Mathf.Min(Mathf.Max(launchMeter, inputShaftMeter), 1f - autoClutchPedal);
            }
            // ...except for a bump start: engine not firing, ignition on, in gear, and the wheels would spin the crank past its firing speed -> engage
            if (autoClutch && !engine.combusting && engine.ignition && !engine.starter && drivetrain.gear != 0 && Mathf.Abs((frontLeftWheel.angularVelocity + frontRightWheel.angularVelocity) * 0.5f * drivetrain.TotalRatio) * 30f / Mathf.PI > engine.FireRpm * 1.5f) autoClutchPedal = 0f;
            if (!carParams.experimentalTransmissionEnabled && experimentalTransmission != null) experimentalTransmission.Deactivate(frontLeftWheel, frontRightWheel);
            if (carParams.experimentalTransmissionEnabled)
            {   // EXPERIMENTAL transmission and synchronizer model (Transmission/*.cs): runs on the simple drivetrain (adapter A) or inside the torsional chain when the driveline toggle is also on (adapter B)
                if (experimentalTransmission == null) experimentalTransmission = new ExperimentalTransmission(engine, carParams, null);
                if (experimentalDriveline != null) experimentalDriveline.Deactivate();
                experimentalTransmission.Advance(substep, throttle, clutch, autoClutch, autoClutchPedal, drivetrain, frontLeftWheel, frontRightWheel);
            }
            else if (carParams.experimentalDrivelineEnabled)
            {   // EXPERIMENTAL driveline shuffle model (Driveline/*.cs): torsional chain with Karnopp clutch, damper, lash, halfshafts, engine roll, firing pulses
                if (experimentalDriveline == null) experimentalDriveline = new ExperimentalDriveline(engine, carParams, null);
                experimentalDriveline.Advance(substep, throttle, clutch, autoClutchPedal, drivetrain, frontLeftWheel, frontRightWheel);
            }
            else
            {
                if (experimentalDriveline != null) experimentalDriveline.Deactivate();
                float clutchTorque = drivetrain.Clutch(engine.Omega, (frontLeftWheel.angularVelocity + frontRightWheel.angularVelocity) * 0.5f, Mathf.Max(clutch, autoClutchPedal), substep, carParams.wheelInertia, out float wheelTorque);
                engine.Step(throttle, clutchTorque, substep);
                frontLeftWheel.driveTorque = frontRightWheel.driveTorque = wheelTorque;
            }
            rearLeftWheel.driveTorque = rearRightWheel.driveTorque = 0f;
            frontLeftWheel.brakeTorque = frontRightWheel.brakeTorque = brake * carParams.brakeTorqueFront;
            float handbrakeFraction = Mathf.Max(handbrake ? 1f : 0f, handbrakeInput); rearLeftWheel.brakeTorque = rearRightWheel.brakeTorque = Mathf.Max(handbrakeFraction * carParams.handbrakeTorque, brake * carParams.brakeTorqueRear);   // analog handbrake lever supported
            foreach (var wheel in allWheels) wheel.TireStep(substep, substepForceScale);
        }
        if (carParams.tireThermal || ThermalForced)
        {   // tire thermal model (option): 1 kHz node update from the substep-averaged heat inputs, then new Pacejka multipliers
            foreach (var wheel in allWheels) wheel.ThermalStep(wheel == frontLeftWheel || wheel == frontRightWheel ? carParams.frontColdPressure : carParams.rearColdPressure, TireWarmStart, TireWarmCarcass);
        }
        else foreach (var wheel in allWheels) if (wheel.thermal != null) wheel.ThermalOff();
        Vector3 bodyVelocity = body.velocity;
        body.AddForce(-bodyVelocity * (0.5f * carParams.airDensity * carParams.dragCoefficient * carParams.frontalArea * bodyVelocity.magnitude));
    }
}
