// Pedal position to clutch clamp load for a cable-operated diaphragm-spring clutch (experimental driveline).
// Chain: pedal -> release-lever travel (free play first) -> release bearing contacts the diaphragm fingers -> pressure plate lifts by lever travel
// divided by the finger lever ratio -> clamp load falls linearly over the facing cushion-spring travel (the cushion spring is what makes the bite
// progressive). Engagement speed is NOT in this curve: it follows from how fast the pedal moves through it.
using UnityEngine;

public static class ClutchEngagementCurve
{
    // @description - clamp load fraction for a pedal position
    // @param {ShuffleDrivelineParameters} parameters - model parameters
    // @param {float} pedalPosition - 0 released .. 1 pedal at the floor
    // @return {float} - clamp load / full clamp load, 0..1
    public static float ClampFraction(ShuffleDrivelineParameters parameters, float pedalPosition)
    {
        float leverTravel = Mathf.Clamp01(pedalPosition) * parameters.releaseLeverTravelAtFullPedalMeters;
        float plateLift = Mathf.Max(0f, leverTravel - parameters.releaseLeverFreePlayMeters) / parameters.diaphragmFingerLeverRatio;
        return Mathf.Clamp01(1f - plateLift / parameters.cushionSpringTravelMeters);
    }

    // @description - pedal position at which the clamp load starts to fall (end of free play)
    // @param {ShuffleDrivelineParameters} parameters - model parameters
    // @return {float} - pedal position 0..1
    public static float BitePointPedalPosition(ShuffleDrivelineParameters parameters) => parameters.releaseLeverFreePlayMeters / parameters.releaseLeverTravelAtFullPedalMeters;

    // @description - pedal position at which the clamp load has fallen to zero (clutch fully released)
    // @param {ShuffleDrivelineParameters} parameters - model parameters
    // @return {float} - pedal position 0..1
    public static float FullyReleasedPedalPosition(ShuffleDrivelineParameters parameters)
        => (parameters.releaseLeverFreePlayMeters + parameters.cushionSpringTravelMeters * parameters.diaphragmFingerLeverRatio) / parameters.releaseLeverTravelAtFullPedalMeters;

    // @description - inverse of ClampFraction on the bite range, used to map the auto-clutch law (which outputs a capacity fraction) back to a pedal position
    // @param {ShuffleDrivelineParameters} parameters - model parameters
    // @param {float} clampFraction - 0..1
    // @return {float} - pedal position 0..1
    public static float PedalPositionForClampFraction(ShuffleDrivelineParameters parameters, float clampFraction)
    {
        float bite = BitePointPedalPosition(parameters), released = FullyReleasedPedalPosition(parameters);
        return Mathf.Lerp(released, bite, Mathf.Clamp01(clampFraction));
    }
}
