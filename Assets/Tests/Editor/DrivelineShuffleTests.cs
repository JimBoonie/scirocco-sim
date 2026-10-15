// Validation of the EXPERIMENTAL driveline shuffle model (Driveline/*.cs). The default driveline path is not exercised here; its bit-identical proof is the diagnostic-scenario hash run.
// Pass criteria are the physical ones from driveline_shuffle_research.md section 5. Each test logs its measured value so a failure is readable.
using NUnit.Framework; using UnityEngine;

public class DrivelineShuffleTests
{
    const float substep = 0.0005f;   // s, the outer tire/drivetrain substep of CarController
    static System.Text.StringBuilder report = new System.Text.StringBuilder();
    // Vehicle-in-the-loop rig (test only): the car as a rigid mass on two driven tires. Each tire is the sim's relaxation model: patch deflection u' = (omega R - v) - |v| u / sigma,
    // F = k u with k = C_x F_z / sigma (C_x = longitudinalStiffnessCoefficient, sigma = relaxationLength, F_z = static front load per wheel, front weight fraction E for the rig only).
    // Rolling resistance and drag are ignored: they only add damping.
    static double rigVehicleSpeed, rigLeftDeflection, rigRightDeflection;
    const float rigFrontWeightFraction = 0.60f, rigMinimumSpeedForRelaxation = 0.5f;

    // @description - advances one rig tire patch deflection and returns its force
    // @param {WheelCorner} wheel - wheel
    // @param {double} deflection - patch deflection in m (updated)
    // @return {float} - force in N
    static float RigTireForce(WheelCorner wheel, ref double deflection)
    {
        var carParams = wheel.carParams; float loadPerWheel = carParams.mass * 9.81f * rigFrontWeightFraction * 0.5f, relaxationLength = carParams.relaxationLength;
        double rate = wheel.angularVelocity * carParams.wheelRadius - rigVehicleSpeed - (System.Math.Abs(rigVehicleSpeed) + rigMinimumSpeedForRelaxation) * deflection / relaxationLength;
        deflection += rate * substep;
        return (float)(carParams.longitudinalStiffnessCoefficient * loadPerWheel / relaxationLength * deflection);
    }

    // @description - builds an engine, the experimental driveline and two driven wheels at rest on the ground
    // @param {CarParams} carParams - (out) parameter asset
    // @param {ShuffleDrivelineParameters} parameters - (out) model parameters
    // @param {Engine} engine - (out) engine
    // @param {Drivetrain} drivetrain - (out) gearbox state
    // @param {WheelCorner} leftWheel - (out) left driven wheel
    // @param {WheelCorner} rightWheel - (out) right driven wheel
    // @return {ExperimentalDriveline} - model under test
    static ExperimentalDriveline Build(out CarParams carParams, out ShuffleDrivelineParameters parameters, out Engine engine, out Drivetrain drivetrain, out WheelCorner leftWheel, out WheelCorner rightWheel)
    {
        carParams = ScriptableObject.CreateInstance<CarParams>(); parameters = ShuffleDrivelineParameters.CreateDefault(); engine = new Engine(carParams); drivetrain = new Drivetrain(carParams);
        leftWheel = new GameObject("left wheel").AddComponent<WheelCorner>(); rightWheel = new GameObject("right wheel").AddComponent<WheelCorner>();
        leftWheel.carParams = carParams; rightWheel.carParams = carParams; leftWheel.force = Vector2.zero; rightWheel.force = Vector2.zero; rigVehicleSpeed = 0.0; rigLeftDeflection = rigRightDeflection = 0.0;
        return new ExperimentalDriveline(engine, carParams, parameters);
    }

    // @description - advances the model for a simulated time at the 2 kHz outer substep
    // @param {ExperimentalDriveline} model - model
    // @param {Engine} engine - engine
    // @param {Drivetrain} drivetrain - gearbox state
    // @param {WheelCorner} leftWheel - left wheel
    // @param {WheelCorner} rightWheel - right wheel
    // @param {float} seconds - simulated time in s
    // @param {float} throttle - accelerator pedal 0..1
    // @param {float} pedal - clutch pedal 0..1
    // @param {System.Action<float>} sample - optional callback per substep with the time in s
    // @return {Void}
    static void Run(ExperimentalDriveline model, Engine engine, Drivetrain drivetrain, WheelCorner leftWheel, WheelCorner rightWheel, float seconds, float throttle, float pedal, System.Action<float> sample = null)
    {
        int steps = (int)(seconds / substep);
        for (int step = 0; step < steps; step++)
        {
            leftWheel.force.x = RigTireForce(leftWheel, ref rigLeftDeflection); rightWheel.force.x = RigTireForce(rightWheel, ref rigRightDeflection);
            model.Advance(substep, throttle, pedal, 0f, drivetrain, leftWheel, rightWheel);
            rigVehicleSpeed += (leftWheel.force.x + rightWheel.force.x) / leftWheel.carParams.mass * substep;
            leftWheel.angularVelocity += (leftWheel.driveTorque - leftWheel.force.x * leftWheel.carParams.wheelRadius) / leftWheel.carParams.wheelInertia * substep;
            rightWheel.angularVelocity += (rightWheel.driveTorque - rightWheel.force.x * rightWheel.carParams.wheelRadius) / rightWheel.carParams.wheelInertia * substep;
            sample?.Invoke((step + 1) * substep);
        }
    }

