using UnityEngine;
// Baked kinematic tables from kinematics/solve_kinematics.py (inputs: suspension_params.json; hardpoints partly ESTIMATED).
// Front: functions of wheel travel t (m, + bump) and left-wheel steer angle (deg, + = points right). Right side mirrored.
public class SuspensionTables : ScriptableObject
{
    public float[] travel, frontCamberBody, frontBumpSteer, frontTrackChange, frontRollCenterHeight, frontMotionRatio, rearMotionRatio;
    public float[] steerGrid;              // left-wheel steer angle at each solver rack position (ascending)
    public float[] frontCamberTravelSteer; // [travel.Length * steerGrid.Length], camber change vs body
    public float rearCamberRollFraction, rearRollCenterHeight;
    // ---- full twist-beam (multibody_compliance.py, linearized at design; bush rates E) ----
    public float rearBeamCamberPerOwnTravel, rearBeamCamberPerOtherTravel, rearBeamToePerOwnTravel, rearBeamToePerOtherTravel;          // deg per meter travel (own wheel, other wheel), body-relative
    public float rearBeamToePerOwnLateralForce, rearBeamToePerOtherLateralForce, rearBeamCamberPerOwnLateralForce, rearBeamCamberPerOtherLateralForce;  // deg per N outward lateral force at a contact patch
    public float rearBeamToePerOwnLongitudinalForce, rearBeamToePerOtherLongitudinalForce;                            // deg per N +x force
    public float rearBeamRollCenterHeight, rearBeamAntiLiftCoefficient;                              // solved roll center (m); vertical body force per N of +x force
    // ---- front compliant model ----
    public float frontToePerLateralForce, frontCamberPerLateralForce, frontToePerBrakeForce, frontCamberPerBrakeForce, frontToePerVerticalForce, frontCamberPerVerticalForce; // deg per N (outward Fy, +x Fx at patch, Fz)
    public float frontAntiDiveCoefficient, frontAntiSquatCoefficient;                             // vertical body force per N +x (brake at patch / drive at hub)
    public float frontJackingPerLateralForce;                                          // validation only
    public float frontToePerDriveForce, frontCamberPerDriveForce;                                   // deg per N drive force (reacted at the hub)
    // joint reaction maps (force ON THE CAR BODY, left-corner frame x fwd / y left / z up, per N of tire load; linear at design)
    // frontJointReactions: [load Fx_brake, Fx_drive, Fy_out, Fz][joint arm_front_bush, arm_rear_bush, tie_rod, top_mount, ball_joint(on knuckle)][xyz]
    // rearBeamJointReactions: left-wheel loads [Fx, Fy_out, Fz][joint pivot_bush_L, pivot_bush_R, spring_damper_L, spring_damper_R][xyz]
    public float[] frontJointReactions, rearBeamJointReactions;
    public float[] kingpinAxis, kingpinContactPatchOffset, kingpinWheelCenterOffset;
    // steering: rack travel grid (m, + = rack to the left) and left-wheel road angle [travel x rack] (deg, + = points right, bump steer included; D)
    public float[] rackGrid, frontToeTravelRack;   // kingpin axis (unit) and contact patch / wheel center relative to its ground point (left frame, D)

    // @description - piecewise-linear interpolation value(position), clamped to the end values; 0 if the table is empty
    // @param {float[]} positions - ascending sample positions
    // @param {float[]} values - sample values
    // @param {float} position - position to evaluate
    // @return {float} - interpolated value
    public static float Interpolate1D(float[] positions, float[] values, float position)
    {
        if (positions == null || positions.Length == 0) return 0f;
        if (position <= positions[0]) return values[0]; int count = positions.Length; if (position >= positions[count - 1]) return values[count - 1];
        int index = 1; while (position > positions[index]) index++; return Mathf.Lerp(values[index - 1], values[index], (position - positions[index - 1]) / (positions[index] - positions[index - 1]));
    }

    // @description - front camber change vs body from the baked [travel x steer] grid, bilinear interpolation
    // @param {float} travel - wheel travel in m (+ bump)
    // @param {float} steerLeftDeg - left-wheel steer angle in deg (+ = points right)
    // @return {float} - camber change in deg
    public float FrontCamber(float travel, float steerLeftDeg)
    {
        int travelCount = this.travel.Length, steerCount = steerGrid.Length;
        float travelIndex = GridIndex(this.travel, travel), steerIndex = GridIndex(steerGrid, steerLeftDeg);
        int travelCell = Mathf.Min((int)travelIndex, travelCount - 2), steerCell = Mathf.Min((int)steerIndex, steerCount - 2); float travelFraction = travelIndex - travelCell, steerFraction = steerIndex - steerCell;
        float lowLow = frontCamberTravelSteer[travelCell * steerCount + steerCell], lowHigh = frontCamberTravelSteer[travelCell * steerCount + steerCell + 1];
        float highLow = frontCamberTravelSteer[(travelCell + 1) * steerCount + steerCell], highHigh = frontCamberTravelSteer[(travelCell + 1) * steerCount + steerCell + 1];
        return Mathf.Lerp(Mathf.Lerp(lowLow, lowHigh, steerFraction), Mathf.Lerp(highLow, highHigh, steerFraction), travelFraction);
    }

    // @description - left-wheel road angle (bump steer included) from the baked [travel x rack] grid, bilinear interpolation
    // @param {float} travel - wheel travel in m (+ bump)
    // @param {float} rack - rack travel in m (+ = rack to the left)
    // @return {float} - road-wheel angle in deg (+ = points right)
    public float FrontSteerLeft(float travel, float rack)
    {
        int travelCount = this.travel.Length, rackCount = rackGrid.Length;
        float travelIndex = GridIndex(this.travel, travel), rackIndex = GridIndex(rackGrid, rack);
        int travelCell = Mathf.Min((int)travelIndex, travelCount - 2), rackCell = Mathf.Min((int)rackIndex, rackCount - 2); float travelFraction = travelIndex - travelCell, rackFraction = rackIndex - rackCell;
        float lowLow = frontToeTravelRack[travelCell * rackCount + rackCell], lowHigh = frontToeTravelRack[travelCell * rackCount + rackCell + 1];
        float highLow = frontToeTravelRack[(travelCell + 1) * rackCount + rackCell], highHigh = frontToeTravelRack[(travelCell + 1) * rackCount + rackCell + 1];
        return Mathf.Lerp(Mathf.Lerp(lowLow, lowHigh, rackFraction), Mathf.Lerp(highLow, highHigh, rackFraction), travelFraction);
    }

    // @description - fractional grid index of position in an ascending grid (clamped to 0 .. count-1)
    // @param {float[]} grid - ascending grid positions
    // @param {float} position - position to find in the grid, in the grid units
    // @return {float} - index of the lower grid point plus the fraction towards the next
    static float GridIndex(float[] grid, float position)
    {
        int count = grid.Length; if (position <= grid[0]) return 0f; if (position >= grid[count - 1]) return count - 1;
        int index = 1; while (position > grid[index]) index++; return index - 1 + (position - grid[index - 1]) / (grid[index] - grid[index - 1]);
    }
}
