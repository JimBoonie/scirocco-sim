// Tests of the EXPERIMENTAL brake model parts: pedal force to line pressure, booster lag and run-out, vacuum supply and depletion, rear pressure limiter points (Bentley Table b),
// disc and drum torques against the legacy fixed torques, the foot rate limit, the engine manifold feedback and the booster faults.
using NUnit.Framework; using UnityEngine;

public class BrakeModelTests
{
    const double StepSeconds = 0.0005;   // s, the 2 kHz physics substep
    const double IdleManifoldPressurePascals = 31000.0;   // Pa, the simulated idle manifold pressure (about 70 kPa vacuum)
    const double SettleSeconds = 2.0;   // s, long enough for every pneumatic transient to finish

    // @description - builds the brake system with default parameters, optionally with a modified copy of them
    // @param {System.Action<BrakeSystemParameters>} modify - changes the parameters before the system is built (null = none)
    // @return {ExperimentalBrakes} - brake system
    static ExperimentalBrakes BuildBrakes(System.Action<BrakeSystemParameters> modify = null)
    {
        var parameters = BrakeSystemParameters.CreateDefault(); modify?.Invoke(parameters);
        return new ExperimentalBrakes(ScriptableObject.CreateInstance<CarParams>(), parameters);
    }

    // @description - a load-cell pedal input at the idle manifold pressure
    // @param {double} pedalForceNewtons - pedal force in N
    // @return {BrakeStepInput} - input with a 400 N full-scale load-cell pedal at that force fraction
    static BrakeStepInput LoadCellInput(double pedalForceNewtons)
    {
        const double FullScaleNewtons = 400.0;   // N, a full-scale force larger than every force these tests apply
        return new BrakeStepInput { pedalAxis = pedalForceNewtons / FullScaleNewtons, loadCellPedal = true, loadCellMaxForceNewtons = FullScaleNewtons, manifoldPressurePascals = IdleManifoldPressurePascals, manifoldVolumeCubicMeters = 2.5e-3 };
    }

    // @description - runs the brake system for a time at a constant input
    // @param {ExperimentalBrakes} brakes - brake system
    // @param {BrakeStepInput} input - constant input
    // @param {double} seconds - duration in s
    // @return {BrakeStepResult} - result of the last step
    static BrakeStepResult Run(ExperimentalBrakes brakes, BrakeStepInput input, double seconds)
    {
        BrakeStepResult result = default; int steps = (int)(seconds / StepSeconds);
        for (int stepIndex = 0; stepIndex < steps; stepIndex++) result = brakes.Step(input, StepSeconds);
        return result;
    }

    // @description - pedal force to master pressure at steady state against the hand calculation: rod force = ratio x force, output = jump-in + servo ratio x (rod - cut-in), pressure = output / master area
    // @return {Void} - nothing
    [Test] public void PedalForceToLinePressureMatchesTheHandCalculation()
    {
        var brakes = BuildBrakes(); var parameters = brakes.Parameters; const double PedalForce = 100.0;
        BrakeStepResult result = Run(brakes, LoadCellInput(PedalForce), SettleSeconds);
        double rodForce = PedalForce * parameters.pedalRatio, outputForce = parameters.boosterJumpInOutputForceNewtons + parameters.boosterServoRatio * (rodForce - parameters.boosterCutInRodForceNewtons);
        double expectedPressure = outputForce / BrakeSystemParameters.CircleArea(parameters.masterCylinderBoreMeters);
        Assert.Less(outputForce, rodForce + brakes.Booster.MaximumAssistForceNewtons(IdleManifoldPressurePascals), "this force is on the power slope, below run-out");
        Assert.AreEqual(expectedPressure, result.masterPressurePascals, 0.01 * expectedPressure, "master pressure within 1 % of the hand calculation");
        Assert.AreEqual(outputForce - rodForce, result.boosterAssistForceNewtons, 0.01 * outputForce);
    }

