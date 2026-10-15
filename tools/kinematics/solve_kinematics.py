#!/usr/bin/python3
"""Offline suspension kinematics for Scirocco II (A1 platform).
Inputs: ../suspension_params.json (hardpoints tagged S/SD/D/E; many hardpoints are ESTIMATES -> results inherit that).
Vehicle frame: x forward, y left, z up, origin on the ground at the front axle centreline. Left side solved; right mirrored.
Outputs: tables.json (consumed by the Unity editor setup -> SuspensionTables ScriptableObject) + PNG plots.
Units: metres, degrees, radians where stated; hardpoint tags S = sourced, SD = sourced then derived, D = derived, E = estimated."""
import json
import numpy as np
from scipy.optimize import least_squares
from scipy.spatial.transform import Rotation
import matplotlib
matplotlib.use('Agg')
import matplotlib.pyplot as plt
import params_io

PARAMETERS = params_io.load()
FRONT = PARAMETERS['front_macpherson']
REAR = PARAMETERS['rear_twist_beam']


# @description - Reads one front hardpoint from the parameter file as a 3-vector (x forward, y left, z up).
# @param {str} name - key of the hardpoint in the 'front_macpherson' section
# @return {ndarray} - the hardpoint position in metres, shape (3,)
def front_point(name):
    return np.array(FRONT[name][0], float)


STRUT_TOP_MOUNT = front_point('strut_top_mount')
LOWER_BALL_JOINT = front_point('lower_ball_joint')
ARM_FRONT_PIVOT = front_point('lower_arm_front_pivot')
ARM_REAR_PIVOT = front_point('lower_arm_rear_pivot')
WHEEL_CENTRE = front_point('wheel_centre')
TIE_ROD_OUTER = front_point('tie_rod_outer')
TIE_ROD_INNER = front_point('tie_rod_inner')
LOADED_RADIUS = PARAMETERS['vehicle']['tyre_loaded_radius'][0]                    # m, loaded tyre radius (S)
STRUT_AXIS = (STRUT_TOP_MOUNT - LOWER_BALL_JOINT) / np.linalg.norm(STRUT_TOP_MOUNT - LOWER_BALL_JOINT)   # strut axis (= steering axis here: strut top + LBJ)
ARM_PIVOT_AXIS = (ARM_REAR_PIVOT - ARM_FRONT_PIVOT) / np.linalg.norm(ARM_REAR_PIVOT - ARM_FRONT_PIVOT)   # lower arm pivot axis
TIE_ROD_LENGTH = np.linalg.norm(TIE_ROD_OUTER - TIE_ROD_INNER)                   # m, rigid rod length (SD)
SPIN_AXIS = np.array([0, 1.0, 0])                                                # design wheel spin axis (alignment applied separately at runtime)
# tie-rod points are used as given in the JSON (2026-10-13 end take-off geometry; the old 0.333 m inner-height re-derivation is removed)
TIE_ROD_HEIGHTS = (float(TIE_ROD_INNER[2]), float(TIE_ROD_INNER[2]))


# @description - Position of the lower ball joint after the lower arm has rotated about its pivot axis (kinematic constraint: arm length is constant).
# @param {float} arm_angle - rotation of the lower arm about its pivot axis (rad)
# @return {ndarray} - ball joint position in metres, shape (3,)
def arm_point(arm_angle):
    arm_vector = LOWER_BALL_JOINT - ARM_FRONT_PIVOT
    return ARM_FRONT_PIVOT + Rotation.from_rotvec(ARM_PIVOT_AXIS * arm_angle).apply(arm_vector)


# @description - Builds the rigid-body pose of the knuckle from the four solver unknowns.
# @param {ndarray} unknowns - [arm rotation (rad), knuckle rotation vector x, y, z (rad)]
# @return {tuple} - (ball joint position (m), knuckle Rotation, function mapping a design-position point (m) to its current position)
def knuckle(unknowns):
    arm_angle, rotation_vector = unknowns[0], unknowns[1:4]
    ball_joint = arm_point(arm_angle)
    rotation = Rotation.from_rotvec(rotation_vector)

    # @description - Moves a point rigidly attached to the knuckle from its design position to the current pose.
    # @param {ndarray} design_point - point position at design (m), shape (3,)
    # @return {ndarray} - the same point in the current pose (m), shape (3,)
    def transform_point(design_point):
        return ball_joint + rotation.apply(design_point - LOWER_BALL_JOINT)
    return ball_joint, rotation, transform_point


