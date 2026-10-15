using UnityEngine;
[RequireComponent(typeof(Rigidbody))]
public class CarController : MonoBehaviour
{
    public CarParams p; public WheelCorner FL, FR, RL, RR;
    [Tooltip("Tire/drivetrain substeps per FixedUpdate (FixedUpdate itself runs at 500 Hz)")] public int substeps = 2;
    public Engine engine; public Drivetrain dt; Rigidbody rb;
    [HideInInspector] public float sprungMass; [HideInInspector] public CarParams source; [HideInInspector] public float steerCompl, roadTorqueL, roadTorqueR, columnTorque; SteeringTorque.Geo steerGeo; public float throttle, brake, clutch, steer; public bool handbrake, externalInput; public bool autoClutch; [HideInInspector] public float autoClutchPedal; [HideInInspector] public float handbrakeInput, handwheelDeg; [HideInInspector] public bool handwheelInput; public InputRouter router;
    WheelCorner[] all;
    static bool Arg(string n) => System.Array.IndexOf(System.Environment.GetCommandLineArgs(), n) >= 0;
    // -thermal forces the tire thermal model on (tests); -tirewarm starts tread at the grip optimum, carcass/gas/rim at TireWarmCarcass
    public static readonly bool ThermalForced = Arg("-thermal"), TireWarmStart = Arg("-tirewarm");
    public const double TireWarmCarcass = 40.0;   // degC carcass/gas/rim for -tirewarm (D: highway steady state from the thermal tests)
    void Start()
    {
        if (Time.fixedDeltaTime > 0.00101f) Time.fixedDeltaTime = 0.001f;     // 1 kHz suspension/body, 2 kHz tire/drivetrain
        source = p; p = source.RuntimeCopy();   // runtime copy (tire preset); asset untouched. Single handling baseline: no profile switch
        rb = GetComponent<Rigidbody>(); rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;   // chassis box vs thin track MeshColliders at speed: no tunnelling
        ApplyParams();
        engine = new Engine(p); dt = new Drivetrain(p);
    }
    void ApplyParams()
    {
        float groundY = -(p.hubDropDesign + p.wheelRadius);                     // ground in local space at design ride height
        // unsprung masses: Rigidbody carries the sprung mass; its CG is shifted so the total-vehicle CG (cgHeight, cgFromFront) is unchanged
        float muF = p.useUnsprungMass ? p.unsprungFront : 0f, muR = p.useUnsprungMass ? p.unsprungRear : 0f, ms = p.mass - 2f * (muF + muR);
        float hS = (p.mass * p.cgHeight - 2f * (muF + muR) * p.wheelRadius) / ms, aS = (p.mass * p.cgFromFront - 2f * muR * p.wheelbase) / ms;
        rb.mass = ms; sprungMass = ms;
        rb.centerOfMass = new Vector3(0f, groundY + hS, p.wheelbase * 0.5f - aS);
        rb.inertiaTensor = p.inertiaTensor; rb.inertiaTensorRotation = Quaternion.identity;
        rb.maxAngularVelocity = 50f;
        all = new[] { FL, FR, RL, RR }; foreach (var w in all) w.p = p;
        steerGeo = SteeringTorque.FromTables(p.kin);
        float g = 9.81f, wf = ms * g * (p.wheelbase - aS) / p.wheelbase * 0.5f, wr = ms * g * aS / p.wheelbase * 0.5f;   // sprung corner loads
        foreach (var w in all)
        {
            w.staticSag = (w.front ? wf : wr) / w.WheelRate; w.halfTrack = 0.5f * (w.front ? p.trackFront : p.trackRear);
            w.unsprungStaticLoad = (w.front ? wf + muF * g : wr + muR * g);
        }
    }
    void Update()
    {
        if (externalInput || (router && router.isActiveAndEnabled)) return;   // InputRouter (Input System, all devices) takes over when present
        // Legacy Input Manager: "Vertical"/"Horizontal" map to WASD/arrows and gamepad left stick by default.
        float v = Input.GetAxis("Vertical");
        float trig = Axis("Throttle"), brk = Axis("Brake");   // optional axes (see README); 0 if absent
        throttle = Mathf.Clamp01(Mathf.Max(v, trig)); brake = Mathf.Clamp01(Mathf.Max(-v, brk));
        clutch = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.JoystickButton4) ? 1f : 0f;
        handbrake = Input.GetKey(KeyCode.Space) || Input.GetKey(KeyCode.JoystickButton1);
        float target = Input.GetAxis("Horizontal") * p.MeanLockDeg;
        steer = Mathf.MoveTowards(steer, target, p.steerRate * Time.deltaTime); handwheelInput = false;   // road-angle command -> FixedUpdate inverts the linkage
        if (Input.GetKeyDown(KeyCode.E) || Input.GetKeyDown(KeyCode.JoystickButton5)) dt.Shift(1);
        if (Input.GetKeyDown(KeyCode.Q) || Input.GetKeyDown(KeyCode.JoystickButton2)) dt.Shift(-1);
        if (Input.GetKeyDown(KeyCode.R) || Input.GetKeyDown(KeyCode.JoystickButton6))
            ResetCar();
    }
    public bool ClutchFullyIn => p.clutchMaxTorque * Mathf.Clamp01(1f - clutch) < engine.FrictionTorque(0f);   // clutch can no longer transmit the engine's own drag
    public void ResetCar()
    { transform.position += Vector3.up; transform.rotation = Quaternion.Euler(0f, transform.eulerAngles.y, 0f); rb.velocity = Vector3.zero; rb.angularVelocity = Vector3.zero; foreach (var w in new[] { FL, FR, RL, RR }) if (w) w.ResetState(); Teleported?.Invoke(this); }
    /// raised after any reset / teleport (R, reset to start, fall-safety respawn): cameras snap, look offsets cleared
    float peakTqRpm = -1f;
    float PeakTorqueRpm { get { if (peakTqRpm < 0f) { int k = 0; for (int i = 1; i < p.torquePoints.Length; i++) if (p.torquePoints[i] > p.torquePoints[k]) k = i; peakTqRpm = p.rpmPoints[k]; } return peakTqRpm; } }
    public static event System.Action<CarController> Teleported; public static void RaiseTeleported(CarController c) => Teleported?.Invoke(c);
    float Axis(string n) { try { return Input.GetAxis(n); } catch { return 0f; } }
    void FixedUpdate()
    {
        float F = Time.fixedDeltaTime;
        // Steering: handwheel -> constant pinion -> rack -> per-wheel angle from the suspension kinematics at each wheel's travel
        // (SteeringLinkage; Ackermann and bump steer from the tie-rod geometry). handwheelInput: the driver's wheel sets handwheelDeg;
        // otherwise car.steer is a mean road-angle command (test driver / keyboard) and the linkage is inverted at design height.
        var k = p.kin;
        if (!handwheelInput) handwheelDeg = SteeringLinkage.HandwheelForMean(p, steer);
        handwheelDeg = Mathf.Clamp(handwheelDeg, -p.handwheelLockDeg, p.handwheelLockDeg);
        float rack = SteeringLinkage.Rack(p, handwheelDeg);
        SteeringLinkage.WheelAngles(k, rack, FL.travel, FR.travel, out float steerL, out float steerR);
        if (handwheelInput) steer = SteeringLinkage.MeanAngle(p, handwheelDeg);   // mean road angle at design height (HUD / logs)
        float toeF = 0.5f * p.toeTotalF, toeR = 0.5f * p.toeTotalR;     // per side, + = toe-in
        FL.steerDeg = steerL + toeF; FR.steerDeg = steerR - toeF;      // toe-in: left points right(+), right points left(-)
        RL.steerDeg = toeR; RR.steerDeg = -toeR;
        // Steering torque (physical): tire forces/moments about each kingpin axis (SteeringTorque), previous-step forces.
        // Column torque = sum(road-wheel torque x d(road steer)/d(column angle)); steering compliance = road-wheel torque / system stiffness.
        {
            bool bL = FL.brakeTorque > 0f && FL.force.x < 0f, bR = FR.brakeTorque > 0f && FR.force.x < 0f;
            roadTorqueL = SteeringTorque.RoadTorque(steerGeo, true, FL.steerDeg, FL.force.x, bL, FL.force.y, FL.Fz, SteeringTorque.PneumaticTrail(p, FL.alphaRelaxed));
            roadTorqueR = SteeringTorque.RoadTorque(steerGeo, false, FR.steerDeg, FR.force.x, bR, FR.force.y, FR.Fz, SteeringTorque.PneumaticTrail(p, FR.alphaRelaxed));
            columnTorque = roadTorqueL * SteeringTorque.Lever(p, true, rack, FL.travel) + roadTorqueR * SteeringTorque.Lever(p, false, rack, FR.travel);
            steerCompl = p.steeringStiffness > 0f ? (roadTorqueL + roadTorqueR) / p.steeringStiffness * Mathf.Rad2Deg : 0f;
            FL.steerDeg += steerCompl; FR.steerDeg += steerCompl;
        }
        // camber to ground = static + kinematic change vs body + body roll (roll>0 = rolls left; outer/left wheel top-out)
        float roll = Vector3.SignedAngle(Vector3.ProjectOnPlane(Vector3.up, transform.forward), transform.up, transform.forward);
        float cL = k ? k.FrontCamber(FL.travel, FL.steerDeg) : 0f, cR = k ? k.FrontCamber(FR.travel, -FR.steerDeg) : 0f;
        FL.camberDeg = p.staticCamberF + cL + roll; FR.camberDeg = p.staticCamberF + cR - roll;
        if (k && p.frontModel == CarParams.FrontModel.CompliantMultibody)
        {   // bush compliance (multibody_compliance.py, kinematic part removed) from previous-step tire forces + geometric anti-dive/anti-squat
            float foL = -FL.force.y, foR = FR.force.y;                     // outward lateral force
            bool bL = FL.brakeTorque > 0f && FL.force.x < 0f, bR = FR.brakeTorque > 0f && FR.force.x < 0f;
            float tL = k.fcToeFy * foL + (bL ? k.fcToeFx : k.fcToeFxD) * FL.force.x, tR = k.fcToeFy * foR + (bR ? k.fcToeFx : k.fcToeFxD) * FR.force.x;
            float cL1 = k.fcCamFy * foL + (bL ? k.fcCamFx : k.fcCamFxD) * FL.force.x, cR1 = k.fcCamFy * foR + (bR ? k.fcCamFx : k.fcCamFxD) * FR.force.x;
            FL.steerDeg += tL; FR.steerDeg -= tR; FL.camberDeg += cL1; FR.camberDeg += cR1;
            FL.antiCoef = bL ? k.fcAntiBrake : k.fcAntiDrive; FR.antiCoef = bR ? k.fcAntiBrake : k.fcAntiDrive;
        }
        else FL.antiCoef = FR.antiCoef = 0f;
        if (k && p.rearModel == CarParams.RearModel.FullTwistBeam)
        {   // full twist beam: geometry (own/other travel) + bush compliance from outward lateral and +x forces (previous step)
            float foL = -RL.force.y, foR = RR.force.y;          // outward lateral force (Unity +y_right)
            float cL2 = k.rbCamOwn * RL.travel + k.rbCamOther * RR.travel + k.rbCamFyOwn * foL + k.rbCamFyOther * foR;
            float cR2 = k.rbCamOwn * RR.travel + k.rbCamOther * RL.travel + k.rbCamFyOwn * foR + k.rbCamFyOther * foL;
            float tL = k.rbToeOwn * RL.travel + k.rbToeOther * RR.travel + k.rbToeFyOwn * foL + k.rbToeFyOther * foR + k.rbToeFxOwn * RL.force.x + k.rbToeFxOther * RR.force.x;
            float tR = k.rbToeOwn * RR.travel + k.rbToeOther * RL.travel + k.rbToeFyOwn * foR + k.rbToeFyOther * foL + k.rbToeFxOwn * RR.force.x + k.rbToeFxOther * RL.force.x;
            RL.camberDeg = p.staticCamberR + cL2 + roll; RR.camberDeg = p.staticCamberR + cR2 - roll;
            RL.steerDeg = toeR + tL; RR.steerDeg = -(toeR + tR);
            RL.antiCoef = RR.antiCoef = k.rbAntiLift;
        }
        else
        {
            float beam = k ? -k.rearCamberRollFraction * Mathf.Rad2Deg * (RL.travel - RR.travel) / p.trackRear : 0f;   // beam twist counter-tilt
            RL.camberDeg = p.staticCamberR + beam + roll; RR.camberDeg = p.staticCamberR + beam - roll;
            RL.antiCoef = RR.antiCoef = 0f;
        }
        // ARB / twist beam: k * (travel difference)
        float arbF = (FL.travel - FR.travel) * p.arbFront, arbR = (RL.travel - RR.travel) * p.twistBeamRoll;
        FL.UpdateSuspension(F, arbF); FR.UpdateSuspension(F, -arbF); RL.UpdateSuspension(F, arbR); RR.UpdateSuspension(F, -arbR);
        float h = F / substeps, scale = 1f / substeps;
        for (int i = 0; i < substeps; i++)
        {
            dt.Tick(h);
            // auto clutch (option, for keyboard / no clutch pedal): disengages as the engine falls from idle towards its stall speed
            autoClutchPedal = autoClutch ? Mathf.Clamp01((p.idleRpm - engine.rpm) / (p.idleRpm - p.stallRpm)) : 0f;
            // launch metering (2026-10-13, needed once the tires have real static grip): while the gearbox input shaft turns slower than idle the
            // clutch must slip; its capacity is metered by engine speed above idle, full at the WOT peak-torque rpm (centrifugal / clutch-by-wire
            // behaviour), and closes as the input shaft approaches idle. Input >= idle: the law above, unchanged (locked unless rpm < idle).
            if (autoClutch && dt.gear != 0)
            {
                float inRpm = Mathf.Abs((FL.omega + FR.omega) * 0.5f * dt.TotalRatio) * 30f / Mathf.PI;
                float fLaunch = Mathf.Clamp01((engine.rpm - p.idleRpm) / (PeakTorqueRpm - p.idleRpm)), fIn = Mathf.Clamp01(inRpm / p.idleRpm);
                autoClutchPedal = 1f - Mathf.Min(Mathf.Max(fLaunch, fIn), 1f - autoClutchPedal);
            }
            // ...except for a bump start: engine not firing, ignition on, in gear, and the wheels would spin the crank past its firing speed -> engage
            if (autoClutch && !engine.combusting && engine.ignition && !engine.starter && dt.gear != 0 && Mathf.Abs((FL.omega + FR.omega) * 0.5f * dt.TotalRatio) * 30f / Mathf.PI > engine.FireRpm * 1.5f) autoClutchPedal = 0f;
            float tc = dt.Clutch(engine.Omega, (FL.omega + FR.omega) * 0.5f, Mathf.Max(clutch, autoClutchPedal), h, p.wheelInertia, out float wt);
            engine.Step(throttle, tc, h);
            FL.driveTorque = FR.driveTorque = wt; RL.driveTorque = RR.driveTorque = 0f;
            FL.brakeTorque = FR.brakeTorque = brake * p.brakeTorqueFront;
            float hb = Mathf.Max(handbrake ? 1f : 0f, handbrakeInput); RL.brakeTorque = RR.brakeTorque = Mathf.Max(hb * p.handbrakeTorque, brake * p.brakeTorqueRear);   // analog handbrake lever supported
            foreach (var w in all) w.TireStep(h, scale);
        }
        if (p.tireThermal || ThermalForced)
        {   // tire thermal model (option): 1 kHz node update from the substep-averaged heat inputs, then new Pacejka multipliers
            foreach (var w in all) w.ThermalStep(w == FL || w == FR ? p.coldPressureFront : p.coldPressureRear, TireWarmStart, TireWarmCarcass);
        }
        else foreach (var w in all) if (w.thermal != null) w.ThermalOff();
        Vector3 vel = rb.velocity;
        rb.AddForce(-vel * (0.5f * p.airDensity * p.cd * p.frontalArea * vel.magnitude));
    }
}
