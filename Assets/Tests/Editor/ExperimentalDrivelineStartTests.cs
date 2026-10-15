// Starting the engine with the EXPERIMENTAL driveline toggle on. Regression for "the car does not start with the Experimental driveline ON": the zero-mean compression ripple of
// the firing-pulse model (about +-100 N m at 1 bar) exceeded the starter stall torque (about 105 N m falling with speed), so a crank that had stopped in front of a compression
// hump (about 60 % of rest angles, e.g. every stall) could never be cranked through it. The default path has no ripple and always started.
using NUnit.Framework; using UnityEngine;

public class ExperimentalDrivelineStartTests
{
    const float substep = 0.0005f;                        // s, outer drivetrain substep of CarController
    const float minimumIdleRpm = 700f, maximumIdleRpm = 1100f;   // rpm band that counts as a started, idling engine (idle is 900 rpm)

    // Everything one start attempt needs, created fresh per attempt.
    class Rig
    {
        public CarParams carParams; public Engine engine; public Drivetrain drivetrain; public WheelCorner left, right; public ExperimentalDriveline model;
    }

    // @description - builds an engine, gearbox, two held wheels and the experimental driveline (parameters from the shipped asset, as CarController does)
    // @return {Rig} - the rig, engine stopped with the ignition on
    static Rig BuildRig()
    {
        var rig = new Rig { carParams = ScriptableObject.CreateInstance<CarParams>() };
        rig.engine = new Engine(rig.carParams); rig.drivetrain = new Drivetrain(rig.carParams);
        rig.left = new GameObject("left wheel").AddComponent<WheelCorner>(); rig.right = new GameObject("right wheel").AddComponent<WheelCorner>();
        rig.left.carParams = rig.carParams; rig.right.carParams = rig.carParams; rig.left.force = Vector2.zero; rig.right.force = Vector2.zero;
        rig.model = new ExperimentalDriveline(rig.engine, rig.carParams, ShuffleDrivelineParameters.LoadOrDefault());
        rig.engine.SetRunning(false); rig.engine.ignition = true; return rig;
    }

    // @description - destroys the wheel objects of a rig
    // @param {Rig} rig - rig to dispose
    // @return {Void}
    static void DisposeRig(Rig rig) { Object.DestroyImmediate(rig.left.gameObject); Object.DestroyImmediate(rig.right.gameObject); }

    // @description - advances the rig with the wheels held still (a parked car): the tire force equals the drive torque over the wheel radius
    // @param {Rig} rig - rig
    // @param {float} seconds - simulated time in s
    // @param {float} clutchPedal - clutch pedal 0..1
    // @param {bool} starterUntilEnd - true = hold the starter for the whole time, false = never
    // @return {Void}
    static void RunHeld(Rig rig, float seconds, float clutchPedal, bool starterUntilEnd)
    {
        int steps = (int)(seconds / substep);
        for (int step = 0; step < steps; step++)
        {
            rig.engine.starter = starterUntilEnd;
            rig.left.force.x = rig.left.driveTorque / rig.carParams.wheelRadius; rig.right.force.x = rig.right.driveTorque / rig.carParams.wheelRadius;
            rig.model.Advance(substep, 0f, clutchPedal, 0f, rig.drivetrain, rig.left, rig.right);
            rig.left.angularVelocity = rig.right.angularVelocity = 0f;
        }
    }

    // @description - cranks the stopped engine for 2 s, releases the starter and lets it idle for 4 s
    // @param {Rig} rig - rig (engine stopped)
    // @return {Void}
    static void CrankAndRelease(Rig rig, float clutchPedal) { RunHeld(rig, 2f, clutchPedal, true); RunHeld(rig, 4f, clutchPedal, false); }

    // @description - sets the crank angle of the torsional chain (the angle at which a stopped crank came to rest is arbitrary)
    // @param {Rig} rig - rig
    // @param {float} crankAngleDegrees - angle in deg, 0..720
    // @return {Void}
    static void SetCrankAngle(Rig rig, float crankAngleDegrees)
    {
        rig.model.Advance(substep, 0f, 0f, 0f, rig.drivetrain, rig.left, rig.right);   // initializes the chain before the angle is overwritten
        typeof(TorsionalDriveline).GetField("crankAngle", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).SetValue(rig.model.Chain, (double)(crankAngleDegrees * Mathf.Deg2Rad));
    }

