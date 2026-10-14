// Tire sound v3 (Taylor's spec, iRacing-style). ALL audio-mapping constants live in TireSoundTuning below.
//
// When the tire starts to slip a LOW-frequency, noisy tone (~150 Hz fundamental + a softer 2nd harmonic so it carries on
// small speakers) fades in and builds in volume up to peak grip (scrubRatio ~1). Its pitch rises only ~2 semitones from the
// onset of slip to the peak and reaches its maximum, +2.5 semitones (+15.5 %), at heavy slide (scrubRatio >= OverFull).
// Character goes from band-limited noise (onset) to a more tonal stick-slip buzz (sliding).
// v2 problems fixed here: (a) below scrubRatio 0.5 v2 only played a -50 dB lowpassed rub (~30 dB under the engine, fully
// masked) and the level only built from 0.5, so the low part was never heard; (b) the v2 tone ran 300 -> 380 -> 520 Hz,
// i.e. +9.5 semitones and far above the "low" register.
// Signal per tire: excitation = (1-tonal)*white noise + tonal*jittered impulse train at f0
//                  -> constant-peak-gain bandpass at f0 (+ 0.45 x bandpass at 2 f0), normalised to ~unit RMS
//                  out = gain(ratio, speed, load) * that
// Mix: 4 tires panned + summed, x Tires slider, then the ENGINE CEILING limiter (tire RMS <= engine RMS * CeilingDb).
// No UnityEngine dependency in this part (System.Math only).
public static class TireSoundTuning
{
    // --- ratio breakpoints (scrubRatio = combined slip / peak slip of our Pacejka at the current load) ---
    public const float Onset = 0.25f;          // slip onset: silent below, fades in from here
    public const float BuildEnd = 1.0f;        // peak grip: level fully built
    public const float OverFull = 2.0f;        // heavy slide: pitch at its maximum
    // --- levels (dB rel. full scale of the per-tire voice, before slider/ceiling; 4 tires sum ~ +3 dB) ---
    public const float LevelOnsetDb = -48f;    // just after onset (fade from silence)
    public const float LevelPeakDb = -27f;     // ratio 1
    public const float LevelOverDb = -24f;     // heavy slide
    // --- pitch ---
    public const float BaseHz = 150f;          // fundamental at slip onset
    public const float SemisAtPeak = 2.0f;     // one whole step by peak grip
    public const float SemisMax = 2.5f;        // 1.25 whole steps at heavy slide (spec: 2..3 semitones total)
    public const float Harm2 = 0.45f;          // 2nd harmonic level (makes the low tone audible on small speakers)
    public const float ToneQLo = 3f, ToneQHi = 8f;            // bandpass Q onset / heavy slide (noise -> tone)
    public const float JitterLo = 0.30f, JitterHi = 0.08f;    // impulse period jitter
    public const float TireSpreadCents = 12f;  // small per-tire detune (keeps 4 tires from phase-locking)
    // --- speed / load gates ---
    public const float SpeedFullMs = 8f;
    public const float LoadRefN = 2500f;
    // --- engine ceiling ---
    public const float CeilingDb = -4f;
    public const float CeilingAttack = 0.010f, CeilingRelease = 0.150f;
    public const float ParamTau = 0.03f;
    public static readonly float[] Pan = { -0.45f, 0.45f, -0.7f, 0.7f };   // FL FR RL RR
    // --- control laws (pure functions; unit tested) ---
    public static float Smooth(float e0, float e1, float x) { float t = Clamp01((x - e0) / (e1 - e0)); return t * t * (3f - 2f * t); }
    public static float Clamp01(float x) => x < 0f ? 0f : x > 1f ? 1f : x;
    public static float Lerp(float a, float b, float t) => a + (b - a) * t;
    public static float Build(float r) => Clamp01((r - Onset) / (BuildEnd - Onset));     // 0 at onset .. 1 at peak (linear)
    public static float Over(float r) => Smooth(BuildEnd, OverFull, r);
    /// fade-in from silence right at the onset (0..1 over the first 0.1 of ratio)
    public static float OnsetFade(float r) => Smooth(Onset, Onset + 0.1f, r);
    public static float LevelDb(float r) => r <= BuildEnd ? Lerp(LevelOnsetDb, LevelPeakDb, (float)System.Math.Sqrt(Build(r))) : Lerp(LevelPeakDb, LevelOverDb, Over(r));
    public static float Gain(float r) => Db(LevelDb(r)) * OnsetFade(r);
    public static float Semis(float r) => r <= BuildEnd ? SemisAtPeak * Build(r) : Lerp(SemisAtPeak, SemisMax, Over(r));
    public static float ToneHz(float r) => BaseHz * (float)System.Math.Pow(2.0, Semis(r) / 12.0);
    public static float Tonal(float r) => Clamp01(0.25f + 0.45f * Build(r) + 0.3f * Over(r));
    public static float ToneQ(float r) => Lerp(ToneQLo, ToneQHi, 0.5f * Build(r) + 0.5f * Over(r));
    public static float Jitter(float r) => Lerp(JitterLo, JitterHi, 0.5f * Build(r) + 0.5f * Over(r));
    public static float SpeedGate(float v) => (float)System.Math.Sqrt(Clamp01((v - 0.5f) / (SpeedFullMs - 0.5f)));
    public static float LoadGate(float fz) { float x = fz / LoadRefN; return x <= 0f ? 0f : System.Math.Min(1.4f, (float)System.Math.Sqrt(x)); }
    public static float Db(float db) => (float)System.Math.Pow(10.0, db / 20.0);
    public static float CeilingGain(float tireRms, float engineRms) { float lim = engineRms * Db(CeilingDb); return tireRms <= lim || tireRms <= 1e-9f ? 1f : lim / tireRms; }
}
public class TireSoundDsp
{
    public readonly float fs; readonly System.Random rng;
    readonly float[] cR = new float[4], cV = new float[4], cFz = new float[4];
    readonly float[] sR = new float[4], sG = new float[4];
    readonly double[] ph = new double[4]; readonly float[] per = new float[4];
    readonly float[] x1 = new float[4], x2 = new float[4], y1 = new float[4], y2 = new float[4], z1 = new float[4], z2 = new float[4];
    readonly object gate = new object(); readonly float[] tR = new float[4], tV = new float[4], tFz = new float[4];
    public static volatile float EngineRms;
    public float ceilGain = 1f, lastTireRms, lastPreCeilRms;
    public bool ceilingOn = true;
    public TireSoundDsp(float sampleRate, int seed = 1984) { fs = sampleRate; rng = new System.Random(seed); for (int i = 0; i < 4; i++) { per[i] = 1f; ph[i] = rng.NextDouble(); } }
    public void SetTargets(float[] ratio, float speed, float[] fz) { lock (gate) { for (int i = 0; i < 4; i++) { tR[i] = ratio[i]; tV[i] = speed; tFz[i] = fz[i]; } } }
    float Noise() => (float)rng.NextDouble() * 2f - 1f;
    static float K(float fs, float tau) => 1f - (float)System.Math.Exp(-1.0 / (fs * tau));
    // RBJ constant-0dB-peak bandpass, one sample (direct form I); state in (xa,xb,ya,yb)
    static float Bp(float x, float f, float q, float fs, ref float xa, ref float xb, ref float ya, ref float yb)
    {
        double w = 2.0 * System.Math.PI * f / fs, al = System.Math.Sin(w) / (2.0 * q), a0 = 1.0 + al;
        float y = (float)((al * x - al * xb + 2.0 * System.Math.Cos(w) * ya - (1.0 - al) * yb) / a0);
        xb = xa; xa = x; yb = ya; ya = y; return y;
    }
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
                float gT = TireSoundTuning.Gain(ra) * TireSoundTuning.SpeedGate(cV[i]) * TireSoundTuning.LoadGate(cFz[i]);
                sG[i] += kP * (gT - sG[i]);
                if (sG[i] < 1e-6f) continue;
                float f0 = TireSoundTuning.ToneHz(ra) * (float)System.Math.Pow(2.0, (i - 1.5) * TireSoundTuning.TireSpreadCents / 1200.0);
                float q = TireSoundTuning.ToneQ(ra), tn = TireSoundTuning.Tonal(ra);
                float imp = 0f; ph[i] += f0 / fs * per[i];
                if (ph[i] >= 1.0) { ph[i] -= 1.0; imp = 1f; per[i] = 1f + TireSoundTuning.Jitter(ra) * Noise(); }
                // normalise each excitation so the fundamental band has ~unit RMS: impulse train -> fs/(sqrt2 f0); white(+-1) -> 1/(0.577 sqrt(pi BW/fs))
                float nN = 1f / (0.577f * (float)System.Math.Sqrt(System.Math.PI * f0 / q / fs)), iN = fs / (1.4142f * f0);
                float ex = (1f - tn) * Noise() * nN + tn * imp * iN;
                float b1 = Bp(ex, f0, q, fs, ref x1[i], ref x2[i], ref y1[i], ref y2[i]);
                float b2 = Bp(ex, 2f * f0, q, fs, ref z1[i], ref z2[i], ref dzA[i], ref dzB[i]);
                float m = sG[i] * (b1 + TireSoundTuning.Harm2 * b2) * 0.9f;
                float x = (TireSoundTuning.Pan[i] + 1f) * 0.5f;
                l += m * (float)System.Math.Cos(x * 0.5 * System.Math.PI); r += m * (float)System.Math.Sin(x * 0.5 * System.Math.PI);
            }
            l *= slider; r *= slider; float mono = 0.5f * (l + r);
            acc += mono * mono;
            if (ch == 1) data[s] = mono; else { data[s * ch] = l; data[s * ch + 1] = r; for (int c = 2; c < ch; c++) data[s * ch + c] = 0f; }
        }
        float rms = (float)System.Math.Sqrt(acc / System.Math.Max(1, n)); lastPreCeilRms = rms;
        float target = ceilingOn ? TireSoundTuning.CeilingGain(rms, eRms) : 1f;
        float blockT = n / fs, tau = target < ceilGain ? TireSoundTuning.CeilingAttack : TireSoundTuning.CeilingRelease;
        float g0 = ceilGain, g1 = target < ceilGain ? target : ceilGain + (target - ceilGain) * (1f - (float)System.Math.Exp(-blockT / tau));
        double acc2 = 0;
        for (int s = 0; s < n; s++)
        {
            float g = target < g0 ? g1 : g0 + (g1 - g0) * (s + 1) / n;
            if (ch == 1) { data[s] *= g; acc2 += data[s] * data[s]; }
            else { for (int c = 0; c < ch; c++) data[s * ch + c] *= g; float mm = 0.5f * (data[s * ch] + data[s * ch + 1]); acc2 += mm * mm; }
        }
        ceilGain = g1; lastTireRms = (float)System.Math.Sqrt(acc2 / System.Math.Max(1, n));
    }
    readonly float[] dzA = new float[4], dzB = new float[4];
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