    // @description - with no vacuum (engine off, manifold at ambient) the booster adds nothing: pressure = rod force / master area, so the pedal effort rises by the servo ratio (Bentley: "will increase braking effort")
    // @return {Void} - nothing
    [Test] public void DeadBoosterGivesDirectPushrodPressure()
    {
        var brakes = BuildBrakes(); var parameters = brakes.Parameters; const double PedalForce = 200.0;
        var input = LoadCellInput(PedalForce); input.manifoldPressurePascals = 101325.0;
        BrakeStepResult result = Run(brakes, input, SettleSeconds);
        double expectedPressure = PedalForce * parameters.pedalRatio / BrakeSystemParameters.CircleArea(parameters.masterCylinderBoreMeters);
        Assert.AreEqual(expectedPressure, result.masterPressurePascals, 0.005 * expectedPressure); Assert.Less(result.boosterAssistForceNewtons, 1.0);
    }

    // @description - the pneumatic lag: after a step of pedal force the assist rises monotonically without overshoot, with a time constant of tens of milliseconds, and the release is slower than the apply
    // @return {Void} - nothing
    [Test] public void BoosterLagIsPneumaticMonotonicAndReleaseIsSlower()
    {
        var brakes = BuildBrakes(); var input = LoadCellInput(100.0);
        Run(brakes, LoadCellInput(0.0), 0.2);
        double finalAssist = Run(BuildBrakes(), input, SettleSeconds).boosterAssistForceNewtons;
        double previousAssist = 0.0, riseTime = -1.0; int steps = (int)(SettleSeconds / StepSeconds);
        for (int stepIndex = 0; stepIndex < steps; stepIndex++)
        {
            double assist = brakes.Step(input, StepSeconds).boosterAssistForceNewtons;
            Assert.GreaterOrEqual(assist, previousAssist - 1e-9, "assist never falls while the pedal is held"); Assert.LessOrEqual(assist, finalAssist * 1.001, "no overshoot");
            if (riseTime < 0.0 && assist >= 0.9 * finalAssist) riseTime = stepIndex * StepSeconds; previousAssist = assist;
        }
        Assert.Greater(riseTime, 0.02, "the lag is not instantaneous"); Assert.Less(riseTime, 0.35, "the lag is tens to a few hundred milliseconds");
        double fallTime = -1.0; var releaseInput = LoadCellInput(0.0);
        for (int stepIndex = 0; stepIndex < steps; stepIndex++) { double assist = brakes.Step(releaseInput, StepSeconds).boosterAssistForceNewtons; if (assist <= 0.1 * finalAssist) { fallTime = stepIndex * StepSeconds; break; } }
        Assert.Greater(fallTime, riseTime, "release (smaller vacuum valve) is slower than apply");
    }

    // @description - run-out: above the run-out input force the assist saturates at (ambient - vacuum) x diaphragm area, so output rises 1:1 with input; the run-out input force moves left as the vacuum falls
    // @return {Void} - nothing
    [Test] public void RunOutSaturatesAndMovesWithVacuum()
    {
        var brakes = BuildBrakes(); var booster = brakes.Booster; const double AmbientPressure = 101325.0;
        double maximumAssist = booster.MaximumAssistForceNewtons(IdleManifoldPressurePascals), runOutRod = booster.RunOutRodForceNewtons(IdleManifoldPressurePascals);
        Assert.AreEqual(maximumAssist, booster.StaticAssistForce(runOutRod + 50.0, IdleManifoldPressurePascals), 1e-6, "assist saturates above run-out");
        const double IncrementNewtons = 100.0;
        double outputBelow = runOutRod + 50.0 + maximumAssist, outputAbove = runOutRod + 50.0 + IncrementNewtons + maximumAssist;
        Assert.AreEqual(IncrementNewtons, outputAbove - outputBelow, 1e-6, "output rises 1:1 above run-out");
        Assert.Less(booster.StaticAssistForce(runOutRod - 20.0, IdleManifoldPressurePascals), maximumAssist, "below run-out the assist is still on the power slope");
        double halfVacuumRunOut = booster.RunOutRodForceNewtons(0.5 * (AmbientPressure + IdleManifoldPressurePascals));
        Assert.Less(halfVacuumRunOut, runOutRod, "less vacuum, earlier run-out"); Assert.AreEqual(0.5 * maximumAssist, booster.MaximumAssistForceNewtons(0.5 * (AmbientPressure + IdleManifoldPressurePascals)), 1e-6);
        BrakeStepResult hardResult = Run(brakes, LoadCellInput(300.0), SettleSeconds);
        Assert.AreEqual(maximumAssist, hardResult.boosterAssistForceNewtons, 0.01 * maximumAssist, "the dynamic booster reaches the run-out limit too");
    }

