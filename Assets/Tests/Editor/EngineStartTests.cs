// Ignition / starter: physical cranking speed, start, ignition cut, stall stays stopped.
using NUnit.Framework; using UnityEngine;
public class EngineStartTests
{
    const float dt = 0.0005f;   // 2 kHz substep as in CarController
    // @description - Run (see the body for details).
    // @param {Engine} e - value of type Engine
    // @param {float} s - value of type float
    // @param {float} clutchT - clutch t
    // @return {Void} - nothing
    static void Run(Engine e, float s, float clutchT = 0f) { for (int i = 0; i < (int)(s / dt); i++) e.Step(0f, clutchT, dt); }
    // @description - Cranking speed and start.
    // @return {Void} - nothing
    [Test] public void Cranking_Speed_And_Start()
    {
        var p = ScriptableObject.CreateInstance<CarParams>(); var e = new Engine(p); e.SetRunning(false);
        Assert.AreEqual("OFF", e.State);
        e.starter = true; Run(e, 1.5f); float crank = e.rpm; Debug.Log($"cranking (ignition off) {crank:0} rpm, stall torque at crank {e.StarterTorqueAt(0f):0} N m");
        Assert.Greater(crank, 200f); Assert.Less(crank, 400f); Assert.IsFalse(e.combusting);
        e.starter = false; Run(e, 2f); Assert.Less(e.rpm, 1f);                       // no ignition: spins down and stays stopped
        e.ignition = true; e.starter = true; Run(e, 1.0f); e.starter = false; Run(e, 3f);
        Debug.Log($"after start: {e.rpm:0} rpm {e.State}"); Assert.IsTrue(e.combusting); Assert.AreEqual(p.idleRpm, e.rpm, 150f);
        e.ignition = false; Run(e, 4f); Assert.Less(e.rpm, 1f); Assert.AreEqual("OFF", e.State);   // ignition off cuts combustion
    }
    // @description - Stall stays stopped.
    // @return {Void} - nothing
    [Test] public void Stall_Stays_Stopped()
    {
        var p = ScriptableObject.CreateInstance<CarParams>(); var e = new Engine(p);
        Run(e, 1f, 150f);                                                            // lugged by a locked clutch -> stalls
        Run(e, 3f); Assert.Less(e.rpm, 1f); Assert.AreEqual("STALLED", e.State);
        e.starter = true; Run(e, 1.0f); e.starter = false; Run(e, 3f); Assert.IsTrue(e.combusting);   // restart with the starter
    }
}
