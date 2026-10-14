// Tire sound v2 (TireScrub.cs): control-law shape, spectrum targets, engine ceiling, slider, Pacejka peaks.
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
        for (float r = 0f; r <= 2.5f; r += 0.01f)
        {
            float l = T.LevelDb(r), f = T.ToneHz(r);
            Assert.GreaterOrEqual(l, pl - 1e-4f, $"level monotone at {r}"); Assert.GreaterOrEqual(f, pf - 1e-3f, $"pitch monotone at {r}");
            if (r > 0.02f) Assert.Less(l - pl, 1.2f, $"no level step > 1.2 dB per 0.01 ratio at {r}");
            Assert.That(f, Is.InRange(300f, 600f)); pl = l; pf = f;
        }
        Assert.AreEqual(T.LevelRubDb, T.LevelDb(0f), 1e-4); Assert.AreEqual(T.LevelPeakDb, T.LevelDb(1f), 1e-4); Assert.AreEqual(T.LevelOverDb, T.LevelDb(2.5f), 1e-4);
        Assert.AreEqual(0f, T.Tonal(0.4f), 1e-6, "pure rub under the build zone"); Assert.Greater(T.Tonal(1.2f), 0.85f);
        Assert.Less(T.LevelDb(0.5f) - T.LevelRubDb, 4f, "quiet until the build zone");
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
    [Test] public void Spectrum_Rub_Low_Tone_LowMid_No_Screech()
    {
        var u = Render(0.2f, 1f, 10f, 1.5f, out _); var o = Render(1.8f, 1f, 10f, 1.5f, out _);
        Assert.Greater(Goertzel(u, 24000, 100f), Goertzel(u, 24000, 450f) * 2f, "under limit: low rub");
        float f0 = T.ToneHz(1.8f); Assert.Greater(Goertzel(o, 24000, f0), 10f * Goertzel(o, 24000, 2000f), "over limit: low-mid tone, not 1-3 kHz");
        Assert.Greater(Rms(o, 24000), 10f * Rms(u, 24000), "over limit >= 20 dB above the rub");
        Debug.Log($"TIRESOUND rub rms {20 * Mathf.Log10(Rms(u, 24000)):0.0} dBFS, slide rms {20 * Mathf.Log10(Rms(o, 24000)):0.0} dBFS, tone {f0:0} Hz");
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
