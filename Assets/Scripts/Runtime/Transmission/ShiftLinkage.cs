// Hand to sleeve: the lever, rods and selector as one ideal lever of mechanical advantage MA with an efficiency, plus the assumed driver. Shifters have no force feedback, so the
// driver is a constant-force, constant-speed hand (estimates, tagged in TransmissionParameters). Retention of the dog teeth decides when a gear can be left.
using UnityEngine;

public class ShiftLinkage
{
    const int GearsPerRail = 2, FifthAndReverseRail = 2;   // the 1/2 and 3/4 rails carry two gears; 5th and reverse share the third rail (S: Bentley shift mechanism)
    readonly TransmissionParameters parameters;

    // @description - builds the linkage
    // @param {TransmissionParameters} parameters - linkage and driver estimates
    // @return {void} - constructor
    public ShiftLinkage(TransmissionParameters parameters) { this.parameters = parameters; }

    // @description - axial sleeve force while the driver pushes into gear, F_sleeve = MA eta F_hand
    // @return {double} - force in N
    public double PushForceAtSleeve() => parameters.handToSleeveMechanicalAdvantage * parameters.linkageEfficiency * parameters.driverPushForceNewtons;

    // @description - axial sleeve force while the driver pulls out of gear
    // @return {double} - force in N
    public double PullForceAtSleeve() => parameters.handToSleeveMechanicalAdvantage * parameters.linkageEfficiency * parameters.driverPullForceNewtons;

    // @description - sleeve speed for the assumed lever speed, v_sleeve = v_knob / MA
    // @return {double} - speed in m/s
    public double SleeveSpeed() => parameters.knobPushSpeedMetersPerSecond / parameters.handToSleeveMechanicalAdvantage;

    // @description - time for the lever to move a knob distance at the assumed lever speed
    // @param {double} knobTravelMeters - knob travel in m
    // @return {double} - time in s
    public double KnobTravelTime(double knobTravelMeters) => knobTravelMeters / parameters.knobPushSpeedMetersPerSecond;

    // @description - time the sleeve needs for an axial travel at the sleeve speed
    // @param {double} sleeveTravelMeters - travel in m
    // @return {double} - time in s
    public double SleeveTravelTime(double sleeveTravelMeters) => sleeveTravelMeters / SleeveSpeed();

    // @description - gate (rail) index of a gear: 1/2 rail 0, 3/4 rail 1, 5/reverse rail 2
    // @param {int} gear - 1..5 or -1
    // @return {int} - rail index, 0..2
    public static int RailOf(int gear) => gear == GearSetGeometry.ReverseGear ? FifthAndReverseRail : (gear + 1) / GearsPerRail - 1;

    // @description - lever travel time from the neutral gate center of one rail to the ring contact of the target gear on another rail (crossing the gate plus pushing in)
    // @param {int} fromRail - rail where the lever was last in neutral
    // @param {int} targetGear - gear to push into
    // @return {double} - time in s
    public double TravelTimeToRingContact(int fromRail, int targetGear)
        => KnobTravelTime(System.Math.Abs(RailOf(targetGear) - fromRail) * parameters.railSpacingKnobMeters + parameters.neutralToRingContactKnobTravelMeters);

    // @description - axial force needed to pull the sleeve out of the dog teeth: detent plus the flank force. The torque T across the dog teeth gives a tangential force T / R; the flank
    //   resists the sleeve with friction and the retaining undercut: F = (T / R) (mu + tan(gamma))
    // @param {double} torqueAcrossDogsNewtonMeters - torque the dog teeth carry in N m (magnitude)
    // @return {double} - sleeve force in N
    public double ReleaseForceRequired(double torqueAcrossDogsNewtonMeters)
        => parameters.detentForceAtSleeveNewtons + System.Math.Abs(torqueAcrossDogsNewtonMeters) / parameters.dogToothRadiusMeters * (parameters.dogFlankFrictionCoefficient + System.Math.Tan(parameters.dogRetentionUndercutDegrees * Mathf.Deg2Rad));

    // @description - the largest torque across the dog teeth for which the assumed driver arm force can still pull the gear out
    // @return {double} - torque in N m
    public double MaximumReleasableTorque()
        => (PullForceAtSleeve() - parameters.detentForceAtSleeveNewtons) * parameters.dogToothRadiusMeters / (parameters.dogFlankFrictionCoefficient + System.Math.Tan(parameters.dogRetentionUndercutDegrees * Mathf.Deg2Rad));
}
