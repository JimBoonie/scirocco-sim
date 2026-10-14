using UnityEngine;
// Engine with a manifold-pressure throttle model (after N. Heusinkveld's critique of the linear
// "lerp between WOT and engine-braking curve" model used by many sims).
//  * Pedal -> butterfly angle -> effective area A = 1 - cos(theta) (+ small leak/idle bypass).
//  * Air in through the throttle: Cthr * A * Phi(MAP/Patm)  (compressible orifice, choked below pr 0.528).
//  * Air pumped by cylinders: ~ rpm * MAP (volumetric).
//  * MAP integrates the difference (manifold filling lag ~ tens of ms).
//  => At LOW rpm a small opening already fills the manifold (near WOT torque);
//     at HIGH rpm the same opening gives low MAP -> little or negative torque. Part-throttle curves slope down.
//  * Torque = (WOT + friction) * (MAP - P0)/(1 - P0) - friction - pumping(1 - MAP).
public class Engine
{
    readonly CarParams p; public float rpm, map = 1f, throttleArea, starterTorque; float omega;
    public bool ignition = true, starter, combusting = true, limiter;   // limiter = fuel cut active this step   // starter = key held in START; combusting = combustion this step
    public Engine(CarParams p) { this.p = p; SetRunning(true); }
    public void SetRunning(bool running)   // spawn state: idling, or stopped with ignition off
    { ignition = running; combusting = running; rpm = running ? p.idleRpm : 0f; omega = rpm * Mathf.PI / 30f; map = running ? 0.4f : 1f; }
    public float StarterRatio => (float)p.ringGearTeeth / p.starterPinionTeeth;
    public float StarterTorqueAt(float engOmega)   // at the crankshaft
    {
        float w0 = p.starterNoLoadRpm * Mathf.PI / 30f, T0 = 4f * p.starterPowerW / w0, N = StarterRatio;
        return N * T0 * Mathf.Max(0f, 1f - engOmega * N / w0);
    }
    public string State => !ignition ? (rpm > 1f ? (starter ? "CRANKING (ign off)" : "OFF (spinning down)") : "OFF") : starter ? (combusting ? "STARTING" : "CRANKING") : combusting ? "RUNNING" : (rpm > 1f ? "STALLING" : "STALLED");
    public float Omega => omega;
    /// firing speed from the model: the lowest crank speed at which combustion at the idle-bypass opening (closed pedal) gives net positive
    /// torque (indicated > friction + pumping, steady MAP), floored at minFiringRpm (the K-Jetronic start-enrichment/spark minimum, same as the starter path).
    public float FireRpm { get { if (fireRpm < 0f) { NetPositiveRpm = -1f; for (float r = 10f; r <= 3000f; r += 5f) if (SteadyTorque(p.idleMaxPedal, r) > 0f) { NetPositiveRpm = r; break; } fireRpm = Mathf.Max(p.minFiringRpm, NetPositiveRpm < 0f ? p.stallRpm : NetPositiveRpm); } return fireRpm; } }
    float fireRpm = -1f, prevRpm; bool catching; public float NetPositiveRpm { get; private set; }
    public float WotTorque(float r)
    {
        var x = p.rpmPoints; var y = p.torquePoints;
        if (r <= x[0]) return y[0];
        for (int i = 1; i < x.Length; i++) if (r < x[i]) return Mathf.Lerp(y[i - 1], y[i], (r - x[i - 1]) / (x[i] - x[i - 1]));
        return y[y.Length - 1];
    }
    public float FrictionTorque(float r) => p.frictionTorqueBase + p.frictionTorquePerKrpm * r / 1000f;
    public static float Phi(float pr)
    {
        const float crit = 0.528f; if (pr <= crit) return 1f;
        float u = Mathf.Clamp01((pr - crit) / (1f - crit)); return Mathf.Sqrt(Mathf.Max(1f - u * u, 0f));
    }
    public float Area(float pedal) => p.throttleLeakArea + (1f - p.throttleLeakArea) * (1f - Mathf.Cos(Mathf.Clamp01(pedal) * Mathf.PI * 0.5f));
    // steady-state helper torque at a given pedal/rpm (used for HUD/tests)
    public float SteadyTorque(float pedal, float r)
    {
        float m = 1f; for (int i = 0; i < 200; i++) m += 0.05f * (p.throttleFlowCoeff * Area(pedal) * Phi(m) - r / p.revLimit * m);
        return Torque(Mathf.Clamp(m, 0.05f, 1f), r);
    }
    float Torque(float m, float r)
    {
        float fr = FrictionTorque(r); float p0 = p.mapZeroTorque;
        float ind = (WotTorque(r) + fr) * Mathf.Clamp01((m - p0) / (1f - p0));
        return ind - fr - p.pumpingTorque * (1f - m);
    }
    public float Step(float pedal, float clutchTorque, float dt)
    {
        // idle governor: opens bypass when rpm sags
        float idle = ignition && rpm < p.idleRpm + 100f ? Mathf.Clamp01((p.idleRpm + 100f - rpm) / 300f) * p.idleMaxPedal : 0f;
        float ped = Mathf.Max(pedal, idle);
        limiter = rpm > p.revLimit; if (limiter) ped = 0f;                                         // fuel cut approximated as closed throttle
        throttleArea = Area(ped);
        float inflow = p.throttleFlowCoeff * throttleArea * Phi(map), outflow = Mathf.Max(rpm, 1f) / p.revLimit * map;
        map = Mathf.Clamp(map + p.manifoldRate * (inflow - outflow) * dt, 0.05f, 1f);
        // fuel + spark only with ignition on. A stopped/stalling crank fires once it turns at the engine's firing speed, whatever drives it:
        // the starter, or the wheels through an engaged/slipping clutch (bump/roll start). Firing speed = FireRpm (derived below).
        // hysteresis: a RUNNING engine dies below stallRpm (unless the starter holds it). A non-firing crank fires when driven up through FireRpm
        // (from below), then keeps firing while it catches (until stallRpm is reached; fails if it falls back below FireRpm).
        bool crossedUp = prevRpm < FireRpm && rpm >= FireRpm;
        if (!ignition) { combusting = false; catching = false; }
        else if (combusting) { if (catching && (rpm >= p.stallRpm || rpm < FireRpm)) catching = false; combusting = rpm >= p.stallRpm || (starter && rpm >= p.minFiringRpm) || catching; }
        else { combusting = rpm >= p.stallRpm || (starter && rpm >= FireRpm) || crossedUp; catching = combusting && rpm < p.stallRpm; }
        float T = combusting ? Torque(map, rpm) : -FrictionTorque(rpm);
        if (rpm > p.revLimit) T = Mathf.Min(T, -FrictionTorque(rpm));
        starterTorque = starter ? StarterTorqueAt(omega) : 0f;
        float J = p.engineInertia + (starter ? p.starterArmatureInertia * StarterRatio * StarterRatio : 0f);
        omega = Mathf.Max(0f, omega + (T + starterTorque - clutchTorque) / J * dt);
        prevRpm = rpm; rpm = omega * 30f / Mathf.PI; return T;
    }
}
