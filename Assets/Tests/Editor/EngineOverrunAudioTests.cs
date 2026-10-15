// Overrun (engine braking) audio driven by the physical air-path Engine: coastdown from 5000 rpm with closed throttle, in gear.
using NUnit.Framework; using UnityEngine;
public class EngineOverrunAudioTests
{
    const float fs = 48000f, dt = 0.0005f;
    static float[] Render(float rpm, float load, float map, bool comb, int n = 24000)
    { var g = new GameObject("e"); var s = g.AddComponent<EngineSynth>(); s.SetOfflineState(fs, rpm, load, load, comb, map); var b = new float[n]; s.Render(b, n); Object.DestroyImmediate(g); return b; }
    static float Db(float[] b) { double a = 0; int i0 = b.Length / 4; for (int i = i0; i < b.Length; i++) a += b[i] * b[i]; return 10f * Mathf.Log10((float)(a / (b.Length - i0))); }
    static float Pitch(float[] b, float fGuess)   // autocorrelation peak within +-40% of the guess
    {
        int i0 = b.Length / 4, lo = (int)(fs / (fGuess * 1.4f)), hi = (int)(fs / (fGuess * 0.6f)), best = lo; double bv = double.MinValue;
        for (int L = lo; L <= hi; L++) { double a = 0; for (int i = i0; i + L < b.Length; i++) a += b[i] * b[i + L]; if (a > bv) { bv = a; best = L; } }
        return fs / best;
    }
    // Wheels drag the engine down a 700 rpm/s ramp (3rd/4th-gear coast), pedal 0; yields engine samples every 0.5 s.
    static System.Collections.Generic.IEnumerable<(Engine e, CarParams p)> Coast()
    {
        var p = ScriptableObject.CreateInstance<CarParams>(); var e = new Engine(p);
        typeof(Engine).GetField("angularVelocity", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).SetValue(e, 5000f * Mathf.PI / 30f); e.rpm = 5000f;
        for (int i = 1; i <= 9000; i++) { float target = 5000f - i * dt * 700f; e.Step(0f, -((target - e.rpm) * 2f + e.FrictionTorque(e.rpm)), dt); if (i % 1000 == 0) yield return (e, p); }
    }
    [Test] public void Overrun_Audible_6_To_10_dB_Under_Wot_With_Real_Engine()
    {
        foreach (var (e, p) in Coast())
        {
            float load = Mathf.Clamp01((e.manifoldPressureRatio - p.mapZeroTorque) / (1f - p.mapZeroTorque));
            float on = Db(Render(e.rpm, 1f, 1f, true)), off = Db(Render(e.rpm, load, e.manifoldPressureRatio, e.combusting)), d = on - off;
            Debug.Log($"OVERRUN rpm {e.rpm:0} map {e.manifoldPressureRatio:0.000} load {load:0.00} comb {e.combusting} WOT {on:0.0} dBFS overrun {off:0.0} dBFS diff {d:0.0} dB");
            Assert.That(e.manifoldPressureRatio, Is.InRange(0.05f, 0.35f), "closed-throttle overrun MAP (leakage + residual floor; real SI overrun ~0.15-0.35 bar, aa1car MAP article)"); Assert.IsTrue(e.combusting);
            Assert.That(d, Is.InRange(6f, 10f), $"overrun at {e.rpm:0} rpm");
        }
    }
    [Test] public void Overrun_Pitch_Tracks_Rpm_During_Coastdown()
    {
        float prev = float.MaxValue;
        foreach (var (e, p) in Coast())
        {
            float f0 = e.rpm / 30f, f = Pitch(Render(e.rpm, 0f, e.manifoldPressureRatio, e.combusting), f0);
            Debug.Log($"PITCH rpm {e.rpm:0} expected {f0:0.0} Hz measured {f:0.0} Hz");
            Assert.AreEqual(f0, f, f0 * 0.03f, "firing frequency = rpm/60 x 2"); Assert.Less(f, prev, "pitch falls with rpm"); prev = f;
        }
    }
    [Test] public void Fuel_Cut_Motoring_Still_Audible()
    {
        float on = Db(Render(6800f, 1f, 1f, true)), cut = Db(Render(6800f, 0f, 0.02f, false));
        Debug.Log($"FUELCUT WOT {on:0.0} motoring {cut:0.0}"); Assert.Less(on - cut, 12f);
    }
}
