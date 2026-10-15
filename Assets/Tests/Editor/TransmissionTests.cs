// Validation of the EXPERIMENTAL transmission and synchronizer model at the physics level: a one-degree-of-freedom input-node plant (the carrier is held at a fixed speed, as a vehicle
// is, the input node integrates the torque the model returns). Covers sync time vs the Schaeffler/Chalmers formula and the Farokhi Nejad 0.69 s measurement, the blocking threshold,
// crunch and refusal conditions, release from gear under load vs lift-off, neutral drag, rattle onset, parameter identities and step-size convergence.
using NUnit.Framework; using UnityEngine;

public class TransmissionTests
{
    const double Dt = 2e-4, RadiansPerSecondPerRpm = System.Math.PI / 30.0, AmbientKelvin = 293.0;

    // @description - input-node plant: integrates the node speed with the torque the model returns (plus an optional constant clutch-like torque) against a carrier held at a fixed speed
    class NodePlant
    {
        public TransmissionModel model; public double inputSpeed, carrierSpeed, plantInertia, nodeInertia, appliedTorque, dt = Dt, lastTorque; public TransmissionStepResult last;
        // @description - one step of model and node
        // @return {Void}
        public void Step()
        {
            var input = new TransmissionStepInput { inputShaftAngularVelocity = inputSpeed, carrierAngularVelocity = carrierSpeed, gearTrainInputTorque = lastTorque, inputNodeInertiaKilogramMetersSquared = nodeInertia, ambientTemperatureKelvin = AmbientKelvin, dt = dt };
            last = model.Advance(input); lastTorque = appliedTorque + last.inputShaftExternalTorque;
            if (last.lockedTotalRatioSigned != 0.0) inputSpeed = last.lockedTotalRatioSigned * carrierSpeed; else inputSpeed += lastTorque / plantInertia * dt;
        }
        // @description - steps until the predicate holds or the time limit passes
        // @param {System.Func<bool>} done - stop condition
        // @param {double} seconds - limit in s
        // @return {double} - elapsed s
        public double RunUntil(System.Func<bool> done, double seconds) { int steps = (int)(seconds / dt), count = 0; while (count < steps && !done()) { Step(); count++; } return count * dt; }
    }

    // @description - builds a plant with default parameters (a copy the test may change) at a given speed error for a gear
    // @param {TransmissionParameters} parameters - parameters
    // @param {int} gear - 1..5 or -1
    // @param {double} speedErrorRadPerSecond - input speed minus the speed the gear demands at the input shaft
    // @param {double} carrierSpeed - carrier speed in rad/s
    // @param {double} inertia - node inertia in kg m^2 (plant and model)
    // @return {NodePlant} - plant with the model in neutral and the request set
    static NodePlant Build(TransmissionParameters parameters, int gear, double speedErrorRadPerSecond, double carrierSpeed, double inertia)
    {
        var carParams = ScriptableObject.CreateInstance<CarParams>(); var model = new TransmissionModel(parameters, carParams.finalDrive, (float)AmbientKelvin);
        double pinion = carrierSpeed * carParams.finalDrive; var plant = new NodePlant { model = model, carrierSpeed = carrierSpeed, plantInertia = inertia, nodeInertia = inertia };
        plant.inputSpeed = model.Geometry.GearboxRatio(gear) * pinion + speedErrorRadPerSecond; plant.Step(); model.RequestedGear = gear; return plant;
    }

    // @description - sets a constant cone friction coefficient at every speed
    // @param {TransmissionParameters} parameters - parameters (modified)
    // @param {float} friction - coefficient
    // @return {Void}
    static void SetConstantFriction(TransmissionParameters parameters, float friction) { for (int index = 0; index < parameters.coneFrictionCoefficientAtSpeed.Length; index++) parameters.coneFrictionCoefficientAtSpeed[index] = friction; }

