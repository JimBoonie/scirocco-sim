// Compares SteeringTorque (C#) with the independent Python reference ffb/steering_torque_ref.py (cases in steer_ref.json).
using NUnit.Framework; using UnityEngine;
public class SteeringTorqueTests
{
    [System.Serializable] class KingpinGeometry { public float[] u, r_cp, r_wc; }
    [System.Serializable] class ReferenceCase { public bool left, braking, lever_case; public float steer, fx, fy, fz, tp, expected, d, leverL, leverR; }
    [System.Serializable] class ReferenceData { public KingpinGeometry geom; public ReferenceCase[] cases; }
    // @description - Verifies that the C# steering torque equals the independent Python reference for every case in steer_ref.json (tolerance 1e-3 N m plus 0.01 %), skipping the removed linear-lever cases, and that more than 30 cases were checked.
    // @return {Void} - nothing
    [Test] public void Matches_Python_Reference()
    {
        var reference = JsonUtility.FromJson<ReferenceData>(System.IO.File.ReadAllText(System.IO.Path.Combine(Application.dataPath, "Tests/Editor/steer_ref.json")));
        var suspensionTables = ScriptableObject.CreateInstance<SuspensionTables>(); suspensionTables.kingpinAxis = reference.geom.u; suspensionTables.kingpinContactPatchOffset = reference.geom.r_cp; suspensionTables.kingpinWheelCentreOffset = reference.geom.r_wc;
        var geometry = SteeringTorque.FromTables(suspensionTables); int caseCount = 0;
        foreach (var testCase in reference.cases)
        {
            if (testCase.lever_case) continue;   // reference lever cases describe the removed linear-ratio + Ackermann-fit model; the kinematic lever is tested in SteeringLinkageTests
            float roadTorque = SteeringTorque.RoadTorque(geometry, testCase.left, testCase.steer, testCase.fx, testCase.braking, testCase.fy, testCase.fz, testCase.tp);
            Assert.AreEqual(testCase.expected, roadTorque, 1e-3 + 1e-4 * Mathf.Abs(testCase.expected), $"case {caseCount}"); caseCount++;
        }
        Assert.Greater(caseCount, 30);
    }
    // @description - Verifies physical sanity of the steering torque: aligning torque from a lateral force behind the patch steers the wheels back (negative sum), weight jacking alone gives under 0.5 N m at the column at 20 deg, and drive force on one wheel gives torque steer.
    // @return {Void} - nothing
    [Test] public void Physical_Sanity()
    {
        var reference = JsonUtility.FromJson<ReferenceData>(System.IO.File.ReadAllText(System.IO.Path.Combine(Application.dataPath, "Tests/Editor/steer_ref.json")));
        var suspensionTables = ScriptableObject.CreateInstance<SuspensionTables>(); suspensionTables.kingpinAxis = reference.geom.u; suspensionTables.kingpinContactPatchOffset = reference.geom.r_cp; suspensionTables.kingpinWheelCentreOffset = reference.geom.r_wc; var geometry = SteeringTorque.FromTables(suspensionTables);
        // right turn: lateral force to the right acting behind the patch (pneumatic trail) -> aligning torque steers left (negative)
        Assert.Less(SteeringTorque.RoadTorque(geometry, true, 2f, 0f, false, 3000f, 3500f, 0.025f) + SteeringTorque.RoadTorque(geometry, false, 2f, 0f, false, 3000f, 3500f, 0.025f), 0f);
        // weight jacking only (KPI 12.8 deg, scrub -10 mm, trail ~0 from the ESTIMATED hardpoints): small at the column (< 0.5 N m at 20 deg)
        float jack = SteeringTorque.RoadTorque(geometry, true, 20f, 0f, false, 0f, 3000f, 0f) + SteeringTorque.RoadTorque(geometry, false, 20f, 0f, false, 0f, 3000f, 0f);
        var carParams = ScriptableObject.CreateInstance<CarParams>(); carParams.suspensionTables = ScriptableObject.CreateInstance<SuspensionTables>(); SuspensionTablesData.Fill(carParams.suspensionTables);
        Assert.Less(Mathf.Abs(jack) / SteeringLinkage.CentreRatio(carParams), 0.5f);
        // FWD drive force reacted at the hub, unequal (left only): torque steer is non-zero
        Assert.AreNotEqual(0f, SteeringTorque.RoadTorque(geometry, true, 0f, 2000f, false, 0f, 3000f, 0f));
    }
}
