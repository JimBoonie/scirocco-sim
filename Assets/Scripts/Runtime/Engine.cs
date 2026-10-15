using UnityEngine;
// Physical air-path engine model (after N. Heusinkveld's critique of the "lerp between WOT and engine-braking curves" throttle model;
// sources and tags in CarParams and diagnostics/throttle_model/SOURCES.md).
//  * Pedal -> cable -> primary butterfly angle (linear lever, E); a progressive link opens the secondary plate late (two-stage CIS body).
//  * Open area of each plate: Heywood (1988) butterfly-area formula incl. shaft; plus the idle-air bypass (sized so idle is a steady state).
//  * Throttle mass flow: compressible isentropic orifice, Cd * A * p0 / sqrt(R T0) * Psi(p_man/p0), choked below the critical ratio.
//  * Cylinder air demand: VE(rpm) * rho_man * Vd * rpm/120 (four-stroke). VE(rpm) is DERIVED so the steady WOT brake torque equals the sourced curve.
//  * Manifold: isothermal filling/emptying dp/dt = R T / V * (mdot_throttle - mdot_cylinders).
//  * Closed-plate leakage (v2): the stop screw holds the primary plate stopScrewTurns * pitch / leverArm rad off its seat (Bentley paper + 1/4 turn),
//    plus the plate-to-bore annulus pi * D * clearance on both plates. Always open, in parallel with the idle bypass.
//  * Fresh charge per cycle (v2): VE(rpm) * rho_man * Vd * phi, phi = residual-gas fraction lost:
//      Heywood (1988) ideal throttled-cycle volumetric efficiency (r_c - (p_e/p_i)^(1/n)) / (r_c - 1) (residual at p_e expands into the intake at IVO)
//      minus the volume, at manifold pressure, of exhaust gas blown back through the valve-overlap curtain (isentropic orifice, overlap duration).
//    -> as p_man falls the cylinders induct less fresh air per stroke, so the manifold cannot be pumped to near vacuum.
//  * Indicated torque = eta_i * m_air/cycle * LHV / AFR / (4 pi);  friction: Chen-Flynn FMEP;  pumping: (p_man,WOT(rpm) - p_man) * Vd / (4 pi).
//    The Heywood/Barnes-Moss tfmep correlation is total MOTORED friction at WOT, which by Heywood's definition (tfmep = pmep + rfmep + afmep)
//    already contains the WOT pumping loss; only the extra throttling loss below the WOT manifold pressure is added (v2; v1 used p_amb, double-counting
//    the WOT throttle-body drop).
//  => the torque at a fixed pedal falls with rpm (the cylinders out-pump a fixed throttle area), so part-throttle curves slope down
//     and cross zero; at low rpm a small opening already fills the manifold.
public class Engine
{
    readonly CarParams carParams;
    public float rpm;                       // crankshaft speed in rpm
    public float manifoldPressureRatio = 1f;   // manifold absolute pressure / ambient (HUD/audio)
    public float throttleArea;              // open throttle flow area / wide-open-throttle area
    public float starterTorque;             // N m at the crankshaft from the starter motor this step
    float angularVelocity;                  // rad/s, crankshaft (same state as rpm, kept in SI)
    float manifoldPressure;                 // Pa absolute
    public bool ignition = true;            // key in ON
    public bool starter;                    // key held in START
    public bool combusting = true;          // combustion happening this step
    public bool limiter;                    // fuel cut active this step
    readonly float[] volumetricEfficiency;  // VE(rpm) table, same rpm grid as carParams.rpmPoints (derived in the constructor)
    readonly float[] wideOpenManifoldPressure;   // Pa, steady WOT manifold pressure on the rpm grid (used as the pumping-loss reference)
    float wideOpenThrottleArea;             // m^2, both plates fully open
    float idleBypassAreaValue;              // m^2, idle-air bypass (sized so idle is a steady state)
    float closedPlateLeakAreaValue;         // m^2, closed-plate leakage (stop screw + plate-to-bore annulus)
    public float IdleBypassArea => idleBypassAreaValue;
    public float LeakArea => closedPlateLeakAreaValue;

