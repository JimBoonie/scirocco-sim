// EXPERIMENTAL transmission and synchronizer model (implements the two-port ITransmission). Owns: the gate state machine (release, travel, synchronize, free flight, dog crossing,
// engaged, grinding), the synchronizer cone and blocking ring physics, the reverse idler clash, the loose-gear rattle, drag and efficiency, and the oil temperature.
// It does NOT own the clutch, damper, shafts or tires. The player's gear request (the shifter's reported gear) is RequestedGear; the engaged gear follows it physically.
using UnityEngine;

public class TransmissionModel : ITransmission
{
    readonly TransmissionParameters parameters; readonly GearSetGeometry geometry; readonly ShiftLinkage linkage; readonly GearboxOilModel oil; readonly GearboxDragLoads dragLoads;
    readonly double[] dogRelativeAngle = new double[GearSetGeometry.ForwardGearCount];   // rad at the dog shaft, hub minus gear, wrapped to one dog pitch
    readonly double[] ringGapMeters = new double[GearSetGeometry.ForwardGearCount];
    bool initialized, coneStuck, dogsIn; int lastRail; double phaseTimer, slipDirectionHint, synchronizationStartTime, clock, grindEnergyJoules;
    public GearboxPhase Phase { get; private set; }
    public int RequestedGear { get; set; }
    public int EngagedGear { get; private set; }
    public int WorkingGear { get; private set; }
    public int ScratchCount { get; private set; }
    public int GrindCount { get; private set; }
    public int DoubleBumpCount { get; private set; }
    public int CompletedShiftCount { get; private set; }
    public double LastSynchronizationSeconds { get; private set; }
    public double LastConeDissipationJoules { get; private set; }
    public double ImpactEnergyJoules => grindEnergyJoules;
    public double OilTemperatureKelvin => oil.TemperatureKelvin;
    public GearSetGeometry Geometry => geometry;
    public ShiftLinkage Linkage => linkage;
    public GearboxOilModel Oil => oil;
    public GearboxDragLoads Drag => dragLoads;
    public double LastConeTorqueNewtonMeters { get; private set; }
    public double LastBlockingTorqueNewtonMeters { get; private set; }
    public bool ConeIsStuck => coneStuck;

    // @description - builds the model
    // @param {TransmissionParameters} parameters - parameter set
    // @param {float} finalDriveRatio - car final drive ratio
    // @param {float} initialOilTemperatureKelvin - starting oil temperature in K (ambient)
    // @return {void} - constructor
    public TransmissionModel(TransmissionParameters parameters, float finalDriveRatio, float initialOilTemperatureKelvin)
    {
        this.parameters = parameters; geometry = new GearSetGeometry(parameters, finalDriveRatio); linkage = new ShiftLinkage(parameters);
        oil = new GearboxOilModel(parameters, initialOilTemperatureKelvin); dragLoads = new GearboxDragLoads(parameters, geometry, oil);
        for (int index = 0; index < ringGapMeters.Length; index++) ringGapMeters[index] = parameters.ringToConeAxialGapMeters[index];
    }

    // @description - true while the lever has not delivered the requested gear (used by the keyboard auto-clutch to hold the clutch open)
    public bool ShiftInProgress => RequestedGear != EngagedGear || (Phase != GearboxPhase.Neutral && Phase != GearboxPhase.Engaged);

    // @description - the gear whose input-output tie is active (dogs in, or the cone stuck), 0 when the input node is free
    // @return {int} - gear or 0
    int TiedGear() => dogsIn ? EngagedGear : (coneStuck ? WorkingGear : 0);

    // @description - advances the transmission one step: gate state machine, then the loads
    // @param {TransmissionStepInput} input - inputs
    // @return {TransmissionStepResult} - lock state and the port torques
    public TransmissionStepResult Advance(in TransmissionStepInput input)
    {
        if (!initialized) Initialize();
        clock += input.dt;
        double pinionSpeed = input.carrierAngularVelocity * geometry.FinalDriveRatio;
        AccumulateDogAngles(input.inputShaftAngularVelocity, pinionSpeed, input.dt);
        var result = new TransmissionStepResult { meshEfficiency = 1.0, lashOwnedByTransmission = true };
        StepStateMachine(input, ref result);
        int tied = TiedGear();
        DragLoads loads = dragLoads.Compute(input.inputShaftAngularVelocity, input.carrierAngularVelocity, tied, input.inputNodeInertiaKilogramMetersSquared, input.dt);
        result.inputShaftExternalTorque += loads.inputTorque; result.carrierExternalTorque += loads.carrierTorque;
        result.lockedTotalRatioSigned = tied != 0 ? geometry.TotalRatio(tied) : 0.0;
        if (tied != 0) result.meshEfficiency = geometry.MeshEfficiency(tied);
        if (dogsIn) result.halfLashAtInputShaftRadians = geometry.HalfLashAtInputShaft(EngagedGear);
        double meshHeat = tied != 0 ? System.Math.Abs(input.gearTrainInputTorque * input.inputShaftAngularVelocity) * (1.0 - result.meshEfficiency) : 0.0;
        oil.AdvanceTemperature(loads.heatPowerWatts + meshHeat + ConeHeatPower(input), input.ambientTemperatureKelvin, input.dt);
        return result;
    }