    // @description - removes the least-squares straight line from a signal (the slow acceleration trend under the shuffle oscillation)
    // @param {System.Collections.Generic.List<float>} input - samples
    // @return {System.Collections.Generic.List<float>} - detrended copy
    static System.Collections.Generic.List<float> Detrend(System.Collections.Generic.List<float> input)
    {
        int count = input.Count; double sumX = 0, sumY = 0, sumXY = 0, sumXX = 0;
        for (int i = 0; i < count; i++) { sumX += i; sumY += input[i]; sumXY += (double)i * input[i]; sumXX += (double)i * i; }
        double slope = (count * sumXY - sumX * sumY) / (count * sumXX - sumX * sumX), intercept = (sumY - slope * sumX) / count;
        var output = new System.Collections.Generic.List<float>(count); for (int i = 0; i < count; i++) output.Add((float)(input[i] - slope * i - intercept)); return output;
    }

    // @description - dominant frequency of a sampled signal: Hann-windowed discrete Fourier scan (0.02 Hz steps) after removing the linear trend
    // @param {System.Collections.Generic.List<float>} samples - samples at substep spacing
    // @param {int} startIndex - first sample used
    // @param {float} minimumFrequency - Hz
    // @param {float} maximumFrequency - Hz
    // @return {float} - frequency in Hz
    static float DominantFrequency(System.Collections.Generic.List<float> input, int startIndex, float minimumFrequency, float maximumFrequency)
    {
        var samples = Detrend(input); int count = samples.Count - startIndex; float bestFrequency = minimumFrequency; double bestMagnitude = -1.0;
        for (float frequency = minimumFrequency; frequency <= maximumFrequency; frequency += 0.02f)
        {
            double real = 0.0, imaginary = 0.0, omega = 2.0 * System.Math.PI * frequency * substep;
            for (int i = 0; i < count; i++) { double window = 0.5 - 0.5 * System.Math.Cos(2.0 * System.Math.PI * i / (count - 1)); real += window * samples[startIndex + i] * System.Math.Cos(omega * i); imaginary += window * samples[startIndex + i] * System.Math.Sin(omega * i); }
            double magnitude = real * real + imaginary * imaginary; if (magnitude > bestMagnitude) { bestMagnitude = magnitude; bestFrequency = frequency; }
        }
        return bestFrequency;
    }

    // @description - runs the vehicle-in-the-loop rig at a steady 1500 rpm, steps the throttle and measures the shuffle frequency of the carrier speed, per gear
    // @return {Void}
    [Test] public void ShuffleFrequencyPerGearMatchesPrediction()
    {
        float[] lower = { 1.96f, 2.87f, 3.9f, 4.8f, 5.7f }, upper = { 3.12f, 4.7f, 6.4f, 7.8f, 9.3f };   // Hz, research report section 0: 5th-95th percentile bounds, lower edge x0.87 for the engine-roll shift
        float previousFrequency = 0f;
        for (int gear = 1; gear <= 5; gear++)
        {
            var model = Build(out var carParams, out var parameters, out var engine, out var drivetrain, out var left, out var right); parameters.firingPulsesEnabled = false;
            drivetrain.SelectGear(gear); drivetrain.Tick(1f);
            float wheelSpeed = 1500f * Mathf.PI / 30f / drivetrain.TotalRatio; left.angularVelocity = right.angularVelocity = wheelSpeed; rigVehicleSpeed = wheelSpeed * carParams.wheelRadius; engine.SetRunning(true); engine.EndExternalCrankInterval(wheelSpeed * drivetrain.TotalRatio);
            var samples = new System.Collections.Generic.List<float>();
            Run(model, engine, drivetrain, left, right, 3.0f, 1f, 0f, _ => samples.Add((float)model.Chain.CarrierAngularVelocity));
            float frequency = DominantFrequency(samples, samples.Count / 10, 1f, 25f);
            report.AppendLine($"shuffle gear {gear}: {frequency:0.00} Hz (band {lower[gear - 1]:0.0}..{upper[gear - 1]:0.0})");
            Assert.Greater(frequency, previousFrequency, "shuffle frequency must rise with gear"); previousFrequency = frequency;
            Assert.GreaterOrEqual(frequency, lower[gear - 1], $"gear {gear} shuffle frequency below the predicted band");
            Assert.LessOrEqual(frequency, upper[gear - 1], $"gear {gear} shuffle frequency above the predicted band");
        }
    }

    // @description - a clutch dump from idle in first gear must produce a single torque peak, then settle to the steady value within 0.5 s without growing
    // @return {Void}
    [Test] public void ClutchDumpSettles()
    {
        var model = Build(out var carParams, out var parameters, out var engine, out var drivetrain, out var left, out var right);
        drivetrain.SelectGear(1); drivetrain.Tick(1f); engine.SetRunning(true);
        var torque = new System.Collections.Generic.List<float>();
        Run(model, engine, drivetrain, left, right, 1.0f, 0.5f, 1f, _ => torque.Add(left.driveTorque));
        int released = torque.Count; Run(model, engine, drivetrain, left, right, 1.0f, 0.5f, 0f, _ => torque.Add(left.driveTorque));
        float peak = 0f, steady = 0f; int steadyCount = (int)(0.2f / substep);
        for (int i = released; i < torque.Count; i++) peak = Mathf.Max(peak, torque[i]);
        for (int i = torque.Count - steadyCount; i < torque.Count; i++) steady += torque[i]; steady /= steadyCount;
        float overshoot = peak / Mathf.Max(steady, 1f);
        report.AppendLine($"clutch dump: peak {peak:0} N m, settled {steady:0} N m, overshoot {overshoot:0.00}");
        Assert.Greater(peak, steady, "the dump must overshoot the settled torque");
        Assert.Less(overshoot, 6f, "the dump overshoot is unphysical");
        Assert.Greater(engine.rpm, carParams.stallRpm, "the engine must not stall on a half-throttle dump");
        Assert.Less(Mathf.Abs(drivetrain.clutchSlip), 1e-3f, "a locked clutch must show no residual slip (the default model shows 19..130 rpm)"); Assert.IsTrue(model.Chain.ClutchIsStuck, "the clutch must be stuck after the dump has settled");
    }

