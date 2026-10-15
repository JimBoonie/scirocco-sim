// Torsional chain of the experimental driveline (implements ITorsionalDriveline). Degrees of freedom (all rad, rad/s, SI):
//   crank+flywheel (J_e) --Karnopp clutch friction-- disc (J_c) --3-stage damper w/ hysteresis-- input shaft (J_in) --gear mesh with lash (k_m, c_m)--
//   carrier (J_d) --unequal halfshafts combined for the open differential with lash-- left wheel, right wheel (J_w each)
//   plus the engine+gearbox block rolling on its mounts (I_b, k_b, c_b), which reacts every internal torque.
// Mesh coordinate: psi = theta_in/r - theta_d + (1 - 1/r) theta_b (r = total ratio). Shaft coordinate: delta = theta_d - mean(theta_wheel).
// Integration: semi-implicit (symplectic) Euler in micro-steps sized so the fastest mode advances at most maximumPhaseAdvancePerMicrostepRadians.
// Stiff modes are therefore resolved, not filtered, and the engine firing pulses (up to ~400 Hz) are sampled many times per cycle.
using UnityEngine;

public class TorsionalDriveline : ITorsionalDriveline
{
    readonly ShuffleDrivelineParameters parameters; readonly CarParams carParams;
    readonly ClutchDamperModel damper; readonly ClutchFrictionModel clutchFriction;
    readonly double crankInertia, discInertia, inputShaftInertia, carrierInertia, meshStiffness, meshDampingRatio, meshHalfLashAtInput;
    readonly double shaftStiffness, shaftDamping, shaftHalfLash, blockInertia, mountStiffness, mountDamping;
    readonly bool engineRollEnabled;
    double crankAngle, crankSpeed, discSpeed, inputSpeed, carrierSpeed, leftSpeed, rightSpeed, blockSpeed;
    double damperTwist, meshDeflection, shaftDeflection, blockAngle;
    double previousRatio; bool clutchStuck; double meshDamping, meshHalfLash;

    public double CrankAngularVelocity => crankSpeed;
    public double InputShaftAngularVelocity => inputSpeed;
    public double CarrierAngularVelocity => carrierSpeed;
    public double DamperTwistRadians => damperTwist;
    public double MeshDeflectionRadians => meshDeflection;
    public double ShaftDeflectionRadians => shaftDeflection;
    public double EngineBlockAngleRadians => blockAngle;
    public bool ClutchIsStuck => clutchStuck;
    public double ClutchTemperatureKelvin => clutchFriction.TemperatureKelvin;

    // @description - builds the chain from the parameter asset and the car parameters
    // @param {ShuffleDrivelineParameters} parameters - model parameters
    // @param {CarParams} carParams - engine inertia and clutch capacity
    public TorsionalDriveline(ShuffleDrivelineParameters parameters, CarParams carParams)
    {
        this.parameters = parameters; this.carParams = carParams;
        damper = new ClutchDamperModel(parameters); clutchFriction = new ClutchFrictionModel(parameters, carParams);
        crankInertia = carParams.engineInertia; discInertia = parameters.clutchDiscInertiaKilogramMetersSquared; inputShaftInertia = parameters.inputShaftInertiaKilogramMetersSquared;
        carrierInertia = parameters.carrierInertiaKilogramMetersSquared; meshStiffness = parameters.gearMeshStiffnessAtCarrierNewtonMetersPerRadian; meshDampingRatio = parameters.gearMeshDampingRatio;
        meshHalfLashAtInput = 0.5 * parameters.gearLashAtInputShaftDegrees * Mathf.Deg2Rad;
        shaftStiffness = parameters.CombinedShaftStiffness();
        shaftDamping = parameters.shaftStructuralLossFactor * shaftStiffness / (2.0 * Mathf.PI * parameters.dampingReferenceFrequencyHertz);   // eta k / omega_ref
        shaftHalfLash = 0.5 * parameters.shaftAndJointLashAtWheelDegrees * Mathf.Deg2Rad;
        engineRollEnabled = parameters.engineRollEnabled; blockInertia = parameters.engineBlockRollInertiaKilogramMetersSquared;
        double rollOmega = 2.0 * Mathf.PI * parameters.engineRollNaturalFrequencyHertz;
        mountStiffness = blockInertia * rollOmega * rollOmega; mountDamping = 2.0 * parameters.engineRollDampingRatio * System.Math.Sqrt(mountStiffness * blockInertia);
    }