# @description - Constraint residuals of the front corner: strut axis through the top mount, constant tie-rod length, wheel centre at the requested height.
# @param {ndarray} unknowns - solver unknowns (see knuckle)
# @param {float} travel - wheel-centre vertical travel from design height (m, + bump)
# @param {float} rack - rack displacement from centre (m)
# @return {ndarray} - 5 residuals: strut-axis misalignment x3 (m, weighted x10), tie-rod length error (m), wheel height error (m)
def residual(unknowns, travel, rack):
    ball_joint, rotation, transform_point = knuckle(unknowns)
    strut_direction = rotation.apply(STRUT_AXIS)
    top_mount_offset = STRUT_TOP_MOUNT - ball_joint
    perpendicular_offset = top_mount_offset - top_mount_offset.dot(strut_direction) * strut_direction   # strut axis passes through top mount
    inner_tie_rod = TIE_ROD_INNER + np.array([0, rack, 0])
    tie_rod_error = np.linalg.norm(transform_point(TIE_ROD_OUTER) - inner_tie_rod) - TIE_ROD_LENGTH
    wheel_height_error = transform_point(WHEEL_CENTRE)[2] - (WHEEL_CENTRE[2] + travel)
    return np.concatenate([perpendicular_offset * 10, [tie_rod_error, wheel_height_error]])


# @description - Solves the front corner pose for one wheel travel and rack position by least squares.
# @param {float} travel - wheel-centre vertical travel from design height (m, + bump)
# @param {float} rack - rack displacement from centre (m)
# @param {ndarray} start_unknowns - starting guess for the solver unknowns (see knuckle)
# @return {tuple} - (solved unknowns, final least-squares cost)
def solve_front(travel, rack, start_unknowns):
    solution = least_squares(residual, start_unknowns, args=(travel, rack), xtol=1e-12, ftol=1e-12)
    return solution.x, solution.cost


# @description - Camber (deg, + top out, left wheel) of the wheel for a given spin axis.
# @param {ndarray} spin_axis_now - wheel spin axis in the current pose, unit vector
# @return {float} - camber angle in degrees
def camber_degrees(spin_axis_now):
    return -np.degrees(np.arcsin(spin_axis_now[2]))


# @description - Toe angle (deg, + toe-in: front of the wheel points to -y) of the wheel for a given spin axis.
# @param {ndarray} spin_axis_now - wheel spin axis in the current pose, unit vector
# @return {float} - toe angle in degrees
def toe_degrees(spin_axis_now):
    forward_direction = np.cross(spin_axis_now, [0, 0, 1.0])
    return -np.degrees(np.arctan2(forward_direction[1], forward_direction[0]))


# @description - Front-view roll-centre height of the corner: instant centre = intersection of the line through the top mount perpendicular to the strut axis and the lower-arm line.
# @param {ndarray} ball_joint - lower ball joint position (m)
# @param {ndarray} strut_direction - strut axis in the current pose, unit vector
# @param {ndarray} wheel_centre - wheel centre position in the current pose (m)
# @return {float} - roll-centre height above the ground (m)
def roll_centre_height(ball_joint, strut_direction, wheel_centre):
    top_mount_yz = np.array([STRUT_TOP_MOUNT[1], STRUT_TOP_MOUNT[2]])
    perpendicular_direction = np.array([-strut_direction[2], strut_direction[1]])          # perpendicular to the strut axis in the y-z plane
    pivot_in_ball_joint_plane = ARM_FRONT_PIVOT + ARM_PIVOT_AXIS * ((ball_joint - ARM_FRONT_PIVOT).dot(ARM_PIVOT_AXIS))
    pivot_yz = np.array([pivot_in_ball_joint_plane[1], pivot_in_ball_joint_plane[2]])
    arm_direction = np.array([ball_joint[1], ball_joint[2]]) - pivot_yz
    line_matrix = np.array([perpendicular_direction, -arm_direction]).T
    line_parameters = np.linalg.solve(line_matrix, pivot_yz - top_mount_yz)
    instant_centre = top_mount_yz + line_parameters[0] * perpendicular_direction
    contact_point = np.array([wheel_centre[1], wheel_centre[2] - LOADED_RADIUS])           # contact point (approx. below the wheel centre)
    ray_parameter = -contact_point[0] / (instant_centre[0] - contact_point[0])
    return contact_point[1] + ray_parameter * (instant_centre[1] - contact_point[1])


