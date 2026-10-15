#!/usr/bin/python3
# @description - Metrics from visco_smoke.py runs. usage: visco_metrics.py <smoke_dir> -> metrics.csv + markdown table on stdout
import sys
import os
import re
import numpy as np
import pandas as pd

SKIDPAD_RADIUS = 40.0   # m skidpad circle radius centered at x = radius, z = 0 (S: scenario definition)


# @description - Reads one scenario CSV of one configuration.
# @param {String} smoke_directory - directory written by visco_smoke.py
# @param {String} configuration - configuration sub-directory name
# @param {String} scenario - scenario name (file name without .csv)
# @return {DataFrame} - the time series, or None when the file is missing or has 10 rows or fewer
def load_scenario(smoke_directory, configuration, scenario):
    path = f'{smoke_directory}/{configuration}/{scenario}.csv'
    if not os.path.exists(path):
        return None
    data = pd.read_csv(path)
    return data if len(data) > 10 else None


# @description - Skidpad metrics: peak lateral acceleration (100-sample mean, g) and understeer gradient (deg/g, linear fit of steer vs lateral g below 0.5 g, SD: SAE J266).
# @param {DataFrame} data - skidpad time series
# @return {Dict} - skid_ay_max (g), skid_us (deg/g)
def skidpad_metrics(data):
    data = data[data.time_s > 8]
    on_circle = data[abs(np.hypot(data.x_m - SKIDPAD_RADIUS, data.z_m) - SKIDPAD_RADIUS) < 1.5]
    linear_range = on_circle[abs(on_circle.lateral_g) < 0.5]
    return dict(skid_ay_max=abs(on_circle.lateral_g).rolling(100, center=True).mean().max(), skid_us=np.polyfit(abs(linear_range.lateral_g), linear_range.steer_deg, 1)[0])


# @description - Step-steer metrics measured after the first 4 s of the step.
# @param {DataFrame} data - step-steer time series
# @return {Dict} - lateral g, steady yaw rate (deg/s), overshoot (%), 90 % rise time (s)
def step_metrics(data):
    step = data[data.phase == 1]
    start_time = step.time_s.iloc[0]
    yaw_rate = step.yaw_rate_dps.abs()
    steady_yaw = yaw_rate[step.time_s > start_time + 4].mean()
    lateral_g = step.lateral_g.abs()[step.time_s > start_time + 4].mean()
    time_to_90_percent = step.time_s[yaw_rate >= 0.9 * steady_yaw].iloc[0] - start_time
    return dict(step_ay=lateral_g, step_r_ss=steady_yaw, step_overshoot=(yaw_rate.max() / steady_yaw - 1) * 100, step_t90=time_to_90_percent)


# @description - Braking metrics: path length of the braking phase and its initial speed.
# @param {DataFrame} data - brake time series
# @return {Dict} - brake_dist (m), brake_v0 (km/h)
def brake_metrics(data):
    braking = data[data.phase == 1]
    distance = np.hypot(np.diff(braking.x_m), np.diff(braking.z_m)).sum()
    return dict(brake_dist=distance, brake_v0=braking.speed_kph.iloc[0])


# @description - Lift-off metrics: lateral g before, change of yaw rate after 1 s (deg/s), peak sideslip change (deg).
# @param {DataFrame} data - lift-off time series
# @return {Dict} - lift_ay0 (g), lift_dr_1s (deg/s), lift_dbeta_max (deg)
def lift_off_metrics(data):
    before = data[data.phase == 0].iloc[-50:]
    after = data[data.phase == 1]
    start_time = after.time_s.iloc[0]
    one_second_row = after[after.time_s <= start_time + 1.0].iloc[-1]
    return dict(lift_ay0=before.lateral_g.abs().mean(), lift_dr_1s=abs(one_second_row.yaw_rate_dps) - before.yaw_rate_dps.abs().mean(), lift_dbeta_max=(after.sideslip_deg.abs().max() - before.sideslip_deg.abs().mean()))


# @description - Mean bulk temperature (F) and grip (%) over the wheels in one THERM log line.
# @param {String} line - THERM log line
# @return {Tuple} - (mean bulk temperature, mean grip percentage), NaN when absent
def mean_temperature_and_grip(line):
    bulk = [float(value) for value in re.findall(r'bulk (-?\d+)', line)]
    grip = [float(value) for value in re.findall(r'grip (\d+)%', line)]
    return np.mean(bulk) if bulk else np.nan, np.mean(grip) if grip else np.nan


# @description - Lap times and tire thermal state from the Laguna thermal log of one configuration.
# @param {String} smoke_directory - directory written by visco_smoke.py
# @param {String} configuration - configuration sub-directory name
# @return {Dict} - lap<N> times (s), bulkF_t30/t60/t120 (F), bulkF_end (F), grip_end (%), fail flag; empty when there is no log
def laguna_metrics(smoke_directory, configuration):
    path = f'{smoke_directory}/{configuration}/lagunatherm.log'
    if not os.path.exists(path):
        return {}
    lines = [line for line in open(path, errors='replace') if line.startswith('THERM')]
    metrics = {}
    laps = [(int(match.group(1)), float(match.group(2))) for line in lines for match in [re.search(r'LAP (\d+) ([\d.]+)s', line)] if match]
    previous_time = 0
    for lap_number, lap_time in laps:
        metrics[f'lap{lap_number}'] = lap_time - previous_time
        previous_time = lap_time
    for line in lines:
        match = re.search(r't (\d+) ', line)
        if match and int(match.group(1)) in (30, 60, 120):
            metrics[f'bulkF_t{match.group(1)}'], _ = mean_temperature_and_grip(line)
    lap_lines = [line for line in lines if 'LAP' in line]
    if lap_lines:
        metrics['bulkF_end'], metrics['grip_end'] = mean_temperature_and_grip(lap_lines[-1])
    metrics['fail'] = any('FAIL' in line for line in lines)
    return metrics


# @description - Collects the metrics of every configuration, saves metrics.csv and prints the transposed table.
# @param {String} smoke_directory - directory written by visco_smoke.py
# @return {Void} - nothing
def main(smoke_directory):
    rows = []
    for configuration in sorted(os.listdir(smoke_directory)):
        row = dict(cfg=configuration)
        for scenario, metric_function in (('skidpad', skidpad_metrics), ('stepsteer', step_metrics), ('brakethr', brake_metrics), ('liftoff', lift_off_metrics)):
            data = load_scenario(smoke_directory, configuration, scenario)
            if data is not None:
                try:
                    row.update(metric_function(data))
                except Exception as error:
                    row[scenario + '_err'] = str(error)[:40]
        row.update(laguna_metrics(smoke_directory, configuration))
        rows.append(row)
    table = pd.DataFrame(rows).set_index('cfg')
    table.to_csv(f'{smoke_directory}/metrics.csv')
    pd.set_option('display.width', 250)
    print(table.round(3).T.to_string())


if __name__ == '__main__':
    main(sys.argv[1])
