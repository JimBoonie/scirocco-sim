// Physical steering torque: tire forces/moments about each kingpin (steering) axis -> tie rods/rack -> column torque.
// Geometry (kpU axis direction, contact patch and wheel center relative to the axis ground point) is DERIVED from the
// suspension hardpoints by kinematics/multibody_compliance.py (left-corner frame: x fwd, y left/outboard, z up).
// Terms captured by the vector formulation (no separate empirical terms):
//   lateral force x (pneumatic trail + mechanical trail from caster), longitudinal force x scrub radius (brake force at the
//   contact patch) or x kingpin offset at hub height (drive force reacted at the hub: FWD torque steer), vertical load x
//   kingpin inclination / caster (weight-jacking moments that grow with steer angle).
// Pneumatic trail t_p(alpha) = t0 * max(0, 1 - |alpha|/alpha_tp) (E) shifts the lateral force behind the patch center (= tire Mz).
using UnityEngine;
public static class SteeringTorque
{
    // Kingpin geometry of one (left-frame) corner.
    public struct Geo
    {
        public Vector3 kingpinAxis;           // u: unit vector along the kingpin axis
        public Vector3 contactPatchOffset;    // r_cp: contact patch relative to the axis ground point (m)
        public Vector3 wheelCenterOffset;     // r_wc: wheel center relative to the axis ground point (m)
    }

    // @description - reads the kingpin geometry from the baked tables (vertical-axis fallback when there are none)
    // @param {SuspensionTables} tables - baked kinematic tables (may be null)
    // @return {Geo} - kingpin geometry
    public static Geo FromTables(SuspensionTables tables)
    {
        var geometry = new Geo();
        if (tables != null && tables.kingpinAxis != null && tables.kingpinAxis.Length == 3) { geometry.kingpinAxis = new Vector3(tables.kingpinAxis[0], tables.kingpinAxis[1], tables.kingpinAxis[2]).normalized; geometry.contactPatchOffset = ToVector3(tables.kingpinContactPatchOffset); geometry.wheelCenterOffset = ToVector3(tables.kingpinWheelCenterOffset); }
        else geometry.kingpinAxis = Vector3.forward;   // (x,y,z) = (fwd,left,up) in this frame -> vertical axis fallback (no tables)
        return geometry;
    }

    // @description - converts a 3-element array to a vector (zero if missing)
    // @param {float[]} values - x, y, z
    // @return {Vector3} - the vector
    static Vector3 ToVector3(float[] values) => values != null && values.Length == 3 ? new Vector3(values[0], values[1], values[2]) : Vector3.zero;

    // NOTE: math is done in a plain right-handed (x fwd, y left, z up) frame stored in Vector3 (x,y,z) -> no Unity axis conventions.
    // @description - cross product first x second in the right-handed frame
    // @param {Vector3} first - first vector
    // @param {Vector3} second - second vector
    // @return {Vector3} - first x second
    static Vector3 CrossProduct(Vector3 first, Vector3 second) => new Vector3(first.y * second.z - first.z * second.y, first.z * second.x - first.x * second.z, first.x * second.y - first.y * second.x);

    // @description - rotates a vector about a unit axis (Rodrigues' formula: v cos + (k x v) sin + k (k.v)(1 - cos))
    // @param {Vector3} vector - vector to rotate
    // @param {Vector3} axis - unit rotation axis
    // @param {float} angle - rotation angle in rad
    // @return {Vector3} - rotated vector
    static Vector3 RotateAboutAxis(Vector3 vector, Vector3 axis, float angle)
    { float cosine = Mathf.Cos(angle), sine = Mathf.Sin(angle); return vector * cosine + CrossProduct(axis, vector) * sine + axis * (Vector3.Dot(axis, vector) * (1f - cosine)); }

