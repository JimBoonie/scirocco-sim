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

    // Number of driven wheels sharing the drive torque through the open differential (FWD: the two front wheels, equal split).
    const int DrivenWheelCount = 2;
    // Fraction of the slip the clutch coupling removes per explicit step at its critically sized gain (0.5 = the slip is gone in two steps without overshoot).
    const float CouplingSlipRemovalPerStep = 0.5f;

    // @description - factor by which the gearbox and final drive scale torque on the way from the crankshaft side to the driven wheels, from the power flow direction.
    //   The gears lose a fraction of the power they transmit (efficiency eta < 1), whichever way the power flows:
    //   - Power (engine drives the wheels, torque on the crank > 0): P_wheels = eta x P_engine, so wheel torque = engine torque x ratio x eta (the loss reduces the wheel torque).
    //   - Overrun (wheels drive the engine, engine braking, torque on the crank < 0): P_engine_absorbed = eta x P_wheels, so the wheels must supply MORE than the engine
    //     absorbs: wheel braking torque = engine torque x ratio / eta. Multiplying by eta here (the old behavior) would let the wheels give up only eta x the power the engine
    //     absorbs, i.e. create (1 / eta - 1) = 11.1 % of the wheels' power (10 % of the engine's absorbed power) out of nothing at eta = 0.90.
    //   Engine friction and pumping losses are already inside the Engine model (they set the torque the engine absorbs), so this factor counts only the gear mesh and bearing loss.
    // @param {float} crankTorque - torque the clutch takes off the crankshaft in N m (positive = power flows engine to wheels, negative = overrun)
    // @return {float} - dimensionless torque scale from crank side to wheel side (eta on power, 1 / eta on overrun); gear ratio not included
    public float TorqueScaleFromCrankSide(float crankTorque)
    {
        float efficiency = Mathf.Max(carParams.drivetrainEfficiency, Mathf.Epsilon);   // guards the division on overrun
        return crankTorque >= 0f ? efficiency : 1f / efficiency;
    }

    // @description - torque delivered to each driven wheel for a given clutch torque, with the efficiency applied in the direction of the power flow (see TorqueScaleFromCrankSide).
    //   Open differential: equal split between the driven wheels. Sign: positive drives the car forward in a forward gear; on overrun it is a braking torque.
    // @param {float} crankTorque - torque the clutch takes off the crankshaft in N m (positive = power, negative = overrun)
    // @return {float} - torque on each driven wheel in N m (0 in neutral)
    public float WheelTorqueEach(float crankTorque) => crankTorque * TotalRatio * TorqueScaleFromCrankSide(crankTorque) / DrivenWheelCount;

    // @description - Coulomb-capped clutch with a per-step stable coupling gain. Also updates clutchSlip.
    //   Capacity = clutchMaxTorque x (1 - pedal). Gain k = CouplingSlipRemovalPerStep (0.5) / (dt x (1/J_engine + R^2 x s / (2 J_wheel))) [N m s/rad], where s is the torque scale of the current power flow
    //   direction (eta on power, 1 / eta on overrun: the wheels react to a crank torque change with ratio x s), i.e. the coupling that would remove the slip in two explicit
    //   steps (critically sized), so it never overshoots.
    // @param {float} engineAngularVelocity - crankshaft speed in rad/s
    // @param {float} wheelAngularVelocityAverage - mean driven-wheel speed in rad/s
    // @param {float} clutchPedal - clutch pedal position 0 (engaged) .. 1 (fully in)
    // @param {float} dt - time step in s
    // @param {float} wheelInertiaEach - rotational inertia of one driven wheel in kg m^2
    // @param {float} wheelTorqueEach - (out) drive torque delivered to each driven wheel in N m (open differential: equal split); negative values are overrun braking
    // @return {float} - torque the clutch takes off the crankshaft in N m (0 in neutral or while shifting)
    public float Clutch(float engineAngularVelocity, float wheelAngularVelocityAverage, float clutchPedal, float dt, float wheelInertiaEach, out float wheelTorqueEach)
    {
        wheelTorqueEach = 0f; float totalRatio = TotalRatio;
        if (gear == 0 || shiftTimer > 0f) { clutchSlip = 0f; return 0f; }
        clutchSlip = engineAngularVelocity - wheelAngularVelocityAverage * totalRatio;
        float clutchCapacity = carParams.clutchMaxTorque * Mathf.Clamp01(1f - clutchPedal);
        float torqueScale = TorqueScaleFromCrankSide(clutchSlip);                           // the clutch torque has the sign of the slip, so the slip gives the power flow direction
        float inverseReflectedInertia = 1f / carParams.engineInertia + totalRatio * totalRatio * torqueScale / (DrivenWheelCount * wheelInertiaEach);
        float couplingGain = CouplingSlipRemovalPerStep / (dt * inverseReflectedInertia);                         // critically sized coupling gain
        float clutchTorque = Mathf.Clamp(clutchSlip * couplingGain, -clutchCapacity, clutchCapacity);
        wheelTorqueEach = WheelTorqueEach(clutchTorque);
        return clutchTorque;
    }

    // @description - counts the shift timer down
    // @param {float} dt - time step in s
    // @return {Void}
    public void Tick(float dt) { if (shiftTimer > 0f) shiftTimer -= dt; }
}
