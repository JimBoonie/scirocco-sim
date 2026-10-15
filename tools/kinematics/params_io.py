#!/usr/bin/python3
"""Shared loader so the sensitivity study can re-run the solvers on perturbed ESTIMATED inputs without touching defaults.
Environment:
  KINEMATICS_OUTPUT_DIRECTORY - directory for generated files (default '.')
  MULTIBODY_PERTURBATIONS_JSON - JSON dict of perturbations:
    front_pivot_delta_z (m, both lower-arm pivots), rear_pivot_delta_z (m, trailing-arm pivot), front_bush_scale, rear_bush_scale (factors),
    shear_centre_x (m, absolute beam shear-centre x), roll_scale (factor on the rear beam torsion/roll stiffness)."""
import json
import os

OUTPUT_DIRECTORY = os.environ.get('KINEMATICS_OUTPUT_DIRECTORY', '.')
PERTURBATIONS = json.loads(os.environ.get('MULTIBODY_PERTURBATIONS_JSON', '{}'))
FRONT_BUSHING_NAMES = ('front_arm_front_N_per_mm', 'front_arm_rear_N_per_mm')
REAR_BUSHING_NAMES = ('rear_beam_pivot_N_per_mm',)


# @description - Moves both front lower-arm pivots up or down by the perturbation, when one is requested (metres, vehicle z up).
# @param {dict} front - the 'front_macpherson' section of the parameters, modified in place
# @return {Void} - nothing
def perturb_front_pivot_height(front):
    if 'front_pivot_delta_z' not in PERTURBATIONS:
        return
    for pivot_name in ('lower_arm_front_pivot', 'lower_arm_rear_pivot'):
        front[pivot_name][0][2] += PERTURBATIONS['front_pivot_delta_z']


# @description - Moves the rear trailing-arm pivot up or down by the perturbation, when one is requested (metres, vehicle z up).
# @param {dict} rear - the 'rear_twist_beam' section of the parameters, modified in place
# @return {Void} - nothing
def perturb_rear_pivot_height(rear):
    if 'rear_pivot_delta_z' in PERTURBATIONS:
        rear['trailing_arm_pivot'][0][2] += PERTURBATIONS['rear_pivot_delta_z']


# @description - Scales the radial and axial stiffness of the named rubber bushes (N/mm) when the matching perturbation factor is requested.
# @param {dict} bushings - the 'bushings' section of the parameters, modified in place
# @return {Void} - nothing
def perturb_bushing_rates(bushings):
    for perturbation_key, bushing_names in (('front_bush_scale', FRONT_BUSHING_NAMES), ('rear_bush_scale', REAR_BUSHING_NAMES)):
        if perturbation_key not in PERTURBATIONS:
            continue
        for bushing_name in bushing_names:
            for direction in ('radial', 'axial'):
                bushings[bushing_name][direction][0] *= PERTURBATIONS[perturbation_key]


# @description - Applies the beam shear-centre and roll-stiffness perturbations (x in metres, stiffness in N m/deg).
# @param {dict} parameters - the whole parameter dictionary, modified in place
# @return {Void} - nothing
def perturb_rear_beam(parameters):
    if 'shear_centre_x' in PERTURBATIONS:
        parameters['rear_twist_beam']['beam_shear_centre_x'][0] = PERTURBATIONS['shear_centre_x']
    if 'roll_scale' in PERTURBATIONS:
        parameters['anti_roll']['rear_roll_stiffness_total_Nm_per_deg'][0] *= PERTURBATIONS['roll_scale']


# @description - Loads suspension_params.json (one directory above this file) and applies the requested perturbations of ESTIMATED inputs.
# @return {dict} - the parameter dictionary with every perturbation applied
def load():
    parameters_path = os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', 'suspension_params.json')
    parameters = json.load(open(parameters_path))
    perturb_front_pivot_height(parameters['front_macpherson'])
    perturb_rear_pivot_height(parameters['rear_twist_beam'])
    perturb_bushing_rates(parameters['bushings'])
    perturb_rear_beam(parameters)
    return parameters


# @description - Builds the path of a generated file inside the output directory.
# @param {str} file_name - file name without directory, for example 'tables.json'
# @return {str} - path of the file inside KINEMATICS_OUTPUT_DIRECTORY
def out(file_name):
    return os.path.join(OUTPUT_DIRECTORY, file_name)
