// Physical air-path engine model: WOT matches the sourced curve, idle is a stable steady state, part-throttle torque falls with rpm.
using NUnit.Framework; using UnityEngine;
public class EngineAirPathTests
{
    // @description - Creates a default CarParams asset and builds the physical air-path Engine on it, so each test starts from the shipped engine data.
    // @param {CarParams} carParams - output: the freshly created car parameter asset (rpm and torque curve points, idle rpm) for the test to read
    // @return {Engine} - engine constructed from that car parameter asset, stopped and at rest
    static Engine Make(out CarParams carParams) { carParams = ScriptableObject.CreateInstance<CarParams>(); return new Engine(carParams); }
    // @description - Verifies that wide-open-throttle torque from the air-path model lies within 1 N m of the sourced torque curve at every curve rpm, and that the derived volumetric efficiency stays physical (0.5 to 1.05).
    // @return {Void} - nothing
    [Test] public void Wot_Matches_Sourced_Curve()
    {
        var engine = Make(out var carParams);
        for (int i = 0; i < carParams.rpmPoints.Length - 1; i++) Assert.AreEqual(carParams.torquePoints[i], engine.SteadyTorque(1f, carParams.rpmPoints[i]), 1f, $"WOT @ {carParams.rpmPoints[i]}");
        for (int i = 1; i < carParams.rpmPoints.Length - 1; i++) { float volumetricEfficiency = engine.VolumetricEfficiency(carParams.rpmPoints[i]); Assert.Greater(volumetricEfficiency, 0.5f); Assert.Less(volumetricEfficiency, 1.05f, "derived VE physical"); }
    }
    // @description - Verifies that at idle rpm the closed-throttle engine torque is about 0 N m, is positive just below idle and negative just above (self-restoring), and that a 4 s simulation settles to idle rpm within 25 rpm while combusting.
    // @return {Void} - nothing
    [Test] public void Idle_Is_Stable_Steady_State()
    {
        var engine = Make(out var carParams);
        Assert.AreEqual(0f, engine.SteadyTorque(0f, carParams.idleRpm), 0.5f);
        Assert.Greater(engine.SteadyTorque(0f, carParams.idleRpm - 100f), 0f); Assert.Less(engine.SteadyTorque(0f, carParams.idleRpm + 100f), 0f);   // self-restoring
        const float dt = 0.0005f; for (int i = 0; i < 4 / dt; i++) engine.Step(0f, 0f, dt);
        Assert.AreEqual(carParams.idleRpm, engine.rpm, 25f); Assert.IsTrue(engine.combusting);
    }
    // @description - Verifies that at 10, 20 and 30 % pedal the torque at 2000 rpm exceeds the torque at 4000 rpm, that closed-throttle torque at 3000 rpm is negative (engine braking), and that 30 % pedal gives under 60 % of the wide-open torque.
    // @return {Void} - nothing
    [Test] public void PartThrottle_Torque_Falls_With_Rpm()
    {
        var engine = Make(out var carParams);
        foreach (float pedal in new[] { 0.1f, 0.2f, 0.3f }) Assert.Greater(engine.SteadyTorque(pedal, 2000f), engine.SteadyTorque(pedal, 4000f), $"pedal {pedal}");
        float torqueAtZeroPedal = engine.SteadyTorque(0f, 3000f), torqueAtThirtyPercent = engine.SteadyTorque(0.3f, 3000f); Debug.Log($"3000 rpm: 0% {torqueAtZeroPedal:0} Nm, 30% {torqueAtThirtyPercent:0} Nm, WOT {engine.WotTorque(3000f):0}");
        Assert.Less(torqueAtZeroPedal, 0f); Assert.Less(torqueAtThirtyPercent, 0.6f * engine.WotTorque(3000f));
    }
}