    // @description - restarts the chain unloaded from measured speeds
    // @param {double} crankAngularVelocity - rad/s
    // @param {double} meanWheelAngularVelocity - rad/s
    public void Initialize(double crankAngularVelocity, double meanWheelAngularVelocity)
    {
        crankSpeed = discSpeed = inputSpeed = crankAngularVelocity; carrierSpeed = leftSpeed = rightSpeed = meanWheelAngularVelocity; blockSpeed = 0.0;
        damperTwist = meshDeflection = shaftDeflection = blockAngle = 0.0; previousRatio = 0.0; clutchStuck = false; damper.Reset(); clutchFriction.ResetTemperature();
    }

    // @description - dead-zone (backlash) function: zero inside +-halfLash, the excess outside
    // @param {double} deflection - rad
    // @param {double} halfLash - rad
    // @return {double} - effective elastic deflection in rad
    static double DeadZone(double deflection, double halfLash) => deflection > halfLash ? deflection - halfLash : deflection < -halfLash ? deflection + halfLash : 0.0;

    // @description - torque of an element with backlash, stiffness and damping that can only push (never pull) across the gap
    // @param {double} deflection - rad
    // @param {double} deflectionRate - rad/s
    // @param {double} halfLash - rad
    // @param {double} stiffness - N m/rad
    // @param {double} damping - N m s/rad
    // @return {double} - N m, zero in the gap and when the damping would make it tensile
    static double LashTorque(double deflection, double deflectionRate, double halfLash, double stiffness, double damping)
    {
        double elastic = DeadZone(deflection, halfLash); if (elastic == 0.0) return 0.0;
        double torque = stiffness * elastic + damping * deflectionRate;
        return (elastic > 0.0 && torque < 0.0) || (elastic < 0.0 && torque > 0.0) ? 0.0 : torque;
    }

    // @description - Karnopp stick-slip clutch torque. Inside the dead band |slip| < T_static dt / J_reduced (D: the largest speed change one step
    //   can produce) the clutch sticks if the torque needed to hold it, T_hold, is within static capacity; otherwise it slips with kinetic friction.
    // @param {double} engineTorque - crank torque applied by the engine side in N m
    // @param {double} damperTorque - torque the damper takes off the disc in N m
    // @param {double} auxiliaryInertia - extra crank inertia in kg m^2
    // @param {double} clampFraction - 0..1
    // @param {double} step - micro-step in s
    // @return {double} - clutch torque from the crank onto the disc in N m
    double ResolveClutchTorque(double engineTorque, double damperTorque, double auxiliaryInertia, double clampFraction, double step)
    {
        double crankTotal = crankInertia + auxiliaryInertia, slip = crankSpeed - discSpeed;
        double staticCapacity = clutchFriction.StaticCapacity((float)clampFraction), kineticCapacity = clutchFriction.KineticCapacity((float)clampFraction, (float)slip);
        if (staticCapacity <= 0.0) { clutchStuck = false; return 0.0; }
        double reducedInertia = crankTotal * discInertia / (crankTotal + discInertia), deadBandSpeed = staticCapacity * step / reducedInertia;
        if (System.Math.Abs(slip) <= deadBandSpeed)
        {
            double holdTorque = (discInertia * engineTorque + crankTotal * damperTorque) / (crankTotal + discInertia);
            if (System.Math.Abs(holdTorque) <= staticCapacity) { clutchStuck = true; return holdTorque; }
            clutchStuck = false; return holdTorque > 0.0 ? kineticCapacity : -kineticCapacity;
        }
        clutchStuck = false; return slip > 0.0 ? kineticCapacity : -kineticCapacity;
    }

