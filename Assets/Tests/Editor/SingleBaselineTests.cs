// Single handling baseline (2026-10-13): the former 'ProposedUndersteer' inputs are the only profile; the P-key toggle is retired
// and old saved input settings migrate cleanly. Values are ESTIMATES (E), chosen by Taylor for the understeer balance.
using NUnit.Framework; using UnityEngine;
public class SingleBaselineTests
{
    // @description - Verifies that the default car parameters hold the former proposed handling values (anti-roll bar 7200 N m/rad, twist-beam roll rate 8520, steering stiffness 12000, pneumatic trail 0.035 m, unchanged wheel rates) and that the old HandlingProfile enum, kinProposed table set and ToggleProfile switch are gone.
    // @return {Void} - nothing
    [Test] public void Defaults_Are_The_Former_Proposed_Inputs()
    {
        var carParams = ScriptableObject.CreateInstance<CarParams>();
        Assert.AreEqual(7200f, carParams.frontAntiRollBarRate); Assert.AreEqual(8520f, carParams.twistBeamRollRate);
        Assert.AreEqual(12000f, carParams.steeringStiffness); Assert.AreEqual(0.035f, carParams.pneumaticTrailAtZeroSlip, 1e-6f);
        Assert.AreEqual(15500f, carParams.frontWheelRate); Assert.AreEqual(11900f, carParams.rearWheelRate);   // spring/wheel rates unchanged
        Assert.IsNull(typeof(CarParams).GetNestedType("HandlingProfile"), "no HandlingProfile enum");
        Assert.IsNull(typeof(CarParams).GetField("kinProposed"), "no second table set"); Assert.IsNull(typeof(CarParams).GetField("profile"));
        Assert.IsNull(typeof(CarController).GetMethod("ToggleProfile"), "no profile switch");
        var runtimeCopy = carParams.RuntimeCopy(); Assert.AreNotSame(carParams, runtimeCopy); Assert.AreEqual(carParams.frontAntiRollBarRate, runtimeCopy.frontAntiRollBarRate);
    }
    // @description - Verifies that the baked suspension tables use the single baseline hardpoints: front bump steer within +-2 deg/m over +-40 mm of travel, rear beam roll center 0.055 m, anti-lift coefficient 0.596, and a steering-rack grid of the expected size.
    // @return {Void} - nothing
    [Test] public void Tables_Are_Resolved_With_The_Single_Baseline_Hardpoints()
    {
        var suspensionTables = ScriptableObject.CreateInstance<SuspensionTables>(); SuspensionTablesData.Fill(suspensionTables);
        // re-solve 2026-10-13: front lower-arm pivots back at the original height, where the sourced tie-rod inner point gives
        // near-zero bump steer (solver: +0.55 deg/m; the dropped +20 mm estimate gave -18.4 deg/m)
        const float BumpWindowM = 0.04f, MaxBumpSteerDegPerM = 2f;
        int lowIndex = System.Array.FindIndex(suspensionTables.travel, travelValue => travelValue >= -BumpWindowM - 1e-4f), highIndex = System.Array.FindLastIndex(suspensionTables.travel, travelValue => travelValue <= BumpWindowM + 1e-4f);
        float slope = (suspensionTables.frontBumpSteer[highIndex] - suspensionTables.frontBumpSteer[lowIndex]) / (suspensionTables.travel[highIndex] - suspensionTables.travel[lowIndex]);
        Assert.AreEqual(0f, slope, MaxBumpSteerDegPerM, "front bump steer near zero with the sourced tie rod");
        Assert.AreEqual(0.055f, suspensionTables.rearBeamRollCenterHeight, 0.002f, "rear RC with pivot -30 mm / shear center -2.06 m (re-solve: -2.00 dropped)");
        Assert.AreEqual(0.596f, suspensionTables.rearBeamAntiLiftCoefficient, 0.01f);
        Assert.Greater(suspensionTables.rackGrid.Length, 10, "steering grid present"); Assert.AreEqual(suspensionTables.rackGrid.Length * suspensionTables.travel.Length, suspensionTables.frontToeTravelRack.Length);
    }
}