    // @description - removes every speed-dependent and bearing drag from a parameter set (isolates the cone physics in a test)
    // @param {TransmissionParameters} parameters - parameters (modified)
    // @return {TransmissionParameters} - the same object
    static TransmissionParameters WithoutDrag(TransmissionParameters parameters)
    {
        parameters.spinLossScaleForThisGearbox = 0f; parameters.looseGearRollingDragNewtonMeters = 0f; parameters.looseGearViscousDragNewtonMetersSecondPerRadian = 0f;
        parameters.outputPinionBearingTurningTorqueNewtonMeters = 0f; parameters.differentialBearingTurningTorqueNewtonMeters = 0f; return parameters;
    }

    // @description - the SC-74 test-rig synchronizer of Farokhi Nejad et al. 2017 as parameters: d 74 mm, mu 0.06, 1400 N, alpha 6.9 deg, J 0.17 kg m^2
    // @return {TransmissionParameters} - parameters
    static TransmissionParameters Sc74Parameters()
    {
        var parameters = TransmissionParameters.CreateDefault(); SetConstantFriction(parameters, 0.06f); parameters.coneHalfAngleDegrees = 6.9f; parameters.meanConeRadiusMeters[2] = 0.037f;
        parameters.handToSleeveMechanicalAdvantage = 1f; parameters.linkageEfficiency = 1f; parameters.driverPushForceNewtons = 1400f; parameters.blockingToothToConeRadiusRatio = 0.5f;   // a rig ring that blocks at mu 0.06
        return WithoutDrag(parameters);
    }

    // @description - Verifies the synchronization time against the constant-torque formula and the published SC-74 measurement (0.69 s, J 0.17 kg m^2, 1000 rpm, mu 0.06, 1400 N): the model must be within 5 % of the measurement and 1 % of the formula.
    // @return {Void}
    [Test] public void SynchronizationTimeMatchesFormulaAndMeasurement()
    {
        var parameters = Sc74Parameters(); double speedError = 1000.0 * RadiansPerSecondPerRpm, inertia = 0.17;
        var plant = Build(parameters, 3, speedError, 20.0, inertia); plant.RunUntil(() => plant.model.LastSynchronizationSeconds > 0.0, 3.0);
        double coneTorque = SynchronizerPhysics.ConeTorque(1400.0, 0.06, 0.037, 6.9 * Mathf.Deg2Rad), formula = SynchronizerPhysics.SynchronizationTime(inertia, speedError, coneTorque, 0.0);
        double measurement = 0.69, measured = plant.model.LastSynchronizationSeconds;
        Assert.AreEqual(formula, measured, 0.01 * formula + 2 * Dt, "model vs constant-torque formula (drag is a few percent of the cone torque)"); Assert.AreEqual(measurement, measured, 0.05 * measurement, "model vs Farokhi Nejad SC-74 measurement 0.69 s");
        Debug.Log($"TRANSMISSION VALIDATION sync time SC-74: model {measured:0.0000} s, formula {formula:0.0000} s, measured 0.69 s");
    }

    // @description - Verifies the blocking threshold: the model passes the ring (scratch) exactly below mu_min = sin(alpha) (R_S/R_C) f, within 2 %, and the threshold does not move over a 10x range of shift force.
    // @return {Void}
    [Test] public void BlockingThresholdMatchesFrictionFormulaAndIsForceIndependent()
    {
        foreach (float pushForce in new[] { 5f, 50f })
        {
            var reference = TransmissionParameters.CreateDefault(); reference.driverPushForceNewtons = pushForce;
            double predicted = SynchronizerPhysics.MinimumBlockingFriction(reference.coneHalfAngleDegrees * Mathf.Deg2Rad, reference.blockingToothToConeRadiusRatio, reference.roofIncludedAngleDegrees[2] * Mathf.Deg2Rad, reference.blockingToothFrictionCoefficient);
            double low = predicted * 0.5, high = predicted * 1.5;
            for (int iteration = 0; iteration < 30; iteration++)
            {
                double middle = 0.5 * (low + high); var parameters = TransmissionParameters.CreateDefault(); parameters.driverPushForceNewtons = pushForce; SetConstantFriction(parameters, (float)middle);
                var plant = Build(parameters, 3, 100.0, 20.0, 0.02); plant.RunUntil(() => plant.model.Phase == GearboxPhase.Synchronizing || plant.model.Phase == GearboxPhase.FreeFlight, 1.0); plant.Step();
                if (plant.model.ScratchCount > 0) low = middle; else high = middle;   // scratched: friction below the threshold
            }
            Assert.AreEqual(predicted, 0.5 * (low + high), 0.02 * predicted, $"blocking threshold at {pushForce} N hand force");
        }
    }

