// Ignition / starter: physical cranking speed, start, ignition cut, stall stays stopped.
using NUnit.Framework; using UnityEngine;
public class EngineStartTests
{
    const float dt = 0.0005f;   // 2 kHz substep as in CarController
    // @description - Steps the engine for a given simulated time at the 2 kHz substep rate used by CarController, with the pedal released.
    // @param {Engine} engine - engine to advance
    // @param {float} seconds - simulated duration in seconds
    // @param {float} clutchTorque - torque the clutch exerts on the engine in N m (positive drags the engine; 0 = disengaged)
    // @return {Void} - nothing
    static void Run(Engine engine, float seconds, float clutchTorque = 0f) { for (int i = 0; i < (int)(seconds / dt); i++) engine.Step(0f, clutchTorque, dt); }
    // @description - Verifies cranking and starting: with the ignition off the starter spins the engine at 200..400 rpm without combustion; it spins down when released; with the ignition on it starts and idles within 150 rpm of idle speed; switching the ignition off stops it (state OFF).
    // @return {Void} - nothing
    [Test] public void Cranking_Speed_And_Start()
    {
        var carParams = ScriptableObject.CreateInstance<CarParams>(); var engine = new Engine(carParams); engine.SetRunning(false);
        Assert.AreEqual("OFF", engine.State);
        engine.starter = true; Run(engine, 1.5f); float crank = engine.rpm; Debug.Log($"cranking (ignition off) {crank:0} rpm, stall torque at crank {engine.StarterTorqueAt(0f):0} N m");
        Assert.Greater(crank, 200f); Assert.Less(crank, 400f); Assert.IsFalse(engine.combusting);
        engine.starter = false; Run(engine, 2f); Assert.Less(engine.rpm, 1f);                       // no ignition: spins down and stays stopped
        engine.ignition = true; engine.starter = true; Run(engine, 1.0f); engine.starter = false; Run(engine, 3f);
        Debug.Log($"after start: {engine.rpm:0} rpm {engine.State}"); Assert.IsTrue(engine.combusting); Assert.AreEqual(carParams.idleRpm, engine.rpm, 150f);
        engine.ignition = false; Run(engine, 4f); Assert.Less(engine.rpm, 1f); Assert.AreEqual("OFF", engine.State);   // ignition off cuts combustion
    }
    // @description - Verifies that an engine lugged to a stall by a locked 150 N m clutch load stays stopped (rpm below 1, state STALLED), and that the starter restarts it into combustion.
    // @return {Void} - nothing
    [Test] public void Stall_Stays_Stopped()
    {
        var carParams = ScriptableObject.CreateInstance<CarParams>(); var engine = new Engine(carParams);
        Run(engine, 1f, 150f);                                                            // lugged by a locked clutch -> stalls
        Run(engine, 3f); Assert.Less(engine.rpm, 1f); Assert.AreEqual("STALLED", engine.State);
        engine.starter = true; Run(engine, 1.0f); engine.starter = false; Run(engine, 3f); Assert.IsTrue(engine.combusting);   // restart with the starter
    }
}