    // @description - builds the engine: computes the WOT and leakage areas, derives VE(rpm) from the sourced torque curve and sizes the idle bypass
    // @param {CarParams} carParams - engine, throttle body and manifold parameters
    // @return {Void} - constructor
    public Engine(CarParams carParams)
    {
        this.carParams = carParams; volumetricEfficiency = new float[carParams.rpmPoints.Length]; wideOpenManifoldPressure = new float[carParams.rpmPoints.Length];
        wideOpenThrottleArea = PlateArea(carParams.fullOpenPlateDeg * Mathf.Deg2Rad, carParams.primaryBore) + PlateArea(carParams.fullOpenPlateDeg * Mathf.Deg2Rad, carParams.secondaryBore);
        float closedPlateAngle = carParams.closedPlateDeg * Mathf.Deg2Rad;
        // stop screw lifts the primary plate by turns x pitch / lever arm rad; the annulus pi * D * clearance exists on both plates
        closedPlateLeakAreaValue = PlateArea(closedPlateAngle + carParams.stopScrewTurns * carParams.stopScrewPitch / carParams.stopLeverArm, carParams.primaryBore) + Mathf.PI * (carParams.primaryBore + carParams.secondaryBore) * carParams.plateBoreClearance;
        CalibrateVolumetricEfficiency();
        SizeIdleBypass();
        SetRunning(true);
    }

    // @description - fixed-point iteration for VE(rpm) so that the steady wide-open-throttle brake torque equals the sourced curve (D):
    //   VE = (T_brake + T_friction + T_pumping) / T_indicated(VE = 1). Also stores the steady WOT manifold pressure per rpm point.
    // @return {Void}
    void CalibrateVolumetricEfficiency()
    {
        for (int i = 0; i < volumetricEfficiency.Length; i++) { volumetricEfficiency[i] = 1f; wideOpenManifoldPressure[i] = carParams.ambientPressure; }
        for (int iteration = 0; iteration < 40; iteration++)
            for (int i = 0; i < volumetricEfficiency.Length; i++)
            {
                float engineRpm = Mathf.Max(carParams.rpmPoints[i], carParams.rpmPoints[0]), pressure = SolveSteadyManifoldPressure(1f, engineRpm, volumetricEfficiency[i]);
                wideOpenManifoldPressure[i] = pressure;
                ComputeTorqueParts(engineRpm, pressure, 1f, out float indicated, out float friction, out float pumping);
                volumetricEfficiency[i] = Mathf.Max(0f, (carParams.torquePoints[i] + friction + pumping) / indicated);
            }
    }

    // @description - bisection for the idle-air bypass area (D) that makes the steady net torque with the pedal released exactly zero at idleRpm
    // @return {Void}
    void SizeIdleBypass()
    {
        float low = 0f, high = 0.02f * wideOpenThrottleArea;
        for (int iteration = 0; iteration < 50; iteration++) { idleBypassAreaValue = 0.5f * (low + high); if (SteadyTorque(0f, carParams.idleRpm) > 0f) high = idleBypassAreaValue; else low = idleBypassAreaValue; }
    }

    // @description - sets the spawn state: idling with combustion, or stopped with the ignition off
    // @param {bool} running - true = idling, false = stopped (ignition off, ambient manifold pressure)
    // @return {Void}
    public void SetRunning(bool running)
    {
        ignition = running; combusting = running; rpm = running ? carParams.idleRpm : 0f; angularVelocity = rpm * Mathf.PI / 30f;
        manifoldPressure = running ? SolveSteadyManifoldPressure(0f, carParams.idleRpm, VolumetricEfficiency(carParams.idleRpm)) : carParams.ambientPressure;
        manifoldPressureRatio = manifoldPressure / carParams.ambientPressure;
    }

    // Starter reduction: ring gear teeth / pinion teeth.
    public float StarterRatio => (float)carParams.ringGearTeeth / carParams.starterPinionTeeth;

    // @description - starter torque at the crankshaft from the linear DC-motor model T_motor = T0 (1 - w/w0), T0 = 4 P / w0 (D, max power at w0/2);
    //   the overrunning clutch means it never gives negative torque
    // @param {float} crankAngularVelocity - crankshaft speed in rad/s
    // @return {float} - starter torque at the crankshaft in N m
    public float StarterTorqueAt(float crankAngularVelocity)
    {
        float noLoadAngularVelocity = carParams.starterNoLoadRpm * Mathf.PI / 30f, stallTorque = 4f * carParams.starterPowerW / noLoadAngularVelocity, ratio = StarterRatio;
        return ratio * stallTorque * Mathf.Max(0f, 1f - crankAngularVelocity * ratio / noLoadAngularVelocity);
    }

    // Human-readable engine state for the HUD.
    public string State => !ignition ? (rpm > 1f ? (starter ? "CRANKING (ign off)" : "OFF (spinning down)") : "OFF") : starter ? (combusting ? "STARTING" : "CRANKING") : combusting ? "RUNNING" : (rpm > 1f ? "STALLING" : "STALLED");