    // @description - Verifies the shift time is inversely proportional to the sleeve force (cone torque scales with F) and that a worn ring (gap at the wear limit) does not clamp: it scratches and crunches.
    // @return {Void}
    [Test] public void SyncTimeScalesWithForceAndWornRingCrunches()
    {
        double[] times = new double[2]; float[] forces = { 20f, 40f };
        for (int index = 0; index < 2; index++)
        {
            var parameters = WithoutDrag(TransmissionParameters.CreateDefault()); SetConstantFriction(parameters, 0.1f); parameters.driverPushForceNewtons = forces[index]; var plant = Build(parameters, 3, 100.0, 20.0, 0.02);
            plant.RunUntil(() => plant.model.LastSynchronizationSeconds > 0.0, 5.0); times[index] = plant.model.LastSynchronizationSeconds; Assert.Greater(times[index], 0.0);
        }
        Assert.AreEqual(2.0, times[0] / times[1], 0.06, "doubling the force halves the synchronization time");
        var worn = TransmissionParameters.CreateDefault(); var wornPlant = Build(worn, 3, 100.0, 20.0, 0.02); wornPlant.model.SetRingGap(3, worn.ringGapWearLimitMeters);
        wornPlant.RunUntil(() => wornPlant.model.GrindCount > 0, 1.0);
        Assert.Greater(wornPlant.model.ScratchCount, 0, "a ring at its wear limit cannot block"); Assert.Greater(wornPlant.model.GrindCount, 0, "and the dogs then clash at the unmatched speed");
    }

    // @description - Verifies the clutch-drag effect: with the engine joined (inertia 0.13 + 0.02) the same shift takes (J_node + J_engine) / J_node times as long; a clutch torque above the index torque refuses the gear even at matched speeds.
    // @return {Void}
    [Test] public void ClutchDragSlowsAndBlocksTheShift()
    {
        var parameters = WithoutDrag(TransmissionParameters.CreateDefault()); SetConstantFriction(parameters, 0.1f); var open = Build(parameters, 3, 100.0, 20.0, 0.02); open.RunUntil(() => open.model.LastSynchronizationSeconds > 0.0, 5.0);
        var joinedParameters = WithoutDrag(TransmissionParameters.CreateDefault()); SetConstantFriction(joinedParameters, 0.1f); var joined = Build(joinedParameters, 3, 100.0, 20.0, 0.02 + 0.13); joined.nodeInertia = 0.02; joined.RunUntil(() => joined.model.LastSynchronizationSeconds > 0.0, 20.0);
        Assert.AreEqual((0.02 + 0.13) / 0.02, joined.model.LastSynchronizationSeconds / open.model.LastSynchronizationSeconds, 0.15, "engine inertia joined by the clutch slows the synchronization");
        double blockingTorque = SynchronizerPhysics.BlockingTorque(parameters.handToSleeveMechanicalAdvantage * parameters.linkageEfficiency * parameters.driverPushForceNewtons, parameters.meanConeRadiusMeters[2] * parameters.blockingToothToConeRadiusRatio, parameters.roofIncludedAngleDegrees[2] * Mathf.Deg2Rad, parameters.blockingToothFrictionCoefficient);
        var refused = Build(TransmissionParameters.CreateDefault(), 3, 0.0, 20.0, 0.02); refused.appliedTorque = 1.3 * blockingTorque; refused.RunUntil(() => refused.model.Phase == GearboxPhase.Engaged, 2.0);
        Assert.AreNotEqual(GearboxPhase.Engaged, refused.model.Phase, "a clutch torque above the index torque keeps the ring blocking after the speeds matched");
        var allowed = Build(TransmissionParameters.CreateDefault(), 3, 0.0, 20.0, 0.02); allowed.appliedTorque = 0.7 * blockingTorque; allowed.RunUntil(() => allowed.model.Phase == GearboxPhase.Engaged, 2.0);
        Assert.AreEqual(GearboxPhase.Engaged, allowed.model.Phase, "below the index torque the ring unblocks and the gear goes in");
    }

