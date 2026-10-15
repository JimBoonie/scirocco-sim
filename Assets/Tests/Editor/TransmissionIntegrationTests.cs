// Vehicle-in-the-loop validation of the EXPERIMENTAL transmission: the four toggle combinations, Off -> On -> Off isolation, engine/clutch/synchronizer shifts (auto-clutch assist, shift-success
// window vs clutch pedal and rev mismatch), per-gear efficiency energy balance in both power directions, lash ownership. The whole-car bit-identity proof is the 15-scenario hash run.
using NUnit.Framework; using UnityEngine;

public class TransmissionIntegrationTests
{
    const float Substep = 0.0005f;   // s, the outer tire/drivetrain substep of CarController
    const double FrontWeightFraction = 0.60, MinimumSpeedForRelaxation = 0.5;
    const string DiagnosticsFolder = "/workspace/scirocco_sim/diagnostics/transmission";

    // @description - vehicle-in-the-loop rig (test only): the car as a rigid mass on two driven tires with the sim's relaxation tire model, plus the engine, gearbox and whichever model the flags select
    class Rig
    {
        public CarParams carParams; public Engine engine; public Drivetrain drivetrain; public WheelCorner left, right; public ExperimentalDriveline driveline; public ExperimentalTransmission transmission;
        public double vehicleSpeed, leftDeflection, rightDeflection;

        // @description - builds a rig with the given toggles
        // @param {bool} transmissionOn - experimentalTransmissionEnabled
        // @param {bool} drivelineOn - experimentalDrivelineEnabled
        public Rig(bool transmissionOn, bool drivelineOn)
        {
            carParams = ScriptableObject.CreateInstance<CarParams>(); engine = new Engine(carParams); drivetrain = new Drivetrain(carParams);
            left = new GameObject("left").AddComponent<WheelCorner>(); right = new GameObject("right").AddComponent<WheelCorner>(); left.carParams = right.carParams = carParams; left.force = right.force = Vector2.zero;
            carParams.experimentalTransmissionEnabled = transmissionOn; carParams.experimentalDrivelineEnabled = drivelineOn;
        }

        // @description - puts the car in a gear rolling at an engine speed with the engine running
        // @param {int} gear - gear
        // @param {float} engineRpm - rpm
        // @return {Void}
        public void Roll(int gear, float engineRpm)
        {
            drivetrain.SelectGear(gear); drivetrain.Tick(1f); float wheelSpeed = engineRpm * Mathf.PI / 30f / Mathf.Abs(drivetrain.TotalRatio);
            left.angularVelocity = right.angularVelocity = wheelSpeed; vehicleSpeed = wheelSpeed * carParams.wheelRadius; engine.SetRunning(true); engine.EndExternalCrankInterval(engineRpm * Mathf.PI / 30f);
        }

        // @description - rig tire force of one wheel
        // @param {WheelCorner} wheel - wheel
        // @param {double} deflection - patch deflection (updated)
        // @return {float} - N
        float TireForce(WheelCorner wheel, ref double deflection)
        {
            double load = carParams.mass * 9.81 * FrontWeightFraction * 0.5, relaxation = carParams.relaxationLength;
            deflection += (wheel.angularVelocity * carParams.wheelRadius - vehicleSpeed - (System.Math.Abs(vehicleSpeed) + MinimumSpeedForRelaxation) * deflection / relaxation) * Substep;
            return (float)(carParams.longitudinalStiffnessCoefficient * load / relaxation * deflection);
        }

