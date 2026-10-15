// Overrun (engine braking) audio driven by the physical air-path Engine: coastdown from 5000 rpm with closed throttle, in gear.
using NUnit.Framework; using UnityEngine;
public class EngineOverrunAudioTests
{
    const float SampleRateHz = 48000f, dt = 0.0005f;
    // @description - Renders one block of the engine sound offline (no audio device) at a fixed engine state, using a temporary EngineSynth object.
    // @param {float} rpm - engine speed in revolutions per minute
    // @param {float} engineLoad - engine load, 0 (closed throttle) to 1 (wide open)
    // @param {float} manifoldPressureRatio - manifold pressure divided by ambient pressure (dimensionless, 1 = ambient)
    // @param {bool} isCombusting - true when the engine fires, false for fuel cut / motoring
    // @param {int} sampleCount - number of samples to render at SampleRateHz (default 0.5 s)
    // @return {float[]} - mono samples normalised to full scale (+-1)
    static float[] RenderEngineSynth(float rpm, float engineLoad, float manifoldPressureRatio, bool isCombusting, int sampleCount = 24000)
    { var gameObject = new GameObject("e"); var synth = gameObject.AddComponent<EngineSynth>(); synth.SetOfflineState(SampleRateHz, rpm, engineLoad, engineLoad, isCombusting, manifoldPressureRatio); var buffer = new float[sampleCount]; synth.Render(buffer, sampleCount); Object.DestroyImmediate(gameObject); return buffer; }
    // @description - Mean power of the second three quarters of a rendered buffer in decibels relative to full scale (10 log10 of the mean square), skipping the first quarter as start-up.
    // @param {float[]} buffer - mono samples normalised to full scale (+-1)
    // @return {float} - mean power in dB relative to full scale (negative numbers)
    static float MeanPowerDecibels(float[] buffer) { double sumOfSquares = 0; int firstSample = buffer.Length / 4; for (int i = firstSample; i < buffer.Length; i++) sumOfSquares += buffer[i] * buffer[i]; return 10f * Mathf.Log10((float)(sumOfSquares / (buffer.Length - firstSample))); }
    // @description - Measures the pitch of a rendered engine sound as the lag of the autocorrelation peak within -40 % to +40 % of the expected frequency (skipping the first quarter of the buffer as start-up).
    // @param {float[]} buffer - mono samples at SampleRateHz, normalised to full scale (+-1)
    // @param {float} guessHz - expected fundamental in hertz (the search window is 0.6 to 1.4 times this)
    // @return {float} - measured fundamental in hertz
    static float MeasurePitchHz(float[] buffer, float guessHz)   // autocorrelation peak within +-40% of the guess
    {
        int firstSample = buffer.Length / 4, lowestLag = (int)(SampleRateHz / (guessHz * 1.4f)), highestLag = (int)(SampleRateHz / (guessHz * 0.6f)), bestLag = lowestLag; double bestCorrelation = double.MinValue;
        for (int lagSamples = lowestLag; lagSamples <= highestLag; lagSamples++) { double correlation = 0; for (int i = firstSample; i + lagSamples < buffer.Length; i++) correlation += buffer[i] * buffer[i + lagSamples]; if (correlation > bestCorrelation) { bestCorrelation = correlation; bestLag = lagSamples; } }
        return SampleRateHz / bestLag;
    }
    // @description - Simulates a coastdown from 5000 rpm at 700 rpm/s (3rd/4th-gear drag) with the pedal released: wheel torque drags the engine along the ramp, and the state is yielded every 0.5 s of simulated time.
    // @return {IEnumerable<(Engine engine, CarParams carParams)>} - the engine and its parameters at each 0.5 s sample (the same engine object, mutated between samples)
    static System.Collections.Generic.IEnumerable<(Engine engine, CarParams carParams)> Coast()
    {
        var carParams = ScriptableObject.CreateInstance<CarParams>(); var engine = new Engine(carParams);
        typeof(Engine).GetField("angularVelocity", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).SetValue(engine, 5000f * Mathf.PI / 30f); engine.rpm = 5000f;
        for (int i = 1; i <= 9000; i++) { float target = 5000f - i * dt * 700f; engine.Step(0f, -((target - engine.rpm) * 2f + engine.FrictionTorque(engine.rpm)), dt); if (i % 1000 == 0) yield return (engine, carParams); }
    }
    // @description - Verifies that during a closed-throttle coastdown the manifold pressure ratio stays within 0.05..0.35 (overrun leakage plus residual floor), the engine keeps combusting, and the overrun sound is 6 to 10 dB below the wide-open-throttle sound at the same rpm.
    // @return {Void} - nothing
    [Test] public void Overrun_Audible_6_To_10_dB_Under_Wot_With_Real_Engine()
    {
        foreach (var (engine, carParams) in Coast())
        {
            float load = Mathf.Clamp01((engine.manifoldPressureRatio - carParams.mapZeroTorque) / (1f - carParams.mapZeroTorque));
            float wideOpenDb = MeanPowerDecibels(RenderEngineSynth(engine.rpm, 1f, 1f, true)), overrunDb = MeanPowerDecibels(RenderEngineSynth(engine.rpm, load, engine.manifoldPressureRatio, engine.combusting)), differenceDb = wideOpenDb - overrunDb;
            Debug.Log($"OVERRUN rpm {engine.rpm:0} map {engine.manifoldPressureRatio:0.000} load {load:0.00} comb {engine.combusting} WOT {wideOpenDb:0.0} dBFS overrun {overrunDb:0.0} dBFS diff {differenceDb:0.0} dB");
            Assert.That(engine.manifoldPressureRatio, Is.InRange(0.05f, 0.35f), "closed-throttle overrun MAP (leakage + residual floor; real SI overrun ~0.15-0.35 bar, aa1car MAP article)"); Assert.IsTrue(engine.combusting);
            Assert.That(differenceDb, Is.InRange(6f, 10f), $"overrun at {engine.rpm:0} rpm");
        }
    }
    // @description - Verifies that during the 5000 rpm coastdown the measured pitch (autocorrelation) matches the firing frequency rpm / 30 Hz within 3 % and falls monotonically as rpm falls.
    // @return {Void} - nothing
    [Test] public void Overrun_Pitch_Tracks_Rpm_During_Coastdown()
    {
        float previousHz = float.MaxValue;
        foreach (var (engine, carParams) in Coast())
        {
            float expectedHz = engine.rpm / 30f, measuredHz = MeasurePitchHz(RenderEngineSynth(engine.rpm, 0f, engine.manifoldPressureRatio, engine.combusting), expectedHz);
            Debug.Log($"PITCH rpm {engine.rpm:0} expected {expectedHz:0.0} Hz measured {measuredHz:0.0} Hz");
            Assert.AreEqual(expectedHz, measuredHz, expectedHz * 0.03f, "firing frequency = rpm/60 x 2"); Assert.Less(measuredHz, previousHz, "pitch falls with rpm"); previousHz = measuredHz;
        }
    }
    // @description - Verifies that with the fuel cut (no combustion, closed throttle) at 6800 rpm the motoring sound is less than 12 dB below the wide-open-throttle sound, i.e. it stays audible.
    // @return {Void} - nothing
    [Test] public void Fuel_Cut_Motoring_Still_Audible()
    {
        float wideOpenDb = MeanPowerDecibels(RenderEngineSynth(6800f, 1f, 1f, true)), fuelCutDb = MeanPowerDecibels(RenderEngineSynth(6800f, 0f, 0.02f, false));
        Debug.Log($"FUELCUT WOT {wideOpenDb:0.0} motoring {fuelCutDb:0.0}"); Assert.Less(wideOpenDb - fuelCutDb, 12f);
    }
}