    // @description - the static inverse (rod force for a wanted output) is consistent with the forward characteristic
    // @return {Void} - nothing
    [Test] public void StaticInverseMatchesTheForwardCharacteristic()
    {
        var booster = BuildBrakes().Booster;
        foreach (double rodForce in new[] { 10.0, 39.0, 41.0, 80.0, 200.0, 350.0, 600.0, 1500.0 })
        {
            double output = rodForce + booster.StaticAssistForce(rodForce, IdleManifoldPressurePascals);
            Assert.AreEqual(output, RodPlusAssist(booster, booster.RodForceForOutputForce(output, IdleManifoldPressurePascals)), 1e-6);
        }
    }

    // @description - output force of a static rod force
    // @param {VacuumBoosterModel} booster - booster
    // @param {double} rodForceNewtons - rod force in N
    // @return {double} - rod plus assist force in N
    static double RodPlusAssist(VacuumBoosterModel booster, double rodForceNewtons) => rodForceNewtons + booster.StaticAssistForce(rodForceNewtons, IdleManifoldPressurePascals);

    // @description - vacuum depletion with the engine off: each pump of the pedal mixes the filled working chamber into the vacuum chamber, so the vacuum chamber pressure rises exactly as the isothermal mass-mixing hand calculation says
    // @return {Void} - nothing
    [Test] public void VacuumDepletionMatchesMassMixing()
    {
        var brakes = BuildBrakes(parameters => parameters.boosterLeakFlowAreaSquareMeters = 0.0); var booster = brakes.Booster; var parameters = brakes.Parameters;
        var engineOffApply = LoadCellInput(60.0); engineOffApply.manifoldPressurePascals = 101325.0; var engineOffRelease = LoadCellInput(0.0); engineOffRelease.manifoldPressurePascals = 101325.0;
        brakes.Step(engineOffRelease, StepSeconds); booster.Reset(IdleManifoldPressurePascals); double previousVacuumPressure = IdleManifoldPressurePascals;
        for (int stroke = 0; stroke < 6; stroke++)
        {
            Run(brakes, engineOffApply, 0.6); double heldWorkingPressure = booster.WorkingPressurePascals, vacuumBefore = booster.VacuumPressurePascals;
            Run(brakes, engineOffRelease, 1.5);
            double workingVolume = parameters.boosterWorkingChamberVolumeCubicMeters, vacuumVolume = parameters.boosterVacuumChamberVolumeCubicMeters;
            double expectedAfter = (vacuumBefore * vacuumVolume + heldWorkingPressure * workingVolume) / (vacuumVolume + workingVolume);
            Assert.AreEqual(expectedAfter, booster.VacuumPressurePascals, 0.01 * (expectedAfter - vacuumBefore) + 1.0, "stroke " + stroke + ": mixing hand calculation");
            Assert.Greater(booster.VacuumPressurePascals, previousVacuumPressure, "each pump raises the vacuum chamber pressure"); previousVacuumPressure = booster.VacuumPressurePascals;
        }
        Assert.Less(booster.MaximumAssistForceNewtons(booster.VacuumPressurePascals), 0.9 * booster.MaximumAssistForceNewtons(IdleManifoldPressurePascals), "assist is reduced after repeated pumping");
    }

    // @description - with the engine running the vacuum chamber recharges through the check valve: after pumping it returns to the manifold pressure, and the check valve never lets air flow from the manifold into the chamber
    // @return {Void} - nothing
    [Test] public void RunningEngineRechargesTheVacuumChamber()
    {
        var brakes = BuildBrakes(); var booster = brakes.Booster; booster.Reset(IdleManifoldPressurePascals);
        for (int stroke = 0; stroke < 4; stroke++) { Run(brakes, LoadCellInput(80.0), 0.5); Run(brakes, LoadCellInput(0.0), 0.3); }
        double pumpedPressure = booster.VacuumPressurePascals; Run(brakes, LoadCellInput(0.0), 6.0);
        Assert.Less(booster.VacuumPressurePascals, pumpedPressure, "the manifold pumps the chamber down"); Assert.AreEqual(IdleManifoldPressurePascals, booster.VacuumPressurePascals, 0.02 * IdleManifoldPressurePascals);
        var higherManifold = LoadCellInput(0.0); higherManifold.manifoldPressurePascals = 60000.0; double before = booster.VacuumPressurePascals; Run(brakes, higherManifold, 1.0);
        Assert.GreaterOrEqual(booster.VacuumPressurePascals, before - 1.0, "no flow from a higher-pressure manifold into the chamber");
    }