    // @description - estimates the fastest angular frequency of the chain (Gershgorin-style sum of the stiffnesses and damping rates acting on each inertia)
    // @param {double} ratio - total ratio r (0 = neutral)
    // @param {double} wheelInertia - kg m^2
    // @return {double} - rad/s
    double FastestAngularFrequency(double ratio, double wheelInertia)
    {
        double stopStiffness = damper.StopStiffness, frequency = System.Math.Sqrt(stopStiffness / discInertia);
        double inverseRatioSquared = ratio == 0.0 ? 0.0 : 1.0 / (ratio * ratio), meshK = ratio == 0.0 ? 0.0 : meshStiffness;
        frequency = System.Math.Max(frequency, System.Math.Sqrt((stopStiffness + meshK * inverseRatioSquared) / inputShaftInertia));
        frequency = System.Math.Max(frequency, System.Math.Sqrt((meshK + shaftStiffness) / carrierInertia));
        frequency = System.Math.Max(frequency, System.Math.Sqrt(0.25 * shaftStiffness / wheelInertia));
        if (engineRollEnabled) { double blockCoupling = ratio == 0.0 ? 0.0 : meshK * (1.0 - 1.0 / ratio) * (1.0 - 1.0 / ratio); frequency = System.Math.Max(frequency, System.Math.Sqrt((mountStiffness + blockCoupling) / blockInertia)); }
        double dampingRate = (meshDamping * (inverseRatioSquared / inputShaftInertia + 1.0 / carrierInertia)) + shaftDamping * (1.0 / carrierInertia + 0.25 / wheelInertia);
        return System.Math.Max(frequency, dampingRate);
    }

    // @description - re-synchronizes the mesh when a gear is engaged (instant synchro: input shaft takes the speed the mesh demands, no lash offset)
    // @param {double} ratio - new total ratio r
    // @return {Void}
    void EngageGear(double ratio)
    {
        meshDeflection = 0.0; inputSpeed = ratio * carrierSpeed - (ratio - 1.0) * blockSpeed;
    }

    // @description - mesh damping from the damping ratio and the reflected inertia chain; lash referred to the carrier
    // @param {double} ratio - total ratio r (nonzero)
    // @return {Void}
    void UpdateMeshProperties(double ratio)
    {
        double inputReflected = inputShaftInertia / (ratio * ratio), equivalentInertia = inputReflected * carrierInertia / (inputReflected + carrierInertia);
        meshDamping = 2.0 * meshDampingRatio * System.Math.Sqrt(meshStiffness * equivalentInertia);
        meshHalfLash = meshHalfLashAtInput / System.Math.Abs(ratio);
    }

    // @description - advances the chain by one outer step with as many micro-steps as stability and accuracy need
    // @param {DrivelineStepInput} input - inputs
    // @param {double} stepSeconds - outer step in s
    // @return {DrivelineStepResult} - wheel torques and state
    public DrivelineStepResult Advance(in DrivelineStepInput input, double stepSeconds)
    {
        double ratio = input.totalRatioSigned, wheelInertia = input.wheelInertiaKilogramMetersSquared;
        leftSpeed = input.leftWheelAngularVelocity; rightSpeed = input.rightWheelAngularVelocity;   // resynchronize the predicted wheels with the tire integrator
        if (ratio != 0.0) { if (previousRatio != ratio) { UpdateMeshProperties(ratio); EngageGear(ratio); } } else meshDeflection = 0.0;
        previousRatio = ratio;
        int microstepCount = (int)System.Math.Ceiling(stepSeconds * FastestAngularFrequency(ratio, wheelInertia) / parameters.maximumPhaseAdvancePerMicrostepRadians);
        if (microstepCount < 1) microstepCount = 1;
        double step = stepSeconds / microstepCount, leftImpulse = 0.0, rightImpulse = 0.0, clutchImpulse = 0.0, auxiliaryInertia = input.crankshaftTorqueSource.AuxiliaryInertiaKilogramMetersSquared;
        for (int microstep = 0; microstep < microstepCount; microstep++)
        {
            MicroStep(input, ratio, wheelInertia, auxiliaryInertia, step, out double shaftTorque, out double clutchTorque);
            leftImpulse += 0.5 * shaftTorque * step; rightImpulse += 0.5 * shaftTorque * step; clutchImpulse += clutchTorque * step;
        }
        crankSpeed = System.Math.Max(0.0, crankSpeed); input.crankshaftTorqueSource.EndInterval(crankSpeed);
        return new DrivelineStepResult
        {
            leftWheelAverageTorque = leftImpulse / stepSeconds, rightWheelAverageTorque = rightImpulse / stepSeconds, crankAngularVelocity = crankSpeed,
            clutchSlipAngularVelocity = crankSpeed - discSpeed, clutchFrictionTorque = clutchImpulse / stepSeconds, clutchTemperatureKelvin = clutchFriction.TemperatureKelvin, microstepCount = microstepCount
        };
    }

