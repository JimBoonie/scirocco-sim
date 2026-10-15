// Rear drum brake with a dual-opposed wheel cylinder: each shoe is pushed by the force wheel cylinder area x effective pressure, torque = brake factor of the shoe pair x that force x drum radius.
// The brake factor (2.4) is an estimate until the shoe geometry is known. No drum temperature (fade) is modeled.

public class DrumBrakeModel
{
    readonly double torquePerPascal; readonly double bitePressurePascals;

    // @description - builds the brake from its geometry
    // @param {BrakeSystemParameters} parameters - brake parameters
    // @return {void} - constructor
    public DrumBrakeModel(BrakeSystemParameters parameters)
    {
        const double RadiusPerDiameter = 0.5;   // - radius over diameter
        torquePerPascal = parameters.rearBrakeFactorOfShoePair * BrakeSystemParameters.CircleArea(parameters.rearWheelCylinderBoreMeters) * parameters.rearDrumInsideDiameterMeters * RadiusPerDiameter;
        bitePressurePascals = parameters.rearBitePressurePascals;
    }

    // @description - brake torque gain above the bite pressure
    // @return {double} - N m per Pa of line pressure
    public double TorquePerPascal => torquePerPascal;

    // @description - brake torque of one wheel
    // @param {double} linePressurePascals - wheel cylinder line pressure, Pa gauge
    // @return {double} - torque magnitude in N m; exactly 0 at or below the bite pressure
    public double Torque(double linePressurePascals) => linePressurePascals > bitePressurePascals ? (linePressurePascals - bitePressurePascals) * torquePerPascal : 0.0;
}
