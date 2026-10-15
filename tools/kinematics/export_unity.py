#!/usr/bin/python3
"""Write tables.json + compliance.json into a C# static filler (Assets/Scripts/Editor/SuspensionTablesData.cs).
fields() is reused by sensitivity.py to produce runtime override files (-overrides suspensionTables.<field>=...) for perturbed inputs.
The field names written here are the full-word names of the SuspensionTables ScriptableObject (see docs/RENAMES.md in the repository)."""
import json

LOAD_CASE_ORDER_FRONT = ('brake_force', 'drive_force', 'lateral_force', 'vertical_force')
FRONT_BODY_JOINT_ORDER = ('arm_front_bush', 'arm_rear_bush', 'tie_rod', 'top_mount')
REAR_LOAD_CASE_ORDER = ('longitudinal_force', 'lateral_force', 'vertical_force')
REAR_BODY_JOINT_ORDER = ('pivot_bush_left', 'pivot_bush_right', 'spring_damper_left', 'spring_damper_right')
GENERATED_CLASS_NAME = 'SuspensionTablesData'
GENERATED_FILE_DESCRIPTION = "Copies the baked suspension kinematic and compliance tables (solved offline by solve_kinematics.py) into a SuspensionTables asset: travel in metres, angles in degrees, forces in newtons, rack travel in metres. Called by SciroccoSetup when the test scene is built so the asset always matches the generated data."


# @description - Flattens a list of rows into one list (row-major), the layout of the 2-D tables in the SuspensionTables asset.
# @param {list} rows - list of lists of numbers
# @return {list} - all numbers in row order
def flatten(rows):
    return [value for row in rows for value in row]


# @description - Front-corner and rear-beam joint reaction vectors flattened in the order the runtime expects.
# @param {dict} front - the 'front' section of compliance.json
# @param {dict} rear_compliance - the rear 'compliance' section of compliance.json
# @return {tuple} - (front joint reactions: 4 load cases x (4 body joints + ball joint) x 3 N per N, rear beam joint reactions: 3 load cases x 4 joints x 3 N per N)
def joint_reactions(front, rear_compliance):
    # joint reaction maps: force ON THE CAR BODY (left-corner frame x forward, y left, z up) per unit tyre load
    front_reactions = []
    for load_case in LOAD_CASE_ORDER_FRONT:
        for joint_name in FRONT_BODY_JOINT_ORDER:
            front_reactions += front[load_case]['body'][joint_name]
        front_reactions += front[load_case]['ball_joint']                       # ball joint: force on the knuckle from the arm
    rear_reactions = []
    for load_case in REAR_LOAD_CASE_ORDER:
        for joint_name in REAR_BODY_JOINT_ORDER:
            rear_reactions += rear_compliance[load_case]['body'][joint_name]
    return front_reactions, rear_reactions


