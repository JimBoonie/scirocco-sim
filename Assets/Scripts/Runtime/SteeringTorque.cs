// Physical steering torque: tire forces/moments about each kingpin (steering) axis -> tie rods/rack -> column torque.
// Geometry (kpU axis direction, contact patch and wheel centre relative to the axis ground point) is DERIVED from the
// suspension hardpoints by kinematics/multibody_compliance.py (left-corner frame: x fwd, y left/outboard, z up).
// Terms captured by the vector formulation (no separate empirical terms):
//   lateral force x (pneumatic trail + mechanical trail from caster), longitudinal force x scrub radius (brake force at the
//   contact patch) or x kingpin offset at hub height (drive force reacted at the hub: FWD torque steer), vertical load x
//   kingpin inclination / caster (weight-jacking moments that grow with steer angle).
// Pneumatic trail t_p(alpha) = t0 * max(0, 1 - |alpha|/alpha_tp) (E) shifts the lateral force behind the patch centre (= tire Mz).
using UnityEngine;
public static class SteeringTorque
{
    public struct Geo { public Vector3 u, rcp, rwc; }
    public static Geo FromTables(SuspensionTables k)
    {
        var g = new Geo();
        if (k != null && k.kpU != null && k.kpU.Length == 3) { g.u = new Vector3(k.kpU[0], k.kpU[1], k.kpU[2]).normalized; g.rcp = V(k.kpRcp); g.rwc = V(k.kpRwc); }
        else g.u = Vector3.forward;   // (x,y,z) = (fwd,left,up) in this frame -> vertical axis fallback (no tables)
        return g;
    }
    static Vector3 V(float[] a) => a != null && a.Length == 3 ? new Vector3(a[0], a[1], a[2]) : Vector3.zero;
    // NOTE: math is done in a plain right-handed (x fwd, y left, z up) frame stored in Vector3 (x,y,z) -> no Unity axis conventions.
    static Vector3 Cross(Vector3 a, Vector3 b) => new Vector3(a.y * b.z - a.z * b.y, a.z * b.x - a.x * b.z, a.x * b.y - a.y * b.x);
    static Vector3 Rot(Vector3 v, Vector3 k, float phi)   // Rodrigues
    { float c = Mathf.Cos(phi), s = Mathf.Sin(phi); return v * c + Cross(k, v) * s + k * (Vector3.Dot(k, v) * (1f - c)); }
    /// Moment about the kingpin axis (N m, + = counter-clockwise about +u, i.e. steers a LEFT-frame wheel to the left/outboard).
    /// deltaDeg: wheel steer angle in this frame (+ = towards -y, i.e. right for the left wheel). Forces from the ground on the tire,
    /// in the wheel frame: fxPatch (brake, at patch), fxHub (drive, at hub), fy (+ = +y/outboard), fz (+ up); tp = pneumatic trail (m).
    public static float KingpinMoment(Geo g, float deltaDeg, float fxPatch, float fxHub, float fy, float fz, float tp)
    {
        if (g.u == Vector3.forward && g.rcp == Vector3.zero) return 0f;
        float phi = -deltaDeg * Mathf.Deg2Rad;
        Vector3 xw = Rot(new Vector3(1f, 0f, 0f), g.u, phi), yw = Rot(new Vector3(0f, 1f, 0f), g.u, phi);
        xw.z = 0f; xw.Normalize(); yw.z = 0f; yw.Normalize();                          // tire forces act in the ground plane
        Vector3 rcp = Rot(g.rcp, g.u, phi), rwc = Rot(g.rwc, g.u, phi);
        Vector3 Fp = xw * fxPatch + new Vector3(0f, 0f, fz), Fl = yw * fy;
        Vector3 M = Cross(rcp, Fp) + Cross(rcp - xw * tp, Fl) + Cross(rwc, xw * fxHub);
        return Vector3.Dot(M, g.u);
    }
    public static float PneumaticTrail(CarParams p, float alphaRad) => p.pneumaticTrail0 * Mathf.Max(0f, 1f - Mathf.Abs(alphaRad) / p.peakSlipTrail);
    /// Road-wheel steer torque of one corner (N m about the kingpin, + = steers the wheel to the RIGHT, car convention).
    public static float RoadTorque(Geo g, bool left, float steerDeg, float fx, bool braking, float fyRight, float fz, float tp)
    {
        float fxP = braking ? fx : 0f, fxH = braking ? 0f : fx;
        if (left) return -KingpinMoment(g, steerDeg, fxP, fxH, -fyRight, fz, tp);   // CCW about up = steer left -> negative
        return KingpinMoment(g, -steerDeg, fxP, fxH, fyRight, fz, tp);                // mirrored frame: mirror flips rotation sense
    }
    /// d(road steer of a wheel)/d(column angle): from the linkage kinematics (SteeringLinkage.Lever, D)
    public static float Lever(CarParams p, bool left, float rack, float travel) => SteeringLinkage.Lever(p, left, rack, travel);
}
