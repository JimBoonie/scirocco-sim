// Two-port interface of the experimental transmission, independent of which driveline is behind it (simple drivetrain or the torsional chain).
//   Input port: the input-shaft node (speed, inertia, net torque the clutch side applies) and the torque the transmission applies back on that node.
//   Output port: the differential carrier (speed) and the torque the transmission applies on it.
// A future unifying refactor can merge the adapters by keeping these two structs and ITransmission.

// @description - phase of the gear-engagement state machine (exposed for tests, HUD and later sound events)
public enum GearboxPhase
{
    Neutral,          // no gear engaged, lever in the gate center
    Releasing,        // sleeve leaving the dog teeth
    Traveling,        // lever and selector moving to the blocking ring contact
    Synchronizing,    // cone slipping, ring blocking the sleeve
    FreeFlight,       // ring unblocked, sleeve moving to the dog chamfers
    DogCrossing,      // sleeve crossing the dog chamfers (indexing)
    Engaged,          // dog teeth in; the driver has stopped pushing
    Grinding          // dog or reverse chamfers clashed; the sleeve was thrown back and the driver pushes again
}

// @description - input of one transmission step (one outer physics substep)
public struct TransmissionStepInput
{
    public double inputShaftAngularVelocity;       // rad/s speed of the input-shaft node
    public double carrierAngularVelocity;          // rad/s speed of the differential carrier
    public double gearTrainInputTorque;            // N m torque the input node delivered into the gear train in the previous step (clutch side net of the node's own inertia)
    public double inputNodeInertiaKilogramMetersSquared;   // kg m^2 inertia of the input node (disc, shaft assembly) as integrated by the adapter
    public double ambientTemperatureKelvin;        // K
    public double dt;                              // s
}

// @description - result of one transmission step
public struct TransmissionStepResult
{
    public double lockedTotalRatioSigned;          // crank turns per carrier turn when the input node is kinematically tied to the carrier (cone stuck or dogs in); 0 = the node is free
    public double inputShaftExternalTorque;        // N m on the input node: spin loss, loose-gear drag, rattle impacts, synchronizer reaction (while free)
    public double carrierExternalTorque;           // N m on the carrier: bearing drag, loose-gear drag, synchronizer reaction (while free)
    public double meshEfficiency;                  // - efficiency of the load-dependent mesh friction in the engaged gear (1 when free)
    public double halfLashAtInputShaftRadians;     // rad half lash the chain must apply in the engaged gear (0 while only the cone holds)
    public bool lashOwnedByTransmission;           // true when the lash value above must replace the driveline's own lumped gear lash
}

// @description - the transmission as a two-port element
public interface ITransmission
{
    // @description - advances the transmission by one step
    // @param {TransmissionStepInput} input - inputs
    // @return {TransmissionStepResult} - kinematic lock state and the torques on both ports
    TransmissionStepResult Advance(in TransmissionStepInput input);
}