# @description - Derived kinematic outputs of one solved front pose.
# @param {ndarray} unknowns - solved unknowns (see knuckle)
# @return {dict} - camber (deg), toe (deg), wheel_centre_y and wheel_centre_z wheel-centre position (m), strut_length (m), roll_centre height (m)
def outputs(unknowns):
    ball_joint, rotation, transform_point = knuckle(unknowns)
    spin_axis_now = rotation.apply(SPIN_AXIS)
    wheel_centre = transform_point(WHEEL_CENTRE)
    strut_direction = rotation.apply(STRUT_AXIS)
    strut_length = (STRUT_TOP_MOUNT - ball_joint).dot(strut_direction)
    return dict(camber=camber_degrees(spin_axis_now), toe=toe_degrees(spin_axis_now), wheel_centre_y=wheel_centre[1], wheel_centre_z=wheel_centre[2], strut_length=strut_length,
                roll_centre=roll_centre_height(ball_joint, strut_direction, wheel_centre))


# @description - Solves the whole front sweep (wheel travel x rack), continuing from the nearest already-solved rack position for robust convergence.
# @param {ndarray} travel - wheel travel samples (m, + bump)
# @param {ndarray} racks - rack displacement samples (m)
# @param {ndarray} design_unknowns - solver unknowns at design height and zero rack
# @return {tuple} - (dict mapping (travel, rack) to outputs(), list of least-squares costs of every solved point)
def sweep_front(travel, racks, design_unknowns):
    grid = {}
    costs = []
    centre_solutions = {}
    solve_order = sorted(range(len(racks)), key=lambda index: abs(racks[index]))
    for rack_index in solve_order:
        rack = racks[rack_index]
        nearest = min(centre_solutions, key=lambda solved_rack: abs(solved_rack - rack)) if centre_solutions else None
        rack_unknowns, cost = solve_front(0.0, rack, centre_solutions[nearest] if nearest is not None else design_unknowns)
        assert cost < 1e-10, (rack, cost)
        centre_solutions[rack] = rack_unknowns
        design_index = int(np.argmin(abs(travel)))
        for travel_indices in (range(design_index, len(travel)), range(design_index, -1, -1)):
            unknowns = rack_unknowns.copy()
            for travel_index in travel_indices:
                unknowns, cost = solve_front(travel[travel_index], rack, unknowns)
                costs.append(cost)
                grid[(travel[travel_index], rack)] = outputs(unknowns)
    return grid, costs


# @description - Rear damper length for a given wheel travel: the damper bottom rotates with the trailing arm about the lateral pivot axis.
# @param {float} travel - wheel travel (m, + bump)
# @param {dict} rear_geometry - pivot, damper_top, damper_bottom (m) and arm_length (m) of the rear suspension
# @return {float} - damper length (m)
def rear_damper_length(travel, rear_geometry):
    arm_angle = np.arcsin(travel / rear_geometry['arm_length'])
    bottom_offset = rear_geometry['damper_bottom'] - rear_geometry['pivot']
    rotation = Rotation.from_rotvec([0, arm_angle, 0])
    return np.linalg.norm(rear_geometry['damper_top'] - (rear_geometry['pivot'] + rotation.apply(bottom_offset)))


# @description - Rear twist-beam geometry from the parameter file and the derived camber-follow fraction.
# @return {dict} - pivot, damper_top, damper_bottom (m), arm_length (m), camber_fraction (wheel tilt relative to the body per unit body roll, dimensionless)
def rear_geometry():
    pivot = np.array(REAR['trailing_arm_pivot'][0])
    wheel_centre = np.array(REAR['wheel_centre'][0])
    shear_centre_x = REAR['beam_shear_centre_x'][0]
    arm_length = np.hypot(*(wheel_centre[[0, 2]] - pivot[[0, 2]]))
    shear_offset = pivot[0] - shear_centre_x
    beam_width = 2 * pivot[1]
    rear_track = PARAMETERS['vehicle']['track_rear'][0]
    camber_fraction = shear_offset * rear_track / (beam_width * arm_length)       # wheel tilt relative to body per unit body-roll (counteracts trailing-arm camber)
    return dict(pivot=pivot, damper_top=np.array(REAR['damper_top'][0]), damper_bottom=np.array(REAR['damper_bottom'][0]), arm_length=arm_length, camber_fraction=camber_fraction)