    // Crankshaft speed in rad/s.
    public float Omega => angularVelocity;

    /// firing speed from the model: the lowest crank speed at which combustion with the pedal released (idle bypass only) gives net positive
    /// torque (indicated > friction + pumping, steady MAP), floored at minFiringRpm (the K-Jetronic start-enrichment/spark minimum, same as the starter path).
    public float FireRpm
    {
        get
        {
            if (cachedFireRpm < 0f)
            {
                NetPositiveRpm = -1f;
                for (float testRpm = 10f; testRpm <= 3000f; testRpm += 5f) if (SteadyTorque(0f, testRpm) > 0f) { NetPositiveRpm = testRpm; break; }
                cachedFireRpm = Mathf.Max(carParams.minFiringRpm, NetPositiveRpm < 0f ? carParams.stallRpm : NetPositiveRpm);
            }
            return cachedFireRpm;
        }
    }
    float cachedFireRpm = -1f, previousRpm; bool catching;
    public float NetPositiveRpm { get; private set; }

    // @description - piecewise-linear interpolation, clamped to the end values
    // @param {float[]} xs - ascending sample positions
    // @param {float[]} ys - sample values (same length as xs)
    // @param {float} x - position to evaluate
    // @return {float} - interpolated value
    static float InterpolateLinear(float[] xs, float[] ys, float x)
    {
        if (x <= xs[0]) return ys[0];
        for (int i = 1; i < xs.Length; i++) if (x < xs[i]) return Mathf.Lerp(ys[i - 1], ys[i], (x - xs[i - 1]) / (xs[i] - xs[i - 1]));
        return ys[ys.Length - 1];
    }

    // @description - sourced/estimated wide-open-throttle brake torque curve
    // @param {float} engineRpm - engine speed in rpm
    // @return {float} - WOT brake torque in N m
    public float WotTorque(float engineRpm) => InterpolateLinear(carParams.rpmPoints, carParams.torquePoints, engineRpm);

    // @description - derived volumetric efficiency (cylinder fill relative to manifold density) at an engine speed
    // @param {float} engineRpm - engine speed in rpm
    // @return {float} - VE (dimensionless, around 0.6..0.9)
    public float VolumetricEfficiency(float engineRpm) => InterpolateLinear(carParams.rpmPoints, volumetricEfficiency, engineRpm);

    // ---- throttle body
    /// @description - open flow area of one butterfly at angle plateAngle (from bore-normal), closed angle closedPlateAngle: Heywood (1988) butterfly-area
    ///   formula including the shaft of diameter d (A = pi D^2/4 (1 - cos/cos0 + 2/pi (a/cos sqrt(cos^2 - a^2 cos0^2) - cos/cos0 asin(a cos0/cos) + tail)), a = d/D)
    // @param {float} plateAngle - plate angle from bore-normal in rad
    // @param {float} boreDiameter - throttle bore diameter in m
    // @return {float} - open flow area in m^2 (0 at or below the closed angle)
    float PlateArea(float plateAngle, float boreDiameter)
    {
        float closedPlateAngle = carParams.closedPlateDeg * Mathf.Deg2Rad; if (plateAngle <= closedPlateAngle) return 0f;
        float shaftToBore = carParams.throttleShaftDia / boreDiameter, cosPlate = Mathf.Cos(plateAngle), cosClosed = Mathf.Cos(closedPlateAngle), shaftTerm;
        float tail = -shaftToBore * Mathf.Sqrt(1f - shaftToBore * shaftToBore) + Mathf.Asin(shaftToBore);
        if (cosPlate > shaftToBore * cosClosed) shaftTerm = shaftToBore / cosPlate * Mathf.Sqrt(cosPlate * cosPlate - shaftToBore * shaftToBore * cosClosed * cosClosed) - cosPlate / cosClosed * Mathf.Asin(shaftToBore * cosClosed / cosPlate) + tail; else shaftTerm = tail;
        return Mathf.PI * boreDiameter * boreDiameter / 4f * (1f - cosPlate / cosClosed + 2f / Mathf.PI * shaftTerm);
    }

