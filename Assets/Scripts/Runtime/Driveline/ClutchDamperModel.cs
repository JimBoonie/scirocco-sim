// Clutch disc torsional damper (experimental driveline): three-stage piecewise-linear spring, separate drive and coast stage angles, and
// stage-dependent dry-friction hysteresis built on the play (backlash) operator so the loop closes without velocity switching noise.
using UnityEngine;

public class ClutchDamperModel
{
    readonly double[] stageStiffness, driveStageEnd, coastStageEnd, stageHysteresis;
    // Presliding twist (D): largest stage friction / stiffest stage stiffness. Below twice the stage friction torque the damper therefore locks (rigid, as real dry friction does),
    // which raises the small-amplitude shuffle frequency; above it the spring acts and the friction gives the hysteresis loop.
    readonly double presliding;                                // rad twist over which the friction torque builds up
    double playState;                                          // rad, friction element position inside +-presliding

    public double StopAngleDrive => driveStageEnd[1];          // rad end of stage 2 (drive)
    public double StopStiffness => stageStiffness[2];          // N m/rad stage 3

    // @description - converts the parameter arrays to SI
    // @param {ShuffleDrivelineParameters} parameters - model parameters
    public ClutchDamperModel(ShuffleDrivelineParameters parameters)
    {
        const double degreesToRadians = System.Math.PI / 180.0;
        stageStiffness = new double[3]; stageHysteresis = new double[3]; driveStageEnd = new double[2]; coastStageEnd = new double[2];
        for (int stage = 0; stage < 3; stage++) { stageStiffness[stage] = parameters.damperStageStiffnessNewtonMetersPerDegree[stage] / degreesToRadians; stageHysteresis[stage] = parameters.damperStageHysteresisNewtonMeters[stage]; }
        for (int stage = 0; stage < 2; stage++) { driveStageEnd[stage] = parameters.damperDriveStageEndDegrees[stage] * degreesToRadians; coastStageEnd[stage] = parameters.damperCoastStageEndDegrees[stage] * degreesToRadians; }
        presliding = System.Math.Max(1e-9, System.Math.Max(stageHysteresis[0], System.Math.Max(stageHysteresis[1], stageHysteresis[2]))) / stageStiffness[2];
    }

    // @description - spring torque of the three stages for a relative twist (positive = drive side)
    // @param {double} twistRadians - engine-side minus gearbox-side angle in rad
    // @return {double} - N m
    public double SpringTorque(double twistRadians)
    {
        double[] ends = twistRadians >= 0.0 ? driveStageEnd : coastStageEnd; double magnitude = System.Math.Abs(twistRadians), torque;
        if (magnitude <= ends[0]) torque = stageStiffness[0] * magnitude;
        else if (magnitude <= ends[1]) torque = stageStiffness[0] * ends[0] + stageStiffness[1] * (magnitude - ends[0]);
        else torque = stageStiffness[0] * ends[0] + stageStiffness[1] * (ends[1] - ends[0]) + stageStiffness[2] * (magnitude - ends[1]);
        return twistRadians >= 0.0 ? torque : -torque;
    }

    // @description - friction torque magnitude of the stage that the current twist lies in
    // @param {double} twistRadians - rad
    // @return {double} - N m
    double StageHysteresis(double twistRadians)
    {
        double[] ends = twistRadians >= 0.0 ? driveStageEnd : coastStageEnd; double magnitude = System.Math.Abs(twistRadians);
        return magnitude <= ends[0] ? stageHysteresis[0] : magnitude <= ends[1] ? stageHysteresis[1] : stageHysteresis[2];
    }

    // @description - total damper torque for a twist, using the current friction element position (no state change)
    // @param {double} twistRadians - engine-side minus gearbox-side angle in rad
    // @return {double} - spring plus hysteresis torque in N m
    public double Torque(double twistRadians) => SpringTorque(twistRadians) + StageHysteresis(twistRadians) * playState / presliding;

    // @description - advances the play operator by a twist increment (call once per micro-step after the twist has been updated)
    // @param {double} twistIncrementRadians - change of twist over the micro-step in rad
    // @return {Void}
    public void AdvanceFrictionElement(double twistIncrementRadians) { playState = System.Math.Max(-presliding, System.Math.Min(presliding, playState + twistIncrementRadians)); }

    // @description - clears the friction element
    // @return {Void}
    public void Reset() { playState = 0.0; }
}
