#!/usr/bin/python3
# @description - Headless smoke comparison Default vs Experimental (viscoelastic) compound + one-at-a-time ablations.
# usage: visco_smoke.py <out_dir> <base_build_dir> <new_build_dir>   (Linux players; -overrides files written per configuration)
# Environment: ONLY (comma-separated configuration names to run), PAR (parallel players, default 6).
import os
import sys
import subprocess
import time

SCENARIOS = ['skidpad', 'stepsteer', 'brakethr', 'liftoff', 'lagunatherm']
ABLATION_FLAGS = ['viscoHysteresisEnabled', 'viscoAdhesionEnabled', 'viscoWilliamsLandelFerryEnabled', 'viscoFlashTemperatureEnabled', 'viscoPayneEffectEnabled', 'viscoRateStiffnessEnabled', 'viscoLoadRelaxationEnabled', 'viscoRollingResistanceEnabled', 'viscoBulkHeatEnabled']
EXPERIMENTAL_OVERRIDES = ['carParams.tireCompound=ExperimentalViscoelastic']
CSV_HEADER = 'scenario,time_s,x_m,z_m,speed_kph,longitudinal_g,lateral_g,yaw_rate_dps,sideslip_deg,steer_deg,throttle,brake,gear,engine_rpm,load_front_left_n,load_front_right_n,load_rear_left_n,load_rear_right_n,wheel_speed_front_left_rad_s,wheel_speed_front_right_rad_s,wheel_speed_rear_left_rad_s,wheel_speed_rear_right_rad_s,pitch_deg,roll_deg,phase\n'


# @description - Rewrites override keys for the BASE player, which predates the carParams. override-key rename and still reads p.<field>.
# @param {List} keys - override lines starting with "carParams."
# @return {List} - the same lines starting with "p."
def legacy_prefix(keys):
    return [key.replace('carParams.', 'p.', 1) for key in keys]


# @description - Builds the table of configurations: name -> (player directory, override lines).
# @param {String} base_directory - player build that predates the viscoelastic compound
# @param {String} new_directory - player build with the viscoelastic compound
# @return {Dict} - configuration table including one ablation per flag
def build_configurations(base_directory, new_directory):
    configurations = {'base_off': (base_directory, []), 'base_th': (base_directory, legacy_prefix(['carParams.tireThermal=true'])), 'new_off': (new_directory, []), 'new_th': (new_directory, ['carParams.tireThermal=true']),
                      'exp_off': (new_directory, EXPERIMENTAL_OVERRIDES), 'exp_th': (new_directory, EXPERIMENTAL_OVERRIDES + ['carParams.tireThermal=true'])}
    for flag in ABLATION_FLAGS:
        configurations['abl_' + flag] = (new_directory, EXPERIMENTAL_OVERRIDES + ['carParams.tireThermal=true', f'carParams.{flag}=false'])
    return configurations


# @description - Writes each selected configuration's override file and lists the (configuration, scenario) jobs; the thermal Laguna run is skipped for the thermal-off configurations.
# @param {Dict} configurations - table from build_configurations
# @param {String} output_directory - results root
# @return {List} - (configuration name, scenario) tuples
def prepare_jobs(configurations, output_directory):
    only = os.environ.get('ONLY')
    selected = [name for name in configurations if not only or name in only.split(',')]
    jobs = []
    for name in selected:
        directory = f'{output_directory}/{name}'
        os.makedirs(directory, exist_ok=True)
        open(f'{directory}/ov.txt', 'w').write('\n'.join(['# ' + name] + configurations[name][1]) + '\n')
        for scenario in SCENARIOS:
            if scenario == 'lagunatherm' and name.endswith('_off'):
                continue
            jobs.append((name, scenario))
    return jobs


# @description - Starts one headless player run.
# @param {String} configuration - configuration name
# @param {String} scenario - scenario name
# @param {Dict} configurations - table from build_configurations
# @param {String} output_directory - results root
# @return {Popen} - the running process
def launch(configuration, scenario, configurations, output_directory):
    directory = f'{output_directory}/{configuration}'
    extra_arguments = '-track laguna' if scenario.startswith('laguna') else ''
    return subprocess.Popen(f'cd {configurations[configuration][0]} && timeout 900 ./SciroccoSimDemo.x86_64 -batchmode -nographics -smoketest -scenario {scenario} {extra_arguments} -overrides {directory}/ov.txt -logFile {directory}/{scenario}.log >/dev/null 2>&1', shell=True)


# @description - Runs all jobs with at most PAR players alive at once and waits for them.
# @param {List} jobs - (configuration, scenario) tuples
# @param {Dict} configurations - table from build_configurations
# @param {String} output_directory - results root
# @return {Void} - nothing
def run_jobs(jobs, configurations, output_directory):
    running = []
    parallel_players = int(os.environ.get('PAR', '6'))
    for configuration, scenario in jobs:
        while len([process for process in running if process.poll() is None]) >= parallel_players:
            time.sleep(2)
        running.append(launch(configuration, scenario, configurations, output_directory))
    for process in running:
        process.wait()


# @description - Converts each log to <scenario>.csv (header plus CSV rows) and <scenario>.therm (THERM, SMOKE and exception lines).
# @param {List} jobs - (configuration, scenario) tuples
# @param {String} output_directory - results root
# @return {Void} - nothing
def convert_logs(jobs, output_directory):
    for configuration, scenario in jobs:
        directory = f'{output_directory}/{configuration}'
        lines = open(f'{directory}/{scenario}.log', errors='replace').read().splitlines()
        open(f'{directory}/{scenario}.csv', 'w').write(CSV_HEADER + ''.join(line[4:] + '\n' for line in lines if line.startswith('CSV,')))
        open(f'{directory}/{scenario}.therm', 'w').write(''.join(line + '\n' for line in lines if line.startswith('THERM') or line.startswith('SMOKE') or 'Exception' in line))


# @description - Runs the whole comparison.
# @param {List} arguments - command-line arguments: output directory, base build directory, new build directory
# @return {Void} - prints "done <job count>"
def main(arguments):
    output_directory, base_directory, new_directory = arguments[0:3]
    configurations = build_configurations(base_directory, new_directory)
    jobs = prepare_jobs(configurations, output_directory)
    run_jobs(jobs, configurations, output_directory)
    convert_logs(jobs, output_directory)
    print('done', len(jobs))


if __name__ == '__main__':
    main(sys.argv[1:])
