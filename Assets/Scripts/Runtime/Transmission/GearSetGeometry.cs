// Gear set of the 020/016-family box derived from tooth counts and the estimated module, helix, width and fill: ratios, loose-gear inertias, which shaft each loose gear is
// on, lash referred to the input shaft and the per-mesh efficiency. All numbers come from TransmissionParameters (no inline values).
using UnityEngine;

public class GearSetGeometry
{
    public const int ForwardGearCount = 5;
    public const int ReverseGear = -1;
    readonly TransmissionParameters parameters; readonly float finalDriveRatio;
    readonly double[] gearRatios = new double[ForwardGearCount]; readonly double[] looseGearInertias = new double[ForwardGearCount];

    public double FinalDriveRatio => finalDriveRatio;

    // @description - computes the ratios and gear inertias
    // @param {TransmissionParameters} parameters - tooth counts and geometry estimates
    // @param {float} finalDriveRatio - final drive ratio ring : pinion (car parameter)
    public GearSetGeometry(TransmissionParameters parameters, float finalDriveRatio)
    {
        this.parameters = parameters; this.finalDriveRatio = finalDriveRatio;
        for (int index = 0; index < ForwardGearCount; index++) { gearRatios[index] = parameters.GearRatio(index); looseGearInertias[index] = ComputeLooseGearInertia(index); }
    }

    // @description - gearbox ratio (input shaft turns per pinion shaft turn) of a gear; negative for reverse (the output shaft turns the other way)
    // @param {int} gear - 1..5 forward, -1 reverse
    // @return {double} - ratio
    public double GearboxRatio(int gear) => gear == ReverseGear ? -parameters.ReverseRatio() : gearRatios[gear - 1];

    // @description - overall ratio crankshaft : carrier, gearbox ratio times the final drive
    // @param {int} gear - 1..5 or -1
    // @return {double} - signed overall ratio
    public double TotalRatio(int gear) => GearboxRatio(gear) * finalDriveRatio;

    // @description - pitch radius of a helical gear, r = z m_n / (2 cos beta)
    // @param {int} toothCount - teeth
    // @return {double} - radius in m
    public double PitchRadius(int toothCount) => toothCount * parameters.normalModuleMillimeters * 1e-3 / (2.0 * System.Math.Cos(parameters.helixAngleDegrees * Mathf.Deg2Rad));

    // @description - inertia of the driven gear of a pair as a web disc, fill x 0.5 rho pi b r^4
    // @param {int} gearIndex - 0..4
    // @return {double} - kg m^2 at the gear's own shaft
    double ComputeLooseGearInertia(int gearIndex)
    {
        double radius = PitchRadius(parameters.drivenToothCount[gearIndex]), width = parameters.gearFaceWidthMillimeters * 1e-3;
        return parameters.gearFillFactor * 0.5 * parameters.steelDensityKilogramsPerCubicMeter * System.Math.PI * width * System.Math.Pow(radius, 4.0);
    }

    // @description - inertia of the loose gear used by the rattle model (1st and 2nd: the driven gear on the output shaft)
    // @param {int} gear - 1..5
    // @return {double} - kg m^2 at the gear's own shaft
    public double LooseGearInertia(int gear) => looseGearInertias[gear - 1];

    // @description - whether the gear's synchronizer cone sits on the input shaft (3rd..5th) or on the output shaft (1st, 2nd)
    // @param {int} gear - 1..5
    // @return {bool} - true when the loose gear is on the input shaft
    public bool LooseOnInputShaft(int gear) => parameters.gearIsLooseOnInputShaft[gear - 1];

    // @description - factor that refers a torque at the synchronizer cone to the input shaft: 1 when the cone is on the input shaft, 1 / ratio when it is on the output shaft
    // @param {int} gear - 1..5
    // @return {double} - dimensionless
    public double ConeTorqueReferenceToInput(int gear) => LooseOnInputShaft(gear) ? 1.0 : 1.0 / gearRatios[gear - 1];

    // @description - factor that refers the speed difference measured at the input shaft to the cone surface: 1 on the input shaft, 1 / ratio on the output shaft
    // @param {int} gear - 1..5
    // @return {double} - dimensionless
    public double ConeSpeedReferenceFromInput(int gear) => ConeTorqueReferenceToInput(gear);

    // @description - total half lash referred to the input shaft: pair backlash at the driven gear, final-drive backlash at the ring gear, and the dog play (at the input hub for
    //   gears on the input shaft, at the output hub otherwise), each multiplied by the speed ratio between its location and the input shaft
    // @param {int} gear - 1..5 or -1
    // @return {double} - half lash in rad at the input shaft
    public double HalfLashAtInputShaft(int gear)
    {
        double normalBacklashMeters = parameters.normalBacklashMillimeters * 1e-3, ratio = System.Math.Abs(GearboxRatio(gear)), transverseFactor = 1.0 / System.Math.Cos(parameters.normalPressureAngleDegrees * Mathf.Deg2Rad);
        int drivenTeeth = gear == ReverseGear ? parameters.reverseDrivenToothCount : parameters.drivenToothCount[gear - 1];
        double pairAngle = normalBacklashMeters * transverseFactor / PitchRadius(drivenTeeth) * ratio;
        double ringAngle = normalBacklashMeters * transverseFactor / PitchRadius(parameters.finalDriveRingToothCount) * ratio * finalDriveRatio;
        double dogAngle = parameters.dogAngularLashDegrees * Mathf.Deg2Rad * (gear != ReverseGear && LooseOnInputShaft(gear) ? 1.0 : ratio);
        return 0.5 * (pairAngle + ringAngle + dogAngle);
    }

    // @description - Ludwig loss factor of one helical mesh, H_V = pi (u + 1) / (z1 u cos(beta)) (1 - eps_a + eps_1^2 + eps_2^2) with eps_1 = eps_2 = eps_a / 2 (ISO/TR 14179-2 form as in Chalmers 2018 eq. 2.157)
    // @param {int} drivenTeeth - driven gear teeth
    // @param {int} drivingTeeth - driving gear teeth
    // @return {double} - dimensionless
    public double MeshLossFactor(int drivenTeeth, int drivingTeeth)
    {
        double ratio = (double)drivenTeeth / drivingTeeth, contact = parameters.meshContactRatio, half = 0.5 * contact;
        return System.Math.PI * (ratio + 1.0) / (drivingTeeth * ratio * System.Math.Cos(parameters.helixAngleDegrees * Mathf.Deg2Rad)) * (1.0 - contact + 2.0 * half * half);
    }

    // @description - efficiency of the gear pair and the final drive in a gear, (1 - H_V1 mu) (1 - H_V2 mu); the reverse path has two more meshes (idler in and out)
    // @param {int} gear - 1..5 or -1
    // @return {double} - efficiency of the load-dependent mesh friction only (spin and bearing losses are separate)
    public double MeshEfficiency(int gear)
    {
        double mu = parameters.meshFrictionCoefficient, efficiency = 1.0 - MeshLossFactor(parameters.finalDriveRingToothCount, parameters.finalDrivePinionToothCount) * mu;
        if (gear == ReverseGear) return efficiency * (1.0 - MeshLossFactor(parameters.reverseIdlerToothCount, parameters.reverseInputToothCount) * mu) * (1.0 - MeshLossFactor(parameters.reverseDrivenToothCount, parameters.reverseIdlerToothCount) * mu);
        return efficiency * (1.0 - MeshLossFactor(parameters.drivenToothCount[gear - 1], parameters.drivingToothCount[gear - 1]) * mu);
    }
}
