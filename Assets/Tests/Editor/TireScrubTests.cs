// Tire sound v3 (TireScrub.cs): control-law shape, spectrum targets, engine ceiling, slider, Pacejka peaks.
using NUnit.Framework; using UnityEngine; using T = TireSoundTuning;
public class TireScrubTests
{
    const float fs = 48000f;
    [Test] public void ScrubRatio_Combined_And_Floors()
    {
        Assert.AreEqual(0.6636158f, TireScrubLaw.ScrubRatio(5f * Mathf.Deg2Rad, 0.05f, 0.2f, 0.1f), 1e-5);
        Assert.AreEqual(0.2f, TireScrubLaw.ScrubRatio(0.01f, 0f, 0.01f, 0.01f), 1e-5, "ap/kp floors 0.05 rad / 0.04");
    }
    [Test] public void Laws_Are_Monotone_And_On_Target()
    {
        float pl = -999f, pf = 0f;
        for (float r = T.Onset + 0.001f; r <= 2.5f; r += 0.01f)
        {
            float l = T.LevelDb(r), f = T.ToneHz(r);
            Assert.GreaterOrEqual(l, pl - 1e-4f, $"level monotone at {r}"); Assert.GreaterOrEqual(f, pf - 1e-3f, $"pitch monotone at {r}");
            Assert.That(f, Is.InRange(T.BaseHz, T.BaseHz * 1.19f), "low register, <= 3 semitones"); pl = l; pf = f;
        }
        Assert.AreEqual(0f, T.Gain(0.2f), 1e-9, "silent before slip onset");
        float span = 12f * Mathf.Log(T.ToneHz(2.5f) / T.ToneHz(T.Onset), 2f);
        Assert.That(span, Is.InRange(2f, 3f), "pitch span onset -> heavy slide");
        Assert.That(T.Semis(1f), Is.InRange(1.5f, 2.5f), "about one whole step by peak grip");
        Assert.Greater(T.LevelDb(0.5f), T.LevelOnsetDb + 8f, "clearly building below the peak");
        Assert.AreEqual(T.LevelPeakDb, T.LevelDb(1f), 1e-4); Assert.AreEqual(T.LevelOverDb, T.LevelDb(2.5f), 1e-4);
    }
    [Test] public void Ceiling_Gain()
    {
        Assert.AreEqual(1f, T.CeilingGain(0.01f, 0.1f), 1e-6);
        Assert.AreEqual(0.1f * T.Db(T.CeilingDb) / 0.2f, T.CeilingGain(0.2f, 0.1f), 1e-6);
        Assert.AreEqual(0f, T.CeilingGain(0.2f, 0f), 1e-6, "engine silent -> tires silent");
    }
    static float[] Render(float ratio, float slider, float eng, float secs, out TireSoundDsp d)
    {
        d = new TireSoundDsp(fs); int n = (int)(secs * fs), blk = 512; var all = new float[n]; var b = new float[blk];
        for (int s = 0; s + blk <= n; s += blk) { d.SetTargets(new[] { ratio, ratio, ratio, ratio }, 15f, new[] { 3000f, 3000f, 3000f, 3000f }); d.Render(b, blk, 1, slider, eng); System.Array.Copy(b, 0, all, s, blk); }
        return all;
    }
    static float Rms(float[] x, int from) { double a = 0; for (int i = from; i < x.Length; i++) a += x[i] * x[i]; return (float)System.Math.Sqrt(a / (x.Length - from)); }
    static float Goertzel(float[] x, int from, float hz) { double w = 2 * System.Math.PI * hz / fs, c = 2 * System.Math.Cos(w), s1 = 0, s2 = 0; for (int i = from; i < x.Length; i++) { double s0 = x[i] + c * s1 - s2; s2 = s1; s1 = s0; } return (float)System.Math.Sqrt(s1 * s1 + s2 * s2 - c * s1 * s2); }
    [Test] public void Ceiling_Holds_Under_Engine_Any_Slider()
    {
        float eng = 0.05f, lim = eng * T.Db(T.CeilingDb);
        foreach (float sl in new[] { 1f, 2f, 4f }) { var x = Render(2f, sl, eng, 1.5f, out var d); Assert.LessOrEqual(Rms(x, 24000), lim * 1.02f, $"slider {sl}"); Assert.LessOrEqual(d.lastTireRms, lim * 1.001f); }
    }
    [Test] public void Slider_Scales_And_Zero_Mutes()
    {
        var a = Render(1f, 1f, 10f, 1f, out _); var h = Render(1f, 0.5f, 10f, 1f, out _); var z = Render(1f, 0f, 10f, 1f, out _);
        Assert.AreEqual(0.5f, Rms(h, 12000) / Rms(a, 12000), 0.02f); Assert.AreEqual(0f, Rms(z, 0), 1e-9);
    }
    static float PeakHz(float[] x, int from, float lo, float hi) { float best = lo, bv = -1f; for (float f = lo; f <= hi; f += 0.5f) { float v = Goertzel(x, from, f); if (v > bv) { bv = v; best = f; } } return best; }
    [Test] public void Spectrum_Low_Tone_Present_Below_Peak_And_Pitch_Span()
    {
        var a = Render(0.35f, 1f, 10f, 2f, out _); var m = Render(0.7f, 1f, 10f, 2f, out _); var o = Render(2.5f, 1f, 10f, 2f, out _);
        foreach (var (x, r) in new[] { (a, 0.35f), (m, 0.7f), (o, 2.5f) })
        {
            float f0 = T.ToneHz(r);
            Assert.Greater(Goertzel(x, 24000, f0), 5f * Goertzel(x, 24000, 1000f), $"r={r}: low tone dominates, no screech");
            Assert.Greater(Goertzel(x, 24000, f0), 3f * Goertzel(x, 24000, f0 * 1.5f), $"r={r}: tone at f0");
        }
        Assert.Greater(20f * Mathf.Log10(Rms(m, 24000)), -45f, "low tone audible below the peak (pre-ceiling)");
        float pa = PeakHz(a, 24000, 120f, 220f), po = PeakHz(o, 24000, 120f, 220f), span = 12f * Mathf.Log(po / pa, 2f);
        Debug.Log($"TIRESOUND peak {pa:0.0} Hz @0.35 -> {po:0.0} Hz @2.5 = {span:0.00} semitones; rms {20 * Mathf.Log10(Rms(a, 24000)):0.0}/{20 * Mathf.Log10(Rms(m, 24000)):0.0}/{20 * Mathf.Log10(Rms(o, 24000)):0.0} dBFS");
        Assert.That(span, Is.InRange(1.6f, 3f));
    }
    [Test] public void Engine_Has_No_Throttle_Hiss()
    {
        float[] R(float thr) { var g = new GameObject("e"); var e = g.AddComponent<EngineSynth>(); e.SetOfflineState(fs, 4000f, 0.8f, thr); var b = new float[9600]; e.Render(b, b.Length); Object.DestroyImmediate(g); return b; }
        var z = R(0f); var f = R(1f); for (int i = 0; i < z.Length; i++) Assert.AreEqual(z[i], f[i], 1e-7, "throttle area must not add noise");
    }
    [Test] public void Offline_Sweeps_To_Disk()
    {
        var dir = System.Environment.GetEnvironmentVariable("SCIROCCO_SWEEP_DIR"); if (string.IsNullOrEmpty(dir)) { Assert.Pass("set SCIROCCO_SWEEP_DIR to write sweeps"); return; }
        int blk = 512, N = (int)(20f * fs); var tb = new float[blk]; var eb = new float[blk];
        var g = new GameObject("e"); var e = g.AddComponent<EngineSynth>();
        // slip sweep: ratio 0 -> 2.5 over 20 s at 4000 rpm / load 0.6, 15 m/s, 3000 N; writes tire (post ceiling), engine
        var d = new TireSoundDsp(fs);
        using (var wt = new System.IO.BinaryWriter(System.IO.File.Create(dir + "/slip_tire.f32"))) using (var we = new System.IO.BinaryWriter(System.IO.File.Create(dir + "/slip_engine.f32")))
            for (int s = 0; s < N; s += blk) { e.SetOfflineState(fs, 4000f, 0.6f, 0.6f); e.Render(eb, blk); double acc = 0; foreach (var v in eb) acc += v * v;
                float r = 2.5f * s / N; d.SetTargets(new[] { r, r, r, r }, 15f, new[] { 3000f, 3000f, 3000f, 3000f }); d.Render(tb, blk, 1, 1f, (float)System.Math.Sqrt(acc / blk));
                foreach (var v in tb) wt.Write(v); foreach (var v in eb) we.Write(v); }
        // throttle sweep: rpm 1500 -> 6500, load/throttle 0 -> 1 over 20 s, engine only (no intake hiss)
        using (var we = new System.IO.BinaryWriter(System.IO.File.Create(dir + "/throttle_engine.f32")))
            for (int s = 0; s < N; s += blk) { float t = (float)s / N; e.SetOfflineState(fs, 1500f + 5000f * t, t, t); e.Render(eb, blk); foreach (var v in eb) we.Write(v); }
        Object.DestroyImmediate(g);
    }
    [Test] public void Silent_When_Stopped()
    {
        var d = new TireSoundDsp(fs); var b = new float[4800]; d.SetTargets(new[] { 2f, 2f, 2f, 2f }, 0f, new[] { 3000f, 3000f, 3000f, 3000f }); d.Render(b, b.Length, 1, 1f, 1f); Assert.AreEqual(0f, Rms(b, 0), 1e-9);
    }
    [Test] public void Pacejka_Peaks_At_3000N()
    {
        var p = ScriptableObject.CreateInstance<CarParams>();
        float a = TireScrubLaw.PeakAlpha(p, 3000f), k = TireScrubLaw.PeakKappa(p, 3000f);
        Assert.Greater(Mathf.Abs(PacejkaTire.PureLat(p, a, 3000f, 0f)), Mathf.Abs(PacejkaTire.PureLat(p, a * 0.8f, 3000f, 0f)));
        Assert.GreaterOrEqual(Mathf.Abs(PacejkaTire.PureLat(p, a, 3000f, 0f)), Mathf.Abs(PacejkaTire.PureLat(p, a * 1.2f, 3000f, 0f)));
        Assert.Greater(Mathf.Abs(PacejkaTire.PureLong(p, k, 3000f)), Mathf.Abs(PacejkaTire.PureLong(p, k * 0.8f, 3000f)));
    }
}
