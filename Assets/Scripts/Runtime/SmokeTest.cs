using UnityEngine;
// Headless test harness. Enabled only with -smoketest. Scenario chosen with -scenario <name>:
//   accel (default) | skidpad | rampsteer | stepsteer | lanechange | brake | brakeinturn | liftoff
// Every 10 ms logs: CSV,<scenario>,t,x,z,v_kmh,ax_g,ay_g,yawrate_dps,beta_deg,steer_deg,thr,brk,gear,rpm,FzFL,FzFR,FzRL,FzRR,wFL,wFR,wRL,wRR,pitch,roll,phase
public class SmokeTest : MonoBehaviour
{
    public CarController car; string scenario = "accel"; float elapsedTime, nextLog, lastShift = -10f, t100 = -1f; bool quitting; float pedal = 0.84f; // best non-locking pedal from sweep with kinematic suspension (0.80:41.5, 0.84:39.4 m, 0.85: locks 47.5 m)
    Rigidbody body; Vector3 vPrev; float axF, ayF; int phase; float phaseT, evT, steerHold, iErr;
    // skidpad state
    const float skidpadRadius = 40f;
    float psR = 40f, psThr = -1f, psAy = 0.5f; int psGear = 0;   // powerstep: -radius, -thr (-1 = keep steady throttle, 0 = lift), -ay target (g), -gear (0 = auto during settle)
    // @description - Reads the command line (-smoketest, -scenario, -overrides, -pedal, and the power-step flags -radius, -thr, -ay, -gear) and disables the harness unless -smoketest is present.
    // @return {Void} - nothing
    void Awake()
    {
        var arguments = System.Environment.GetCommandLineArgs();
        float FloatArgument(string flagName, float defaultValue) { int flagIndex = System.Array.IndexOf(arguments, flagName); return flagIndex >= 0 && flagIndex + 1 < arguments.Length && float.TryParse(arguments[flagIndex + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var parsedValue) ? parsedValue : defaultValue; }
        psR = FloatArgument("-radius", 40f); psThr = FloatArgument("-thr", -1f); psAy = FloatArgument("-ay", 0.5f); psGear = (int)FloatArgument("-gear", 0f);
        if (System.Array.IndexOf(arguments, "-smoketest") < 0) { enabled = false; return; }
        int flagIndex = System.Array.IndexOf(arguments, "-scenario"); if (flagIndex >= 0 && flagIndex + 1 < arguments.Length) scenario = arguments[flagIndex + 1];
        int overridesIndex = System.Array.IndexOf(arguments, "-overrides"); if (overridesIndex >= 0 && overridesIndex + 1 < arguments.Length) ApplyOverrides(arguments[overridesIndex + 1]);
        int pedalIndex = System.Array.IndexOf(arguments, "-pedal"); if (pedalIndex >= 0 && pedalIndex + 1 < arguments.Length) pedal = float.Parse(arguments[pedalIndex + 1], System.Globalization.CultureInfo.InvariantCulture);
    }
    // @description - Applies parameter overrides from a text file, one "carParams.field=value" or "suspensionTables.field=v1;v2;..." per line (lines starting with # are comments). Fields are found by reflection on the field name, so the names must match CarParams and SuspensionTables exactly. Supports float, float array, bool and enum fields.
    // @param {string} path - path of the overrides file
    // @return {Void} - nothing
    void ApplyOverrides(string path)
    {
        var culture = System.Globalization.CultureInfo.InvariantCulture;
        foreach (var rawLine in System.IO.File.ReadAllLines(path))
        {
            var line = rawLine.Trim(); if (line.Length == 0 || line.StartsWith("#")) continue;
            int equalsIndex = line.IndexOf('='); string lhs = line.Substring(0, equalsIndex).Trim(), valueText = line.Substring(equalsIndex + 1).Trim();
            object target = lhs.StartsWith("suspensionTables.") ? (object)car.carParams.suspensionTables : car.carParams; string fieldName = lhs.Substring(lhs.IndexOf('.') + 1);
            var field = target.GetType().GetField(fieldName); if (field == null) { Debug.LogError("override: no field " + lhs); continue; }
            object parsedValue;
            if (field.FieldType == typeof(float)) parsedValue = float.Parse(valueText, culture);
            else if (field.FieldType == typeof(float[])) parsedValue = System.Array.ConvertAll(valueText.Split(';'), element => float.Parse(element, culture));
            else if (field.FieldType == typeof(bool)) parsedValue = valueText == "1" || valueText.ToLower() == "true";
            else if (field.FieldType.IsEnum) parsedValue = System.Enum.Parse(field.FieldType, valueText);
            else { Debug.LogError("override: unsupported type " + lhs); continue; }
            field.SetValue(target, parsedValue); Debug.Log("override " + lhs + " = " + valueText);
        }
    }
    // @description - Test driver gear logic: up-shift above 6000 rpm and down-shift below 2200 rpm (at most one shift per second, never while a shift is in progress). The bump-start scenario and a held power-step test gear are left alone.
    // @return {Void} - nothing
    void AutoGear()
    {
        if (scenario == "bumpstart") return;   // gears managed by the scenario
        if (car.drivetrain.gear < 1 || car.drivetrain.shiftTimer > 0f || elapsedTime - lastShift < 1f) return;
        if (scenario == "powerstep" && (phase >= 1 || (psGear > 0 && body && body.velocity.magnitude > 0.8f * Mathf.Sqrt(psAy * 9.81f * psR)))) { if (phase == 0 && psGear > 0 && car.drivetrain.gear != psGear) car.drivetrain.SelectGear(psGear); return; }   // gear held (test gear selected near target speed)
        if (car.engine.rpm > 6000f && car.drivetrain.gear < 5) { car.drivetrain.Shift(1); lastShift = elapsedTime; }
        else if (car.engine.rpm < 2200f && car.drivetrain.gear > 1) { car.drivetrain.Shift(-1); lastShift = elapsedTime; }
    }
    // @description - PI speed controller for the scripted driver: command = 0.4 x error + 0.15 x integral of error (integral limited to +/-5 m), positive command goes to the throttle, negative to the brake at half scale.
    // @param {float} vTarget - target speed in metres per second
    // @param {float} speed - current speed in metres per second
    // @return {float} - controller command (dimensionless, positive = throttle)
    float SpeedCtl(float vTarget, float speed)   // PI throttle/brake controller, m/s
    {
        float error = vTarget - speed; iErr = Mathf.Clamp(iErr + error * Time.fixedDeltaTime, -5f, 5f);
        float command = 0.4f * error + 0.15f * iErr; car.throttle = Mathf.Clamp01(command); car.brake = Mathf.Clamp01(-command * 0.5f); return command;
    }
    // @description - Pure-pursuit steering for a clockwise circle of the given radius that touches the origin: aims at a point ahead on the circle (look-ahead of 6 m or 0.6 s of travel) and returns the bicycle-model road wheel angle atan(2 L sin(alpha) / look-ahead distance), where L is the wheelbase and alpha the angle to the target in the car frame.
    // @param {float} radius - circle radius in metres (centre at x = radius, z = 0)
    // @return {float} - road wheel steer angle in degrees (positive = right)
    float CircleSteer(float radius)
    {
        Vector3 center = new Vector3(radius, 0f, 0f), position = body.position; position.y = 0f;
        Vector3 rel = position - center; float angle = Mathf.Atan2(rel.z, rel.x);
        float lookAheadAngle = Mathf.Max(6f, body.velocity.magnitude * 0.6f) / radius;
        Vector3 target = center + new Vector3(Mathf.Cos(angle - lookAheadAngle), 0f, Mathf.Sin(angle - lookAheadAngle)) * radius;   // clockwise = right turn
        Vector3 localTarget = car.transform.InverseTransformPoint(new Vector3(target.x, body.position.y, target.z));
        float alpha = Mathf.Atan2(localTarget.x, localTarget.z); float lookDistance = localTarget.magnitude;
        return Mathf.Atan(2f * car.carParams.wheelbase * Mathf.Sin(alpha) / lookDistance) * Mathf.Rad2Deg;
    }
    // @description - Main scenario loop at the physics rate: waits for the track scene, derives filtered longitudinal and lateral acceleration (30 ms low-pass, in g), applies the settle phase, runs the scenario, auto-shifts, logs the CSV and TQ lines every 10 ms and quits when the scenario ends.
    // @return {Void} - nothing
    void FixedUpdate()
    {
        if (!car || car.engine == null) return;
        if (!body) { body = car.GetComponent<Rigidbody>(); vPrev = body.velocity; }
        float deltaTime = Time.fixedDeltaTime; car.externalInput = true;
        if (scenario.StartsWith("laguna") && (TrackManager.Instance == null || !TrackManager.Instance.Ready || TrackManager.Instance.info == null)) { car.throttle = 0f; car.brake = 1f; return; }   // wait for the track scene
        elapsedTime += deltaTime; phaseT += deltaTime;
        car.clutch = 0f; car.handbrake = false;
        Vector3 velocity = body.velocity; Vector3 acceleration = (velocity - vPrev) / deltaTime; vPrev = velocity;
        float smoothing = 1f - Mathf.Exp(-deltaTime / 0.03f);
        axF += smoothing * (Vector3.Dot(acceleration, car.transform.forward) / 9.81f - axF);
        ayF += smoothing * (Vector3.Dot(acceleration, car.transform.right) / 9.81f - ayF);
        float speed = velocity.magnitude;
        if (elapsedTime > 3f && car.drivetrain.gear == 0 && scenario != "revsweep" && !(scenario == "stallrestart" && elapsedTime > 6f)) car.drivetrain.Shift(1);
        if (wav != null) { synth.Sample(deltaTime); int newSamples = Mathf.RoundToInt((elapsedTime + deltaTime) * WavRate) - wavN; if (newSamples > 0) { if (wavN + newSamples > wav.Length) System.Array.Resize(ref wav, wav.Length * 2); var chunk = new float[newSamples]; synth.Render(chunk, newSamples); System.Array.Copy(chunk, 0, wav, wavN, newSamples); wavN += newSamples; } }
        bool settle = elapsedTime < 3f;
        if (settle) { car.throttle = 0f; car.brake = scenario.StartsWith("laguna") ? 1f : 0f; car.steer = 0f; car.autoClutch = true; }   // the test driver launches with the (auto) clutch: with real static tire grip a clutch dump at idle stalls the engine, as in a real car (scenarios that test the clutch held engaged set autoClutch = false themselves)
        else Run(speed);
        if (car.drivetrain.shiftTimer > 0f) car.throttle = 0f;
        AutoGear();
        if (elapsedTime >= nextLog)
        {
            nextLog += 0.01f;
            float forwardVelocity = Vector3.Dot(velocity, car.transform.forward), lateralVelocity = Vector3.Dot(velocity, car.transform.right);
            float beta = speed > 1f ? Mathf.Atan2(lateralVelocity, forwardVelocity) * Mathf.Rad2Deg : 0f;
            float pitch = Mathf.DeltaAngle(0f, car.transform.eulerAngles.x), roll = Mathf.DeltaAngle(0f, car.transform.eulerAngles.z);
            Debug.Log(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "CSV,{0},{1:0.00},{2:0.00},{3:0.00},{4:0.00},{5:0.000},{6:0.000},{7:0.00},{8:0.00},{9:0.00},{10:0.00},{11:0.00},{12},{13:0},{14:0},{15:0},{16:0},{17:0},{18:0.0},{19:0.0},{20:0.0},{21:0.0},{22:0.00},{23:0.00},{24}",
                scenario, elapsedTime, body.position.x, body.position.z, speed * 3.6f, axF, ayF, body.angularVelocity.y * Mathf.Rad2Deg, beta, car.steer, car.throttle, car.brake,
                car.drivetrain.gear, car.engine.rpm, car.frontLeftWheel.verticalLoad, car.frontRightWheel.verticalLoad, car.rearLeftWheel.verticalLoad, car.rearRightWheel.verticalLoad, car.frontLeftWheel.angularVelocity, car.frontRightWheel.angularVelocity, car.rearLeftWheel.angularVelocity, car.rearRightWheel.angularVelocity, pitch, roll, phase));
            Debug.Log(string.Format(System.Globalization.CultureInfo.InvariantCulture, "TQ,{0:0.00},{1:0.000},{2:0.000},{3:0.000},{4:0.00},{5:0.0},{6:0.0},{7:0},{8:0},{9:0},{10:0}",
                elapsedTime, ayF, car.frontLeftWheel.relaxedSlipAngle * Mathf.Rad2Deg, car.frontRightWheel.relaxedSlipAngle * Mathf.Rad2Deg, car.columnTorque, car.leftRoadTorque, car.rightRoadTorque, car.frontLeftWheel.force.y, car.frontRightWheel.force.y, car.frontLeftWheel.verticalLoad, car.frontRightWheel.verticalLoad));
        }
        float tEnd = scenario == "skidpad" ? 160f : scenario == "accel" ? 60f : scenario == "topspeed" ? 150f : scenario == "laguna" ? lagEnd : scenario == "lagunagrass" ? 400f : scenario == "lagunakerb" ? 90f : scenario == "bumpstart" ? 60f : scenario == "lagunaoff" ? 200f : scenario == "powerstep" ? 220f : scenario == "lagunatherm" ? 900f : scenario == "thermslide" ? 200f : scenario.StartsWith("gp") ? 40f : 70f;
        if (scenario == "lagunakerb" && (elapsedTime > tEnd || phase == 99) && !quitting) KerbReport();
        if (scenario == "laguna" && elapsedTime > tEnd && !quitting) LagunaReport();
        if (scenario == "laguna" && phase == 99 && !quitting) LagunaReport();
        if ((elapsedTime > tEnd || phase == 99) && !quitting) { quitting = true; WriteWav(); Debug.Log("SMOKE done " + scenario); Application.Quit(0); }
    }
    // -audiowav <path>: render the engine synth offline (same code as the live AudioSource filter) at WavRate, synchronous with physics
    const int WavRate = 48000; float[] wav; int wavN; EngineSynth synth; string wavPath;
    // @description - Prepares the optional offline engine audio render requested with -audiowav <path>.
    // @return {Void} - nothing
    void Start()
    {
        var arguments = System.Environment.GetCommandLineArgs(); int wavFlagIndex = System.Array.IndexOf(arguments, "-audiowav");
        if (wavFlagIndex >= 0 && wavFlagIndex + 1 < arguments.Length && car) { wavPath = arguments[wavFlagIndex + 1]; wav = new float[WavRate * 20]; synth = new GameObject("OfflineSynth").AddComponent<EngineSynth>(); synth.enabled = false; synth.Init(car, WavRate); }
    }
    // @description - Writes the offline engine audio render to a 16-bit mono WAV file at 48 kHz and logs the sample count.
    // @return {Void} - nothing
    void WriteWav()
    {
        if (wav == null) return;
        using (var writer = new System.IO.BinaryWriter(System.IO.File.Create(wavPath)))
        {
            int bytes = wavN * 2; writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF")); writer.Write(36 + bytes); writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt ")); writer.Write(16); writer.Write((short)1); writer.Write((short)1);
            writer.Write(WavRate); writer.Write(WavRate * 2); writer.Write((short)2); writer.Write((short)16); writer.Write(System.Text.Encoding.ASCII.GetBytes("data")); writer.Write(bytes);
            for (int i = 0; i < wavN; i++) writer.Write((short)Mathf.Clamp(wav[i] * 32767f, -32768f, 32767f));
        }
        Debug.Log("WAV written " + wavPath + " samples " + wavN);
    }
    // @description - Moves the scenario to a new phase and restarts the phase timer.
    // @param {int} newPhase - phase number (99 ends the scenario)
    // @return {Void} - nothing
    void Next(int newPhase) { phase = newPhase; phaseT = 0f; }
    // ---- off-track launch diagnostics (lagunaoff): wheel ground-height jumps, chassis contacts, body vertical speed ----
    readonly float[] diagLastY = new float[4]; float diagNext;
    // Diagnostic component added to the car body by the off-track tests: logs the car's collisions with the scenery.
    public class ContactDiag : MonoBehaviour
    {
        // @description - Logs the first contact of the car body with a collider.
        // @param {Collision} collision - collision data from Unity
        // @return {Void} - nothing
        void OnCollisionEnter(Collision collision) => Log("enter", collision);
        // @description - Logs a continuing contact, but only when the contact impulse exceeds 200 newton-seconds so light touching is not reported.
        // @param {Collision} collision - collision data from Unity
        // @return {Void} - nothing
        void OnCollisionStay(Collision collision) { if (collision.impulse.magnitude > 200f) Log("stay", collision); }
        // @description - Logs one contact (collision) of the car body with its impulse, relative velocity, normal and body state, for the off-track launch diagnostics.
        // @param {string} kind - event kind, "enter" or "stay"
        // @param {Collision} collision - collision data from Unity
        // @return {Void} - nothing
        void Log(string kind, Collision collision) { var body = GetComponent<Rigidbody>(); var normal = collision.contactCount > 0 ? collision.GetContact(0).normal : Vector3.zero; var point = collision.contactCount > 0 ? collision.GetContact(0).point : Vector3.zero;
            Debug.Log($"DIAGC {kind} {collision.collider.name} t {Time.time:0.000} impulse {collision.impulse.ToString("F0")} relVel {collision.relativeVelocity.ToString("F1")} normal {normal.ToString("F2")} pt {point.ToString("F2")} contacts {collision.contactCount} body v {body.velocity.ToString("F2")} y {body.position.y:0.00}"); } }
    // @description - Off-track launch diagnostics: logs sudden ground-height jumps under a wheel (more than 0.25 m between steps) and, while the body rises faster than 1.5 m/s, the per-wheel surface, tire force, suspension force and travel every 20 ms.
    // @return {Void} - nothing
    void LaunchDiag()
    {
        if (!car.GetComponent<ContactDiag>()) { car.gameObject.AddComponent<ContactDiag>(); Debug.Log($"DIAG maxDepenetration default {Physics.defaultMaxDepenetrationVelocity} rb {body.maxDepenetrationVelocity} contactOffset {Physics.defaultContactOffset} cd {body.collisionDetectionMode}"); }
        var wheels = new[] { car.frontLeftWheel, car.frontRightWheel, car.rearLeftWheel, car.rearRightWheel }; string[] wheelNames = { "FL", "FR", "RL", "RR" };
        for (int i = 0; i < 4; i++) { var wheel = wheels[i]; float groundHeight = wheel.grounded ? wheel.Hit.point.y : float.NaN; if (!float.IsNaN(groundHeight) && !float.IsNaN(diagLastY[i]) && Mathf.Abs(groundHeight - diagLastY[i]) > 0.25f) Debug.Log($"DIAGW {wheelNames[i]} ground jump {diagLastY[i]:0.00} -> {groundHeight:0.00} on {wheel.Hit.collider.name} (normal {wheel.Hit.normal.ToString("F2")}) Ftire {wheel.tireForce:0} Fsusp {wheel.suspensionForce:0} travel {wheel.travel:0.000} body vy {body.velocity.y:0.00} t {elapsedTime:0.000}"); diagLastY[i] = groundHeight; }
        if (body.velocity.y > 1.5f && elapsedTime >= diagNext) { diagNext = elapsedTime + 0.02f; string text = ""; for (int i = 0; i < 4; i++) { var wheel = wheels[i]; text += $" {wheelNames[i]}:{(wheel.grounded ? wheel.Hit.collider.name : "air")} Ft {wheel.tireForce:0} Fs {wheel.suspensionForce:0} tr {wheel.travel:0.000}"; } Debug.Log($"DIAGV t {elapsedTime:0.000} pos {body.position.ToString("F2")} v {body.velocity.ToString("F2")} angv {body.angularVelocity.ToString("F2")}{text}"); }
    }
    // ---- bump start: 2nd gear at 40 km/h, clutch engaged, brakes locked -> engine stalls; release brake, clutch stays engaged -> must re-fire.
    // -ignoff: ignition switched off at the stall -> must NOT re-fire. Clutch pedal/auto clutch never used.
    float bsStallV, bsMaxRpm; bool bsIgnOff;
    // @description - Bump-start scenario. Phase 0: drive up to 40 km/h in 2nd gear. Phase 1: lock the brakes with the clutch engaged until the engine stalls (optionally switch the ignition off, -ignoff). Phase 2: release the brake and check that the engine restarts from the rolling wheels (or stays off with the ignition off). Logs a PASS or FAIL result line.
    // @param {float} speed - current speed in metres per second
    // @return {Void} - nothing
    void BumpStart(float speed)
    {
        car.autoClutch = phase == 0; car.clutch = 0f;   // run-up: launch with the auto clutch; from the brake lock on the clutch stays engaged (no auto clutch)
        if (phase == 0)
        {
            bsIgnOff = System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-ignoff") >= 0;
            if (car.drivetrain.gear == 1 && speed > 6f) car.drivetrain.SelectGear(2);
            SpeedCtl(45f / 3.6f, speed);
            if (car.drivetrain.gear == 2 && car.drivetrain.shiftTimer <= 0f && speed >= 40f / 3.6f && elapsedTime > 3f) { Debug.Log($"BUMP firing speed (model) {car.engine.FireRpm:0} rpm: net-positive closed-pedal torque from {car.engine.NetPositiveRpm:0} rpm, floor minFiringRpm {car.carParams.minFiringRpm:0}, stallRpm {car.carParams.stallRpm:0}"); Next(1); }
        }
        else if (phase == 1)   // lock the brakes with the clutch engaged
        {
            car.throttle = 0f; car.brake = 1f;
            if (!car.engine.combusting && car.engine.rpm < 20f && phaseT > 0.2f) { bsStallV = speed; if (bsIgnOff) car.engine.ignition = false; Debug.Log($"BUMP stalled at {speed * 3.6f:0.0} km/h in gear {car.drivetrain.gear} (rpm {car.engine.rpm:0}), ignition {(car.engine.ignition ? "ON" : "OFF")}"); Next(2); }
            if (phaseT > 5f) { Debug.Log("BUMPSTART RESULT FAIL engine never stalled"); Next(99); }
        }
        else if (phase == 2)   // release the brake, clutch engaged, no throttle
        {
            car.throttle = 0f; car.brake = 0f; bsMaxRpm = Mathf.Max(bsMaxRpm, car.engine.rpm);
            if (phaseT > 4f)
            {
                bool running = car.engine.combusting && car.engine.rpm > car.carParams.stallRpm; bool pass = bsIgnOff ? !car.engine.combusting : running;
                Debug.Log($"BUMPSTART RESULT {(pass ? "PASS" : "FAIL")} ignition {(bsIgnOff ? "OFF" : "ON")}: stalled at {bsStallV * 3.6f:0.0} km/h, max crank {bsMaxRpm:0} rpm after release, now {car.engine.State} at {car.engine.rpm:0} rpm, {speed * 3.6f:0.0} km/h (expect {(bsIgnOff ? "no restart" : "restart")})");
                Next(99);
            }
        }
    }
    // ---- power-on / lift-off in a steady turn (diagnostic). Phase 0: hold a psR circle at psAy (closed-loop steer + speed) until steady.
    // Phase 1: steering FROZEN, throttle -> psThr (or kept at the steady value as a control run), gear held, 3 s. Per-wheel logging every 10 ms.
    float psSteer, psThrHold, psThrF; float[] psRef; readonly System.Collections.Generic.List<string> psOut = new System.Collections.Generic.List<string>(); float psNext;
    // @description - Snapshot of the car state during a power-step test: speed (km/h), yaw rate (deg/s), curvature (1/m), body slip angle (deg), lateral and longitudinal acceleration (g), mean front and rear slip angles (deg), friction usage of the four wheels (|F| / peak force) and axle loads (N).
    // @param {float} speed - current speed in metres per second
    // @return {float[]} - array of 14 values in the order listed above
    float[] PsState(float speed)
    {
        float yawRateDeg = body.angularVelocity.y * Mathf.Rad2Deg; var velocity = body.velocity; float forwardVelocity = Vector3.Dot(velocity, car.transform.forward), lateralVelocity = Vector3.Dot(velocity, car.transform.right);
        float beta = Mathf.Atan2(lateralVelocity, Mathf.Max(0.1f, forwardVelocity)) * Mathf.Rad2Deg; float frontSlipAngleDeg = 0.5f * (car.frontLeftWheel.relaxedSlipAngle + car.frontRightWheel.relaxedSlipAngle) * Mathf.Rad2Deg, rearSlipAngleDeg = 0.5f * (car.rearLeftWheel.relaxedSlipAngle + car.rearRightWheel.relaxedSlipAngle) * Mathf.Rad2Deg;
        return new[] { speed * 3.6f, yawRateDeg, yawRateDeg * Mathf.Deg2Rad / Mathf.Max(0.1f, speed), beta, ayF, axF, frontSlipAngleDeg, rearSlipAngleDeg, FrictionUsage(car.frontLeftWheel), FrictionUsage(car.frontRightWheel), FrictionUsage(car.rearLeftWheel), FrictionUsage(car.rearRightWheel), car.frontLeftWheel.verticalLoad + car.frontRightWheel.verticalLoad, car.rearLeftWheel.verticalLoad + car.rearRightWheel.verticalLoad };
    }
    // @description - Friction usage of one wheel: tire force magnitude divided by the available peak force mu_y(Fz) x Fz, with mu_y falling linearly with load change (lateral peak friction coefficient plus slope times normalised load change) and the peak force floored at 1 N. 1.0 means the tire is at its limit.
    // @param {WheelCorner} wheel - wheel corner to evaluate
    // @return {float} - friction usage (dimensionless, 1 = at the limit)
    float FrictionUsage(WheelCorner wheel) { var carParams = car.carParams; float normalizedLoadChange = (wheel.verticalLoad - carParams.nominalVerticalLoad) / carParams.nominalVerticalLoad; float peakForce = Mathf.Max(1f, (carParams.lateralPeakFrictionCoefficient + carParams.lateralPeakFrictionLoadSlope * normalizedLoadChange) * wheel.verticalLoad); return wheel.force.magnitude / peakForce; }   // friction usage ~ |F| / (mu_y(Fz) Fz)
    // @description - Formats a PsState snapshot as one PSTEP log line (speed, yaw rate, curvature and radius, body slip, accelerations, slip angles, friction usage and axle loads).
    // @param {string} tag - label put at the start of the line
    // @param {float[]} state - snapshot from PsState
    // @return {string} - formatted log line
    string PsFmt(string tag, float[] state) => string.Format(System.Globalization.CultureInfo.InvariantCulture, "PSTEP {0}: v {1:0.0} km/h, yaw {2:0.00} deg/s, curvature {3:0.00000} 1/m (R {4:0.0} m), beta {5:+0.00;-0.00} deg, ay {6:0.000} g, ax {7:+0.000;-0.000} g, alphaF {8:0.00} alphaR {9:0.00} deg (aF-aR {10:+0.00;-0.00}), usage FL {11:0.00} FR {12:0.00} RL {13:0.00} RR {14:0.00}, FzF {15:0} FzR {16:0} N",
        tag, state[0], state[1], state[2], 1f / Mathf.Max(1e-5f, Mathf.Abs(state[2])), state[3], state[4], state[5], state[6], state[7], state[6] - state[7], state[8], state[9], state[10], state[11], state[12], state[13]);
    // @description - Body slip angle (velocity direction relative to the car's heading), with the forward speed floored at 0.1 m/s.
    // @return {float} - body slip angle in radians (positive = moving to the right of the heading)
    float PsBeta() { var velocity = body.velocity; return Mathf.Atan2(Vector3.Dot(velocity, car.transform.right), Mathf.Max(0.1f, Vector3.Dot(velocity, car.transform.forward))); }
    // @description - Power-on / lift-off diagnostic in a steady turn. Phase 0: hold a circle of radius psR at the target lateral acceleration until the state is steady (error under 1 m, speed within 0.15 m/s, no acceleration). Phase 1: freeze the steering, change the throttle for 3 s with the gear held, and log per-wheel data every 10 ms plus the change in curvature, yaw rate and slip angle.
    // @param {float} speed - current speed in metres per second
    // @return {Void} - nothing
    void PowerStep(float speed)
    {
        psThrF += (1f - Mathf.Exp(-Time.fixedDeltaTime / 0.5f)) * (car.throttle - psThrF);
        if (phase == 0)
        {
            car.steer = Mathf.Clamp(CircleSteerR(psR), -car.carParams.MeanLockDeg, car.carParams.MeanLockDeg);
            float targetSpeed = Mathf.Sqrt(psAy * 9.81f * psR); SpeedCtl(Mathf.Min(targetSpeed, 30f / 3.6f + 1f * Mathf.Max(0f, elapsedTime - 3f)), speed);
            var position = body.position; float radiusError = Mathf.Abs(Mathf.Sqrt((position.x - psR) * (position.x - psR) + position.z * position.z) - psR);
            // steady test uses dv/dt = body-x accel + ay*sin(beta) (logged: ax +0.012, ay +0.496, beta -1.32 deg at constant v) (at 100 m the ay*sin(beta) term alone is 0.011 g)
            if (elapsedTime > 45f && radiusError < 1.0f && Mathf.Abs(speed - targetSpeed) < 0.15f && Mathf.Abs(axF + ayF * Mathf.Sin(PsBeta())) < 0.01f) { psSteer = car.steer; psThrHold = psThrF; psRef = PsState(speed); psOut.Add(PsFmt($"steady  R {psR:0} m gear {car.drivetrain.gear} steer {psSteer:0.000} deg thr {psThrHold:0.000}", psRef)); Next(1); psNext = 0f; }
            if (elapsedTime > 160f) { Debug.Log("PSTEP no steady state"); phase = 99; }
        }
        else
        {
            car.steer = psSteer; car.brake = 0f; car.throttle = psThr < 0f ? psThrHold : psThr;
            if (phaseT >= psNext - 1e-4f) { var state = PsState(speed); psOut.Add(PsFmt($"t+{phaseT:0.00}s thr {car.throttle:0.00}", state)); psNext += phaseT < 1f ? 0.25f : 0.5f; }
            Debug.Log(string.Format(System.Globalization.CultureInfo.InvariantCulture, "PW,{0:0.000},{1}", phaseT, string.Join(",", System.Linq.Enumerable.Select(new[] { car.frontLeftWheel, car.frontRightWheel, car.rearLeftWheel, car.rearRightWheel },
                wheel => $"{wheel.relaxedSlipAngle * Mathf.Rad2Deg:0.000},{wheel.slipRatio:0.0000},{wheel.verticalLoad:0},{wheel.force.x:0},{wheel.force.y:0},{FrictionUsage(wheel):0.000},{wheel.steerDeg:0.000},{wheel.camberDeg:0.00}"))));
            if (phaseT > 3.001f) { foreach (var line in psOut) Debug.Log(line); var finalState = PsState(speed); Debug.Log(string.Format(System.Globalization.CultureInfo.InvariantCulture, "PSTEP RESULT thr {0:0.00} (steady {1:0.000}): curvature change {2:+0.0;-0.0}% at 1 s, {3:+0.0;-0.0}% at 3 s; yaw rate change {4:+0.00;-0.00} deg/s at 3 s; beta change {5:+0.00;-0.00} deg",
                car.throttle, psThrHold, 100f * (curvatureAtOneSecond - psRef[2]) / psRef[2], 100f * (finalState[2] - psRef[2]) / psRef[2], finalState[1] - psRef[1], finalState[3] - psRef[3])); phase = 99; }
            if (Mathf.Abs(phaseT - 1f) < 0.5f * Time.fixedDeltaTime) curvatureAtOneSecond = PsState(speed)[2];
        }
    }
    float curvatureAtOneSecond;
    // @description - Alias of CircleSteer used by the power-step scenario.
    // @param {float} radius - circle radius in metres
    // @return {float} - road wheel steer angle in degrees (positive = right)
    float CircleSteerR(float radius) => CircleSteer(radius);
    // ---- track drive (-track laguna -scenario laguna): spawn on S/F, follow the centreline with pure pursuit at a curvature-limited speed,
    // and check the car stays on the surface (all wheels hit a collider, body above the surface, within the track edges + shoulder).
    float lagEnd = 63f; int lagIdx, lagUngrounded, lagSamples, lagMaxUngroundedRun, lagRun, lagLaps; float lagMaxOff = 0f, lagMinClear = 99f, lagDist, lagMaxV, lagWorstY = 99f; Vector3 lagPrev; bool lagFail; string lagWhy = "";
    float thNext = 4f; Vector3 thHead;
    // @description - Logs the tire thermal state of all four wheels in user units (mph, degrees Fahrenheit, psi): surface temperatures inner, middle, outer, bulk and gas temperature, and pressure.
    // @param {string} tag - label put in the log line
    // @return {Void} - nothing
    void ThermLog(string tag)
    {   // user-facing units: mph, F, psi
        string line = $"THERM {scenario} {tag} v {body.velocity.magnitude * DebugHUD.MphPerMs:0} mph";
        foreach (var wheel in new[] { car.frontLeftWheel, car.frontRightWheel, car.rearLeftWheel, car.rearRightWheel })
        {
            var thermal = wheel.thermal; if (thermal == null) continue;
            line += $" | {wheel.name} {TireThermal.CelsiusToFahrenheit(thermal.surfaceTemperatures[0]):0}/{TireThermal.CelsiusToFahrenheit(thermal.surfaceTemperatures[1]):0}/{TireThermal.CelsiusToFahrenheit(thermal.surfaceTemperatures[2]):0}F bulk {TireThermal.CelsiusToFahrenheit(thermal.bulkTemperature):0} carc {TireThermal.CelsiusToFahrenheit(thermal.carcassTemperature):0} gas {TireThermal.CelsiusToFahrenheit(thermal.gasTemperature):0} {thermal.gaugePressure / TireThermal.PaPerPsi:0.0}psi grip {wheel.thermalScale.lateralFriction * 100f:0}% res {thermal.Residual / System.Math.Max(thermal.energyIn, 1):E1}";
        }
        Debug.Log(line);
    }
    // @description - Track-following driver for the Laguna Seca scenarios: finds the nearest centreline point, steers with pure pursuit towards a point ahead (aiming at the inside kerb in kerb mode), limits speed by the tightest corner in the next 70 m (v = sqrt(a R), then braking at 4 m/s2 to that speed), and records metrics (all wheels grounded, distance beyond the edge, clearance, fall below the road) that decide PASS or FAIL.
    // @param {float} speed - current speed in metres per second
    // @return {Void} - nothing
    void LagunaDrive(float speed)
    {
        car.autoClutch = true; var trackInfo = TrackManager.Instance.info; var centreline = trackInfo.Centreline; int pointCount = centreline.Count; Vector3 position = body.position;
        int bestIndex = lagIdx; float bestDistanceSquared = float.MaxValue;
        for (int k = -5; k < 40; k++) { int index = (lagIdx + k + pointCount) % pointCount; float distanceSquared = (new Vector2(centreline[index].x - position.x, centreline[index].z - position.z)).sqrMagnitude; if (distanceSquared < bestDistanceSquared) { bestDistanceSquared = distanceSquared; bestIndex = index; } }
        if (bestIndex < lagIdx - pointCount / 2) { lagLaps++; if (scenario == "lagunatherm") { ThermLog($"LAP {lagLaps} {elapsedTime - 4f:0.0}s"); if (lagLaps >= 3) phase = 99; } } lagIdx = bestIndex;
        float look = Mathf.Max(8f, speed * 0.9f); int targetIndex = (lagIdx + Mathf.CeilToInt(look / 2f)) % pointCount; var target = centreline[targetIndex];
        if (kerbMode)   // kerb test: aim the car's centre (edge - 0.3 m) on the inside of each corner, where Blender Bot's procedural kerbs are
        {
            Vector3 headingBefore = centreline[(targetIndex + 1) % pointCount] - centreline[(targetIndex + pointCount - 1) % pointCount], headingAfter = centreline[(targetIndex + 6) % pointCount] - centreline[(targetIndex + 4) % pointCount]; headingBefore.y = headingAfter.y = 0f; float turn = Vector3.SignedAngle(headingBefore, headingAfter, Vector3.up);
            if (Mathf.Abs(turn) > 2f) { var forwardDirection = headingBefore.normalized; var right = new Vector3(forwardDirection.z, 0f, -forwardDirection.x); float cornerSide = Mathf.Sign(turn); float insideHalfWidth = cornerSide > 0 ? trackInfo.HalfWidths[targetIndex].y : trackInfo.HalfWidths[targetIndex].x; target += right * cornerSide * (insideHalfWidth - 0.3f); }
        }
        Vector3 localTarget = car.transform.InverseTransformPoint(new Vector3(target.x, position.y, target.z)); float alpha = Mathf.Atan2(localTarget.x, localTarget.z);
        car.steer = Mathf.Clamp(Mathf.Atan(2f * car.carParams.wheelbase * Mathf.Sin(alpha) / Mathf.Max(1f, localTarget.magnitude)) * Mathf.Rad2Deg, -car.carParams.MeanLockDeg, car.carParams.MeanLockDeg);
        bool isHotRun = scenario == "lagunatherm"; float targetSpeed = isHotRun ? 30f : 24f, lateralAccelerationG = isHotRun ? 0.45f : 0.55f;   // m/s cap; corner speed from the tightest radius in the next ~70 m at 0.55 g (lagunatherm: 0.45 g, 30 m/s; 0.75 and 0.6 g left the road on cold tyres)
        for (int k = 2; k < 35; k++) { var previousPoint = centreline[(lagIdx + k - 2) % pointCount]; var currentPoint = centreline[(lagIdx + k) % pointCount]; var nextPoint = centreline[(lagIdx + k + 2) % pointCount];
            Vector3 segmentBefore = currentPoint - previousPoint, segmentAfter = nextPoint - currentPoint; segmentBefore.y = segmentAfter.y = 0f; float turnAngle = Vector3.Angle(segmentBefore, segmentAfter) * Mathf.Deg2Rad; float cornerRadius = turnAngle > 1e-4f ? segmentBefore.magnitude / turnAngle : 1e4f;
            float cornerSpeed = Mathf.Sqrt(lateralAccelerationG * 9.81f * cornerRadius), distanceAhead = 2f * k; targetSpeed = Mathf.Min(targetSpeed, Mathf.Sqrt(cornerSpeed * cornerSpeed + 2f * 4f * distanceAhead)); }
        SpeedCtl(targetSpeed, speed);
        if (elapsedTime < 4f) return; lagSamples++;
        bool allGrounded = car.frontLeftWheel.grounded && car.frontRightWheel.grounded && car.rearLeftWheel.grounded && car.rearRightWheel.grounded;
        if (!allGrounded) { lagUngrounded++; lagRun++; lagMaxUngroundedRun = Mathf.Max(lagMaxUngroundedRun, lagRun); } else lagRun = 0;
        trackInfo.Nearest(position, out float lateralOffset); var halfWidths = trackInfo.HalfWidths[lagIdx]; float edge = lateralOffset > 0f ? halfWidths.y : halfWidths.x; lagMaxOff = Mathf.Max(lagMaxOff, Mathf.Abs(lateralOffset) - edge);
        if (Physics.Raycast(position + Vector3.up * 3f, Vector3.down, out var hit, 20f)) { float clearance = position.y - hit.point.y; lagMinClear = Mathf.Min(lagMinClear, clearance); } else { lagFail = true; lagWhy += "no surface under car; "; }
        lagWorstY = Mathf.Min(lagWorstY, position.y - centreline[lagIdx].y);
        if (lagPrev != Vector3.zero) lagDist += Vector3.Distance(lagPrev, position); lagPrev = position; lagMaxV = Mathf.Max(lagMaxV, speed);
        if (lagRun * Time.fixedDeltaTime > 0.25f && !lagFail) { lagFail = true; lagWhy += $"wheels off the surface >0.25 s at t={elapsedTime:0.0}; "; }
        if (Mathf.Abs(lateralOffset) - edge > 3.5f && !lagFail) { lagFail = true; lagWhy += $"left the road (+shoulder) at s~{lagIdx * 2} m; "; }
        if (position.y - centreline[lagIdx].y < -1.5f && !lagFail) { lagFail = true; lagWhy += $"fell below the road at s~{lagIdx * 2} m; "; }
        if (kerbMode) { lagFail = false; lagWhy = ""; return; }   // kerb test: cutting kerbs is intended; its own checks are in KerbReport
        if (lagFail) { if (scenario == "lagunatherm") { ThermLog("FAIL " + lagWhy); } Next(99); }
    }
    // ---- kerb test: per-wheel surface under the contact (SurfaceTag of the collider hit) and its load / travel; kerb vs asphalt
    bool kerbMode; readonly float[] kN = new float[2], kFz = new float[2], kFz2 = new float[2], kTrMin = { 9f, 9f }, kTrMax = { -9f, -9f }, kdFz = new float[2]; readonly float[] lastFz = new float[4];
    int kerbHitObjects;
    // @description - Kerb test sampling: for each grounded wheel finds the surface tag under it and accumulates sample count, vertical load statistics, suspension travel range and load change rate, separately for kerb and asphalt.
    // @return {Void} - nothing
    void KerbSample()
    {
        if (elapsedTime < 4f) return; var wheels = new[] { car.frontLeftWheel, car.frontRightWheel, car.rearLeftWheel, car.rearRightWheel };
        for (int i = 0; i < 4; i++)
        {
            var wheel = wheels[i]; float loadRate = Mathf.Abs(wheel.verticalLoad - lastFz[i]) / Time.fixedDeltaTime; lastFz[i] = wheel.verticalLoad;
            if (!wheel.grounded || !Physics.Raycast(wheel.transform.position, -wheel.transform.up, out var hit, 1.5f)) continue;
            var surface = SurfaceTag.Of(hit.collider); int surfaceIndex = surface == SurfaceType.Kerb ? 0 : surface == SurfaceType.Asphalt ? 1 : -1; if (surfaceIndex < 0) continue;
            kN[surfaceIndex]++; kFz[surfaceIndex] += wheel.verticalLoad; kFz2[surfaceIndex] += wheel.verticalLoad * wheel.verticalLoad; kTrMin[surfaceIndex] = Mathf.Min(kTrMin[surfaceIndex], wheel.travel); kTrMax[surfaceIndex] = Mathf.Max(kTrMax[surfaceIndex], wheel.travel); kdFz[surfaceIndex] += loadRate;
        }
    }
    // @description - Prints the kerb versus asphalt statistics and the PASS or FAIL result (needs at least 200 kerb samples, a kerb load rate above 1.5 times the asphalt rate and no fall resets), then quits.
    // @return {Void} - nothing
    void KerbReport()
    {
        string Describe(int surfaceIndex) { float sampleCount = Mathf.Max(1f, kN[surfaceIndex]), meanLoad = kFz[surfaceIndex] / sampleCount, loadDeviation = Mathf.Sqrt(Mathf.Max(0f, kFz2[surfaceIndex] / sampleCount - meanLoad * meanLoad)); return $"{kN[surfaceIndex]:0} wheel-samples, Fz mean {meanLoad:0} N sd {loadDeviation:0} N, mean |dFz/dt| {kdFz[surfaceIndex] / sampleCount / 1000f:0.0} kN/s, travel {kTrMin[surfaceIndex] * 1000f:0}..{kTrMax[surfaceIndex] * 1000f:0} mm"; }
        float kerbSamples = Mathf.Max(1f, kN[0]), asphaltSamples = Mathf.Max(1f, kN[1]); float kerbRate = kdFz[0] / kerbSamples, asphaltRate = kdFz[1] / asphaltSamples;
        bool ok = kN[0] >= 200 && kerbRate > 1.5f * asphaltRate && TrackManager.Instance.FallResets == 0;
        Debug.Log("KERB on kerb: " + Describe(0)); Debug.Log("KERB on asphalt: " + Describe(1));
        Debug.Log($"LAGUNAKERB RESULT {(ok ? "PASS" : "FAIL")} kerb load-rate ratio {kerbRate / Mathf.Max(1e-3f, asphaltRate):0.0}x asphalt (need >1.5x, >=200 kerb samples), drove {lagDist:0} m, fall resets {TrackManager.Instance.FallResets}");
        quitting = true; Application.Quit(ok ? 0 : 3);
    }
    // ---- off-road rest test: teleport onto grass/runoff at several places (beside the track and far away), let the car settle 4 s on the brake,
    // check all wheels hit a surface, the car is at rest, and its height matches the DEM terrain under it (TerrainData height + ride height).
    int gIdx = -1; int gFails; float gT, gDiagNext; readonly System.Collections.Generic.List<string> gRows = new System.Collections.Generic.List<string>();
    // @description - Returns the test positions for the off-road rest test: points beside the track at several corners and three far-field points, each placed high above the ground so the car is dropped onto the surface.
    // @param {int} pointIndex - index of the test point
    // @param {string} name - receives a description of the point
    // @return {Vector3} - world position in metres (zero vector when k is past the last point)
    Vector3 GrassPoint(int pointIndex, out string name)
    {
        var ti = TrackManager.Instance.info; var c = ti.Centreline; int n = c.Count;
        Vector3 OffsetFromCentreline(float distanceAlong, float side, float extra, out string description, string label)
        {
            int index = Mathf.RoundToInt(distanceAlong / 2f) % n; var forward = c[(index + 1) % n] - c[index]; forward.y = 0f; forward.Normalize(); var right = new Vector3(forward.z, 0f, -forward.x);
            float edge = side > 0 ? ti.HalfWidths[index].y : ti.HalfWidths[index].x; description = $"{label} (s={distanceAlong:0} m, {(side > 0 ? "right" : "left")} {edge + extra:0} m from centreline)";
            var point = c[index] + right * side * (edge + extra); point.y = 200f; return point;
        }
        switch (pointIndex)
        {
            case 0: return OffsetFromCentreline(246f, 1f, 30f, out name, "outside T1");
            case 1: return OffsetFromCentreline(500f, 1f, 18f, out name, "outside Andretti hairpin");
            case 2: return OffsetFromCentreline(2500f, -1f, 10f, out name, "Corkscrew entry, left");
            case 3: return OffsetFromCentreline(2560f, 1f, 12f, out name, "Corkscrew, right");
            case 4: return OffsetFromCentreline(1100f, -1f, 25f, out name, "T4 inside");
            case 5: return OffsetFromCentreline(3290f, 1f, 15f, out name, "outside T11");
            case 6: name = "far field NE (900, -600)"; return new Vector3(900f, 200f, -600f);
            case 7: name = "far field W (-500, 500)"; return new Vector3(-500f, 200f, 500f);
            case 8: name = "far field S (300, -1000)"; return new Vector3(300f, 200f, -1000f);
            default: name = ""; return Vector3.zero;
        }
    }
    // ---- park: S/F spawn, neutral, engine idling. 0-13 s brake pedal + handbrake, 13-23 s handbrake only (rear locked).
    // Measured 3-10 s after each change: displacement (creep), max speed and max |accel| (oscillation). Checks the low-speed stick tire model.
    Vector3 pkPos0; float pkMaxV, pkMaxA, pkSlope; bool pkHave; Vector3 pkVPrev; readonly string[] pkRow = new string[2]; int pkFail;
    // @description - Parking test: brake plus handbrake from 0 to 13 s, then handbrake only to 23 s, measuring displacement, max speed and acceleration in the last 7 s of each part. Passes if the car moved under 5 mm and stayed under 0.01 m/s.
    // @param {float} speed - current speed in metres per second (unused)
    // @return {Void} - nothing
    void Park(float speed)
    {
        car.steer = 0f; car.throttle = 0f; if (car.drivetrain.gear != 0) car.drivetrain.SelectGear(0);
        bool handbrakeOnly = elapsedTime >= 13f; car.brake = handbrakeOnly ? 0f : 1f; car.handbrake = true;
        int partIndex = elapsedTime >= 6f && elapsedTime < 13f ? 0 : elapsedTime >= 16f && elapsedTime < 23f ? 1 : -1;
        if (partIndex >= 0)
        {
            var velocity = body.velocity;
            if (!pkHave) { pkHave = true; pkPos0 = body.position; pkMaxV = 0f; pkMaxA = 0f; pkVPrev = velocity;
                if (Physics.Raycast(body.position + Vector3.up * 5f, Vector3.down, out var hit, 20f)) pkSlope = Vector3.Angle(hit.normal, Vector3.up); }
            pkMaxV = Mathf.Max(pkMaxV, velocity.magnitude); pkMaxA = Mathf.Max(pkMaxA, (velocity - pkVPrev).magnitude / Time.fixedDeltaTime); pkVPrev = velocity;
            bool partEnds = (partIndex == 0 && elapsedTime + Time.fixedDeltaTime >= 13f) || (partIndex == 1 && elapsedTime + Time.fixedDeltaTime >= 23f);
            if (partEnds)
            {
                float displacement = (body.position - pkPos0).magnitude; bool ok = displacement < 0.005f && pkMaxV < 0.01f; if (!ok) pkFail++;
                pkRow[partIndex] = string.Format(System.Globalization.CultureInfo.InvariantCulture, "PARK {0} {1}: ground slope {2:0.0} deg, displacement over 7 s {3:0.0} mm, max speed {4:0.0000} m/s, max |accel| {5:0.000} m/s2 (need < 5 mm and < 0.01 m/s)",
                    ok ? "PASS" : "FAIL", partIndex == 0 ? "brake pedal + handbrake" : "handbrake only", pkSlope, displacement * 1000f, pkMaxV, pkMaxA);
                Debug.Log(pkRow[partIndex]); pkHave = false;
            }
        }
        if (elapsedTime >= 23f) { Debug.Log($"PARK RESULT {(pkFail == 0 ? "PASS" : "FAIL")} {2 - pkFail}/2"); Next(99); }
    }
    Vector3 gPos2; bool gHave2;
    // @description - Off-road rest test: teleports the car onto grass, runoff and far-field terrain one point after another, lets it settle for 4 s on the brakes and measures each point.
    // @param {float} speed - current speed in metres per second
    // @return {Void} - nothing
    void LagunaGrass(float speed)
    {
        car.throttle = 0f; car.brake = 1f; car.steer = 0f; car.handbrake = true;
        var trackManager = TrackManager.Instance;
        if (gIdx == 0 && elapsedTime >= gDiagNext) { gDiagNext = elapsedTime + 0.25f; if (!car.GetComponent<ContactDiag>()) car.gameObject.AddComponent<ContactDiag>(); var wheels = new[] { car.frontLeftWheel, car.frontRightWheel, car.rearLeftWheel, car.rearRightWheel }; string wheelText = "";
            foreach (var w in wheels) wheelText += $" [{(w.grounded ? w.Hit.collider.name.Substring(0, 8) : "air")} Fz {w.verticalLoad:0} om {w.angularVelocity:0.00} sr {w.slipRatio:0.00} sa {w.slipAngle * 57.3f:0.0} F {w.force.x:0}/{w.force.y:0} Tb {w.brakeTorque:0}]";
            Debug.Log($"GDIAG t {elapsedTime - gT:0.00} v {body.velocity.ToString("F2")} up {car.transform.up.ToString("F2")}{wheelText}"); }
        if (gIdx >= 0 && !gHave2 && elapsedTime - gT >= 2f) { gPos2 = body.position; gHave2 = true; }
        if (gIdx < 0 || elapsedTime - gT > 4f)
        {
            if (gIdx >= 0) GrassMeasure(speed);
            gIdx++; var position = GrassPoint(gIdx, out _); if (position == Vector3.zero) { LagunaGrassReport(); Next(99); return; }
            trackManager.Place(position, 30f * gIdx); gT = elapsedTime; gHave2 = false;
            if (Physics.Raycast(position + Vector3.up * 30f, Vector3.down, out var hit, 600f)) Debug.Log($"GRASSDBG place hit '{hit.collider.name}' layer {hit.collider.gameObject.layer} at {hit.point} rb {body.position}");
        }
    }
    // @description - Checks one off-road rest point: at least 3 wheels grounded, the car at rest (or on a slope above 20 degrees), and the car height within 0.5 m of the collider surface under it; also reports the offset from the DEM terrain height and warns if the surface mesh disagrees with the heightmap.
    // @param {float} speed - current speed in metres per second
    // @return {Void} - nothing
    void GrassMeasure(float speed)
    {
        GrassPoint(gIdx, out string name); var position = body.position; var terrainCollider = Object.FindObjectOfType<TerrainCollider>();
        float terrainHeight = float.NaN, slope = 0f; string under = "none";
        if (terrainCollider) terrainHeight = TrackManager.TerrainHeightAt(position, out slope);
        float surfaceHeight = float.NaN; if (Physics.Raycast(position + Vector3.up * 3f, Vector3.down, out var hit, 30f)) { under = hit.collider.name; surfaceHeight = hit.point.y; }
        float rideHeight = car.carParams.hubDropDesign + car.carParams.wheelRadius;   // design height of the car origin above the ground
        float heightAboveTerrain = position.y - rideHeight - terrainHeight, heightAboveSurface = position.y - rideHeight - surfaceHeight;   // ds: vs the collider actually under the car (= visual surface: grass/runoff mesh or terrain)
        int groundedWheels = (car.frontLeftWheel.grounded ? 1 : 0) + (car.frontRightWheel.grounded ? 1 : 0) + (car.rearLeftWheel.grounded ? 1 : 0) + (car.rearRightWheel.grounded ? 1 : 0);
        bool ok = terrainCollider != null && groundedWheels >= 3 && (speed < 0.3f || slope > 20f) && Mathf.Abs(heightAboveSurface) < 0.5f && FallResetCount() == 0; bool dataWarn = Mathf.Abs(heightAboveTerrain) >= 0.5f;
        if (!ok) gFails++;
        gRows.Add(string.Format(System.Globalization.CultureInfo.InvariantCulture, "GRASS {0} {1}: creep 2-4 s {13:0.0} mm, pos ({2:0.0},{3:0.00},{4:0.0}) DEM {5:0.00} car-ride-surface {11:+0.00;-0.00} m, car-ride-DEM {6:+0.00;-0.00} m{12}, wheels grounded {7}/4, speed {8:0.00} m/s, surface under car '{9}', slope {10:0}° (>20°: locked car may slide; must stay on the ground)",
            ok ? "PASS" : "FAIL", name, position.x, position.y, position.z, terrainHeight, heightAboveTerrain, groundedWheels, speed, under, slope, heightAboveSurface, dataWarn ? " (surface collider/visual mesh >0.5 m off the heightmap: data issue)" : "", gHave2 ? (position - gPos2).magnitude * 1000f : float.NaN));
    }
    // @description - Number of fall-safety resets of the track manager, or 0 if there is none.
    // @return {int} - fall reset count
    int FallResetCount() => TrackManager.Instance ? TrackManager.Instance.FallResets : 0;
    // @description - Prints every off-road rest result and the overall PASS or FAIL line.
    // @return {Void} - nothing
    void LagunaGrassReport() { foreach (var row in gRows) Debug.Log(row); Debug.Log($"LAGUNAGRASS RESULT {(gFails == 0 ? "PASS" : "FAIL")} {gRows.Count - gFails}/{gRows.Count} points, fall resets {FallResetCount()}"); }
    // ---- off-track at speed: start on the road 150 m upstream at 132 km/h (wheel spin matched), coast in neutral, steer off the asphalt onto runoff/grass/terrain.
    // Checks every physics step: car never below the heightmap (fall-through), a collider always under the car, no fall-safety reset.
    int oIdx = -1, oFails, oResets0; float oT, oMinAboveDem, oMinV, oMaxOff; int oNoSurf, oSteps; string oName; readonly System.Collections.Generic.List<string> oRows = new System.Collections.Generic.List<string>();
    readonly System.Collections.Generic.Dictionary<string, int> oSurf = new System.Collections.Generic.Dictionary<string, int>();
    static readonly (float s, float side, string name)[] OffSpots = {   // s of the run-off point, side +1 right / -1 left
        (2440f, -1f, "wide LEFT before the Corkscrew (T8 approach)"), (3330f, 1f, "outside RIGHT of T11 exit"), (260f, 1f, "outside RIGHT of T1"),
        (2600f, 1f, "Corkscrew exit RIGHT (T8A)"), (1990f, 1f, "outside RIGHT of T6"), (2440f, 1f, "RIGHT before the Corkscrew") };
    // @description - Off-track at speed test: starts on the road 150 m upstream at 132 km/h with matched wheel spin, coasts in neutral and steers off the asphalt onto runoff, grass or terrain at each listed spot. Each step checks that the car stays above the heightmap and that a collider is under it.
    // @param {float} speed - current speed in metres per second
    // @return {Void} - nothing
    void LagunaOff(float speed)
    {
        var trackManager = TrackManager.Instance; var trackInfo = trackManager.info; var centreline = trackInfo.Centreline; int pointCount = centreline.Count;
        if (oIdx < 0 || elapsedTime - oT > 7f)
        {
            if (oIdx >= 0) OffReport1();
            oIdx++; if (oIdx >= OffSpots.Length) { foreach (var row in oRows) Debug.Log(row); Debug.Log($"LAGUNAOFF RESULT {(oFails == 0 ? "PASS" : "FAIL")} {OffSpots.Length - oFails}/{OffSpots.Length} runs, fall resets {trackManager.FallResets}"); Next(99); return; }
            var startSpot = OffSpots[oIdx]; int startIndex = ((int)((startSpot.s - 150f) / 2f) + pointCount) % pointCount; var forward = centreline[(startIndex + 1) % pointCount] - centreline[startIndex]; forward.y = 0f;
            trackManager.Place(centreline[startIndex], Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg);
            body.velocity = car.transform.forward * (132f / 3.6f); foreach (var wheel in new[] { car.frontLeftWheel, car.frontRightWheel, car.rearLeftWheel, car.rearRightWheel }) wheel.angularVelocity = 132f / 3.6f / car.carParams.wheelRadius;
            oT = elapsedTime; oMinAboveDem = 99f; oNoSurf = 0; oSteps = 0; oMaxOff = 0f; oMinV = 999f; oName = startSpot.name; oResets0 = trackManager.FallResets; oSurf.Clear();
        }
        var spot = OffSpots[oIdx]; Vector3 p = body.position; int nearestIndex = trackInfo.Nearest(p, out float lateralOffset);
        // target line: centreline until 120 m before the spot, then ramps out to edge + 25 m on the chosen side by the spot, stays there
        float distanceToSpot = (nearestIndex * 2f - spot.s + 3600f + 1800f) % 3600f - 1800f;   // signed distance to the spot along the lap (m)
        float rampFraction = Mathf.Clamp01((distanceToSpot + 120f) / 120f); int targetIndex = (nearestIndex + 12) % pointCount; var headingDirection = centreline[(targetIndex + 1) % pointCount] - centreline[targetIndex]; headingDirection.y = 0f; headingDirection.Normalize(); var right = new Vector3(headingDirection.z, 0f, -headingDirection.x);
        float edge = spot.side > 0 ? trackInfo.HalfWidths[targetIndex].y : trackInfo.HalfWidths[targetIndex].x; var target = centreline[targetIndex] + right * spot.side * rampFraction * (edge + 25f);
        Vector3 localTarget = car.transform.InverseTransformPoint(new Vector3(target.x, p.y, target.z)); float alpha = Mathf.Atan2(localTarget.x, localTarget.z);
        car.steer = Mathf.Clamp(Mathf.Atan(2f * car.carParams.wheelbase * Mathf.Sin(alpha) / Mathf.Max(1f, localTarget.magnitude)) * Mathf.Rad2Deg, -car.carParams.MeanLockDeg, car.carParams.MeanLockDeg);
        car.throttle = 0f; car.brake = 0f; car.handbrake = false; car.drivetrain.SelectGear(0);
        oSteps++; float edgeHere = lateralOffset > 0 ? trackInfo.HalfWidths[nearestIndex].y : trackInfo.HalfWidths[nearestIndex].x; oMaxOff = Mathf.Max(oMaxOff, Mathf.Abs(lateralOffset) - edgeHere);
        { float terrainHeight = TrackManager.TerrainHeightAt(p, out _); if (!float.IsNaN(terrainHeight)) oMinAboveDem = Mathf.Min(oMinAboveDem, p.y - terrainHeight); }
        if (Physics.Raycast(p + Vector3.up * 2f, Vector3.down, out var hit, 10f)) { var surfaceName = SurfaceTag.Of(hit.collider).ToString(); oSurf[surfaceName] = oSurf.TryGetValue(surfaceName, out var count) ? count + 1 : 1; } else { oNoSurf++; if (oNoSurf == 1 || oNoSurf == 300) { var hs = Physics.RaycastAll(p + Vector3.up * 2f, Vector3.down, 100f); string hl = ""; foreach (var hh in hs) hl += $" {hh.collider.name}@{hh.point.y:0.00}"; Debug.Log($"OFFDIAG no collider: t {elapsedTime:0.00} pos {p.ToString("F2")} vel {body.velocity.ToString("F1")} terrain(finest) {TrackManager.TerrainHeightAt(p, out _):0.00} hits<=100m:{hl}"); } }
        if (Mathf.Abs(lateralOffset) - edgeHere > 2f) oMinV = Mathf.Min(oMinV, speed);
    }
    // @description - Evaluates one off-track run (no fall resets, always a surface under the car, never below the heightmap, went at least 5 m beyond the asphalt) and stores the result row.
    // @return {Void} - nothing
    void OffReport1()
    {
        var trackManager = TrackManager.Instance; int resets = trackManager.FallResets - oResets0; float ride = car.carParams.hubDropDesign + car.carParams.wheelRadius;
        bool ok = resets == 0 && oNoSurf == 0 && oMinAboveDem > ride - 0.5f && oMaxOff > 5f;
        if (!ok) oFails++;
        oRows.Add($"OFF {(ok ? "PASS" : "FAIL")} {oName}: entered at 132 km/h, max {oMaxOff:0.0} m beyond the asphalt edge, slowest off-track {oMinV * 3.6f:0} km/h, min (car origin - heightmap) {oMinAboveDem:0.00} m (ride height {ride:0.00}), steps with no collider under car {oNoSurf}/{oSteps}, fall resets {resets}, surfaces under car: {string.Join(", ", System.Linq.Enumerable.Select(oSurf, kv => $"{kv.Key} {100f * kv.Value / Mathf.Max(1, oSteps):0}%"))}");
    }
    // @description - Prints the Laguna track-following result (distance, time, laps, top speed, share of time with all wheels grounded, worst track edge excursion and clearance) and the PASS or FAIL verdict.
    // @return {Void} - nothing
    void LagunaReport()
    {
        Debug.Log(string.Format(System.Globalization.CultureInfo.InvariantCulture,
            "LAGUNA RESULT {0} drove {1:0} m in {2:0.0} s (track s reached {3} m, laps {4}), vmax {5:0} km/h, all-4-wheels-grounded {6:0.0}% (longest gap {7:0} ms), max beyond edge {8:0.00} m, min body-origin clearance {9:0.00} m, min (car y - centreline z) {10:0.00} m {11}",
            lagFail ? "FAIL" : "PASS", lagDist, elapsedTime - 4f, lagIdx * 2, lagLaps, lagMaxV * 3.6f, 100f * (lagSamples - lagUngrounded) / Mathf.Max(1, lagSamples), lagMaxUngroundedRun * Time.fixedDeltaTime * 1000f, lagMaxOff, lagMinClear, lagWorstY, lagWhy));
    }
    // ---- gamepad steering assist comparison (-gpassist 0 = direct: stick = lock fraction at CarParams.steeringRateDegPerSecond, 1 = GamepadSteering defaults) ----
    // Scripted "loose" gamepad driver: a path/heading controller whose output is exaggerated x3 and saturated (players push the stick
    // too far), plus seeded sample-and-hold noise (+-0.25 at 8 Hz). The same driver and seed for both modes.
    //   gplane: 80 km/h, 3.5 m lane change at phase t 1 s, back at 5 s.   gpoversteer: 90 km/h, gentle turn-in + handbrake pull (-gphb ms, default 300), then recover the original heading.
    GamepadSteering gpA; System.Random gpRng; float gpNoise, gpNoiseT, gpY0, gpH0, gpMaxBeta, gpMaxR, gpErr2, gpDs2, gpPrevSteer, gpRecT = -1f; int gpN, gpRev; float gpPrevD; bool gpSpin, gpDone;
    // @description - Reads an integer command line flag value, or a default if the flag is absent or not a number.
    // @param {string} flagName - flag name, for example "-gpseed"
    // @param {int} defaultValue - value to use when the flag is missing
    // @return {int} - the flag value or the default
    static int GpArg(string flagName, int defaultValue) { var arguments = System.Environment.GetCommandLineArgs(); int flagIndex = System.Array.IndexOf(arguments, flagName); return flagIndex >= 0 && flagIndex + 1 < arguments.Length && int.TryParse(arguments[flagIndex + 1], out var parsedValue) ? parsedValue : defaultValue; }
    // @description - Tells whether the gamepad steering assist is enabled for the comparison run (-gpassist 0 turns it off).
    // @return {bool} - true unless -gpassist 0 was passed
    bool GpAssistOn() { var arguments = System.Environment.GetCommandLineArgs(); int flagIndex = System.Array.IndexOf(arguments, "-gpassist"); return flagIndex < 0 || arguments[flagIndex + 1] != "0"; }
    // @description - Gamepad steering assist comparison with a scripted "loose" gamepad driver: a path and heading controller whose output is tripled and saturated, plus seeded sample-and-hold noise. Runs a lane change (gplane) or an oversteer provocation with handbrake (gpoversteer), with the assist on or off, and logs lateral error, slip angle, steering activity and reversals.
    // @param {float} speed - current speed in metres per second
    // @return {Void} - nothing
    void GpRun(float speed)
    {
        float deltaTime = Time.fixedDeltaTime, kmh = speed * 3.6f, targetSpeed = (scenario == "gplane" ? 80f : 90f) / 3.6f;
        SpeedCtl(targetSpeed, speed);
        if (gpRng == null) { gpRng = new System.Random(GpArg("-gpseed", 12345)); gpA = new GamepadSteering(new GamepadSteerSettings()); gpA.settings.countersteer = GpArg("-gpcs", 20) * 0.1f; gpA.settings.casterFollow = GpArg("-gpcf", 30) * 0.01f; }   // ablation: -gpcs x10, -gpcf %
        Vector3 forward = Vector3.ProjectOnPlane(car.transform.forward, Vector3.up).normalized;
        if (phase == 0)
        {   // straight at speed (heading-hold, exact)
            car.steer = 0f; gpA.roadDeg = 0f; gpA.filtered = 0f;
            if (kmh > targetSpeed * 3.6f - 1f && phaseT > 10f) { gpY0 = body.position.x; gpH0 = Mathf.Atan2(forward.x, forward.z); Next(1); }
            return;
        }
        float headingErrorDeg = Mathf.DeltaAngle(gpH0 * Mathf.Rad2Deg, Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg);   // deg, + right
        float referenceOffset = scenario == "gplane" ? (phaseT > 1f && phaseT < 5f ? 3.5f : 0f) : 0f, lateralOffset = body.position.x - gpY0;   // start heading is +z (lateral = +x right)
        float yawRateDeg = body.angularVelocity.y * Mathf.Rad2Deg;
        float idealStick = 0.25f * (referenceOffset - lateralOffset) - 0.08f * headingErrorDeg - 0.02f * yawRateDeg;   // ideal stick (fraction)
        if (scenario == "gpoversteer" && phaseT < 0.8f) { idealStick = 0.15f; car.handbrake = phaseT > 0.2f && phaseT < 0.2f + GpArg("-gphb", 300) * 0.001f; car.throttle = 0f; }   // gentle turn-in + handbrake pull (-gphb ms, default 300): rear steps out, then recover the heading
        if ((gpNoiseT -= deltaTime) <= 0f) { gpNoiseT = 0.125f; gpNoise = (float)(gpRng.NextDouble() * 2.0 - 1.0) * 0.25f; }
        float stick = Mathf.Clamp(3f * idealStick + gpNoise, -1f, 1f);
        var context = GamepadSteering.Context(car); float lockDeg = car.carParams.MeanLockDeg;
        if (GpAssistOn()) car.steer = gpA.Step(deltaTime, stick, context);
        else car.steer = Mathf.MoveTowards(car.steer, stick * lockDeg, car.carParams.steeringRateDegPerSecond * deltaTime);
        // metrics
        Vector3 velocity = body.velocity; float beta = Mathf.Atan2(Vector3.Dot(velocity, car.transform.right), Vector3.Dot(velocity, car.transform.forward)) * Mathf.Rad2Deg;
        gpMaxBeta = Mathf.Max(gpMaxBeta, Mathf.Abs(beta)); gpMaxR = Mathf.Max(gpMaxR, Mathf.Abs(yawRateDeg)); if (Mathf.Abs(beta) > 30f) gpSpin = true;
        gpErr2 += (referenceOffset - lateralOffset) * (referenceOffset - lateralOffset); float steerRate = (car.steer - gpPrevSteer) / deltaTime; gpDs2 += steerRate * steerRate; float steerChange = car.steer - gpPrevSteer;
        if (steerChange * gpPrevD < 0f && Mathf.Abs(steerChange) > 1e-3f) gpRev++; if (Mathf.Abs(steerChange) > 1e-3f) gpPrevD = steerChange; gpPrevSteer = car.steer; gpN++;
        if (scenario == "gpoversteer" && phaseT > 1f && gpRecT < 0f && Mathf.Abs(yawRateDeg) < 3f && Mathf.Abs(beta) < 2f && Mathf.Abs(headingErrorDeg) < 20f) gpRecT = phaseT;
        if ((phaseT > 10f || gpSpin) && !gpDone)
        {
            gpDone = true;
            Debug.Log(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "GPSTEER RESULT {0} seed=" + GpArg("-gpseed", 12345) + " hb=" + GpArg("-gphb", 300) + " cs=" + GpArg("-gpcs", 20) + " cf=" + GpArg("-gpcf", 30) + " assist={1} spin={2} rmsLatErr={3:0.00} m maxBeta={4:0.0} deg maxYawRate={5:0.0} deg/s rmsSteerRate={6:0.0} deg/s steerReversals={7} recovered_t={8:0.00} s finalHeading={9:0.0} deg",
                scenario, GpAssistOn() ? 1 : 0, gpSpin, Mathf.Sqrt(gpErr2 / gpN), gpMaxBeta, gpMaxR, Mathf.Sqrt(gpDs2 / gpN), gpRev, gpRecT, headingErrorDeg));
            phase = 99;
        }
    }
    // @description - Scenario dispatcher: sets pedals and steering for the selected scenario (revsweep, park, kbdlaunch, stallrestart, gamepad and Laguna tests, thermal tests, accel, topspeed, skidpad, rampsteer, stepsteer, lanechange, brake, brakethr, brakeinturn, liftoff) and ends it by setting phase 99.
    // @param {float} speed - current speed in metres per second
    // @return {Void} - nothing
    void Run(float speed)
    {
        float kmh = speed * 3.6f;
        switch (scenario)
        {
            case "revsweep":      // spawn engine off -> ignition -> starter -> idle -> neutral rev sweep to the limiter -> lift -> ignition off
                if (elapsedTime < 0.05f) car.engine.SetRunning(false);
                car.engine.ignition = elapsedTime > 1f && elapsedTime < 14f; car.engine.starter = elapsedTime > 1.5f && elapsedTime < 3f;
                car.throttle = elapsedTime < 4f ? 0f : elapsedTime < 10f ? (elapsedTime - 4f) / 6f : elapsedTime < 12f ? 1f : 0f; if (elapsedTime > 16f) Next(99); return;
            case "park": Park(speed); return;
            case "kbdlaunch":     // keyboard driver: E into 1st (done at t=3 by the harness), clutch never touched, throttle after 1 s; auto clutch on
                car.autoClutch = true; car.throttle = elapsedTime < 4f ? 0f : 0.4f; if (elapsedTime > 12f) Next(99); return;
            case "stallrestart":  // auto clutch off: stalls in gear at zero throttle; then neutral + starter restarts it
                car.autoClutch = false; car.throttle = 0f;
                if (elapsedTime > 6f && car.drivetrain.gear != 0) car.drivetrain.SelectGear(0);
                car.engine.starter = elapsedTime > 6.5f && elapsedTime < 8f; if (elapsedTime > 11f) Next(99); return;
            case "gplane": case "gpoversteer": GpRun(speed); return;
            case "laguna": LagunaDrive(speed); return;
            case "lagunatherm": car.carParams.tireThermal = true; LagunaDrive(speed); if (elapsedTime >= thNext) { thNext = elapsedTime + 10f; ThermLog($"t {elapsedTime:0}"); } return;   // 3 brisk laps, thermal model on
            case "thermslide":   // thermal model on: 60 s sliding on the 40 m circle (target 45 mph, above the ~40 mph limit), then 120 s straight at 62 mph
                car.carParams.tireThermal = true;
                if (elapsedTime < 63f) { car.steer = Mathf.Clamp(CircleSteer(skidpadRadius), -car.carParams.MeanLockDeg, car.carParams.MeanLockDeg); SpeedCtl(72f / 3.6f, speed); }
                else { if (thHead == Vector3.zero) thHead = Vector3.ProjectOnPlane(car.transform.forward, Vector3.up).normalized; car.steer = Mathf.Clamp(2f * Vector3.SignedAngle(Vector3.ProjectOnPlane(car.transform.forward, Vector3.up), thHead, Vector3.up), -10f, 10f); SpeedCtl(100f / 3.6f, speed); }
                if (elapsedTime >= thNext) { thNext = elapsedTime + 5f; ThermLog($"t {elapsedTime:0} {(elapsedTime < 63f ? "slide" : "straight")}"); }
                return;
            case "lagunaoff": LaunchDiag(); LagunaOff(speed); return;
            case "lagunakerb": kerbMode = true; LagunaDrive(speed); KerbSample(); return;
            case "lagunagrass": LagunaGrass(speed); return;
            case "accel": case "topspeed":   // topspeed = accel held for 150 s
                car.steer = 0f; car.brake = 0f; car.throttle = 1f;
                if (t100 < 0f && kmh >= 100f) { t100 = elapsedTime - 3f; Debug.Log($"SMOKE 0-100 km/h = {t100:0.00} s"); }
                break;
            case "powerstep": PowerStep(speed); return;
            case "bumpstart": BumpStart(speed); return;
            case "skidpad":      // constant radius, speed ramps 0.25 km/h per s from 30 km/h
                car.steer = Mathf.Clamp(CircleSteer(skidpadRadius), -car.carParams.MeanLockDeg, car.carParams.MeanLockDeg);
                SpeedCtl((35f + 0.5f * Mathf.Max(0f, elapsedTime - 3f)) / 3.6f, speed);
                { var position = body.position; if (elapsedTime > 20f && Mathf.Abs(Mathf.Sqrt((position.x - skidpadRadius) * (position.x - skidpadRadius) + position.z * position.z) - skidpadRadius) > 4f) phase = 99; }
                break;
            case "rampsteer":    // 80 km/h, steer ramps 0.5 deg/s (road wheel) after reaching speed
                if (phase == 0) { car.steer = 0f; SpeedCtl(80f / 3.6f, speed); if (kmh > 79f && phaseT > 12f) Next(1); }
                else { SpeedCtl(80f / 3.6f, speed); car.steer = 0.5f * phaseT; if (car.steer > 10f) phase = 99; }
                break;
            case "stepsteer":    // 80 km/h, 2.5 deg step (~0.4 g target) for 6 s
                SpeedCtl(80f / 3.6f, speed);
                if (phase == 0) { car.steer = 0f; if (kmh > 79f && phaseT > 12f) Next(1); }
                else { car.steer = Mathf.MoveTowards(car.steer, 2.5f, 25f * Time.fixedDeltaTime); if (phaseT > 6f) phase = 99; }   // 25 deg/s road-wheel ramp (~440 deg/s handwheel)
                break;
            case "lanechange":   // 80 km/h, one full sine period of 3 deg amplitude over 2.5 s
                SpeedCtl(80f / 3.6f, speed);
                if (phase == 0) { car.steer = 0f; if (kmh > 79f && phaseT > 12f) Next(1); }
                else { car.steer = phaseT < 2.5f ? 3f * Mathf.Sin(2f * Mathf.PI * phaseT / 2.5f) : 0f; if (phaseT > 7f) phase = 99; }
                break;
            case "brake":        // accelerate to 105, then full brake (no ABS: lock-up test)
            case "brakethr":     // same with -pedal (default 0.85) (approximates threshold braking)
                car.steer = 0f;
                if (phase == 0) { car.throttle = 1f; car.brake = 0f; if (kmh > 105f) Next(1); }
                else if (phase == 1) { car.throttle = 0f; car.brake = scenario == "brakethr" ? pedal : 1f; car.clutch = 1f; if (kmh < 0.5f) Next(2); }
                else { car.brake = 1f; car.clutch = 1f; if (phaseT > 1f) phase = 99; }
                break;
            case "brakeinturn":  // steady 40 m circle at ~0.6 g (≈ 63 km/h) then freeze steer + 0.4 brake (≈0.4 g)
            case "liftoff":      // same, then freeze steer + throttle off
                if (phase == 0)
                {
                    car.steer = Mathf.Clamp(CircleSteer(skidpadRadius), -car.carParams.MeanLockDeg, car.carParams.MeanLockDeg);
                    SpeedCtl(Mathf.Min(60f, 35f + 1f * Mathf.Max(0f, elapsedTime - 3f)) / 3.6f, speed);   // gentle ramp to 60 km/h (~0.71 g on R=40)
                    var position = body.position; float radiusError = Mathf.Abs(Mathf.Sqrt((position.x - skidpadRadius) * (position.x - skidpadRadius) + position.z * position.z) - skidpadRadius);
                    if (elapsedTime > 40f && radiusError < 1f) { steerHold = car.steer; Next(1); }
                }
                else
                {
                    car.steer = steerHold;
                    if (scenario == "liftoff") { car.throttle = 0f; car.brake = 0f; } else { car.throttle = 0f; car.brake = 0.4f; car.clutch = 1f; }
                    if (phaseT > 3f) phase = 99;
                }
                break;
        }
    }
}
