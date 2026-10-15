// Procedural car audio (no samples). Two 3D sources: engine bay (engine, starter, ignition click, stall sputter, limiter cut) and tires (scrub).
// Physics-derived inputs: firing frequency = rpm/60 * 2 (4-cyl 4-stroke), load from manifold pressure, starter gear-mesh frequency from
// pinion teeth x motor speed (ring/pinion ratio), tire scrub from sliding velocity x normal load.
// Mix gains / filter corners below are PERCEPTUAL audio levels (not vehicle physics).
using UnityEngine;
public class CarAudio : MonoBehaviour
{
    public CarController car; public static float Volume = 0.8f, TireVolume = 1f; public static bool Mute;   // Volume = master (engine + tires), TireVolume = extra tire-only gain
    // @description - Creates the two procedural audio sources on the car: the engine synthesiser over the front axle (half a wheelbase plus 0.15 m ahead of the centre) and the tire synthesiser under the car. Does nothing in -smoketest runs.
    // @return {Void} - nothing
    void Start()
    {
        if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-smoketest") >= 0 || !car) { enabled = false; return; }
        var carParams = car.carParams;
        Make<EngineSynth>("EngineAudio", new Vector3(0f, 0.1f, carParams.wheelbase * 0.5f + 0.15f)).Init(car);   // transverse engine over the front axle
        Make<TireSynth>("TireAudio", new Vector3(0f, -0.4f, 0f)).Init(car);
    }
    // @description - Creates a child object with an AudioSource that loops a constant carrier clip (all ones), adds the synthesiser component as an audio filter and starts playing. The synth output multiplies the carrier, so Unity still applies 3D distance attenuation and panning.
    // @param {string} objectName - name of the new game object
    // @param {Vector3} local - position of the object relative to the car, in metres
    // @return {T} - the synthesiser component that was added
    T Make<T>(string objectName, Vector3 local) where T : SynthBase
    {
        var audioObject = new GameObject(objectName); audioObject.transform.SetParent(car.transform, false); audioObject.transform.localPosition = local;
        var audioSource = audioObject.AddComponent<AudioSource>(); var sampleRate = AudioSettings.outputSampleRate;
        var clip = AudioClip.Create(objectName + "_carrier", sampleRate, 1, sampleRate, false); var ones = new float[sampleRate]; for (int i = 0; i < sampleRate; i++) ones[i] = 1f; clip.SetData(ones, 0);   // constant carrier: the filter output keeps 3D attenuation/panning
        audioSource.clip = clip; audioSource.loop = true; audioSource.spatialBlend = 1f; audioSource.dopplerLevel = 0f; audioSource.minDistance = 1.5f; audioSource.maxDistance = 200f; audioSource.rolloffMode = AudioRolloffMode.Logarithmic; audioSource.playOnAwake = false;
        var synth = audioObject.AddComponent<T>(); audioSource.Play(); return synth;
    }
    public static volatile bool Paused;   // menu open / Time.timeScale == 0 -> all car audio fades to silence
    // @description - Applies the master volume and mute setting every frame and updates the Paused flag (menu open or time frozen) that makes the audio fade out.
    // @return {Void} - nothing
    void Update() { AudioListener.volume = Mute ? 0f : Volume; Paused = Time.timeScale <= 0f || (car && car.router && car.router.menuOpen); }
}
/// click-free pause gate: per-sample linear ramp (FadeSec) towards 0 (paused) or 1 (running). Pure; unit tested.
public class AudioPauseGate
{
    public const float FadeSec = 0.03f;
    public float gain = 1f;
    // @description - Applies a click-free pause to an audio block: the gain ramps linearly towards 0 when paused or 1 when running over FadeSec (0.03 s), then multiplies every channel of each frame.
    // @param {float[]} data - interleaved audio buffer, modified in place (full-scale units)
    // @param {int} frameCount - number of sample frames in the block
    // @param {int} channelCount - channels per frame
    // @param {bool} paused - true to fade to silence, false to fade in
    // @param {float} sampleRate - audio sample rate in hertz
    // @return {Void} - nothing
    public void Apply(float[] data, int frameCount, int channelCount, bool paused, float sampleRate)
    {
        float target = paused ? 0f : 1f, step = 1f / (FadeSec * sampleRate);
        if (gain == target) { if (target == 0f) System.Array.Clear(data, 0, frameCount * channelCount); return; }
        for (int i = 0; i < frameCount; i++)
        {
            gain = gain < target ? System.Math.Min(target, gain + step) : System.Math.Max(target, gain - step);
            for (int channelIndex = 0; channelIndex < channelCount; channelIndex++) data[i * channelCount + channelIndex] *= gain;
        }
    }
}
public abstract class SynthBase : MonoBehaviour
{
    protected CarController car; protected float sr; protected System.Random rng = new System.Random(1984);
    // @description - Connects the synthesiser to the car and sets the sample rate (the audio output rate unless one is given, which tests and offline renders use).
    // @param {CarController} carController - car whose state drives the sound
    // @param {float} sampleRate - sample rate in hertz, or 0 to use the audio output rate
    // @return {Void} - nothing
    public virtual void Init(CarController carController, float sampleRate = 0f) { car = carController; sr = sampleRate > 0f ? sampleRate : AudioSettings.outputSampleRate; }
    // @description - One sample of white noise, uniform between -1 and 1, from the synth's fixed-seed generator.
    // @return {float} - noise sample (full-scale units)
    protected float Noise() => (float)rng.NextDouble() * 2f - 1f;
    // @description - One-pole lowpass coefficient for a cutoff frequency: 1 - exp(-2 pi fc / fs). Multiply the difference between input and state by this each sample.
    // @param {float} cutoffFrequency - filter cutoff in hertz
    // @param {float} sampleRate - audio sample rate in hertz
    // @return {float} - per-sample filter coefficient between 0 and 1 (dimensionless)
    protected static float OnePole(float cutoffFrequency, float sampleRate) => 1f - Mathf.Exp(-2f * Mathf.PI * cutoffFrequency / sampleRate);
}
public class EngineSynth : SynthBase
{
    // state copied from the main thread (floats/bools: atomic)
    volatile float rpm, load, mapBar = 1f, thrArea, starterRps, crankAmp; volatile bool combusting, starter, limiter, ignition, sputter;
    volatile int clicks; bool wasComb, wasIgn; float sputterT;
    double phase; float jitter = 1f, jitS = 1f, ampS = -1f, lp, lp2, hp, clickEnv, starterPhase, motorPhase;
    const int Harm = 10;   // harmonics of the firing frequency
    // @description - Connects the engine synthesiser to the car and remembers the current ignition and combustion state so the first frame does not trigger a click or a sputter.
    // @param {CarController} car - car whose engine drives the sound
    // @param {float} sampleRate - sample rate in hertz, or 0 to use the audio output rate
    // @return {Void} - nothing
    public override void Init(CarController car, float sampleRate = 0f) { base.Init(car, sampleRate); if (car.engine != null) { wasIgn = car.engine.ignition; wasComb = car.engine.combusting; } }
    // @description - Copies the engine state to the synthesiser every frame.
    // @return {Void} - nothing
    void Update() { Sample(Time.deltaTime); }
    // @description - Copies engine state (rpm, throttle area, combustion, starter, limiter, ignition, manifold pressure) into the fields read by the audio thread. Derives engine load from manifold pressure above the zero-torque level, the starter armature speed (crank speed times the ring-to-pinion ratio) and a 1.2 s sputter after a stall, and counts ignition clicks.
    // @param {float} deltaTime - frame time in seconds (counts down the sputter timer)
    // @return {Void} - nothing
    public void Sample(float deltaTime)   // copy engine state for the audio thread
    {
        var engine = car.engine; var carParams = car.carParams; if (engine == null) return;
        rpm = engine.rpm; thrArea = engine.throttleArea; combusting = engine.combusting; starter = engine.starter; limiter = engine.limiter; ignition = engine.ignition;
        load = Mathf.Clamp01((engine.manifoldPressureRatio - carParams.mapZeroTorque) / (1f - carParams.mapZeroTorque)); mapBar = engine.manifoldPressureRatio;
        starterRps = engine.starter ? engine.rpm / 60f * engine.StarterRatio : 0f;                       // armature rev/s (pinion turns ratio x crank speed)
        crankAmp = Mathf.Clamp01(engine.rpm / carParams.idleRpm);
        if (engine.ignition != wasIgn) { clicks++; wasIgn = engine.ignition; }
        if (wasComb && !engine.combusting && engine.ignition && engine.rpm > 1f) sputterT = 1.2f;         // just stalled: misfires while it runs down
        wasComb = engine.combusting; sputterT = Mathf.Max(0f, sputterT - deltaTime); sputter = sputterT > 0f && engine.rpm > 1f;
    }
    // @description - Sets the audio-thread state directly for offline rendering (tests and the sweep tool), without a CarController.
    // @param {float} sampleRate - sample rate in hertz
    // @param {float} engineRpm - engine speed in revolutions per minute
    // @param {float} engineLoad - engine load, 0 (closed throttle) to 1 (wide open), dimensionless
    // @param {float} throttleArea - throttle opening area (same unit as Engine.throttleArea)
    // @param {bool} isCombusting - true if the engine is combusting (also sets the ignition on)
    // @param {float} manifoldPressureRatio - manifold pressure ratio (dimensionless, 1 = ambient); a negative value derives it from the load
    // @return {Void} - nothing
    public void SetOfflineState(float sampleRate, float engineRpm, float engineLoad, float throttleArea, bool isCombusting = true, float manifoldPressureRatio = -1f) { sr = sampleRate; rpm = engineRpm; load = engineLoad; mapBar = manifoldPressureRatio >= 0f ? manifoldPressureRatio : OfflineMapZero + engineLoad * (1f - OfflineMapZero); thrArea = throttleArea; combusting = isCombusting; ignition = isCombusting; crankAmp = 1f; }
    float[] mono = new float[0]; readonly AudioPauseGate pause = new AudioPauseGate();
    // @description - Unity audio callback for the engine source: renders the mono engine sound, publishes its RMS level for the tire limiter, multiplies it onto every channel of the carrier and applies the pause fade.
    // @param {float[]} data - interleaved audio buffer supplied by Unity, modified in place
    // @param {int} channelCount - channels per frame
    // @return {Void} - nothing
    void OnAudioFilterRead(float[] data, int channelCount)
    {
        int frameCount = data.Length / channelCount; if (mono.Length < frameCount) mono = new float[frameCount];
        Render(mono, frameCount);
        double sumOfSquares = 0; for (int i = 0; i < frameCount; i++) sumOfSquares += mono[i] * mono[i];
        TireSoundDsp.EngineRms = (float)System.Math.Sqrt(sumOfSquares / System.Math.Max(1, frameCount));   // tire ceiling reference (engine synth output, pre-spatial)
        for (int i = 0; i < frameCount; i++) for (int channelIndex = 0; channelIndex < channelCount; channelIndex++) data[i * channelCount + channelIndex] *= mono[i];
        pause.Apply(data, frameCount, channelCount, CarAudio.Paused, sr);
    }
    // Exhaust level model v2 -> v3 below (overrun fix after the physical air-path Engine, ece5ae0). v1 scaled level with p_EVO itself, assuming
    // closed-throttle MAP ~0.22 bar; the air-path engine gives the physically correct ~0.02-0.05 bar on overrun at 3000-5000 rpm, which
    // put overrun 18-21 dB under WOT (inaudible under tires). The acoustic source at the tailpipe is not p_EVO but the pressure STEP
    // the opening exhaust valve throws into the pipe, |p_EVO - p_back| (p_back ~1.05 atm upstream of the muffler):
    //   WOT fired:  p_EVO ~5 atm   -> +3.95 atm blowdown pulse
    //   overrun:    p_EVO ~0.1 atm -> -0.95 atm (exhaust gas rushes BACK into the near-vacuum cylinder: a strong reverse pulse; this is why
    //               a closed-throttle overrun still pops/burbles loudly at rpm). The reverse pulse radiates weaker than a blowdown of the
    //               same size (it is a rarefaction of cool gas filling through the valve curtain, and is partly absorbed by the cat/muffler
    //               before it reaches the tailpipe): weighted BackflowWeight (estimate, set so overrun sits mid of the 6-10 dB band).
    // plus the exhaust-stroke displacement pulse (piston pushes the cylinder volume out every cycle whatever the charge), modelled as an
    // equivalent PDisp atm added to the step so cruise (p_EVO ~= p_back) never nulls. Radiated level ~ source^LevelExp (muffler compression).
    // Result at 3000-5000 rpm overrun: ~7-9 dB under WOT after the load lowpass + tanh stage (target 6-10 dB). Fuel cut: similar (motoring).
    // v3 (2026-10-13 PT, after Engine v2 closed-plate leakage + residual floor raised overrun MAP to ~0.09-0.15 bar): the source is the gas VOLUME
    // the valve event moves at pipe pressure, not the linear step. Blowdown: cylinder gas expands isentropically to p_back, V_out = V (p_EVO/p_back)^(1/g) - V.
    // Backflow: pipe gas fills the cylinder until it reaches p_back, compressing the residual isentropically, V_in = V - V (p_EVO/p_back)^(1/g).
    // => |(p_EVO/p_back)^(1/g) - 1| * p_back (equivalent atm), g = burned-gas ratio of specific heats (S: Heywood ~1.3). Higher MAP -> smaller backflow pulse.
    // BackflowWeight (E) = rarefaction transmission through cat/muffler vs a blowdown pulse: 0.60 (was 0.47 on the linear step), fixed once at the reference
    // overrun state (3600 rpm, MAP 0.10 bar, fired) to sit ~8 dB under WOT; its rpm/MAP dependence then comes only from the volume law above.
    // PDisp (E) kept at the same 9.2 % of the WOT source as v2. OfflineMapZero = Engine v2 closed-throttle MAP at ~3600 rpm (D).
    public const float MotorRatio = 1.2f, CombRatio = 3.8f, LevelExp = 0.75f, AmpWot = 0.55f, PBack = 1.05f, GasGamma = 1.3f, PDispFrac = 0.092f, BackflowWeight = 0.60f, OfflineMapZero = 0.10f;
    // @description - Equivalent gas volume the exhaust valve event moves at pipe pressure: V = p_back * ((p_EVO / p_back)^(1/gamma) - 1) in atm-equivalent units (S: isentropic expansion, Heywood, gamma 1.3 burned gas). Positive is a blowdown pulse; a negative result (backflow into the cylinder) is returned as a positive size weighted by BackflowWeight (E).
    // @param {float} evolvedPressure - cylinder pressure when the exhaust valve opens, in atmospheres
    // @return {float} - pulse size in atmosphere-equivalent units (always zero or positive)
    static float PulseVolume(float evolvedPressure) { float pulseSize = PBack * (Mathf.Pow(Mathf.Max(0f, evolvedPressure) / PBack, 1f / GasGamma) - 1f); return pulseSize >= 0f ? pulseSize : -BackflowWeight * pulseSize; }
    // @description - Exhaust loudness from manifold pressure: the exhaust-valve-open pressure is manifold pressure times the motoring ratio (plus the combustion ratio when fired). The pulse size plus the exhaust-stroke displacement pulse is normalised to wide-open throttle and raised to LevelExp (muffler compression), then scaled by AmpWot.
    // @param {float} map - manifold pressure ratio (dimensionless, 1 = ambient)
    // @param {bool} fired - true if the cylinders are firing, false when motoring (overrun or fuel cut)
    // @return {float} - amplitude in full-scale units (AmpWot at wide-open throttle)
    public static float ExhaustAmp(float map, bool fired)
    {
        float exhaustValveOpenPressure = Mathf.Max(0f, map) * (MotorRatio + (fired ? CombRatio : 0f)), wideOpenPulse = PulseVolume(MotorRatio + CombRatio), displacementPulse = PDispFrac / (1f - PDispFrac) * wideOpenPulse;
        return AmpWot * Mathf.Pow((PulseVolume(exhaustValveOpenPressure) + displacementPulse) / (wideOpenPulse + displacementPulse), LevelExp);
    }
    // @description - Smooth limiter, tanh(x), used instead of a hard clamp so loud peaks do not produce harsh distortion.
    // @param {float} sample - input sample (full-scale units)
    // @return {float} - limited sample between -1 and 1
    static float SoftClip(float sample) => (float)System.Math.Tanh(sample);   // tanh instead of hard clamp
    // @description - Synthesises mono engine sound: a pulse train at the firing frequency (rpm / 60 x 2 for a four-cylinder four-stroke) built from ten harmonics plus a half-order imbalance term, with cycle-to-cycle jitter, overrun burble, starter whine and ignition click, followed by a two-pole lowpass and a soft clip. Also used offline by the -audiowav test.
    // @param {float[]} buf - output buffer for mono samples (full-scale units)
    // @param {int} sampleCount - number of samples to write
    // @return {Void} - nothing
    public void Render(float[] buf, int sampleCount)   // mono synthesis (also used offline by the -audiowav test)
    {
        float firingHz = rpm / 60f * 2f;                         // firing frequency, 4-cyl 4-stroke
        float tilt = Mathf.Lerp(1.7f, 0.9f, load);        // brighter spectrum under load
        bool fired = combusting && !limiter;
        float amp = ExhaustAmp(mapBar, fired) * (fired ? 1f : crankAmp);
        float burble = fired ? (1f - load) * Mathf.Clamp01((rpm - 1300f) / 2000f) : 0f;   // overrun pumping/burble: weak, lean, late-burning closed-throttle charges vary cycle to cycle
        float rough = combusting ? 0.06f * load + 0.22f * burble : 0.3f;   // per-cycle pulse variation, now subtle + smoothed (was 0.35 x load as a hard per-cycle amplitude step = grain)
        float lowpassCoefficient = OnePole(700f + 5500f * load, sr);     // softer (darker) off load, but the firing fundamental + low harmonics stay audible   // (no intake sound: the old white-noise intake hiss was removed on request)
        float norm = 0f; for (int k = 1; k <= Harm; k++) norm += 1f / Mathf.Pow(k, tilt); norm += 0.25f;   // peak of the harmonic sum -> keeps the pulse train within +-1
        float amplitudeSmoothing = OnePole(30f, sr), jitterSmoothing = OnePole(Mathf.Max(20f, 1.5f * firingHz), sr);   // amplitude smoothing; jitter glides over ~1/3 cycle instead of stepping
        float meshHz = starterRps * 9f;                    // starter gear-mesh whine = pinion teeth x armature rev/s (9 teeth, S)
        if (clicks > 0) { clickEnv = 1f; clicks = 0; }
        for (int i = 0; i < sampleCount; i++)
        {
            double previousPhase = phase; phase += firingHz / sr; if (phase >= 1.0) { phase -= 1.0; jitter = 1f + rough * Noise(); if (sputter && rng.NextDouble() < 0.5) jitter = 3f * (float)rng.NextDouble(); if (jitter < 0.2f) jitter = 0.2f; }
            float phaseRadians = (float)(phase * 2.0 * System.Math.PI), harmonicSum = 0f;
            for (int k = 1; k <= Harm; k++) harmonicSum += Mathf.Sin(k * phaseRadians) / Mathf.Pow(k, tilt);
            harmonicSum += 0.25f * Mathf.Sin(0.5f * phaseRadians);           // half order: cylinder-to-cylinder imbalance
            if (ampS < 0f) ampS = amp; ampS += amplitudeSmoothing * (amp - ampS); jitS += jitterSmoothing * (jitter - jitS);   // de-zipper: no amplitude steps
            float engineSample = harmonicSum / norm * ampS * jitS * (firingHz > 1f ? 1f : 0f);
            float starterWhine = 0f;
            if (starterRps > 0f || starter)
            {
                motorPhase += meshHz / sr; if (motorPhase > 1f) motorPhase -= 1f; starterPhase += (rpm / 60f * 2f) / sr; if (starterPhase > 1f) starterPhase -= 1f;
                starterWhine = 0.18f * Mathf.Sin(motorPhase * 2f * Mathf.PI) * (0.7f + 0.3f * Mathf.Sin(starterPhase * 2f * Mathf.PI)) + 0.05f * Noise();   // whine modulated by compression strokes
            }
            float click = 0f; if (clickEnv > 1e-3f) { click = clickEnv * Noise() * 0.6f; clickEnv *= Mathf.Exp(-1f / (0.004f * sr)); }
            lp += lowpassCoefficient * (engineSample - lp); lp2 += lowpassCoefficient * (lp - lp2);   // 2-pole lowpass (12 dB/oct): smoother, no fizzy top
            float outputSample = SoftClip(lp2 + starterWhine + click);
            buf[i] = outputSample;
        }
    }
}
public class TireSynth : SynthBase
{
    // tire sound v3 (TireScrub.cs: low slip tone, +2.5 semitones max, constants in TireSoundTuning). 2D stereo source, x Tires slider, then capped under the engine RMS.
    TireSoundDsp dsp; readonly float[] rat = new float[4], fzs = new float[4];
    public float[] lastRatio = new float[4];
    // @description - Connects the tire synthesiser to the car and sets it up as a 2D stereo source (no 3D panning), creating the tire DSP.
    // @param {CarController} car - car whose wheels drive the sound
    // @param {float} sampleRate - sample rate in hertz, or 0 to use the audio output rate
    // @return {Void} - nothing
    public override void Init(CarController car, float sampleRate = 0f)
    {
        base.Init(car, sampleRate); dsp = new TireSoundDsp(sr);
        var audioSource = GetComponent<AudioSource>(); if (audioSource) { audioSource.spatialBlend = 0f; audioSource.panStereo = 0f; }
    }
    // @description - Feeds the tire synthesiser with the latest wheel state every frame.
    // @return {Void} - nothing
    void Update() { Feed(); }
    // @description - Computes each wheel's scrub ratio (combined slip over its peak-grip slip at the current load) and hands ratios, vertical loads and car speed to the audio thread. Everything is zero while paused, in the menu or with no engine.
    // @return {Void} - nothing
    public void Feed()
    {
        if (dsp == null) return;
        var body = car.GetComponent<Rigidbody>(); float speed = body ? body.velocity.magnitude : 0f;
        bool running = Time.timeScale > 0f && !(car.router && car.router.menuOpen) && car.engine != null;   // silent in menu / paused
        var wheels = new[] { car.frontLeftWheel, car.frontRightWheel, car.rearLeftWheel, car.rearRightWheel };
        for (int i = 0; i < 4; i++)
        {
            var wheel = wheels[i]; float wheelLoad = Mathf.Max(wheel.verticalLoad, 1f);
            float ratio = TireScrubLaw.ScrubRatio(wheel.relaxedSlipAngle, wheel.slipRatio, TireScrubLaw.PeakAlpha(car.carParams, wheelLoad), TireScrubLaw.PeakKappa(car.carParams, wheelLoad)); lastRatio[i] = ratio;
            rat[i] = ratio; fzs[i] = running && wheel.verticalLoad > 1f ? wheel.verticalLoad : 0f;
        }
        dsp.SetTargets(rat, running ? speed : 0f, fzs);
    }
    readonly AudioPauseGate pause = new AudioPauseGate();
    // @description - Renders tire sound offline for the -audiocoast probe, using the supplied engine RMS for the ceiling.
    // @param {float[]} stereo - interleaved stereo output buffer (full-scale units)
    // @param {int} frameCount - number of sample frames
    // @param {float} engineRms - engine RMS used by the loudness ceiling (full-scale units)
    // @return {Void} - nothing
    public void RenderOffline(float[] stereo, int frameCount, float engineRms) => dsp.Render(stereo, frameCount, 2, CarAudio.TireVolume, engineRms);   // -audiocoast probe
    // @description - Unity audio callback for the tire source: renders the tire sound for the block and applies the pause fade.
    // @param {float[]} data - interleaved audio buffer supplied by Unity, modified in place
    // @param {int} channelCount - channels per frame
    // @return {Void} - nothing
    void OnAudioFilterRead(float[] data, int channelCount) { if (dsp == null) return; dsp.Render(data, data.Length / channelCount, channelCount, CarAudio.TireVolume); pause.Apply(data, data.Length / channelCount, channelCount, CarAudio.Paused, sr); }
}

