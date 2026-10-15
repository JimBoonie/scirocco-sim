// Compares SteeringTorque (C#) with the independent Python reference ffb/steering_torque_ref.py (cases in steer_ref.json).
using NUnit.Framework; using UnityEngine;
public class SteeringTorqueTests
{
    [System.Serializable] class G { public float[] u, r_cp, r_wc; }
    [System.Serializable] class Case { public bool left, braking, lever_case; public float steer, fx, fy, fz, tp, expected, d, leverL, leverR; }
    [System.Serializable] class Ref { public G geom; public Case[] cases; }
    // @description - Matches python reference.
    // @return {Void} - nothing
    [Test] public void Matches_Python_Reference()
    {
        var r = JsonUtility.FromJson<Ref>(System.IO.File.ReadAllText(System.IO.Path.Combine(Application.dataPath, "Tests/Editor/steer_ref.json")));
        var k = ScriptableObject.CreateInstance<SuspensionTables>(); k.kingpinAxis = r.geom.u; k.kingpinContactPatchOffset = r.geom.r_cp; k.kingpinWheelCentreOffset = r.geom.r_wc;
        var g = SteeringTorque.FromTables(k); int n = 0;
        foreach (var c in r.cases)
        {
            if (c.lever_case) continue;   // reference lever cases describe the removed linear-ratio + Ackermann-fit model; the kinematic lever is tested in SteeringLinkageTests
            float t = SteeringTorque.RoadTorque(g, c.left, c.steer, c.fx, c.braking, c.fy, c.fz, c.tp);
            Assert.AreEqual(c.expected, t, 1e-3 + 1e-4 * Mathf.Abs(c.expected), $"case {n}"); n++;
        }
        Assert.Greater(n, 30);
    }
    // @description - Physical sanity.
    // @return {Void} - nothing
    [Test] public void Physical_Sanity()
    {
        var r = JsonUtility.FromJson<Ref>(System.IO.File.ReadAllText(System.IO.Path.Combine(Application.dataPath, "Tests/Editor/steer_ref.json")));
        var k = ScriptableObject.CreateInstance<SuspensionTables>(); k.kingpinAxis = r.geom.u; k.kingpinContactPatchOffset = r.geom.r_cp; k.kingpinWheelCentreOffset = r.geom.r_wc; var g = SteeringTorque.FromTables(k);
        // right turn: lateral force to the right acting behind the patch (pneumatic trail) -> aligning torque steers left (negative)
        Assert.Less(SteeringTorque.RoadTorque(g, true, 2f, 0f, false, 3000f, 3500f, 0.025f) + SteeringTorque.RoadTorque(g, false, 2f, 0f, false, 3000f, 3500f, 0.025f), 0f);
        // weight jacking only (KPI 12.8 deg, scrub -10 mm, trail ~0 from the ESTIMATED hardpoints): small at the column (< 0.5 N m at 20 deg)
        float jack = SteeringTorque.RoadTorque(g, true, 20f, 0f, false, 0f, 3000f, 0f) + SteeringTorque.RoadTorque(g, false, 20f, 0f, false, 0f, 3000f, 0f);
        var p = ScriptableObject.CreateInstance<CarParams>(); p.suspensionTables = ScriptableObject.CreateInstance<SuspensionTables>(); SuspensionTablesData.Fill(p.suspensionTables);
        Assert.Less(Mathf.Abs(jack) / SteeringLinkage.CentreRatio(p), 0.5f);
        // FWD drive force reacted at the hub, unequal (left only): torque steer is non-zero
        Assert.AreNotEqual(0f, SteeringTorque.RoadTorque(g, true, 0f, 2000f, false, 0f, 3000f, 0f));
    }
}