    // @description - moment about the kingpin axis (+ = counter-clockwise about +u, i.e. steers a LEFT-frame wheel to the left/outboard) from the tire forces:
    //   M = r_cp x (Fx_patch + Fz) + (r_cp - x_w t_p) x Fy + r_wc x Fx_hub, all in the steered wheel frame; forces act in the ground plane
    // @param {Geo} geometry - kingpin geometry
    // @param {float} deltaDeg - wheel steer angle in this frame (+ = towards -y, i.e. right for the left wheel)
    // @param {float} patchLongitudinalForce - brake force at the contact patch in N
    // @param {float} hubLongitudinalForce - drive force reacted at the hub in N
    // @param {float} lateralForce - lateral force in N (+ = +y/outboard)
    // @param {float} verticalForce - vertical load in N (+ up)
    // @param {float} pneumaticTrail - pneumatic trail in m
    // @return {float} - moment about the kingpin axis in N m
    public static float KingpinMoment(Geo geometry, float deltaDeg, float patchLongitudinalForce, float hubLongitudinalForce, float lateralForce, float verticalForce, float pneumaticTrail)
    {
        if (geometry.kingpinAxis == Vector3.forward && geometry.contactPatchOffset == Vector3.zero) return 0f;
        float angle = -deltaDeg * Mathf.Deg2Rad;
        Vector3 wheelX = RotateAboutAxis(new Vector3(1f, 0f, 0f), geometry.kingpinAxis, angle), wheelY = RotateAboutAxis(new Vector3(0f, 1f, 0f), geometry.kingpinAxis, angle);
        wheelX.z = 0f; wheelX.Normalize(); wheelY.z = 0f; wheelY.Normalize();                          // tire forces act in the ground plane
        Vector3 contactPatch = RotateAboutAxis(geometry.contactPatchOffset, geometry.kingpinAxis, angle), wheelCenter = RotateAboutAxis(geometry.wheelCenterOffset, geometry.kingpinAxis, angle);
        Vector3 patchForce = wheelX * patchLongitudinalForce + new Vector3(0f, 0f, verticalForce), lateralForceVector = wheelY * lateralForce;
        Vector3 moment = CrossProduct(contactPatch, patchForce) + CrossProduct(contactPatch - wheelX * pneumaticTrail, lateralForceVector) + CrossProduct(wheelCenter, wheelX * hubLongitudinalForce);
        return Vector3.Dot(moment, geometry.kingpinAxis);
    }

    // @description - pneumatic trail t_p(alpha) = t0 max(0, 1 - |alpha| / alpha_peak) (E)
    // @param {CarParams} carParams - holds pneumaticTrail0 and peakSlipTrail
    // @param {float} slipAngleRad - slip angle in rad
    // @return {float} - pneumatic trail in m
    public static float PneumaticTrail(CarParams carParams, float slipAngleRad) => carParams.pneumaticTrailAtZeroSlip * Mathf.Max(0f, 1f - Mathf.Abs(slipAngleRad) / carParams.peakSlipTrail);

    // @description - road-wheel steer torque of one corner (about the kingpin; + = steers the wheel to the RIGHT, car convention). Brake force acts at the patch, drive force at the hub.
    // @param {Geo} geometry - kingpin geometry
    // @param {bool} left - true = left corner
    // @param {float} steerDeg - road-wheel steer angle in deg (+ = right)
    // @param {float} longitudinalForce - tire longitudinal force in N
    // @param {bool} braking - true = treat the longitudinal force as brake force (at the patch), false = drive force (at the hub)
    // @param {float} lateralForceRight - lateral force in N (+ = right, car convention)
    // @param {float} verticalForce - vertical load in N
    // @param {float} pneumaticTrail - pneumatic trail in m
    // @return {float} - steer torque in N m
    public static float RoadTorque(Geo geometry, bool left, float steerDeg, float longitudinalForce, bool braking, float lateralForceRight, float verticalForce, float pneumaticTrail)
    {
        float patchForce = braking ? longitudinalForce : 0f, hubForce = braking ? 0f : longitudinalForce;
        if (left) return -KingpinMoment(geometry, steerDeg, patchForce, hubForce, -lateralForceRight, verticalForce, pneumaticTrail);   // CCW about up = steer left -> negative
        return KingpinMoment(geometry, -steerDeg, patchForce, hubForce, lateralForceRight, verticalForce, pneumaticTrail);                // mirrored frame: mirror flips rotation sense
    }

    // @description - d(road steer of a wheel)/d(column angle) from the linkage kinematics (SteeringLinkage.Lever, D)
    // @param {CarParams} carParams - car parameters
    // @param {bool} left - true = left wheel
    // @param {float} rack - rack travel in m
    // @param {float} travel - wheel travel in m
    // @return {float} - lever in deg/deg
    public static float Lever(CarParams carParams, bool left, float rack, float travel) => SteeringLinkage.Lever(carParams, left, rack, travel);
}