    // @description - pedal -> plate angles: linear cable lever (E) for the primary plate, progressive link opens the secondary plate late
    // @param {float} pedal - accelerator pedal 0..1
    // @param {float} primary - (out) primary plate angle in rad
    // @param {float} secondary - (out) secondary plate angle in rad
    // @return {Void}
    public void PlateAngles(float pedal, out float primary, out float secondary)
    {
        float closedAngle = carParams.closedPlateDeg * Mathf.Deg2Rad, fullOpenAngle = carParams.fullOpenPlateDeg * Mathf.Deg2Rad, secondaryStartAngle = carParams.secondaryStartDeg * Mathf.Deg2Rad;
        primary = closedAngle + (fullOpenAngle - closedAngle) * Mathf.Clamp01(pedal);
        secondary = closedAngle + (fullOpenAngle - closedAngle) * Mathf.Clamp01((primary - secondaryStartAngle) / (fullOpenAngle - secondaryStartAngle));
    }

    // @description - total throttle flow area: both plates + idle bypass + closed-plate leakage
    // @param {float} pedal - accelerator pedal 0..1
    // @return {float} - total open flow area in m^2
    public float TotalFlowArea(float pedal) { PlateAngles(pedal, out float primaryAngle, out float secondaryAngle); return PlateArea(primaryAngle, carParams.primaryBore) + PlateArea(secondaryAngle, carParams.secondaryBore) + idleBypassAreaValue + closedPlateLeakAreaValue; }

    /// @description - isentropic orifice flow function Psi(pr) = sqrt(2g/(g-1) (pr^(2/g) - pr^((g+1)/g))), held at its choked value below the critical ratio
    // @param {float} pressureRatio - downstream / upstream absolute pressure
    // @return {float} - dimensionless flow function Psi
    public float OrificeFlowFunction(float pressureRatio)
    {
        float gamma = carParams.airGamma, criticalPressureRatio = Mathf.Pow(2f / (gamma + 1f), gamma / (gamma - 1f)); pressureRatio = Mathf.Clamp(pressureRatio, criticalPressureRatio, 1f);
        return Mathf.Sqrt(Mathf.Max(0f, 2f * gamma / (gamma - 1f) * (Mathf.Pow(pressureRatio, 2f / gamma) - Mathf.Pow(pressureRatio, (gamma + 1f) / gamma))));
    }

    // @description - compressible mass flow through the throttle orifice: Cd A p0 / sqrt(R T0) Psi(p_man/p0)
    // @param {float} area - open flow area in m^2
    // @param {float} manifoldPressureNow - manifold absolute pressure in Pa
    // @return {float} - air mass flow into the manifold in kg/s
    float ThrottleMassFlow(float area, float manifoldPressureNow) => carParams.throttleDischargeCoeff * area * carParams.ambientPressure / Mathf.Sqrt(carParams.airGasConstant * carParams.ambientTemperature) * OrificeFlowFunction(manifoldPressureNow / carParams.ambientPressure);

    /// @description - fresh-charge fraction (see header): ideal-cycle residual expansion (r_c - (p_e/p_i)^(1/n)) / (r_c - 1) minus the volume
    ///   of exhaust gas blown back through the valve-overlap curtain, both at manifold pressure. The backflow term is isentropic orifice flow through
    ///   the triangular-lift mean curtain area pi d lift / 2 over the overlap duration. (Kept as ONE expression on purpose: Unity's Mono evaluates
    ///   float expressions in double precision, so splitting it into separate float-returning functions would change the last bits.)
    // @param {float} engineRpm - engine speed in rpm
    // @param {float} manifoldPressureNow - manifold absolute pressure in Pa
    // @return {float} - fraction of the swept volume filled with fresh charge (never below 0)
    public float FreshFraction(float engineRpm, float manifoldPressureNow)
    {
        manifoldPressureNow = Mathf.Max(manifoldPressureNow, 1f); float compressionRatio = carParams.compressionRatio;
        float fraction = (compressionRatio - Mathf.Pow(carParams.exhaustBackPressure / manifoldPressureNow, 1f / carParams.residualExpansionExponent)) / (compressionRatio - 1f);
        if (engineRpm > 0f)
        {
            float overlapTime = carParams.valveOverlapDeg * Mathf.Deg2Rad / (engineRpm * Mathf.PI / 30f), curtainArea = Mathf.PI * carParams.exhaustValveDia * carParams.valveLiftAtTdc / 2f;   // triangular lift -> mean curtain
            float gasConstantTimesTemperature = carParams.airGasConstant * carParams.overlapGasTemperature;
            float backflowMass = carParams.throttleDischargeCoeff * curtainArea * carParams.exhaustBackPressure / Mathf.Sqrt(gasConstantTimesTemperature) * OrificeFlowFunction(Mathf.Min(1f, manifoldPressureNow / carParams.exhaustBackPressure)) * overlapTime;
            fraction -= backflowMass * gasConstantTimesTemperature / manifoldPressureNow / (carParams.displacement / carParams.cylinders);
        }
        return Mathf.Max(0f, fraction);
    }