    // @description - Verifies reverse clash: at a speed difference the idler chamfers cannot index, the model grinds and books exactly 1/2 J d_omega^2 on the first impact; with the input stopped it engages.
    // @return {Void}
    [Test] public void ReverseClashEnergyAndEngagementAtRest()
    {
        var parameters = TransmissionParameters.CreateDefault(); double inertia = 0.02, speedError = 80.0;
        var plant = Build(parameters, -1, speedError, 0.0, inertia); plant.plantInertia = 1e9;   // input speed frozen: the first clash happens at exactly the set error
        plant.RunUntil(() => plant.model.GrindCount > 0, 2.0);
        Assert.AreEqual(1, plant.model.GrindCount); Assert.AreEqual(SynchronizerPhysics.ImpactEnergy(inertia, speedError), plant.model.ImpactEnergyJoules, 1e-3 * SynchronizerPhysics.ImpactEnergy(inertia, speedError), "impact energy of the first clash");
        var calm = Build(parameters, -1, 0.0, 0.0, inertia); calm.RunUntil(() => calm.model.Phase == GearboxPhase.Engaged, 2.0);
        Assert.AreEqual(GearboxPhase.Engaged, calm.model.Phase); Assert.AreEqual(0, calm.model.GrindCount);
    }

    // @description - Verifies the gear leaves only when the assumed arm force exceeds the dog retention: stays in gear under load, leaves near zero torque (lift-off), the threshold equals ShiftLinkage.MaximumReleasableTorque within 2 %.
    // @return {Void}
    [Test] public void ReleaseFromGearUnderLoadVersusLiftOff()
    {
        var parameters = WithoutDrag(TransmissionParameters.CreateDefault()); double threshold = new ShiftLinkage(parameters).MaximumReleasableTorque();   // no drag: the torque across the dogs is exactly the applied torque
        System.Func<double, bool> leaves = torque =>
        {
            var plant = Build(parameters, 3, 0.0, 20.0, 0.02); plant.RunUntil(() => plant.model.Phase == GearboxPhase.Engaged, 2.0); plant.appliedTorque = torque; plant.lastTorque = torque; plant.model.RequestedGear = 0;
            plant.RunUntil(() => plant.model.EngagedGear == 0, 1.0); return plant.model.EngagedGear == 0;
        };
        Assert.IsTrue(leaves(0.0), "lift-off: no torque across the dogs, the gear comes out"); Assert.IsFalse(leaves(2.0 * threshold), "clutch engaged under load: the gear stays in");
        Assert.IsFalse(leaves(1.05 * threshold)); Assert.IsTrue(leaves(0.95 * threshold), "near the threshold the outcome flips");
        Assert.Greater(threshold, 0.0); Debug.Log($"TRANSMISSION VALIDATION release threshold {threshold:0.0} N m across the dogs (driver pull {parameters.driverPullForceNewtons} N at the knob)");
    }

