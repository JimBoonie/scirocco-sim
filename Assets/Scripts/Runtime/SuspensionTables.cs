using UnityEngine;
// Baked kinematic tables from kinematics/solve_kinematics.py (inputs: suspension_params.json; hardpoints partly ESTIMATED).
// Front: functions of wheel travel t (m, + bump) and left-wheel steer angle (deg, + = points right). Right side mirrored.
public class SuspensionTables : ScriptableObject
{
    public float[] travel, frontCamberBody, frontBumpSteer, frontTrackChange, frontRcHeight, frontMotionRatio, rearMotionRatio;
    public float[] steerGrid;              // left-wheel steer angle at each solver rack position (ascending)
    public float[] frontCamberTravelSteer; // [travel.Length * steerGrid.Length], camber change vs body
    public float rearCamberRollFraction, rearRcHeight;
    // ---- full twist-beam (multibody_compliance.py, linearised at design; bush rates E) ----
    public float rbCamOwn, rbCamOther, rbToeOwn, rbToeOther;          // deg per metre travel (own wheel, other wheel), body-relative
    public float rbToeFyOwn, rbToeFyOther, rbCamFyOwn, rbCamFyOther;  // deg per N outward lateral force at a contact patch
    public float rbToeFxOwn, rbToeFxOther;                            // deg per N +x force
    public float rbRcHeight, rbAntiLift;                              // solved roll centre (m); vertical body force per N of +x force
    // ---- front compliant model ----
    public float fcToeFy, fcCamFy, fcToeFx, fcCamFx, fcToeFz, fcCamFz; // deg per N (outward Fy, +x Fx at patch, Fz)
    public float fcAntiBrake, fcAntiDrive;                             // vertical body force per N +x (brake at patch / drive at hub)
    public float fcJackPerFy;                                          // validation only
    public float fcToeFxD, fcCamFxD;                                   // deg per N drive force (reacted at the hub)
    // joint reaction maps (force ON THE CAR BODY, left-corner frame x fwd / y left / z up, per N of tire load; linear at design)
    // fcJoint: [load Fx_brake, Fx_drive, Fy_out, Fz][joint arm_front_bush, arm_rear_bush, tie_rod, top_mount, ball_joint(on knuckle)][xyz]
    // rbJoint: left-wheel loads [Fx, Fy_out, Fz][joint pivot_bush_L, pivot_bush_R, spring_damper_L, spring_damper_R][xyz]
    public float[] fcJoint, rbJoint;
    public float[] kpU, kpRcp, kpRwc;
    // steering: rack travel grid (m, + = rack to the left) and left-wheel road angle [travel x rack] (deg, + = points right, bump steer included; D)
    public float[] rackGrid, frontToeTravelRack;   // kingpin axis (unit) and contact patch / wheel centre relative to its ground point (left frame, D)

    public static float S1(float[] xs, float[] ys, float x)
    {
        if (xs == null || xs.Length == 0) return 0f;
        if (x <= xs[0]) return ys[0]; int n = xs.Length; if (x >= xs[n - 1]) return ys[n - 1];
        int i = 1; while (x > xs[i]) i++; return Mathf.Lerp(ys[i - 1], ys[i], (x - xs[i - 1]) / (xs[i] - xs[i - 1]));
    }
    public float FrontCamber(float t, float steerLeftDeg)
    {
        int nt = travel.Length, ns = steerGrid.Length;
        float ft = Idx(travel, t), fs = Idx(steerGrid, steerLeftDeg);
        int i0 = Mathf.Min((int)ft, nt - 2), j0 = Mathf.Min((int)fs, ns - 2); float u = ft - i0, w = fs - j0;
        float a = frontCamberTravelSteer[i0 * ns + j0], b = frontCamberTravelSteer[i0 * ns + j0 + 1];
        float c = frontCamberTravelSteer[(i0 + 1) * ns + j0], d = frontCamberTravelSteer[(i0 + 1) * ns + j0 + 1];
        return Mathf.Lerp(Mathf.Lerp(a, b, w), Mathf.Lerp(c, d, w), u);
    }
    public float FrontSteerLeft(float t, float rack)
    {
        int nt = travel.Length, nr = rackGrid.Length;
        float ft = Idx(travel, t), fr = Idx(rackGrid, rack);
        int i0 = Mathf.Min((int)ft, nt - 2), j0 = Mathf.Min((int)fr, nr - 2); float u = ft - i0, w = fr - j0;
        float a = frontToeTravelRack[i0 * nr + j0], b = frontToeTravelRack[i0 * nr + j0 + 1];
        float c = frontToeTravelRack[(i0 + 1) * nr + j0], d = frontToeTravelRack[(i0 + 1) * nr + j0 + 1];
        return Mathf.Lerp(Mathf.Lerp(a, b, w), Mathf.Lerp(c, d, w), u);
    }
    static float Idx(float[] xs, float x)
    {
        int n = xs.Length; if (x <= xs[0]) return 0f; if (x >= xs[n - 1]) return n - 1;
        int i = 1; while (x > xs[i]) i++; return i - 1 + (x - xs[i - 1]) / (xs[i] - xs[i - 1]);
    }
}
