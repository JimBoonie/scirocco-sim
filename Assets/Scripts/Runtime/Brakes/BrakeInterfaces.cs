// Small interface between the car and the EXPERIMENTAL brake system, so the brake model can be moved or replaced (for example by an ABS or a thermal model) without touching the car code.
// The car hands in the pedal and the manifold state, and gets back the four brake torques and the air mass the booster check valve returned to the intake manifold.

// @description - fault state of the vacuum booster, all zero (= healthy) by default; set it to simulate a leak, a pinched vacuum hose or a stuck check valve
public struct BoosterFault
{
    public double extraLeakAreaSquareMeters;       // m^2 added leak into the vacuum chamber (0 = healthy)
    public double vacuumSupplyBlockedFraction;     // 0..1 share of the check valve and hose area that is blocked (0 = healthy, 1 = no vacuum supply)
}

// @description - input of one brake step (one outer physics substep)
public struct BrakeStepInput
{
    public double pedalAxis;                       // 0..1 brake axis from the input layer (travel fraction, or force over full-scale force for a load-cell pedal)
    public bool loadCellPedal;                     // true = the axis is a force fraction of loadCellMaxForceNewtons; false = a travel fraction
    public double loadCellMaxForceNewtons;         // N pedal force at axis 1 (read only for a load-cell pedal)
    public double manifoldPressurePascals;         // Pa absolute intake manifold pressure
    public double manifoldVolumeCubicMeters;       // m^3 intake manifold volume (sets how much the returned air raises the manifold pressure)
    public BoosterFault boosterFault;              // booster fault state
}

// @description - result of one brake step
public struct BrakeStepResult
{
    public double frontLeftTorqueNewtonMeters, frontRightTorqueNewtonMeters;   // N m brake torque magnitude per wheel
    public double rearLeftTorqueNewtonMeters, rearRightTorqueNewtonMeters;     // N m brake torque magnitude per wheel (hydraulic part only; the handbrake is added by the car)
    public double manifoldMassReturnedKilograms;   // kg of air the vacuum chamber pushed into the intake manifold during this step
    public double pedalForceNewtons;               // N pedal pad force (measured for a load-cell pedal, derived from the pedal travel otherwise)
    public double masterPressurePascals;           // Pa master cylinder pressure
    public double boosterAssistForceNewtons;       // N force the booster adds to the input rod force
}

// @description - the brake system as the car sees it
public interface IBrakeSystem
{
    // @description - advances the brake system by one step
    // @param {BrakeStepInput} input - pedal and manifold state
    // @param {double} stepSeconds - step length in s
    // @return {BrakeStepResult} - brake torques and the manifold air mass returned
    BrakeStepResult Step(BrakeStepInput input, double stepSeconds);

    // @description - forgets all state (foot, booster, circuits); the next step starts from the manifold vacuum of that moment
    // @return {Void} - nothing
    void Reset();
}
