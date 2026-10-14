using UnityEngine;
// Steering linkage: handwheel -> constant-ratio rack-and-pinion -> rack travel -> per-wheel road angle from the suspension kinematics.
// rack (m) = handwheel (deg) x CarParams.rackPerHandwheelDeg. Each front wheel's angle is read from the solver grid
// SuspensionTables.frontToeTravelRack (kinematics/solve_kinematics.py: tie-rod points of suspension_params.json, D), interpolated in
// wheel travel and rack travel, so Ackermann and bump steer both come from the geometry (no linear ratio, no Ackermann fit).
public static class SteeringLinkage
{
    public static float Rack(CarParams p, float handwheelDeg) => handwheelDeg * p.rackPerHandwheelDeg;
    /// road-wheel angles (deg, + = points right, bump steer included, static toe/compliance excluded) at rack travel and wheel travels
    public static void WheelAngles(SuspensionTables k, float rack, float travelL, float travelR, out float left, out float right)
    { left = k.FrontSteerLeft(travelL, rack); right = -k.FrontSteerLeft(travelR, -rack); }   // right side = mirror image (rack sign flips)
    /// mean road-wheel angle at design ride height for a handwheel angle (D)
    public static float MeanAngle(CarParams p, float handwheelDeg)
    { WheelAngles(p.kin, Rack(p, handwheelDeg), 0f, 0f, out float l, out float r); return 0.5f * (l + r); }
    /// inverse of MeanAngle (monotonic -> bisection), clamped at the rack stop. Used by the test driver / keyboard, which command a road angle.
    public static float HandwheelForMean(CarParams p, float meanDeg)
    {
        float lo = -p.handwheelLockDeg, hi = p.handwheelLockDeg;
        if (meanDeg <= MeanAngle(p, lo)) return lo; if (meanDeg >= MeanAngle(p, hi)) return hi;
        for (int i = 0; i < 40; i++) { float m = 0.5f * (lo + hi); if (MeanAngle(p, m) < meanDeg) lo = m; else hi = m; }
        return 0.5f * (lo + hi);
    }
    /// d(road angle of one wheel)/d(handwheel angle) at the current rack and wheel travel (deg/deg), central difference over one grid step
    public static float Lever(CarParams p, bool left, float rack, float travel)
    {
        var k = p.kin; float h = 0.5f * (k.rackGrid[1] - k.rackGrid[0]);
        float s = left ? 1f : -1f;   // right wheel: -FrontSteerLeft(t, -rack) -> derivative = FrontSteerLeft'(t, -rack)
        float r0 = s * rack;
        return (k.FrontSteerLeft(travel, r0 + h) - k.FrontSteerLeft(travel, r0 - h)) / (2f * h) * p.rackPerHandwheelDeg;
    }
    /// on-centre overall ratio handwheel/mean road angle (D, for column-referred stiffness and UI only)
    public static float CentreRatio(CarParams p) { float d = 1f; return 2f * d / (MeanAngle(p, d) - MeanAngle(p, -d)); }
}
