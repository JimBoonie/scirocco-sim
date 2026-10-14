using UnityEngine;
// Physical air-path engine model (after N. Heusinkveld's critique of the "lerp between WOT and engine-braking curves" throttle model;
// sources and tags in CarParams and diagnostics/throttle_model/SOURCES.md).
//  * Pedal -> cable -> primary butterfly angle (linear lever, E); a progressive link opens the secondary plate late (two-stage CIS body).
//  * Open area of each plate: Heywood (1988) butterfly-area formula incl. shaft; plus the idle-air bypass (sized so idle is a steady state).
//  * Throttle mass flow: compressible isentropic orifice, Cd * A * p0 / sqrt(R T0) * Psi(p_man/p0), choked below the critical ratio.
//  * Cylinder air demand: VE(rpm) * rho_man * Vd * rpm/120 (four-stroke). VE(rpm) is DERIVED so the steady WOT brake torque equals the sourced curve.
//  * Manifold: isothermal filling/emptying dp/dt = R T / V * (mdot_throttle - mdot_cylinders).
//  * Indicated torque = eta_i * m_air/cycle * LHV / AFR / (4 pi);  friction: Chen-Flynn FMEP;  pumping: (p_amb - p_man) * Vd / (4 pi).
//  => the torque at a fixed pedal falls with rpm (the cylinders out-pump a fixed throttle area), so part-throttle curves slope down
//     and cross zero; at low rpm a small opening already fills the manifold.
public class Engine
{
    readonly CarParams p; public float rpm, map = 1f, throttleArea, starterTorque; float omega, pMan;   // map = p_man / p_amb (HUD/audio); throttleArea = open area / WOT area
    public bool ignition = true, starter, combusting = true, limiter;   // limiter = fuel cut active this step   // starter = key held in START; combusting = combustion this step
    readonly float[] ve; float aWot, aBypass;
    public float IdleBypassArea => aBypass;
    public Engine(CarParams p)
    {
        this.p = p; ve = new float[p.rpmPoints.Length];
        aWot = PlateArea(p.fullOpenPlateDeg * Mathf.Deg2Rad, p.primaryBore) + PlateArea(p.fullOpenPlateDeg * Mathf.Deg2Rad, p.secondaryBore);
        for (int i = 0; i < ve.Length; i++) ve[i] = 1f;
        for (int k = 0; k < 40; k++)   // VE(rpm) fixed point: steady WOT brake torque == sourced curve (D)
            for (int i = 0; i < ve.Length; i++)
            {
                float r = Mathf.Max(p.rpmPoints[i], p.rpmPoints[0]), pm = SteadyMap(1f, r, ve[i]); Parts(r, pm, 1f, out float ti, out float tf, out float tp);
                ve[i] = Mathf.Max(0f, (p.torquePoints[i] + tf + tp) / ti);
            }
        float lo = 0f, hi = 0.02f * aWot;   // idle bypass (D): closed-pedal steady net torque = 0 at idleRpm
        for (int k = 0; k < 50; k++) { aBypass = 0.5f * (lo + hi); if (SteadyTorque(0f, p.idleRpm) > 0f) hi = aBypass; else lo = aBypass; }
        SetRunning(true);
    }
    public void SetRunning(bool running)   // spawn state: idling, or stopped with ignition off
    { ignition = running; combusting = running; rpm = running ? p.idleRpm : 0f; omega = rpm * Mathf.PI / 30f; pMan = running ? SteadyMap(0f, p.idleRpm, VE(p.idleRpm)) : p.ambientPressure; map = pMan / p.ambientPressure; }
    public float StarterRatio => (float)p.ringGearTeeth / p.starterPinionTeeth;
    public float StarterTorqueAt(float engOmega)   // at the crankshaft
    {
        float w0 = p.starterNoLoadRpm * Mathf.PI / 30f, T0 = 4f * p.starterPowerW / w0, N = StarterRatio;
        return N * T0 * Mathf.Max(0f, 1f - engOmega * N / w0);
    }
    public string State => !ignition ? (rpm > 1f ? (starter ? "CRANKING (ign off)" : "OFF (spinning down)") : "OFF") : starter ? (combusting ? "STARTING" : "CRANKING") : combusting ? "RUNNING" : (rpm > 1f ? "STALLING" : "STALLED");
    public float Omega => omega;
    /// firing speed from the model: the lowest crank speed at which combustion with the pedal released (idle bypass only) gives net positive
    /// torque (indicated > friction + pumping, steady MAP), floored at minFiringRpm (the K-Jetronic start-enrichment/spark minimum, same as the starter path).
    public float FireRpm { get { if (fireRpm < 0f) { NetPositiveRpm = -1f; for (float r = 10f; r <= 3000f; r += 5f) if (SteadyTorque(0f, r) > 0f) { NetPositiveRpm = r; break; } fireRpm = Mathf.Max(p.minFiringRpm, NetPositiveRpm < 0f ? p.stallRpm : NetPositiveRpm); } return fireRpm; } }
    float fireRpm = -1f, prevRpm; bool catching; public float NetPositiveRpm { get; private set; }
    static float Interp(float[] x, float[] y, float r)
    {
        if (r <= x[0]) return y[0];
        for (int i = 1; i < x.Length; i++) if (r < x[i]) return Mathf.Lerp(y[i - 1], y[i], (r - x[i - 1]) / (x[i] - x[i - 1]));
        return y[y.Length - 1];
    }
    public float WotTorque(float r) => Interp(p.rpmPoints, p.torquePoints, r);
    public float VE(float r) => Interp(p.rpmPoints, ve, r);
    // ---- throttle body
    /// open flow area of one butterfly at angle psi (from bore-normal), closed angle psi0: Heywood (1988) eq. 7.? incl. shaft diameter d.
    float PlateArea(float psi, float D)
    {
        float psi0 = p.closedPlateDeg * Mathf.Deg2Rad; if (psi <= psi0) return 0f;
        float a = p.throttleShaftDia / D, c = Mathf.Cos(psi), c0 = Mathf.Cos(psi0), t;
        float tail = -a * Mathf.Sqrt(1f - a * a) + Mathf.Asin(a);
        if (c > a * c0) t = a / c * Mathf.Sqrt(c * c - a * a * c0 * c0) - c / c0 * Mathf.Asin(a * c0 / c) + tail; else t = tail;
        return Mathf.PI * D * D / 4f * (1f - c / c0 + 2f / Mathf.PI * t);
    }
    public void PlateAngles(float pedal, out float primary, out float secondary)   // rad; linear cable lever (E), progressive secondary
    {
        float psi0 = p.closedPlateDeg * Mathf.Deg2Rad, psiW = p.fullOpenPlateDeg * Mathf.Deg2Rad, s0 = p.secondaryStartDeg * Mathf.Deg2Rad;
        primary = psi0 + (psiW - psi0) * Mathf.Clamp01(pedal);
        secondary = psi0 + (psiW - psi0) * Mathf.Clamp01((primary - s0) / (psiW - s0));
    }
    public float Area(float pedal) { PlateAngles(pedal, out float a1, out float a2); return PlateArea(a1, p.primaryBore) + PlateArea(a2, p.secondaryBore) + aBypass; }
    /// isentropic orifice flow function Psi(pr) = sqrt(2g/(g-1) (pr^(2/g) - pr^((g+1)/g))), held at its choked value below the critical ratio
    public float Psi(float pr)
    {
        float g = p.airGamma, crit = Mathf.Pow(2f / (g + 1f), g / (g - 1f)); pr = Mathf.Clamp(pr, crit, 1f);
        return Mathf.Sqrt(Mathf.Max(0f, 2f * g / (g - 1f) * (Mathf.Pow(pr, 2f / g) - Mathf.Pow(pr, (g + 1f) / g))));
    }
    float ThrottleFlow(float area, float pm) => p.throttleDischargeCoeff * area * p.ambientPressure / Mathf.Sqrt(p.airGasConstant * p.ambientTemperature) * Psi(pm / p.ambientPressure);
    float CylinderFlow(float r, float pm, float v) => v * pm / (p.airGasConstant * p.ambientTemperature) * p.displacement * r / 120f;
    float SteadyMap(float pedal, float r, float v)   // bisection on mdot_throttle = mdot_cylinders
    {
        float A = Area(pedal), lo = 1f, hi = p.ambientPressure;
        for (int i = 0; i < 50; i++) { float m = 0.5f * (lo + hi); if (ThrottleFlow(A, m) > CylinderFlow(r, m, v)) lo = m; else hi = m; }
        return lo;
    }
    // ---- torque
    float Fmep(float r, float pm) { float n = r / 1000f; return p.fmepA + p.fmepB * p.peakCylPressureWot * pm / p.ambientPressure + p.fmepCPerKrpm * n + p.fmepDPerKrpm2 * n * n; }
    void Parts(float r, float pm, float v, out float ind, out float fric, out float pump)
    {
        float k = p.displacement / (4f * Mathf.PI), mCycle = v * pm / (p.airGasConstant * p.ambientTemperature) * p.displacement;
        ind = p.indicatedEfficiency * mCycle * p.fuelLhv / p.airFuelRatio / (4f * Mathf.PI);
        fric = Fmep(r, pm) * k; pump = (p.ambientPressure - pm) * k;
    }
    /// friction + pumping drag (positive number) at the current manifold pressure
    public float FrictionTorque(float r) { Parts(r, pMan, 0f, out _, out float f, out float pu); return f + pu; }
    public float SteadyTorque(float pedal, float r) { float v = VE(r), pm = SteadyMap(pedal, r, v); Parts(r, pm, v, out float i, out float f, out float pu); return i - f - pu; }
    public float SteadyMapRatio(float pedal, float r) => SteadyMap(pedal, r, VE(r)) / p.ambientPressure;
    public float Step(float pedal, float clutchTorque, float dt)
    {
        throttleArea = Area(pedal) / aWot;
        float v = VE(rpm), mIn = ThrottleFlow(throttleArea * aWot, pMan), mOut = CylinderFlow(rpm, pMan, v);
        pMan = Mathf.Clamp(pMan + p.airGasConstant * p.ambientTemperature / p.manifoldVolume * (mIn - mOut) * dt, 1f, p.ambientPressure);
        map = pMan / p.ambientPressure;
        // fuel + spark only with ignition on. A stopped/stalling crank fires once it turns at the engine's firing speed, whatever drives it:
        // the starter, or the wheels through an engaged/slipping clutch (bump/roll start). Firing speed = FireRpm (derived below).
        // hysteresis: a RUNNING engine dies below stallRpm (unless the starter holds it). A non-firing crank fires when driven up through FireRpm
        // (from below), then keeps firing while it catches (until stallRpm is reached; fails if it falls back below FireRpm).
        bool crossedUp = prevRpm < FireRpm && rpm >= FireRpm;
        if (!ignition) { combusting = false; catching = false; }
        else if (combusting) { if (catching && (rpm >= p.stallRpm || rpm < FireRpm)) catching = false; combusting = rpm >= p.stallRpm || (starter && rpm >= p.minFiringRpm) || catching; }
        else { combusting = rpm >= p.stallRpm || (starter && rpm >= FireRpm) || crossedUp; catching = combusting && rpm < p.stallRpm; }
        limiter = rpm > p.revLimit;   // fuel-pump-relay cut (S: DX 6500-6900 rpm): no combustion, air still pumped
        Parts(rpm, pMan, v, out float ind, out float fr, out float pu);
        float T = (combusting && !limiter ? ind : 0f) - fr - pu;
        starterTorque = starter ? StarterTorqueAt(omega) : 0f;
        float J = p.engineInertia + (starter ? p.starterArmatureInertia * StarterRatio * StarterRatio : 0f);
        omega = Mathf.Max(0f, omega + (T + starterTorque - clutchTorque) / J * dt);
        prevRpm = rpm; rpm = omega * 30f / Mathf.PI; return T;
    }
}