    // @description - tip-in then tip-out must cross the lash dead zone twice and the torque must reverse sign
    // @return {Void}
    [Test] public void TipInTipOutCrossesLash()
    {
        var model = Build(out var carParams, out var parameters, out var engine, out var drivetrain, out var left, out var right); parameters.firingPulsesEnabled = false;
        drivetrain.SelectGear(3); drivetrain.Tick(1f);
        float wheelSpeed = 3000f * Mathf.PI / 30f / drivetrain.TotalRatio; left.angularVelocity = right.angularVelocity = wheelSpeed; rigVehicleSpeed = wheelSpeed * carParams.wheelRadius; engine.SetRunning(true); engine.EndExternalCrankInterval(wheelSpeed * drivetrain.TotalRatio);
        float minimumDeflection = float.MaxValue, maximumDeflection = float.MinValue; bool sawPositive = false, sawNegative = false;
        Run(model, engine, drivetrain, left, right, 0.6f, 1f, 0f, _ => { float deflection = (float)model.Chain.ShaftDeflectionRadians; minimumDeflection = Mathf.Min(minimumDeflection, deflection); maximumDeflection = Mathf.Max(maximumDeflection, deflection); sawPositive |= left.driveTorque > 5f; sawNegative |= sawPositive && left.driveTorque < -5f; });
        Run(model, engine, drivetrain, left, right, 0.6f, 0f, 0f, _ => { float deflection = (float)model.Chain.ShaftDeflectionRadians; minimumDeflection = Mathf.Min(minimumDeflection, deflection); maximumDeflection = Mathf.Max(maximumDeflection, deflection); sawPositive |= left.driveTorque > 5f; sawNegative |= sawPositive && left.driveTorque < -5f; });
        float lash = parameters.shaftAndJointLashAtWheelDegrees * Mathf.Deg2Rad;
        report.AppendLine($"tip-in/out: shaft deflection {minimumDeflection * Mathf.Rad2Deg:0.00}..{maximumDeflection * Mathf.Rad2Deg:0.00} deg, lash {parameters.shaftAndJointLashAtWheelDegrees:0.00} deg, torque reversed {sawNegative}");
        Assert.Greater(maximumDeflection - minimumDeflection, lash, "the shaft deflection must cross more than the lash");
        Assert.IsTrue(sawNegative, "tip-out must reverse the wheel torque");
    }

    // @description - full throttle against locked wheels must stall the engine below stall speed and it must stay stopped
    // @return {Void}
    [Test] public void StallAgainstLockedWheelsStaysStopped()
    {
        var model = Build(out var carParams, out var parameters, out var engine, out var drivetrain, out var left, out var right);
        drivetrain.SelectGear(1); drivetrain.Tick(1f); engine.SetRunning(true); left.brakeTorque = right.brakeTorque = 1e6f;
        Run(model, engine, drivetrain, left, right, 3f, 1f, 0f, _ => { left.angularVelocity = 0f; right.angularVelocity = 0f; });
        report.AppendLine($"stall: {engine.rpm:0.0} rpm, combusting {engine.combusting}");
        Assert.Less(engine.rpm, 1f, "a stalled crank must stop"); Assert.IsFalse(engine.combusting, "combustion must stop at the stall");
    }

    // @description - halving the micro-step phase limit must change the shuffle response by less than 5 percent (step-size convergence)
    // @return {Void}
    [Test] public void MicrostepConvergence()
    {
        float coarse = ShuffleAmplitude(0.1f), fine = ShuffleAmplitude(0.05f);
        float relativeChange = Mathf.Abs(fine - coarse) / Mathf.Max(coarse, 1e-6f);
        report.AppendLine($"convergence: carrier speed amplitude {coarse:0.000} rad/s at 0.10 rad/step, {fine:0.000} at 0.05, relative change {relativeChange:0.000}");
        Assert.Less(relativeChange, 0.05f, "halving the micro-step must change the response by less than 5 percent");
    }

    // @description - peak-to-peak carrier speed oscillation after a throttle step at a given micro-step phase limit
    // @param {float} phaseLimitRadians - maximum phase advance per micro-step in rad
    // @return {float} - rad/s
    static float ShuffleAmplitude(float phaseLimitRadians)
    {
        var model = Build(out var carParams, out var parameters, out var engine, out var drivetrain, out var left, out var right);
        parameters.maximumPhaseAdvancePerMicrostepRadians = phaseLimitRadians; parameters.firingPulsesEnabled = false;
        drivetrain.SelectGear(2); drivetrain.Tick(1f); float wheelSpeed = 2000f * Mathf.PI / 30f / drivetrain.TotalRatio; left.angularVelocity = right.angularVelocity = wheelSpeed; rigVehicleSpeed = wheelSpeed * carParams.wheelRadius; engine.SetRunning(true); engine.EndExternalCrankInterval(wheelSpeed * drivetrain.TotalRatio);
        var samples = new System.Collections.Generic.List<float>();
        Run(model, engine, drivetrain, left, right, 1.0f, 1f, 0f, time => { if (time >= 0.3f) samples.Add((float)model.Chain.CarrierAngularVelocity); });
        var detrended = Detrend(samples); float minimum = float.MaxValue, maximum = float.MinValue; foreach (float value in detrended) { minimum = Mathf.Min(minimum, value); maximum = Mathf.Max(maximum, value); }
        return maximum - minimum;
    }