    // @description - air mass flow drawn by the cylinders: VE x fresh fraction x rho_man x Vd x rpm/120 (four-stroke: one intake per two revolutions)
    // @param {float} engineRpm - engine speed in rpm
    // @param {float} manifoldPressureNow - manifold absolute pressure in Pa
    // @param {float} efficiency - volumetric efficiency VE
    // @return {float} - air mass flow into the cylinders in kg/s
    float CylinderMassFlow(float engineRpm, float manifoldPressureNow, float efficiency) => efficiency * FreshFraction(engineRpm, manifoldPressureNow) * manifoldPressureNow / (carParams.airGasConstant * carParams.ambientTemperature) * carParams.displacement * engineRpm / 120f;

    // @description - bisection for the steady manifold pressure where throttle mass flow equals cylinder mass flow
    // @param {float} pedal - accelerator pedal 0..1
    // @param {float} engineRpm - engine speed in rpm
    // @param {float} efficiency - volumetric efficiency VE
    // @return {float} - steady manifold absolute pressure in Pa
    float SolveSteadyManifoldPressure(float pedal, float engineRpm, float efficiency)
    {
        float area = TotalFlowArea(pedal), low = 1f, high = carParams.ambientPressure;
        for (int iteration = 0; iteration < 50; iteration++) { float middle = 0.5f * (low + high); if (ThrottleMassFlow(area, middle) > CylinderMassFlow(engineRpm, middle, efficiency)) low = middle; else high = middle; }
        return low;
    }

    // ---- torque
    // @description - Chen-Flynn friction mean effective pressure FMEP = A + B Pmax + C N + D N^2 (N in krpm; Pmax scaled by MAP)
    // @param {float} engineRpm - engine speed in rpm
    // @param {float} manifoldPressureNow - manifold absolute pressure in Pa
    // @return {float} - friction mean effective pressure in Pa
    float FrictionMeanEffectivePressure(float engineRpm, float manifoldPressureNow) { float kiloRpm = engineRpm / 1000f; return carParams.fmepA + carParams.fmepB * carParams.peakCylPressureWot * manifoldPressureNow / carParams.ambientPressure + carParams.fmepCPerKrpm * kiloRpm + carParams.fmepDPerKrpm2 * kiloRpm * kiloRpm; }

    // @description - splits the engine torque into indicated, friction and extra pumping parts. Torque = mean effective pressure x Vd / (4 pi);
    //   indicated = eta_i m_air_per_cycle LHV / AFR / (4 pi); pumping = (p_man,WOT(rpm) - p_man) Vd / (4 pi) (extra throttling loss below the WOT manifold pressure only)
    // @param {float} engineRpm - engine speed in rpm
    // @param {float} manifoldPressureNow - manifold absolute pressure in Pa
    // @param {float} efficiency - volumetric efficiency VE
    // @param {float} indicated - (out) gross indicated torque in N m
    // @param {float} friction - (out) friction torque in N m (positive = drag)
    // @param {float} pumping - (out) extra pumping torque in N m (positive = drag)
    // @return {Void}
    void ComputeTorqueParts(float engineRpm, float manifoldPressureNow, float efficiency, out float indicated, out float friction, out float pumping)
    {
        float torquePerPressure = carParams.displacement / (4f * Mathf.PI), airMassPerCycle = efficiency * FreshFraction(engineRpm, manifoldPressureNow) * manifoldPressureNow / (carParams.airGasConstant * carParams.ambientTemperature) * carParams.displacement;
        indicated = carParams.indicatedEfficiency * airMassPerCycle * carParams.fuelLhv / carParams.airFuelRatio / (4f * Mathf.PI);
        friction = FrictionMeanEffectivePressure(engineRpm, manifoldPressureNow) * torquePerPressure; pumping = (InterpolateLinear(carParams.rpmPoints, wideOpenManifoldPressure, engineRpm) - manifoldPressureNow) * torquePerPressure;
    }

    /// @description - friction + pumping drag at the current manifold pressure (no combustion: VE = 0)
    // @param {float} engineRpm - engine speed in rpm
    // @return {float} - drag torque in N m (positive number)
    public float FrictionTorque(float engineRpm) { ComputeTorqueParts(engineRpm, manifoldPressure, 0f, out _, out float friction, out float pumping); return friction + pumping; }

