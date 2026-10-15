// Ambient air state that the pneumatic parts of the brake system share (taken from CarParams, so the booster and the engine manifold use the same air).

public struct AirProperties
{
    public double ambientPressurePascals;      // Pa (S: ISA sea level)
    public double temperatureKelvin;           // K gas temperature of the chambers (E: same as the manifold air)
    public double gasConstant;                 // J/(kg K) (S: dry air)
    public double heatCapacityRatio;           // - cp/cv (S: dry air)

    // @description - gas constant times temperature, the factor between pressure times volume and mass
    // @return {double} - J/kg
    public double GasConstantTimesTemperature => gasConstant * temperatureKelvin;

    // @description - reads the air state from the car parameters
    // @param {CarParams} carParams - car parameters
    // @return {AirProperties} - ambient air state
    public static AirProperties FromCarParams(CarParams carParams) => new AirProperties { ambientPressurePascals = carParams.ambientPressure, temperatureKelvin = carParams.ambientTemperature, gasConstant = carParams.airGasConstant, heatCapacityRatio = carParams.airGamma };
}
