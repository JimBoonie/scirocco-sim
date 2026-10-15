// Tire sound v3 (TireScrub.cs): control-law shape, spectrum targets, engine ceiling, slider, Pacejka peaks.
using NUnit.Framework; using UnityEngine; using Tuning = TireSoundTuning;
public class TireScrubTests
{
    const float SampleRateHz = 48000f;
    // @description - Verifies the combined scrub ratio sqrt((angle/peakAngle)^2 + (ratio/peakRatio)^2) for a known case (0.6636) and that tiny peak values are floored at 0.05 rad and 0.04, giving 0.2 for the tiny inputs.
    // @return {Void} - nothing
    [Test] public void ScrubRatio_Combined_And_Floors()
    {
        Assert.AreEqual(0.6636158f, TireScrubLaw.ScrubRatio(5f * Mathf.Deg2Rad, 0.05f, 0.2f, 0.1f), 1e-5);
        Assert.AreEqual(0.2f, TireScrubLaw.ScrubRatio(0.01f, 0f, 0.01f, 0.01f), 1e-5, "ap/kp floors 0.05 rad / 0.04");
    }
    // @description - Verifies the tire sound laws from slip onset to 2.5 x peak: level and pitch never decrease with scrub ratio, pitch stays in the low register (at most 3 semitones above the base), the gain is 0 before onset, the pitch span is 2 to 3 semitones, about 2 semitones at the peak, and the level hits its peak and over-peak targets.
    // @return {Void} - nothing
    [Test] public void Laws_Are_Monotone_And_On_Target()
    {
        float previousLevelDb = -999f, previousToneHz = 0f;
        for (float scrubRatio = Tuning.Onset + 0.001f; scrubRatio <= 2.5f; scrubRatio += 0.01f)
        {
            float levelDb = Tuning.LevelDb(scrubRatio), toneHz = Tuning.ToneHz(scrubRatio);
            Assert.GreaterOrEqual(levelDb, previousLevelDb - 1e-4f, $"level monotone at {scrubRatio}"); Assert.GreaterOrEqual(toneHz, previousToneHz - 1e-3f, $"pitch monotone at {scrubRatio}");
            Assert.That(toneHz, Is.InRange(Tuning.BaseHz, Tuning.BaseHz * 1.19f), "low register, <= 3 semitones"); previousLevelDb = levelDb; previousToneHz = toneHz;
        }
        Assert.AreEqual(0f, Tuning.Gain(0.2f), 1e-9, "silent before slip onset");
        float span = 12f * Mathf.Log(Tuning.ToneHz(2.5f) / Tuning.ToneHz(Tuning.Onset), 2f);
        Assert.That(span, Is.InRange(2f, 3f), "pitch span onset -> heavy slide");
        Assert.That(Tuning.Semis(1f), Is.InRange(1.5f, 2.5f), "about one whole step by peak grip");
        Assert.Greater(Tuning.LevelDb(0.5f), Tuning.LevelOnsetDb + 8f, "clearly building below the peak");
        Assert.AreEqual(Tuning.LevelPeakDb, Tuning.LevelDb(1f), 1e-4); Assert.AreEqual(Tuning.LevelOverDb, Tuning.LevelDb(2.5f), 1e-4);
    }
    // @description - Verifies the engine-ceiling gain: 1 when the tires are below the limit, engine level x ceiling / tire level when above, and 0 when the engine is silent.
    // @return {Void} - nothing
    [Test] public void Ceiling_Gain()
    {
        Assert.AreEqual(1f, Tuning.CeilingGain(0.01f, 0.1f), 1e-6);
        Assert.AreEqual(0.1f * Tuning.Db(Tuning.CeilingDb) / 0.2f, Tuning.CeilingGain(0.2f, 0.1f), 1e-6);
        Assert.AreEqual(0f, Tuning.CeilingGain(0.2f, 0f), 1e-6, "engine silent -> tires silent");
    }
    // @description - Renders seconds of tire sound offline at a constant scrub ratio on all four tires (15 m/s, 3000 N per tire), 512 samples at a time.
    // @param {float} scrubRatio - tire scrub ratio (dimensionless, 1 = grip peak)
    // @param {float} slider - tire volume multiplier (0 = mute, 1 = default)
    // @param {float} engineRms - engine RMS level used for the ceiling, in full-scale units
    // @param {float} seconds - duration in seconds
    // @param {TireSoundDsp} dsp - tire sound synthesiser instance
    // @return {float[]} - mono samples at SampleRateHz
    static float[] RenderTireSound(float scrubRatio, float slider, float engineRms, float seconds, out TireSoundDsp dsp)
    {
        dsp = new TireSoundDsp(SampleRateHz); int sampleCount = (int)(seconds * SampleRateHz), blockSize = 512; var rendered = new float[sampleCount]; var block = new float[blockSize];
        for (int start = 0; start + blockSize <= sampleCount; start += blockSize) { dsp.SetTargets(new[] { scrubRatio, scrubRatio, scrubRatio, scrubRatio }, 15f, new[] { 3000f, 3000f, 3000f, 3000f }); dsp.Render(block, blockSize, 1, slider, engineRms); System.Array.Copy(block, 0, rendered, start, blockSize); }
        return rendered;
    }
    // @description - Root-mean-square level of a buffer from a start sample to its end.
    // @param {float[]} samples - mono samples to analyse (full-scale units, +-1)
    // @param {int} firstSample - index of the first sample to include (earlier samples are start-up)
    // @return {float} - RMS level in full-scale units
    static float RootMeanSquare(float[] samples, int firstSample) { double sumOfSquares = 0; for (int i = firstSample; i < samples.Length; i++) sumOfSquares += samples[i] * samples[i]; return (float)System.Math.Sqrt(sumOfSquares / (samples.Length - firstSample)); }
    // @description - Amplitude of one frequency in a buffer by the Goertzel algorithm (a single-bin DFT) from a start sample to the end.
    // @param {float[]} samples - mono samples to analyse (full-scale units, +-1)
    // @param {int} firstSample - index of the first sample to include (earlier samples are start-up)
    // @param {float} frequencyHz - frequency to measure in hertz
    // @return {float} - amplitude of that frequency (sum of the samples' projections, not normalised)
    static float GoertzelAmplitude(float[] samples, int firstSample, float frequencyHz) { double omega = 2 * System.Math.PI * frequencyHz / SampleRateHz, coefficient = 2 * System.Math.Cos(omega), state1 = 0, state2 = 0; for (int i = firstSample; i < samples.Length; i++) { double newState = samples[i] + coefficient * state1 - state2; state2 = state1; state1 = newState; } return (float)System.Math.Sqrt(state1 * state1 + state2 * state2 - coefficient * state1 * state2); }
    // @description - Verifies that at tire volume sliders 1, 2 and 4 the rendered tire RMS stays within 2 % of the engine ceiling and the synthesiser's own last tire level within 0.1 %.
    // @return {Void} - nothing
    [Test] public void Ceiling_Holds_Under_Engine_Any_Slider()
    {
        float engineRms = 0.05f, limit = engineRms * Tuning.Db(Tuning.CeilingDb);
        foreach (float slider in new[] { 1f, 2f, 4f }) { var rendered = RenderTireSound(2f, slider, engineRms, 1.5f, out var dsp); Assert.LessOrEqual(RootMeanSquare(rendered, 24000), limit * 1.02f, $"slider {slider}"); Assert.LessOrEqual(dsp.lastTireRms, limit * 1.001f); }
    }
    // @description - Verifies that halving the slider halves the RMS (within 0.02) and a slider of 0 gives silence.
    // @return {Void} - nothing
    [Test] public void Slider_Scales_And_Zero_Mutes()
    {
        var full = RenderTireSound(1f, 1f, 10f, 1f, out _); var half = RenderTireSound(1f, 0.5f, 10f, 1f, out _); var zero = RenderTireSound(1f, 0f, 10f, 1f, out _);
        Assert.AreEqual(0.5f, RootMeanSquare(half, 12000) / RootMeanSquare(full, 12000), 0.02f); Assert.AreEqual(0f, RootMeanSquare(zero, 0), 1e-9);
    }
    // @description - Finds the frequency of the strongest component between two frequencies in 0.5 Hz steps by Goertzel amplitude.
    // @param {float[]} samples - mono samples to analyse (full-scale units, +-1)
    // @param {int} firstSample - index of the first sample to include (earlier samples are start-up)
    // @param {float} lowHz - lowest frequency of the search range in hertz
    // @param {float} highHz - highest frequency of the search range in hertz
    // @return {float} - frequency of the strongest component in hertz
    static float PeakHz(float[] samples, int firstSample, float lowHz, float highHz) { float bestHz = lowHz, bestPower = -1f; for (float candidateHz = lowHz; candidateHz <= highHz; candidateHz += 0.5f) { float power = GoertzelAmplitude(samples, firstSample, candidateHz); if (power > bestPower) { bestPower = power; bestHz = candidateHz; } } return bestHz; }
    // @description - noise band centre: Goertzel power at 1 Hz steps smoothed over +-12 Hz, then the power-weighted centroid of the region above half max (single bins / argmax of bandpassed noise fluctuate by several Hz; the half-max centroid is stable to ~1 %)
    // @param {float[]} samples - mono samples to analyse (full-scale units, +-1)
    // @param {int} firstSample - index of the first sample to include (earlier samples are start-up)
    // @param {float} lowHz - lowest frequency of the search range in hertz
    // @param {float} highHz - highest frequency of the search range in hertz
    // @return {float} - the band centre hz
    static float BandCentreHz(float[] samples, int firstSample, float lowHz, float highHz) { int binCount = (int)(highHz - lowHz) + 1; var power = new float[binCount]; var smoothed = new float[binCount]; for (int i = 0; i < binCount; i++) power[i] = GoertzelAmplitude(samples, firstSample, lowHz + i) * GoertzelAmplitude(samples, firstSample, lowHz + i);
        float maxSmoothed = 0f; for (int i = 0; i < binCount; i++) { float windowSum = 0f; for (int j = System.Math.Max(0, i - 12); j <= System.Math.Min(binCount - 1, i + 12); j++) windowSum += power[j]; smoothed[i] = windowSum; maxSmoothed = System.Math.Max(maxSmoothed, windowSum); }
        double weightSum = 0, weightedFrequencySum = 0; for (int i = 0; i < binCount; i++) if (smoothed[i] > 0.5f * maxSmoothed) { weightSum += smoothed[i]; weightedFrequencySum += smoothed[i] * (lowHz + i); } return (float)(weightedFrequencySum / weightSum); }
    // @description - Verifies the tire spectrum at scrub ratios 0.35, 0.7 and 2.5: the low tone dominates 1 kHz by 5x and its 1.5th harmonic by 3x (no screech), is audible below the peak (above -45 dBFS), and the band centre rises 1.6 to 3 semitones from ratio 0.35 to 2.5.
    // @return {Void} - nothing
    [Test] public void Spectrum_Low_Tone_Present_Below_Peak_And_Pitch_Span()
    {
        var lowRatio = RenderTireSound(0.35f, 1f, 10f, 2f, out _); var midRatio = RenderTireSound(0.7f, 1f, 10f, 2f, out _); var overRatio = RenderTireSound(2.5f, 1f, 10f, 2f, out _);
        foreach (var (rendered, scrubRatio) in new[] { (lowRatio, 0.35f), (midRatio, 0.7f), (overRatio, 2.5f) })
        {
            float toneHz = Tuning.ToneHz(scrubRatio);
            Assert.Greater(GoertzelAmplitude(rendered, 24000, toneHz), 5f * GoertzelAmplitude(rendered, 24000, 1000f), $"r={scrubRatio}: low tone dominates, no screech");
            Assert.Greater(GoertzelAmplitude(rendered, 24000, toneHz), 3f * GoertzelAmplitude(rendered, 24000, toneHz * 1.5f), $"r={scrubRatio}: tone at f0");
        }
        Assert.Greater(20f * Mathf.Log10(RootMeanSquare(midRatio, 24000)), -45f, "low tone audible below the peak (pre-ceiling)");
        float lowPeakHz = BandCentreHz(lowRatio, 24000, 100f, 260f), overPeakHz = BandCentreHz(overRatio, 24000, 100f, 260f), span = 12f * Mathf.Log(overPeakHz / lowPeakHz, 2f);
        Debug.Log($"TIRESOUND peak {lowPeakHz:0.0} Hz @0.35 -> {overPeakHz:0.0} Hz @2.5 = {span:0.00} semitones; rms {20 * Mathf.Log10(RootMeanSquare(lowRatio, 24000)):0.0}/{20 * Mathf.Log10(RootMeanSquare(midRatio, 24000)):0.0}/{20 * Mathf.Log10(RootMeanSquare(overRatio, 24000)):0.0} dBFS");
        Assert.That(span, Is.InRange(1.6f, 3f));
    }
    // @description - Verifies that the engine sound is identical at throttle 0 and 1 for the same load (to 1e-7), i.e. the throttle area adds no hiss.
    // @return {Void} - nothing
    [Test] public void Engine_Has_No_Throttle_Hiss()
    {
        float[] RenderThrottle(float throttle) { var gameObject = new GameObject("e"); var synth = gameObject.AddComponent<EngineSynth>(); synth.SetOfflineState(SampleRateHz, 4000f, 0.8f, throttle); var buffer = new float[9600]; synth.Render(buffer, buffer.Length); Object.DestroyImmediate(gameObject); return buffer; }
        var zeroThrottle = RenderThrottle(0f); var fullThrottle = RenderThrottle(1f); for (int i = 0; i < zeroThrottle.Length; i++) Assert.AreEqual(zeroThrottle[i], fullThrottle[i], 1e-7, "throttle area must not add noise");
    }
    // @description - Verifies the audio pause gate: pausing fades to exact silence without a step larger than 0.002 per sample, stays silent while paused, and resuming fades back to full level without clicks.
    // @return {Void} - nothing
    [Test] public void Pause_Gate_Fades_Out_Silent_And_Back_No_Click()
    {
        var gate = new AudioPauseGate(); int frameCount = 4800; var buffer = new float[frameCount * 2];
        System.Func<bool, float[]> applyBlock = paused => { for (int i = 0; i < buffer.Length; i++) buffer[i] = 1f; gate.Apply(buffer, frameCount, 2, paused, SampleRateHz); return buffer; };
        var output = applyBlock(true); float previousSample = 1f, maxStep = 0f;
        for (int i = 0; i < frameCount; i++) { maxStep = Mathf.Max(maxStep, Mathf.Abs(output[i * 2] - previousSample)); previousSample = output[i * 2]; }
        Assert.AreEqual(0f, output[(frameCount - 1) * 2], 1e-6, "silent after the fade"); Assert.Less(maxStep, 0.002f, "no click on pause");
        output = applyBlock(true); foreach (var sample in output) Assert.AreEqual(0f, sample, 0f, "fully silent while paused");
        output = applyBlock(false); Assert.Less(output[0], 0.002f, "starts from silence"); Assert.AreEqual(1f, output[(frameCount - 1) * 2], 1e-6, "back to full within the fade");
        for (int i = 1; i < frameCount; i++) Assert.LessOrEqual(output[i * 2] - output[(i - 1) * 2], 0.002f, "no click on resume");
    }
    // @description - Renders one second of engine sound offline at a fixed rpm and load and returns its level in dB relative to full scale (after skipping the first quarter).
    // @param {float} rpm - engine speed in revolutions per minute
    // @param {float} engineLoad - engine load, 0 (closed throttle) to 1 (wide open)
    // @param {int} sampleCount - number of samples to render
    // @return {float} - level in dB relative to full scale
    static float EngineLevelDb(float rpm, float engineLoad, int sampleCount = 48000) { var gameObject = new GameObject("e"); var synth = gameObject.AddComponent<EngineSynth>(); synth.SetOfflineState(SampleRateHz, rpm, engineLoad, engineLoad); var buffer = new float[sampleCount]; synth.Render(buffer, sampleCount); Object.DestroyImmediate(gameObject); return 20f * Mathf.Log10(RootMeanSquare(buffer, sampleCount / 4)); }
    // @description - Verifies that at 2000 to 6000 rpm the closed-throttle overrun sound is 5 to 11 dB below the loaded sound and louder than -30 dBFS.
    // @return {Void} - nothing
    [Test] public void Engine_Overrun_Stays_Audible_6_To_10_dB_Down()
    {
        foreach (var rpm in new[] { 2000f, 3000f, 4500f, 6000f })
        {
            float wideOpenDb = EngineLevelDb(rpm, 1f), overrunDb = EngineLevelDb(rpm, 0f), differenceDb = wideOpenDb - overrunDb;
            Debug.Log($"ENGINE_ONOFF {rpm:0} rpm: on {wideOpenDb:0.0} off {overrunDb:0.0} diff {differenceDb:0.0} dB");
            Assert.That(differenceDb, Is.InRange(5f, 11f), $"overrun at {rpm} rpm should be ~6-10 dB under load");
            Assert.Greater(overrunDb, -30f, "overrun clearly audible");
        }
    }
    // @description - Verifies that with a heavy slide on all tires the tire level stays below the engine level minus 3 dB and above the engine level minus 12 dB, for both loaded and overrun engines.
    // @return {Void} - nothing
    [Test] public void Tire_Ceiling_Tracks_Overrun_Engine()
    {
        foreach (var load in new[] { 0f, 1f })
        {
            var gameObject = new GameObject("e"); var synth = gameObject.AddComponent<EngineSynth>(); synth.SetOfflineState(SampleRateHz, 4000f, load, load); var engineBuffer = new float[512]; var dsp = new TireSoundDsp(SampleRateHz); var tireBuffer = new float[512];
            float engineRms = 0f, tireEnergy = 0f; int blockCount = 0;
            for (int k = 0; k < 400; k++) { synth.Render(engineBuffer, 512); double sumOfSquares = 0; foreach (var sample in engineBuffer) sumOfSquares += sample * sample; engineRms = (float)System.Math.Sqrt(sumOfSquares / 512);
                dsp.SetTargets(new[] { 2.5f, 2.5f, 2.5f, 2.5f }, 20f, new[] { 4000f, 4000f, 4000f, 4000f }); dsp.Render(tireBuffer, 512, 1, 4f, engineRms); if (k > 200) { tireEnergy += RootMeanSquare(tireBuffer, 0) * RootMeanSquare(tireBuffer, 0); blockCount++; } }
            Object.DestroyImmediate(gameObject);
            float tireDb = 10f * Mathf.Log10(tireEnergy / blockCount), engineDb = 20f * Mathf.Log10(engineRms);
            Debug.Log($"CEILING load {load}: tire {tireDb:0.0} engine {engineDb:0.0}");
            Assert.LessOrEqual(tireDb, engineDb - 4f + 1f, "tire stays under engine - 4 dB"); Assert.Greater(tireDb, engineDb - 12f, "tire still audible under overrun engine");
        }
    }
    // @description - Writes the loaded/overrun engine sweep (2 s each at 2000, 3000, 4500, 6000 rpm) as raw float32 to the directory in SCIROCCO_SWEEP_DIR for listening; passes without writing if that variable is unset.
    // @return {Void} - nothing
    [Test] public void Offline_OnOff_Sweep_To_Disk()
    {
        var directory = System.Environment.GetEnvironmentVariable("SCIROCCO_SWEEP_DIR"); if (string.IsNullOrEmpty(directory)) { Assert.Pass("set SCIROCCO_SWEEP_DIR"); return; }
        // at 2000/3000/4500/6000 rpm: 2 s on (load 1) then 2 s off (closed-throttle overrun, load 0)
        var gameObject = new GameObject("e"); var synth = gameObject.AddComponent<EngineSynth>(); var engineBuffer = new float[480];
        using (var writer = new System.IO.BinaryWriter(System.IO.File.Create(directory + "/onoff_engine.f32")))
            foreach (var rpm in new[] { 2000f, 3000f, 4500f, 6000f }) for (int phase = 0; phase < 2; phase++) for (int block = 0; block < 200; block++) { synth.SetOfflineState(SampleRateHz, rpm, phase == 0 ? 1f : 0f, phase == 0 ? 1f : 0f); synth.Render(engineBuffer, 480); foreach (var sample in engineBuffer) writer.Write(sample); }
        Object.DestroyImmediate(gameObject);
    }
    // @description - Verifies that the tire sound is noise rather than a buzz: the autocorrelation at three pitch periods is below 0.35.
    // @return {Void} - nothing
    [Test] public void Tire_Is_Noise_Not_Buzz()
    {
        var dsp = new TireSoundDsp(SampleRateHz); dsp.ceilingOn = false; var buffer = new float[48000]; dsp.SetTargets(new[] { 2.5f, 2.5f, 2.5f, 2.5f }, 20f, new[] { 3000f, 3000f, 3000f, 3000f }); dsp.Render(buffer, buffer.Length, 1, 1f, 1f);
        int lag = (int)(3f * SampleRateHz / TireSoundTuning.ToneHz(2.5f)); double zeroLagCorrelation = 0, laggedCorrelation = 0; for (int i = 12000; i < buffer.Length - lag; i++) { zeroLagCorrelation += buffer[i] * buffer[i]; laggedCorrelation += buffer[i] * buffer[i + lag]; }
        Debug.Log($"TIRE autocorr @3 periods {laggedCorrelation / zeroLagCorrelation:0.000}"); Assert.Less(System.Math.Abs(laggedCorrelation / zeroLagCorrelation), 0.35, "no periodic (pitched/buzzy) excitation");
    }
    // @description - Writes a slip sweep (scrub ratio 0 to 2.5 over 20 s) and a throttle sweep (1500 to 6500 rpm) as raw float32 files to SCIROCCO_SWEEP_DIR for listening; passes without writing if that variable is unset.
    // @return {Void} - nothing
    [Test] public void Offline_Sweeps_To_Disk()
    {
        var directory = System.Environment.GetEnvironmentVariable("SCIROCCO_SWEEP_DIR"); if (string.IsNullOrEmpty(directory)) { Assert.Pass("set SCIROCCO_SWEEP_DIR to write sweeps"); return; }
        int blockSize = 512, totalSamples = (int)(20f * SampleRateHz); var tireBuffer = new float[blockSize]; var engineBuffer = new float[blockSize];
        var gameObject = new GameObject("e"); var synth = gameObject.AddComponent<EngineSynth>();
        // slip sweep: ratio 0 -> 2.5 over 20 s at 4000 rpm / load 0.6, 15 m/s, 3000 N; writes tire (post ceiling), engine
        var dsp = new TireSoundDsp(SampleRateHz);
        using (var tireWriter = new System.IO.BinaryWriter(System.IO.File.Create(directory + "/slip_tire.f32"))) using (var engineWriter = new System.IO.BinaryWriter(System.IO.File.Create(directory + "/slip_engine.f32")))
            for (int start = 0; start < totalSamples; start += blockSize) { synth.SetOfflineState(SampleRateHz, 4000f, 0.6f, 0.6f); synth.Render(engineBuffer, blockSize); double sumOfSquares = 0; foreach (var sample in engineBuffer) sumOfSquares += sample * sample;
                float scrubRatio = 2.5f * start / totalSamples; dsp.SetTargets(new[] { scrubRatio, scrubRatio, scrubRatio, scrubRatio }, 15f, new[] { 3000f, 3000f, 3000f, 3000f }); dsp.Render(tireBuffer, blockSize, 1, 1f, (float)System.Math.Sqrt(sumOfSquares / blockSize));
                foreach (var sample in tireBuffer) tireWriter.Write(sample); foreach (var sample in engineBuffer) engineWriter.Write(sample); }
        // throttle sweep: rpm 1500 -> 6500, load/throttle 0 -> 1 over 20 s, engine only (no intake hiss)
        using (var engineWriter = new System.IO.BinaryWriter(System.IO.File.Create(directory + "/throttle_engine.f32")))
            for (int start = 0; start < totalSamples; start += blockSize) { float throttle = (float)start / totalSamples; synth.SetOfflineState(SampleRateHz, 1500f + 5000f * throttle, throttle, throttle); synth.Render(engineBuffer, blockSize); foreach (var sample in engineBuffer) engineWriter.Write(sample); }
        Object.DestroyImmediate(gameObject);
    }
    // @description - Verifies that the tire sound is exactly silent when the car speed is 0.
    // @return {Void} - nothing
    [Test] public void Silent_When_Stopped()
    {
        var dsp = new TireSoundDsp(SampleRateHz); var buffer = new float[4800]; dsp.SetTargets(new[] { 2f, 2f, 2f, 2f }, 0f, new[] { 3000f, 3000f, 3000f, 3000f }); dsp.Render(buffer, buffer.Length, 1, 1f, 1f); Assert.AreEqual(0f, RootMeanSquare(buffer, 0), 1e-9);
    }
    // @description - Verifies that the numerically found peak slip angle and slip ratio at 3000 N are real maxima: lateral force at 0.8 x the peak angle is smaller, at 1.2 x not larger, and longitudinal force at 0.8 x the peak ratio is smaller.
    // @return {Void} - nothing
    [Test] public void Pacejka_Peaks_At_3000N()
    {
        var carParams = ScriptableObject.CreateInstance<CarParams>();
        float peakSlipAngle = TireScrubLaw.PeakAlpha(carParams, 3000f), peakSlipRatio = TireScrubLaw.PeakKappa(carParams, 3000f);
        Assert.Greater(Mathf.Abs(PacejkaTire.PureLat(carParams, peakSlipAngle, 3000f, 0f)), Mathf.Abs(PacejkaTire.PureLat(carParams, peakSlipAngle * 0.8f, 3000f, 0f)));
        Assert.GreaterOrEqual(Mathf.Abs(PacejkaTire.PureLat(carParams, peakSlipAngle, 3000f, 0f)), Mathf.Abs(PacejkaTire.PureLat(carParams, peakSlipAngle * 1.2f, 3000f, 0f)));
        Assert.Greater(Mathf.Abs(PacejkaTire.PureLong(carParams, peakSlipRatio, 3000f)), Mathf.Abs(PacejkaTire.PureLong(carParams, peakSlipRatio * 0.8f, 3000f)));
    }
}