# @description - Front sweep results as the arrays the Unity tables use.
# @param {dict} grid - mapping (travel, rack) to outputs() from sweep_front
# @param {ndarray} travel - wheel travel samples (m)
# @param {ndarray} racks - rack samples (m)
# @param {dict} design_outputs - outputs() at design height and zero rack
# @return {dict} - camber/toe/track/roll-centre/motion-ratio and steer-grid arrays (degrees and metres)
def front_tables(grid, travel, racks, design_outputs):
    centre_rack_index = int(np.argmin(abs(racks)))
    centre_column = [grid[(travel_value, racks[centre_rack_index])] for travel_value in travel]
    camber = np.array([point['camber'] - design_outputs['camber'] for point in centre_column])
    toe = np.array([point['toe'] - design_outputs['toe'] for point in centre_column])
    track_change = np.array([point['wheel_centre_y'] - design_outputs['wheel_centre_y'] for point in centre_column])
    roll_centre = np.array([point['roll_centre'] for point in centre_column])
    strut_length = np.array([point['strut_length'] for point in centre_column])
    motion_ratio = np.gradient(-strut_length, travel)                             # spring/damper compression per wheel travel
    # steer angle (left wheel, + = wheel points right = toe-in) vs rack at design height, and camber vs steer angle
    steer_angle = np.array([grid[(0.0, rack)]['toe'] - design_outputs['toe'] for rack in racks])
    toe_by_travel_and_rack = np.array([[grid[(travel_value, rack)]['toe'] - design_outputs['toe'] for rack in racks] for travel_value in travel])   # [travel, rack] left-wheel toe-in = steer (+ right), bump steer included
    camber_by_travel_and_rack = np.array([[grid[(travel_value, rack)]['camber'] - design_outputs['camber'] for rack in racks] for travel_value in travel])   # [travel, rack]
    return dict(camber=camber, toe=toe, track_change=track_change, roll_centre=roll_centre, motion_ratio=motion_ratio, steer_angle=steer_angle,
                toe_by_travel_and_rack=toe_by_travel_and_rack, camber_by_travel_and_rack=camber_by_travel_and_rack)


# @description - Writes tables.json (read by multibody_compliance.py, export_unity.py and the Unity tests).
# @param {dict} front - arrays from front_tables
# @param {dict} rear - rear_geometry() result
# @param {ndarray} travel - wheel travel samples (m)
# @param {ndarray} racks - rack samples (m)
# @param {dict} design_outputs - outputs() at design height and zero rack
# @param {dict} rear_motion_ratio - rear damper motion ratio per travel sample (dimensionless array in 'values')
# @return {dict} - the dictionary that was written
def write_tables(front, rear, travel, racks, design_outputs, rear_motion_ratio):
    rear_roll_centre_height = PARAMETERS['kinematics_targets']['rear_roll_centre_height_m'][0]   # D in the JSON; a simple swing-arm estimate is kept for comparison
    design = dict(tie_rod_inner_z_json=TIE_ROD_HEIGHTS[0], tie_rod_inner_z_used=TIE_ROD_HEIGHTS[1], front_roll_centre_height=design_outputs['roll_centre'],
                  kpi_deg=float(np.degrees(np.arctan2(-STRUT_AXIS[1], STRUT_AXIS[2]))), caster_deg=float(np.degrees(np.arctan2(-STRUT_AXIS[0], STRUT_AXIS[2]))))
    tables = dict(
        _note="Generated by kinematics/solve_kinematics.py from suspension_params.json. Hardpoints partly ESTIMATED (E); tables inherit that.",
        travel=travel.tolist(), front_camber_body=front['camber'].tolist(), front_bumpsteer_toe=front['toe'].tolist(), front_track_change=front['track_change'].tolist(),
        front_roll_centre_height=front['roll_centre'].tolist(), front_motion_ratio=front['motion_ratio'].tolist(), rack=racks.tolist(), front_steer_vs_rack=front['steer_angle'].tolist(),
        front_toe_travel_rack=front['toe_by_travel_and_rack'].tolist(), front_camber_travel_rack=front['camber_by_travel_and_rack'].tolist(),
        rear_camber_roll_fraction=float(rear['camber_fraction']), rear_roll_centre_height=float(rear_roll_centre_height),
        rear_motion_ratio=rear_motion_ratio.tolist(), design=design)
    json.dump(tables, open(params_io.out('tables.json'), 'w'), indent=1)
    return tables