    // @description - step-size convergence at high rpm with the firing ripple on: 5500 rpm in 2nd gear, throttle step; halving the micro-step phase limit (0.1 to 0.05 rad) must change the
    //   mean wheel torque and the crank-speed ripple by less than 3 percent, and the coarse 0.2 rad run must stay within 10 percent (reported)
    // @return {Void}
    [Test] public void MicrostepConvergenceAtHighRpmWithFiringPulses()
    {
        float[] meanA = HighRpmMetrics(0.2f), meanB = HighRpmMetrics(0.1f), meanC = HighRpmMetrics(0.05f);
        float torqueChange = Mathf.Abs(meanC[0] - meanB[0]) / Mathf.Abs(meanC[0]), rippleChange = Mathf.Abs(meanC[1] - meanB[1]) / Mathf.Max(meanC[1], 1e-6f);
        report.AppendLine($"high-rpm convergence (5500 rpm, ripple on): mean wheel torque {meanA[0]:0.0}/{meanB[0]:0.0}/{meanC[0]:0.0} N m, crank-speed ripple {meanA[1]:0.000}/{meanB[1]:0.000}/{meanC[1]:0.000} rad/s at 0.20/0.10/0.05 rad per micro-step; 0.10 vs 0.05 changes {100 * torqueChange:0.00} % and {100 * rippleChange:0.00} %");
        Assert.Less(torqueChange, 0.03f); Assert.Less(rippleChange, 0.03f);
        Assert.Less(Mathf.Abs(meanA[0] - meanC[0]) / Mathf.Abs(meanC[0]), 0.10f, "the coarse step must stay within 10 percent");
    }

    // @description - mean wheel torque and peak-to-peak crank speed ripple (detrended) in the last 0.3 s of a high-rpm throttle step
    // @param {float} phaseLimitRadians - micro-step phase limit in rad
    // @return {float[]} - {mean wheel torque in N m, crank speed ripple in rad/s}
    static float[] HighRpmMetrics(float phaseLimitRadians)
    {
        var model = Build(out var carParams, out var parameters, out var engine, out var drivetrain, out var left, out var right); parameters.maximumPhaseAdvancePerMicrostepRadians = phaseLimitRadians; model = new ExperimentalDriveline(engine, carParams, parameters);
        drivetrain.SelectGear(2); drivetrain.Tick(1f); float wheelSpeed = 5500f * Mathf.PI / 30f / drivetrain.TotalRatio; left.angularVelocity = right.angularVelocity = wheelSpeed; rigVehicleSpeed = wheelSpeed * carParams.wheelRadius;
        engine.SetRunning(true); engine.EndExternalCrankInterval(wheelSpeed * drivetrain.TotalRatio);
        var speeds = new System.Collections.Generic.List<float>(); double torqueSum = 0.0; int torqueCount = 0;
        Run(model, engine, drivetrain, left, right, 0.8f, 0.7f, 0f, time => { if (time > 0.5f) { speeds.Add((float)model.Chain.CrankAngularVelocity); torqueSum += left.driveTorque; torqueCount++; } });
        var detrended = Detrend(speeds); float low = float.MaxValue, high = float.MinValue; foreach (float value in detrended) { low = Mathf.Min(low, value); high = Mathf.Max(high, value); }
        return new[] { (float)(torqueSum / torqueCount), high - low };
    }

    // @description - the Wiebe burn must put 50 percent burned at the configured angle and the peak pressure must sit after TDC in the expected range
    // @return {Void}
    [Test] public void WiebeBurnPhasingAndPeakPressure()
    {
        var model = Build(out var carParams, out var parameters, out var engine, out var drivetrain, out var left, out var right);
        var firing = model.TorqueSource.FiringPulses; firing.SetOperatingPoint(carParams.ambientPressure, carParams.exhaustBackPressure, carParams.ambientPressure, 150f);
        double halfBurnAngle = parameters.halfBurnAngleAfterTopDeadCenterDegrees * Mathf.Deg2Rad;
        float peak = 0f; double peakAngle = 0.0;
        for (double angle = -Mathf.PI; angle < Mathf.PI; angle += 0.5 * Mathf.Deg2Rad) { float pressure = (float)firing.CylinderPressure(angle); if (pressure > peak) { peak = pressure; peakAngle = angle; } }
        report.AppendLine($"Wiebe: spark advance {firing.SparkAdvanceBeforeTopDeadCenterDegrees:0.0} deg BTDC, peak pressure {peak / 1e5:0.0} bar at {peakAngle * Mathf.Rad2Deg:0.0} deg, heat per cycle {firing.HeatPerCycleJoules:0} J");
        Assert.Greater(firing.SparkAdvanceBeforeTopDeadCenterDegrees, 5.0, "MBT spark must be before TDC");
        Assert.Less(firing.SparkAdvanceBeforeTopDeadCenterDegrees, 40.0, "MBT spark must be within a normal advance range");
        Assert.Greater(peakAngle * Mathf.Rad2Deg, 5.0, "peak pressure must come after TDC"); Assert.Less(peakAngle * Mathf.Rad2Deg, 25.0, "peak pressure must come before 25 deg ATDC");
        Assert.Greater(peak, 20e5f, "WOT peak pressure is far too low"); Assert.Less(peak, 120e5f, "WOT peak pressure is far too high");
    }

    // @description - the firing torque ripple must have zero mean and a firing frequency of twice per revolution
    // @return {Void}
    [Test] public void FiringRippleHasZeroMeanAndTheRightFrequency()
    {
        var model = Build(out var carParams, out var parameters, out var engine, out var drivetrain, out var left, out var right);
        var firing = model.TorqueSource.FiringPulses; firing.SetOperatingPoint(carParams.ambientPressure * 0.5, carParams.exhaustBackPressure, carParams.ambientPressure, 80f);
        double speed = 3000.0 * Mathf.PI / 30.0, sum = 0.0; int count = 4000; var samples = new System.Collections.Generic.List<float>();
        for (int i = 0; i < count; i++) { double torque = firing.RippleTorque(i * speed * substep, speed); sum += torque; samples.Add((float)torque); }
        float frequency = DominantFrequency(samples, 0, 50f, 400f);
        report.AppendLine($"ripple: mean {sum / count:0.0000} N m, dominant {frequency:0.0} Hz at 3000 rpm (expected {2.0 * speed / (2.0 * Mathf.PI):0.0} Hz)");
        Assert.AreEqual(0.0, sum / count, 1.0, "the ripple mean must be zero");
        Assert.AreEqual(2.0 * speed / (2.0 * Mathf.PI), frequency, 5f, "the firing frequency must be two per revolution");
    }

