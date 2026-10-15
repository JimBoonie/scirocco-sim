// Compressible flow of a perfect gas through a sharp-edged orifice (isentropic, choked below the critical pressure ratio). Used by the booster air valve, vacuum valve, check valve and leak.
using System;

public static class CompressibleOrifice
{
    // @description - mass flow through an orifice from the upstream to the downstream side; zero when the downstream pressure is not lower
    // @param {double} dischargeCoefficient - orifice discharge coefficient
    // @param {double} areaSquareMeters - flow area in m^2
    // @param {double} upstreamPressurePascals - absolute pressure upstream, Pa
    // @param {double} downstreamPressurePascals - absolute pressure downstream, Pa
    // @param {double} gasConstant - specific gas constant, J/(kg K)
    // @param {double} temperatureKelvin - gas temperature, K
    // @param {double} heatCapacityRatio - cp/cv, dimensionless
    // @return {double} - mass flow in kg/s, never negative
    public static double MassFlow(double dischargeCoefficient, double areaSquareMeters, double upstreamPressurePascals, double downstreamPressurePascals, double gasConstant, double temperatureKelvin, double heatCapacityRatio)
    {
        if (areaSquareMeters <= 0.0 || upstreamPressurePascals <= downstreamPressurePascals) return 0.0;
        double pressureRatio = Math.Max(downstreamPressurePascals, 0.0) / upstreamPressurePascals;
        return dischargeCoefficient * areaSquareMeters * upstreamPressurePascals / Math.Sqrt(gasConstant * temperatureKelvin) * FlowFunction(pressureRatio, heatCapacityRatio);
    }

    // @description - critical pressure ratio below which the flow is choked, (2/(gamma+1))^(gamma/(gamma-1))
    // @param {double} heatCapacityRatio - cp/cv
    // @return {double} - downstream over upstream pressure at the sonic limit
    public static double CriticalPressureRatio(double heatCapacityRatio) => Math.Pow(2.0 / (heatCapacityRatio + 1.0), heatCapacityRatio / (heatCapacityRatio - 1.0));

    // @description - dimensionless flow function psi(r) so that mass flow = Cd A p_up psi / sqrt(R T); constant (choked) below the critical ratio
    // @param {double} pressureRatio - downstream over upstream pressure, 0..1
    // @param {double} heatCapacityRatio - cp/cv
    // @return {double} - flow function value
    public static double FlowFunction(double pressureRatio, double heatCapacityRatio)
    {
        double effectiveRatio = Math.Max(pressureRatio, CriticalPressureRatio(heatCapacityRatio));
        double subsonicTerm = Math.Pow(effectiveRatio, 2.0 / heatCapacityRatio) - Math.Pow(effectiveRatio, (heatCapacityRatio + 1.0) / heatCapacityRatio);
        return Math.Sqrt(2.0 * heatCapacityRatio / (heatCapacityRatio - 1.0) * Math.Max(subsonicTerm, 0.0));
    }
}
