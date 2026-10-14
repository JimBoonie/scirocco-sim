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
    volatile float rpm, load, mapBar = 1f, thrArea, starterRps, crankAmp; volatile bool combusting, starter, limiter, ignition, sputter;
    volatile int clicks; bool wasComb, wasIgn; float sputterT;
    double phase; float jitter = 1f, jitS = 1f, ampS = -1f, lp, lp2, hp, clickEnv, starterPhase, motorPhase;
    const int Harm = 10;   // harmonics of the firing frequency
    public override void Init(CarController c, float sampleRate = 0f) { base.Init(c, sampleRate); if (c.engine != null) { wasIgn = c.engine.ignition; wasComb = c.engine.combusting; } }
    void Update() { Sample(Time.deltaTime); }
    public void Sample(float dt)   // copy engine state for the audio thread
    {
        var e = car.engine; var p = car.p; if (e == null) return;
        rpm = e.rpm; thrArea = e.throttleArea; combusting = e.combusting; starter = e.starter; limiter = e.limiter; ignition = e.ignition;
        load = Mathf.Clamp01((e.map - p.mapZeroTorque) / (1f - p.mapZeroTorque)); mapBar = e.map;
        starterRps = e.starter ? e.rpm / 60f * e.StarterRatio : 0f;                       // armature rev/s (pinion turns ratio x crank speed)
        crankAmp = Mathf.Clamp01(e.rpm / p.idleRpm);
        if (e.ignition != wasIgn) { clicks++; wasIgn = e.ignition; }
        if (wasComb && !e.combusting && e.ignition && e.rpm > 1f) sputterT = 1.2f;         // just stalled: misfires while it runs down
        wasComb = e.combusting; sputterT = Mathf.Max(0f, sputterT - dt); sputter = sputterT > 0f && e.rpm > 1f;
    }
    /// offline render (tests / sweep tool): set the audio-thread state directly, no CarController needed
    public void SetOfflineState(float sampleRate, float rpmV, float loadV, float throttleArea, bool comb = true, float mapV = -1f) { sr = sampleRate; rpm = rpmV; load = loadV; mapBar = mapV >= 0f ? mapV : OfflineMapZero + loadV * (1f - OfflineMapZero); thrArea = throttleArea; combusting = comb; ignition = comb; crankAmp = 1f; }
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
    static float PulseVolume(float pEvo) { float v = PBack * (Mathf.Pow(Mathf.Max(0f, pEvo) / PBack, 1f / GasGamma) - 1f); return v >= 0f ? v : -BackflowWeight * v; }
    public static float ExhaustAmp(float map, bool fired)
    {
        float pEvo = Mathf.Max(0f, map) * (MotorRatio + (fired ? CombRatio : 0f)), wot = PulseVolume(MotorRatio + CombRatio), pDisp = PDispFrac / (1f - PDispFrac) * wot;
        return AmpWot * Mathf.Pow((PulseVolume(pEvo) + pDisp) / (wot + pDisp), LevelExp);
    }
    static float SoftClip(float x) => (float)System.Math.Tanh(x);   // tanh instead of hard clamp
    public void Render(float[] buf, int n)   // mono synthesis (also used offline by the -audiowav test)
    {
        float f = rpm / 60f * 2f;                         // firing frequency, 4-cyl 4-stroke
        float tilt = Mathf.Lerp(1.7f, 0.9f, load);        // brighter spectrum under load
        bool fired = combusting && !limiter;
        float amp = ExhaustAmp(mapBar, fired) * (fired ? 1f : crankAmp);
        float burble = fired ? (1f - load) * Mathf.Clamp01((rpm - 1300f) / 2000f) : 0f;   // overrun pumping/burble: weak, lean, late-burning closed-throttle charges vary cycle to cycle
        float rough = combusting ? 0.06f * load + 0.22f * burble : 0.3f;   // per-cycle pulse variation, now subtle + smoothed (was 0.35 x load as a hard per-cycle amplitude step = grain)
        float aLP = OnePole(700f + 5500f * load, sr);     // softer (darker) off load, but the firing fundamental + low harmonics stay audible   // (no intake sound: the old white-noise intake hiss was removed on request)
        float norm = 0f; for (int k = 1; k <= Harm; k++) norm += 1f / Mathf.Pow(k, tilt); norm += 0.25f;   // peak of the harmonic sum -> keeps the pulse train within +-1
        float aSm = OnePole(30f, sr), aJ = OnePole(Mathf.Max(20f, 1.5f * f), sr);   // amplitude smoothing; jitter glides over ~1/3 cycle instead of stepping
        float meshF = starterRps * 9f;                    // starter gear-mesh whine = pinion teeth x armature rev/s (9 teeth, S)
        if (clicks > 0) { clickEnv = 1f; clicks = 0; }
        for (int i = 0; i < n; i++)
        {
            double prev = phase; phase += f / sr; if (phase >= 1.0) { phase -= 1.0; jitter = 1f + rough * Noise(); if (sputter && rng.NextDouble() < 0.5) jitter = 3f * (float)rng.NextDouble(); if (jitter < 0.2f) jitter = 0.2f; }
            float th = (float)(phase * 2.0 * System.Math.PI), s = 0f;
            for (int k = 1; k <= Harm; k++) s += Mathf.Sin(k * th) / Mathf.Pow(k, tilt);
            s += 0.25f * Mathf.Sin(0.5f * th);           // half order: cylinder-to-cylinder imbalance
            if (ampS < 0f) ampS = amp; ampS += aSm * (amp - ampS); jitS += aJ * (jitter - jitS);   // de-zipper: no amplitude steps
            float eng = s / norm * ampS * jitS * (f > 1f ? 1f : 0f);
            float st = 0f;
            if (starterRps > 0f || starter)
            {
                motorPhase += meshF / sr; if (motorPhase > 1f) motorPhase -= 1f; starterPhase += (rpm / 60f * 2f) / sr; if (starterPhase > 1f) starterPhase -= 1f;
                st = 0.18f * Mathf.Sin(motorPhase * 2f * Mathf.PI) * (0.7f + 0.3f * Mathf.Sin(starterPhase * 2f * Mathf.PI)) + 0.05f * Noise();   // whine modulated by compression strokes
            }
            float click = 0f; if (clickEnv > 1e-3f) { click = clickEnv * Noise() * 0.6f; clickEnv *= Mathf.Exp(-1f / (0.004f * sr)); }
            lp += aLP * (eng - lp); lp2 += aLP * (lp - lp2);   // 2-pole lowpass (12 dB/oct): smoother, no fizzy top
            float o = SoftClip(lp2 + st + click);
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
    void Update() { Feed(); }
    public void Feed()
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
    public void RenderOffline(float[] stereo, int n, float engineRms) => dsp.Render(stereo, n, 2, CarAudio.TireVolume, engineRms);   // -audiocoast probe
    void OnAudioFilterRead(float[] data, int ch) { if (dsp == null) return; dsp.Render(data, data.Length / ch, ch, CarAudio.TireVolume); pause.Apply(data, data.Length / ch, ch, CarAudio.Paused, sr); }
}

/// In-game overrun diagnostic (player flag -audiocoast, used with -smoketest -scenario audiocoast): WOT up to 5000 rpm in 3rd, lift and coast
/// in gear to ~2300 rpm, repeat in 4th; logs engine state + offline-rendered engine/tire RMS (same synths, pre-spatial) every 0.1 s, then quits.
[DefaultExecutionOrder(1000)]
public class AudioCoastProbe : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot() { if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-audiocoast") >= 0) DontDestroyOnLoad(new GameObject("AudioCoastProbe").AddComponent<AudioCoastProbe>().gameObject); }
    CarController car; EngineSynth eng; TireSynth tire; float t, next, wotDb = float.NaN; int stage; const float Fs = 48000f; readonly float[] eb = new float[4800], tb = new float[9600];
    void FixedUpdate()
    {
        if (!car) { car = FindObjectOfType<CarController>(); if (!car || car.engine == null) return;
            eng = new GameObject("probeEng").AddComponent<EngineSynth>(); eng.Init(car, Fs); tire = new GameObject("probeTire").AddComponent<TireSynth>(); tire.Init(car, Fs); }
        t += Time.fixedDeltaTime; if (t < 3.5f) return;
        int gear = stage < 2 ? 3 : 4; var e = car.engine;
        if (stage == 0 || stage == 2) { car.throttle = 1f; if (car.dt.gear == gear && e.rpm >= 5000f) { stage++; Debug.Log($"COAST start gear {gear}"); } }
        else { car.throttle = 0f; car.brake = 0f; if (e.rpm < 2300f) { stage++; Debug.Log($"COAST end gear {gear}"); if (stage >= 4) { Debug.Log("COAST done"); Application.Quit(0); } } }
        if (t < next) return; next = t + 0.1f;
        eng.Sample(0.1f); eng.Render(eb, eb.Length); double a = 0; for (int i = 2400; i < eb.Length; i++) a += eb[i] * eb[i]; float eRms = Mathf.Sqrt((float)(a / 2400));
        tire.Feed(); tire.RenderOffline(tb, 4800, eRms); double b = 0; for (int i = 4800; i < tb.Length; i++) b += tb[i] * tb[i]; float tRms = Mathf.Sqrt((float)(b / 4800));
        float eDb = 20f * Mathf.Log10(eRms + 1e-9f), tDb = 20f * Mathf.Log10(tRms + 1e-9f); if (car.throttle > 0.99f && e.rpm > 4500f) wotDb = eDb;
        var rb = car.GetComponent<Rigidbody>();
        Debug.Log(string.Format(System.Globalization.CultureInfo.InvariantCulture, "COAST,{0:0.0},{1},{2},{3:0},{4:0.0},{5:0.000},{6:0.00},{7},{8},{9:0.0},{10:0.0},{11:0.0},{12:0.0}",
            t, stage, car.dt.gear, e.rpm, rb ? rb.velocity.magnitude * 3.6f : 0f, e.map, Mathf.Clamp01((e.map - car.p.mapZeroTorque) / (1f - car.p.mapZeroTorque)), e.combusting, e.limiter, eDb, tDb, wotDb - eDb, car.throttle));
    }
}