    // @description - asserts the engine started and idles
    // @param {Rig} rig - rig after the start attempt
    // @param {string} label - what was tried, for the failure message
    // @return {Void}
    static void AssertIdling(Rig rig, string label)
    {
        Assert.IsTrue(rig.engine.combusting, label + ": engine is not combusting, rpm " + rig.engine.rpm);
        Assert.That(rig.engine.rpm, Is.InRange(minimumIdleRpm, maximumIdleRpm), label);
    }

    // @description - the engine starts from every crank rest angle (period 180 deg for four cylinders) in neutral
    // @return {Void}
    [Test] public void StartsFromEveryCrankRestAngle()
    {
        for (int angle = 0; angle < 180; angle += 10)
        {
            var rig = BuildRig(); SetCrankAngle(rig, angle); CrankAndRelease(rig, 0f); AssertIdling(rig, "rest angle " + angle + " deg"); DisposeRig(rig);
        }
    }

    // @description - the engine starts in neutral and in gear with the clutch pedal down, and in neutral with the clutch up
    // @return {Void}
    [Test] public void StartsInNeutralAndInGearWithClutchIn()
    {
        foreach (var (gear, clutchPedal) in new[] { (0, 0f), (0, 1f), (1, 1f), (2, 1f) })
        {
            var rig = BuildRig(); rig.drivetrain.SelectGear(gear); rig.drivetrain.Tick(1f); CrankAndRelease(rig, clutchPedal); AssertIdling(rig, "gear " + gear + " clutch " + clutchPedal); DisposeRig(rig);
        }
    }

    // @description - an engine stalled in gear (clutch engaged, wheels held) restarts after selecting neutral, as the stallrestart scenario does
    // @return {Void}
    [Test] public void RestartsAfterStallInGear()
    {
        var rig = BuildRig(); rig.engine.SetRunning(true); rig.drivetrain.SelectGear(1); rig.drivetrain.Tick(1f);
        RunHeld(rig, 3f, 0f, false);
        Assert.IsFalse(rig.engine.combusting, "the held engine in gear with the clutch up should have stalled");
        rig.drivetrain.SelectGear(0); rig.drivetrain.Tick(1f); CrankAndRelease(rig, 0f); AssertIdling(rig, "restart after stall"); DisposeRig(rig);
    }

    // @description - the motored ripple fraction is 0 at rest, 1/2 at the half-speed parameter and 1 when the engine fires
    // @return {Void}
    [Test] public void GasRippleFractionFollowsCrankSpeed()
    {
        var rig = BuildRig(); var parameters = rig.model.Parameters; var source = rig.model.TorqueSource;
        double halfSpeed = parameters.crankingLeakageHalfSpeedRpm * Mathf.PI / 30.0;
        Assert.AreEqual(0.0, source.GasRippleFraction(0.0), 1e-12); Assert.AreEqual(0.5, source.GasRippleFraction(halfSpeed), 1e-12);
        rig.engine.combusting = true; Assert.AreEqual(1.0, source.GasRippleFraction(0.0), 1e-12); Assert.AreEqual(1.0, source.GasRippleFraction(halfSpeed), 1e-12);
        DisposeRig(rig);
    }

    // @description - a firing engine keeps the full ripple: torque minus the unscaled ripple is the same mean at every crank angle, so running behavior is unchanged by the starting fix
    // @return {Void}
    [Test] public void FiringEngineKeepsFullRipple()
    {
        var rig = BuildRig(); rig.engine.SetRunning(true); var source = rig.model.TorqueSource; source.BeginInterval(0.0, substep); const double speed = 94.0;   // rad/s, about 900 rpm
        double firstMean = source.TorqueAt(1.1, speed) - source.FiringPulses.RippleTorque(1.1, speed), secondMean = source.TorqueAt(2.7, speed) - source.FiringPulses.RippleTorque(2.7, speed);
        Assert.AreEqual(firstMean, secondMean, 1e-9); DisposeRig(rig);
    }
}
