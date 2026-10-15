// Single handling baseline (2026-10-13): the former 'ProposedUndersteer' inputs are the only profile; the P-key toggle is retired
// and old saved input settings migrate cleanly. Values are ESTIMATES (E), chosen by Taylor for the understeer balance.
using NUnit.Framework; using UnityEngine;
public class SingleBaselineTests
{
    [Test] public void Defaults_Are_The_Former_Proposed_Inputs()
    {
        var p = ScriptableObject.CreateInstance<CarParams>();
        Assert.AreEqual(7200f, p.frontAntiRollBarRate); Assert.AreEqual(8520f, p.twistBeamRollRate);
        Assert.AreEqual(12000f, p.steeringStiffness); Assert.AreEqual(0.035f, p.pneumaticTrailAtZeroSlip, 1e-6f);
        Assert.AreEqual(15500f, p.frontWheelRate); Assert.AreEqual(11900f, p.rearWheelRate);   // spring/wheel rates unchanged
        Assert.IsNull(typeof(CarParams).GetNestedType("HandlingProfile"), "no HandlingProfile enum");
        Assert.IsNull(typeof(CarParams).GetField("kinProposed"), "no second table set"); Assert.IsNull(typeof(CarParams).GetField("profile"));
        Assert.IsNull(typeof(CarController).GetMethod("ToggleProfile"), "no profile switch");
        var r = p.RuntimeCopy(); Assert.AreNotSame(p, r); Assert.AreEqual(p.frontAntiRollBarRate, r.frontAntiRollBarRate);
    }
    [Test] public void Tables_Are_Resolved_With_The_Single_Baseline_Hardpoints()
    {
        var k = ScriptableObject.CreateInstance<SuspensionTables>(); SuspensionTablesData.Fill(k);
        // re-solve 2026-10-13: front lower-arm pivots back at the original height, where the sourced tie-rod inner point gives
        // near-zero bump steer (solver: +0.55 deg/m; the dropped +20 mm estimate gave -18.4 deg/m)
        const float BumpWindowM = 0.04f, MaxBumpSteerDegPerM = 2f;
        int lo = System.Array.FindIndex(k.travel, t => t >= -BumpWindowM - 1e-4f), hi = System.Array.FindLastIndex(k.travel, t => t <= BumpWindowM + 1e-4f);
        float slope = (k.frontBumpSteer[hi] - k.frontBumpSteer[lo]) / (k.travel[hi] - k.travel[lo]);
        Assert.AreEqual(0f, slope, MaxBumpSteerDegPerM, "front bump steer near zero with the sourced tie rod");
        Assert.AreEqual(0.055f, k.rearBeamRollCentreHeight, 0.002f, "rear RC with pivot -30 mm / shear centre -2.06 m (re-solve: -2.00 dropped)");
        Assert.AreEqual(0.596f, k.rearBeamAntiLiftCoefficient, 0.01f);
        Assert.Greater(k.rackGrid.Length, 10, "steering grid present"); Assert.AreEqual(k.rackGrid.Length * k.travel.Length, k.frontToeTravelRack.Length);
    }
}
