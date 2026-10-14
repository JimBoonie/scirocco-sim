using UnityEngine;
// Clutch + 5-speed + reverse + open differential (equal torque split) for FWD.
// Clutch: Coulomb-capped coupling; its gain is chosen per step from the coupled inertias so the
// explicit integration is stable (acts as a "lock" when slip is small, slips when torque > capacity).
public class Drivetrain
{
    readonly CarParams p; public int gear = 0;      // -1 R, 0 N, 1..5
    public float shiftTimer, clutchSlip;
    public Drivetrain(CarParams p) { this.p = p; }
    public float Ratio => gear == 0 ? 0f : (gear < 0 ? -p.reverseRatio : p.gearRatios[gear - 1]) * p.finalDrive;
    public void Shift(int d)
    {
        int ng = Mathf.Clamp(gear + d, -1, p.gearRatios.Length);
        if (ng != gear) { gear = ng; shiftTimer = p.shiftTime; }
    }
    public void SelectGear(int g) { g = Mathf.Clamp(g, -1, p.gearRatios.Length); if (g != gear) { gear = g; shiftTimer = p.shiftTime; } }   // H-pattern
    public float Clutch(float engOmega, float wheelOmegaAvg, float clutchPedal, float dt, float wheelInertiaEach, out float wheelTorqueEach)
    {
        wheelTorqueEach = 0f; float R = Ratio;
        if (gear == 0 || shiftTimer > 0f) { clutchSlip = 0f; return 0f; }
        clutchSlip = engOmega - wheelOmegaAvg * R;
        float cap = p.clutchMaxTorque * Mathf.Clamp01(1f - clutchPedal);
        float invI = 1f / p.engineInertia + R * R * p.drivetrainEff / (2f * wheelInertiaEach);
        float k = 0.5f / (dt * invI);                         // critically sized coupling gain
        float Tc = Mathf.Clamp(clutchSlip * k, -cap, cap);
        wheelTorqueEach = Tc * R * p.drivetrainEff * 0.5f;    // open diff: equal split
        return Tc;
    }
    public void Tick(float dt) { if (shiftTimer > 0f) shiftTimer -= dt; }
}
