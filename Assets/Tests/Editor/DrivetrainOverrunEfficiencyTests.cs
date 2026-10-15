// Energy balance of the drivetrain in both power flow directions (power: engine drives the wheels; overrun: the wheels drive the engine, engine braking).
// The input power minus the output power is the power lost in the clutch slip and the gears; it must never be negative (no power created from nothing).
using NUnit.Framework; using UnityEngine;
public class DrivetrainOverrunEfficiencyTests
{
    const float TimeStepSeconds = 0.001f, WheelInertiaKilogramSquareMeters = 0.9f, ClutchPedalEngaged = 0f, RadiansPerSecondPerRpm = Mathf.PI / 30f;
    const float PowerToleranceWatts = 1e-3f, TorqueToleranceNewtonMeters = 1e-4f, RelativeTolerance = 1e-5f;
    const int WheelCount = 2;
    static readonly int[] DrivenGears = { 1, 2, 3, 4, 5, -1 };
    static readonly float[] WheelSpeedsRadPerSecond = { 5f, 40f, 90f, 130f };
    static readonly float[] SlipFractionsOfEngineSpeed = { -0.05f, -0.01f, -0.001f, 0.001f, 0.01f, 0.05f };

    // @description - creates a drivetrain with default car parameters in the given gear, with the shift timer already expired
    // @param {int} gear - -1 = reverse, 1..5 = forward
    // @param {CarParams} carParams - (out) the car parameters the drivetrain uses
    // @return {Drivetrain} - drivetrain ready to transmit torque
    static Drivetrain CreateDrivetrainInGear(int gear, out CarParams carParams)
    {
        carParams = ScriptableObject.CreateInstance<CarParams>(); var drivetrain = new Drivetrain(carParams);
        drivetrain.SelectGear(gear); drivetrain.shiftTimer = 0f; return drivetrain;
    }

    // @description - runs one clutch step at a crank speed offset from the wheel-side speed by a slip fraction and returns the powers on both sides of the drivetrain
    // @param {Drivetrain} drivetrain - drivetrain in a forward or reverse gear
    // @param {float} wheelAngularVelocity - mean driven-wheel speed in rad/s (sign follows the gear: negative in reverse)
    // @param {float} slipFraction - crank speed minus wheel-side speed, as a fraction of the wheel-side speed (positive = power, negative = overrun)
    // @param {float} clutchTorque - (out) torque the clutch takes off the crankshaft in N m
    // @param {float} wheelTorqueEach - (out) torque on each driven wheel in N m
    // @param {float} crankSidePower - (out) power the clutch takes off the crankshaft in W (positive = engine gives power)
    // @param {float} wheelSidePower - (out) power delivered to the driven wheels in W (positive = wheels receive power, negative = wheels give power)
    // @return {Void}
    static void StepClutch(Drivetrain drivetrain, float wheelAngularVelocity, float slipFraction, out float clutchTorque, out float wheelTorqueEach, out float crankSidePower, out float wheelSidePower)
    {
        float wheelSideSpeed = wheelAngularVelocity * drivetrain.TotalRatio, engineAngularVelocity = wheelSideSpeed * (1f + slipFraction);
        clutchTorque = drivetrain.Clutch(engineAngularVelocity, wheelAngularVelocity, ClutchPedalEngaged, TimeStepSeconds, WheelInertiaKilogramSquareMeters, out wheelTorqueEach);
        crankSidePower = clutchTorque * engineAngularVelocity; wheelSidePower = WheelCount * wheelTorqueEach * wheelAngularVelocity;
    }

    // @description - Verifies energy balance when the engine drives the wheels (power): input power (crank) minus output power (wheels) is the loss and is >= 0, for every gear, wheel speed and slip.
    //   The loss is the clutch slip heat plus the gear loss, and the gear part of it is exactly (1 - efficiency) x the power entering the gears.
    // @return {Void}
    [Test] public void Power_Input_Minus_Output_Equals_Non_Negative_Loss()
    {
        foreach (int gear in DrivenGears) foreach (float wheelSpeed in WheelSpeedsRadPerSecond) foreach (float slipFraction in SlipFractionsOfEngineSpeed)
        {
            var drivetrain = CreateDrivetrainInGear(gear, out var carParams); float signedWheelSpeed = gear < 0 ? -wheelSpeed : wheelSpeed;
            StepClutch(drivetrain, signedWheelSpeed, slipFraction, out float clutchTorque, out float wheelTorqueEach, out float crankSidePower, out float wheelSidePower);
            if (clutchTorque <= 0f) continue;   // only the power direction in this test
            float loss = crankSidePower - wheelSidePower;
            Assert.GreaterOrEqual(loss, -PowerToleranceWatts, $"power created from nothing: gear {gear} wheel {wheelSpeed} rad/s slip {slipFraction}");
            // split the loss: the clutch slip heat (crank speed above the gearbox input speed) and the gear loss, exactly (1 - efficiency) x the power entering the gears
            float gearboxInputSpeed = signedWheelSpeed * drivetrain.TotalRatio, powerEnteringGears = clutchTorque * gearboxInputSpeed, clutchSlipLoss = crankSidePower - powerEnteringGears;
            Assert.GreaterOrEqual(clutchSlipLoss, -PowerToleranceWatts, "clutch slip heat is non-negative"); Assert.AreEqual((1f - carParams.drivetrainEfficiency) * powerEnteringGears, powerEnteringGears - wheelSidePower, Mathf.Abs(powerEnteringGears) * RelativeTolerance + PowerToleranceWatts, "gear loss = (1 - efficiency) x power entering the gears");
        }
    }