/// In-game overrun diagnostic (player flag -audiocoast, used with -smoketest -scenario audiocoast): WOT up to 5000 rpm in 3rd, lift and coast
/// in gear to ~2300 rpm, repeat in 4th; logs engine state + offline-rendered engine/tire RMS (same synths, pre-spatial) every 0.1 s, then quits.
[DefaultExecutionOrder(1000)]
public class AudioCoastProbe : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    // @description - Starts the -audiocoast diagnostic probe after the scene loads when that command line flag is present.
    // @return {Void} - nothing
    static void Boot() { if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-audiocoast") >= 0) DontDestroyOnLoad(new GameObject("AudioCoastProbe").AddComponent<AudioCoastProbe>().gameObject); }
    CarController car; EngineSynth eng; TireSynth tire; float t, next, wotDb = float.NaN; int stage; const float Fs = 48000f; readonly float[] eb = new float[4800], tb = new float[9600];
    // @description - Drives the -audiocoast overrun test: wide open throttle to 5000 rpm in 3rd, then coasting down to 2300 rpm, repeated in 4th gear, logging engine state and the offline-rendered engine and tire RMS every 0.1 s, then quits.
    // @return {Void} - nothing
    void FixedUpdate()
    {
        if (!car) { car = FindObjectOfType<CarController>(); if (!car || car.engine == null) return;
            eng = new GameObject("probeEng").AddComponent<EngineSynth>(); eng.Init(car, Fs); tire = new GameObject("probeTire").AddComponent<TireSynth>(); tire.Init(car, Fs); }
        t += Time.fixedDeltaTime; if (t < 3.5f) return;
        int gear = stage < 2 ? 3 : 4; var engine = car.engine;
        if (stage == 0 || stage == 2) { car.throttle = 1f; if (car.drivetrain.gear == gear && engine.rpm >= 5000f) { stage++; Debug.Log($"COAST start gear {gear}"); } }
        else { car.throttle = 0f; car.brake = 0f; if (engine.rpm < 2300f) { stage++; Debug.Log($"COAST end gear {gear}"); if (stage >= 4) { Debug.Log("COAST done"); Application.Quit(0); } } }
        if (t < next) return; next = t + 0.1f;
        eng.Sample(0.1f); eng.Render(eb, eb.Length); double engineSumOfSquares = 0; for (int i = 2400; i < eb.Length; i++) engineSumOfSquares += eb[i] * eb[i]; float engineRms = Mathf.Sqrt((float)(engineSumOfSquares / 2400));
        tire.Feed(); tire.RenderOffline(tb, 4800, engineRms); double tireSumOfSquares = 0; for (int i = 4800; i < tb.Length; i++) tireSumOfSquares += tb[i] * tb[i]; float tireRms = Mathf.Sqrt((float)(tireSumOfSquares / 4800));
        float engineDb = 20f * Mathf.Log10(engineRms + 1e-9f), tireDb = 20f * Mathf.Log10(tireRms + 1e-9f); if (car.throttle > 0.99f && engine.rpm > 4500f) wotDb = engineDb;
        var body = car.GetComponent<Rigidbody>();
        Debug.Log(string.Format(System.Globalization.CultureInfo.InvariantCulture, "COAST,{0:0.0},{1},{2},{3:0},{4:0.0},{5:0.000},{6:0.00},{7},{8},{9:0.0},{10:0.0},{11:0.0},{12:0.0}",
            t, stage, car.drivetrain.gear, engine.rpm, body ? body.velocity.magnitude * 3.6f : 0f, engine.manifoldPressureRatio, Mathf.Clamp01((engine.manifoldPressureRatio - car.carParams.mapZeroTorque) / (1f - car.carParams.mapZeroTorque)), engine.combusting, engine.limiter, engineDb, tireDb, wotDb - engineDb, car.throttle));
    }
}