    // @description - starts the model in the gear the lever is already in (the toggle was switched on while driving in gear)
    // @return {Void}
    void Initialize()
    {
        initialized = true; EngagedGear = RequestedGear; WorkingGear = RequestedGear; dogsIn = EngagedGear != 0; Phase = EngagedGear != 0 ? GearboxPhase.Engaged : GearboxPhase.Neutral;
        lastRail = EngagedGear != 0 ? ShiftLinkage.RailOf(EngagedGear) : 0;
    }

    double coneHeatThisStep;
    // @description - heat the cone dissipated in the last step as power
    // @param {TransmissionStepInput} input - inputs (for dt)
    // @return {double} - W
    double ConeHeatPower(in TransmissionStepInput input) { double power = coneHeatThisStep / input.dt; coneHeatThisStep = 0.0; return power; }

    // @description - integrates the relative angle between the hub and each gear at the dog shaft, wrapped to one dog pitch (the angular phase that decides tooth-on-tooth contact)
    // @param {double} inputSpeed - rad/s
    // @param {double} pinionSpeed - rad/s
    // @param {double} dt - s
    // @return {Void}
    void AccumulateDogAngles(double inputSpeed, double pinionSpeed, double dt)
    {
        double pitch = 2.0 * System.Math.PI / parameters.dogToothCount;
        for (int gear = 1; gear <= GearSetGeometry.ForwardGearCount; gear++)
        {
            double errorAtInput = inputSpeed - geometry.GearboxRatio(gear) * pinionSpeed;
            double angle = dogRelativeAngle[gear - 1] + errorAtInput * geometry.ConeSpeedReferenceFromInput(gear) * dt;
            dogRelativeAngle[gear - 1] = angle - pitch * System.Math.Floor(angle / pitch);
        }
    }

    // @description - dispatches the phase handler
    // @param {TransmissionStepInput} input - inputs
    // @param {TransmissionStepResult} result - result being built (torques added)
    // @return {Void}
    void StepStateMachine(in TransmissionStepInput input, ref TransmissionStepResult result)
    {
        switch (Phase)
        {
            case GearboxPhase.Neutral: StepNeutral(); break;
            case GearboxPhase.Engaged: StepEngaged(input); break;
            case GearboxPhase.Releasing: StepReleasing(input.dt); break;
            case GearboxPhase.Traveling: StepTraveling(input); break;
            case GearboxPhase.Synchronizing: StepSynchronizing(input, ref result); break;
            case GearboxPhase.FreeFlight: StepFreeFlight(input, ref result); break;
            case GearboxPhase.DogCrossing: StepDogCrossing(input.dt); break;
            case GearboxPhase.Grinding: StepGrinding(input); break;
        }
    }

    // @description - neutral: when a gear is requested the lever starts to move toward its ring contact
    // @return {Void}
    void StepNeutral()
    {
        if (RequestedGear == 0) return;
        BeginTravel(RequestedGear);
    }

    // @description - starts the lever travel to a gear
    // @param {int} gear - target gear 1..5 or -1
    // @return {Void}
    void BeginTravel(int gear) { WorkingGear = gear; phaseTimer = linkage.TravelTimeToRingContact(lastRail, gear); Phase = GearboxPhase.Traveling; }

    // @description - engaged: the driver has stopped pushing. When the request differs (gear or neutral) the driver pulls with the assumed arm force; the gear leaves only when
    //   that force exceeds the detent plus the flank force of the torque across the dog teeth (stays in gear under load, leaves near zero torque)
    // @param {TransmissionStepInput} input - inputs (torque across the dogs)
    // @return {Void}
    void StepEngaged(in TransmissionStepInput input)
    {
        if (RequestedGear == EngagedGear) return;
        double torqueAcrossDogs = TorqueAcrossDogs(input);
        if (linkage.PullForceAtSleeve() < linkage.ReleaseForceRequired(torqueAcrossDogs)) return;
        Phase = GearboxPhase.Releasing; phaseTimer = linkage.SleeveTravelTime(parameters.dogChamferTravelMeters);
    }