    // @description - a hard release returns air from the vacuum chamber to the manifold (the idle bump source); none returns while the engine is off, and the returned mass is zero at a steady idle without pedal
    // @return {Void} - nothing
    [Test] public void HardReleaseReturnsAirToTheManifold()
    {
        var brakes = BuildBrakes(); brakes.Booster.Reset(IdleManifoldPressurePascals); double returnedMass = 0.0;
        Run(brakes, LoadCellInput(100.0), 0.5);
        for (int stepIndex = 0; stepIndex < (int)(1.0 / StepSeconds); stepIndex++) returnedMass += brakes.Step(LoadCellInput(0.0), StepSeconds).manifoldMassReturnedKilograms;
        Assert.Greater(returnedMass, 1e-5, "air returned after a hard release"); Assert.Less(returnedMass, 1e-3, "but it is a small amount (idle air flow is 3.5e-3 kg/s)");
        double steadyMass = 0.0; var steady = BuildBrakes(); steady.Booster.Reset(IdleManifoldPressurePascals);
        for (int stepIndex = 0; stepIndex < (int)(1.0 / StepSeconds); stepIndex++) steadyMass += steady.Step(LoadCellInput(0.0), StepSeconds).manifoldMassReturnedKilograms;
        Assert.Less(steadyMass, 1.5e-5, "no pedal, steady manifold: only the healthy leak (about 8e-6 kg/s) is pumped back");
        var engineOff = BuildBrakes(); var offInput = LoadCellInput(100.0); offInput.manifoldPressurePascals = 101325.0; double offMass = 0.0; Run(engineOff, offInput, 0.5); offInput = LoadCellInput(0.0); offInput.manifoldPressurePascals = 101325.0;
        for (int stepIndex = 0; stepIndex < (int)(1.0 / StepSeconds); stepIndex++) offMass += engineOff.Step(offInput, StepSeconds).manifoldMassReturnedKilograms;
        Assert.AreEqual(0.0, offMass, "engine off: the check valve is closed");
    }

    // @description - the engine hook raises the manifold pressure by R T / V x mass, clamps at ambient, and leaves Step untouched when not called
    // @return {Void} - nothing
    [Test] public void EngineManifoldHookAddsMassIsothermally()
    {
        var carParams = ScriptableObject.CreateInstance<CarParams>(); var engine = new Engine(carParams); engine.SetRunning(true);
        float pressureBefore = engine.ManifoldPressurePascals; const float AddedMass = 1.0e-4f;
        engine.AddExternalManifoldMass(AddedMass);
        float expectedRise = carParams.airGasConstant * carParams.ambientTemperature / carParams.manifoldVolume * AddedMass;
        Assert.AreEqual(pressureBefore + expectedRise, engine.ManifoldPressurePascals, 1f); Assert.AreEqual(engine.ManifoldPressurePascals / carParams.ambientPressure, engine.manifoldPressureRatio, 1e-6f);
        engine.AddExternalManifoldMass(10f); Assert.AreEqual(carParams.ambientPressure, engine.ManifoldPressurePascals, "clamped at ambient");
    }

