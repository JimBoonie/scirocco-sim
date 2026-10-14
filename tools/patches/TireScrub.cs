// Tire sound v2: designed from scratch (old Web Audio spec dropped). ALL audio-mapping constants live in TireSoundTuning below.
//
// Physical picture (stick-slip): each tread block enters the contact patch, sticks, shears, and slips once the local shear
// stress exceeds the friction limit. Below the limit almost the whole patch sticks; only a small trailing-edge region slips,
// so what you hear is random rubbing noise: broadband, low frequency, quiet. As combined slip approaches the peak
// (scrubRatio 0.5..1) the sliding region grows forward and the slips start to synchronise (stick-slip limit cycle). Energy
// gathers around the tread-block/carcass resonance and the sound turns from noise into a tone. Past the peak the whole patch
// slides; the limit cycle is sustained and tonal, low-mid (the user's target: 300-600 Hz), and its pitch rises a little with
// sliding speed.
// Signal per tire: [rub]  white noise -> 2-pole lowpass (cutoff rises with ratio)
//                  [tone] jittered stick-slip impulse train at f0(ratio) -> resonator (centre = f0, Q rises with ratio)
//                  out = gain(ratio, speed, load) * ((1 - tonal) * rub + tonal * tone)
// Mix: the 4 tires are panned and summed, then the Tires slider applies, then the ENGINE CEILING: a smoothed limiter keeps the
// tire RMS <= engine RMS * CeilingDb (the engine RMS is published by EngineSynth each audio block). Tires are therefore always
// below the engine, whatever the slider says.
// No UnityEngine dependency in this part (System.Math only), so the offline sweep tool compiles this exact file with mcs.
public static class TireSoundTuning
{
    // --- ratio breakpoints (scrubRatio = combined slip / peak slip of our Pacejka at the current load) ---
    public const float BuildStart = 0.5f;      // pitch/level/tonality start building here
    public const float BuildEnd = 1.0f;        // ...fully built at the peak
    // --- levels (dB relative to full scale of the per-tire voice, before slider/ceiling) ---
    public const float LevelRubDb = -50f;      // ratio 0, rolling: faint low rub (4 tires mixed: ~-47 dBFS, ~30 dB under the engine)
    public const float LevelPeakDb = -29f;     // ratio 1 (4 tires: ~-26 dBFS)
    public const float LevelOverDb = -26f;     // ratio >= OverFull: slow further rise when sliding (4 tires: ~-23 dBFS = ~6 dB under the median engine)
    public const float ToneNorm = 0.38f;       // tone voice calibration to unit RMS (measured offline, sweep tool)
    public const float OverFull = 2.0f;
    // --- rub (under the limit) ---
    public const float RubLpLoHz = 110f, RubLpHiHz = 380f;   // lowpass cutoff at ratio 0 / ratio 1
    // --- tone (near / over the limit) ---
    public const float ToneHzAtBuild = 300f;   // stick-slip frequency where it first appears (ratio 0.5)
    public const float ToneHzAtPeak = 380f;    // ratio 1
    public const float ToneHzOver = 520f;      // ratio >= OverFull (more sliding speed -> faster limit cycle); stays in 300-600 Hz
    public const float ToneQLo = 2.5f, ToneQHi = 9f;          // resonator Q at build start / OverFull (noisy -> tonal)
    public const float JitterLo = 0.35f, JitterHi = 0.06f;    // period jitter (fraction) at build start / OverFull
    public const float TireSpreadCents = 35f;  // per-tire detune so the 4 tires don't phase-lock into one beep
    // --- speed / load gates ---
    public const float SpeedFullMs = 8f;       // the voice fades in from 0.5 m/s to this speed (sqrt law)
    public const float LoadRefN = 2500f;       // level ~ sqrt(Fz/LoadRef), clamped 0..1.4
    // --- engine ceiling ---
    public const float CeilingDb = -4f;        // tire RMS stays at least 4 dB under the engine RMS
    public const float CeilingAttack = 0.010f, CeilingRelease = 0.150f;   // limiter time constants (s)
    public const float ParamTau = 0.03f;       // smoothing of the control targets (s)
    public static readonly float[] Pan = { -0.45f, 0.45f, -0.7f, 0.7f };   // FL FR RL RR
    // --- control laws (pure functions; unit tested) ---
    public static float Smooth(float e0, float e1, float x) { float t = Clamp01((x - e0) / (e1 - e0)); return t * t * (3f - 2f * t); }
    public static float Clamp01(float x) => x < 0f ? 0f : x > 1f ? 1f : x;
    public static float Lerp(float a, float b, float t) => a + (b - a) * t;
    public static float Build(float r) => Smooth(BuildStart, BuildEnd, r);
    public static float Over(float r) => Smooth(BuildEnd, OverFull, r);
    public static float Tonal(float r) => Clamp01(0.85f * Build(r) + 0.15f * Over(r));   // 0 = pure rub, 1 = pure tone
    public static float LevelDb(float r) => r <= BuildEnd ? Lerp(LevelRubDb, LevelPeakDb, Smooth(0f, BuildEnd, r) * 0.25f + 0.75f * Build(r)) : Lerp(LevelPeakDb, LevelOverDb, Over(r));
    public static float ToneHz(float r) => r <= BuildEnd ? Lerp(ToneHzAtBuild, ToneHzAtPeak, Build(r)) : Lerp(ToneHzAtPeak, ToneHzOver, Over(r));
    public static float ToneQ(float r) => Lerp(ToneQLo, ToneQHi, 0.6f * Build(r) + 0.4f * Over(r));
    public static float Jitter(float r) => Lerp(JitterLo, JitterHi, 0.6f * Build(r) + 0.4f * Over(r));
    public static float RubHz(float r) => Lerp(RubLpLoHz, RubLpHiHz, Smooth(0f, BuildEnd, r));
    public static float SpeedGate(float v) => (float)System.Math.Sqrt(Clamp01((v - 0.5f) / (SpeedFullMs - 0.5f)));
    public static float LoadGate(float fz) { float x = fz / LoadRefN; return x <= 0f ? 0f : System.Math.Min(1.4f, (float)System.Math.Sqrt(x)); }
    public static float Db(float db) => (float)System.Math.Pow(10.0, db / 20.0);
    /// gain (linear) the ceiling allows: min(1, engineRms*Db(CeilingDb)/tireRms)
    public static float CeilingGain(float tireRms, float engineRms) { float lim = engineRms * Db(CeilingDb); return tireRms <= lim || tireRms <= 1e-9f ? 1f : lim / tireRms; }
}
public class TireSoundDsp
{
    public readonly float fs; readonly System.Random rng;
    // per-tire state
    readonly float[] cR = new float[4], cV = new float[4], cFz = new float[4];   // current targets (copied from main thread)
    readonly float[] sR = new float[4], sG = new float[4];                       // smoothed ratio, smoothed gain
    readonly double[] ph = new double[4]; readonly float[] per = new float[4];
    readonly float[] lp1 = new float[4], lp2 = new float[4], r1 = new float[4], r2 = new float[4];
    readonly object gate = new object(); readonly float[] tR = new float[4], tV = new float[4], tFz = new float[4];
    public static volatile float EngineRms;   // written by EngineSynth (post engine gain), read here
    public float ceilGain = 1f, lastTireRms, lastPreCeilRms;
    public bool ceilingOn = true;
    public TireSoundDsp(float sampleRate, int seed = 1984) { fs = sampleRate; rng = new System.Random(seed); for (int i = 0; i < 4; i++) { per[i] = 1f; ph[i] = rng.NextDouble(); } }
    public void SetTargets(float[] ratio, float speed, float[] fz) { lock (gate) { for (int i = 0; i < 4; i++) { tR[i] = ratio[i]; tV[i] = speed; tFz[i] = fz[i]; } } }
    float Noise() => (float)rng.NextDouble() * 2f - 1f;
    static float K(float fs, float tau) => 1f - (float)System.Math.Exp(-1.0 / (fs * tau));
    /// renders n frames, interleaved ch channels (ch==1 -> mono), OVERWRITES data. slider = Tires slider gain, engineRms = engine RMS for the ceiling (<0 -> use EngineRms)
    public void Render(float[] data, int n, int ch, float slider, float engineRms = -1f)
    {
        lock (gate) { System.Array.Copy(tR, cR, 4); System.Array.Copy(tV, cV, 4); System.Array.Copy(tFz, cFz, 4); }
        float eRms = engineRms >= 0f ? engineRms : EngineRms;
        float kP = K(fs, TireSoundTuning.ParamTau);
        double acc = 0;
        for (int s = 0; s < n; s++)
        {
            float l = 0f, r = 0f;
            for (int i = 0; i < 4; i++)
            {
                sR[i] += kP * (cR[i] - sR[i]); float ra = sR[i];
                float gT = TireSoundTuning.Db(TireSoundTuning.LevelDb(ra)) * TireSoundTuning.SpeedGate(cV[i]) * TireSoundTuning.LoadGate(cFz[i]);
                sG[i] += kP * (gT - sG[i]);
                if (sG[i] < 1e-6f) continue;
                // rub: white noise -> 2x one-pole lowpass
                float a = 1f - (float)System.Math.Exp(-2.0 * System.Math.PI * TireSoundTuning.RubHz(ra) / fs);
                lp1[i] += a * (Noise() - lp1[i]); lp2[i] += a * (lp1[i] - lp2[i]);
                float rub = lp2[i] / (0.30f * (float)System.Math.Sqrt(a));   // unit RMS: white(-1..1) through 2 one-poles has rms ~0.30*sqrt(a)
                // tone: stick-slip impulse train (period jittered per cycle) -> 2-pole resonator at f0
                float f0 = TireSoundTuning.ToneHz(ra) * (float)System.Math.Pow(2.0, (i - 1.5) * TireSoundTuning.TireSpreadCents / 1200.0);
                float imp = 0f; ph[i] += f0 / fs * per[i];
                if (ph[i] >= 1.0) { ph[i] -= 1.0; imp = 1f; per[i] = 1f + TireSoundTuning.Jitter(ra) * Noise(); }
                float q = TireSoundTuning.ToneQ(ra), w = (float)(2.0 * System.Math.PI * f0 / fs), rr = (float)System.Math.Exp(-w / (2f * q));
                float b1 = 2f * rr * (float)System.Math.Cos(w), b2 = -rr * rr;
                float y = (imp + 0.15f * Noise()) * (1f - rr) * 2.6f + b1 * r1[i] + b2 * r2[i]; r2[i] = r1[i]; r1[i] = y;
                float tone = y * (float)System.Math.Sqrt(fs / f0) * TireSoundTuning.ToneNorm;   // ~unit RMS across f0/Q
                float tn = TireSoundTuning.Tonal(ra), m = sG[i] * ((1f - tn) * rub + tn * tone);
                float x = (TireSoundTuning.Pan[i] + 1f) * 0.5f;   // equal-power pan
                l += m * (float)System.Math.Cos(x * 0.5 * System.Math.PI); r += m * (float)System.Math.Sin(x * 0.5 * System.Math.PI);
            }
            l *= slider; r *= slider; float mono = 0.5f * (l + r);
            acc += mono * mono;
            if (ch == 1) data[s] = mono; else { data[s * ch] = l; data[s * ch + 1] = r; for (int c = 2; c < ch; c++) data[s * ch + c] = 0f; }
        }
        // ceiling: block RMS vs engine RMS; smoothed gain (attack fast, release slow), then applied with a per-sample ramp
        float rms = (float)System.Math.Sqrt(acc / System.Math.Max(1, n)); lastPreCeilRms = rms;
        float target = ceilingOn ? TireSoundTuning.CeilingGain(rms, eRms) : 1f;
        float blockT = n / fs, tau = target < ceilGain ? TireSoundTuning.CeilingAttack : TireSoundTuning.CeilingRelease;
        float g0 = ceilGain, g1 = target < ceilGain ? target : ceilGain + (target - ceilGain) * (1f - (float)System.Math.Exp(-blockT / tau));
        // attack is applied fully within the block so the block never exceeds the ceiling
        double acc2 = 0;
        for (int s = 0; s < n; s++)
        {
            float g = target < g0 ? g1 : g0 + (g1 - g0) * (s + 1) / n;
            if (ch == 1) { data[s] *= g; acc2 += data[s] * data[s]; }
            else { for (int c = 0; c < ch; c++) data[s * ch + c] *= g; float mm = 0.5f * (data[s * ch] + data[s * ch + 1]); acc2 += mm * mm; }
        }
        ceilGain = g1; lastTireRms = (float)System.Math.Sqrt(acc2 / System.Math.Max(1, n));
    }
}
#if UNITY_5_3_OR_NEWER
/// scrubRatio and Pacejka peak finding (kept from v1; these are physics, not audio constants)
public static class TireScrubLaw
{
    /// alpha in rad, kappa slip ratio, ap/kp = peak slip angle (rad) / peak slip ratio of OUR Pacejka at the wheel's current load
    public static float ScrubRatio(float alphaRad, float kappa, float apRad, float kp) { float ap = UnityEngine.Mathf.Max(0.05f, apRad), k = UnityEngine.Mathf.Max(0.04f, kp); float a = UnityEngine.Mathf.Abs(alphaRad) / ap, b = UnityEngine.Mathf.Abs(kappa) / k; return UnityEngine.Mathf.Sqrt(a * a + b * b); }
    public static float PeakAlpha(CarParams p, float fz) => Peak(x => UnityEngine.Mathf.Abs(PacejkaTire.PureLat(p, x, UnityEngine.Mathf.Max(fz, 1f), 0f)), 0.002f, 0.6f);
    public static float PeakKappa(CarParams p, float fz) => Peak(x => UnityEngine.Mathf.Abs(PacejkaTire.PureLong(p, x, UnityEngine.Mathf.Max(fz, 1f))), 0.002f, 1.0f);
    static float Peak(System.Func<float, float> f, float lo, float hi)
    {
        int n = 60; float best = lo, bv = -1f; for (int i = 0; i <= n; i++) { float x = lo + (hi - lo) * i / n, v = f(x); if (v > bv) { bv = v; best = x; } }
        float a = UnityEngine.Mathf.Max(lo, best - (hi - lo) / n), b = UnityEngine.Mathf.Min(hi, best + (hi - lo) / n); const float g = 0.618034f;
        for (int k = 0; k < 40; k++) { float c = b - g * (b - a), d = a + g * (b - a); if (f(c) > f(d)) b = d; else a = c; }
        return 0.5f * (a + b);
    }
}
#endif
