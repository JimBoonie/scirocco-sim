using UnityEngine;
// Clutch + 5-speed + reverse + open differential (equal torque split) for FWD.
// Clutch: Coulomb-capped coupling; its gain is chosen per step from the coupled inertias so the
// explicit integration is stable (acts as a "lock" when slip is small, slips when torque > capacity).
public class Drivetrain
{
    readonly CarParams carParams;
    public int gear = 0;                  // -1 = reverse, 0 = neutral, 1..5 = forward gears
    public float shiftTimer;              // s remaining in neutral after a shift (no torque transmitted)
    public float clutchSlip;              // rad/s, engine speed minus the clutch's wheel-side speed (engine side faster = positive)

    // @description - creates the drivetrain for a car; starts in neutral
    // @param {CarParams} carParams - gear ratios, final drive, clutch capacity, inertias, efficiency
    // @return {Void} - constructor
    public Drivetrain(CarParams carParams) { this.carParams = carParams; }

    // Overall ratio crankshaft : wheel for the engaged gear (gear ratio x final drive); 0 in neutral, negative in reverse.
    public float TotalRatio => gear == 0 ? 0f : (gear < 0 ? -carParams.reverseRatio : carParams.gearRatios[gear - 1]) * carParams.finalDrive;

    // @description - moves the gear selector up or down by a number of gears and starts the shift (neutral) timer if the gear changed
    // @param {int} gearChange - +1 = up, -1 = down
    // @return {Void}
    public void Shift(int gearChange)
    {
        int newGear = Mathf.Clamp(gear + gearChange, -1, carParams.gearRatios.Length);
        if (newGear != gear) { gear = newGear; shiftTimer = carParams.shiftTime; }
    }

    // @description - selects an absolute gear (H-pattern shifter) and starts the shift timer if the gear changed
    // @param {int} targetGear - -1 = reverse, 0 = neutral, 1..5 = forward
    // @return {Void}
    public void SelectGear(int targetGear)
    {
        targetGear = Mathf.Clamp(targetGear, -1, carParams.gearRatios.Length);
        if (targetGear != gear) { gear = targetGear; shiftTimer = carParams.shiftTime; }
    }

    // @description - Coulomb-capped clutch with a per-step stable coupling gain. Also updates clutchSlip.
    //   Capacity = clutchMaxTorque x (1 - pedal). Gain k = 0.5 / (dt x (1/J_engine + R^2 x efficiency / (2 J_wheel))) [N m s/rad],
    //   i.e. the coupling that would remove the slip in two explicit steps (critically sized), so it never overshoots.
    // @param {float} engineAngularVelocity - crankshaft speed in rad/s
    // @param {float} wheelAngularVelocityAverage - mean driven-wheel speed in rad/s
    // @param {float} clutchPedal - clutch pedal position 0 (engaged) .. 1 (fully in)
    // @param {float} dt - time step in s
    // @param {float} wheelInertiaEach - rotational inertia of one driven wheel in kg m^2
    // @param {float} wheelTorqueEach - (out) drive torque delivered to each driven wheel in N m (open differential: equal split)
    // @return {float} - torque the clutch takes off the crankshaft in N m (0 in neutral or while shifting)
    public float Clutch(float engineAngularVelocity, float wheelAngularVelocityAverage, float clutchPedal, float dt, float wheelInertiaEach, out float wheelTorqueEach)
    {
        wheelTorqueEach = 0f; float totalRatio = TotalRatio;
        if (gear == 0 || shiftTimer > 0f) { clutchSlip = 0f; return 0f; }
        clutchSlip = engineAngularVelocity - wheelAngularVelocityAverage * totalRatio;
        float clutchCapacity = carParams.clutchMaxTorque * Mathf.Clamp01(1f - clutchPedal);
        float inverseReflectedInertia = 1f / carParams.engineInertia + totalRatio * totalRatio * carParams.drivetrainEff / (2f * wheelInertiaEach);
        float couplingGain = 0.5f / (dt * inverseReflectedInertia);                         // critically sized coupling gain
        float clutchTorque = Mathf.Clamp(clutchSlip * couplingGain, -clutchCapacity, clutchCapacity);
        wheelTorqueEach = clutchTorque * totalRatio * carParams.drivetrainEff * 0.5f;       // open diff: equal split
        return clutchTorque;
    }

    // @description - counts the shift timer down
    // @param {float} dt - time step in s
    // @return {Void}
    public void Tick(float dt) { if (shiftTimer > 0f) shiftTimer -= dt; }
}