    // @description - the pedal curve must be full clamp through the free play, zero at full pedal, and monotone in between
    // @return {Void}
    [Test] public void PedalCurveHasFreePlayAndABiteRange()
    {
        var parameters = ShuffleDrivelineParameters.CreateDefault();
        float bite = ClutchEngagementCurve.BitePointPedalPosition(parameters), released = ClutchEngagementCurve.FullyReleasedPedalPosition(parameters);
        report.AppendLine($"pedal curve: bite at {bite:0.00}, fully released at {released:0.00}");
        Assert.AreEqual(1f, ClutchEngagementCurve.ClampFraction(parameters, 0f), 1e-4f);
        Assert.AreEqual(1f, ClutchEngagementCurve.ClampFraction(parameters, bite * 0.99f), 1e-3f, "free play must hold full clamp");
        Assert.AreEqual(0f, ClutchEngagementCurve.ClampFraction(parameters, 1f), 1e-4f);
        float previous = 1f;
        for (float pedal = bite; pedal <= released; pedal += 0.02f) { float clamp = ClutchEngagementCurve.ClampFraction(parameters, pedal); Assert.LessOrEqual(clamp, previous + 1e-4f, "the bite range must be monotone"); previous = clamp; }
        Assert.Greater(released, bite, "there must be a bite range"); Assert.Less(released, 1f, "full release must be reached before the pedal stop");
    }

    // @description - the two unequal halfshafts must combine to the open-differential stiffness 4 kL kR / (kL + kR) and the right shaft must be softer
    // @return {Void}
    [Test] public void UnequalShaftsCombineForTheOpenDifferential()
    {
        var parameters = ShuffleDrivelineParameters.CreateDefault();
        float left = parameters.LeftShaftStiffness(), right = parameters.RightShaftStiffness(), combined = parameters.CombinedShaftStiffness();
        report.AppendLine($"shafts: left {left / Mathf.Rad2Deg:0.0} N m/deg, right {right / Mathf.Rad2Deg:0.0}, combined (open differential) {combined / Mathf.Rad2Deg:0.0} N m/deg");
        Assert.Less(right, left, "the longer right shaft must be softer");
        Assert.AreEqual(4f * left * right / (left + right), combined, combined * 1e-4f, "open differential combination");
    }

    // @description - symmetric Jacobi eigenvalue iteration (test helper)
    // @param {double[,]} matrix - symmetric matrix, destroyed
    // @return {double[]} - eigenvalues
    static double[] JacobiEigenvalues(double[,] matrix)
    {
        int size = matrix.GetLength(0);
        for (int sweep = 0; sweep < 100; sweep++)
        {
            double off = 0.0; for (int row = 0; row < size; row++) for (int column = row + 1; column < size; column++) off += matrix[row, column] * matrix[row, column];
            if (off < 1e-18) break;
            for (int p = 0; p < size; p++) for (int q = p + 1; q < size; q++)
            {
                if (System.Math.Abs(matrix[p, q]) < 1e-300) continue;
                double theta = (matrix[q, q] - matrix[p, p]) / (2.0 * matrix[p, q]), t = System.Math.Sign(theta == 0 ? 1 : theta) / (System.Math.Abs(theta) + System.Math.Sqrt(theta * theta + 1.0)), c = 1.0 / System.Math.Sqrt(t * t + 1.0), sn = t * c;
                for (int k = 0; k < size; k++) { double kp = matrix[k, p], kq = matrix[k, q]; matrix[k, p] = c * kp - sn * kq; matrix[k, q] = sn * kp + c * kq; }
                for (int k = 0; k < size; k++) { double pk = matrix[p, k], qk = matrix[q, k]; matrix[p, k] = c * pk - sn * qk; matrix[q, k] = sn * pk + c * qk; }
            }
        }
        var values = new double[size]; for (int i = 0; i < size; i++) values[i] = matrix[i, i]; System.Array.Sort(values); return values;
    }

    // @description - lowest nonzero natural frequency of the linear chain with the clutch locked, damper main stage, no lash, no engine roll (the closed-form check of the research report):
    //   DOFs crank+disc, input shaft, carrier, wheels (lumped), vehicle; springs: damper stage 2, mesh, combined halfshafts, tire patch (patch spring k = C_x F_z / sigma)
    // @param {CarParams} carParams - car parameters
    // @param {ShuffleDrivelineParameters} parameters - model parameters
    // @param {double} ratio - total ratio
    // @return {double} - Hz
    static double EigenFrequency(CarParams carParams, ShuffleDrivelineParameters parameters, double ratio)
    {
        double[] inertia = { carParams.engineInertia + parameters.clutchDiscInertiaKilogramMetersSquared, parameters.inputShaftInertiaKilogramMetersSquared, parameters.carrierInertiaKilogramMetersSquared, 2.0 * carParams.wheelInertia, carParams.mass };
        double damperStiffness = parameters.damperStageStiffnessNewtonMetersPerDegree[1] * Mathf.Rad2Deg, meshStiffness = parameters.gearMeshStiffnessAtCarrierNewtonMetersPerRadian, shaftStiffness = parameters.CombinedShaftStiffness();
        double tireStiffness = 2.0 * carParams.longitudinalStiffnessCoefficient * carParams.mass * 9.81 * rigFrontWeightFraction * 0.5 / carParams.relaxationLength, radius = carParams.wheelRadius;
        // coordinates: theta_e, theta_in, theta_d, theta_w, x ; springs as vectors of coefficients
        var stiffness = new double[5, 5];
        void AddSpring(double k, double a, int i, double b, int j) { double[] coefficient = new double[5]; coefficient[i] += a; coefficient[j] += b; for (int r = 0; r < 5; r++) for (int c = 0; c < 5; c++) stiffness[r, c] += k * coefficient[r] * coefficient[c]; }
        AddSpring(damperStiffness, 1, 0, -1, 1);
        AddSpring(meshStiffness, 1.0 / ratio, 1, -1, 2);
        AddSpring(shaftStiffness, 1, 2, -1, 3);
        AddSpring(tireStiffness, radius, 3, -1, 4);
        var scaled = new double[5, 5]; for (int r = 0; r < 5; r++) for (int c = 0; c < 5; c++) scaled[r, c] = stiffness[r, c] / System.Math.Sqrt(inertia[r] * inertia[c]);
        double[] eigenvalues = JacobiEigenvalues(scaled); double lowest = 0.0; foreach (double value in eigenvalues) if (value > 1e-3) { lowest = value; break; }
        return System.Math.Sqrt(lowest) / (2.0 * System.Math.PI);
    }

