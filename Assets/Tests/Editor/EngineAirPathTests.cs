// Physical air-path engine model: WOT matches the sourced curve, idle is a stable steady state, part-throttle torque falls with rpm.
using NUnit.Framework; using UnityEngine;
public class EngineAirPathTests
{
    // @description - Make (see the body for details).
    // @param {out CarParams} carParams - car params
    // @return {Engine} - the make
    static Engine Make(out CarParams carParams) { carParams = ScriptableObject.CreateInstance<CarParams>(); return new Engine(carParams); }
    // @description - Wot matches sourced curve.
    // @return {Void} - nothing
    [Test] public void Wot_Matches_Sourced_Curve()
    {
        var e = Make(out var p);
        for (int i = 0; i < p.rpmPoints.Length - 1; i++) Assert.AreEqual(p.torquePoints[i], e.SteadyTorque(1f, p.rpmPoints[i]), 1f, $"WOT @ {p.rpmPoints[i]}");
        for (int i = 1; i < p.rpmPoints.Length - 1; i++) { float v = e.VolumetricEfficiency(p.rpmPoints[i]); Assert.Greater(v, 0.5f); Assert.Less(v, 1.05f, "derived VE physical"); }
    }
    // @description - Idle is stable steady state.
    // @return {Void} - nothing
    [Test] public void Idle_Is_Stable_Steady_State()
    {
        var e = Make(out var p);
        Assert.AreEqual(0f, e.SteadyTorque(0f, p.idleRpm), 0.5f);
        Assert.Greater(e.SteadyTorque(0f, p.idleRpm - 100f), 0f); Assert.Less(e.SteadyTorque(0f, p.idleRpm + 100f), 0f);   // self-restoring
        const float dt = 0.0005f; for (int i = 0; i < 4 / dt; i++) e.Step(0f, 0f, dt);
        Assert.AreEqual(p.idleRpm, e.rpm, 25f); Assert.IsTrue(e.combusting);
    }
    // @description - Part throttle torque falls with rpm.
    // @return {Void} - nothing
    [Test] public void PartThrottle_Torque_Falls_With_Rpm()
    {
        var e = Make(out var p);
        foreach (float ped in new[] { 0.1f, 0.2f, 0.3f }) Assert.Greater(e.SteadyTorque(ped, 2000f), e.SteadyTorque(ped, 4000f), $"pedal {ped}");
        float t0 = e.SteadyTorque(0f, 3000f), t30 = e.SteadyTorque(0.3f, 3000f); Debug.Log($"3000 rpm: 0% {t0:0} Nm, 30% {t30:0} Nm, WOT {e.WotTorque(3000f):0}");
        Assert.Less(t0, 0f); Assert.Less(t30, 0.6f * e.WotTorque(3000f));
    }
}
