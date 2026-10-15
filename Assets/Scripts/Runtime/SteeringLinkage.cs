using UnityEngine;
// Steering linkage: handwheel -> constant-ratio rack-and-pinion -> rack travel -> per-wheel road angle from the suspension kinematics.
// rack (m) = handwheel (deg) x CarParams.rackPerHandwheelDeg. Each front wheel's angle is read from the solver grid
// SuspensionTables.frontToeTravelRack (kinematics/solve_kinematics.py: tie-rod points of suspension_params.json, D), interpolated in
// wheel travel and rack travel, so Ackermann and bump steer both come from the geometry (no linear ratio, no Ackermann fit).
public static class SteeringLinkage
{
    // @description - rack travel for a handwheel angle (constant pinion)
    // @param {CarParams} carParams - holds rackPerHandwheelDeg
    // @param {float} handwheelDeg - handwheel angle in deg
    // @return {float} - rack travel in m (+ = rack to the left)
    public static float Rack(CarParams carParams, float handwheelDeg) => handwheelDeg * carParams.rackPerHandwheelDeg;

    // @description - road-wheel angles at a rack travel and wheel travels (bump steer included, static toe/compliance excluded); the right side is the mirror image (rack sign flips)
    // @param {SuspensionTables} tables - baked kinematic tables
    // @param {float} rack - rack travel in m
    // @param {float} leftTravel - left wheel travel in m (+ bump)
    // @param {float} rightTravel - right wheel travel in m (+ bump)
    // @param {float} left - (out) left road-wheel angle in deg (+ = points right)
    // @param {float} right - (out) right road-wheel angle in deg (+ = points right)
    // @return {Void}
    public static void WheelAngles(SuspensionTables tables, float rack, float leftTravel, float rightTravel, out float left, out float right)
    { left = tables.FrontSteerLeft(leftTravel, rack); right = -tables.FrontSteerLeft(rightTravel, -rack); }

    // @description - mean road-wheel angle at design ride height for a handwheel angle (D)
    // @param {CarParams} carParams - car parameters with the kinematic tables
    // @param {float} handwheelDeg - handwheel angle in deg
    // @return {float} - mean of the left and right road-wheel angles in deg
    public static float MeanAngle(CarParams carParams, float handwheelDeg)
    { WheelAngles(carParams.suspensionTables, Rack(carParams, handwheelDeg), 0f, 0f, out float left, out float right); return 0.5f * (left + right); }

    // @description - inverse of MeanAngle (monotonic -> bisection), clamped at the rack stop. Used by the test driver / keyboard, which command a road angle.
    // @param {CarParams} carParams - car parameters
    // @param {float} meanDeg - wanted mean road-wheel angle in deg
    // @return {float} - handwheel angle in deg
    public static float HandwheelForMean(CarParams carParams, float meanDeg)
    {
        float low = -carParams.handwheelLockDeg, high = carParams.handwheelLockDeg;
        if (meanDeg <= MeanAngle(carParams, low)) return low; if (meanDeg >= MeanAngle(carParams, high)) return high;
        for (int iteration = 0; iteration < 40; iteration++) { float middle = 0.5f * (low + high); if (MeanAngle(carParams, middle) < meanDeg) low = middle; else high = middle; }
        return 0.5f * (low + high);
    }

    // @description - d(road angle of one wheel)/d(handwheel angle) at the current rack and wheel travel, central difference over one grid step
    // @param {CarParams} carParams - car parameters with the tables
    // @param {bool} left - true = left wheel, false = right wheel
    // @param {float} rack - current rack travel in m
    // @param {float} travel - current wheel travel in m
    // @return {float} - lever in deg/deg
    public static float Lever(CarParams carParams, bool left, float rack, float travel)
    {
        var tables = carParams.suspensionTables; float halfStep = 0.5f * (tables.rackGrid[1] - tables.rackGrid[0]);
        float sideSign = left ? 1f : -1f;   // right wheel: -FrontSteerLeft(t, -rack) -> derivative = FrontSteerLeft'(t, -rack)
        float sideRack = sideSign * rack;
        return (tables.FrontSteerLeft(travel, sideRack + halfStep) - tables.FrontSteerLeft(travel, sideRack - halfStep)) / (2f * halfStep) * carParams.rackPerHandwheelDeg;
    }

    // @description - on-center overall ratio handwheel / mean road angle (D, for column-referred stiffness and UI only)
    // @param {CarParams} carParams - car parameters
    // @return {float} - ratio in deg of handwheel per deg of road wheel
    public static float CenterRatio(CarParams carParams) { float probeAngle = 1f; return 2f * probeAngle / (MeanAngle(carParams, probeAngle) - MeanAngle(carParams, -probeAngle)); }
}
