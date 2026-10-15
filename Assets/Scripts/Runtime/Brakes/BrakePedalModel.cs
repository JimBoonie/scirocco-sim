// Driver side of the EXPERIMENTAL brake model: how the brake axis becomes a pedal force or a pedal travel.
// Load-cell pedal: the axis is force over the configured full-scale force, so force = axis x full-scale force and goes into the model directly.
// Position input (keyboard, gamepad trigger, potentiometer pedal): the axis is a travel fraction. The foot cannot step, so the pedal pad moves at a limited foot speed (apply and release speeds, estimates).
// Full travel is the travel at which the model reproduces the old fixed full-pedal front torque (derived, so a full key press keeps its old meaning).
using System;

public class BrakePedalModel
{
    readonly BrakeSystemParameters parameters; readonly double fullTravelMeters;
    double footTravelFraction;

    // @description - builds the pedal model
    // @param {BrakeSystemParameters} parameters - brake parameters
    // @param {double} fullTravelMeters - pedal pad travel at axis 1 for a position input, m
    // @return {void} - constructor
    public BrakePedalModel(BrakeSystemParameters parameters, double fullTravelMeters)
    {
        this.parameters = parameters; this.fullTravelMeters = fullTravelMeters;
    }

    // @description - pedal pad travel at axis 1 for a position input
    // @return {double} - m
    public double FullTravelMeters => fullTravelMeters;

    // @description - foot position as a fraction of the full travel (rate limited)
    // @return {double} - 0..1
    public double FootTravelFraction => footTravelFraction;

    // @description - puts the foot back at rest
    // @return {Void} - nothing
    public void Reset() { footTravelFraction = 0.0; }

    // @description - pedal force of a load-cell pedal
    // @param {double} pedalAxis - force fraction 0..1
    // @param {double} fullScaleForceNewtons - force at axis 1, N
    // @return {double} - pedal pad force in N
    public static double LoadCellForceNewtons(double pedalAxis, double fullScaleForceNewtons) => Math.Min(Math.Max(pedalAxis, 0.0), 1.0) * fullScaleForceNewtons;

    // @description - makes the foot position equal the axis without a rate limit (used while a load-cell pedal is in use, so a switch to a position input starts from the right place)
    // @param {double} pedalAxis - axis 0..1
    // @return {Void} - nothing
    public void FollowAxis(double pedalAxis) { footTravelFraction = Math.Min(Math.Max(pedalAxis, 0.0), 1.0); }

    // @description - moves the foot toward the commanded travel fraction at the foot speed limit (apply speed when pressing, release speed when lifting)
    // @param {double} pedalAxis - commanded travel fraction 0..1
    // @param {double} stepSeconds - step length in s
    // @return {double} - pedal pad travel in m after the step
    public double AdvanceFootTravelMeters(double pedalAxis, double stepSeconds)
    {
        double commanded = Math.Min(Math.Max(pedalAxis, 0.0), 1.0);
        double speed = commanded > footTravelFraction ? parameters.footApplySpeedMetersPerSecond : parameters.footReleaseSpeedMetersPerSecond;
        double maximumChange = speed / fullTravelMeters * stepSeconds;
        footTravelFraction += Math.Min(Math.Max(commanded - footTravelFraction, -maximumChange), maximumChange);
        return footTravelFraction * fullTravelMeters;
    }
}