# @description - Prints the solver quality and the headline kinematic numbers.
# @param {list} costs - least-squares cost of every solved point
# @param {dict} tables - dictionary returned by write_tables
# @param {dict} front - arrays from front_tables
# @param {ndarray} travel - wheel travel samples (m)
# @param {ndarray} rear_motion_ratio - rear motion ratio per travel sample
# @param {float} rear_camber_fraction - rear camber-follow fraction (dimensionless)
# @return {Void} - nothing
def print_summary(costs, tables, front, travel, rear_motion_ratio, rear_camber_fraction):
    design = tables['design']
    camber_slope = np.polyfit(travel[5:14], front['camber'][5:14], 1)[0]
    bump_steer_slope = np.polyfit(travel[5:14], front['toe'][5:14], 1)[0]
    print("max residual cost", max(costs), "points>1e-10:", sum(cost > 1e-10 for cost in costs))
    print(f"front RC at design {design['front_roll_centre_height']*1000:.0f} mm, KPI {design['kpi_deg']:.2f} deg, caster {design['caster_deg']:.2f} deg")
    print(f"camber gain {camber_slope:.1f} deg/m, bump steer {bump_steer_slope:.2f} deg/m (+=toe-in)")
    print(f"front MR mean {front['motion_ratio'].mean():.3f}, rear MR mean {rear_motion_ratio.mean():.3f}, rear camber-vs-body-roll correction {rear_camber_fraction:.2f} (ground camber follows {1-rear_camber_fraction:.2f} x roll)")
    steer_angle = front['steer_angle']
    camber_by_travel_and_rack = front['camber_by_travel_and_rack']
    print(f"steer at rack +-70mm: {steer_angle[0]:.1f} / {steer_angle[-1]:.1f} deg; camber change at that steer: {camber_by_travel_and_rack[9,0]:.2f}/{camber_by_travel_and_rack[9,-1]:.2f}")


# @description - Draws kinematics.png: camber, bump steer, track change, roll centre, motion ratio and camber vs steer.
# @param {dict} front - arrays from front_tables
# @param {ndarray} travel - wheel travel samples (m)
# @param {ndarray} rear_motion_ratio - rear motion ratio per travel sample
# @return {Void} - nothing (writes the PNG)
def plot_kinematics(front, travel, rear_motion_ratio):
    figure, axes = plt.subplots(2, 3, figsize=(15, 8))
    panels = [(axes[0, 0], front['camber'], 'front camber change vs body [deg] (+ top out)'), (axes[0, 1], front['toe'], 'front bump steer [deg] (+ toe-in)'),
              (axes[0, 2], front['track_change'] * 1000, 'front half-track change [mm]'), (axes[1, 0], front['roll_centre'] * 1000, 'front roll centre height [mm]'),
              (axes[1, 1], front['motion_ratio'], 'motion ratio (front strut, rear damper dashed)')]
    for axis, values, title in panels:
        axis.plot(travel * 1000, values)
        axis.set_title(title, fontsize=9)
        axis.set_xlabel('wheel travel [mm] (+bump)')
        axis.grid()
    axes[1, 1].plot(travel * 1000, rear_motion_ratio, '--')
    for index, travel_value in enumerate(travel):
        if index % 3 == 0:
            axes[1, 2].plot(front['steer_angle'], front['camber_by_travel_and_rack'][index], label=f'{travel_value*1000:.0f} mm')
    axes[1, 2].set_title('front camber change vs wheel steer angle', fontsize=9)
    axes[1, 2].set_xlabel('left wheel steer [deg] (+ right)')
    axes[1, 2].legend(fontsize=7)
    axes[1, 2].grid()
    plt.suptitle('Scirocco II kinematics from suspension_params.json (hardpoints partly ESTIMATED)')
    plt.tight_layout()
    plt.savefig(params_io.out('kinematics.png'), dpi=100)


# @description - Runs the full offline kinematics solve: front sweep, rear beam geometry, tables.json, console summary and kinematics.png.
# @return {Void} - nothing
def main():
    travel = np.round(np.arange(-0.09, 0.0801, 0.01), 3)
    racks = np.round(np.linspace(-0.07, 0.07, 71), 4)   # +-70 mm rack in 2 mm steps (D; linear-interpolation error < 0.02 deg at lock): covers the +-68.6 mm stop; per-wheel steer comes from this grid at runtime
    design_unknowns, _ = solve_front(0, 0, np.zeros(4))
    design_outputs = outputs(design_unknowns)
    grid, costs = sweep_front(travel, racks, design_unknowns)
    front = front_tables(grid, travel, racks, design_outputs)
    rear = rear_geometry()
    # arm-arc fore/aft wheel motion -> negligible toe for symmetric bump on a beam axle (both wheels move together)
    rear_damper_lengths = np.array([rear_damper_length(travel_value, rear) for travel_value in travel])
    rear_motion_ratio = np.gradient(-rear_damper_lengths, travel)
    tables = write_tables(front, rear, travel, racks, design_outputs, rear_motion_ratio)
    print_summary(costs, tables, front, travel, rear_motion_ratio, rear['camber_fraction'])
    plot_kinematics(front, travel, rear_motion_ratio)


if __name__ == '__main__':
    main()