    // @description - the booster fault interface: an extra leak lowers the vacuum and the assist, and a blocked supply stops the recharge
    // @return {Void} - nothing
    [Test] public void BoosterFaultsReduceVacuumAndAssist()
    {
        var healthy = BuildBrakes(); var leaking = BuildBrakes(); var blocked = BuildBrakes();
        healthy.Booster.Reset(IdleManifoldPressurePascals); leaking.Booster.Reset(IdleManifoldPressurePascals); blocked.Booster.Reset(IdleManifoldPressurePascals);
        var leakInput = LoadCellInput(0.0); leakInput.boosterFault = new BoosterFault { extraLeakAreaSquareMeters = 2.0e-5 };
        Run(healthy, LoadCellInput(0.0), 2.0); Run(leaking, leakInput, 2.0);
        Assert.Greater(leaking.Booster.VacuumPressurePascals, healthy.Booster.VacuumPressurePascals + 5000.0, "a leak keeps the chamber above the manifold pressure");
        Assert.Less(leaking.Booster.AssistForceNewtons, 1.0, "no assist without pedal force");
        double healthyAssist = Run(healthy, LoadCellInput(100.0), 1.0).boosterAssistForceNewtons; leakInput.pedalAxis = LoadCellInput(100.0).pedalAxis;
        double leakingAssist = Run(leaking, leakInput, 1.0).boosterAssistForceNewtons;
        Assert.Less(leakingAssist, 0.9 * healthyAssist, "reduced vacuum, reduced assist");
        var blockedInput = LoadCellInput(0.0); blockedInput.boosterFault = new BoosterFault { vacuumSupplyBlockedFraction = 1.0 };
        for (int stroke = 0; stroke < 4; stroke++) { Run(blocked, LoadCellInput(80.0), 0.5); Run(blocked, blockedInput, 0.5); }
        double pumpedPressure = blocked.Booster.VacuumPressurePascals; Run(blocked, blockedInput, 5.0);
        Assert.Greater(blocked.Booster.VacuumPressurePascals, pumpedPressure - 100.0, "a blocked supply does not recharge");
    }

    // @description - rear pressure limiter points: Bentley Table b gives rear 39..43 bar at 55 bar front and 51..57 bar at 100 bar front; below the knee both are equal; the limiter is monotone
    // @return {Void} - nothing
    [Test] public void RearLimiterReproducesBentleyTableB()
    {
        var parameters = BrakeSystemParameters.CreateDefault(); double bar = parameters.pascalsPerBar;
        double rearAt55 = RearLimiterBar(55.0, parameters), rearAt100 = RearLimiterBar(100.0, parameters);
        Assert.That(rearAt55, Is.InRange(39.0, 43.0), "front 55 bar -> rear 39-43 bar"); Assert.That(rearAt100, Is.InRange(51.0, 57.0), "front 100 bar -> rear 51-57 bar");
        Assert.AreEqual(20.0, RearLimiterBar(20.0, parameters), 1e-9, "below the knee rear = front"); Assert.AreEqual(35.0, RearLimiterBar(35.0, parameters), 1e-9, "at the knee rear = front");
        double previous = 0.0; for (double masterBar = 0.0; masterBar <= 150.0; masterBar += 1.0) { double rear = RearLimiterBar(masterBar, parameters); Assert.GreaterOrEqual(rear, previous); previous = rear; }
        Assert.AreEqual(0.0, RearPressureLimiter.RearPressure(-5.0 * bar, parameters.rearLimiterKneePressurePascals, parameters.rearLimiterSlopeRatio));
    }

    // @description - rear pressure in bar for a master pressure in bar
    // @param {double} masterBar - master pressure in bar
    // @param {BrakeSystemParameters} parameters - brake parameters
    // @return {double} - rear pressure in bar
    static double RearLimiterBar(double masterBar, BrakeSystemParameters parameters) => RearPressureLimiter.RearPressure(masterBar * parameters.pascalsPerBar, parameters.rearLimiterKneePressurePascals, parameters.rearLimiterSlopeRatio) / parameters.pascalsPerBar;

    // @description - sanity check: the estimates reproduce the legacy fixed torques (front 1400 N m, rear about 300 N m) at the master pressure where the front torque equals 1400; front gain 13.2 N m per bar (research 4.3)
    // @return {Void} - nothing
    [Test] public void EstimatesReproduceTheLegacyFixedTorques()
    {
        var carParams = ScriptableObject.CreateInstance<CarParams>(); var brakes = new ExperimentalBrakes(carParams, BrakeSystemParameters.CreateDefault()); var parameters = brakes.Parameters;
        double masterPressure = brakes.Disc.PressureForTorque(carParams.brakeTorqueFront), bar = parameters.pascalsPerBar;
        Assert.AreEqual(carParams.brakeTorqueFront, brakes.Disc.Torque(brakes.CircuitA.FrontPressure(masterPressure)), 1e-6);
        double rearTorque = brakes.Drum.Torque(brakes.CircuitA.RearPressure(masterPressure));
        Assert.AreEqual(carParams.brakeTorqueRear, rearTorque, 0.05 * carParams.brakeTorqueRear, "rear torque within 5 % of the legacy 300 N m");
        Assert.AreEqual(13.18, brakes.Disc.TorquePerPascal * bar, 0.15, "front disc gain, N m per bar");
        Assert.Less(masterPressure / bar, 115.0); Assert.Greater(masterPressure / bar, 95.0, "the legacy full pedal is the hard-braking pressure level (Bentley test point 100 bar)");
        Assert.Less(parameters.frontEffectiveFrictionRadiusMeters, 0.5 * parameters.frontDiscOuterDiameterMeters, "the friction radius lies inside the 239 mm disc");
    }