    // @description - torque the dog teeth carry: the gear train input torque for gears whose dogs are on the input shaft, times the ratio for the output-shaft dogs (1st, 2nd, reverse)
    // @param {TransmissionStepInput} input - inputs
    // @return {double} - N m magnitude at the dog shaft
    double TorqueAcrossDogs(in TransmissionStepInput input)
    {
        bool dogsOnInput = EngagedGear > 0 && geometry.LooseOnInputShaft(EngagedGear);
        return System.Math.Abs(input.gearTrainInputTorque) * (dogsOnInput ? 1.0 : System.Math.Abs(geometry.GearboxRatio(EngagedGear)));
    }

    // @description - releasing: the sleeve slides out of the dogs; at the end the gear is out and the lever sits in the gate center
    // @param {double} dt - s
    // @return {Void}
    void StepReleasing(double dt)
    {
        phaseTimer -= dt; if (phaseTimer > 0.0) return;
        lastRail = ShiftLinkage.RailOf(EngagedGear); EngagedGear = 0; WorkingGear = 0; dogsIn = false; coneStuck = false; Phase = GearboxPhase.Neutral;
    }

    // @description - traveling: the lever moves to the ring contact; a changed request retargets it, a request for neutral lets it go back
    // @param {TransmissionStepInput} input - inputs
    // @return {Void}
    void StepTraveling(in TransmissionStepInput input)
    {
        if (RequestedGear != WorkingGear) { if (RequestedGear == 0) { Phase = GearboxPhase.Neutral; WorkingGear = 0; } else BeginTravel(RequestedGear); return; }
        phaseTimer -= input.dt; if (phaseTimer > 0.0) return;
        lastRail = ShiftLinkage.RailOf(WorkingGear);
        if (WorkingGear == GearSetGeometry.ReverseGear) { TryReverseEngagement(input); return; }
        Phase = GearboxPhase.Synchronizing; coneStuck = false; synchronizationStartTime = clock; LastConeDissipationJoules = 0.0;
    }

    // @description - shared cone quantities of the working gear for this step
    // @param {TransmissionStepInput} input - inputs
    // @param {double} speedErrorAtInput - (out) input speed minus the speed the gear demands, rad/s
    // @param {double} coneTorque - (out) available cone torque in N m (at the cone)
    // @param {double} blockingTorque - (out) blocking (index) torque in N m
    // @return {void}
    void ConeQuantities(in TransmissionStepInput input, out double speedErrorAtInput, out double coneTorque, out double blockingTorque)
    {
        int gear = WorkingGear; int index = gear - 1; double pinionSpeed = input.carrierAngularVelocity * geometry.FinalDriveRatio;
        speedErrorAtInput = input.inputShaftAngularVelocity - geometry.GearboxRatio(gear) * pinionSpeed;
        double coneRadius = parameters.meanConeRadiusMeters[index], coneSpeed = speedErrorAtInput * geometry.ConeSpeedReferenceFromInput(gear);
        double friction = SynchronizerPhysics.ConeFriction(parameters, coneStuck ? 0.0 : coneRadius * System.Math.Abs(coneSpeed)), force = linkage.PushForceAtSleeve();
        bool ringClamps = ringGapMeters[index] > parameters.ringGapWearLimitMeters;   // S: at the wear limit the ring bottoms out and the cone no longer clamps
        coneTorque = ringClamps ? SynchronizerPhysics.ConeTorque(force, friction, coneRadius, parameters.coneHalfAngleDegrees * Mathf.Deg2Rad) : 0.0;
        blockingTorque = SynchronizerPhysics.BlockingTorque(force, coneRadius * parameters.blockingToothToConeRadiusRatio, parameters.roofIncludedAngleDegrees[index] * Mathf.Deg2Rad, parameters.blockingToothFrictionCoefficient);
        LastConeTorqueNewtonMeters = coneTorque; LastBlockingTorqueNewtonMeters = blockingTorque;
    }

    // @description - synchronizing: the sleeve pushes the ring against the cone. Blocking holds while the cone torque is at least the index torque; the cone then drags the input to the
    //   gear speed. When the speeds match the cone sticks; the ring unblocks only if the torque the cone still carries is below the index torque.
    // @param {TransmissionStepInput} input - inputs
    // @param {TransmissionStepResult} result - result being built
    // @return {Void}
    void StepSynchronizing(in TransmissionStepInput input, ref TransmissionStepResult result)
    {
        if (RequestedGear != WorkingGear) { CancelEngagement(); return; }
        ConeQuantities(input, out double speedError, out double coneTorque, out double blockingTorque);
        if (coneTorque < blockingTorque) { ScratchCount++; EnterFreeFlight(); ApplySlippingCone(input, ref result, speedError, coneTorque); return; }   // the ring cannot block: scratch, the sleeve passes unsynchronized
        if (coneStuck) { StepStuckCone(input, coneTorque, blockingTorque); return; }
        SlipOrStick(input, ref result, speedError, coneTorque);
    }