# @description - Maps solved kinematic tables and compliance results to the SuspensionTables field names and values (degrees, metres, N per N).
# @param {dict} tables - contents of tables.json (written by solve_kinematics.py)
# @param {dict} compliance - contents of compliance.json (written by multibody_compliance.py)
# @return {dict} - SuspensionTables field name to a float or a list of floats, in the order they are written
def fields(tables, compliance):
    kinematic_coefficients = compliance['rear']['kinematic_coefficients']
    rear_compliance = compliance['rear']['compliance']
    front = compliance['front']
    suspension_fields = dict(
        travel=tables['travel'], frontCamberBody=tables['front_camber_body'], frontBumpSteer=tables['front_bumpsteer_toe'], frontTrackChange=tables['front_track_change'],
        frontRollCentreHeight=tables['front_roll_centre_height'], frontMotionRatio=tables['front_motion_ratio'], rearMotionRatio=tables['rear_motion_ratio'], steerGrid=tables['front_steer_vs_rack'],
        frontCamberTravelSteer=flatten(tables['front_camber_travel_rack']), rearCamberRollFraction=tables['rear_camber_roll_fraction'], rearRollCentreHeight=tables['rear_roll_centre_height'],
        rearBeamCamberPerOwnTravel=kinematic_coefficients[0][0], rearBeamCamberPerOtherTravel=kinematic_coefficients[0][1], rearBeamToePerOwnTravel=kinematic_coefficients[2][0], rearBeamToePerOtherTravel=kinematic_coefficients[2][1],
        rearBeamToePerOwnLateralForce=rear_compliance['lateral_force']['toe_own'], rearBeamToePerOtherLateralForce=rear_compliance['lateral_force']['toe_other'],
        rearBeamCamberPerOwnLateralForce=rear_compliance['lateral_force']['camber_own'], rearBeamCamberPerOtherLateralForce=rear_compliance['lateral_force']['camber_other'],
        rearBeamToePerOwnLongitudinalForce=rear_compliance['longitudinal_force']['toe_own'], rearBeamToePerOtherLongitudinalForce=rear_compliance['longitudinal_force']['toe_other'],
        rearBeamRollCentreHeight=compliance['rear']['roll_centre_height'], rearBeamAntiLiftCoefficient=compliance['rear']['anti_lift_coefficient'],
        frontToePerLateralForce=front['lateral_force']['toe'], frontCamberPerLateralForce=front['lateral_force']['camber'],
        frontToePerBrakeForce=front['brake_force']['toe'], frontCamberPerBrakeForce=front['brake_force']['camber'],
        frontToePerDriveForce=front['drive_force']['toe'], frontCamberPerDriveForce=front['drive_force']['camber'],
        frontToePerVerticalForce=front['vertical_force']['toe'], frontCamberPerVerticalForce=front['vertical_force']['camber'],
        frontAntiDiveCoefficient=compliance['anti_brake_front'], frontAntiSquatCoefficient=compliance['anti_drive_front'], frontJackingPerLateralForce=compliance['front_jacking_per_lateral_force'])
    suspension_fields['frontJointReactions'], suspension_fields['rearBeamJointReactions'] = joint_reactions(front, rear_compliance)
    if 'front_toe_travel_rack' in tables:
        suspension_fields['rackGrid'] = tables['rack']
        suspension_fields['frontToeTravelRack'] = flatten(tables['front_toe_travel_rack'])
    kingpin = compliance['kingpin_geometry']
    suspension_fields['kingpinAxis'] = kingpin['kingpin_axis']
    suspension_fields['kingpinContactPatchOffset'] = kingpin['contact_patch_offset']
    suspension_fields['kingpinWheelCentreOffset'] = kingpin['wheel_centre_offset']
    return suspension_fields


# @description - Formats a float or a list of floats as a C# literal with 6 significant digits.
# @param {object} value - a float or a list of floats
# @return {str} - C# source text such as "0.5f" or "new float[]{0.5f,1f}"
def format_value(value):
    if isinstance(value, list):
        return "new float[]{" + ",".join(f"{element:.6g}f" for element in value) + "}"
    return f"{value:.6g}f"


# @description - Writes the generated C# class that fills a SuspensionTables asset, and prints a short check.
# @param {dict} tables - contents of tables.json
# @param {dict} compliance - contents of compliance.json
# @param {str} class_name - name of the generated static class
# @param {str} path - output .cs file path
# @return {Void} - nothing (writes the file)
def write_cs(tables, compliance, class_name, path):
    suspension_fields = fields(tables, compliance)
    body = "\n".join(f"        suspensionTables.{name} = {format_value(value)};" for name, value in suspension_fields.items())
    source = f"""// AUTO-GENERATED by kinematics/export_unity.py from tables.json + compliance.json. Do not edit by hand.
// Source hardpoints: suspension_params.json (partly ESTIMATED; tie-rod points used as given, rod 0.379 m SD).
public static class {class_name}
{{
    // @description - {GENERATED_FILE_DESCRIPTION}
    // @param {{SuspensionTables}} suspensionTables - the asset to fill (every table field is overwritten)
    // @return {{Void}} - nothing
    public static void Fill(SuspensionTables suspensionTables)
    {{
{body}
    }}
}}
"""
    open(path, 'w').write(source)
    steer_grid = tables['front_steer_vs_rack']
    print("written", len(suspension_fields), "fields; steerGrid ascending:", all(first < second for first, second in zip(steer_grid, steer_grid[1:])))


# @description - Reads tables.json and compliance.json from the current directory and writes the generated SuspensionTablesData.cs into the Unity project.
# @return {Void} - nothing
def main():
    editor_directory = '../unity/SciroccoSimDemo/Assets/Scripts/Editor/'
    write_cs(json.load(open('tables.json')), json.load(open('compliance.json')), GENERATED_CLASS_NAME, editor_directory + 'SuspensionTablesData.cs')
    # single baseline (2026-10-13): the former 'ProposedUndersteer' hardpoints/bush rates are folded into suspension_params.json; no second table set


if __name__ == '__main__':
    main()