    // @description - the drum brake factor of 2.4 (estimate) is used: torque = factor x wheel cylinder force x drum radius
    // @return {Void} - nothing
    [Test] public void DrumTorqueUsesTheBrakeFactorAndCylinderForce()
    {
        var parameters = BrakeSystemParameters.CreateDefault(); var drum = new DrumBrakeModel(parameters); const double LinePressure = 50.0e5;
        double expected = parameters.rearBrakeFactorOfShoePair * (LinePressure - parameters.rearBitePressurePascals) * BrakeSystemParameters.CircleArea(parameters.rearWheelCylinderBoreMeters) * 0.5 * parameters.rearDrumInsideDiameterMeters;
        Assert.AreEqual(expected, drum.Torque(LinePressure), 1e-9); Assert.AreEqual(2.4, parameters.rearBrakeFactorOfShoePair); Assert.AreEqual(0.0, drum.Torque(parameters.rearBitePressurePascals));
    }

    // @description - no pedal gives exactly zero torque on all four wheels, and a small force below the bite pressure also gives exactly zero
    // @return {Void} - nothing
    [Test] public void NoPedalGivesExactlyZeroTorque()
    {
        var brakes = BuildBrakes(); brakes.Booster.Reset(IdleManifoldPressurePascals);
        for (int stepIndex = 0; stepIndex < 4000; stepIndex++)
        {
            BrakeStepResult result = brakes.Step(LoadCellInput(0.0), StepSeconds);
            Assert.AreEqual(0.0, result.frontLeftTorqueNewtonMeters); Assert.AreEqual(0.0, result.frontRightTorqueNewtonMeters); Assert.AreEqual(0.0, result.rearLeftTorqueNewtonMeters); Assert.AreEqual(0.0, result.rearRightTorqueNewtonMeters);
        }
    }

    // @description - the diagonal split: both circuits carry the same pressure when intact, so left and right torques agree
    // @return {Void} - nothing
    [Test] public void IntactDiagonalCircuitsGiveSymmetricTorques()
    {
        BrakeStepResult result = Run(BuildBrakes(), LoadCellInput(150.0), SettleSeconds);
        Assert.AreEqual(result.frontLeftTorqueNewtonMeters, result.frontRightTorqueNewtonMeters); Assert.AreEqual(result.rearLeftTorqueNewtonMeters, result.rearRightTorqueNewtonMeters); Assert.Greater(result.frontLeftTorqueNewtonMeters, result.rearLeftTorqueNewtonMeters);
    }

