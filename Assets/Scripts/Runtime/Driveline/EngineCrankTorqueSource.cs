// Engine-side adapter of the experimental driveline: wraps the existing mean-value Engine (manifold, combustion state, mean torques) and adds the
// firing and reciprocating ripple from FiringPulseModel. Implements ICrankshaftTorqueSource, so a future engine refactor only has to provide this interface.
using UnityEngine;

public class EngineCrankTorqueSource : ICrankshaftTorqueSource
{
    readonly Engine engine; readonly CarParams carParams; readonly FiringPulseModel firingPulseModel; readonly bool firingPulsesEnabled; readonly double leakageHalfSpeedRadiansPerSecond;
    double meanTorque, auxiliaryInertia;

    public double AuxiliaryInertiaKilogramMetersSquared => auxiliaryInertia;
    public FiringPulseModel FiringPulses => firingPulseModel;

    // @description - wraps an engine
    // @param {Engine} engine - mean-value engine model
    // @param {CarParams} carParams - car parameters
    // @param {ShuffleDrivelineParameters} parameters - model parameters
    public EngineCrankTorqueSource(Engine engine, CarParams carParams, ShuffleDrivelineParameters parameters)
    {
        this.engine = engine; this.carParams = carParams; firingPulsesEnabled = parameters.firingPulsesEnabled; firingPulseModel = new FiringPulseModel(parameters, carParams);
        leakageHalfSpeedRadiansPerSecond = parameters.crankingLeakageHalfSpeedRpm * Mathf.PI / 30.0;
    }

    // @description - advances the engine's manifold and combustion state and sets the firing-pulse operating point
    // @param {double} throttleFraction - accelerator pedal 0..1
    // @param {double} intervalSeconds - interval in s
    public void BeginInterval(double throttleFraction, double intervalSeconds)
    {
        float netTorque = engine.BeginExternalCrankInterval((float)throttleFraction, (float)intervalSeconds, out float indicated, out float starterTorque, out float starterInertia);
        meanTorque = netTorque + starterTorque; auxiliaryInertia = starterInertia;
        firingPulseModel.SetOperatingPoint(engine.ManifoldPressurePascals, carParams.exhaustBackPressure, carParams.ambientPressure, indicated);
    }

    // @description - share of the motored (not firing) gas ripple that survives at a crank speed. Rings leak and the charge loses heat during the slow compression
    //   stroke, so a stopped or slowly cranked engine has far less compression hump than the adiabatic table predicts; without this the starter (stall torque
    //   ~105 N m) cannot climb the hump when the crank stopped in front of it and the engine never starts. Leakage ~ 1/speed: fraction = w / (w + w_half) (D).
    //   A firing engine keeps the full ripple (1), so running behavior is unchanged.
    // @param {double} crankAngularVelocity - rad/s
    // @return {double} - 0..1
    public double GasRippleFraction(double crankAngularVelocity)
    {
        if (engine.combusting || leakageHalfSpeedRadiansPerSecond <= 0.0) return 1.0;
        double speed = System.Math.Max(0.0, crankAngularVelocity); return speed / (speed + leakageHalfSpeedRadiansPerSecond);
    }

    // @description - mean torque plus ripple at a crank angle
    // @param {double} crankAngleRadians - rad
    // @param {double} crankAngularVelocity - rad/s
    // @return {double} - N m
    public double TorqueAt(double crankAngleRadians, double crankAngularVelocity)
        => firingPulsesEnabled ? meanTorque + firingPulseModel.RippleTorque(crankAngleRadians, crankAngularVelocity, GasRippleFraction(crankAngularVelocity)) : meanTorque;

    // @description - gives the integrated crank speed to the engine
    // @param {double} crankAngularVelocity - rad/s
    public void EndInterval(double crankAngularVelocity) => engine.EndExternalCrankInterval((float)crankAngularVelocity);
}