    // @description - one semi-implicit Euler micro-step: all torques from the current positions, then speeds, then positions
    // @param {DrivelineStepInput} input - inputs
    // @param {double} ratio - total ratio r
    // @param {double} wheelInertia - kg m^2 each
    // @param {double} auxiliaryInertia - extra crank inertia kg m^2
    // @param {double} step - micro-step in s
    // @param {double} shaftTorque - (out) torque the combined halfshafts carry in N m
    // @param {double} clutchTorque - (out) clutch torque in N m
    // @return {Void}
    void MicroStep(in DrivelineStepInput input, double ratio, double wheelInertia, double auxiliaryInertia, double step, out double shaftTorque, out double clutchTorque)
    {
        double engineTorque = input.crankshaftTorqueSource.TorqueAt(crankAngle, crankSpeed);
        double damperTorque = damper.Torque(damperTwist);
        double meshTorque = 0.0, inputReaction = 0.0, meshRate = 0.0;
        if (ratio != 0.0)
        {
            meshRate = inputSpeed / ratio - carrierSpeed + (1.0 - 1.0 / ratio) * blockSpeed;
            meshTorque = LashTorque(meshDeflection, meshRate, meshHalfLash, meshStiffness, meshDamping);
            bool driving = meshTorque * carrierSpeed > 0.0;
            inputReaction = -meshTorque / ratio * (driving ? 1.0 / input.drivetrainEfficiency : input.drivetrainEfficiency);
        }
        double shaftRate = carrierSpeed - 0.5 * (leftSpeed + rightSpeed);
        shaftTorque = LashTorque(shaftDeflection, shaftRate, shaftHalfLash, shaftStiffness, shaftDamping);
        clutchTorque = ResolveClutchTorque(engineTorque, damperTorque, auxiliaryInertia, input.clutchClampFraction, step);
        double crankTotalInertia = crankInertia + auxiliaryInertia;
        crankSpeed += (engineTorque - clutchTorque) / crankTotalInertia * step;
        discSpeed += (clutchTorque - damperTorque) / discInertia * step;
        inputSpeed += (damperTorque + inputReaction) / inputShaftInertia * step;
        carrierSpeed += (meshTorque - shaftTorque) / carrierInertia * step;
        leftSpeed += (0.5 * shaftTorque - input.leftTireResistingTorque) / wheelInertia * step;
        rightSpeed += (0.5 * shaftTorque - input.rightTireResistingTorque) / wheelInertia * step;
        if (engineRollEnabled) blockSpeed += (-engineTorque - (inputReaction + meshTorque) - mountStiffness * blockAngle - mountDamping * blockSpeed) / blockInertia * step;
        if (clutchStuck) { double common = (crankTotalInertia * crankSpeed + discInertia * discSpeed) / (crankTotalInertia + discInertia); crankSpeed = discSpeed = common; }
        else clutchFriction.AddHeat((float)(clutchTorque * (crankSpeed - discSpeed)), (float)step);
        double twistIncrement = (discSpeed - inputSpeed) * step;
        damperTwist += twistIncrement; damper.AdvanceFrictionElement(twistIncrement);
        if (ratio != 0.0) meshDeflection += (inputSpeed / ratio - carrierSpeed + (1.0 - 1.0 / ratio) * blockSpeed) * step;
        shaftDeflection += (carrierSpeed - 0.5 * (leftSpeed + rightSpeed)) * step;
        if (engineRollEnabled) blockAngle += blockSpeed * step;
        crankAngle += crankSpeed * step; if (crankAngle > 4.0 * Mathf.PI) crankAngle -= 4.0 * Mathf.PI;
    }
}