    // @description - the cone holds the speeds together: unstick when the load exceeds the cone, unblock when it falls below the index torque
    // @param {TransmissionStepInput} input - inputs
    // @param {double} coneTorque - N m available at the cone
    // @param {double} blockingTorque - N m
    // @return {Void}
    void StepStuckCone(in TransmissionStepInput input, double coneTorque, double blockingTorque)
    {
        double needed = System.Math.Abs(input.gearTrainInputTorque) / geometry.ConeTorqueReferenceToInput(WorkingGear);
        if (needed > coneTorque) { coneStuck = false; slipDirectionHint = System.Math.Sign(input.gearTrainInputTorque); return; }
        if (needed < blockingTorque) { LastSynchronizationSeconds = clock - synchronizationStartTime; EnterFreeFlight(); }
    }

    // @description - slipping cone: apply the cone torque against the speed error, or stick when one step of cone torque can remove the whole error (Karnopp dead band)
    // @param {TransmissionStepInput} input - inputs
    // @param {TransmissionStepResult} result - result being built
    // @param {double} speedError - input speed error in rad/s
    // @param {double} coneTorque - N m
    // @return {Void}
    void SlipOrStick(in TransmissionStepInput input, ref TransmissionStepResult result, double speedError, double coneTorque)
    {
        double referenceToInput = geometry.ConeTorqueReferenceToInput(WorkingGear), deadBand = parameters.stickDeadBandSafetyFactor * coneTorque * referenceToInput * input.dt / input.inputNodeInertiaKilogramMetersSquared;
        if (System.Math.Abs(speedError) <= deadBand) { coneStuck = true; return; }
        ApplySlippingCone(input, ref result, speedError, coneTorque);
    }

    // @description - torque of a slipping cone on both ports, limited so that it cannot reverse the speed error within the step; also books the dissipated energy
    // @param {TransmissionStepInput} input - inputs
    // @param {TransmissionStepResult} result - result being built
    // @param {double} speedError - input speed error in rad/s
    // @param {double} coneTorque - N m at the cone
    // @return {Void}
    void ApplySlippingCone(in TransmissionStepInput input, ref TransmissionStepResult result, double speedError, double coneTorque)
    {
        double direction = speedError != 0.0 ? System.Math.Sign(speedError) : slipDirectionHint, referenceToInput = geometry.ConeTorqueReferenceToInput(WorkingGear);
        double magnitude = System.Math.Min(coneTorque * referenceToInput, input.inputNodeInertiaKilogramMetersSquared * System.Math.Abs(speedError) / input.dt);
        result.inputShaftExternalTorque -= direction * magnitude;
        result.carrierExternalTorque += direction * magnitude * geometry.TotalRatio(WorkingGear);
        double dissipated = magnitude * System.Math.Abs(speedError) * input.dt; coneHeatThisStep += dissipated; LastConeDissipationJoules += dissipated;
    }

    // @description - the ring is unblocked or has been passed: the sleeve moves on to the dog chamfers
    // @return {Void}
    void EnterFreeFlight() { Phase = GearboxPhase.FreeFlight; phaseTimer = linkage.SleeveTravelTime(parameters.postUnblockSleeveTravelMeters); }

    // @description - free flight: the cone stays pressed (it slips if the load is above it); at the end the dogs are evaluated
    // @param {TransmissionStepInput} input - inputs
    // @param {TransmissionStepResult} result - result being built
    // @return {Void}
    void StepFreeFlight(in TransmissionStepInput input, ref TransmissionStepResult result)
    {
        if (RequestedGear != WorkingGear) { CancelEngagement(); return; }
        ConeQuantities(input, out double speedError, out double coneTorque, out _);
        if (!coneStuck && coneTorque > 0.0) ApplySlippingCone(input, ref result, speedError, coneTorque);
        phaseTimer -= input.dt; if (phaseTimer > 0.0) return;
        EvaluateDogCrossing(input, speedError);
    }