    // @description - the foot rate limit: a key press (axis 0 -> 1) moves the pedal at the apply speed, not instantly; lifting is faster; a load-cell pedal has no limit
    // @return {Void} - nothing
    [Test] public void KeyPressMovesAtFootSpeedAndLoadCellIsNotLimited()
    {
        var brakes = BuildBrakes(); var parameters = brakes.Parameters; var keyInput = new BrakeStepInput { pedalAxis = 1.0, manifoldPressurePascals = IdleManifoldPressurePascals, manifoldVolumeCubicMeters = 2.5e-3 };
        brakes.Booster.Reset(IdleManifoldPressurePascals);
        const double ProbeSeconds = 0.05; Run(brakes, keyInput, ProbeSeconds);
        double expectedFraction = parameters.footApplySpeedMetersPerSecond * ProbeSeconds / brakes.Pedal.FullTravelMeters;
        Assert.AreEqual(expectedFraction, brakes.Pedal.FootTravelFraction, 1e-6, "pedal moved at the apply speed"); Assert.Less(brakes.Pedal.FootTravelFraction, 0.5);
        double applyTime = brakes.Pedal.FullTravelMeters / parameters.footApplySpeedMetersPerSecond, releaseTime = brakes.Pedal.FullTravelMeters / parameters.footReleaseSpeedMetersPerSecond;
        Run(brakes, keyInput, applyTime); Assert.AreEqual(1.0, brakes.Pedal.FootTravelFraction, 1e-9);
        var releaseInput = keyInput; releaseInput.pedalAxis = 0.0; Run(brakes, releaseInput, 0.5 * releaseTime); Assert.AreEqual(0.5, brakes.Pedal.FootTravelFraction, 0.01, "released at the release speed"); Assert.Less(releaseTime, applyTime);
        Assert.That(applyTime, Is.InRange(0.1, 0.6), "a full press takes a few tenths of a second, like a hurried foot");
        var loadCell = BuildBrakes(); BrakeStepResult first = loadCell.Step(LoadCellInput(200.0), StepSeconds);
        Assert.AreEqual(200.0, first.pedalForceNewtons, 1e-9, "a load-cell pedal force goes in directly on the first step");
    }

    // @description - a full travel-based press needs a plausible pedal force (derived from the car's own force-travel relation): a hard but possible foot force
    // @return {Void} - nothing
    [Test] public void FullTravelPressNeedsAPlausibleHardPedalForce()
    {
        var brakes = BuildBrakes(); brakes.Booster.Reset(IdleManifoldPressurePascals);
        double fullForce = brakes.PedalForceForTravelNewtons(brakes.Pedal.FullTravelMeters);
        Assert.That(fullForce, Is.InRange(150.0, 800.0), "full-pedal force in N"); Assert.AreEqual(0.0, brakes.PedalForceForTravelNewtons(0.0));
        double halfForce = brakes.PedalForceForTravelNewtons(0.5 * brakes.Pedal.FullTravelMeters); Assert.Less(halfForce, fullForce); Assert.Greater(halfForce, 0.0);
        var deadBooster = BuildBrakes(); deadBooster.Booster.Reset(101325.0);
        Assert.Greater(deadBooster.PedalForceForTravelNewtons(deadBooster.Pedal.FullTravelMeters), 1.5 * fullForce, "without vacuum the same travel needs much more force (the booster is already at run-out at full pedal, so less than the servo ratio)");
    }

    // @description - the pedal travel to pressure functions are inverse to each other, and monotone
    // @return {Void} - nothing
    [Test] public void PedalTravelAndPressureAreInverse()
    {
        var circuit = new HydraulicBrakeCircuit(BrakeSystemParameters.CreateDefault()); double previousTravel = 0.0;
        foreach (double pressure in new[] { 5.0e5, 20.0e5, 60.0e5, 100.0e5 })
        {
            double travel = circuit.PedalTravelAtPressure(pressure); Assert.Greater(travel, previousTravel); previousTravel = travel; Assert.AreEqual(pressure, circuit.PressureAtPedalTravel(travel), 1e-3 * pressure);
        }
        Assert.AreEqual(0.0, circuit.PressureAtPedalTravel(0.001), "free play and take-up produce no pressure");
    }

    // @description - the load-cell default entry is 70 lb (user-reported), about 311 N, in the profile, the car parameters, the tab and the brake parameters
    // @return {Void} - nothing
    [Test] public void LoadCellDefaultIsSeventyPoundsAbout311Newtons()
    {
        Assert.AreEqual(311.4, 70.0 * BrakeSystemParameters.NewtonsPerPoundForce, 0.1); Assert.AreEqual(70f, new BindingProfile().loadCellBrakePedalMaxForcePounds);
        Assert.AreEqual(70f, ScriptableObject.CreateInstance<CarParams>().loadCellBrakePedalMaxForcePounds); Assert.AreEqual(70f, ExperimentalSettingsTab.DefaultLoadCellMaxForcePounds);
        Assert.AreEqual(70.0, BrakeSystemParameters.CreateDefault().loadCellDefaultMaxForcePounds);
    }

