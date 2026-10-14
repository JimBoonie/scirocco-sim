using UnityEngine;
// Headless test harness. Enabled only with -smoketest. Scenario chosen with -scenario <name>:
//   accel (default) | skidpad | rampsteer | stepsteer | lanechange | brake | brakeinturn | liftoff
// Every 10 ms logs: CSV,<scenario>,t,x,z,v_kmh,ax_g,ay_g,yawrate_dps,beta_deg,steer_deg,thr,brk,gear,rpm,FzFL,FzFR,FzRL,FzRR,wFL,wFR,wRL,wRR,pitch,roll,phase
public class SmokeTest : MonoBehaviour
{
    public CarController car; string sc = "accel"; float t, nextLog, lastShift = -10f, t100 = -1f; bool quitting; float pedal = 0.84f; // best non-locking pedal from sweep with kinematic suspension (0.80:41.5, 0.84:39.4 m, 0.85: locks 47.5 m)
    Rigidbody rb; Vector3 vPrev; float axF, ayF; int phase; float phaseT, evT, steerHold, iErr;
    // skidpad state
    const float R = 40f;
    float psR = 40f, psThr = -1f, psAy = 0.5f; int psGear = 0;   // powerstep: -radius, -thr (-1 = keep steady throttle, 0 = lift), -ay target (g), -gear (0 = auto during settle)
    void Awake()
    {
        var a = System.Environment.GetCommandLineArgs();
        float Fa(string n, float d) { int i = System.Array.IndexOf(a, n); return i >= 0 && i + 1 < a.Length && float.TryParse(a[i + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var x) ? x : d; }
        psR = Fa("-radius", 40f); psThr = Fa("-thr", -1f); psAy = Fa("-ay", 0.5f); psGear = (int)Fa("-gear", 0f);
        if (System.Array.IndexOf(a, "-smoketest") < 0) { enabled = false; return; }
        int i = System.Array.IndexOf(a, "-scenario"); if (i >= 0 && i + 1 < a.Length) sc = a[i + 1];
        int o = System.Array.IndexOf(a, "-overrides"); if (o >= 0 && o + 1 < a.Length) ApplyOverrides(a[o + 1]);
        int j = System.Array.IndexOf(a, "-pedal"); if (j >= 0 && j + 1 < a.Length) pedal = float.Parse(a[j + 1], System.Globalization.CultureInfo.InvariantCulture);
    }
    // -overrides <file>: lines "p.field=value" or "kin.field=v1;v2;..." (CarParams / SuspensionTables; float, float[], bool, enum).
    // Used by the sensitivity study and fallback-mode runs; never changes the CarParams.cs defaults.
    void ApplyOverrides(string path)
    {
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        foreach (var raw in System.IO.File.ReadAllLines(path))
        {
            var line = raw.Trim(); if (line.Length == 0 || line.StartsWith("#")) continue;
            int eq = line.IndexOf('='); string lhs = line.Substring(0, eq).Trim(), val = line.Substring(eq + 1).Trim();
            object target = lhs.StartsWith("kin.") ? (object)car.p.kin : car.p; string fname = lhs.Substring(lhs.IndexOf('.') + 1);
            var f = target.GetType().GetField(fname); if (f == null) { Debug.LogError("override: no field " + lhs); continue; }
            object v;
            if (f.FieldType == typeof(float)) v = float.Parse(val, inv);
            else if (f.FieldType == typeof(float[])) v = System.Array.ConvertAll(val.Split(';'), x => float.Parse(x, inv));
            else if (f.FieldType == typeof(bool)) v = val == "1" || val.ToLower() == "true";
            else if (f.FieldType.IsEnum) v = System.Enum.Parse(f.FieldType, val);
            else { Debug.LogError("override: unsupported type " + lhs); continue; }
            f.SetValue(target, v); Debug.Log("override " + lhs + " = " + val);
        }
    }
    void AutoGear()
    {
        if (sc == "bumpstart") return;   // gears managed by the scenario
        if (car.dt.gear < 1 || car.dt.shiftTimer > 0f || t - lastShift < 1f) return;
        if (sc == "powerstep" && (phase >= 1 || (psGear > 0 && rb && rb.velocity.magnitude > 0.8f * Mathf.Sqrt(psAy * 9.81f * psR)))) { if (phase == 0 && psGear > 0 && car.dt.gear != psGear) car.dt.SelectGear(psGear); return; }   // gear held (test gear selected near target speed)
        if (car.engine.rpm > 6000f && car.dt.gear < 5) { car.dt.Shift(1); lastShift = t; }
        else if (car.engine.rpm < 2200f && car.dt.gear > 1) { car.dt.Shift(-1); lastShift = t; }
    }
    float SpeedCtl(float vTarget, float v)   // PI throttle/brake controller, m/s
    {
        float e = vTarget - v; iErr = Mathf.Clamp(iErr + e * Time.fixedDeltaTime, -5f, 5f);
        float u = 0.4f * e + 0.15f * iErr; car.throttle = Mathf.Clamp01(u); car.brake = Mathf.Clamp01(-u * 0.5f); return u;
    }
    // circle follower centred at (R,0) [right turn] using pure pursuit
    float CircleSteer(float radius)
    {
        Vector3 c = new Vector3(radius, 0f, 0f), p = rb.position; p.y = 0f;
        Vector3 rel = p - c; float ang = Mathf.Atan2(rel.z, rel.x);
        float look = Mathf.Max(6f, rb.velocity.magnitude * 0.6f) / radius;
        Vector3 tgt = c + new Vector3(Mathf.Cos(ang - look), 0f, Mathf.Sin(ang - look)) * radius;   // clockwise = right turn
        Vector3 loc = car.transform.InverseTransformPoint(new Vector3(tgt.x, rb.position.y, tgt.z));
        float alpha = Mathf.Atan2(loc.x, loc.z); float ld = loc.magnitude;
        return Mathf.Atan(2f * car.p.wheelbase * Mathf.Sin(alpha) / ld) * Mathf.Rad2Deg;
    }
    void FixedUpdate()
    {
        if (!car || car.engine == null) return;
        if (!rb) { rb = car.GetComponent<Rigidbody>(); vPrev = rb.velocity; }
        float dt = Time.fixedDeltaTime; car.externalInput = true;
        if (sc.StartsWith("laguna") && (TrackManager.Instance == null || !TrackManager.Instance.Ready || TrackManager.Instance.info == null)) { car.throttle = 0f; car.brake = 1f; return; }   // wait for the track scene
        t += dt; phaseT += dt;
        car.clutch = 0f; car.handbrake = false;
        Vector3 vel = rb.velocity; Vector3 acc = (vel - vPrev) / dt; vPrev = vel;
        float k = 1f - Mathf.Exp(-dt / 0.03f);
        axF += k * (Vector3.Dot(acc, car.transform.forward) / 9.81f - axF);
        ayF += k * (Vector3.Dot(acc, car.transform.right) / 9.81f - ayF);
        float v = vel.magnitude;
        if (t > 3f && car.dt.gear == 0 && sc != "revsweep" && !(sc == "stallrestart" && t > 6f)) car.dt.Shift(1);
        if (wav != null) { synth.Sample(dt); int ns = Mathf.RoundToInt((t + dt) * WavRate) - wavN; if (ns > 0) { if (wavN + ns > wav.Length) System.Array.Resize(ref wav, wav.Length * 2); var tmp = new float[ns]; synth.Render(tmp, ns); System.Array.Copy(tmp, 0, wav, wavN, ns); wavN += ns; } }
        bool settle = t < 3f;
        if (settle) { car.throttle = 0f; car.brake = sc.StartsWith("laguna") ? 1f : 0f; car.steer = 0f; car.autoClutch = true; }   // the test driver launches with the (auto) clutch: with real static tire grip a clutch dump at idle stalls the engine, as in a real car (scenarios that test the clutch held engaged set autoClutch = false themselves)
        else Run(v);
        if (car.dt.shiftTimer > 0f) car.throttle = 0f;
        AutoGear();
        if (t >= nextLog)
        {
            nextLog += 0.01f;
            float vx = Vector3.Dot(vel, car.transform.forward), vy = Vector3.Dot(vel, car.transform.right);
            float beta = v > 1f ? Mathf.Atan2(vy, vx) * Mathf.Rad2Deg : 0f;
            float pitch = Mathf.DeltaAngle(0f, car.transform.eulerAngles.x), roll = Mathf.DeltaAngle(0f, car.transform.eulerAngles.z);
            Debug.Log(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "CSV,{0},{1:0.00},{2:0.00},{3:0.00},{4:0.00},{5:0.000},{6:0.000},{7:0.00},{8:0.00},{9:0.00},{10:0.00},{11:0.00},{12},{13:0},{14:0},{15:0},{16:0},{17:0},{18:0.0},{19:0.0},{20:0.0},{21:0.0},{22:0.00},{23:0.00},{24}",
                sc, t, rb.position.x, rb.position.z, v * 3.6f, axF, ayF, rb.angularVelocity.y * Mathf.Rad2Deg, beta, car.steer, car.throttle, car.brake,
                car.dt.gear, car.engine.rpm, car.FL.Fz, car.FR.Fz, car.RL.Fz, car.RR.Fz, car.FL.omega, car.FR.omega, car.RL.omega, car.RR.omega, pitch, roll, phase));
            Debug.Log(string.Format(System.Globalization.CultureInfo.InvariantCulture, "TQ,{0:0.00},{1:0.000},{2:0.000},{3:0.000},{4:0.00},{5:0.0},{6:0.0},{7:0},{8:0},{9:0},{10:0}",
                t, ayF, car.FL.alphaRelaxed * Mathf.Rad2Deg, car.FR.alphaRelaxed * Mathf.Rad2Deg, car.columnTorque, car.roadTorqueL, car.roadTorqueR, car.FL.force.y, car.FR.force.y, car.FL.Fz, car.FR.Fz));
        }
        float tEnd = sc == "skidpad" ? 160f : sc == "accel" ? 60f : sc == "topspeed" ? 150f : sc == "laguna" ? lagEnd : sc == "lagunagrass" ? 400f : sc == "lagunakerb" ? 90f : sc == "bumpstart" ? 60f : sc == "lagunaoff" ? 200f : sc == "powerstep" ? 220f : sc == "lagunatherm" ? 900f : sc == "thermslide" ? 200f : 70f;
        if (sc == "lagunakerb" && (t > tEnd || phase == 99) && !quitting) KerbReport();
        if (sc == "laguna" && t > tEnd && !quitting) LagunaReport();
        if (sc == "laguna" && phase == 99 && !quitting) LagunaReport();
        if ((t > tEnd || phase == 99) && !quitting) { quitting = true; WriteWav(); Debug.Log("SMOKE done " + sc); Application.Quit(0); }
    }
    // -audiowav <path>: render the engine synth offline (same code as the live AudioSource filter) at WavRate, synchronous with physics
    const int WavRate = 48000; float[] wav; int wavN; EngineSynth synth; string wavPath;
    void Start()
    {
        var a = System.Environment.GetCommandLineArgs(); int w = System.Array.IndexOf(a, "-audiowav");
        if (w >= 0 && w + 1 < a.Length && car) { wavPath = a[w + 1]; wav = new float[WavRate * 20]; synth = new GameObject("OfflineSynth").AddComponent<EngineSynth>(); synth.enabled = false; synth.Init(car, WavRate); }
    }
    void WriteWav()
    {
        if (wav == null) return;
        using (var bw = new System.IO.BinaryWriter(System.IO.File.Create(wavPath)))
        {
            int bytes = wavN * 2; bw.Write(System.Text.Encoding.ASCII.GetBytes("RIFF")); bw.Write(36 + bytes); bw.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt ")); bw.Write(16); bw.Write((short)1); bw.Write((short)1);
            bw.Write(WavRate); bw.Write(WavRate * 2); bw.Write((short)2); bw.Write((short)16); bw.Write(System.Text.Encoding.ASCII.GetBytes("data")); bw.Write(bytes);
            for (int i = 0; i < wavN; i++) bw.Write((short)Mathf.Clamp(wav[i] * 32767f, -32768f, 32767f));
        }
        Debug.Log("WAV written " + wavPath + " samples " + wavN);
    }
    void Next(int p) { phase = p; phaseT = 0f; }
    // ---- off-track launch diagnostics (lagunaoff): wheel ground-height jumps, chassis contacts, body vertical speed ----
    readonly float[] diagLastY = new float[4]; float diagNext;
    public class ContactDiag : MonoBehaviour { void OnCollisionEnter(Collision c) => Log("enter", c); void OnCollisionStay(Collision c) { if (c.impulse.magnitude > 200f) Log("stay", c); }
        void Log(string k, Collision c) { var rb = GetComponent<Rigidbody>(); var n = c.contactCount > 0 ? c.GetContact(0).normal : Vector3.zero; var pt = c.contactCount > 0 ? c.GetContact(0).point : Vector3.zero;
            Debug.Log($"DIAGC {k} {c.collider.name} t {Time.time:0.000} impulse {c.impulse.ToString("F0")} relVel {c.relativeVelocity.ToString("F1")} normal {n.ToString("F2")} pt {pt.ToString("F2")} contacts {c.contactCount} body v {rb.velocity.ToString("F2")} y {rb.position.y:0.00}"); } }
    void LaunchDiag()
    {
        if (!car.GetComponent<ContactDiag>()) { car.gameObject.AddComponent<ContactDiag>(); Debug.Log($"DIAG maxDepenetration default {Physics.defaultMaxDepenetrationVelocity} rb {rb.maxDepenetrationVelocity} contactOffset {Physics.defaultContactOffset} cd {rb.collisionDetectionMode}"); }
        var ws = new[] { car.FL, car.FR, car.RL, car.RR }; string[] nm = { "FL", "FR", "RL", "RR" };
        for (int i = 0; i < 4; i++) { var w = ws[i]; float y = w.grounded ? w.Hit.point.y : float.NaN; if (!float.IsNaN(y) && !float.IsNaN(diagLastY[i]) && Mathf.Abs(y - diagLastY[i]) > 0.25f) Debug.Log($"DIAGW {nm[i]} ground jump {diagLastY[i]:0.00} -> {y:0.00} on {w.Hit.collider.name} (normal {w.Hit.normal.ToString("F2")}) Ftire {w.Ftire:0} Fsusp {w.Fsusp:0} travel {w.travel:0.000} body vy {rb.velocity.y:0.00} t {t:0.000}"); diagLastY[i] = y; }
        if (rb.velocity.y > 1.5f && t >= diagNext) { diagNext = t + 0.02f; string s = ""; for (int i = 0; i < 4; i++) { var w = ws[i]; s += $" {nm[i]}:{(w.grounded ? w.Hit.collider.name : "air")} Ft {w.Ftire:0} Fs {w.Fsusp:0} tr {w.travel:0.000}"; } Debug.Log($"DIAGV t {t:0.000} pos {rb.position.ToString("F2")} v {rb.velocity.ToString("F2")} angv {rb.angularVelocity.ToString("F2")}{s}"); }
    }
    // ---- bump start: 2nd gear at 40 km/h, clutch engaged, brakes locked -> engine stalls; release brake, clutch stays engaged -> must re-fire.
    // -ignoff: ignition switched off at the stall -> must NOT re-fire. Clutch pedal/auto clutch never used.
    float bsStallV, bsMaxRpm; bool bsIgnOff;
    void BumpStart(float v)
    {
        car.autoClutch = phase == 0; car.clutch = 0f;   // run-up: launch with the auto clutch; from the brake lock on the clutch stays engaged (no auto clutch)
        if (phase == 0)
        {
            bsIgnOff = System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-ignoff") >= 0;
            if (car.dt.gear == 1 && v > 6f) car.dt.SelectGear(2);
            SpeedCtl(45f / 3.6f, v);
            if (car.dt.gear == 2 && car.dt.shiftTimer <= 0f && v >= 40f / 3.6f && t > 3f) { Debug.Log($"BUMP firing speed (model) {car.engine.FireRpm:0} rpm: net-positive closed-pedal torque from {car.engine.NetPositiveRpm:0} rpm, floor minFiringRpm {car.p.minFiringRpm:0}, stallRpm {car.p.stallRpm:0}"); Next(1); }
        }
        else if (phase == 1)   // lock the brakes with the clutch engaged
        {
            car.throttle = 0f; car.brake = 1f;
            if (!car.engine.combusting && car.engine.rpm < 20f && phaseT > 0.2f) { bsStallV = v; if (bsIgnOff) car.engine.ignition = false; Debug.Log($"BUMP stalled at {v * 3.6f:0.0} km/h in gear {car.dt.gear} (rpm {car.engine.rpm:0}), ignition {(car.engine.ignition ? "ON" : "OFF")}"); Next(2); }
            if (phaseT > 5f) { Debug.Log("BUMPSTART RESULT FAIL engine never stalled"); Next(99); }
        }
        else if (phase == 2)   // release the brake, clutch engaged, no throttle
        {
            car.throttle = 0f; car.brake = 0f; bsMaxRpm = Mathf.Max(bsMaxRpm, car.engine.rpm);
            if (phaseT > 4f)
            {
                bool running = car.engine.combusting && car.engine.rpm > car.p.stallRpm; bool pass = bsIgnOff ? !car.engine.combusting : running;
                Debug.Log($"BUMPSTART RESULT {(pass ? "PASS" : "FAIL")} ignition {(bsIgnOff ? "OFF" : "ON")}: stalled at {bsStallV * 3.6f:0.0} km/h, max crank {bsMaxRpm:0} rpm after release, now {car.engine.State} at {car.engine.rpm:0} rpm, {v * 3.6f:0.0} km/h (expect {(bsIgnOff ? "no restart" : "restart")})");
                Next(99);
            }
        }
    }
    // ---- power-on / lift-off in a steady turn (diagnostic). Phase 0: hold a psR circle at psAy (closed-loop steer + speed) until steady.
    // Phase 1: steering FROZEN, throttle -> psThr (or kept at the steady value as a control run), gear held, 3 s. Per-wheel logging every 10 ms.
    float psSteer, psThrHold, psThrF; float[] psRef; readonly System.Collections.Generic.List<string> psOut = new System.Collections.Generic.List<string>(); float psNext;
    float[] PsState(float v)
    {
        float yr = rb.angularVelocity.y * Mathf.Rad2Deg; var vel = rb.velocity; float vx = Vector3.Dot(vel, car.transform.forward), vy = Vector3.Dot(vel, car.transform.right);
        float beta = Mathf.Atan2(vy, Mathf.Max(0.1f, vx)) * Mathf.Rad2Deg; float aF = 0.5f * (car.FL.alphaRelaxed + car.FR.alphaRelaxed) * Mathf.Rad2Deg, aR = 0.5f * (car.RL.alphaRelaxed + car.RR.alphaRelaxed) * Mathf.Rad2Deg;
        return new[] { v * 3.6f, yr, yr * Mathf.Deg2Rad / Mathf.Max(0.1f, v), beta, ayF, axF, aF, aR, U(car.FL), U(car.FR), U(car.RL), U(car.RR), car.FL.Fz + car.FR.Fz, car.RL.Fz + car.RR.Fz };
    }
    float U(WheelCorner w) { var p = car.p; float dfz = (w.Fz - p.Fz0) / p.Fz0; float D = Mathf.Max(1f, (p.pDy1 + p.pDy2 * dfz) * w.Fz); return w.force.magnitude / D; }   // friction usage ~ |F| / (mu_y(Fz) Fz)
    string PsFmt(string tag, float[] s) => string.Format(System.Globalization.CultureInfo.InvariantCulture, "PSTEP {0}: v {1:0.0} km/h, yaw {2:0.00} deg/s, curvature {3:0.00000} 1/m (R {4:0.0} m), beta {5:+0.00;-0.00} deg, ay {6:0.000} g, ax {7:+0.000;-0.000} g, alphaF {8:0.00} alphaR {9:0.00} deg (aF-aR {10:+0.00;-0.00}), usage FL {11:0.00} FR {12:0.00} RL {13:0.00} RR {14:0.00}, FzF {15:0} FzR {16:0} N",
        tag, s[0], s[1], s[2], 1f / Mathf.Max(1e-5f, Mathf.Abs(s[2])), s[3], s[4], s[5], s[6], s[7], s[6] - s[7], s[8], s[9], s[10], s[11], s[12], s[13]);
    float PsBeta() { var vel = rb.velocity; return Mathf.Atan2(Vector3.Dot(vel, car.transform.right), Mathf.Max(0.1f, Vector3.Dot(vel, car.transform.forward))); }
    void PowerStep(float v)
    {
        psThrF += (1f - Mathf.Exp(-Time.fixedDeltaTime / 0.5f)) * (car.throttle - psThrF);
        if (phase == 0)
        {
            car.steer = Mathf.Clamp(CircleSteerR(psR), -car.p.MeanLockDeg, car.p.MeanLockDeg);
            float vT = Mathf.Sqrt(psAy * 9.81f * psR); SpeedCtl(Mathf.Min(vT, 30f / 3.6f + 1f * Mathf.Max(0f, t - 3f)), v);
            var pp = rb.position; float re = Mathf.Abs(Mathf.Sqrt((pp.x - psR) * (pp.x - psR) + pp.z * pp.z) - psR);
            // steady test uses dv/dt = body-x accel + ay*sin(beta) (logged: ax +0.012, ay +0.496, beta -1.32 deg at constant v) (at 100 m the ay*sin(beta) term alone is 0.011 g)
            if (t > 45f && re < 1.0f && Mathf.Abs(v - vT) < 0.15f && Mathf.Abs(axF + ayF * Mathf.Sin(PsBeta())) < 0.01f) { psSteer = car.steer; psThrHold = psThrF; psRef = PsState(v); psOut.Add(PsFmt($"steady  R {psR:0} m gear {car.dt.gear} steer {psSteer:0.000} deg thr {psThrHold:0.000}", psRef)); Next(1); psNext = 0f; }
            if (t > 160f) { Debug.Log("PSTEP no steady state"); phase = 99; }
        }
        else
        {
            car.steer = psSteer; car.brake = 0f; car.throttle = psThr < 0f ? psThrHold : psThr;
            if (phaseT >= psNext - 1e-4f) { var st = PsState(v); psOut.Add(PsFmt($"t+{phaseT:0.00}s thr {car.throttle:0.00}", st)); psNext += phaseT < 1f ? 0.25f : 0.5f; }
            Debug.Log(string.Format(System.Globalization.CultureInfo.InvariantCulture, "PW,{0:0.000},{1}", phaseT, string.Join(",", System.Linq.Enumerable.Select(new[] { car.FL, car.FR, car.RL, car.RR },
                w => $"{w.alphaRelaxed * Mathf.Rad2Deg:0.000},{w.slipRatio:0.0000},{w.Fz:0},{w.force.x:0},{w.force.y:0},{U(w):0.000},{w.steerDeg:0.000},{w.camberDeg:0.00}"))));
            if (phaseT > 3.001f) { foreach (var o in psOut) Debug.Log(o); var e = PsState(v); Debug.Log(string.Format(System.Globalization.CultureInfo.InvariantCulture, "PSTEP RESULT thr {0:0.00} (steady {1:0.000}): curvature change {2:+0.0;-0.0}% at 1 s, {3:+0.0;-0.0}% at 3 s; yaw rate change {4:+0.00;-0.00} deg/s at 3 s; beta change {5:+0.00;-0.00} deg",
                car.throttle, psThrHold, 100f * (K1 - psRef[2]) / psRef[2], 100f * (e[2] - psRef[2]) / psRef[2], e[1] - psRef[1], e[3] - psRef[3])); phase = 99; }
            if (Mathf.Abs(phaseT - 1f) < 0.5f * Time.fixedDeltaTime) K1 = PsState(v)[2];
        }
    }
    float K1;
    float CircleSteerR(float radius) => CircleSteer(radius);
    // ---- track drive (-track laguna -scenario laguna): spawn on S/F, follow the centreline with pure pursuit at a curvature-limited speed,
    // and check the car stays on the surface (all wheels hit a collider, body above the surface, within the track edges + shoulder).
    float lagEnd = 63f; int lagIdx, lagUngrounded, lagSamples, lagMaxUngroundedRun, lagRun, lagLaps; float lagMaxOff = 0f, lagMinClear = 99f, lagDist, lagMaxV, lagWorstY = 99f; Vector3 lagPrev; bool lagFail; string lagWhy = "";
    float thNext = 4f; Vector3 thHead;
    void ThermLog(string tag)
    {   // user-facing units: mph, F, psi
        string o = $"THERM {sc} {tag} v {rb.velocity.magnitude * DebugHUD.MphPerMs:0} mph";
        foreach (var w in new[] { car.FL, car.FR, car.RL, car.RR })
        {
            var th = w.thermal; if (th == null) continue;
            o += $" | {w.name} {TireThermal.CToF(th.Ts[0]):0}/{TireThermal.CToF(th.Ts[1]):0}/{TireThermal.CToF(th.Ts[2]):0}F bulk {TireThermal.CToF(th.Tb):0} carc {TireThermal.CToF(th.Tc):0} gas {TireThermal.CToF(th.Tg):0} {th.pGauge / TireThermal.PaPerPsi:0.0}psi grip {w.ts.muy * 100f:0}% res {th.Residual / System.Math.Max(th.EIn, 1):E1}";
        }
        Debug.Log(o);
    }
    void LagunaDrive(float v)
    {
        car.autoClutch = true; var ti = TrackManager.Instance.info; var c = ti.Centreline; int n = c.Count; Vector3 p = rb.position;
        int best = lagIdx; float bd = float.MaxValue;
        for (int k = -5; k < 40; k++) { int i = (lagIdx + k + n) % n; float d = (new Vector2(c[i].x - p.x, c[i].z - p.z)).sqrMagnitude; if (d < bd) { bd = d; best = i; } }
        if (best < lagIdx - n / 2) { lagLaps++; if (sc == "lagunatherm") { ThermLog($"LAP {lagLaps} {t - 4f:0.0}s"); if (lagLaps >= 3) phase = 99; } } lagIdx = best;
        float look = Mathf.Max(8f, v * 0.9f); int ti0 = (lagIdx + Mathf.CeilToInt(look / 2f)) % n; var tgt = c[ti0];
        if (kerbMode)   // kerb test: aim the car's centre (edge - 0.3 m) on the inside of each corner, where Blender Bot's procedural kerbs are
        {
            Vector3 f0 = c[(ti0 + 1) % n] - c[(ti0 + n - 1) % n], f1 = c[(ti0 + 6) % n] - c[(ti0 + 4) % n]; f0.y = f1.y = 0f; float turn = Vector3.SignedAngle(f0, f1, Vector3.up);
            if (Mathf.Abs(turn) > 2f) { var fr = f0.normalized; var right = new Vector3(fr.z, 0f, -fr.x); float side = Mathf.Sign(turn); float hwi = side > 0 ? ti.HalfWidths[ti0].y : ti.HalfWidths[ti0].x; tgt += right * side * (hwi - 0.3f); }
        }
        Vector3 loc = car.transform.InverseTransformPoint(new Vector3(tgt.x, p.y, tgt.z)); float alpha = Mathf.Atan2(loc.x, loc.z);
        car.steer = Mathf.Clamp(Mathf.Atan(2f * car.p.wheelbase * Mathf.Sin(alpha) / Mathf.Max(1f, loc.magnitude)) * Mathf.Rad2Deg, -car.p.MeanLockDeg, car.p.MeanLockDeg);
        bool hot = sc == "lagunatherm"; float vT = hot ? 30f : 24f, lagG = hot ? 0.45f : 0.55f;   // m/s cap; corner speed from the tightest radius in the next ~70 m at 0.55 g (lagunatherm: 0.45 g, 30 m/s; 0.75 and 0.6 g left the road on cold tyres)
        for (int k = 2; k < 35; k++) { var a0 = c[(lagIdx + k - 2) % n]; var a1 = c[(lagIdx + k) % n]; var a2 = c[(lagIdx + k + 2) % n];
            Vector3 d1 = a1 - a0, d2 = a2 - a1; d1.y = d2.y = 0f; float ang = Vector3.Angle(d1, d2) * Mathf.Deg2Rad; float R = ang > 1e-4f ? d1.magnitude / ang : 1e4f;
            float vc = Mathf.Sqrt(lagG * 9.81f * R), dist = 2f * k; vT = Mathf.Min(vT, Mathf.Sqrt(vc * vc + 2f * 4f * dist)); }
        SpeedCtl(vT, v);
        if (t < 4f) return; lagSamples++;
        bool all = car.FL.grounded && car.FR.grounded && car.RL.grounded && car.RR.grounded;
        if (!all) { lagUngrounded++; lagRun++; lagMaxUngroundedRun = Mathf.Max(lagMaxUngroundedRun, lagRun); } else lagRun = 0;
        ti.Nearest(p, out float lat); var hw = ti.HalfWidths[lagIdx]; float edge = lat > 0f ? hw.y : hw.x; lagMaxOff = Mathf.Max(lagMaxOff, Mathf.Abs(lat) - edge);
        if (Physics.Raycast(p + Vector3.up * 3f, Vector3.down, out var hit, 20f)) { float clr = p.y - hit.point.y; lagMinClear = Mathf.Min(lagMinClear, clr); } else { lagFail = true; lagWhy += "no surface under car; "; }
        lagWorstY = Mathf.Min(lagWorstY, p.y - c[lagIdx].y);
        if (lagPrev != Vector3.zero) lagDist += Vector3.Distance(lagPrev, p); lagPrev = p; lagMaxV = Mathf.Max(lagMaxV, v);
        if (lagRun * Time.fixedDeltaTime > 0.25f && !lagFail) { lagFail = true; lagWhy += $"wheels off the surface >0.25 s at t={t:0.0}; "; }
        if (Mathf.Abs(lat) - edge > 3.5f && !lagFail) { lagFail = true; lagWhy += $"left the road (+shoulder) at s~{lagIdx * 2} m; "; }
        if (p.y - c[lagIdx].y < -1.5f && !lagFail) { lagFail = true; lagWhy += $"fell below the road at s~{lagIdx * 2} m; "; }
        if (kerbMode) { lagFail = false; lagWhy = ""; return; }   // kerb test: cutting kerbs is intended; its own checks are in KerbReport
        if (lagFail) { if (sc == "lagunatherm") { ThermLog("FAIL " + lagWhy); } Next(99); }
    }
    // ---- kerb test: per-wheel surface under the contact (SurfaceTag of the collider hit) and its load / travel; kerb vs asphalt
    bool kerbMode; readonly float[] kN = new float[2], kFz = new float[2], kFz2 = new float[2], kTrMin = { 9f, 9f }, kTrMax = { -9f, -9f }, kdFz = new float[2]; readonly float[] lastFz = new float[4];
    int kerbHitObjects;
    void KerbSample()
    {
        if (t < 4f) return; var ws = new[] { car.FL, car.FR, car.RL, car.RR };
        for (int i = 0; i < 4; i++)
        {
            var w = ws[i]; float dFz = Mathf.Abs(w.Fz - lastFz[i]) / Time.fixedDeltaTime; lastFz[i] = w.Fz;
            if (!w.grounded || !Physics.Raycast(w.transform.position, -w.transform.up, out var h, 1.5f)) continue;
            var st = SurfaceTag.Of(h.collider); int k = st == SurfaceType.Kerb ? 0 : st == SurfaceType.Asphalt ? 1 : -1; if (k < 0) continue;
            kN[k]++; kFz[k] += w.Fz; kFz2[k] += w.Fz * w.Fz; kTrMin[k] = Mathf.Min(kTrMin[k], w.travel); kTrMax[k] = Mathf.Max(kTrMax[k], w.travel); kdFz[k] += dFz;
        }
    }
    void KerbReport()
    {
        string S(int k) { float n = Mathf.Max(1f, kN[k]), m = kFz[k] / n, sd = Mathf.Sqrt(Mathf.Max(0f, kFz2[k] / n - m * m)); return $"{kN[k]:0} wheel-samples, Fz mean {m:0} N sd {sd:0} N, mean |dFz/dt| {kdFz[k] / n / 1000f:0.0} kN/s, travel {kTrMin[k] * 1000f:0}..{kTrMax[k] * 1000f:0} mm"; }
        float n0 = Mathf.Max(1f, kN[0]), n1 = Mathf.Max(1f, kN[1]); float rate0 = kdFz[0] / n0, rate1 = kdFz[1] / n1;
        bool ok = kN[0] >= 200 && rate0 > 1.5f * rate1 && TrackManager.Instance.FallResets == 0;
        Debug.Log("KERB on kerb: " + S(0)); Debug.Log("KERB on asphalt: " + S(1));
        Debug.Log($"LAGUNAKERB RESULT {(ok ? "PASS" : "FAIL")} kerb load-rate ratio {rate0 / Mathf.Max(1e-3f, rate1):0.0}x asphalt (need >1.5x, >=200 kerb samples), drove {lagDist:0} m, fall resets {TrackManager.Instance.FallResets}");
        quitting = true; Application.Quit(ok ? 0 : 3);
    }
    // ---- off-road rest test: teleport onto grass/runoff at several places (beside the track and far away), let the car settle 4 s on the brake,
    // check all wheels hit a surface, the car is at rest, and its height matches the DEM terrain under it (TerrainData height + ride height).
    int gIdx = -1; int gFails; float gT, gDiagNext; readonly System.Collections.Generic.List<string> gRows = new System.Collections.Generic.List<string>();
    Vector3 GrassPoint(int k, out string name)
    {
        var ti = TrackManager.Instance.info; var c = ti.Centreline; int n = c.Count;
        Vector3 Off(float s, float side, float extra, out string nm, string label)
        {
            int i = Mathf.RoundToInt(s / 2f) % n; var f = c[(i + 1) % n] - c[i]; f.y = 0f; f.Normalize(); var right = new Vector3(f.z, 0f, -f.x);
            float edge = side > 0 ? ti.HalfWidths[i].y : ti.HalfWidths[i].x; nm = $"{label} (s={s:0} m, {(side > 0 ? "right" : "left")} {edge + extra:0} m from centreline)";
            var p = c[i] + right * side * (edge + extra); p.y = 200f; return p;
        }
        switch (k)
        {
            case 0: return Off(246f, 1f, 30f, out name, "outside T1");
            case 1: return Off(500f, 1f, 18f, out name, "outside Andretti hairpin");
            case 2: return Off(2500f, -1f, 10f, out name, "Corkscrew entry, left");
            case 3: return Off(2560f, 1f, 12f, out name, "Corkscrew, right");
            case 4: return Off(1100f, -1f, 25f, out name, "T4 inside");
            case 5: return Off(3290f, 1f, 15f, out name, "outside T11");
            case 6: name = "far field NE (900, -600)"; return new Vector3(900f, 200f, -600f);
            case 7: name = "far field W (-500, 500)"; return new Vector3(-500f, 200f, 500f);
            case 8: name = "far field S (300, -1000)"; return new Vector3(300f, 200f, -1000f);
            default: name = ""; return Vector3.zero;
        }
    }
    // ---- park: S/F spawn, neutral, engine idling. 0-13 s brake pedal + handbrake, 13-23 s handbrake only (rear locked).
    // Measured 3-10 s after each change: displacement (creep), max speed and max |accel| (oscillation). Checks the low-speed stick tire model.
    Vector3 pkPos0; float pkMaxV, pkMaxA, pkSlope; bool pkHave; Vector3 pkVPrev; readonly string[] pkRow = new string[2]; int pkFail;
    void Park(float v)
    {
        car.steer = 0f; car.throttle = 0f; if (car.dt.gear != 0) car.dt.SelectGear(0);
        bool hb = t >= 13f; car.brake = hb ? 0f : 1f; car.handbrake = true;
        int i = t >= 6f && t < 13f ? 0 : t >= 16f && t < 23f ? 1 : -1;
        if (i >= 0)
        {
            var vel = rb.velocity;
            if (!pkHave) { pkHave = true; pkPos0 = rb.position; pkMaxV = 0f; pkMaxA = 0f; pkVPrev = vel;
                if (Physics.Raycast(rb.position + Vector3.up * 5f, Vector3.down, out var hh, 20f)) pkSlope = Vector3.Angle(hh.normal, Vector3.up); }
            pkMaxV = Mathf.Max(pkMaxV, vel.magnitude); pkMaxA = Mathf.Max(pkMaxA, (vel - pkVPrev).magnitude / Time.fixedDeltaTime); pkVPrev = vel;
            bool end = (i == 0 && t + Time.fixedDeltaTime >= 13f) || (i == 1 && t + Time.fixedDeltaTime >= 23f);
            if (end)
            {
                float d = (rb.position - pkPos0).magnitude; bool ok = d < 0.005f && pkMaxV < 0.01f; if (!ok) pkFail++;
                pkRow[i] = string.Format(System.Globalization.CultureInfo.InvariantCulture, "PARK {0} {1}: ground slope {2:0.0} deg, displacement over 7 s {3:0.0} mm, max speed {4:0.0000} m/s, max |accel| {5:0.000} m/s2 (need < 5 mm and < 0.01 m/s)",
                    ok ? "PASS" : "FAIL", i == 0 ? "brake pedal + handbrake" : "handbrake only", pkSlope, d * 1000f, pkMaxV, pkMaxA);
                Debug.Log(pkRow[i]); pkHave = false;
            }
        }
        if (t >= 23f) { Debug.Log($"PARK RESULT {(pkFail == 0 ? "PASS" : "FAIL")} {2 - pkFail}/2"); Next(99); }
    }
    Vector3 gPos2; bool gHave2;
    void LagunaGrass(float v)
    {
        car.throttle = 0f; car.brake = 1f; car.steer = 0f; car.handbrake = true;
        var tm = TrackManager.Instance;
        if (gIdx == 0 && t >= gDiagNext) { gDiagNext = t + 0.25f; if (!car.GetComponent<ContactDiag>()) car.gameObject.AddComponent<ContactDiag>(); var ws = new[] { car.FL, car.FR, car.RL, car.RR }; string q = "";
            foreach (var w in ws) q += $" [{(w.grounded ? w.Hit.collider.name.Substring(0, 8) : "air")} Fz {w.Fz:0} om {w.omega:0.00} sr {w.slipRatio:0.00} sa {w.slipAngle * 57.3f:0.0} F {w.force.x:0}/{w.force.y:0} Tb {w.brakeTorque:0}]";
            Debug.Log($"GDIAG t {t - gT:0.00} v {rb.velocity.ToString("F2")} up {car.transform.up.ToString("F2")}{q}"); }
        if (gIdx >= 0 && !gHave2 && t - gT >= 2f) { gPos2 = rb.position; gHave2 = true; }
        if (gIdx < 0 || t - gT > 4f)
        {
            if (gIdx >= 0) GrassMeasure(v);
            gIdx++; var p = GrassPoint(gIdx, out _); if (p == Vector3.zero) { LagunaGrassReport(); Next(99); return; }
            tm.Place(p, 30f * gIdx); gT = t; gHave2 = false;
            if (Physics.Raycast(p + Vector3.up * 30f, Vector3.down, out var h0, 600f)) Debug.Log($"GRASSDBG place hit '{h0.collider.name}' layer {h0.collider.gameObject.layer} at {h0.point} rb {rb.position}");
        }
    }
    void GrassMeasure(float v)
    {
        GrassPoint(gIdx, out string name); var p = rb.position; var tc = Object.FindObjectOfType<TerrainCollider>();
        float hTerr = float.NaN, slope = 0f; string under = "none";
        if (tc) hTerr = TrackManager.TerrainHeightAt(p, out slope);
        float hSurf = float.NaN; if (Physics.Raycast(p + Vector3.up * 3f, Vector3.down, out var hit, 30f)) { under = hit.collider.name; hSurf = hit.point.y; }
        float ride = car.p.hubDropDesign + car.p.wheelRadius;   // design height of the car origin above the ground
        float dz = p.y - ride - hTerr, ds = p.y - ride - hSurf;   // ds: vs the collider actually under the car (= visual surface: grass/runoff mesh or terrain)
        int gr = (car.FL.grounded ? 1 : 0) + (car.FR.grounded ? 1 : 0) + (car.RL.grounded ? 1 : 0) + (car.RR.grounded ? 1 : 0);
        bool ok = tc != null && gr >= 3 && (v < 0.3f || slope > 20f) && Mathf.Abs(ds) < 0.5f && tm_FallResets() == 0; bool dataWarn = Mathf.Abs(dz) >= 0.5f;
        if (!ok) gFails++;
        gRows.Add(string.Format(System.Globalization.CultureInfo.InvariantCulture, "GRASS {0} {1}: creep 2-4 s {13:0.0} mm, pos ({2:0.0},{3:0.00},{4:0.0}) DEM {5:0.00} car-ride-surface {11:+0.00;-0.00} m, car-ride-DEM {6:+0.00;-0.00} m{12}, wheels grounded {7}/4, speed {8:0.00} m/s, surface under car '{9}', slope {10:0}° (>20°: locked car may slide; must stay on the ground)",
            ok ? "PASS" : "FAIL", name, p.x, p.y, p.z, hTerr, dz, gr, v, under, slope, ds, dataWarn ? " (surface collider/visual mesh >0.5 m off the heightmap: data issue)" : "", gHave2 ? (p - gPos2).magnitude * 1000f : float.NaN));
    }
    int tm_FallResets() => TrackManager.Instance ? TrackManager.Instance.FallResets : 0;
    void LagunaGrassReport() { foreach (var r in gRows) Debug.Log(r); Debug.Log($"LAGUNAGRASS RESULT {(gFails == 0 ? "PASS" : "FAIL")} {gRows.Count - gFails}/{gRows.Count} points, fall resets {tm_FallResets()}"); }
    // ---- off-track at speed: start on the road 150 m upstream at 132 km/h (wheel spin matched), coast in neutral, steer off the asphalt onto runoff/grass/terrain.
    // Checks every physics step: car never below the heightmap (fall-through), a collider always under the car, no fall-safety reset.
    int oIdx = -1, oFails, oResets0; float oT, oMinAboveDem, oMinV, oMaxOff; int oNoSurf, oSteps; string oName; readonly System.Collections.Generic.List<string> oRows = new System.Collections.Generic.List<string>();
    readonly System.Collections.Generic.Dictionary<string, int> oSurf = new System.Collections.Generic.Dictionary<string, int>();
    static readonly (float s, float side, string name)[] OffSpots = {   // s of the run-off point, side +1 right / -1 left
        (2440f, -1f, "wide LEFT before the Corkscrew (T8 approach)"), (3330f, 1f, "outside RIGHT of T11 exit"), (260f, 1f, "outside RIGHT of T1"),
        (2600f, 1f, "Corkscrew exit RIGHT (T8A)"), (1990f, 1f, "outside RIGHT of T6"), (2440f, 1f, "RIGHT before the Corkscrew") };
    void LagunaOff(float v)
    {
        var tm = TrackManager.Instance; var ti = tm.info; var c = ti.Centreline; int n = c.Count;
        if (oIdx < 0 || t - oT > 7f)
        {
            if (oIdx >= 0) OffReport1();
            oIdx++; if (oIdx >= OffSpots.Length) { foreach (var r in oRows) Debug.Log(r); Debug.Log($"LAGUNAOFF RESULT {(oFails == 0 ? "PASS" : "FAIL")} {OffSpots.Length - oFails}/{OffSpots.Length} runs, fall resets {tm.FallResets}"); Next(99); return; }
            var sp = OffSpots[oIdx]; int i0 = ((int)((sp.s - 150f) / 2f) + n) % n; var f = c[(i0 + 1) % n] - c[i0]; f.y = 0f;
            tm.Place(c[i0], Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg);
            rb.velocity = car.transform.forward * (132f / 3.6f); foreach (var w in new[] { car.FL, car.FR, car.RL, car.RR }) w.omega = 132f / 3.6f / car.p.wheelRadius;
            oT = t; oMinAboveDem = 99f; oNoSurf = 0; oSteps = 0; oMaxOff = 0f; oMinV = 999f; oName = sp.name; oResets0 = tm.FallResets; oSurf.Clear();
        }
        var spot = OffSpots[oIdx]; Vector3 p = rb.position; int idx = ti.Nearest(p, out float lat);
        // target line: centreline until 120 m before the spot, then ramps out to edge + 25 m on the chosen side by the spot, stays there
        float ds = (idx * 2f - spot.s + 3600f + 1800f) % 3600f - 1800f;   // signed distance to the spot along the lap (m)
        float k = Mathf.Clamp01((ds + 120f) / 120f); int ti0 = (idx + 12) % n; var f0 = c[(ti0 + 1) % n] - c[ti0]; f0.y = 0f; f0.Normalize(); var right = new Vector3(f0.z, 0f, -f0.x);
        float edge = spot.side > 0 ? ti.HalfWidths[ti0].y : ti.HalfWidths[ti0].x; var tgt = c[ti0] + right * spot.side * k * (edge + 25f);
        Vector3 loc = car.transform.InverseTransformPoint(new Vector3(tgt.x, p.y, tgt.z)); float alpha = Mathf.Atan2(loc.x, loc.z);
        car.steer = Mathf.Clamp(Mathf.Atan(2f * car.p.wheelbase * Mathf.Sin(alpha) / Mathf.Max(1f, loc.magnitude)) * Mathf.Rad2Deg, -car.p.MeanLockDeg, car.p.MeanLockDeg);
        car.throttle = 0f; car.brake = 0f; car.handbrake = false; car.dt.SelectGear(0);
        oSteps++; float edgeHere = lat > 0 ? ti.HalfWidths[idx].y : ti.HalfWidths[idx].x; oMaxOff = Mathf.Max(oMaxOff, Mathf.Abs(lat) - edgeHere);
        { float hT = TrackManager.TerrainHeightAt(p, out _); if (!float.IsNaN(hT)) oMinAboveDem = Mathf.Min(oMinAboveDem, p.y - hT); }
        if (Physics.Raycast(p + Vector3.up * 2f, Vector3.down, out var h, 10f)) { var st = SurfaceTag.Of(h.collider).ToString(); oSurf[st] = oSurf.TryGetValue(st, out var q) ? q + 1 : 1; } else { oNoSurf++; if (oNoSurf == 1 || oNoSurf == 300) { var hs = Physics.RaycastAll(p + Vector3.up * 2f, Vector3.down, 100f); string hl = ""; foreach (var hh in hs) hl += $" {hh.collider.name}@{hh.point.y:0.00}"; Debug.Log($"OFFDIAG no collider: t {t:0.00} pos {p.ToString("F2")} vel {rb.velocity.ToString("F1")} terrain(finest) {TrackManager.TerrainHeightAt(p, out _):0.00} hits<=100m:{hl}"); } }
        if (Mathf.Abs(lat) - edgeHere > 2f) oMinV = Mathf.Min(oMinV, v);
    }
    void OffReport1()
    {
        var tm = TrackManager.Instance; int resets = tm.FallResets - oResets0; float ride = car.p.hubDropDesign + car.p.wheelRadius;
        bool ok = resets == 0 && oNoSurf == 0 && oMinAboveDem > ride - 0.5f && oMaxOff > 5f;
        if (!ok) oFails++;
        oRows.Add($"OFF {(ok ? "PASS" : "FAIL")} {oName}: entered at 132 km/h, max {oMaxOff:0.0} m beyond the asphalt edge, slowest off-track {oMinV * 3.6f:0} km/h, min (car origin - heightmap) {oMinAboveDem:0.00} m (ride height {ride:0.00}), steps with no collider under car {oNoSurf}/{oSteps}, fall resets {resets}, surfaces under car: {string.Join(", ", System.Linq.Enumerable.Select(oSurf, kv => $"{kv.Key} {100f * kv.Value / Mathf.Max(1, oSteps):0}%"))}");
    }
    void LagunaReport()
    {
        Debug.Log(string.Format(System.Globalization.CultureInfo.InvariantCulture,
            "LAGUNA RESULT {0} drove {1:0} m in {2:0.0} s (track s reached {3} m, laps {4}), vmax {5:0} km/h, all-4-wheels-grounded {6:0.0}% (longest gap {7:0} ms), max beyond edge {8:0.00} m, min body-origin clearance {9:0.00} m, min (car y - centreline z) {10:0.00} m {11}",
            lagFail ? "FAIL" : "PASS", lagDist, t - 4f, lagIdx * 2, lagLaps, lagMaxV * 3.6f, 100f * (lagSamples - lagUngrounded) / Mathf.Max(1, lagSamples), lagMaxUngroundedRun * Time.fixedDeltaTime * 1000f, lagMaxOff, lagMinClear, lagWorstY, lagWhy));
    }
    void Run(float v)
    {
        float kmh = v * 3.6f;
        switch (sc)
        {
            case "revsweep":      // spawn engine off -> ignition -> starter -> idle -> neutral rev sweep to the limiter -> lift -> ignition off
                if (t < 0.05f) car.engine.SetRunning(false);
                car.engine.ignition = t > 1f && t < 14f; car.engine.starter = t > 1.5f && t < 3f;
                car.throttle = t < 4f ? 0f : t < 10f ? (t - 4f) / 6f : t < 12f ? 1f : 0f; if (t > 16f) Next(99); return;
            case "park": Park(v); return;
            case "kbdlaunch":     // keyboard driver: E into 1st (done at t=3 by the harness), clutch never touched, throttle after 1 s; auto clutch on
                car.autoClutch = true; car.throttle = t < 4f ? 0f : 0.4f; if (t > 12f) Next(99); return;
            case "stallrestart":  // auto clutch off: stalls in gear at zero throttle; then neutral + starter restarts it
                car.autoClutch = false; car.throttle = 0f;
                if (t > 6f && car.dt.gear != 0) car.dt.SelectGear(0);
                car.engine.starter = t > 6.5f && t < 8f; if (t > 11f) Next(99); return;
            case "laguna": LagunaDrive(v); return;
            case "lagunatherm": car.p.tireThermal = true; LagunaDrive(v); if (t >= thNext) { thNext = t + 10f; ThermLog($"t {t:0}"); } return;   // 3 brisk laps, thermal model on
            case "thermslide":   // thermal model on: 60 s sliding on the 40 m circle (target 45 mph, above the ~40 mph limit), then 120 s straight at 62 mph
                car.p.tireThermal = true;
                if (t < 63f) { car.steer = Mathf.Clamp(CircleSteer(R), -car.p.MeanLockDeg, car.p.MeanLockDeg); SpeedCtl(72f / 3.6f, v); }
                else { if (thHead == Vector3.zero) thHead = Vector3.ProjectOnPlane(car.transform.forward, Vector3.up).normalized; car.steer = Mathf.Clamp(2f * Vector3.SignedAngle(Vector3.ProjectOnPlane(car.transform.forward, Vector3.up), thHead, Vector3.up), -10f, 10f); SpeedCtl(100f / 3.6f, v); }
                if (t >= thNext) { thNext = t + 5f; ThermLog($"t {t:0} {(t < 63f ? "slide" : "straight")}"); }
                return;
            case "lagunaoff": LaunchDiag(); LagunaOff(v); return;
            case "lagunakerb": kerbMode = true; LagunaDrive(v); KerbSample(); return;
            case "lagunagrass": LagunaGrass(v); return;
            case "accel": case "topspeed":   // topspeed = accel held for 150 s
                car.steer = 0f; car.brake = 0f; car.throttle = 1f;
                if (t100 < 0f && kmh >= 100f) { t100 = t - 3f; Debug.Log($"SMOKE 0-100 km/h = {t100:0.00} s"); }
                break;
            case "powerstep": PowerStep(v); return;
            case "bumpstart": BumpStart(v); return;
            case "skidpad":      // constant radius, speed ramps 0.25 km/h per s from 30 km/h
                car.steer = Mathf.Clamp(CircleSteer(R), -car.p.MeanLockDeg, car.p.MeanLockDeg);
                SpeedCtl((35f + 0.5f * Mathf.Max(0f, t - 3f)) / 3.6f, v);
                { var pp = rb.position; if (t > 20f && Mathf.Abs(Mathf.Sqrt((pp.x - R) * (pp.x - R) + pp.z * pp.z) - R) > 4f) phase = 99; }
                break;
            case "rampsteer":    // 80 km/h, steer ramps 0.5 deg/s (road wheel) after reaching speed
                if (phase == 0) { car.steer = 0f; SpeedCtl(80f / 3.6f, v); if (kmh > 79f && phaseT > 12f) Next(1); }
                else { SpeedCtl(80f / 3.6f, v); car.steer = 0.5f * phaseT; if (car.steer > 10f) phase = 99; }
                break;
            case "stepsteer":    // 80 km/h, 2.5 deg step (~0.4 g target) for 6 s
                SpeedCtl(80f / 3.6f, v);
                if (phase == 0) { car.steer = 0f; if (kmh > 79f && phaseT > 12f) Next(1); }
                else { car.steer = Mathf.MoveTowards(car.steer, 2.5f, 25f * Time.fixedDeltaTime); if (phaseT > 6f) phase = 99; }   // 25 deg/s road-wheel ramp (~440 deg/s handwheel)
                break;
            case "lanechange":   // 80 km/h, one full sine period of 3 deg amplitude over 2.5 s
                SpeedCtl(80f / 3.6f, v);
                if (phase == 0) { car.steer = 0f; if (kmh > 79f && phaseT > 12f) Next(1); }
                else { car.steer = phaseT < 2.5f ? 3f * Mathf.Sin(2f * Mathf.PI * phaseT / 2.5f) : 0f; if (phaseT > 7f) phase = 99; }
                break;
            case "brake":        // accelerate to 105, then full brake (no ABS: lock-up test)
            case "brakethr":     // same with -pedal (default 0.85) (approximates threshold braking)
                car.steer = 0f;
                if (phase == 0) { car.throttle = 1f; car.brake = 0f; if (kmh > 105f) Next(1); }
                else if (phase == 1) { car.throttle = 0f; car.brake = sc == "brakethr" ? pedal : 1f; car.clutch = 1f; if (kmh < 0.5f) Next(2); }
                else { car.brake = 1f; car.clutch = 1f; if (phaseT > 1f) phase = 99; }
                break;
            case "brakeinturn":  // steady 40 m circle at ~0.6 g (≈ 63 km/h) then freeze steer + 0.4 brake (≈0.4 g)
            case "liftoff":      // same, then freeze steer + throttle off
                if (phase == 0)
                {
                    car.steer = Mathf.Clamp(CircleSteer(R), -car.p.MeanLockDeg, car.p.MeanLockDeg);
                    SpeedCtl(Mathf.Min(60f, 35f + 1f * Mathf.Max(0f, t - 3f)) / 3.6f, v);   // gentle ramp to 60 km/h (~0.71 g on R=40)
                    var pp = rb.position; float re = Mathf.Abs(Mathf.Sqrt((pp.x - R) * (pp.x - R) + pp.z * pp.z) - R);
                    if (t > 40f && re < 1f) { steerHold = car.steer; Next(1); }
                }
                else
                {
                    car.steer = steerHold;
                    if (sc == "liftoff") { car.throttle = 0f; car.brake = 0f; } else { car.throttle = 0f; car.brake = 0.4f; car.clutch = 1f; }
                    if (phaseT > 3f) phase = 99;
                }
                break;
        }
    }
}