    // Test double of the engine side: a constant crank torque, no auxiliary inertia (proves the interface is all the driveline needs)
    class ConstantTorqueSource : ICrankshaftTorqueSource
    {
        public double torque; double crankSpeedAtEnd;
        public double AuxiliaryInertiaKilogramMetersSquared => 0.0;
        public void BeginInterval(double throttleFraction, double intervalSeconds) { }
        public double TorqueAt(double crankAngleRadians, double crankAngularVelocity) => torque;
        public void EndInterval(double crankAngularVelocity) { crankSpeedAtEnd = crankAngularVelocity; }
    }

    // @description - runs the torsional chain alone against the linear vehicle rig (two wheels, tire patch springs, vehicle mass) with a constant-torque engine
    // @param {CarParams} carParams - car parameters
    // @param {ShuffleDrivelineParameters} parameters - model parameters
    // @param {int} gear - gear 1..5
    // @param {double} initialCrankSpeed - rad/s
    // @param {double} torque - crank torque in N m applied from t = 0
    // @param {double} clampFraction - clutch clamp 0..1
    // @param {double} seconds - run time in s
    // @param {bool} patchDamping - include the speed-dependent patch relaxation damping (false = pure patch spring, for the eigenfrequency check)
    // @param {System.Action<double, TorsionalDriveline>} sample - called every outer step with time and chain
    // @param {double} initialSlipSpeed - initial crank speed minus wheel speed times ratio in rad/s (0 = locked start)
    // @return {Void}
    static void RunChainOnly(CarParams carParams, ShuffleDrivelineParameters parameters, int gear, double initialCrankSpeed, double torque, double clampFraction, double seconds, bool patchDamping, System.Action<double, TorsionalDriveline> sample, double initialSlipSpeed = 0.0)
    {
        var drivetrain = new Drivetrain(carParams); drivetrain.SelectGear(gear); drivetrain.Tick(1f); double ratio = drivetrain.TotalRatio;
        var chain = new TorsionalDriveline(parameters, carParams); double wheelSpeed = (initialCrankSpeed - initialSlipSpeed) / ratio; chain.Initialize(initialCrankSpeed, wheelSpeed);
        double vehicleSpeed = wheelSpeed * carParams.wheelRadius, leftDeflection = 0.0, rightDeflection = 0.0, wheelLeft = wheelSpeed, wheelRight = wheelSpeed;
        double patchStiffness = carParams.longitudinalStiffnessCoefficient * carParams.mass * 9.81 * rigFrontWeightFraction * 0.5 / carParams.relaxationLength;
        var source = new ConstantTorqueSource { torque = torque }; int steps = (int)(seconds / substep);
        for (int step = 0; step < steps; step++)
        {
            double relaxationRate = patchDamping ? (System.Math.Abs(vehicleSpeed) + rigMinimumSpeedForRelaxation) / carParams.relaxationLength : 0.0;
            var input = new DrivelineStepInput { crankshaftTorqueSource = source, clutchClampFraction = clampFraction, totalRatioSigned = ratio, wheelInertiaKilogramMetersSquared = carParams.wheelInertia,
                leftWheelAngularVelocity = wheelLeft, rightWheelAngularVelocity = wheelRight, leftTireResistingTorque = patchStiffness * leftDeflection * carParams.wheelRadius, rightTireResistingTorque = patchStiffness * rightDeflection * carParams.wheelRadius, drivetrainEfficiency = carParams.drivetrainEfficiency };
            var result = chain.Advance(input, substep);
            wheelLeft += (result.leftWheelAverageTorque - input.leftTireResistingTorque) / carParams.wheelInertia * substep; wheelRight += (result.rightWheelAverageTorque - input.rightTireResistingTorque) / carParams.wheelInertia * substep;
            leftDeflection += (wheelLeft * carParams.wheelRadius - vehicleSpeed - relaxationRate * leftDeflection) * substep; rightDeflection += (wheelRight * carParams.wheelRadius - vehicleSpeed - relaxationRate * rightDeflection) * substep;
            vehicleSpeed += patchStiffness * (leftDeflection + rightDeflection) / carParams.mass * substep;
            sample?.Invoke((step + 1) * substep, chain);
        }
    }