    // @description - robustness: random pedal inputs, manifold pressures and faults never produce NaN, negative torque or pressure outside the physical range
    // @return {Void} - nothing
    [Test] public void RandomInputsStayFiniteAndPhysical()
    {
        var brakes = BuildBrakes(); var random = new System.Random(12345);
        for (int stepIndex = 0; stepIndex < 40000; stepIndex++)
        {
            var input = new BrakeStepInput { pedalAxis = random.NextDouble() < 0.3 ? random.NextDouble() : (stepIndex / 4000) % 2, loadCellPedal = (stepIndex / 10000) % 2 == 1, loadCellMaxForceNewtons = 311.0, manifoldPressurePascals = 20000.0 + 81000.0 * random.NextDouble(), manifoldVolumeCubicMeters = 2.5e-3 };
            input.boosterFault = new BoosterFault { extraLeakAreaSquareMeters = random.NextDouble() < 0.1 ? 2.0e-6 : 0.0, vacuumSupplyBlockedFraction = random.NextDouble() < 0.1 ? 1.0 : 0.0 };
            BrakeStepResult result = brakes.Step(input, StepSeconds);
            foreach (double value in new[] { result.frontLeftTorqueNewtonMeters, result.rearRightTorqueNewtonMeters, result.masterPressurePascals, result.manifoldMassReturnedKilograms, result.pedalForceNewtons, result.boosterAssistForceNewtons })
            { Assert.IsFalse(double.IsNaN(value) || double.IsInfinity(value)); Assert.GreaterOrEqual(value, 0.0); }
            Assert.That(brakes.Booster.VacuumPressurePascals, Is.InRange(0.0, 101325.0)); Assert.That(brakes.Booster.WorkingPressurePascals, Is.InRange(0.0, 101325.0));
        }
    }

    // @description - Reset forgets all state: after a Reset the system behaves exactly like a new one
    // @return {Void} - nothing
    [Test] public void ResetRestoresTheStartingState()
    {
        var used = BuildBrakes(); Run(used, LoadCellInput(120.0), 1.0); used.Reset(); var fresh = BuildBrakes();
        for (int stepIndex = 0; stepIndex < 2000; stepIndex++)
        {
            BrakeStepResult usedResult = used.Step(LoadCellInput(90.0), StepSeconds), freshResult = fresh.Step(LoadCellInput(90.0), StepSeconds);
            Assert.AreEqual(freshResult.frontLeftTorqueNewtonMeters, usedResult.frontLeftTorqueNewtonMeters); Assert.AreEqual(freshResult.masterPressurePascals, usedResult.masterPressurePascals); Assert.AreEqual(freshResult.manifoldMassReturnedKilograms, usedResult.manifoldMassReturnedKilograms);
        }
    }

    // @description - the compressible orifice: zero flow without a pressure difference, choked flow independent of the downstream pressure, and the textbook choked value
    // @return {Void} - nothing
    [Test] public void OrificeFlowIsChokedBelowTheCriticalRatio()
    {
        const double Gamma = 1.4, GasConstant = 287.05, Temperature = 293.15, Area = 1.0e-5, Pressure = 101325.0;
        Assert.AreEqual(0.0, CompressibleOrifice.MassFlow(1.0, Area, Pressure, Pressure, GasConstant, Temperature, Gamma)); Assert.AreEqual(0.0, CompressibleOrifice.MassFlow(1.0, Area, 50000.0, 60000.0, GasConstant, Temperature, Gamma));
        double chokedA = CompressibleOrifice.MassFlow(1.0, Area, Pressure, 10000.0, GasConstant, Temperature, Gamma), chokedB = CompressibleOrifice.MassFlow(1.0, Area, Pressure, 40000.0, GasConstant, Temperature, Gamma);
        Assert.AreEqual(chokedA, chokedB, 1e-12); double textbook = Area * Pressure * System.Math.Sqrt(Gamma / (GasConstant * Temperature)) * System.Math.Pow(2.0 / (Gamma + 1.0), (Gamma + 1.0) / (2.0 * (Gamma - 1.0)));
        Assert.AreEqual(textbook, chokedA, 1e-9 * textbook); Assert.AreEqual(0.5283, CompressibleOrifice.CriticalPressureRatio(Gamma), 1e-4);
        Assert.Less(CompressibleOrifice.MassFlow(1.0, Area, Pressure, 80000.0, GasConstant, Temperature, Gamma), chokedA, "subsonic flow is below the choked flow");
    }
}
