// Procedural car audio (no samples). Two 3D sources: engine bay (engine, starter, ignition click, stall sputter, limiter cut) and tires (scrub).
// Physics-derived inputs: firing frequency = rpm/60 * 2 (4-cyl 4-stroke), load from manifold pressure, starter gear-mesh frequency from
// pinion teeth x motor speed (ring/pinion ratio), tire scrub from sliding velocity x normal load.
// Mix gains / filter corners below are PERCEPTUAL audio levels (not vehicle physics).
using UnityEngine;
public class CarAudio : MonoBehaviour
{
    public CarController car; public static float Volume = 0.8f, TireVolume = 1f; public static bool Mute;   // Volume = master (engine + tires), TireVolume = extra tire-only gain
    void Start()
    {
        if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-smoketest") >= 0 || !car) { enabled = false; return; }
        var p = car.p;
        Make<EngineSynth>("EngineAudio", new Vector3(0f, 0.1f, p.wheelbase * 0.5f + 0.15f)).Init(car);   // transverse engine over the front axle
        Make<TireSynth>("TireAudio", new Vector3(0f, -0.4f, 0f)).Init(car);
    }
    T Make<T>(string n, Vector3 local) where T : SynthBase
    {
        var g = new GameObject(n); g.transform.SetParent(car.transform, false); g.transform.localPosition = local;
        var src = g.AddComponent<AudioSource>(); var sr = AudioSettings.outputSampleRate;
        var clip = AudioClip.Create(n + "_carrier", sr, 1, sr, false); var ones = new float[sr]; for (int i = 0; i < sr; i++) ones[i] = 1f; clip.SetData(ones, 0);   // constant carrier: the filter output keeps 3D attenuation/panning
        src.clip = clip; src.loop = true; src.spatialBlend = 1f; src.dopplerLevel = 0f; src.minDistance = 1.5f; src.maxDistance = 200f; src.rolloffMode = AudioRolloffMode.Logarithmic; src.playOnAwake = false;
        var s = g.AddComponent<T>(); src.Play(); return s;
    }
    public static volatile bool Paused;   // menu open / Time.timeScale == 0 -> all car audio fades to silence
    void Update() { AudioListener.volume = Mute ? 0f : Volume; Paused = Time.timeScale <= 0f || (car && car.router && car.router.menuOpen); }
}
/// click-free pause gate: per-sample linear ramp (FadeSec) towards 0 (paused) or 1 (running). Pure; unit tested.
public class AudioPauseGate
{
    public const float FadeSec = 0.03f;
    public float gain = 1f;
    /// multiplies n interleaved frames (ch channels) in place
    public void Apply(float[] data, int n, int ch, bool paused, float sampleRate)
    {
        float target = paused ? 0f : 1f, step = 1f / (FadeSec * sampleRate);
        if (gain == target) { if (target == 0f) System.Array.Clear(data, 0, n * ch); return; }
        for (int i = 0; i < n; i++)
        {
            gain = gain < target ? System.Math.Min(target, gain + step) : System.Math.Max(target, gain - step);
            for (int c = 0; c < ch; c++) data[i * ch + c] *= gain;
        }
    }
}
public abstract class SynthBase : MonoBehaviour
{
    protected CarController car; protected float sr; protected System.Random rng = new System.Random(1984);
    public virtual void Init(CarController c, float sampleRate = 0f) { car = c; sr = sampleRate > 0f ? sampleRate : AudioSettings.outputSampleRate; }
    protected float Noise() => (float)rng.NextDouble() * 2f - 1f;
    protected static float OnePole(float fc, float sr) => 1f - Mathf.Exp(-2f * Mathf.PI * fc / sr);
}
public class EngineSynth : SynthBase
{
    // state copied from the main thread (floats/bools: atomic)
    volatile float rpm, load, thrArea, starterRps, crankAmp; volatile bool combusting, starter, limiter, ignition, sputter;
    volatile int clicks; bool wasComb, wasIgn; float sputterT;
    double phase; float jitter = 1f, lp, hp, clickEnv, starterPhase, motorPhase;
    const int Harm = 10;   // harmonics of the firing frequency
    public override void Init(CarController c, float sampleRate = 0f) { base.Init(c, sampleRate); if (c.engine != null) { wasIgn = c.engine.ignition; wasComb = c.engine.combusting; } }
    void Update() { Sample(Time.deltaTime); }
    public void Sample(float dt)   // copy engine state for the audio thread
    {
        var e = car.engine; var p = car.p; if (e == null) return;
        rpm = e.rpm; thrArea = e.throttleArea; combusting = e.combusting; starter = e.starter; limiter = e.limiter; ignition = e.ignition;
        load = Mathf.Clamp01((e.map - p.mapZeroTorque) / (1f - p.mapZeroTorque));
        starterRps = e.starter ? e.rpm / 60f * e.StarterRatio : 0f;                       // armature rev/s (pinion turns ratio x crank speed)
        crankAmp = Mathf.Clamp01(e.rpm / p.idleRpm);
        if (e.ignition != wasIgn) { clicks++; wasIgn = e.ignition; }
        if (wasComb && !e.combusting && e.ignition && e.rpm > 1f) sputterT = 1.2f;         // just stalled: misfires while it runs down
        wasComb = e.combusting; sputterT = Mathf.Max(0f, sputterT - dt); sputter = sputterT > 0f && e.rpm > 1f;
    }
    /// offline render (tests / sweep tool): set the audio-thread state directly, no CarController needed
    public void SetOfflineState(float sampleRate, float rpmV, float loadV, float throttleArea, bool comb = true) { sr = sampleRate; rpm = rpmV; load = loadV; thrArea = throttleArea; combusting = comb; ignition = comb; crankAmp = 1f; }
    float[] mono = new float[0]; readonly AudioPauseGate pause = new AudioPauseGate();
    void OnAudioFilterRead(float[] data, int ch)
    {
        int n = data.Length / ch; if (mono.Length < n) mono = new float[n];
        Render(mono, n);
        double acc = 0; for (int i = 0; i < n; i++) acc += mono[i] * mono[i];
        TireSoundDsp.EngineRms = (float)System.Math.Sqrt(acc / System.Math.Max(1, n));   // tire ceiling reference (engine synth output, pre-spatial)
        for (int i = 0; i < n; i++) for (int c = 0; c < ch; c++) data[i * ch + c] *= mono[i];
        pause.Apply(data, n, ch, CarAudio.Paused, sr);
    }
    public void Render(float[] buf, int n)   // mono synthesis (also used offline by the -audiowav test)
    {
        float f = rpm / 60f * 2f;                         // firing frequency, 4-cyl 4-stroke
        float tilt = Mathf.Lerp(1.7f, 0.9f, load);        // brighter spectrum under load
        float amp = combusting && !limiter ? Mathf.Lerp(0.12f, 0.55f, load) : 0.10f * crankAmp;   // overrun / motoring quieter
        float rough = combusting ? 0.35f * load : 0.6f;
        float aLP = OnePole(200f + 6000f * load, sr);   // (no intake sound: the old white-noise intake hiss was removed on request)
        float norm = 0f; for (int k = 1; k <= Harm; k++) norm += 1f / Mathf.Pow(k, tilt); norm += 0.25f;   // peak of the harmonic sum -> keeps the pulse train within +-1
        float meshF = starterRps * 9f;                    // starter gear-mesh whine = pinion teeth x armature rev/s (9 teeth, S)
        if (clicks > 0) { clickEnv = 1f; clicks = 0; }
        for (int i = 0; i < n; i++)
        {
            double prev = phase; phase += f / sr; if (phase >= 1.0) { phase -= 1.0; jitter = 1f + rough * Noise(); if (sputter && rng.NextDouble() < 0.5) jitter = 3f * (float)rng.NextDouble(); }
            float th = (float)(phase * 2.0 * System.Math.PI), s = 0f;
            for (int k = 1; k <= Harm; k++) s += Mathf.Sin(k * th) / Mathf.Pow(k, tilt);
            s += 0.25f * Mathf.Sin(0.5f * th);           // half order: cylinder-to-cylinder imbalance
            float eng = s / norm * amp * jitter * (f > 1f ? 1f : 0f);
            float st = 0f;
            if (starterRps > 0f || starter)
            {
                motorPhase += meshF / sr; if (motorPhase > 1f) motorPhase -= 1f; starterPhase += (rpm / 60f * 2f) / sr; if (starterPhase > 1f) starterPhase -= 1f;
                st = 0.18f * Mathf.Sin(motorPhase * 2f * Mathf.PI) * (0.7f + 0.3f * Mathf.Sin(starterPhase * 2f * Mathf.PI)) + 0.05f * Noise();   // whine modulated by compression strokes
            }
            float click = 0f; if (clickEnv > 1e-3f) { click = clickEnv * Noise() * 0.6f; clickEnv *= Mathf.Exp(-1f / (0.004f * sr)); }
            lp += aLP * (eng - lp);
            float o = Mathf.Clamp(lp + st + click, -1f, 1f);
            buf[i] = o;
        }
    }
}
public class TireSynth : SynthBase
{
    // tire sound v3 (TireScrub.cs: low slip tone, +2.5 semitones max, constants in TireSoundTuning). 2D stereo source, x Tires slider, then capped under the engine RMS.
    TireSoundDsp dsp; readonly float[] rat = new float[4], fzs = new float[4];
    public float[] lastRatio = new float[4];
    public override void Init(CarController c, float sampleRate = 0f)
    {
        base.Init(c, sampleRate); dsp = new TireSoundDsp(sr);
        var src = GetComponent<AudioSource>(); if (src) { src.spatialBlend = 0f; src.panStereo = 0f; }
    }
    void Update()
    {
        if (dsp == null) return;
        var rb = car.GetComponent<Rigidbody>(); float speed = rb ? rb.velocity.magnitude : 0f;
        bool running = Time.timeScale > 0f && !(car.router && car.router.menuOpen) && car.engine != null;   // silent in menu / paused
        var ws = new[] { car.FL, car.FR, car.RL, car.RR };
        for (int i = 0; i < 4; i++)
        {
            var w = ws[i]; float fz = Mathf.Max(w.Fz, 1f);
            float ratio = TireScrubLaw.ScrubRatio(w.alphaRelaxed, w.slipRatio, TireScrubLaw.PeakAlpha(car.p, fz), TireScrubLaw.PeakKappa(car.p, fz)); lastRatio[i] = ratio;
            rat[i] = ratio; fzs[i] = running && w.Fz > 1f ? w.Fz : 0f;
        }
        dsp.SetTargets(rat, running ? speed : 0f, fzs);
    }
    readonly AudioPauseGate pause = new AudioPauseGate();
    void OnAudioFilterRead(float[] data, int ch) { if (dsp == null) return; dsp.Render(data, data.Length / ch, ch, CarAudio.TireVolume); pause.Apply(data, data.Length / ch, ch, CarAudio.Paused, sr); }
}
