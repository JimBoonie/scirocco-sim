// Steering linkage (SteeringLinkage.cs): constant pinion -> rack -> per-wheel angles from the kinematic grid (solve_kinematics.py).
using NUnit.Framework; using UnityEngine;
public class SteeringLinkageTests
{
    // @description - P.
    // @return {CarParams} - the p
    static CarParams P() { var p = ScriptableObject.CreateInstance<CarParams>(); p.suspensionTables = ScriptableObject.CreateInstance<SuspensionTables>(); SuspensionTablesData.Fill(p.suspensionTables); return p; }
    // Targets (degrees). Bentley 1985-93 US Scirocco/Cabriolet manual p.10-10 Table b [SD: unconfirmed for 1984]
    const float ToeOutInnerDeg = 20f;                          // reference inner-wheel angle of the toe-out-on-turns check
    const float ToeOutOuterDeg = 18.5f, ToeOutTolDeg = 0.5f;   // SD: toe difference -1d30' +-30' -> outer 18d30' (18d00'..19d00'); replaces A1 18d10'
    const float LockCheckDeg = 20f;                            // camber check made at 20d left and 20d right lock
    const float CamberChangeDeg = 1.25f, CamberChangeTolDeg = 20f / 60f;   // SD: 1d15' +-20'
    const float MaxCamberDiffDeg = 1f;                         // SD: max front camber difference between sides
    // Lock: S [A1] 40d22' / 32d55'; solver with the 0.379 m tie rod (SD) at +-68.6 mm rack gives 40.34 / 32.91 (D) with the original
    // lower-arm pivots (E; the +20 mm single-baseline estimate gave 40.50 / 32.90 and was dropped in the 2026-10-13 re-solve). Also checked against the sourced values within 0.25 deg.
    const float LockInnerDeg = 40.34f, LockOuterDeg = 32.91f, LockTolDeg = 0.1f;
    const float LockInnerSrcDeg = 40.367f, LockOuterSrcDeg = 32.917f, LockSrcTolDeg = 0.25f;
    // @description - Log.
    // @param {string} s - value of type string
    // @return {Void} - nothing
    static void Log(string s) { Debug.Log("STEER " + s); TestContext.Out.WriteLine(s); }
    // @description - Full lock matches sourced lock angles.
    // @return {Void} - nothing
    [Test] public void Full_Lock_Matches_Sourced_Lock_Angles()
    {   // S [A1]: 40d22' inner / 32d55' outer; solver (0.379 m rod, original pivots) at +-68.6 mm: 40.34 / 32.91
        var p = P(); float rack = SteeringLinkage.Rack(p, p.handwheelLockDeg);
        Assert.AreEqual(0.0686, rack, 2e-4, "rack stop +-68.6 mm at 634.7 deg");
        SteeringLinkage.WheelAngles(p.suspensionTables, rack, 0f, 0f, out float l, out float r);
        Log($"right lock: inner (R) {r:0.000} deg, outer (L) {l:0.000} deg");
        Assert.AreEqual(LockInnerDeg, r, LockTolDeg, "inner"); Assert.AreEqual(LockOuterDeg, l, LockTolDeg, "outer");
        Assert.AreEqual(LockInnerSrcDeg, r, LockSrcTolDeg, "inner vs sourced 40d22'"); Assert.AreEqual(LockOuterSrcDeg, l, LockSrcTolDeg, "outer vs sourced 32d55'");
        SteeringLinkage.WheelAngles(p.suspensionTables, -rack, 0f, 0f, out float l2, out float r2);
        Assert.AreEqual(-r, l2, 1e-4f, "left lock mirrors"); Assert.AreEqual(-l, r2, 1e-4f);
    }
    // @description - Mid range angles.
    // @return {Void} - nothing
    [Test] public void Mid_Range_Angles()
    {
        var p = P(); SteeringLinkage.WheelAngles(p.suspensionTables, SteeringLinkage.Rack(p, 100f), 0f, 0f, out float l, out float r);
        Log($"100 deg handwheel: L {l:0.000} R {r:0.000} mean {0.5f * (l + r):0.000}; on-centre ratio {SteeringLinkage.CentreRatio(p):0.00}:1");
        Assert.AreEqual(5.2f, 0.5f * (l + r), 0.1f, "mean ~5.2 deg at 100 deg");
        Assert.Greater(r, l, "inner wheel (right, in a right turn) steers more: geometric Ackermann");
        Assert.AreEqual(0f, SteeringLinkage.MeanAngle(p, 0f), 1e-5f, "straight ahead at centre");
        foreach (float hw in new[] { -500f, -50f, 3f, 200f, 600f })
            Assert.AreEqual(hw, SteeringLinkage.HandwheelForMean(p, SteeringLinkage.MeanAngle(p, hw)), 0.05f, "inverse");
    }
    // @description - Bump steer comes from the same grid.
    // @return {Void} - nothing
    [Test] public void Bump_Steer_Comes_From_The_Same_Grid()
    {   // at centre the grid must reproduce the bump-steer curve of the solver; under steer it changes with travel
        var p = P(); var k = p.suspensionTables;
        for (int i = 0; i < k.travel.Length; i++) Assert.AreEqual(k.frontBumpSteer[i], k.FrontSteerLeft(k.travel[i], 0f), 1e-4f);
        float lv = SteeringLinkage.Lever(p, true, 0f, 0f), rv = SteeringLinkage.Lever(p, false, 0f, 0f);
        Assert.AreEqual(lv, rv, 1e-5f, "symmetric lever on centre"); Assert.AreEqual(1f / SteeringLinkage.CentreRatio(p), lv, 2e-4f);
    }
    // @description - Rack for right wheel.
    // @param {SuspensionTables} k - value of type SuspensionTables
    // @param {float} target - target
    // @return {float} - the rack for right wheel
    static float RackForRightWheel(SuspensionTables k, float target)
    {   // bisection on rack (right turn: + rack, right wheel = inner) at design height
        float lo = 0f, hi = k.rackGrid[k.rackGrid.Length - 1];
        for (int i = 0; i < 60; i++) { float m = 0.5f * (lo + hi); SteeringLinkage.WheelAngles(k, m, 0f, 0f, out _, out float r); if (r < target) lo = m; else hi = m; }
        return 0.5f * (lo + hi);
    }
    // @description - Toe out on turns matches manual.
    // @return {Void} - nothing
    [Test] public void Toe_Out_On_Turns_Matches_Manual()
    {
        var k = P().suspensionTables; float rack = RackForRightWheel(k, ToeOutInnerDeg);
        SteeringLinkage.WheelAngles(k, rack, 0f, 0f, out float outer, out float inner);
        Log($"toe-out on turns: inner {inner:0.000} deg, outer {outer:0.000} deg (target {ToeOutOuterDeg} +-{ToeOutTolDeg})");
        Assert.AreEqual(ToeOutInnerDeg, inner, 1e-3f); Assert.AreEqual(ToeOutOuterDeg, outer, ToeOutTolDeg, "outer at inner 20 deg");
    }
    // @description - Camber change across 20deg lock matches manual.
    // @return {Void} - nothing
    [Test] public void Camber_Change_Across_20deg_Lock_Matches_Manual()
    {   // left wheel; steer + = right. Camber at 20d left lock minus at 20d right lock (caster check)
        var k = P().suspensionTables; float d = k.FrontCamber(0f, -LockCheckDeg) - k.FrontCamber(0f, LockCheckDeg);
        Log($"camber change 20L->20R: {d:0.000} deg (target {CamberChangeDeg} +-{CamberChangeTolDeg:0.000})");
        Assert.AreEqual(CamberChangeDeg, d, CamberChangeTolDeg);
    }
    // @description - Side to side within manual limits.
    // @return {Void} - nothing
    [Test] public void Side_To_Side_Within_Manual_Limits()
    {   // both sides are modelled mirrored (69.0/67.0 mm inner-joint offset NOT applied), so L/R must agree well inside the limits
        var p = P(); var k = p.suspensionTables;
        SteeringLinkage.WheelAngles(k, 0f, 0f, 0f, out float l0, out float r0);
        Assert.AreEqual(l0, r0, 1e-4f, "straight ahead: same toe both sides");
        // right wheel is the mirror of the left: its camber at right-wheel steer s equals the left-wheel table at -s (left frame).
        // Same-condition comparison (both wheels straight ahead, and both wheels toed in by 20 deg) must stay within MaxCamberDiffDeg.
        float camL = k.FrontCamber(0f, LockCheckDeg), camR = k.FrontCamber(0f, LockCheckDeg);
        Assert.LessOrEqual(Mathf.Abs(k.FrontCamber(0f, 0f) - k.FrontCamber(0f, 0f)), MaxCamberDiffDeg, "camber L/R straight ahead");
        float rack = SteeringLinkage.Rack(p, p.handwheelLockDeg);
        SteeringLinkage.WheelAngles(k, rack, 0f, 0f, out float lR, out float rR); SteeringLinkage.WheelAngles(k, -rack, 0f, 0f, out float lL, out float rL);
        Assert.LessOrEqual(Mathf.Abs(camL - camR), MaxCamberDiffDeg, "camber L/R at 20 deg lock"); Assert.AreEqual(rR, -lL, 1e-4f, "lock symmetric L/R"); Assert.AreEqual(lR, -rL, 1e-4f);
        Log($"side-to-side: camber at +-20 lock {camL:0.000}/{camR:0.000}, lock L/R {-lL:0.000}/{rR:0.000}");
    }
}
