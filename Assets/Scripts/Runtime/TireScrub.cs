// Tire sound v3 (Taylor's spec, iRacing-style). ALL audio-mapping constants live in TireSoundTuning below.
//
// When the tire starts to slip a LOW-frequency, noisy tone (~150 Hz fundamental + a softer 2nd harmonic so it carries on
// small speakers) fades in and builds in volume up to peak grip (scrubRatio ~1). Its pitch rises only ~2 semitones from the
// onset of slip to the peak and reaches its maximum, +2.5 semitones (+15.5 %), at heavy slide (scrubRatio >= OverFull).
// v4: NOISE ONLY (Taylor: v3's impulse-train tone + 2nd harmonic sounded like "angry bees"). Static/white noise through a
// resonant bandpass (centre = ToneHz, Q 3..4, i.e. a band ~45-50 Hz wide, never a pitched tone) plus a wide low-Q band at
// ~2.2 x centre for gentle broadband body. No impulse train, no harmonics.
// v2 problems fixed here: (a) below scrubRatio 0.5 v2 only played a -50 dB lowpassed rub (~30 dB under the engine, fully
// masked) and the level only built from 0.5, so the low part was never heard; (b) the v2 tone ran 300 -> 380 -> 520 Hz,
// i.e. +9.5 semitones and far above the "low" register.
// Signal per tire: white noise -> constant-peak-gain bandpass at f0 (Q ToneQ) + BroadLevel x bandpass at BroadMul*f0 (Q BroadQ),
//                  normalised to ~unit RMS
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
    public const float BroadLevel = 0.35f, BroadMul = 2.2f, BroadQ = 0.7f;   // gentle broadband body above the main band (carries on small speakers, not hissy)
    public const float ToneQLo = 3f, ToneQHi = 4f;            // resonant-noise bandpass Q onset / heavy slide (stays noise: Q <= 4)
    public const float TireSpreadCents = 12f;  // small per-tire detune (keeps 4 tires from phase-locking)
    // --- speed / load gates ---
    public const float SpeedFullMs = 8f;
    public const float LoadRefN = 2500f;
    // --- engine ceiling ---
    public const float CeilingDb = -4f;
    public const float CeilingAttack = 0.010f, CeilingRelease = 0.150f;
    public const float ParamTau = 0.03f;
    public static readonly float[] Pan = { -0.45f, 0.45f, -0.7f, 0.7f };   // FL FR RL RR
    // @description - Smoothstep easing: 0 at or below edgeLow, 1 at or above edgeHigh, with zero slope at both ends (3t^2 - 2t^3). Used so the tire sound fades in and out without clicks.
    // @param {float} edgeLow - input value where the output leaves 0 (same unit as value)
    // @param {float} edgeHigh - input value where the output reaches 1 (same unit as value)
    // @param {float} value - input value to ease (same unit as the edges)
    // @return {float} - eased value between 0 and 1 (dimensionless)
    public static float Smooth(float edgeLow, float edgeHigh, float value) { float fraction = Clamp01((value - edgeLow) / (edgeHigh - edgeLow)); return fraction * fraction * (3f - 2f * fraction); }
    // @description - Limits a value to the range 0..1. Local copy so this class has no UnityEngine dependency and can be unit tested.
    // @param {float} value - value to limit (dimensionless)
    // @return {float} - value limited to 0..1 (dimensionless)
    public static float Clamp01(float value) => value < 0f ? 0f : value > 1f ? 1f : value;
    // @description - Linear interpolation from start to end. Local copy so this class has no UnityEngine dependency.
    // @param {float} start - result when fraction is 0
    // @param {float} end - result when fraction is 1
    // @param {float} fraction - blend fraction (dimensionless, not clamped)
    // @return {float} - start + (end - start) * fraction (same unit as start)
    public static float Lerp(float start, float end, float fraction) => start + (end - start) * fraction;
    // @description - Linear build-up of the tire sound from slip onset (0) to peak grip (1).
    // @param {float} scrubRatio - scrub ratio (dimensionless, combined slip divided by peak-grip slip)
    // @return {float} - build-up between 0 and 1 (dimensionless)
    public static float Build(float scrubRatio) => Clamp01((scrubRatio - Onset) / (BuildEnd - Onset));     // 0 at onset .. 1 at peak (linear)
    // @description - How far into a heavy slide the tire is: 0 at peak grip, 1 at OverFull, smoothly eased.
    // @param {float} scrubRatio - scrub ratio (dimensionless)
    // @return {float} - over-peak amount between 0 and 1 (dimensionless)
    public static float Over(float scrubRatio) => Smooth(BuildEnd, OverFull, scrubRatio);
    // @description - Short fade-in from silence right at slip onset, so the sound does not pop on (0..1 over the first 0.1 of scrub ratio).
    // @param {float} scrubRatio - scrub ratio (dimensionless)
    // @return {float} - fade factor between 0 and 1 (dimensionless)
    public static float OnsetFade(float scrubRatio) => Smooth(Onset, Onset + 0.1f, scrubRatio);
    // @description - Loudness of one tire voice as a function of slip: rises from the onset level to the peak level up to peak grip (square-root curve), then creeps up to the heavy-slide level.
    // @param {float} scrubRatio - scrub ratio (dimensionless)
    // @return {float} - level in decibels relative to full scale of one tire voice
    public static float LevelDb(float scrubRatio) => scrubRatio <= BuildEnd ? Lerp(LevelOnsetDb, LevelPeakDb, (float)System.Math.Sqrt(Build(scrubRatio))) : Lerp(LevelPeakDb, LevelOverDb, Over(scrubRatio));
    // @description - Linear amplitude gain of one tire voice for a given slip, including the onset fade.
    // @param {float} scrubRatio - scrub ratio (dimensionless)
    // @return {float} - linear gain (dimensionless, 1 = full scale)
    public static float Gain(float scrubRatio) => Db(LevelDb(scrubRatio)) * OnsetFade(scrubRatio);
    // @description - Pitch shift of the tire tone above the base frequency: rises to SemisAtPeak by peak grip, then to SemisMax in a heavy slide.
    // @param {float} scrubRatio - scrub ratio (dimensionless)
    // @return {float} - pitch offset in semitones
    public static float Semis(float scrubRatio) => scrubRatio <= BuildEnd ? SemisAtPeak * Build(scrubRatio) : Lerp(SemisAtPeak, SemisMax, Over(scrubRatio));
    // @description - Centre frequency of the tire noise band: BaseHz raised by the semitone offset (2^(semitones/12)).
    // @param {float} scrubRatio - scrub ratio (dimensionless)
    // @return {float} - centre frequency in hertz
    public static float ToneHz(float scrubRatio) => BaseHz * (float)System.Math.Pow(2.0, Semis(scrubRatio) / 12.0);
    // @description - Quality factor of the resonant noise bandpass; it narrows slightly as the slip grows but stays at or below 4 so the sound remains noise, not a pitched tone.
    // @param {float} scrubRatio - scrub ratio (dimensionless)
    // @return {float} - filter Q (dimensionless)
    public static float ToneQ(float scrubRatio) => Lerp(ToneQLo, ToneQHi, 0.5f * Build(scrubRatio) + 0.5f * Over(scrubRatio));
    // @description - Silences tire noise at standstill: 0 below 0.5 m/s, rising with the square root to 1 at SpeedFullMs.
    // @param {float} speed - car speed in metres per second
    // @return {float} - speed gate between 0 and 1 (dimensionless)
    public static float SpeedGate(float speed) => (float)System.Math.Sqrt(Clamp01((speed - 0.5f) / (SpeedFullMs - 0.5f)));
    // @description - Scales tire noise with how hard the tire is pressed to the road: square root of load over LoadRefN, capped at 1.4, and 0 for an unloaded tire.
    // @param {float} verticalLoad - vertical tire load in newtons
    // @return {float} - load gate between 0 and 1.4 (dimensionless)
    public static float LoadGate(float verticalLoad) { float loadRatio = verticalLoad / LoadRefN; return loadRatio <= 0f ? 0f : System.Math.Min(1.4f, (float)System.Math.Sqrt(loadRatio)); }
    // @description - Converts decibels to a linear amplitude ratio: 10^(dB/20).
    // @param {float} db - level in decibels
    // @return {float} - linear amplitude ratio (dimensionless)
    public static float Db(float db) => (float)System.Math.Pow(10.0, db / 20.0);
    // @description - Gain that keeps the summed tire sound at or below the engine loudness plus CeilingDb, so tires never drown out the engine.
    // @param {float} tireRms - RMS level of the mixed tire sound (full-scale units)
    // @param {float} engineRms - RMS level of the engine sound (full-scale units)
    // @return {float} - gain between 0 and 1 to apply to the tire mix (dimensionless; 1 = no limiting)
    public static float CeilingGain(float tireRms, float engineRms) { float limit = engineRms * Db(CeilingDb); return tireRms <= limit || tireRms <= 1e-9f ? 1f : limit / tireRms; }
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
    // @description - Creates the tire sound synthesiser: one noise voice per tire with its own random phase.
    // @param {float} sampleRate - audio sample rate in hertz
    // @param {int} seed - seed of the noise generator (fixed so the sound is repeatable)
    // @return {Void} - nothing (constructor)
    public TireSoundDsp(float sampleRate, int seed = 1984) { fs = sampleRate; rng = new System.Random(seed); for (int tireIndex = 0; tireIndex < 4; tireIndex++) { per[tireIndex] = 1f; ph[tireIndex] = rng.NextDouble(); } }
    // @description - Hands the latest physics values to the audio thread (thread-safe); the audio thread smooths towards them.
    // @param {float[]} ratio - scrub ratio per tire (dimensionless), 4 entries in order front-left, front-right, rear-left, rear-right
    // @param {float} speed - car speed in metres per second, applied to all tires
    // @param {float[]} fz - vertical load per tire in newtons, 4 entries in the same order
    // @return {Void} - nothing
    public void SetTargets(float[] ratio, float speed, float[] fz) { lock (gate) { for (int tireIndex = 0; tireIndex < 4; tireIndex++) { tR[tireIndex] = ratio[tireIndex]; tV[tireIndex] = speed; tFz[tireIndex] = fz[tireIndex]; } } }
    // @description - One sample of white noise, uniform between -1 and 1.
    // @return {float} - noise sample (full-scale units)
    float Noise() => (float)rng.NextDouble() * 2f - 1f;
    // @description - One-pole smoothing coefficient: the fraction of the remaining distance covered per sample, 1 - exp(-1/(sampleRate * timeConstant)).
    // @param {float} sampleRate - audio sample rate in hertz
    // @param {float} timeConstant - smoothing time constant in seconds
    // @return {float} - per-sample smoothing coefficient between 0 and 1 (dimensionless)
    static float K(float sampleRate, float timeConstant) => 1f - (float)System.Math.Exp(-1.0 / (sampleRate * timeConstant));
    // @description - Filters one sample through an RBJ biquad bandpass with constant 0 dB peak gain (direct form I). The caller keeps the previous inputs and outputs so each voice has its own filter state.
    // @param {float} input - input sample (full-scale units)
    // @param {float} centerHz - centre frequency in hertz
    // @param {float} qualityFactor - quality factor (dimensionless)
    // @param {float} sampleRate - audio sample rate in hertz
    // @param {float} previousInput - previous input sample, updated by this call
    // @param {float} inputTwoBack - input sample before the previous one, updated by this call
    // @param {float} previousOutput - previous output sample, updated by this call
    // @param {float} outputTwoBack - output sample before the previous one, updated by this call
    // @return {float} - filtered output sample (full-scale units)
    static float Bp(float input, float centerHz, float qualityFactor, float sampleRate, ref float previousInput, ref float inputTwoBack, ref float previousOutput, ref float outputTwoBack)
    {
        double angularFrequency = 2.0 * System.Math.PI * centerHz / sampleRate, alpha = System.Math.Sin(angularFrequency) / (2.0 * qualityFactor), normalization = 1.0 + alpha;
        float output = (float)((alpha * input - alpha * inputTwoBack + 2.0 * System.Math.Cos(angularFrequency) * previousOutput - (1.0 - alpha) * outputTwoBack) / normalization);
        inputTwoBack = previousInput; previousInput = input; outputTwoBack = previousOutput; previousOutput = output; return output;
    }
    // @description - Fills one audio block with the mixed tire sound. Per tire: smooth the slip and load targets, generate noise, filter it through a main bandpass plus a wide body band, pan it, and sum. Then apply the engine ceiling limiter across the block. Runs on the audio thread.
    // @param {float[]} data - interleaved output buffer to fill (full-scale units)
    // @param {int} frameCount - number of sample frames in the block
    // @param {int} channelCount - channels per frame (1 = mono, 2 or more = stereo plus silence)
    // @param {float} slider - user tire volume slider (dimensionless, 0..1)
    // @param {float} engineRms - engine RMS for the ceiling, or a negative number to use the shared EngineRms value
    // @return {Void} - nothing
    public void Render(float[] data, int frameCount, int channelCount, float slider, float engineRms = -1f)
    {
        lock (gate) { System.Array.Copy(tR, cR, 4); System.Array.Copy(tV, cV, 4); System.Array.Copy(tFz, cFz, 4); }
        float engineLevel = engineRms >= 0f ? engineRms : EngineRms;
        float parameterSmoothing = K(fs, TireSoundTuning.ParamTau);
        double sumOfSquares = 0;
        for (int frameIndex = 0; frameIndex < frameCount; frameIndex++)
        {
            float leftSample = 0f, rightSample = 0f;
            for (int tireIndex = 0; tireIndex < 4; tireIndex++)
            {
                sR[tireIndex] += parameterSmoothing * (cR[tireIndex] - sR[tireIndex]); float scrubRatio = sR[tireIndex];
                float targetGain = TireSoundTuning.Gain(scrubRatio) * TireSoundTuning.SpeedGate(cV[tireIndex]) * TireSoundTuning.LoadGate(cFz[tireIndex]);
                sG[tireIndex] += parameterSmoothing * (targetGain - sG[tireIndex]);
                if (sG[tireIndex] < 1e-6f) continue;
                float centerHz = TireSoundTuning.ToneHz(scrubRatio) * (float)System.Math.Pow(2.0, (tireIndex - 1.5) * TireSoundTuning.TireSpreadCents / 1200.0);
                float qualityFactor = TireSoundTuning.ToneQ(scrubRatio);
                // white(+-1) through a peak-gain bandpass of bandwidth f0/q has RMS 0.577 sqrt(pi BW/fs): normalise the main band to ~unit RMS
                float noiseNormalization = 1f / (0.577f * (float)System.Math.Sqrt(System.Math.PI * centerHz / qualityFactor / fs));
                float excitation = Noise() * noiseNormalization;
                float mainBand = Bp(excitation, centerHz, qualityFactor, fs, ref x1[tireIndex], ref x2[tireIndex], ref y1[tireIndex], ref y2[tireIndex]);
                float broadBand = Bp(excitation, TireSoundTuning.BroadMul * centerHz, TireSoundTuning.BroadQ, fs, ref z1[tireIndex], ref z2[tireIndex], ref dzA[tireIndex], ref dzB[tireIndex]) * (float)System.Math.Sqrt(TireSoundTuning.BroadQ / (qualityFactor * TireSoundTuning.BroadMul));
                float tireSample = sG[tireIndex] * (mainBand + TireSoundTuning.BroadLevel * broadBand) * 0.9f;
                float panPosition = (TireSoundTuning.Pan[tireIndex] + 1f) * 0.5f;
                leftSample += tireSample * (float)System.Math.Cos(panPosition * 0.5 * System.Math.PI); rightSample += tireSample * (float)System.Math.Sin(panPosition * 0.5 * System.Math.PI);
            }
            leftSample *= slider; rightSample *= slider; float mono = 0.5f * (leftSample + rightSample);
            sumOfSquares += mono * mono;
            if (channelCount == 1) data[frameIndex] = mono; else { data[frameIndex * channelCount] = leftSample; data[frameIndex * channelCount + 1] = rightSample; for (int channelIndex = 2; channelIndex < channelCount; channelIndex++) data[frameIndex * channelCount + channelIndex] = 0f; }
        }
        float blockRms = (float)System.Math.Sqrt(sumOfSquares / System.Math.Max(1, frameCount)); lastPreCeilRms = blockRms;
        float target = ceilingOn ? TireSoundTuning.CeilingGain(blockRms, engineLevel) : 1f;
        float blockDuration = frameCount / fs, limiterTimeConstant = target < ceilGain ? TireSoundTuning.CeilingAttack : TireSoundTuning.CeilingRelease;
        float startGain = ceilGain, endGain = target < ceilGain ? target : ceilGain + (target - ceilGain) * (1f - (float)System.Math.Exp(-blockDuration / limiterTimeConstant));
        double sumOfSquaresAfterLimit = 0;
        for (int frameIndex = 0; frameIndex < frameCount; frameIndex++)
        {
            float blockGain = target < startGain ? endGain : startGain + (endGain - startGain) * (frameIndex + 1) / frameCount;
            if (channelCount == 1) { data[frameIndex] *= blockGain; sumOfSquaresAfterLimit += data[frameIndex] * data[frameIndex]; }
            else { for (int channelIndex = 0; channelIndex < channelCount; channelIndex++) data[frameIndex * channelCount + channelIndex] *= blockGain; float monoSample = 0.5f * (data[frameIndex * channelCount] + data[frameIndex * channelCount + 1]); sumOfSquaresAfterLimit += monoSample * monoSample; }
        }
        ceilGain = endGain; lastTireRms = (float)System.Math.Sqrt(sumOfSquaresAfterLimit / System.Math.Max(1, frameCount));
    }
    readonly float[] dzA = new float[4], dzB = new float[4];
}
#if UNITY_5_3_OR_NEWER
/// scrubRatio and Pacejka peak finding (kept from v1; these are physics, not audio constants)
public static class TireScrubLaw
{
    // @description - Combined slip measure for the tire sound: sqrt((slipAngle/peakSlipAngle)^2 + (slipRatio/peakSlipRatio)^2). 1 means the tire is at its grip peak. Peaks are floored (0.05 rad, 0.04) to avoid dividing by near zero.
    // @param {float} alphaRad - slip angle in radians
    // @param {float} kappa - longitudinal slip ratio (dimensionless)
    // @param {float} apRad - slip angle at peak lateral force in radians
    // @param {float} kp - slip ratio at peak longitudinal force (dimensionless)
    // @return {float} - combined scrub ratio (dimensionless)
    public static float ScrubRatio(float alphaRad, float kappa, float apRad, float kp) { float peakAngle = UnityEngine.Mathf.Max(0.05f, apRad), peakRatio = UnityEngine.Mathf.Max(0.04f, kp); float angleFraction = UnityEngine.Mathf.Abs(alphaRad) / peakAngle, ratioFraction = UnityEngine.Mathf.Abs(kappa) / peakRatio; return UnityEngine.Mathf.Sqrt(angleFraction * angleFraction + ratioFraction * ratioFraction); }
    // @description - Finds the slip angle at which the tire model gives its maximum lateral force at the given load (numeric search).
    // @param {CarParams} carParams - car parameter set holding the tire coefficients
    // @param {float} fz - vertical tire load in newtons
    // @return {float} - slip angle of peak lateral force in radians
    public static float PeakAlpha(CarParams carParams, float fz) => Peak(slipAngle => UnityEngine.Mathf.Abs(PacejkaTire.PureLat(carParams, slipAngle, UnityEngine.Mathf.Max(fz, 1f), 0f)), 0.002f, 0.6f);
    // @description - Finds the slip ratio at which the tire model gives its maximum longitudinal force at the given load (numeric search).
    // @param {CarParams} carParams - car parameter set holding the tire coefficients
    // @param {float} fz - vertical tire load in newtons
    // @return {float} - slip ratio of peak longitudinal force (dimensionless)
    public static float PeakKappa(CarParams carParams, float fz) => Peak(slipRatio => UnityEngine.Mathf.Abs(PacejkaTire.PureLong(carParams, slipRatio, UnityEngine.Mathf.Max(fz, 1f))), 0.002f, 1.0f);
    // @description - Finds the argument of the maximum of a single-peak function: a coarse 60-step scan, then a golden-section refinement (40 iterations) around the best step.
    // @param {float} lowerBound - lower search bound
    // @param {float} upperBound - upper search bound
    // @return {float} - argument of the maximum (same unit as the bounds)
    static float Peak(System.Func<float, float> function, float lowerBound, float upperBound)
    {
        int stepCount = 60; float bestArgument = lowerBound, bestValue = -1f; for (int stepIndex = 0; stepIndex <= stepCount; stepIndex++) { float argument = lowerBound + (upperBound - lowerBound) * stepIndex / stepCount, value = function(argument); if (value > bestValue) { bestValue = value; bestArgument = argument; } }
        float bracketLow = UnityEngine.Mathf.Max(lowerBound, bestArgument - (upperBound - lowerBound) / stepCount), bracketHigh = UnityEngine.Mathf.Min(upperBound, bestArgument + (upperBound - lowerBound) / stepCount); const float goldenRatio = 0.618034f;
        for (int iteration = 0; iteration < 40; iteration++) { float innerLow = bracketHigh - goldenRatio * (bracketHigh - bracketLow), innerHigh = bracketLow + goldenRatio * (bracketHigh - bracketLow); if (function(innerLow) > function(innerHigh)) bracketHigh = innerHigh; else bracketLow = innerLow; }
        return 0.5f * (bracketLow + bracketHigh);
    }
}
#endif
