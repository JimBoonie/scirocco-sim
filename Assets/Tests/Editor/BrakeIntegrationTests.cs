// Integration tests of the EXPERIMENTAL brakes: toggle isolation (Off -> On -> Off leaves no residue; the default branch text is unchanged) and the stopping-distance scenario comparison
// with the experimental model on (a longitudinal two-axle simulation with the project's Pacejka tire). The whole-car proof of the default path is the per-step float-bit hash over the 15 diagnostic scenarios.
// The tire is NOT tuned to the period stopping distance: the default tire stops from 100 km/h in about 39 m where period road tests (C/D 1982 and 1986, 70-0 mph 192 and 196 ft) show about 46.5 m.
using NUnit.Framework; using UnityEngine; using System.Collections.Generic; using System.Globalization; using System.IO; using System.Text;

public class BrakeIntegrationTests
{
    const float Substep = 0.0005f;   // s, the 2 kHz substep of CarController
    const double StandardGravity = 9.80665;   // m/s^2 (S)
    const double StartSpeedMetersPerSecond = 100.0 / 3.6;   // 100 km/h
    const double SimulationStepSeconds = 1.0e-4;   // s, small enough for the stiff tire slip dynamics of the stopping simulation
    const double EndSpeedMetersPerSecond = 1.0;   // m/s, the simulation ends here and the rest of the stop is extrapolated at constant deceleration
    const double IdleManifoldPressurePascals = 31000.0;   // Pa

    enum BrakeScheme { LegacyFixedTorque, KeyboardPress, LoadCellSeventyPounds, LoadCellDeadBooster }

    // @description - result of one simulated stop
    class StopResult { public double distanceMeters, timeSeconds, peakDecelerationG; public bool rearLocked, frontLocked; public List<string> rows = new List<string>(); }

