using UnityEngine;
using UnityEngine.Serialization;
// One corner. Raycast wheel position -> wheel travel t (m, +bump, 0 = design ride height).
// Vertical: spring (wheel rate, preloaded to carry static load at t=0) + piecewise damper through the kinematic motion ratio
// + progressive bump stop + ARB/beam (from controller) + jacking force from the roll-centre height.
// Tire: Pacejka combined slip with relaxation length; lateral force applied at roll-centre height (geometric load transfer).
public class WheelCorner : MonoBehaviour
{
    [FormerlySerializedAs("p")] public CarParams carParams;
    public bool front, left; public Transform visual;
    [HideInInspector] public float travel;
    [HideInInspector] [FormerlySerializedAs("Fz")] public float verticalLoad;
    [HideInInspector] [FormerlySerializedAs("Fspring")] public float springForce;
    [HideInInspector] [FormerlySerializedAs("Fdamper")] public float damperForce;
    [HideInInspector] [FormerlySerializedAs("Fbump")] public float bumpStopTotalForce;
    [HideInInspector] [FormerlySerializedAs("Farb")] public float antiRollBarForce;
    [HideInInspector] [FormerlySerializedAs("Fjack")] public float jackingForce;
    [HideInInspector] [FormerlySerializedAs("Fanti")] public float antiPitchForce;
    [HideInInspector] public float steerDeg, driveTorque, brakeTorque, camberDeg;
    [HideInInspector] [FormerlySerializedAs("rcHeight")] public float rollCentreHeight;
    [HideInInspector] [FormerlySerializedAs("omega")] public float angularVelocity;
    [HideInInspector] public float slipRatio, slipAngle;
    [HideInInspector] [FormerlySerializedAs("alphaRelaxed")] public float relaxedSlipAngle;
    [HideInInspector] public float staticSag, halfTrack;
    [HideInInspector] public Vector2 force; [HideInInspector] public bool grounded;
    [HideInInspector] public Vector3 bodyForce;             // total force this corner puts into the body (world)
    float lastTravel, spinDeg; bool haveLastTravel; RaycastHit hit; Rigidbody body;
    public RaycastHit Hit => hit; public float WheelY => wheelCentreHeight;   // diagnostics
    public float DriveInertia;
    // @description - Caches the parent Rigidbody (the car body) that every corner pushes its forces into.
    // @return {Void} - nothing
    void Awake() { body = GetComponentInParent<Rigidbody>(); }
    float BumpTravel => front ? carParams.bumpTravelF : carParams.bumpTravelR;
    float ReboundTravel => front ? carParams.reboundTravelF : carParams.reboundTravelR;
    // @description - Spring/damper motion ratio at a wheel travel: wheel-centre velocity times this equals damper velocity (table from SuspensionTables).
    // @param {float} wheelTravel - wheel travel in m (+ = bump)
    // @return {float} - motion ratio, dimensionless (1 when no kinematics tables are assigned)
    float MotionRatio(float wheelTravel) { var tables = carParams.kin; if (!tables) return 1f; return SuspensionTables.Interpolate1D(tables.travel, front ? tables.frontMotionRatio : tables.rearMotionRatio, wheelTravel); }
    // @description - Roll-centre height above ground for this axle at a wheel travel (front: table; rear: twist-beam value or table).
    // @param {float} wheelTravel - wheel travel in m (+ = bump)
    // @return {float} - roll-centre height in m
    public float RollCentreHeightAt(float wheelTravel) { var tables = carParams.kin; if (!tables) return 0f; if (front) return SuspensionTables.Interpolate1D(tables.travel, tables.frontRcHeight, wheelTravel); return carParams.rearModel == CarParams.RearModel.FullTwistBeam ? tables.rbRcHeight : tables.rearRcHeight; }
    [HideInInspector] public float antiCoef;   // vertical body force per N of +x tire force (set by controller)
    // @description - Bump-stop force from compression, integrating the piecewise-linear stiffness table: F(x) = integral of rate(x) dx, linear extrapolation past the last point.
    // @param {float} compression - bump-stop compression in m (<= 0 means not touching)
    // @return {float} - bump-stop force in N
    float BumpStopForce(float compression)   // integrate piecewise-linear rate table
    {
        if (compression <= 0f) return 0f; float force = 0f; var positions = carParams.bumpStopX; var rates = carParams.bumpStopRate;
        for (int i = 1; i < positions.Length; i++)
        {
            float segmentStart = positions[i - 1], segmentEnd = positions[i]; if (compression <= segmentStart) break; float segmentReached = Mathf.Min(compression, segmentEnd);
            float rateAtStart = rates[i - 1], rateAtReached = Mathf.Lerp(rates[i - 1], rates[i], (segmentReached - segmentStart) / (segmentEnd - segmentStart)); force += 0.5f * (rateAtStart + rateAtReached) * (segmentReached - segmentStart);
        }
        if (compression > positions[positions.Length - 1]) force += rates[rates.Length - 1] * (compression - positions[positions.Length - 1]);
        return force;
    }
    // @description - Piecewise-linear damper force at a damper velocity, with separate bump and rebound tables and linear extrapolation beyond the last velocity point.
    // @param {float} damperVelocity - damper velocity in m/s (+ = compressing)
    // @return {float} - damper force in N (sign follows the velocity)
    float DamperForceAt(float damperVelocity)
    {
        var velocityPoints = carParams.damperV; var bumpForces = front ? carParams.damperBumpF : carParams.damperBumpR; var reboundForces = front ? carParams.damperReboundF : carParams.damperReboundR;
        float speed = Mathf.Abs(damperVelocity), force; int pointCount = velocityPoints.Length; var forceTable = damperVelocity >= 0f ? bumpForces : reboundForces;
        if (speed >= velocityPoints[pointCount - 1]) force = forceTable[pointCount - 1] + (forceTable[pointCount - 1] - forceTable[pointCount - 2]) / (velocityPoints[pointCount - 1] - velocityPoints[pointCount - 2]) * (speed - velocityPoints[pointCount - 1]);
        else force = SuspensionTables.Interpolate1D(velocityPoints, forceTable, speed);
        return damperVelocity >= 0f ? force : -force;
    }
    public float WheelRate => front ? carParams.wheelRateFront : carParams.wheelRateRear;
    // ---- unsprung vertical DOF (carParams.useUnsprungMass): wheel-centre height yw / velocity vw in world, tire spring to ground ----
    [HideInInspector] [FormerlySerializedAs("Ftire")] public float tireForce;
    [HideInInspector] [FormerlySerializedAs("Fsusp")] public float suspensionForce;
    [HideInInspector] public float unsprungMass; float wheelCentreHeight, wheelCentreVelocity; bool wheelHeightInitialised; Vector3 lastPointVelocity; bool havePointVelocity;
    // @description - 1 kHz vertical update: raycasts the ground, computes wheel travel, spring/damper/bump/ARB/jacking forces, optionally integrates the unsprung wheel height, and applies the force to the body.
    // @param {float} deltaTime - suspension step in s
    // @param {float} arbForce - anti-roll bar / twist beam force from the controller, in N
    // @return {Void} - nothing; sets travel, verticalLoad and applies body forces
    public void UpdateSuspension(float deltaTime, float arbForce)
    {
        if (!body) body = GetComponentInParent<Rigidbody>();
        bool useUnsprungMass = carParams.useUnsprungMass; unsprungMass = useUnsprungMass ? (front ? carParams.unsprungFront : carParams.unsprungRear) : 0f;
        float tireStaticDeflection = useUnsprungMass ? unsprungStaticLoad / carParams.tireRate : 0f;   // tire static deflection: free radius = R + statDefl keeps design ride height
        float rayLength = carParams.hubDropDesign + ReboundTravel + carParams.wheelRadius + tireStaticDeflection + 0.05f;
        grounded = Physics.Raycast(transform.position, -transform.up, out hit, rayLength);
        Vector3 designHubPosition = transform.position - transform.up * carParams.hubDropDesign;
        float chassisUpY = Mathf.Max(transform.up.y, 0.3f);
        if (!useUnsprungMass)
        {
            if (grounded && hit.distance > carParams.hubDropDesign + ReboundTravel + carParams.wheelRadius) grounded = false;
            if (!grounded) { travel = -ReboundTravel; haveLastTravel = false; verticalLoad = tireForce = suspensionForce = springForce = damperForce = bumpStopTotalForce = antiRollBarForce = jackingForce = antiPitchForce = 0f; rollCentreHeight = RollCentreHeightAt(travel); bodyForce = Vector3.zero; return; }
            float unclampedTravel = carParams.hubDropDesign - (hit.distance - carParams.wheelRadius);
            travel = Mathf.Clamp(unclampedTravel, -ReboundTravel, BumpTravel);
            float travelVelocity = haveLastTravel ? (travel - lastTravel) / deltaTime : 0f; lastTravel = travel; haveLastTravel = true;
            ComputeSuspensionForces(travelVelocity, unclampedTravel, arbForce);
            verticalLoad = tireForce = Mathf.Max(0f, suspensionForce);
            Vector3 supportForceVector = hit.normal * verticalLoad; body.AddForceAtPosition(supportForceVector, transform.position); bodyForce = supportForceVector;
            return;
        }
        Vector3 bodyPointVelocity = body.GetPointVelocity(designHubPosition);
        if (!wheelHeightInitialised) { wheelCentreHeight = designHubPosition.y; wheelCentreVelocity = bodyPointVelocity.y; wheelHeightInitialised = true; }
        travel = (wheelCentreHeight - designHubPosition.y) / chassisUpY; float relativeVelocity = (wheelCentreVelocity - bodyPointVelocity.y) / chassisUpY;
        ComputeSuspensionForces(relativeVelocity, travel, arbForce);
        float topOutForce = travel < -ReboundTravel ? -2e5f * (-ReboundTravel - travel) : 0f;   // damper top-out stop (E)
        suspensionForce += topOutForce;
        // tire: deflection relative to the free radius
        if (grounded)
        {
            float tireDeflection = hit.point.y + carParams.wheelRadius + tireStaticDeflection - wheelCentreHeight;
            tireForce = tireDeflection > 0f ? Mathf.Max(0f, carParams.tireRate * tireDeflection - carParams.tireDamping * wheelCentreVelocity) : 0f;
            if (tireForce <= 0f) grounded = false;
        }
        else tireForce = 0f;
        verticalLoad = tireForce;
        // semi-implicit wheel integration (1 kHz): m_u yw'' = tireForce - suspensionForce*up.y - m_u g
        wheelCentreVelocity += ((tireForce - suspensionForce * chassisUpY) / unsprungMass - 9.81f) * deltaTime; wheelCentreHeight += wheelCentreVelocity * deltaTime;
        Vector3 suspensionBodyForce = transform.up * suspensionForce; body.AddForceAtPosition(suspensionBodyForce, transform.position);
        // unsprung horizontal inertia (wheels move with the body horizontally; body Rigidbody carries sprung mass only)
        Vector3 pointVelocity = body.GetPointVelocity(designHubPosition);
        if (havePointVelocity) { Vector3 pointAcceleration = (pointVelocity - lastPointVelocity) / deltaTime; pointAcceleration.y = 0f; body.AddForceAtPosition(-unsprungMass * pointAcceleration, designHubPosition); }
        lastPointVelocity = pointVelocity; havePointVelocity = true;
        bodyForce = suspensionBodyForce;
    }
    [HideInInspector] public float unsprungStaticLoad;   // tire static load (sprung + unsprung corner weight), set by controller
    // @description - Clears the unsprung-wheel and tire state after a teleport so a stale wheel height cannot launch the car.
    // @return {Void} - nothing
    public void ResetState() { wheelHeightInitialised = false; haveLastTravel = false; havePointVelocity = false; wheelCentreVelocity = 0f; angularVelocity = 0f; relaxedSlipAngle = 0f; }   // after a teleport: re-seed the unsprung wheel height from the body (stale yw launched the car)
    // @description - Sums the vertical suspension force: spring + damper (through the motion ratio) + bump stop + ARB + jacking from roll-centre height + anti-dive/squat lift.
    // @param {float} suspensionVelocity - wheel travel velocity in m/s
    // @param {float} unclampedTravel - travel before clamping to the rebound/bump limits, in m
    // @param {float} arbForce - anti-roll bar force in N
    // @return {Void} - nothing; sets springForce, damperForce, bumpStopTotalForce, antiRollBarForce, jackingForce, antiPitchForce, suspensionForce
    void ComputeSuspensionForces(float suspensionVelocity, float unclampedTravel, float arbForce)
    {
        float motionRatio = MotionRatio(Mathf.Clamp(travel, -ReboundTravel, BumpTravel));
        springForce = WheelRate * (travel + staticSag);
        damperForce = DamperForceAt(suspensionVelocity * motionRatio) * motionRatio;
        bumpStopTotalForce = BumpStopForce(unclampedTravel - carParams.bumpStopGap) + (unclampedTravel > BumpTravel ? 2e6f * (unclampedTravel - BumpTravel) : 0f);   // + hard limit past full bump
        antiRollBarForce = arbForce;
        rollCentreHeight = RollCentreHeightAt(Mathf.Clamp(travel, -ReboundTravel, BumpTravel));
        // jacking: lateral force (previous step) toward car centre * h_rc / half-track (swing-arm equivalent)
        float inwardLateralForce = left ? force.y : -force.y;
        jackingForce = carParams.useJacking ? inwardLateralForce * rollCentreHeight / Mathf.Max(halfTrack, 0.3f) : 0f;
        antiPitchForce = antiCoef * force.x;
        suspensionForce = springForce + damperForce + bumpStopTotalForce + antiRollBarForce + jackingForce + antiPitchForce;
    }
    // ---- low-speed "stick" model (contact-patch bristle, Pacejka "Tire and Vehicle Dynamics" 3rd ed. sec. 7.2 / 8.4 transient slip) ----
    // State: contact-patch deflections dx, dy (m, wheel frame) with  d(delta)/dt = -Vs - |Vx| * delta / sigma  (Vs = contact slip velocity).
    // At speed this converges to delta = sigma * slip, so the spring force k*delta = C * slip with k = C / sigma reproduces the Pacejka
    // slip stiffness exactly (C_kappa = B*C*D = pKx1*verticalLoad, C_alpha = Ky, both at the current load). At standstill |Vx| -> 0, the leak vanishes
    // and the deflection is a spring that holds the car (static friction) instead of the viscous creep of a slip-based model.
    // Damping c = 2*zeta*sqrt(k*m_corner), m_corner = static corner load / g. ASSUMPTION: zeta = 1 (critically damped patch mode; the
    // real tread/carcass hysteresis is not modelled, and a parked car must not ring at the ~3 Hz patch-spring frequency).
    // Limit: friction ellipse with the Pacejka peak coefficients D_x, D_y (load-sensitive, camber factor) -> full static grip mu*verticalLoad;
    // beyond it the patch slides and the deflection is reset to the elastic limit.
    // Blend: weight w of the slip-based Pacejka path = smoothstep over contact speed from vB/2 to vB, vB = the slip-regularisation floor
    // max(minDen, 0.5 m/s) below which the slip-based path no longer sees the true slip. Above vB the original path is used unchanged.
    public const float StickZeta = 1f;
    [HideInInspector] [FormerlySerializedAs("stickDx")] public float patchDeflectionX;
    [HideInInspector] [FormerlySerializedAs("stickDy")] public float patchDeflectionY;
    [HideInInspector] [FormerlySerializedAs("stickWeight")] public float stickModelWeight;
      // deflections (m); weight of the stick model in the force (diagnostics)
    // tire thermal model (option): null when off; thermalScale = Pacejka multipliers (TireScale.One when off -> bit-identical forces)
    public TireThermal thermal; public TireScale thermalScale = TireScale.One; double frictionHeatAccumulated, rollingResistanceHeatAccumulated, thermalAccumulatedTime;
    // @description - Advances the tire thermal model by the accumulated heat inputs of the last physics step and refreshes the Pacejka scale factors.
    // @param {double} coldGauge - cold inflation gauge pressure in Pa
    // @param {bool} warm - start tread at the grip optimum
    // @param {double} warmCarcass - carcass temperature for a warm start, in degC
    // @return {Void} - nothing; updates thermalScale
    public void ThermalStep(double coldGauge, bool warm, double warmCarcass)
    {
        if (thermal == null) { thermal = new TireThermal(coldGauge, warm, warmCarcass); frictionHeatAccumulated = rollingResistanceHeatAccumulated = thermalAccumulatedTime = 0; }
        thermal.SetColdPressure(coldGauge);
        double integrationTime = thermalAccumulatedTime > 0 ? thermalAccumulatedTime : Time.fixedDeltaTime;
        thermal.bulkFrictionPower = bulkHeatAccumulated / integrationTime;
        thermal.Step(integrationTime, frictionHeatAccumulated / integrationTime, rollingResistanceHeatAccumulated / integrationTime, Vector3.Dot(body.GetPointVelocity(transform.position), transform.forward), verticalLoad, camberDeg * Mathf.Deg2Rad, carParams.tireRate, grounded);
        frictionHeatAccumulated = rollingResistanceHeatAccumulated = thermalAccumulatedTime = bulkHeatAccumulated = 0; thermalScale = thermal.Scale();
    }
    // ---- Experimental (viscoelastic) compound (ViscoTire.cs); never reached with the Default compound ----
    ViscoTire.Flags viscoFlags; double viscoReferenceSlipSpeed = -1, bulkHeatAccumulated;
    [HideInInspector] [FormerlySerializedAs("veGrip")] public float viscoGripFactor = 1f;
    [HideInInspector] [FormerlySerializedAs("veFlashK")] public float viscoFlashTemperatureRise;
    [HideInInspector] [FormerlySerializedAs("veKScale")] public float viscoStiffnessScale = 1f;
    [HideInInspector] [FormerlySerializedAs("veSigScale")] public float viscoRelaxationScale = 1f;
    [HideInInspector] [FormerlySerializedAs("veRrScale")] public float viscoRollingResistanceScale = 1f;
    [HideInInspector] [FormerlySerializedAs("veMu")] public float viscoFrictionCoefficient;
      // diagnostics
    // @description - Applies the viscoelastic (Experimental) compound: grip factor, rate-dependent stiffness, load-dependent relaxation length and rolling-resistance scale.
    // @param {float} longitudinalSpeed - contact-patch speed along the wheel heading, m/s
    // @param {float} lateralSpeed - contact-patch speed across the wheel heading, m/s
    // @param {ref float} relaxationLength - relaxation length in m, scaled in place
    // @param {ref TireScale} scale - Pacejka scale factors, scaled in place
    // @param {ref float} rollingResistanceScale - rolling-resistance multiplier, set here
    // @return {Void} - nothing
    void ViscoUpdate(float longitudinalSpeed, float lateralSpeed, ref float relaxationLength, ref TireScale scale, ref float rrScale)
    {
        viscoFlags = ViscoTire.FromParams(carParams); if (viscoReferenceSlipSpeed < 0) viscoReferenceSlipSpeed = ViscoTire.VsRef(carParams);
        double gripTemperature = thermal != null ? thermal.GripTemp() : TireThermal.OptimalTemperature;              // base coefficients = tyre at its optimum (D, as TireThermal)
        double contactPressure = thermal != null ? System.Math.Max(thermal.gaugePressure, 20000.0) : TireThermal.NominalGaugePressure;   // nominal contact pressure ~ inflation
        double slipSpeedX = longitudinalSpeed - angularVelocity * carParams.wheelRadius, slipSpeed = System.Math.Sqrt(slipSpeedX * slipSpeedX + lateralSpeed * lateralSpeed);
        double gripFactor = ViscoTire.GripFactor(slipSpeed, gripTemperature, contactPressure, viscoReferenceSlipSpeed, viscoFlags, out double flashTemperatureRise);
        scale.mux *= (float)gripFactor; scale.muy *= (float)gripFactor; viscoGripFactor = (float)gripFactor; viscoFlashTemperatureRise = (float)flashTemperatureRise;
        float loadClamped = Mathf.Max(verticalLoad, 1f); double stiffnessFactor = 1.0;
        if (viscoFlags.rateStiffness)
        {
            double contactLength = loadClamped / (contactPressure * TireThermal.TreadWidth), referenceContactLength = carParams.Fz0 / (TireThermal.NominalGaugePressure * TireThermal.TreadWidth);   // contact length (D, membrane approx.)
            stiffnessFactor = ViscoTire.StiffnessFactor(longitudinalSpeed, contactLength, referenceContactLength); scale.kx *= (float)stiffnessFactor; scale.ky *= (float)stiffnessFactor;
        }
        viscoStiffnessScale = (float)stiffnessFactor;
        if (viscoFlags.loadRelax)
        {   // sigma = C_Falpha / c_Fy with constant lateral carcass stiffness c_Fy (Pacejka 2012 ch. 7, S; c_Fy const E): sigma scales with C_Falpha
            float corneringStiffness = scale.ky * Mathf.Sin(2f * Mathf.Atan(loadClamped / (carParams.pKy2 * scale.kyLoad * carParams.Fz0))), referenceCorneringStiffness = Mathf.Sin(2f * Mathf.Atan(1f / carParams.pKy2));
            relaxationLength *= corneringStiffness / referenceCorneringStiffness;
        }
        viscoRelaxationScale = relaxationLength / carParams.relaxLength;
        if (viscoFlags.rollingRes)
        {
            double rollingLossTemperature = thermal != null ? TireThermal.RollingLossTreadShare * thermal.bulkTemperature + (1 - TireThermal.RollingLossTreadShare) * thermal.carcassTemperature : ViscoTire.RollingResistanceReferenceTemperature;
            rrScale = (float)ViscoTire.RollingFactor(longitudinalSpeed, carParams.wheelRadius, rollingLossTemperature);
        }
        viscoRollingResistanceScale = rrScale;
    }
    /// tread deformation hysteresis (brush model, Pacejka ch. 3): tread elements renew every L/V, each storing F^2 L share / (4 C) in the patch
    /// (C_F = 2 c_p a^2, a = L/2), of which ~pi tan d is lost per load-unload pass (E: half-cycle loss). Power = pi tan d V share sum F^2/(4 C);
    /// contact length cancels. This part of the friction power is dissipated in the tread bulk instead of the surface (TireThermal clamps it).
    // @description - Tread-bulk hysteresis heating: pi * tan(delta) * V * sum(F^2 / (4 C)) (brush model); the rest of the friction power heats the surface.
    // @param {float} longitudinalSpeed - speed along the wheel heading, m/s
    // @param {float} longitudinalForce - tire longitudinal force without rolling resistance, N
    // @param {float} lateralForce - tire lateral force, N
    // @param {in TireScale} scale - Pacejka scale factors
    // @return {float} - power dissipated in the tread bulk, W
    float BulkHysteresisPower(float longitudinalSpeed, float longitudinalForce, float lateralForce, in TireScale scale)
    {
        if (thermal == null) return 0f;
        float loadClamped = Mathf.Max(verticalLoad, 1f);
        double longitudinalStiffness = carParams.pKx1 * scale.kx * loadClamped, lateralStiffness = carParams.pKy1 * scale.ky * carParams.Fz0 * Mathf.Sin(2f * Mathf.Atan(loadClamped / (carParams.pKy2 * scale.kyLoad * carParams.Fz0)));
        double contactLength = loadClamped / (System.Math.Max(thermal.gaugePressure, 20000.0) * TireThermal.TreadWidth), absoluteSpeed = System.Math.Abs(longitudinalSpeed);
        double tanDelta = ViscoTire.TanDelta(System.Math.Max(absoluteSpeed, 0.5) / contactLength, thermal.bulkTemperature);
        return (float)(System.Math.PI * tanDelta * absoluteSpeed * ViscoTire.TreadShare * (longitudinalForce * longitudinalForce / (4 * longitudinalStiffness) + lateralForce * lateralForce / (4 * lateralStiffness)));
    }
    // @description - Switches the thermal model off and restores unit Pacejka scale factors (bit-identical forces).
    // @return {Void} - nothing
    public void ThermalOff() { thermal = null; thermalScale = TireScale.One; }
    // @description - 2 kHz tire step: slip from contact-patch velocity, Pacejka forces blended with the low-speed bristle (stick) model, wheel spin integration with brake/drive torque, and force application to the body.
    // @param {float} substep - tire/drivetrain substep in s
    // @param {float} forceScale - fraction of the force applied per substep (1 / substeps)
    // @return {Void} - nothing; sets force, slipRatio, slipAngle, angularVelocity
    public void TireStep(float substep, float forceScale)
    {
        float rotationalInertia = carParams.wheelInertia + DriveInertia;
        if (!grounded)
        {
            angularVelocity += (driveTorque - Mathf.Sign(angularVelocity) * Mathf.Min(brakeTorque, Mathf.Abs(angularVelocity) * rotationalInertia / substep)) / rotationalInertia * substep;
            force = Vector2.zero; slipRatio = 0f; patchDeflectionX = patchDeflectionY = 0f; stickModelWeight = 0f; return;
        }
        Quaternion steerRotation = Quaternion.AngleAxis(steerDeg, transform.up);
        Vector3 forwardDirection = steerRotation * transform.forward, rightDirection = steerRotation * transform.right;
        Vector3 contactVelocity = body.GetPointVelocity(hit.point);
        float longitudinalSpeed = Vector3.Dot(contactVelocity, forwardDirection), lateralSpeed = Vector3.Dot(contactVelocity, rightDirection);
        float minimumSlipDenominator = 1.5f * carParams.pKx1 * Mathf.Max(verticalLoad, 500f) * carParams.wheelRadius * carParams.wheelRadius * substep / rotationalInertia;
        float blendSpeed = Mathf.Max(minimumSlipDenominator, 0.5f);
        float slipDenominator = Mathf.Max(Mathf.Abs(longitudinalSpeed), blendSpeed);
        float magicFormulaSlipRatio = Mathf.Clamp((angularVelocity * carParams.wheelRadius - longitudinalSpeed) / slipDenominator, -1.5f, 1.5f);
        slipRatio = magicFormulaSlipRatio;
        slipAngle = Mathf.Atan2(lateralSpeed, Mathf.Max(Mathf.Abs(longitudinalSpeed), 0.5f));
        float relaxationLength = carParams.relaxLength, rollingResistanceScale = 1f; TireScale effectiveScale = thermalScale;   // Default compound: exactly the original values
        bool isViscoCompound = carParams.tireCompound == CarParams.TireCompound.ExperimentalViscoelastic;
        if (isViscoCompound) ViscoUpdate(longitudinalSpeed, lateralSpeed, ref relaxationLength, ref effectiveScale, ref rollingResistanceScale);
        float relaxationStepFraction = Mathf.Min(1f, (Mathf.Abs(longitudinalSpeed) + 0.5f) * substep / relaxationLength);
        relaxedSlipAngle += relaxationStepFraction * (slipAngle - relaxedSlipAngle);
        float camberRadians = camberDeg * Mathf.Deg2Rad * (left ? -1f : 1f);
        force = PacejkaTire.Combined(carParams, magicFormulaSlipRatio, -relaxedSlipAngle, verticalLoad, camberRadians, isViscoCompound ? effectiveScale : thermalScale);
        float wheelSpeedKmh = longitudinalSpeed * 3.6f, rollingResistanceForce = (carParams.rollingRes + carParams.rollingResV2 * wheelSpeedKmh * wheelSpeedKmh) * verticalLoad * thermalScale.rr * Mathf.Clamp(longitudinalSpeed, -1f, 1f);
        if (isViscoCompound) rollingResistanceForce *= rollingResistanceScale;   // speed-dependent radial-tire RR (Wong form, E)
        force.x -= rollingResistanceForce;
        float contactSpeed = Mathf.Sqrt(longitudinalSpeed * longitudinalSpeed + lateralSpeed * lateralSpeed);
        float pacejkaWeight = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.5f * blendSpeed, blendSpeed, contactSpeed)); stickModelWeight = 1f - pacejkaWeight;
        float patchRelaxationLength = relaxationLength, absoluteLongitudinalSpeed = Mathf.Abs(longitudinalSpeed);
        if (pacejkaWeight >= 1f)
        {   // original slip-based path (unchanged); the patch state tracks its steady value so the stick model takes over smoothly
            float spinWithoutBrakeSlipPath = angularVelocity + (driveTorque - force.x * carParams.wheelRadius) / rotationalInertia * substep;
            float brakeSpinChangeSlipPath = brakeTorque / rotationalInertia * substep;
            angularVelocity = Mathf.Abs(spinWithoutBrakeSlipPath) <= brakeSpinChangeSlipPath ? 0f : spinWithoutBrakeSlipPath - Mathf.Sign(spinWithoutBrakeSlipPath) * brakeSpinChangeSlipPath;
            patchDeflectionX += substep * (-(longitudinalSpeed - angularVelocity * carParams.wheelRadius) - absoluteLongitudinalSpeed * patchDeflectionX / patchRelaxationLength); patchDeflectionY += substep * (-lateralSpeed - absoluteLongitudinalSpeed * patchDeflectionY / patchRelaxationLength);
            ClampPatch(verticalLoad, camberRadians, patchRelaxationLength);
        }
        else
        {
            float loadClamped = Mathf.Max(verticalLoad, 0f);
            float longitudinalSlipStiffness = carParams.pKx1 * thermalScale.kx * loadClamped;                                                             // dFx/dkappa at 0 (= B*C*D)
            float corneringStiffness = carParams.pKy1 * thermalScale.ky * carParams.Fz0 * Mathf.Sin(2f * Mathf.Atan(loadClamped / (carParams.pKy2 * thermalScale.kyLoad * carParams.Fz0)));       // Ky = dFy/dalpha at 0
            float patchStiffnessX = longitudinalSlipStiffness / patchRelaxationLength, patchStiffnessY = corneringStiffness / patchRelaxationLength;
            float cornerMass = Mathf.Max(unsprungStaticLoad, 1f) / 9.81f;
            float patchDampingX = 2f * StickZeta * Mathf.Sqrt(patchStiffnessX * cornerMass), patchDampingY = 2f * StickZeta * Mathf.Sqrt(patchStiffnessY * cornerMass);
            float wheelRadius = carParams.wheelRadius, stickBlend = 1f - pacejkaWeight;
            // wheel spin: Fx = w*Fpac + b*(kx*dx - cx*(vx - angularVelocity*R)); damping term implicit (stiff vs the wheel inertia at 2 kHz)
            float implicitDampingTerm = substep * stickBlend * patchDampingX * wheelRadius * wheelRadius / rotationalInertia;
            float spinWithoutBrake = (angularVelocity + substep / rotationalInertia * (driveTorque - wheelRadius * (pacejkaWeight * force.x + stickBlend * (patchStiffnessX * patchDeflectionX - patchDampingX * longitudinalSpeed - rollingResistanceForce)))) / (1f + implicitDampingTerm);
            float brakeSpinChange = brakeTorque / rotationalInertia * substep / (1f + implicitDampingTerm);
            angularVelocity = Mathf.Abs(spinWithoutBrake) <= brakeSpinChange ? 0f : spinWithoutBrake - Mathf.Sign(spinWithoutBrake) * brakeSpinChange;
            float slipSpeedX = longitudinalSpeed - angularVelocity * wheelRadius, slipSpeedY = lateralSpeed;
            patchDeflectionX += substep * (-slipSpeedX - absoluteLongitudinalSpeed * patchDeflectionX / patchRelaxationLength); patchDeflectionY += substep * (-slipSpeedY - absoluteLongitudinalSpeed * patchDeflectionY / patchRelaxationLength);
            float patchForceX = patchStiffnessX * patchDeflectionX - patchDampingX * slipSpeedX, patchForceY = patchStiffnessY * patchDeflectionY - patchDampingY * slipSpeedY;
            float relativeLoadChange = (loadClamped - carParams.Fz0) / carParams.Fz0;
            float peakForceX = Mathf.Max((carParams.pDx1 + carParams.pDx2 * relativeLoadChange) * thermalScale.mux * loadClamped, 1f), peakForceY = Mathf.Max((carParams.pDy1 + carParams.pDy2 * relativeLoadChange) * thermalScale.muy * (1f - 4f * camberRadians * camberRadians) * loadClamped, 1f);
            float ellipseRatio = (patchForceX / peakForceX) * (patchForceX / peakForceX) + (patchForceY / peakForceY) * (patchForceY / peakForceY);
            if (ellipseRatio > 1f) { float scaleBack = 1f / Mathf.Sqrt(ellipseRatio); patchForceX *= scaleBack; patchForceY *= scaleBack; patchDeflectionX = patchForceX / patchStiffnessX; patchDeflectionY = patchForceY / patchStiffnessY; }   // sliding: elastic part at the limit
            force = new Vector2(pacejkaWeight * force.x + stickBlend * (patchForceX - rollingResistanceForce), pacejkaWeight * force.y + stickBlend * patchForceY);
            // reported slip: the true slip ratio (a locked, sliding wheel reads -1); below 5 cm/s the ratio is undefined -> 0/0 guard
            float trueSlipRatio = Mathf.Clamp((angularVelocity * wheelRadius - longitudinalSpeed) / Mathf.Max(absoluteLongitudinalSpeed, 0.05f), -1.5f, 1.5f);
            slipRatio = pacejkaWeight * magicFormulaSlipRatio + stickBlend * trueSlipRatio;
        }
        if (thermal != null)
        {   // thermal inputs: friction power -F.Vs (tire force without RR) and rolling-resistance power, applied force scale included
            float slipSpeedX = longitudinalSpeed - angularVelocity * carParams.wheelRadius;
            frictionHeatAccumulated += -((force.x + rollingResistanceForce) * slipSpeedX + force.y * lateralSpeed) * forceScale * substep; rollingResistanceHeatAccumulated += rollingResistanceForce * longitudinalSpeed * forceScale * substep; thermalAccumulatedTime += substep;
            if (isViscoCompound && viscoFlags.bulkHeat) bulkHeatAccumulated += BulkHysteresisPower(longitudinalSpeed, force.x + rollingResistanceForce, force.y, effectiveScale) * forceScale * substep;
        }
        // longitudinal at the contact patch; lateral at roll-centre height (geometric lateral load transfer)
        Vector3 lateralForcePoint = carParams.useJacking ? hit.point + transform.up * rollCentreHeight : hit.point;
        body.AddForceAtPosition(forwardDirection * force.x * forceScale, hit.point);
        body.AddForceAtPosition(rightDirection * force.y * forceScale, lateralForcePoint);
    }
    // @description - Keeps the tracked contact-patch deflection inside the friction ellipse (the force the patch can hold elastically), scaling both components back.
    // @param {float} loadClamped - vertical load in N
    // @param {float} camberRadians - camber angle in rad
    // @param {float} patchRelaxationLength - relaxation length in m
    // @return {Void} - nothing; scales patchDeflectionX/Y
    void ClampPatch(float loadClamped, float camberRadians, float patchRelaxationLength)
    {   // keep the tracked patch deflection inside the friction ellipse (what the patch can hold elastically)
        loadClamped = Mathf.Max(loadClamped, 0f); if (loadClamped <= 0f) { patchDeflectionX = patchDeflectionY = 0f; return; }
        float relativeLoadChange = (loadClamped - carParams.Fz0) / carParams.Fz0;
        float patchStiffnessX = carParams.pKx1 * thermalScale.kx * loadClamped / patchRelaxationLength, patchStiffnessY = carParams.pKy1 * thermalScale.ky * carParams.Fz0 * Mathf.Sin(2f * Mathf.Atan(loadClamped / (carParams.pKy2 * thermalScale.kyLoad * carParams.Fz0))) / patchRelaxationLength;
        float peakForceX = Mathf.Max((carParams.pDx1 + carParams.pDx2 * relativeLoadChange) * thermalScale.mux * loadClamped, 1f), peakForceY = Mathf.Max((carParams.pDy1 + carParams.pDy2 * relativeLoadChange) * thermalScale.muy * (1f - 4f * camberRadians * camberRadians) * loadClamped, 1f);
        float patchForceX = patchStiffnessX * patchDeflectionX, patchForceY = patchStiffnessY * patchDeflectionY, ellipseRatio = (patchForceX / peakForceX) * (patchForceX / peakForceX) + (patchForceY / peakForceY) * (patchForceY / peakForceY);
        if (ellipseRatio > 1f) { float scaleBack = 1f / Mathf.Sqrt(ellipseRatio); patchDeflectionX *= scaleBack; patchDeflectionY *= scaleBack; }
    }
    // @description - Poses the wheel visual: position follows travel, rotation from steer, camber and accumulated spin.
    // @return {Void} - nothing
    void LateUpdate()
    {
        if (!visual || carParams == null) return;
        spinDeg = (spinDeg + angularVelocity * Mathf.Rad2Deg * Time.deltaTime) % 360f;
        visual.position = transform.position - transform.up * (carParams.hubDropDesign - travel);   // travel follows the unsprung DOF when enabled
        float visualCamberDeg = (left ? 1f : -1f) * camberDeg;   // visual tilt only
        visual.localRotation = Quaternion.Euler(0f, steerDeg, visualCamberDeg) * Quaternion.Euler(spinDeg, 0f, 0f) * Quaternion.Euler(0f, 0f, 90f);
    }
}