    // @description - first mode of the chain alone: simulated (DFT of the carrier speed after a crank torque step) against the linear eigenfrequency of the same chain, per gear.
    //   Linear case (damper friction off: with friction the damper locks below about twice its friction torque and the small-amplitude frequency rises; 60 N m step keeps the damper twist swing inside stage 2, 5..16 deg; a larger step reaches the stiff stop and raises the frequency): clutch locked, damper main stage, no lash, no engine roll, patch spring without relaxation damping. Criterion 2 percent in the research plan; 4 percent allowed here
    //   because the DFT of a finite record resolves about 1 percent and the damper stage is only approximately constant.
    // @return {Void}
    [Test] public void SimulatedModeMatchesTheLinearEigenfrequency()
    {
        var carParams = ScriptableObject.CreateInstance<CarParams>(); carParams.drivetrainEfficiency = 1f; var parameters = ShuffleDrivelineParameters.CreateDefault();   // efficiency 1: the linear eigenproblem is symmetric only without the mesh loss factor
        parameters.engineRollEnabled = false; parameters.gearLashAtInputShaftDegrees = 0f; parameters.shaftAndJointLashAtWheelDegrees = 0f; parameters.shaftStructuralLossFactor = 0.002f; parameters.damperStageHysteresisNewtonMeters = new float[] { 0f, 0f, 0f };
        for (int gear = 1; gear <= 5; gear++)
        {
            var samples = new System.Collections.Generic.List<float>();
            RunChainOnly(carParams, parameters, gear, 150.0, 60.0, 1.0, 6.0, false, (time, chain) => samples.Add((float)chain.CarrierAngularVelocity));
            var drivetrain = new Drivetrain(carParams); drivetrain.SelectGear(gear);
            float simulated = DominantFrequency(samples, samples.Count / 20, 1f, 25f); double predicted = EigenFrequency(carParams, parameters, drivetrain.TotalRatio);
            report.AppendLine($"eigen vs simulation, gear {gear}: eigenvalue {predicted:0.00} Hz, simulated {simulated:0.00} Hz, difference {100.0 * (simulated - predicted) / predicted:0.0} %");
            Assert.AreEqual(predicted, simulated, 0.04 * predicted, $"gear {gear}: simulated mode must match the linear eigenfrequency");
        }
    }

    // @description - switching the engine roll degree of freedom on must lower the first-gear frequency (Gaudin: 2.4 to 2.1 Hz for a comparable mount) and not raise it
    // @return {Void}
    [Test] public void EngineRollLowersTheFirstMode()
    {
        float without = FirstGearFrequency(false), with = FirstGearFrequency(true);
        report.AppendLine($"engine roll: first gear {without:0.00} Hz without, {with:0.00} Hz with ({100f * (with - without) / without:0.0} %)");
        Assert.Less(with, without + 0.02f, "engine roll must not raise the first mode"); Assert.Greater(with, 0.7f * without, "engine roll must not collapse the first mode");
    }

    // @description - first-gear first-mode frequency of the chain alone with or without the engine roll degree of freedom
    // @param {bool} engineRoll - include the roll degree of freedom
    // @return {float} - Hz
    static float FirstGearFrequency(bool engineRoll)
    {
        var carParams = ScriptableObject.CreateInstance<CarParams>(); var parameters = ShuffleDrivelineParameters.CreateDefault();
        parameters.engineRollEnabled = engineRoll; parameters.gearLashAtInputShaftDegrees = 0f; parameters.shaftAndJointLashAtWheelDegrees = 0f; parameters.shaftStructuralLossFactor = 0.002f; parameters.damperStageHysteresisNewtonMeters = new float[] { 0f, 0f, 0f };
        var samples = new System.Collections.Generic.List<float>(); RunChainOnly(carParams, parameters, 1, 150.0, 60.0, 1.0, 6.0, false, (time, chain) => samples.Add((float)chain.CarrierAngularVelocity));
        return DominantFrequency(samples, samples.Count / 20, 1f, 25f);
    }

    // @description - judder: a held partial clamp with a prescribed crank speed (very large crank inertia) at launch. The growth of the carrier-speed oscillation between an early and a late window
    //   is measured for a sweep of the friction-versus-speed slope. Criteria: pure Coulomb (slope 0) decays; a strongly falling slope grows; the growth ratio rises monotonically as the slope falls.
    //   The threshold slope is reported; it is NOT compared with the linearized criterion F R mu' = -c (not done, see report).
    // @return {Void}
    [Test] public void JudderGrowsOnlyWithAFallingFrictionSlope()
    {
        float[] slopes = { 0f, -0.005f, -0.01f, -0.02f, -0.05f }; float previousRatio = 0f; float firstRatio = 0f, lastRatio = 0f;
        foreach (float slope in slopes)
        {
            var carParams = ScriptableObject.CreateInstance<CarParams>(); carParams.engineInertia = 100f; var parameters = ShuffleDrivelineParameters.CreateDefault();
            parameters.frictionSlopePerMeterPerSecond = slope; parameters.engineRollEnabled = false;
            var early = new System.Collections.Generic.List<float>(); var late = new System.Collections.Generic.List<float>();
            RunChainOnly(carParams, parameters, 1, 150.0, 0.0, 0.10, 3.0, true, (time, chain) => { if (time > 0.3 && time < 0.8) early.Add((float)chain.CarrierAngularVelocity); if (time > 2.0 && time < 2.5) late.Add((float)chain.CarrierAngularVelocity); }, 150.0);
            float ratio = RootMeanSquare(Detrend(late)) / Mathf.Max(RootMeanSquare(Detrend(early)), 1e-6f);
            report.AppendLine($"judder sweep: friction slope {slope:0.000}/(m/s): late/early oscillation {ratio:0.00}");
            if (slope == 0f) { firstRatio = ratio; Assert.Less(ratio, 1f, "pure Coulomb friction must not judder"); }
            Assert.GreaterOrEqual(ratio, previousRatio * 0.9f, "growth must not fall as the slope becomes more negative"); previousRatio = ratio; lastRatio = ratio;
        }
        Assert.Greater(lastRatio, 1.5f, "a strongly falling slope must make the oscillation grow"); Assert.Greater(lastRatio, 2f * firstRatio, "growth must clearly exceed the Coulomb case");
    }