    // @description - Verifies neutral drag bookkeeping: the loose-gear sum equals the parameter formula, the bulk spin plus the loose gears reproduces the anchored total, and the 30 -> 80 C drop equals the anchor.
    // @return {Void}
    [Test] public void NeutralDragAndOilViscosity()
    {
        var parameters = TransmissionParameters.CreateDefault(); var plant = Build(parameters, 3, 0.0, 0.0, 0.02); plant.model.RequestedGear = 0; plant.inputSpeed = 800.0 * RadiansPerSecondPerRpm; plant.Step();
        var oil = plant.model.Oil; var drag = plant.model.Drag; double inputSpeed = plant.inputSpeed, expectedSum = 0.0;
        for (int gear = 1; gear <= 5; gear++)
        {
            double ratio = plant.model.Geometry.GearboxRatio(gear), relative = parameters.gearIsLooseOnInputShaft[gear - 1] ? inputSpeed : inputSpeed / ratio, factor = parameters.gearIsLooseOnInputShaft[gear - 1] ? 1.0 : 1.0 / ratio;
            expectedSum += (parameters.looseGearRollingDragNewtonMeters + parameters.looseGearViscousDragNewtonMetersSecondPerRadian * oil.ViscosityRatioToReference() * relative) * factor;
        }
        Assert.AreEqual(expectedSum, drag.LooseGearDragSumAtInput(inputSpeed, 0.0), 1e-9); Assert.AreEqual(oil.SpinTorqueMagnitude(inputSpeed), drag.BulkSpinTorque(inputSpeed, 0.0) + expectedSum, 1e-9, "bulk + loose gears = anchored total");
        var cold = new GearboxOilModel(parameters, parameters.spinLossColdOilTemperatureKelvin); var reference = new GearboxOilModel(parameters, parameters.spinLossReferenceOilTemperatureKelvin); double speed = 1000.0 * RadiansPerSecondPerRpm;
        Assert.AreEqual(1.0 - parameters.spinLossDropFromColdToReference, reference.SpinTorqueMagnitude(speed) / cold.SpinTorqueMagnitude(speed), 1e-6, "30 to 80 C spin-loss drop");
        Assert.AreEqual(parameters.spinLossReferenceTorqueNewtonMeters * parameters.spinLossScaleForThisGearbox, reference.SpinTorqueMagnitude(speed), 1e-9, "anchor at 1000 rpm and 80 C");
        Assert.AreEqual(parameters.spinLossSecondTorqueNewtonMeters * parameters.spinLossScaleForThisGearbox, reference.SpinTorqueMagnitude(4000.0 * RadiansPerSecondPerRpm), 1e-6, "anchor at 4000 rpm");
        Assert.AreEqual(parameters.oilKinematicViscosityAt40CMillimetersSquaredPerSecond, reference.Viscosity(313.15), 1e-3); Assert.AreEqual(parameters.oilKinematicViscosityAt100CMillimetersSquaredPerSecond, reference.Viscosity(373.15), 1e-3);
    }

    // @description - Verifies the parameter identities: ratios from tooth counts match the sim's rounded ratios (0.1 %), reverse is 38/12, the final drive matches, and the loose-gear inertia is of the order of a web gear.
    // @return {Void}
    [Test] public void GearSetMatchesSourcedRatios()
    {
        var parameters = TransmissionParameters.CreateDefault(); var carParams = ScriptableObject.CreateInstance<CarParams>(); var geometry = new GearSetGeometry(parameters, carParams.finalDrive);
        for (int gear = 1; gear <= 5; gear++) Assert.AreEqual(carParams.gearRatios[gear - 1], geometry.GearboxRatio(gear), 0.001 * carParams.gearRatios[gear - 1]);
        Assert.AreEqual(38.0 / 12.0, -geometry.GearboxRatio(-1), 1e-6); Assert.AreEqual((double)parameters.finalDriveRingToothCount / parameters.finalDrivePinionToothCount, carParams.finalDrive, 0.001);
        Assert.That(geometry.LooseGearInertia(1), Is.InRange(1e-4, 1e-3)); Assert.Less(1.0 - geometry.MeshEfficiency(3), 0.1); Assert.Greater(1.0 - geometry.MeshEfficiency(3), 0.0);
    }

