// Gear oil and drag losses of the experimental transmission: viscosity from temperature (ASTM D341 / Walther form through two datasheet points), unloaded spin loss (churning,
// windage, bearing viscous; anchored to the Szweda 2008 measurement), loose-gear drag, output-side bearing drag, and a lumped oil temperature node.
using UnityEngine;

public class GearboxOilModel
{
    const double WaltherOffset = 0.7;            // - constant of ASTM D341 for kinematic viscosities above 2 mm^2/s (S: standard)
    const double CelsiusToKelvin = 273.15;       // K (S: definition)
    const double ReferenceCelsiusLow = 40.0, ReferenceCelsiusHigh = 100.0;   // C of the two viscosity datasheet points (S: ISO viscosity convention)
    readonly TransmissionParameters parameters;
    readonly double waltherIntercept, waltherSlope, spinSpeedExponent, spinViscosityExponent, referenceViscosity;
    public double TemperatureKelvin { get; private set; }

    // @description - fits the viscosity law and the spin-loss exponents from the parameter anchors
    // @param {TransmissionParameters} parameters - oil and loss parameters
    // @param {double} initialTemperatureKelvin - starting oil temperature in K (ambient)
    public GearboxOilModel(TransmissionParameters parameters, double initialTemperatureKelvin)
    {
        this.parameters = parameters; TemperatureKelvin = initialTemperatureKelvin;
        double doubleLogLow = System.Math.Log10(System.Math.Log10(parameters.oilKinematicViscosityAt40CMillimetersSquaredPerSecond + WaltherOffset));
        double doubleLogHigh = System.Math.Log10(System.Math.Log10(parameters.oilKinematicViscosityAt100CMillimetersSquaredPerSecond + WaltherOffset));
        double logTemperatureLow = System.Math.Log10(ReferenceCelsiusLow + CelsiusToKelvin), logTemperatureHigh = System.Math.Log10(ReferenceCelsiusHigh + CelsiusToKelvin);
        waltherSlope = (doubleLogLow - doubleLogHigh) / (logTemperatureHigh - logTemperatureLow);
        waltherIntercept = doubleLogLow + waltherSlope * logTemperatureLow;
        spinSpeedExponent = System.Math.Log(parameters.spinLossSecondTorqueNewtonMeters / parameters.spinLossReferenceTorqueNewtonMeters) / System.Math.Log(parameters.spinLossSecondSpeedRevolutionsPerMinute / parameters.spinLossReferenceSpeedRevolutionsPerMinute);
        referenceViscosity = Viscosity(parameters.spinLossReferenceOilTemperatureKelvin);
        double coldViscosity = Viscosity(parameters.spinLossColdOilTemperatureKelvin);
        spinViscosityExponent = System.Math.Log(1.0 - parameters.spinLossDropFromColdToReference) / System.Math.Log(referenceViscosity / coldViscosity);
    }

    // @description - kinematic viscosity from log10 log10(nu + 0.7) = A - B log10(T)
    // @param {double} temperatureKelvin - oil temperature in K
    // @return {double} - viscosity in mm^2/s
    public double Viscosity(double temperatureKelvin)
        => System.Math.Pow(10.0, System.Math.Pow(10.0, waltherIntercept - waltherSlope * System.Math.Log10(temperatureKelvin))) - WaltherOffset;

    // @description - ratio of the current viscosity to the viscosity of the spin-loss reference temperature
    // @return {double} - dimensionless
    public double ViscosityRatioToReference() => Viscosity(TemperatureKelvin) / referenceViscosity;

    // @description - unloaded spin torque of the whole box (churning, windage, bearing viscous, loose-gear shear) at the input shaft, anchored to Szweda 2008:
    //   T = T_ref s (omega / omega_ref)^p (nu / nu_ref)^q with p from the 1000 and 4000 rpm points and q from the 30 -> 80 C drop
    // @param {double} inputSpeedRadiansPerSecond - input shaft speed in rad/s (magnitude is used)
    // @return {double} - torque magnitude in N m, opposing rotation
    public double SpinTorqueMagnitude(double inputSpeedRadiansPerSecond)
    {
        double referenceSpeed = parameters.spinLossReferenceSpeedRevolutionsPerMinute * System.Math.PI / 30.0, speed = System.Math.Abs(inputSpeedRadiansPerSecond);
        if (speed <= 0.0) return 0.0;
        return parameters.spinLossReferenceTorqueNewtonMeters * parameters.spinLossScaleForThisGearbox * System.Math.Pow(speed / referenceSpeed, spinSpeedExponent) * System.Math.Pow(ViscosityRatioToReference(), spinViscosityExponent);
    }

    // @description - drag torque of one loose gear against its shaft: needle bearing floor plus viscous shear scaled by the oil viscosity
    // @param {double} relativeSpeedRadiansPerSecond - speed of the gear relative to its shaft in rad/s
    // @return {double} - torque magnitude in N m at the gear (opposes the relative motion)
    public double LooseGearDragMagnitude(double relativeSpeedRadiansPerSecond)
    {
        double speed = System.Math.Abs(relativeSpeedRadiansPerSecond);
        return speed <= 0.0 ? 0.0 : parameters.looseGearRollingDragNewtonMeters + parameters.looseGearViscousDragNewtonMetersSecondPerRadian * ViscosityRatioToReference() * speed;
    }

    // @description - bearing preload drag of the output side (pinion shaft taper pair referred to the carrier by the final drive ratio, plus the differential pair)
    // @param {double} finalDriveRatio - ring : pinion
    // @return {double} - torque magnitude at the carrier in N m
    public double OutputBearingDragMagnitude(double finalDriveRatio)
        => parameters.outputPinionBearingTurningTorqueNewtonMeters * finalDriveRatio + parameters.differentialBearingTurningTorqueNewtonMeters;

    // @description - advances the lumped oil temperature: m c dT/dt = P_loss + P_cone - G (T - T_ambient)
    // @param {double} heatPowerWatts - heat flowing into the oil in W
    // @param {double} ambientTemperatureKelvin - ambient temperature in K
    // @param {double} stepSeconds - step in s
    // @return {Void}
    public void AdvanceTemperature(double heatPowerWatts, double ambientTemperatureKelvin, double stepSeconds)
    {
        double heatCapacity = parameters.oilMassKilograms * parameters.oilSpecificHeatJoulesPerKilogramKelvin;
        TemperatureKelvin += (heatPowerWatts - parameters.casingThermalConductanceWattsPerKelvin * (TemperatureKelvin - ambientTemperatureKelvin)) / heatCapacity * stepSeconds;
    }
}