        // @description - one substep exactly as CarController dispatches it for the current flags
        // @param {float} throttle - 0..1
        // @param {float} pedal - manual clutch 0..1
        // @param {bool} autoClutch - keyboard auto-clutch
        // @param {float} autoLaw - existing auto-clutch law output 0..1
        // @return {Void}
        public void Step(float throttle, float pedal, bool autoClutch = false, float autoLaw = 0f)
        {
            drivetrain.Tick(Substep); left.force.x = TireForce(left, ref leftDeflection); right.force.x = TireForce(right, ref rightDeflection);
            if (!carParams.experimentalTransmissionEnabled && transmission != null) transmission.Deactivate(left, right);
            if (carParams.experimentalTransmissionEnabled)
            {
                if (transmission == null) transmission = new ExperimentalTransmission(engine, carParams, null);
                if (driveline != null) driveline.Deactivate();
                transmission.Advance(Substep, throttle, pedal, autoClutch, autoLaw, drivetrain, left, right);
            }
            else if (carParams.experimentalDrivelineEnabled) { if (driveline == null) driveline = new ExperimentalDriveline(engine, carParams, null); driveline.Advance(Substep, throttle, pedal, autoLaw, drivetrain, left, right); }
            else
            {
                if (driveline != null) driveline.Deactivate();
                float clutchTorque = drivetrain.Clutch(engine.Omega, (left.angularVelocity + right.angularVelocity) * 0.5f, Mathf.Max(pedal, autoLaw), Substep, carParams.wheelInertia, out float wheelTorque);
                engine.Step(throttle, clutchTorque, Substep); left.driveTorque = right.driveTorque = wheelTorque;
            }
            float inertiaLeft = carParams.wheelInertia + left.DriveInertia, inertiaRight = carParams.wheelInertia + right.DriveInertia;
            vehicleSpeed += (left.force.x + right.force.x) / carParams.mass * Substep;
            left.angularVelocity += (left.driveTorque - left.force.x * carParams.wheelRadius) / inertiaLeft * Substep; right.angularVelocity += (right.driveTorque - right.force.x * carParams.wheelRadius) / inertiaRight * Substep;
        }

        // @description - runs a number of seconds
        // @param {float} seconds - s
        // @param {float} throttle - 0..1
        // @param {float} pedal - 0..1
        // @param {bool} autoClutch - option
        // @param {float} autoLaw - law output
        // @return {Void}
        public void Run(float seconds, float throttle, float pedal, bool autoClutch = false, float autoLaw = 0f) { int steps = (int)(seconds / Substep); for (int step = 0; step < steps; step++) Step(throttle, pedal, autoClutch, autoLaw); }

        // @description - runs until a condition holds or the time runs out
        // @param {System.Func<bool>} done - stop condition
        // @param {float} seconds - limit
        // @param {float} throttle - 0..1
        // @param {float} pedal - 0..1
        // @param {bool} autoClutch - option
        // @param {float} autoLaw - law output
        // @return {float} - elapsed s
        public float RunUntil(System.Func<bool> done, float seconds, float throttle, float pedal, bool autoClutch = false, float autoLaw = 0f)
        { int steps = (int)(seconds / Substep), count = 0; while (count < steps && !done()) { Step(throttle, pedal, autoClutch, autoLaw); count++; } return count * Substep; }
    }

    // @description - Verifies all four toggle combinations accelerate the car in 3rd gear to nearly the same speed, with the engine locked to the wheels and no NaN.
    // @return {Void}
    [Test] public void AllFourToggleCombinationsRun()
    {
        float[] finalSpeeds = new float[4]; bool[,] combinations = { { false, false }, { true, false }, { false, true }, { true, true } };
        for (int index = 0; index < 4; index++)
        {
            var rig = new Rig(combinations[index, 0], combinations[index, 1]); rig.Roll(3, 3000f); rig.Run(2f, 1f, 0f);
            float ratio = rig.drivetrain.TotalRatio, wheelSpeed = 0.5f * (rig.left.angularVelocity + rig.right.angularVelocity);
            Assert.IsFalse(float.IsNaN(rig.engine.Omega) || float.IsNaN(wheelSpeed), $"combination {index}"); Assert.AreEqual(rig.engine.Omega, wheelSpeed * ratio, 0.03f * rig.engine.Omega, $"engine locked to the wheels, combination {index}");
            finalSpeeds[index] = (float)rig.vehicleSpeed; Debug.Log($"TRANSMISSION VALIDATION combination transmission={combinations[index, 0]} driveline={combinations[index, 1]}: 2 s WOT in 3rd from 3000 rpm -> {rig.vehicleSpeed:0.00} m/s, engine {rig.engine.rpm:0} rpm");
        }
        for (int index = 1; index < 4; index++) Assert.AreEqual(finalSpeeds[0], finalSpeeds[index], 0.06f * finalSpeeds[0], $"combination {index} differs by only the modeled losses and dynamics");
    }