    // @description - steady-state net engine torque at a fixed pedal and speed (manifold pressure settled)
    // @param {float} pedal - accelerator pedal 0..1
    // @param {float} engineRpm - engine speed in rpm
    // @return {float} - indicated - friction - pumping in N m
    public float SteadyTorque(float pedal, float engineRpm) { float efficiency = VolumetricEfficiency(engineRpm), pressure = SolveSteadyManifoldPressure(pedal, engineRpm, efficiency); ComputeTorqueParts(engineRpm, pressure, efficiency, out float indicated, out float friction, out float pumping); return indicated - friction - pumping; }

    // @description - steady-state manifold pressure as a fraction of ambient at a fixed pedal and speed
    // @param {float} pedal - accelerator pedal 0..1
    // @param {float} engineRpm - engine speed in rpm
    // @return {float} - manifold pressure / ambient pressure
    public float SteadyMapRatio(float pedal, float engineRpm) => SolveSteadyManifoldPressure(pedal, engineRpm, VolumetricEfficiency(engineRpm)) / carParams.ambientPressure;

    // @description - advances the manifold filling, the combustion state and the crankshaft by one time step.
    //   Manifold (isothermal): dp/dt = R T / V (mdot_throttle - mdot_cylinders). Crankshaft: J dw/dt = T_net + T_starter - T_clutch.
    // @param {float} pedal - accelerator pedal 0..1
    // @param {float} clutchTorque - torque the clutch takes off the crankshaft in N m
    // @param {float} dt - time step in s
    // @return {float} - net engine torque (before starter and clutch) in N m
    public float Step(float pedal, float clutchTorque, float dt)
    {
        throttleArea = TotalFlowArea(pedal) / wideOpenThrottleArea;
        float efficiency = VolumetricEfficiency(rpm), massFlowIn = ThrottleMassFlow(throttleArea * wideOpenThrottleArea, manifoldPressure), massFlowOut = CylinderMassFlow(rpm, manifoldPressure, efficiency);
        manifoldPressure = Mathf.Clamp(manifoldPressure + carParams.airGasConstant * carParams.ambientTemperature / carParams.manifoldVolume * (massFlowIn - massFlowOut) * dt, 1f, carParams.ambientPressure);
        manifoldPressureRatio = manifoldPressure / carParams.ambientPressure;
        UpdateCombustionState();
        limiter = rpm > carParams.revLimit;   // fuel-pump-relay cut (S: DX 6500-6900 rpm): no combustion, air still pumped
        ComputeTorqueParts(rpm, manifoldPressure, efficiency, out float indicated, out float friction, out float pumping);
        float netTorque = (combusting && !limiter ? indicated : 0f) - friction - pumping;
        starterTorque = starter ? StarterTorqueAt(angularVelocity) : 0f;
        float effectiveInertia = carParams.engineInertia + (starter ? carParams.starterArmatureInertia * StarterRatio * StarterRatio : 0f);
        angularVelocity = Mathf.Max(0f, angularVelocity + (netTorque + starterTorque - clutchTorque) / effectiveInertia * dt);
        previousRpm = rpm; rpm = angularVelocity * 30f / Mathf.PI; return netTorque;
    }

    // @description - decides whether combustion happens this step: fuel + spark only with ignition on. A stopped/stalling crank fires once it turns at
    //   the engine's firing speed, whatever drives it (the starter, or the wheels through an engaged/slipping clutch: bump/roll start).
    //   Hysteresis: a RUNNING engine dies below stallRpm (unless the starter holds it). A non-firing crank fires when driven up through FireRpm
    //   (from below), then keeps firing while it catches (until stallRpm is reached; fails if it falls back below FireRpm).
    // @return {Void} - updates combusting and the internal catching flag
    void UpdateCombustionState()
    {
        bool crossedFiringSpeedUpwards = previousRpm < FireRpm && rpm >= FireRpm;
        if (!ignition) { combusting = false; catching = false; }
        else if (combusting) { if (catching && (rpm >= carParams.stallRpm || rpm < FireRpm)) catching = false; combusting = rpm >= carParams.stallRpm || (starter && rpm >= carParams.minFiringRpm) || catching; }
        else { combusting = rpm >= carParams.stallRpm || (starter && rpm >= FireRpm) || crossedFiringSpeedUpwards; catching = combusting && rpm < carParams.stallRpm; }
    }
}