    // @description - copies every instance field (public and private) from one object to another of the same type
    // @param {object} source - object to read
    // @param {object} destination - object to overwrite
    // @return {Void} - nothing
    static void CopyAllFields(object source, object destination)
    {
        for (var type = source.GetType(); type != null && type != typeof(object); type = type.BaseType)
            foreach (var field in type.GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.DeclaredOnly))
                field.SetValue(destination, field.GetValue(source));
    }

    // @description - FNV-1a mix of float bits
    // @param {ulong} hash - running hash
    // @param {float} value - value to mix in
    // @return {ulong} - new hash
    static ulong Mix(ulong hash, float value)
    {
        int bits = System.BitConverter.SingleToInt32Bits(value);
        for (int byteIndex = 0; byteIndex < 4; byteIndex++) { hash ^= (ulong)((bits >> (8 * byteIndex)) & 0xFF); hash *= 1099511628211UL; }
        return hash;
    }

    // @description - hash of the state the default brake path can touch: engine speed and manifold pressure, wheel speeds and the four brake torques
    // @param {Engine} engine - engine
    // @param {float[]} wheelSpeeds - four wheel speeds, rad/s
    // @param {float[]} brakeTorques - four brake torques, N m
    // @return {ulong} - hash
    static ulong Hash(Engine engine, float[] wheelSpeeds, float[] brakeTorques)
    {
        ulong hash = 14695981039346656037UL; hash = Mix(hash, engine.Omega); hash = Mix(hash, engine.manifoldPressureRatio);
        foreach (float value in wheelSpeeds) hash = Mix(hash, value);
        foreach (float value in brakeTorques) hash = Mix(hash, value);
        return hash;
    }

    // @description - one default-path brake substep exactly as CarController runs it, plus a simple wheel and engine integration
    // @param {Engine} engine - engine (idle, clutch out)
    // @param {float[]} wheelSpeeds - four wheel speeds FL, FR, RL, RR in rad/s (updated)
    // @param {float[]} brakeTorques - four brake torques (written)
    // @param {CarParams} carParams - parameters
    // @param {float} brake - brake axis 0..1
    // @return {Void} - nothing
    static void DefaultBrakeStep(Engine engine, float[] wheelSpeeds, float[] brakeTorques, CarParams carParams, float brake)
    {
        engine.Step(0.1f, 0f, Substep);
        brakeTorques[0] = brakeTorques[1] = brake * carParams.brakeTorqueFront; brakeTorques[2] = brakeTorques[3] = Mathf.Max(0f * carParams.handbrakeTorque, brake * carParams.brakeTorqueRear);
        IntegrateWheels(wheelSpeeds, brakeTorques, carParams);
    }

    // @description - wheel speed update under brake torque with a constant 20 N m tire resistance (wheels stop at zero)
    // @param {float[]} wheelSpeeds - four wheel speeds in rad/s (updated)
    // @param {float[]} brakeTorques - four brake torques, N m
    // @param {CarParams} carParams - parameters
    // @return {Void} - nothing
    static void IntegrateWheels(float[] wheelSpeeds, float[] brakeTorques, CarParams carParams)
    {
        for (int wheelIndex = 0; wheelIndex < 4; wheelIndex++) wheelSpeeds[wheelIndex] = Mathf.Max(0f, wheelSpeeds[wheelIndex] + (800f - brakeTorques[wheelIndex] - 20f) / carParams.wheelInertia * Substep);
    }

    // @description - one experimental-path brake substep exactly as CarController applies it (torques, manifold mass back to the engine)
    // @param {ExperimentalBrakes} brakes - brake system
    // @param {Engine} engine - engine
    // @param {float[]} wheelSpeeds - four wheel speeds (updated)
    // @param {float[]} brakeTorques - four brake torques (written)
    // @param {CarParams} carParams - parameters
    // @param {float} brake - brake axis 0..1
    // @return {Void} - nothing
    static void ExperimentalBrakeStep(ExperimentalBrakes brakes, Engine engine, float[] wheelSpeeds, float[] brakeTorques, CarParams carParams, float brake)
    {
        engine.Step(0.1f, 0f, Substep);
        var input = new BrakeStepInput { pedalAxis = brake, manifoldPressurePascals = engine.ManifoldPressurePascals, manifoldVolumeCubicMeters = carParams.manifoldVolume };
        BrakeStepResult result = brakes.Step(input, Substep);
        engine.AddExternalManifoldMass((float)result.manifoldMassReturnedKilograms);
        brakeTorques[0] = (float)result.frontLeftTorqueNewtonMeters; brakeTorques[1] = (float)result.frontRightTorqueNewtonMeters; brakeTorques[2] = (float)result.rearLeftTorqueNewtonMeters; brakeTorques[3] = (float)result.rearRightTorqueNewtonMeters;
        IntegrateWheels(wheelSpeeds, brakeTorques, carParams);
    }

    // @description - Off for 1 s, On for 1 s, Off for 1 s (state restored at the switch back) must equal an uninterrupted default run from the same state, bit for bit; the experimental model must really change the state while on
    // @return {Void} - nothing
    [Test] public void OffOnOffLeavesNoResidue()
    {
        var carParams = ScriptableObject.CreateInstance<CarParams>(); var engineA = new Engine(carParams); var engineB = new Engine(carParams); engineA.SetRunning(true); engineB.SetRunning(true);
        var wheelsA = new float[] { 60f, 60f, 60f, 60f }; var wheelsB = (float[])wheelsA.Clone(); var torquesA = new float[4]; var torquesB = new float[4]; int phaseSteps = (int)(1f / Substep);
        for (int stepIndex = 0; stepIndex < phaseSteps; stepIndex++) { float brake = stepIndex < 800 ? 0.6f : 0f; DefaultBrakeStep(engineA, wheelsA, torquesA, carParams, brake); DefaultBrakeStep(engineB, wheelsB, torquesB, carParams, brake); }
        Assert.AreEqual(Hash(engineA, wheelsA, torquesA), Hash(engineB, wheelsB, torquesB), "identical default runs must agree");
        var snapshotEngine = new Engine(carParams); CopyAllFields(engineB, snapshotEngine); var snapshotWheels = (float[])wheelsB.Clone();
        var experimental = new ExperimentalBrakes(carParams, BrakeSystemParameters.CreateDefault());
        for (int stepIndex = 0; stepIndex < phaseSteps; stepIndex++) { ExperimentalBrakeStep(experimental, engineB, wheelsB, torquesB, carParams, stepIndex < 1200 ? 0.8f : 0f); }
        experimental.Reset();
        Assert.AreNotEqual(Hash(engineA, wheelsA, torquesA), Hash(engineB, wheelsB, torquesB), "the experimental model must actually change the physical state while on (otherwise the test proves nothing)");
        CopyAllFields(snapshotEngine, engineB); wheelsB = (float[])snapshotWheels.Clone();
        for (int stepIndex = 0; stepIndex < phaseSteps; stepIndex++)
        {
            float brake = stepIndex < 600 ? 0.3f : 0f; DefaultBrakeStep(engineA, wheelsA, torquesA, carParams, brake); DefaultBrakeStep(engineB, wheelsB, torquesB, carParams, brake);
            Assert.AreEqual(Hash(engineA, wheelsA, torquesA), Hash(engineB, wheelsB, torquesB), "default path diverged after Off -> On -> Off at step " + stepIndex);
        }
    }

    // @description - the default branch in CarController still contains the two original brake torque statements unchanged, and the experimental call sits in its own branch
    // @return {Void} - nothing
    [Test] public void DefaultBranchTextIsUnchanged()
    {
        string source = File.ReadAllText(Path.Combine(Application.dataPath, "Scripts/Runtime/CarController.cs"));
        StringAssert.Contains("frontLeftWheel.brakeTorque = frontRightWheel.brakeTorque = brake * carParams.brakeTorqueFront;", source);
        StringAssert.Contains("float handbrakeFraction = Mathf.Max(handbrake ? 1f : 0f, handbrakeInput); rearLeftWheel.brakeTorque = rearRightWheel.brakeTorque = Mathf.Max(handbrakeFraction * carParams.handbrakeTorque, brake * carParams.brakeTorqueRear);", source);
        StringAssert.Contains("if (carParams.experimentalBrakesEnabled) ApplyExperimentalBrakes(substep);", source);
    }

    // @description - the toggles default to off everywhere, so a default install runs the standard brakes
    // @return {Void} - nothing
    [Test] public void BrakeTogglesDefaultToOff()
    {
        Assert.IsFalse(ScriptableObject.CreateInstance<CarParams>().experimentalBrakesEnabled); Assert.IsFalse(new BindingProfile().experimentalBrakesEnabled); Assert.IsFalse(InputMapper.Defaults().experimentalBrakesEnabled);
    }

    // @description - builds the brake torque source of a scheme at a time step (legacy fixed torques or the experimental model)
    // @param {BrakeScheme} scheme - brake scheme
    // @param {CarParams} carParams - parameters
    // @return {ExperimentalBrakes} - experimental brakes, or null for the legacy scheme
    static ExperimentalBrakes BuildSchemeBrakes(BrakeScheme scheme, CarParams carParams)
    {
        if (scheme == BrakeScheme.LegacyFixedTorque) return null;
        var brakes = new ExperimentalBrakes(carParams, BrakeSystemParameters.CreateDefault()); return brakes;
    }

    // @description - simulates a straight-line stop from 100 km/h with two axles (static weight transfer from the center of gravity height) and the project's Pacejka longitudinal tire, with no ABS
    // @param {BrakeScheme} scheme - which brake model and pedal input to use
    // @return {StopResult} - distance, time, peak deceleration and lock-up flags, plus CSV rows every 10 ms
    static StopResult SimulateStop(BrakeScheme scheme)
    {
        var carParams = ScriptableObject.CreateInstance<CarParams>(); var brakes = BuildSchemeBrakes(scheme, carParams); var result = new StopResult();
        double speed = StartSpeedMetersPerSecond, distance = 0.0, time = 0.0, lastDeceleration = 0.0, frontWheelSpeed = speed / carParams.wheelRadius, rearWheelSpeed = frontWheelSpeed, nextRowTime = 0.0;
        double frontStaticShare = (carParams.wheelbase - carParams.cgFromFront) / carParams.wheelbase, manifoldPressure = scheme == BrakeScheme.LoadCellDeadBooster ? 101325.0 : IdleManifoldPressurePascals;
        brakes?.Booster.Reset(manifoldPressure);
        while (speed > EndSpeedMetersPerSecond && time < 20.0)
        {
            BrakeStepResult brakeResult = StepBrakes(scheme, brakes, carParams, manifoldPressure, out double frontTorque, out double rearTorque);
            double transfer = lastDeceleration * carParams.mass * carParams.cgHeight / carParams.wheelbase, frontLoad = 0.5 * (carParams.mass * StandardGravity * frontStaticShare + transfer), rearLoad = 0.5 * (carParams.mass * StandardGravity * (1.0 - frontStaticShare) - transfer);
            double frontForce = -PacejkaTire.PureLong(carParams, (float)SlipRatio(frontWheelSpeed * carParams.wheelRadius, speed), (float)frontLoad), rearForce = -PacejkaTire.PureLong(carParams, (float)SlipRatio(rearWheelSpeed * carParams.wheelRadius, speed), (float)rearLoad);
            double deceleration = 2.0 * (frontForce + rearForce) / carParams.mass;
            frontWheelSpeed = System.Math.Max(0.0, frontWheelSpeed + (-frontTorque + frontForce * carParams.wheelRadius) / carParams.wheelInertia * SimulationStepSeconds); rearWheelSpeed = System.Math.Max(0.0, rearWheelSpeed + (-rearTorque + rearForce * carParams.wheelRadius) / carParams.wheelInertia * SimulationStepSeconds);
            speed -= deceleration * SimulationStepSeconds; distance += speed * SimulationStepSeconds; time += SimulationStepSeconds; lastDeceleration = deceleration;
            result.peakDecelerationG = System.Math.Max(result.peakDecelerationG, deceleration / StandardGravity);
            if (time >= nextRowTime) { nextRowTime += 0.01; result.rows.Add(Row(time, speed, deceleration / StandardGravity, brakeResult, frontTorque, rearTorque, frontWheelSpeed * carParams.wheelRadius, rearWheelSpeed * carParams.wheelRadius)); }
            if (speed > 5.0) { result.frontLocked |= frontWheelSpeed * carParams.wheelRadius < 0.05 * speed; result.rearLocked |= rearWheelSpeed * carParams.wheelRadius < 0.05 * speed; }
        }
        result.distanceMeters = distance + speed * speed / (2.0 * System.Math.Max(lastDeceleration, 1.0)); result.timeSeconds = time + speed / System.Math.Max(lastDeceleration, 1.0);
        return result;
    }

    // @description - longitudinal slip ratio of a wheel (negative when braking)
    // @param {double} wheelSurfaceSpeed - wheel angular speed times rolling radius, m/s
    // @param {double} vehicleSpeed - vehicle speed, m/s
    // @return {double} - slip ratio, clamped to -1
    static double SlipRatio(double wheelSurfaceSpeed, double vehicleSpeed) => System.Math.Max((wheelSurfaceSpeed - vehicleSpeed) / System.Math.Max(vehicleSpeed, 0.5), -1.0);

    // @description - brake torques of the scheme for one simulation step
    // @param {BrakeScheme} scheme - brake scheme
    // @param {ExperimentalBrakes} brakes - experimental brakes (null for the legacy scheme)
    // @param {CarParams} carParams - parameters
    // @param {double} manifoldPressure - manifold pressure, Pa
    // @param {double} frontTorque - front torque per wheel (out)
    // @param {double} rearTorque - rear torque per wheel (out)
    // @return {BrakeStepResult} - full brake result (default for the legacy scheme)
    static BrakeStepResult StepBrakes(BrakeScheme scheme, ExperimentalBrakes brakes, CarParams carParams, double manifoldPressure, out double frontTorque, out double rearTorque)
    {
        if (scheme == BrakeScheme.LegacyFixedTorque) { frontTorque = carParams.brakeTorqueFront; rearTorque = carParams.brakeTorqueRear; return default; }
        bool loadCell = scheme != BrakeScheme.KeyboardPress;
        var input = new BrakeStepInput { pedalAxis = 1.0, loadCellPedal = loadCell, loadCellMaxForceNewtons = 70.0 * BrakeSystemParameters.NewtonsPerPoundForce, manifoldPressurePascals = manifoldPressure, manifoldVolumeCubicMeters = carParams.manifoldVolume };
        BrakeStepResult result = brakes.Step(input, SimulationStepSeconds); frontTorque = result.frontLeftTorqueNewtonMeters; rearTorque = result.rearLeftTorqueNewtonMeters; return result;
    }

    // @description - one CSV row of the stopping log
    // @param {double} time - s
    // @param {double} speed - m/s
    // @param {double} decelerationG - g
    // @param {BrakeStepResult} brakeResult - brake result
    // @param {double} frontTorque - N m per wheel
    // @param {double} rearTorque - N m per wheel
    // @param {double} frontSurfaceSpeed - front wheel surface speed, m/s
    // @param {double} rearSurfaceSpeed - rear wheel surface speed, m/s
    // @return {string} - CSV row
    static string Row(double time, double speed, double decelerationG, BrakeStepResult brakeResult, double frontTorque, double rearTorque, double frontSurfaceSpeed, double rearSurfaceSpeed)
    {
        var culture = CultureInfo.InvariantCulture;
        return string.Join(",", System.Array.ConvertAll(new[] { time, speed, decelerationG, brakeResult.pedalForceNewtons, brakeResult.masterPressurePascals / 1.0e5, brakeResult.boosterAssistForceNewtons, frontTorque, rearTorque, frontSurfaceSpeed, rearSurfaceSpeed }, value => value.ToString("0.#####", culture)));
    }

    // @description - writes a CSV file under diagnostics/brakes of the project
    // @param {string} fileName - file name
    // @param {string} header - CSV header line
    // @param {IEnumerable<string>} rows - CSV rows
    // @return {Void} - nothing
    static void WriteDiagnostics(string fileName, string header, IEnumerable<string> rows)
    {
        string directory = Path.GetFullPath(Path.Combine(Application.dataPath, "../diagnostics/brakes")); Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, fileName), header + "\n" + string.Join("\n", rows) + "\n", new UTF8Encoding(false));
    }

    // @description - stopping-distance scenario comparison with the experimental model on: legacy fixed torques, a keyboard press (foot speed and booster lag), a 70 lb load-cell pedal and a dead booster. Reports the distances; the tire is not tuned.
    // @return {Void} - nothing
    [Test] public void StoppingDistanceComparisonWithTheExperimentalModelOn()
    {
        StopResult legacy = SimulateStop(BrakeScheme.LegacyFixedTorque), keyboard = SimulateStop(BrakeScheme.KeyboardPress), loadCell = SimulateStop(BrakeScheme.LoadCellSeventyPounds), dead = SimulateStop(BrakeScheme.LoadCellDeadBooster);
        string header = "time_s,speed_ms,deceleration_g,pedal_force_N,master_bar,booster_assist_N,front_torque_Nm,rear_torque_Nm,front_surface_speed_ms,rear_surface_speed_ms";
        WriteDiagnostics("stop_legacy.csv", header, legacy.rows); WriteDiagnostics("stop_keyboard.csv", header, keyboard.rows); WriteDiagnostics("stop_loadcell70.csv", header, loadCell.rows); WriteDiagnostics("stop_deadbooster.csv", header, dead.rows);
        WriteDiagnostics("stop_summary.csv", "scheme,distance_m,time_s,peak_deceleration_g,front_locked,rear_locked", new[] { SummaryRow("legacy", legacy), SummaryRow("keyboard_full_press", keyboard), SummaryRow("loadcell_70lb", loadCell), SummaryRow("dead_booster_70lb", dead) });
        Debug.Log($"STOP 100-0 km/h: legacy {legacy.distanceMeters:0.0} m, keyboard {keyboard.distanceMeters:0.0} m, load cell 70 lb {loadCell.distanceMeters:0.0} m, dead booster {dead.distanceMeters:0.0} m (period road tests about 46.5 m)");
        Assert.That(legacy.distanceMeters, Is.InRange(36.0, 50.0), "the simple two-axle model with the default tire stops in the range of the full-car scenarios (39.3 m without rear lock-up, about 47 m with it)");
        Assert.Greater(keyboard.distanceMeters, legacy.distanceMeters, "foot speed and booster lag add distance to an instant step"); Assert.Less(keyboard.distanceMeters, legacy.distanceMeters + 8.0, "but only a few meters");
        Assert.AreEqual(legacy.peakDecelerationG, keyboard.peakDecelerationG, 0.05 * legacy.peakDecelerationG, "the full-press stop is tire-limited like the legacy stop, not brake-limited");
        Assert.Less(loadCell.distanceMeters, legacy.distanceMeters + 8.0, "70 lb is enough to stop near the tire limit with the booster");
        Assert.Greater(dead.distanceMeters, loadCell.distanceMeters + 5.0, "a dead booster at the same 70 lb stops much later"); Assert.Less(dead.peakDecelerationG, 0.9 * loadCell.peakDecelerationG);
    }

    // @description - one summary CSV row
    // @param {string} name - scheme name
    // @param {StopResult} stop - result
    // @return {string} - CSV row
    static string SummaryRow(string name, StopResult stop) => string.Format(CultureInfo.InvariantCulture, "{0},{1:0.00},{2:0.00},{3:0.000},{4},{5}", name, stop.distanceMeters, stop.timeSeconds, stop.peakDecelerationG, stop.frontLocked, stop.rearLocked);

    // @description - pedal-response table: steady pedal force against master pressure, front and rear torque and the ideal-braking decel estimate, for a healthy, a half-vacuum and a dead booster; written to diagnostics/brakes/pedal_response.csv for the plot
    // @return {Void} - nothing
    [Test] public void PedalResponseTableIsMonotoneAndWritten()
    {
        var rows = new List<string>(); var vacuumLevels = new[] { ("healthy_70kPa_vacuum", IdleManifoldPressurePascals), ("half_vacuum", 0.5 * (IdleManifoldPressurePascals + 101325.0)), ("dead_booster", 101325.0) };
        foreach (var (label, manifoldPressure) in vacuumLevels)
        {
            double previousPressure = -1.0;
            for (double pedalForce = 0.0; pedalForce <= 700.0; pedalForce += 10.0)
            {
                var brakes = new ExperimentalBrakes(ScriptableObject.CreateInstance<CarParams>(), BrakeSystemParameters.CreateDefault());
                var input = new BrakeStepInput { pedalAxis = pedalForce / 700.0, loadCellPedal = true, loadCellMaxForceNewtons = 700.0, manifoldPressurePascals = manifoldPressure, manifoldVolumeCubicMeters = 2.5e-3 }; BrakeStepResult result = default;
                for (int stepIndex = 0; stepIndex < 4000; stepIndex++) result = brakes.Step(input, 0.0005);
                Assert.GreaterOrEqual(result.masterPressurePascals, previousPressure - 1.0, label + ": pressure never falls with more pedal force"); previousPressure = result.masterPressurePascals;
                rows.Add(string.Format(CultureInfo.InvariantCulture, "{0},{1:0},{2:0.00},{3:0.0},{4:0.0}", label, pedalForce, result.masterPressurePascals / 1.0e5, result.frontLeftTorqueNewtonMeters, result.rearLeftTorqueNewtonMeters));
            }
        }
        WriteDiagnostics("pedal_response.csv", "case,pedal_force_N,master_bar,front_torque_Nm,rear_torque_Nm", rows);
    }
}
