// Small interfaces between the engine side and the driveline side of the experimental model, so the pieces can be moved when engine, ignition,
// driveline, transmission and brake code are reorganized. The torque source only knows crank angle/speed; the driveline only knows torques.

// @description - input of one driveline step (one outer physics substep)
public struct DrivelineStepInput
{
    public ICrankshaftTorqueSource crankshaftTorqueSource;   // engine side
    public double clutchClampFraction;                        // 0..1 clamp load / full clamp load (from the pedal curve)
    public double totalRatioSigned;                           // crank turns per carrier turn, negative in reverse, 0 = neutral or shifting
    public double wheelInertiaKilogramMetersSquared;          // each driven wheel
    public double leftWheelAngularVelocity, rightWheelAngularVelocity;   // rad/s measured at the start of the step
    public double leftTireResistingTorque, rightTireResistingTorque;     // N m the tire currently exerts against wheel rotation (R times longitudinal force)
    public double drivetrainEfficiency;                       // - mesh efficiency applied to the power flow direction
    public double inputShaftExternalTorque, carrierExternalTorque;   // N m applied to the input-shaft node and to the carrier by the experimental transmission (0 = none; unused while the transmission toggle is off)
    public bool overrideMeshLash;                             // true = the transmission owns the gear lash: use meshHalfLashAtInputShaftRadians instead of the parameter
    public double meshHalfLashAtInputShaftRadians;            // rad half lash referred to the input shaft (read only when overrideMeshLash)
}

// @description - result of one driveline step
public struct DrivelineStepResult
{
    public double leftWheelAverageTorque, rightWheelAverageTorque;   // N m averaged over the step, to hand to the tire integrator
    public double crankAngularVelocity;                              // rad/s at the end of the step
    public double clutchSlipAngularVelocity;                         // rad/s engine minus clutch disc
    public double clutchFrictionTorque;                              // N m mean torque through the clutch
    public double clutchTemperatureKelvin;                           // K rubbing-mass temperature
    public int microstepCount;                                       // integration micro-steps used
}

// @description - engine side: everything the driveline needs from an engine
public interface ICrankshaftTorqueSource
{
    // @description - refreshes manifold, combustion state and mean torques for the coming interval (call once per outer step)
    // @param {double} throttleFraction - accelerator pedal 0..1
    // @param {double} intervalSeconds - length of the coming interval in s
    void BeginInterval(double throttleFraction, double intervalSeconds);
    // @description - inertia the engine side adds to the crank during the interval (starter armature), kg m^2
    double AuxiliaryInertiaKilogramMetersSquared { get; }
    // @description - crank torque at an instant: mean net torque plus firing and reciprocating pulsation (N m, positive speeds the crank up)
    // @param {double} crankAngleRadians - crank angle in rad, 0 at firing TDC of cylinder 1
    // @param {double} crankAngularVelocity - crank speed in rad/s
    double TorqueAt(double crankAngleRadians, double crankAngularVelocity);
    // @description - hands the integrated crank speed back so the engine model stays consistent
    // @param {double} crankAngularVelocity - crank speed in rad/s at the end of the interval
    void EndInterval(double crankAngularVelocity);
}

// @description - driveline side: a torsional chain from the crank to the two driven wheels
public interface ITorsionalDriveline
{
    // @description - restarts the chain from measured speeds (used when the model is switched on)
    // @param {double} crankAngularVelocity - rad/s
    // @param {double} meanWheelAngularVelocity - rad/s
    void Initialize(double crankAngularVelocity, double meanWheelAngularVelocity);
    // @description - advances the chain by one outer step
    // @param {DrivelineStepInput} input - inputs for the step
    // @param {double} stepSeconds - outer step in s
    // @return {DrivelineStepResult} - torques to the wheels and chain state
    DrivelineStepResult Advance(in DrivelineStepInput input, double stepSeconds);
}