    // @description - sleeve reached the dog chamfers: tooth-on-tooth clash when the teeth slip past faster than the chamfers can index while the sleeve crosses, otherwise engage
    //   (with a double bump when the dogs are far from aligned and must index)
    // @param {TransmissionStepInput} input - inputs
    // @param {double} speedErrorAtInput - rad/s
    // @return {Void}
    void EvaluateDogCrossing(in TransmissionStepInput input, double speedErrorAtInput)
    {
        double crossingTime = linkage.SleeveTravelTime(parameters.dogChamferTravelMeters), dogSlip = System.Math.Abs(speedErrorAtInput * geometry.ConeSpeedReferenceFromInput(WorkingGear));
        double capture = SynchronizerPhysics.CaptureAngle(parameters.dogToothCount, parameters.dogChamferCaptureFraction);
        if (dogSlip * crossingTime > capture) { Clash(input.inputNodeInertiaKilogramMetersSquared, speedErrorAtInput, crossingTime); return; }
        double pitch = 2.0 * System.Math.PI / parameters.dogToothCount, phase = dogRelativeAngle[WorkingGear - 1] / pitch, misalignment = System.Math.Min(phase, 1.0 - phase);
        if (misalignment > parameters.dogChamferCaptureFraction) { DoubleBumpCount++; crossingTime *= 2.0; }   // the chamfers touch tooth on tooth and must index the dogs round before the sleeve goes in
        Phase = GearboxPhase.DogCrossing; phaseTimer = crossingTime;
    }

    // @description - a clash: the impact energy of the speed difference is booked, the sleeve is thrown back and the driver pushes again
    // @param {double} inertia - input node inertia in kg m^2
    // @param {double} speedErrorAtInput - rad/s at the input shaft
    // @param {double} bounceTime - s the sleeve needs to come back
    // @return {Void}
    void Clash(double inertia, double speedErrorAtInput, double bounceTime)
    {
        GrindCount++; grindEnergyJoules += SynchronizerPhysics.ImpactEnergy(inertia, speedErrorAtInput); Phase = GearboxPhase.Grinding; phaseTimer = bounceTime; coneStuck = false;
    }

    // @description - dog crossing: after the crossing time the dogs are in
    // @param {double} dt - s
    // @return {Void}
    void StepDogCrossing(double dt)
    {
        phaseTimer -= dt; if (phaseTimer > 0.0) return;
        EngagedGear = WorkingGear; dogsIn = true; coneStuck = false; Phase = GearboxPhase.Engaged; CompletedShiftCount++;
        if (LastSynchronizationSeconds <= 0.0) LastSynchronizationSeconds = clock - synchronizationStartTime;
    }

    // @description - grinding: the sleeve comes back and pushes again (forward gears go back to the ring, reverse tries the idler again)
    // @param {TransmissionStepInput} input - inputs
    // @return {Void}
    void StepGrinding(in TransmissionStepInput input)
    {
        if (RequestedGear != WorkingGear) { CancelEngagement(); return; }
        phaseTimer -= input.dt; if (phaseTimer > 0.0) return;
        if (WorkingGear == GearSetGeometry.ReverseGear) { TryReverseEngagement(input); return; }
        Phase = GearboxPhase.Synchronizing;
    }

    // @description - reverse idler (no synchronizer): the idler teeth slide into the rotating gears; it meshes when the teeth slip past slower than the chamfers can index
    // @param {TransmissionStepInput} input - inputs
    // @return {Void}
    void TryReverseEngagement(in TransmissionStepInput input)
    {
        double pinionSpeed = input.carrierAngularVelocity * geometry.FinalDriveRatio, speedError = input.inputShaftAngularVelocity - geometry.GearboxRatio(GearSetGeometry.ReverseGear) * pinionSpeed;
        double crossingTime = linkage.SleeveTravelTime(parameters.reverseChamferTravelMeters), capture = SynchronizerPhysics.CaptureAngle(parameters.reverseInputToothCount, parameters.reverseToothCaptureFraction);
        if (System.Math.Abs(speedError) * crossingTime > capture) { Clash(input.inputNodeInertiaKilogramMetersSquared, speedError, crossingTime); return; }
        Phase = GearboxPhase.DogCrossing; phaseTimer = crossingTime; coneStuck = false;
    }

    // @description - the driver gave up or changed the request while the sleeve was out: it goes back to neutral
    // @return {Void}
    void CancelEngagement() { Phase = GearboxPhase.Neutral; WorkingGear = 0; coneStuck = false; }

    // @description - the ring gap of a gear (wear state); a gap at or below the wear limit no longer clamps the cone
    // @param {int} gear - 1..5
    // @param {double} gapMeters - m
    // @return {Void}
    public void SetRingGap(int gear, double gapMeters) { ringGapMeters[gear - 1] = gapMeters; }
}
