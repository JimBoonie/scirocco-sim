// Toggle isolation of the EXPERIMENTAL driveline model: after Off -> On -> Off the default path must continue bit-for-bit from the same physical state,
// i.e. the experimental objects leave no residue that the default Drivetrain.Clutch + Engine.Step path reads. (The whole-car proof is the per-step float-bit hash
// over the 15 diagnostic scenarios; this test covers the engine/drivetrain/wheel-speed state that the toggle can touch.)
using NUnit.Framework; using UnityEngine;

public class DrivelineToggleTests
{
    const float substep = 0.0005f;

    // @description - copies every instance field (public and private) from one object to another of the same type
    // @param {object} source - object to read
    // @param {object} destination - object to overwrite
    // @return {Void}
    static void CopyAllFields(object source, object destination)
    {
        for (var type = source.GetType(); type != null && type != typeof(object); type = type.BaseType)
            foreach (var field in type.GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.DeclaredOnly))
                field.SetValue(destination, field.GetValue(source));
    }

    // @description - one default-path substep exactly as CarController runs it (Drivetrain.Clutch, Engine.Step, wheel torque) with a simple wheel integration
    // @param {Engine} engine - engine
    // @param {Drivetrain} drivetrain - gearbox state
    // @param {float[]} wheelSpeed - two-element wheel speed array in rad/s (updated)
    // @param {CarParams} carParams - parameters
    // @param {float} throttle - pedal 0..1
    // @param {float} clutchPedal - pedal 0..1
    // @return {Void}
    static void DefaultStep(Engine engine, Drivetrain drivetrain, float[] wheelSpeed, CarParams carParams, float throttle, float clutchPedal)
    {
        drivetrain.Tick(substep);
        float clutchTorque = drivetrain.Clutch(engine.Omega, (wheelSpeed[0] + wheelSpeed[1]) * 0.5f, clutchPedal, substep, carParams.wheelInertia, out float wheelTorque);
        engine.Step(throttle, clutchTorque, substep);
        wheelSpeed[0] += (wheelTorque - 20f) / carParams.wheelInertia * substep; wheelSpeed[1] += (wheelTorque - 20f) / carParams.wheelInertia * substep;   // 20 N m constant tire/road resistance
    }

    // @description - FNV-1a over the bits of the physical state the default path owns
    // @param {Engine} engine - engine
    // @param {Drivetrain} drivetrain - gearbox
    // @param {float[]} wheelSpeed - wheel speeds
    // @return {ulong} - hash
    static ulong Hash(Engine engine, Drivetrain drivetrain, float[] wheelSpeed)
    {
        ulong hash = 14695981039346656037UL;
        foreach (float value in new[] { engine.Omega, engine.manifoldPressureRatio, drivetrain.clutchSlip, wheelSpeed[0], wheelSpeed[1] })
        { int bits = System.BitConverter.SingleToInt32Bits(value); for (int i = 0; i < 4; i++) { hash ^= (ulong)((bits >> (8 * i)) & 0xFF); hash *= 1099511628211UL; } }
        return hash;
    }

    // @description - Off for 1 s, On for 1 s, Off for 1 s (state restored at the switch back) must equal an uninterrupted default run from the same state, bit for bit
    // @return {Void}
    [Test] public void OffOnOffLeavesNoResidue()
    {
        var carParams = ScriptableObject.CreateInstance<CarParams>(); var engineA = new Engine(carParams); var drivetrainA = new Drivetrain(carParams); var wheelsA = new float[2];
        var engineB = new Engine(carParams); var drivetrainB = new Drivetrain(carParams); var wheelsB = new float[2];
        drivetrainA.SelectGear(2); drivetrainA.Tick(1f); drivetrainB.SelectGear(2); drivetrainB.Tick(1f);
        int phaseSteps = (int)(1f / substep);
        for (int i = 0; i < phaseSteps; i++) { DefaultStep(engineA, drivetrainA, wheelsA, carParams, 0.6f, i < 400 ? 1f : 0f); DefaultStep(engineB, drivetrainB, wheelsB, carParams, 0.6f, i < 400 ? 1f : 0f); }
        Assert.AreEqual(Hash(engineA, drivetrainA, wheelsA), Hash(engineB, drivetrainB, wheelsB), "identical default runs must agree");
        // snapshot of the physical state at the switch, then the experimental model runs on copy B and is switched off again
        var snapshotEngine = new Engine(carParams); var snapshotDrivetrain = new Drivetrain(carParams); CopyAllFields(engineB, snapshotEngine); CopyAllFields(drivetrainB, snapshotDrivetrain);
        var snapshotWheels = (float[])wheelsB.Clone();
        var parameters = ShuffleDrivelineParameters.CreateDefault(); var experimental = new ExperimentalDriveline(engineB, carParams, parameters);
        var left = new GameObject("l").AddComponent<WheelCorner>(); var right = new GameObject("r").AddComponent<WheelCorner>(); left.carParams = right.carParams = carParams;
        for (int i = 0; i < phaseSteps; i++)
        {
            left.angularVelocity = wheelsB[0]; right.angularVelocity = wheelsB[1]; left.force = right.force = new Vector2(20f / carParams.wheelRadius, 0f);
            experimental.Advance(substep, 0.6f, 0f, 0f, drivetrainB, left, right);
            wheelsB[0] += (left.driveTorque - 20f) / carParams.wheelInertia * substep; wheelsB[1] += (right.driveTorque - 20f) / carParams.wheelInertia * substep;
        }
        experimental.Deactivate();
        Assert.AreNotEqual(Hash(engineA, drivetrainA, wheelsA), Hash(engineB, drivetrainB, wheelsB), "the experimental model must actually change the physical state while on (otherwise the test proves nothing)");
        CopyAllFields(snapshotEngine, engineB); CopyAllFields(snapshotDrivetrain, drivetrainB); wheelsB[0] = snapshotWheels[0]; wheelsB[1] = snapshotWheels[1];
        for (int i = 0; i < phaseSteps; i++) { DefaultStep(engineA, drivetrainA, wheelsA, carParams, 0.6f, 0f); DefaultStep(engineB, drivetrainB, wheelsB, carParams, 0.6f, 0f); if (Hash(engineA, drivetrainA, wheelsA) != Hash(engineB, drivetrainB, wheelsB)) Assert.Fail($"default path diverged {i} steps after switching back"); }
    }

    // @description - the parameter asset in Resources loads and equals the code defaults field by field
    // @return {Void}
    [Test] public void ParameterAssetMatchesTheDefaults()
    {
        var loaded = Resources.Load<ShuffleDrivelineParameters>("ShuffleDrivelineParameters"); Assert.IsNotNull(loaded, "Assets/Resources/ShuffleDrivelineParameters.asset must exist");
        Assert.AreEqual(JsonUtility.ToJson(ShuffleDrivelineParameters.CreateDefault()), JsonUtility.ToJson(loaded), "asset differs from the documented defaults");
    }
}