    // @description - rms of a signal
    // @param {System.Collections.Generic.List<float>} values - samples
    // @return {float} - rms
    static float RootMeanSquare(System.Collections.Generic.List<float> values) { double sum = 0.0; foreach (float value in values) sum += value * value; return values.Count > 0 ? (float)System.Math.Sqrt(sum / values.Count) : 0f; }

    // @description - writes the time traces behind the diagnostics plot when SCIROCCO_DRIVELINE_DIR is set: per gear a throttle step from a steady 2500 rpm (vehicle rig), a tip-out,
    //   a clutch dump in first gear, and one revolution of firing ripple. Does nothing (and passes) when the variable is not set.
    // @return {Void}
    [Test] public void WriteDiagnosticTracesWhenRequested()
    {
        string directory = System.Environment.GetEnvironmentVariable("SCIROCCO_DRIVELINE_DIR"); if (string.IsNullOrEmpty(directory)) { Assert.Pass("SCIROCCO_DRIVELINE_DIR not set"); return; }
        System.IO.Directory.CreateDirectory(directory); var culture = System.Globalization.CultureInfo.InvariantCulture;
        for (int gear = 1; gear <= 5; gear++)
        {
            var model = Build(out var carParams, out var parameters, out var engine, out var drivetrain, out var left, out var right); drivetrain.SelectGear(gear); drivetrain.Tick(1f);
            float wheelSpeed = 2500f * Mathf.PI / 30f / drivetrain.TotalRatio; left.angularVelocity = right.angularVelocity = wheelSpeed; rigVehicleSpeed = wheelSpeed * carParams.wheelRadius;
            engine.SetRunning(true); engine.EndExternalCrankInterval(wheelSpeed * drivetrain.TotalRatio); var lines = new System.Collections.Generic.List<string> { "time_s,throttle,wheel_torque_Nm,engine_rpm,vehicle_speed_mps,shaft_deflection_deg,clutch_slip_rad_s" };
            float throttle = 0.15f; float time0 = 0f;
            System.Action<float> record = time => { if (Mathf.RoundToInt(time / substep) % 4 == 0) lines.Add(string.Join(",", new[] { (time0 + time).ToString("0.0000", culture), throttle.ToString("0.00", culture), left.driveTorque.ToString("0.00", culture), engine.rpm.ToString("0.0", culture), rigVehicleSpeed.ToString("0.0000", culture), (model.Chain.ShaftDeflectionRadians * Mathf.Rad2Deg).ToString("0.0000", culture), drivetrain.clutchSlip.ToString("0.000", culture) })); };
            Run(model, engine, drivetrain, left, right, 1.0f, 0.15f, 0f, record); time0 = 1.0f; throttle = 0.8f;
            Run(model, engine, drivetrain, left, right, 2.0f, 0.8f, 0f, record); time0 = 3.0f; throttle = 0f;
            Run(model, engine, drivetrain, left, right, 2.0f, 0f, 0f, record);
            System.IO.File.WriteAllLines(System.IO.Path.Combine(directory, $"shuffle_gear{gear}.csv"), lines);
        }
        {
            var model = Build(out var carParams, out var parameters, out var engine, out var drivetrain, out var left, out var right); drivetrain.SelectGear(1); drivetrain.Tick(1f); engine.SetRunning(true);
            var lines = new System.Collections.Generic.List<string> { "time_s,wheel_torque_Nm,engine_rpm,clutch_slip_rad_s" }; float time0 = 0f;
            System.Action<float> record = time => { if (Mathf.RoundToInt(time / substep) % 2 == 0) lines.Add(string.Join(",", new[] { (time0 + time).ToString("0.0000", culture), left.driveTorque.ToString("0.00", culture), engine.rpm.ToString("0.0", culture), drivetrain.clutchSlip.ToString("0.000", culture) })); };
            Run(model, engine, drivetrain, left, right, 0.5f, 0.5f, 1f, record); time0 = 0.5f; Run(model, engine, drivetrain, left, right, 1.5f, 0.5f, 0f, record);
            System.IO.File.WriteAllLines(System.IO.Path.Combine(directory, "clutch_dump_gear1.csv"), lines);
        }
        {
            var model = Build(out var carParams, out var parameters, out var engine, out var drivetrain, out var left, out var right); var firing = model.TorqueSource.FiringPulses;
            var lines = new System.Collections.Generic.List<string> { "crank_angle_deg,ripple_Nm_3000rpm_wot,ripple_Nm_3000rpm_closed" }; double speed = 3000.0 * Mathf.PI / 30.0;
            var wide = new System.Collections.Generic.List<double>(); var closed = new System.Collections.Generic.List<double>();
            firing.SetOperatingPoint(carParams.ambientPressure, carParams.exhaustBackPressure, carParams.ambientPressure, 160f); for (int i = 0; i < 720; i++) wide.Add(firing.RippleTorque(i * Mathf.Deg2Rad, speed));
            firing.SetOperatingPoint(0.3 * carParams.ambientPressure, carParams.exhaustBackPressure, carParams.ambientPressure, 15f); for (int i = 0; i < 720; i++) closed.Add(firing.RippleTorque(i * Mathf.Deg2Rad, speed));
            for (int i = 0; i < 720; i++) lines.Add($"{i},{wide[i].ToString("0.00", culture)},{closed[i].ToString("0.00", culture)}");
            System.IO.File.WriteAllLines(System.IO.Path.Combine(directory, "firing_ripple.csv"), lines);
        }
    }

    // @description - prints the collected measurements
    // @return {Void}
    [OneTimeTearDown] public void PrintReport() { Debug.Log("DRIVELINE VALIDATION\n" + report); }
}