    // @description - copies every instance field (public and private) from one object to another of the same type
    // @param {object} source - object to read
    // @param {object} destination - object to overwrite
    // @return {Void}
    static void CopyAllFields(object source, object destination)
    {
        for (var type = source.GetType(); type != null && type != typeof(object); type = type.BaseType)
            foreach (var field in type.GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.DeclaredOnly)) field.SetValue(destination, field.GetValue(source));
    }

    // @description - FNV-1a over the bits of the physical state the default path owns
    // @param {Rig} rig - rig
    // @return {ulong} - hash
    static ulong Hash(Rig rig)
    {
        ulong hash = 14695981039346656037UL; float[] values = { rig.engine.Omega, rig.engine.manifoldPressureRatio, rig.drivetrain.clutchSlip, rig.left.angularVelocity, rig.right.angularVelocity, rig.left.driveTorque, rig.right.driveTorque, rig.left.DriveInertia, rig.right.DriveInertia, (float)rig.vehicleSpeed };
        foreach (float value in values) { int bits = System.BitConverter.SingleToInt32Bits(value); for (int i = 0; i < 4; i++) { hash ^= (ulong)((bits >> (8 * i)) & 0xFF); hash *= 1099511628211UL; } }
        return hash;
    }

    // @description - Verifies Off -> On -> Off isolation for each toggle and for both: after the models are switched off the default path continues bit for bit from the same physical state as a control rig
    //   that never had a model on (engine, gearbox, wheels, tire patches copied at the switch-off instant; DriveInertia must be zero again).
    // @return {Void}
    [Test] public void OffOnOffLeavesNoResidueForEachToggleAndBoth()
    {
        bool[,] combinations = { { true, false }, { false, true }, { true, true } };
        for (int index = 0; index < 3; index++)
        {
            var rig = new Rig(false, false); rig.Roll(3, 2500f); rig.Run(0.2f, 0.6f, 0f);
            rig.carParams.experimentalTransmissionEnabled = combinations[index, 0]; rig.carParams.experimentalDrivelineEnabled = combinations[index, 1]; rig.Run(0.4f, 0.6f, 0f);
            rig.carParams.experimentalTransmissionEnabled = false; rig.carParams.experimentalDrivelineEnabled = false; rig.Step(0.6f, 0f);   // the first default step runs Deactivate
            var control = new Rig(false, false); CopyAllFields(rig.engine, control.engine); CopyAllFields(rig.drivetrain, control.drivetrain);
            control.left.angularVelocity = rig.left.angularVelocity; control.right.angularVelocity = rig.right.angularVelocity; control.vehicleSpeed = rig.vehicleSpeed; control.leftDeflection = rig.leftDeflection; control.rightDeflection = rig.rightDeflection; control.left.force = rig.left.force; control.right.force = rig.right.force;
            Assert.AreEqual(0f, rig.left.DriveInertia, $"DriveInertia reset, combination {index}"); Assert.AreEqual(0f, rig.right.DriveInertia);
            for (int step = 0; step < 800; step++) { rig.Step(0.6f, 0f); control.Step(0.6f, 0f); }
            Assert.AreEqual(Hash(control), Hash(rig), $"default path after Off-On-Off must equal a fresh default continuation, combination {index} (transmission {combinations[index, 0]}, driveline {combinations[index, 1]})");
        }
    }

    // @description - builds a rig rolling in 2nd gear at 3000 rpm, then shifts to 3rd with the keyboard auto-clutch assist at wide-open throttle; the clutch is opened by the assist, the synchronizer finishes and the gear is in.
    //   The same shift with the clutch left engaged (assist off) does not engage.
    // @return {Void}
    [Test] public void AutoClutchAssistLetsTheSynchronizerFinish()
    {
        foreach (bool withDriveline in new[] { false, true })
        {
            var rig = new Rig(true, withDriveline); rig.Roll(2, 3000f); rig.Run(0.3f, 1f, 0f, true, 0f); var model = rig.transmission.Model; Assert.AreEqual(2, model.EngagedGear, "the model starts in the gear the lever is in");
            rig.drivetrain.SelectGear(3); float time = rig.RunUntil(() => model.EngagedGear == 3 && model.Phase == GearboxPhase.Engaged, 3f, 1f, 0f, true, 0f);
            Assert.AreEqual(3, model.EngagedGear, $"assisted upshift engages (driveline {withDriveline})"); Assert.AreEqual(0, model.GrindCount, "no grinding"); Assert.AreEqual(0, model.ScratchCount);
            Debug.Log($"TRANSMISSION VALIDATION assisted 2->3 at WOT (driveline {withDriveline}): gear in after {time:0.000} s, synchronization {model.LastSynchronizationSeconds:0.000} s, cone dissipation {model.LastConeDissipationJoules:0.0} J");
        }
        var bare = new Rig(true, false); bare.Roll(2, 3000f); bare.Run(0.3f, 1f, 0f); bare.drivetrain.SelectGear(3); bare.RunUntil(() => bare.transmission.Model.EngagedGear == 3, 2f, 1f, 0f);
        Assert.AreNotEqual(3, bare.transmission.Model.EngagedGear, "with the clutch engaged at wide-open throttle the gear stays in (torque across the dogs) or the ring blocks");
    }

    // @description - one shift attempt for the success window: neutral, clutch pedal held, engine started at a rev mismatch to the speed 3rd gear needs, closed throttle
    // @param {float} pedal - clutch pedal 0..1
    // @param {float} mismatchFraction - (engine speed - matched speed) / matched speed
    // @param {float} throttle - 0..1
    // @param {float} seconds - time allowed
    // @param {float} engagedAtSeconds - (out) time the gear was in, or -1
    // @param {int} grinds - (out) clash count
    // @return {bool} - true when 3rd is engaged within the time
    static bool ShiftAttempt(float pedal, float mismatchFraction, float throttle, float seconds, out float engagedAtSeconds, out int grinds)
    {
        var rig = new Rig(true, false); rig.Roll(3, 3000f); float matchedSpeed = rig.engine.Omega; rig.drivetrain.SelectGear(0); rig.Run(0.01f, 0f, 1f);   // neutral: model goes to Neutral (it starts in 3rd, releases at zero torque)
        rig.engine.EndExternalCrankInterval(matchedSpeed * (1f + mismatchFraction)); rig.transmission.AdapterA.SetNodeSpeed(matchedSpeed * (1f + mismatchFraction));
        rig.drivetrain.SelectGear(3); engagedAtSeconds = rig.RunUntil(() => rig.transmission.Model.EngagedGear == 3 && rig.transmission.Model.Phase == GearboxPhase.Engaged, seconds, throttle, pedal);
        grinds = rig.transmission.Model.GrindCount; bool success = rig.transmission.Model.EngagedGear == 3; if (!success) engagedAtSeconds = -1f; return success;
    }

    static readonly float[] PedalPositions = { 0f, 0.2f, 0.3f, 0.36f, 0.38f, 0.40f, 0.42f, 0.44f, 0.46f, 0.48f, 0.50f, 0.52f, 0.6f, 0.8f, 1f };   // dense around the bite range of the pedal curve (bite begins 0.38, released at 0.50)

    // @description - Computes the shift-success window over clutch pedal position and rev mismatch for closed throttle and a held throttle, writes the CSV files to the diagnostics folder (a script draws the plot), and checks the physics:
    //   a released clutch with a modest mismatch succeeds, a fully engaged clutch with a big mismatch does not.
    // @return {Void}
    [Test] public void ShiftSuccessWindowOverClutchPedalAndRevMismatch()
    {
        System.IO.Directory.CreateDirectory(DiagnosticsFolder); float[] throttles = { 0f, 0.3f };
        for (int throttleIndex = 0; throttleIndex < throttles.Length; throttleIndex++)
        {
            var lines = new System.Collections.Generic.List<string> { "clutch_pedal,rev_mismatch_percent,success,time_s,grinds,throttle" };
            foreach (float pedal in PedalPositions) for (int mismatchIndex = -6; mismatchIndex <= 6; mismatchIndex++)
            {
                float mismatch = mismatchIndex * 0.05f; bool success = ShiftAttempt(pedal, mismatch, throttles[throttleIndex], 1.5f, out float time, out int grinds);
                lines.Add(string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0:0.00},{1:0.0},{2},{3:0.000},{4},{5:0.00}", pedal, mismatch * 100f, success ? 1 : 0, time, grinds, throttles[throttleIndex]));
            }
            System.IO.File.WriteAllLines(DiagnosticsFolder + $"/shift_window_throttle_{(int)(throttles[throttleIndex] * 100):000}.csv", lines);
        }
        Assert.IsTrue(ShiftAttempt(1f, 0.10f, 0f, 1.5f, out _, out _), "clutch released, 10 % over: the engine side is decoupled, the synchronizer matches the input shaft");
        Assert.IsFalse(ShiftAttempt(0f, -0.30f, 0f, 1.5f, out _, out _), "clutch engaged, engine 30 % too slow: the cone cannot speed the engine up against its friction (a downshift needs a blip)");
        Assert.IsTrue(ShiftAttempt(0f, 0.10f, 0f, 1.5f, out _, out _), "clutch engaged, engine 10 % too fast at closed throttle: the cone and the engine friction slow the engine to the matched speed");
    }

    // @description - Verifies per-gear efficiency in both power directions with the speed-independent drags removed: the loss is (1 - eta) x the power in on power and (1 / eta - 1) x the power in on overrun
    //   (efficiency applies on power, divides on overrun, not counted twice), for every gear and several speeds; and with the drags back the total loss is never negative.
    // @return {Void}
    [Test] public void MeshEfficiencyEnergyBalanceBothDirections()
    {
        foreach (bool withDrags in new[] { false, true }) foreach (int gear in new[] { 1, 2, 3, 4, 5, -1 }) foreach (float wheelSpeed in new[] { 20f, 60f, 120f }) foreach (float slipSign in new[] { 1f, -1f })
        {
            var parameters = TransmissionParameters.CreateDefault();
            if (!withDrags) { parameters.spinLossScaleForThisGearbox = 0f; parameters.looseGearRollingDragNewtonMeters = 0f; parameters.looseGearViscousDragNewtonMetersSecondPerRadian = 0f; parameters.outputPinionBearingTurningTorqueNewtonMeters = 0f; parameters.differentialBearingTurningTorqueNewtonMeters = 0f; }
            var rig = new Rig(true, false); rig.transmission = new ExperimentalTransmission(rig.engine, rig.carParams, parameters); rig.drivetrain.SelectGear(gear); rig.drivetrain.Tick(1f);
            float signedWheelSpeed = gear < 0 ? -wheelSpeed : wheelSpeed, ratio = (float)rig.transmission.Model.Geometry.TotalRatio(gear); rig.left.angularVelocity = rig.right.angularVelocity = signedWheelSpeed;
            rig.engine.SetRunning(true); rig.engine.EndExternalCrankInterval(Mathf.Abs(ratio * signedWheelSpeed) * (1f + 0.02f * slipSign));
            rig.transmission.Advance(Substep, 0f, 0f, false, 0f, rig.drivetrain, rig.left, rig.right);   // first step: the model starts in gear and ties
            float engineSpeed = rig.engine.Omega, wheelDriveBefore = rig.left.driveTorque; double inputSpeed = ratio * signedWheelSpeed;
            rig.transmission.Advance(Substep, 0f, 0f, false, 0f, rig.drivetrain, rig.left, rig.right);
            double intoGearTrain = rig.transmission.AdapterA.GearTrainTorque, powerIn = intoGearTrain * inputSpeed, powerWheels = 2.0 * rig.left.driveTorque * signedWheelSpeed, efficiency = rig.transmission.Model.Geometry.MeshEfficiency(gear);
            double loss = powerIn - powerWheels; Assert.GreaterOrEqual(loss, -1e-6 * (System.Math.Abs(powerIn) + 1.0), $"energy created: gear {gear} wheel {wheelSpeed} sign {slipSign} drags {withDrags}");
            if (!withDrags && System.Math.Abs(powerIn) > 1.0)
            {
                double expected = powerIn > 0.0 ? (1.0 - efficiency) * powerIn : (1.0 / efficiency - 1.0) * System.Math.Abs(powerIn);
                Assert.AreEqual(expected, System.Math.Abs(loss), 2e-3 * System.Math.Abs(powerIn) + 1e-3, $"loss formula: gear {gear} wheel {wheelSpeed} sign {slipSign}");
            }
        }
    }

    // @description - Verifies lash is owned by the transmission only: with the transmission lash and dog play at zero the torsional chain adds none (the mesh deflection stays elastic), and with the default transmission lash the deflection span grows by the lash referred to the carrier.
    // @return {Void}
    [Test] public void LashIsCountedOnceWhenBothToggleAreOn()
    {
        double[] spans = new double[2];
        for (int variant = 0; variant < 2; variant++)
        {
            var parameters = TransmissionParameters.CreateDefault(); if (variant == 0) { parameters.normalBacklashMillimeters = 0f; parameters.dogAngularLashDegrees = 0f; }
            var rig = new Rig(true, true); rig.transmission = new ExperimentalTransmission(rig.engine, rig.carParams, parameters); rig.Roll(3, 2500f); rig.Run(0.3f, 0.3f, 0f);
            double minimum = double.MaxValue, maximum = double.MinValue; var chain = rig.transmission.AdapterB.Driveline.Chain;
            for (int step = 0; step < 4000; step++) { float throttle = step < 1000 ? 0.8f : step < 2000 ? 0f : 0.8f; rig.Step(throttle, 0f); minimum = System.Math.Min(minimum, chain.MeshDeflectionRadians); maximum = System.Math.Max(maximum, chain.MeshDeflectionRadians); }
            spans[variant] = maximum - minimum;
        }
        double ratio = System.Math.Abs(ScriptableObject.CreateInstance<CarParams>().gearRatios[2] * ScriptableObject.CreateInstance<CarParams>().finalDrive), expectedLash = 2.0 * new GearSetGeometry(TransmissionParameters.CreateDefault(), ScriptableObject.CreateInstance<CarParams>().finalDrive).HalfLashAtInputShaft(3) / ratio;
        Assert.AreEqual(expectedLash, spans[1] - spans[0], 0.35 * expectedLash, "the extra deflection span equals the transmission's lash referred to the carrier"); Debug.Log($"TRANSMISSION VALIDATION lash: span without lash {spans[0]:0.00000} rad, with {spans[1]:0.00000} rad, expected extra {expectedLash:0.00000} rad");
    }
}