    // @description - Measures the rattle onset of the loose 1st gear driven by a sinusoidal input speed at the firing frequency of 900 rpm (30 Hz) and compares it with the analytic condition J_g alpha / i > T_drag.
    //   Impacts need the gear to cross the lash after it separates, so the measured onset can only be at or above the analytic one.
    // @return {Void}
    [Test] public void RattleOnsetFollowsInertiaTorqueVersusDrag()
    {
        var parameters = TransmissionParameters.CreateDefault(); var carParams = ScriptableObject.CreateInstance<CarParams>(); var geometry = new GearSetGeometry(parameters, carParams.finalDrive);
        double frequency = 900.0 / 60.0 * 2.0, omegaF = 2.0 * System.Math.PI * frequency, meanSpeed = 900.0 * RadiansPerSecondPerRpm, ratio = geometry.GearboxRatio(1);
        var oil = new GearboxOilModel(parameters, AmbientKelvin); double drag = oil.LooseGearDragMagnitude(meanSpeed / ratio), critical = drag * ratio / geometry.LooseGearInertia(1);
        System.Func<double, int> impacts = acceleration =>
        {
            double halfLash = 0.5 * parameters.normalBacklashMillimeters * 1e-3 / System.Math.Cos(parameters.normalPressureAngleDegrees * Mathf.Deg2Rad) / geometry.PitchRadius(parameters.drivenToothCount[0]);
            var gear = new LooseGearRattle(geometry.LooseGearInertia(1), ratio, halfLash, parameters.impactRestitution); double time = 0.0; int warmUpCount = 0;
            for (int step = 0; step < 7500; step++)
            {
                time += Dt; gear.Advance(meanSpeed + acceleration / omegaF * System.Math.Sin(omegaF * time), 0.0, oil.LooseGearDragMagnitude(meanSpeed / ratio), 0.02, Dt);
                if (step == 1500) warmUpCount = gear.ImpactCount;   // the start-up transient (gear speed from rest of the lash) is not rattle
            }
            return gear.ImpactCount - warmUpCount;
        };
        Assert.AreEqual(0, impacts(0.9 * critical), "below the analytic onset the gear never leaves the flank");
        double measuredOnset = double.NaN; for (double factor = 1.0; factor <= 40.0; factor *= 1.05) if (impacts(factor * critical) > 2) { measuredOnset = factor; break; }
        Assert.IsFalse(double.IsNaN(measuredOnset), "a strong enough pulse rattles the gear"); Assert.GreaterOrEqual(measuredOnset, 1.0);
        Debug.Log($"TRANSMISSION VALIDATION rattle onset: analytic J_g alpha/i = T_drag at alpha {critical:0} rad/s^2; first impacts at {measuredOnset:0.00} x that (lash must also be crossed); analytic condition {geometry.LooseGearInertia(1) * critical / ratio:0.000} N m vs drag {drag:0.000} N m");
    }

    // @description - Verifies step-size convergence of the synchronization time: halving the step twice changes it by less than 2 %.
    // @return {Void}
    [Test] public void SynchronizationTimeConvergesWithStepSize()
    {
        double[] times = new double[3]; double[] steps = { 1e-3, 5e-4, 2.5e-4 };
        for (int index = 0; index < steps.Length; index++) { var plant = Build(TransmissionParameters.CreateDefault(), 3, 100.0, 20.0, 0.02); plant.dt = steps[index]; plant.RunUntil(() => plant.model.LastSynchronizationSeconds > 0.0, 5.0); times[index] = plant.model.LastSynchronizationSeconds; }
        Assert.AreEqual(times[2], times[1], 0.02 * times[2]); Assert.AreEqual(times[2], times[0], 0.04 * times[2], "even the 1 kHz step is within 4 %");
        Debug.Log($"TRANSMISSION VALIDATION convergence: sync time {times[0]:0.0000} s (1 ms), {times[1]:0.0000} s (0.5 ms), {times[2]:0.0000} s (0.25 ms)");
    }

    // @description - Verifies that 1st and 2nd (cone on the output shaft) synchronize slower than a gear with its cone on the input shaft for the same speed error, force and radius: the cone torque is divided by the ratio at the input shaft.
    // @return {Void}
    [Test] public void FirstAndSecondGearSynchronizeSlowerPerCone()
    {
        var parameters = WithoutDrag(TransmissionParameters.CreateDefault()); SetConstantFriction(parameters, 0.1f); for (int index = 0; index < 5; index++) parameters.meanConeRadiusMeters[index] = 0.034f;
        double[] times = new double[2]; int[] gears = { 3, 2 };
        for (int index = 0; index < 2; index++) { var plant = Build(parameters, gears[index], 100.0, 20.0, 0.02); plant.RunUntil(() => plant.model.LastSynchronizationSeconds > 0.0, 10.0); times[index] = plant.model.LastSynchronizationSeconds; }
        Assert.AreEqual(parameters.drivenToothCount[1] / (double)parameters.drivingToothCount[1], times[1] / times[0], 0.06, "t(2nd) / t(3rd) = ratio of 2nd");
    }
}
