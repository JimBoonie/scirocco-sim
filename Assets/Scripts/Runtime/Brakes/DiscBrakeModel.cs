// Front disc brake with a single-piston floating caliper: clamp force = effective pressure x piston area, the same on both pads, torque = 2 x mu x clamp x effective radius x caliper efficiency.
// No pad temperature (fade) is modeled: the friction coefficient is a constant.
using System;

public class DiscBrakeModel
{
    readonly double torquePerPascal; readonly double bitePressurePascals;

    // @description - builds the brake from its geometry and friction
    // @param {BrakeSystemParameters} parameters - brake parameters
    // @return {void} - constructor
    public DiscBrakeModel(BrakeSystemParameters parameters)
    {
        const double PadsPerCaliper = 2.0;   // - one pad on each side of the disc (S: floating caliper design)
        torquePerPascal = PadsPerCaliper * parameters.frontPadFrictionCoefficient * BrakeSystemParameters.CircleArea(parameters.frontCaliperPistonDiameterMeters) * parameters.frontEffectiveFrictionRadiusMeters * parameters.frontCaliperEfficiency;
        bitePressurePascals = parameters.frontBitePressurePascals;
    }

    // @description - brake torque gain above the bite pressure
    // @return {double} - N m per Pa of line pressure
    public double TorquePerPascal => torquePerPascal;

    // @description - brake torque of one wheel
    // @param {double} linePressurePascals - caliper line pressure, Pa gauge
    // @return {double} - torque magnitude in N m; exactly 0 at or below the bite pressure
    public double Torque(double linePressurePascals) => linePressurePascals > bitePressurePascals ? (linePressurePascals - bitePressurePascals) * torquePerPascal : 0.0;

    // @description - line pressure needed for a brake torque (inverse of Torque)
    // @param {double} torqueNewtonMeters - wanted torque, N m
    // @return {double} - line pressure in Pa
    public double PressureForTorque(double torqueNewtonMeters) => torqueNewtonMeters / torquePerPascal + bitePressurePascals;
}
