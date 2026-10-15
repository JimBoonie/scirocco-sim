// Clutch friction capacity (experimental driveline): clamp load, friction coefficient with static/kinetic split, rubbing-speed slope, thermal fade
// and the rubbing-mass heat balance. The full clamp load is DERIVED so that the cold static capacity at full clamp equals CarParams.clutchMaxTorque.
using UnityEngine;

public class ClutchFrictionModel
{
    readonly ShuffleDrivelineParameters parameters;
    readonly float effectiveRadius, fullClampForce, heatCapacity, ambientTemperature;
    public float TemperatureKelvin { get; private set; }

    // @description - derives the full clamp force and the rubbing-mass heat capacity
    // @param {ShuffleDrivelineParameters} parameters - model parameters
    // @param {CarParams} carParams - clutchMaxTorque (cold static capacity at full clamp) and ambient temperature
    public ClutchFrictionModel(ShuffleDrivelineParameters parameters, CarParams carParams)
    {
        this.parameters = parameters; effectiveRadius = parameters.ClutchEffectiveRadiusMeters();
        float staticCoefficient = parameters.kineticFrictionCoefficient * parameters.staticToKineticFrictionRatio;
        fullClampForce = carParams.clutchMaxTorque / (parameters.frictionFaceCount * staticCoefficient * effectiveRadius);   // T = n mu F R_eff (D)
        heatCapacity = (parameters.flywheelMassKilograms + parameters.pressurePlateMassKilograms) * parameters.rubbingMassSpecificHeatJoulesPerKilogramKelvin;
        ambientTemperature = carParams.ambientTemperature; TemperatureKelvin = ambientTemperature;
    }

    // @description - fraction of friction left after thermal fade: 1 below the onset, linear down to 1 - maximum fade at complete fade
    // @return {float} - multiplier 0..1
    public float FadeMultiplier()
    {
        float progress = Mathf.InverseLerp(parameters.fadeOnsetTemperatureKelvin, parameters.fadeCompleteTemperatureKelvin, TemperatureKelvin);
        return 1f - parameters.maximumFadeFraction * progress;
    }

    // @description - static (stick) torque capacity
    // @param {float} clampFraction - 0..1
    // @return {float} - N m
    public float StaticCapacity(float clampFraction)
        => parameters.frictionFaceCount * parameters.kineticFrictionCoefficient * parameters.staticToKineticFrictionRatio * fullClampForce * effectiveRadius * clampFraction * FadeMultiplier();

    // @description - kinetic (slipping) torque capacity including the rubbing-speed slope
    // @param {float} clampFraction - 0..1
    // @param {float} slipAngularVelocity - rad/s between flywheel and disc
    // @return {float} - N m
    public float KineticCapacity(float clampFraction, float slipAngularVelocity)
    {
        float rubbingSpeed = Mathf.Abs(slipAngularVelocity) * effectiveRadius, speedFactor = Mathf.Max(0f, 1f + parameters.frictionSlopePerMeterPerSecond * rubbingSpeed);
        return parameters.frictionFaceCount * parameters.kineticFrictionCoefficient * fullClampForce * effectiveRadius * clampFraction * speedFactor * FadeMultiplier();
    }

    // @description - lumped heat balance of the flywheel and pressure plate: C dT/dt = P - C (T - T0) / tau
    // @param {float} dissipatedPowerWatts - friction power in W (torque times slip speed)
    // @param {float} stepSeconds - step in s
    // @return {Void}
    public void AddHeat(float dissipatedPowerWatts, float stepSeconds)
    {
        float coolingPower = heatCapacity * (TemperatureKelvin - ambientTemperature) / parameters.clutchCoolingTimeConstantSeconds;
        TemperatureKelvin += (dissipatedPowerWatts - coolingPower) / heatCapacity * stepSeconds;
    }

    // @description - resets the rubbing mass to ambient temperature
    // @return {Void}
    public void ResetTemperature() { TemperatureKelvin = ambientTemperature; }
}
