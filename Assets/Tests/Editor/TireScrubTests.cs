// Tire sound v3 (TireScrub.cs): control-law shape, spectrum targets, engine ceiling, slider, Pacejka peaks.
using NUnit.Framework; using UnityEngine; using T = TireSoundTuning;
public class TireScrubTests
{
    const float fs = 48000f;
    // @description - Scrub ratio combined and floors.
    // @return {Void} - nothing
    [Test] public void ScrubRatio_Combined_And_Floors()
    {
        Assert.AreEqual(0.6636158f, TireScrubLaw.ScrubRatio(5f * Mathf.Deg2Rad, 0.05f, 0.2f, 0.1f), 1e-5);
        Assert.AreEqual(0.2f, TireScrubLaw.ScrubRatio(0.01f, 0f, 0.01f, 0.01f), 1e-5, "ap/kp floors 0.05 rad / 0.04");
    }
    // @description - Laws are monotone and on target.
    // @return {Void} - nothing
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
    // @description - Ceiling gain.
    // @return {Void} - nothing
    [Test] public void Ceiling_Gain()
    {
        Assert.AreEqual(1f, T.CeilingGain(0.01f, 0.1f), 1e-6);
        Assert.AreEqual(0.1f * T.Db(T.CeilingDb) / 0.2f, T.CeilingGain(0.2f, 0.1f), 1e-6);
        Assert.AreEqual(0f, T.CeilingGain(0.2f, 0f), 1e-6, "engine silent -> tires silent");
    }
    // @description - Render.
    // @param {float} ratio - ratio
    // @param {float} slider - slider
    // @param {float} eng - eng
    // @param {float} secs - secs
    // @param {out TireSoundDsp} d - value of type TireSoundDsp
    // @return {float[]} - the render
    static float[] Render(float ratio, float slider, float eng, float secs, out TireSoundDsp d)
    {
        d = new TireSoundDsp(fs); int n = (int)(secs * fs), blk = 512; var all = new float[n]; var b = new float[blk];
        for (int s = 0; s + blk <= n; s += blk) { d.SetTargets(new[] { ratio, ratio, ratio, ratio }, 15f, new[] { 3000f, 3000f, 3000f, 3000f }); d.Render(b, blk, 1, slider, eng); System.Array.Copy(b, 0, all, s, blk); }
        return all;
    }
    // @description - Rms.
    // @param {float[]} x - value of type float[]
    // @param {int} from - from
    // @return {float} - the rms
    static float Rms(float[] x, int from) { double a = 0; for (int i = from; i < x.Length; i++) a += x[i] * x[i]; return (float)System.Math.Sqrt(a / (x.Length - from)); }
    // @description - Goertzel.
    // @param {float[]} x - value of type float[]
    // @param {int} from - from
    // @param {float} hz - value of type float
    // @return {float} - the goertzel
    static float Goertzel(float[] x, int from, float hz) { double w = 2 * System.Math.PI * hz / fs, c = 2 * System.Math.Cos(w), s1 = 0, s2 = 0; for (int i = from; i < x.Length; i++) { double s0 = x[i] + c * s1 - s2; s2 = s1; s1 = s0; } return (float)System.Math.Sqrt(s1 * s1 + s2 * s2 - c * s1 * s2); }
    // @description - Ceiling holds under engine any slider.
    // @return {Void} - nothing
    [Test] public void Ceiling_Holds_Under_Engine_Any_Slider()
    {
        float eng = 0.05f, lim = eng * T.Db(T.CeilingDb);
        foreach (float sl in new[] { 1f, 2f, 4f }) { var x = Render(2f, sl, eng, 1.5f, out var d); Assert.LessOrEqual(Rms(x, 24000), lim * 1.02f, $"slider {sl}"); Assert.LessOrEqual(d.lastTireRms, lim * 1.001f); }
    }
    // @description - Slider scales and zero mutes.
    // @return {Void} - nothing
    [Test] public void Slider_Scales_And_Zero_Mutes()
    {
        var a = Render(1f, 1f, 10f, 1f, out _); var h = Render(1f, 0.5f, 10f, 1f, out _); var z = Render(1f, 0f, 10f, 1f, out _);
        Assert.AreEqual(0.5f, Rms(h, 12000) / Rms(a, 12000), 0.02f); Assert.AreEqual(0f, Rms(z, 0), 1e-9);
    }
    // @description - Peak hz.
    // @param {float[]} x - value of type float[]
    // @param {int} from - from
    // @param {float} lo - value of type float
    // @param {float} hi - value of type float
    // @return {float} - the peak hz
    static float PeakHz(float[] x, int from, float lo, float hi) { float best = lo, bv = -1f; for (float f = lo; f <= hi; f += 0.5f) { float v = Goertzel(x, from, f); if (v > bv) { bv = v; best = f; } } return best; }
    // @description - noise band centre: Goertzel power at 1 Hz steps smoothed over +-12 Hz, then the power-weighted centroid of the region above half max (single bins / argmax of bandpassed noise fluctuate by several Hz; the half-max centroid is stable to ~1 %)
    // @param {float[]} x - value of type float[]
    // @param {int} from - from
    // @param {float} lo - value of type float
    // @param {float} hi - value of type float
    // @return {float} - the band centre hz
    static float BandCentreHz(float[] x, int from, float lo, float hi) { int n = (int)(hi - lo) + 1; var v = new float[n]; var sm = new float[n]; for (int i = 0; i < n; i++) v[i] = Goertzel(x, from, lo + i) * Goertzel(x, from, lo + i);
        float mx = 0f; for (int i = 0; i < n; i++) { float a = 0f; for (int j = System.Math.Max(0, i - 12); j <= System.Math.Min(n - 1, i + 12); j++) a += v[j]; sm[i] = a; mx = System.Math.Max(mx, a); }
        double sw = 0, sf = 0; for (int i = 0; i < n; i++) if (sm[i] > 0.5f * mx) { sw += sm[i]; sf += sm[i] * (lo + i); } return (float)(sf / sw); }
    // @description - Spectrum low tone present below peak and pitch span.
    // @return {Void} - nothing
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
        float pa = BandCentreHz(a, 24000, 100f, 260f), po = BandCentreHz(o, 24000, 100f, 260f), span = 12f * Mathf.Log(po / pa, 2f);
        Debug.Log($"TIRESOUND peak {pa:0.0} Hz @0.35 -> {po:0.0} Hz @2.5 = {span:0.00} semitones; rms {20 * Mathf.Log10(Rms(a, 24000)):0.0}/{20 * Mathf.Log10(Rms(m, 24000)):0.0}/{20 * Mathf.Log10(Rms(o, 24000)):0.0} dBFS");
        Assert.That(span, Is.InRange(1.6f, 3f));
    }
    // @description - Engine has no throttle hiss.
    // @return {Void} - nothing
    [Test] public void Engine_Has_No_Throttle_Hiss()
    {
        float[] R(float thr) { var g = new GameObject("e"); var e = g.AddComponent<EngineSynth>(); e.SetOfflineState(fs, 4000f, 0.8f, thr); var b = new float[9600]; e.Render(b, b.Length); Object.DestroyImmediate(g); return b; }
        var z = R(0f); var f = R(1f); for (int i = 0; i < z.Length; i++) Assert.AreEqual(z[i], f[i], 1e-7, "throttle area must not add noise");
    }
    // @description - Pause gate fades out silent and back no click.
    // @return {Void} - nothing
    [Test] public void Pause_Gate_Fades_Out_Silent_And_Back_No_Click()
    {
        var g = new AudioPauseGate(); int n = 4800; var b = new float[n * 2];
        System.Func<bool, float[]> blk = p => { for (int i = 0; i < b.Length; i++) b[i] = 1f; g.Apply(b, n, 2, p, fs); return b; };
        var a = blk(true); float prev = 1f, maxStep = 0f;
        for (int i = 0; i < n; i++) { maxStep = Mathf.Max(maxStep, Mathf.Abs(a[i * 2] - prev)); prev = a[i * 2]; }
        Assert.AreEqual(0f, a[(n - 1) * 2], 1e-6, "silent after the fade"); Assert.Less(maxStep, 0.002f, "no click on pause");
        a = blk(true); foreach (var v in a) Assert.AreEqual(0f, v, 0f, "fully silent while paused");
        a = blk(false); Assert.Less(a[0], 0.002f, "starts from silence"); Assert.AreEqual(1f, a[(n - 1) * 2], 1e-6, "back to full within the fade");
        for (int i = 1; i < n; i++) Assert.LessOrEqual(a[i * 2] - a[(i - 1) * 2], 0.002f, "no click on resume");
    }
    // @description - Eng db.
    // @param {float} rpm - rpm
    // @param {float} load - load
    // @param {int} n - value of type int
    // @return {float} - the eng db
    static float EngDb(float rpm, float load, int n = 48000) { var g = new GameObject("e"); var e = g.AddComponent<EngineSynth>(); e.SetOfflineState(fs, rpm, load, load); var b = new float[n]; e.Render(b, n); Object.DestroyImmediate(g); return 20f * Mathf.Log10(Rms(b, n / 4)); }
    // @description - Engine overrun stays audible 6 to 10 d b down.
    // @return {Void} - nothing
    [Test] public void Engine_Overrun_Stays_Audible_6_To_10_dB_Down()
    {
        foreach (var rpm in new[] { 2000f, 3000f, 4500f, 6000f })
        {
            float on = EngDb(rpm, 1f), off = EngDb(rpm, 0f), d = on - off;
            Debug.Log($"ENGINE_ONOFF {rpm:0} rpm: on {on:0.0} off {off:0.0} diff {d:0.0} dB");
            Assert.That(d, Is.InRange(5f, 11f), $"overrun at {rpm} rpm should be ~6-10 dB under load");
            Assert.Greater(off, -30f, "overrun clearly audible");
        }
    }
    // @description - Tire ceiling tracks overrun engine.
    // @return {Void} - nothing
    [Test] public void Tire_Ceiling_Tracks_Overrun_Engine()
    {
        foreach (var load in new[] { 0f, 1f })
        {
            var g = new GameObject("e"); var e = g.AddComponent<EngineSynth>(); e.SetOfflineState(fs, 4000f, load, load); var eb = new float[512]; var d = new TireSoundDsp(fs); var tb = new float[512];
            float er = 0f, tAcc = 0f; int cnt = 0;
            for (int k = 0; k < 400; k++) { e.Render(eb, 512); double acc = 0; foreach (var v in eb) acc += v * v; er = (float)System.Math.Sqrt(acc / 512);
                d.SetTargets(new[] { 2.5f, 2.5f, 2.5f, 2.5f }, 20f, new[] { 4000f, 4000f, 4000f, 4000f }); d.Render(tb, 512, 1, 4f, er); if (k > 200) { tAcc += Rms(tb, 0) * Rms(tb, 0); cnt++; } }
            Object.DestroyImmediate(g);
            float tDb = 10f * Mathf.Log10(tAcc / cnt), eDb = 20f * Mathf.Log10(er);
            Debug.Log($"CEILING load {load}: tire {tDb:0.0} engine {eDb:0.0}");
            Assert.LessOrEqual(tDb, eDb - 4f + 1f, "tire stays under engine - 4 dB"); Assert.Greater(tDb, eDb - 12f, "tire still audible under overrun engine");
        }
    }
    // @description - Offline on off sweep to disk.
    // @return {Void} - nothing
    [Test] public void Offline_OnOff_Sweep_To_Disk()
    {
        var dir = System.Environment.GetEnvironmentVariable("SCIROCCO_SWEEP_DIR"); if (string.IsNullOrEmpty(dir)) { Assert.Pass("set SCIROCCO_SWEEP_DIR"); return; }
        // at 2000/3000/4500/6000 rpm: 2 s on (load 1) then 2 s off (closed-throttle overrun, load 0)
        var g = new GameObject("e"); var e = g.AddComponent<EngineSynth>(); var eb = new float[480];
        using (var we = new System.IO.BinaryWriter(System.IO.File.Create(dir + "/onoff_engine.f32")))
            foreach (var rpm in new[] { 2000f, 3000f, 4500f, 6000f }) for (int ph = 0; ph < 2; ph++) for (int s = 0; s < 200; s++) { e.SetOfflineState(fs, rpm, ph == 0 ? 1f : 0f, ph == 0 ? 1f : 0f); e.Render(eb, 480); foreach (var v in eb) we.Write(v); }
        Object.DestroyImmediate(g);
    }
    // @description - Tire is noise not buzz.
    // @return {Void} - nothing
    [Test] public void Tire_Is_Noise_Not_Buzz()
    {
        var d = new TireSoundDsp(fs); d.ceilingOn = false; var b = new float[48000]; d.SetTargets(new[] { 2.5f, 2.5f, 2.5f, 2.5f }, 20f, new[] { 3000f, 3000f, 3000f, 3000f }); d.Render(b, b.Length, 1, 1f, 1f);
        int lag = (int)(3f * fs / TireSoundTuning.ToneHz(2.5f)); double r0 = 0, rl = 0; for (int i = 12000; i < b.Length - lag; i++) { r0 += b[i] * b[i]; rl += b[i] * b[i + lag]; }
        Debug.Log($"TIRE autocorr @3 periods {rl / r0:0.000}"); Assert.Less(System.Math.Abs(rl / r0), 0.35, "no periodic (pitched/buzzy) excitation");
    }
    // @description - Offline sweeps to disk.
    // @return {Void} - nothing
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
    // @description - Silent when stopped.
    // @return {Void} - nothing
    [Test] public void Silent_When_Stopped()
    {
        var d = new TireSoundDsp(fs); var b = new float[4800]; d.SetTargets(new[] { 2f, 2f, 2f, 2f }, 0f, new[] { 3000f, 3000f, 3000f, 3000f }); d.Render(b, b.Length, 1, 1f, 1f); Assert.AreEqual(0f, Rms(b, 0), 1e-9);
    }
    // @description - Pacejka peaks at 3000 n.
    // @return {Void} - nothing
    [Test] public void Pacejka_Peaks_At_3000N()
    {
        var p = ScriptableObject.CreateInstance<CarParams>();
        float a = TireScrubLaw.PeakAlpha(p, 3000f), k = TireScrubLaw.PeakKappa(p, 3000f);
        Assert.Greater(Mathf.Abs(PacejkaTire.PureLat(p, a, 3000f, 0f)), Mathf.Abs(PacejkaTire.PureLat(p, a * 0.8f, 3000f, 0f)));
        Assert.GreaterOrEqual(Mathf.Abs(PacejkaTire.PureLat(p, a, 3000f, 0f)), Mathf.Abs(PacejkaTire.PureLat(p, a * 1.2f, 3000f, 0f)));
        Assert.Greater(Mathf.Abs(PacejkaTire.PureLong(p, k, 3000f)), Mathf.Abs(PacejkaTire.PureLong(p, k * 0.8f, 3000f)));
    }
}