    // @description - Verifies energy balance when the wheels drive the engine (overrun, engine braking): input power (wheels give) minus output power (engine absorbs) is the loss and is >= 0.
    //   The old code scaled wheel torque by efficiency in both directions, so the wheels gave up only efficiency x what the engine absorbed and this loss came out negative (about -10 %).
    //   The gear part of the loss is exactly (1 - efficiency) x the power the wheels give.
    // @return {Void}
    [Test] public void Overrun_Input_Minus_Output_Equals_Non_Negative_Loss()
    {
        int overrunSamples = 0;
        foreach (int gear in DrivenGears) foreach (float wheelSpeed in WheelSpeedsRadPerSecond) foreach (float slipFraction in SlipFractionsOfEngineSpeed)
        {
            var drivetrain = CreateDrivetrainInGear(gear, out var carParams); float signedWheelSpeed = gear < 0 ? -wheelSpeed : wheelSpeed;
            StepClutch(drivetrain, signedWheelSpeed, slipFraction, out float clutchTorque, out float wheelTorqueEach, out float crankSidePower, out float wheelSidePower);
            if (clutchTorque >= 0f) continue;   // only the overrun direction in this test
            overrunSamples++;
            float powerGivenByWheels = -wheelSidePower, powerAbsorbedByEngine = -crankSidePower, loss = powerGivenByWheels - powerAbsorbedByEngine;
            Assert.Greater(powerGivenByWheels, 0f, "wheels must be the power source on overrun");
            Assert.GreaterOrEqual(loss, -PowerToleranceWatts, $"overrun creates power from nothing: gear {gear} wheel {wheelSpeed} rad/s slip {slipFraction} (wheels give {powerGivenByWheels:0.0} W, engine absorbs {powerAbsorbedByEngine:0.0} W)");
            // split the loss: the gear loss is exactly (1 - efficiency) x the power the wheels give; the rest is clutch slip heat (gearbox input speed above the crank speed)
            float gearboxInputSpeed = signedWheelSpeed * drivetrain.TotalRatio, powerLeavingGears = -clutchTorque * gearboxInputSpeed, clutchSlipLoss = powerLeavingGears - powerAbsorbedByEngine;
            Assert.GreaterOrEqual(clutchSlipLoss, -PowerToleranceWatts, "clutch slip heat is non-negative"); Assert.AreEqual((1f - carParams.drivetrainEfficiency) * powerGivenByWheels, powerGivenByWheels - powerLeavingGears, powerGivenByWheels * RelativeTolerance + PowerToleranceWatts, "gear loss = (1 - efficiency) x power from the wheels");
        }
        Assert.Greater(overrunSamples, 0, "the test must have exercised overrun");
    }

    // @description - Verifies the torque numbers directly: power torque = crank torque x ratio x efficiency / 2 per wheel (unchanged), overrun torque = crank torque x ratio / efficiency / 2 per wheel, zero in neutral.
    // @return {Void}
    [Test] public void Wheel_Torque_Uses_Efficiency_In_The_Direction_Of_The_Power_Flow()
    {
        const float CrankTorqueNewtonMeters = 100f;
        foreach (int gear in DrivenGears)
        {
            var drivetrain = CreateDrivetrainInGear(gear, out var carParams); float ratio = drivetrain.TotalRatio, efficiency = carParams.drivetrainEfficiency;
            Assert.AreEqual(CrankTorqueNewtonMeters * ratio * efficiency / WheelCount, drivetrain.WheelTorqueEach(CrankTorqueNewtonMeters), TorqueToleranceNewtonMeters, $"power, gear {gear}");
            Assert.AreEqual(-CrankTorqueNewtonMeters * ratio / efficiency / WheelCount, drivetrain.WheelTorqueEach(-CrankTorqueNewtonMeters), TorqueToleranceNewtonMeters, $"overrun, gear {gear}");
        }
        var neutral = new Drivetrain(ScriptableObject.CreateInstance<CarParams>());
        Assert.AreEqual(0f, neutral.WheelTorqueEach(CrankTorqueNewtonMeters), TorqueToleranceNewtonMeters); Assert.AreEqual(0f, neutral.WheelTorqueEach(-CrankTorqueNewtonMeters), TorqueToleranceNewtonMeters);
    }

    // @description - Verifies a round trip through the gears loses power in each direction and that the overrun wheel torque is exactly 1 / efficiency^2 times what the same
    //   crank torque would deliver as power (the same gear loss, applied the other way round), and that the efficiency value itself is untouched at 0.90.
    // @return {Void}
    [Test] public void Overrun_Wheel_Torque_Is_Inverse_Efficiency_Squared_Times_Power_Wheel_Torque_And_Efficiency_Is_Unchanged()
    {
        const float CrankTorqueNewtonMeters = 80f, TaggedEfficiency = 0.90f;
        var drivetrain = CreateDrivetrainInGear(3, out var carParams);
        Assert.AreEqual(TaggedEfficiency, carParams.drivetrainEfficiency, 0f, "drivetrainEfficiency is the tagged (E) value and is deliberately not changed by the overrun fix");
        float powerWheelTorque = drivetrain.WheelTorqueEach(CrankTorqueNewtonMeters), overrunWheelTorque = -drivetrain.WheelTorqueEach(-CrankTorqueNewtonMeters);
        Assert.AreEqual(1f / (TaggedEfficiency * TaggedEfficiency), overrunWheelTorque / powerWheelTorque, RelativeTolerance * 10f);
    }
}
