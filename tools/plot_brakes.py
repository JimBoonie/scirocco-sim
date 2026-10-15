#!/usr/bin/python3
# @description - Renders the experimental brake diagnostics from the CSV files that BrakeIntegrationTests writes to diagnostics/brakes: stopping_distance.png (speed, deceleration, wheel lock and distance of four brake schemes) and pedal_response.png (pedal force to master pressure and torques for a healthy, a half and a dead booster, plus the first 0.6 s of a keyboard press). usage: plot_brakes.py <diagnostics_brakes_dir>
import csv
import os
import sys

import matplotlib
matplotlib.use('Agg')
import matplotlib.pyplot as plt

STOP_FILES = [('stop_legacy.csv', 'legacy fixed torques (1400 / 300 N m)'), ('stop_keyboard.csv', 'experimental, keyboard full press'),
              ('stop_loadcell70.csv', 'experimental, load cell 70 lb'), ('stop_deadbooster.csv', 'experimental, 70 lb, dead booster')]
KEYBOARD_ZOOM_SECONDS = 0.6   # s of the keyboard press shown in the pedal response plot
FIGURE_DPI = 130


# @description - Reads a CSV file into a list of dictionaries with float values where possible.
# @param {str} path - CSV file path
# @return {list} - one dictionary per row
def read_rows(path):
    with open(path) as handle:
        return [{key: (float(value) if value.replace('.', '', 1).replace('-', '', 1).isdigit() else value) for key, value in row.items()} for row in csv.DictReader(handle)]


# @description - Draws speed, deceleration, front and rear torque and the wheel speed ratio of the four stopping runs and saves stopping_distance.png.
# @param {str} directory - diagnostics directory
# @return {None} - nothing
def plot_stopping(directory):
    figure, axes = plt.subplots(2, 2, figsize=(13, 8.5))
    summary = {row['scheme']: row for row in read_rows(os.path.join(directory, 'stop_summary.csv'))}
    for file_name, label in STOP_FILES:
        rows = read_rows(os.path.join(directory, file_name))
        times = [row['time_s'] for row in rows]
        axes[0][0].plot(times, [row['speed_ms'] * 3.6 for row in rows], label=label)
        axes[0][1].plot(times, [row['deceleration_g'] for row in rows], label=label)
        axes[1][0].plot(times, [row['front_torque_Nm'] for row in rows], label=label + ' front')
        axes[1][0].plot(times, [row['rear_torque_Nm'] for row in rows], '--', label=label + ' rear')
        axes[1][1].plot(times, [row['front_surface_speed_ms'] / max(row['speed_ms'], 0.5) for row in rows], label=label + ' front')
    axes[0][0].set_ylabel('speed (km/h)')
    axes[0][1].set_ylabel('deceleration (g)')
    axes[1][0].set_ylabel('brake torque per wheel (N m)')
    axes[1][1].set_ylabel('front wheel speed / vehicle speed')
    for axis in axes.flat:
        axis.set_xlabel('time (s)')
        axis.grid(alpha=0.3)
    axes[0][0].legend(fontsize=7)
    distances = ', '.join('%s %.1f m' % (name, float(row['distance_m'])) for name, row in summary.items())
    figure.suptitle('Experimental brakes: 100-0 km/h straight stop, two-axle model with the default tire (not tuned; period road tests about 46.5 m)\n' + distances, fontsize=9)
    figure.tight_layout(rect=(0, 0, 1, 0.94))
    figure.savefig(os.path.join(directory, 'stopping_distance.png'), dpi=FIGURE_DPI)


# @description - Draws the steady pedal force to master pressure and torque curves and the time response of a keyboard press and saves pedal_response.png.
# @param {str} directory - diagnostics directory
# @return {None} - nothing
def plot_pedal_response(directory):
    figure, axes = plt.subplots(1, 3, figsize=(15, 4.8))
    rows = read_rows(os.path.join(directory, 'pedal_response.csv'))
    for case in sorted({row['case'] for row in rows}):
        selected = [row for row in rows if row['case'] == case]
        forces = [row['pedal_force_N'] for row in selected]
        axes[0].plot(forces, [row['master_bar'] for row in selected], label=case)
        axes[1].plot(forces, [row['front_torque_Nm'] for row in selected], label=case + ' front')
        axes[1].plot(forces, [row['rear_torque_Nm'] for row in selected], '--', label=case + ' rear')
    axes[0].set_xlabel('steady pedal force (N)')
    axes[0].set_ylabel('master cylinder pressure (bar)')
    axes[1].set_xlabel('steady pedal force (N)')
    axes[1].set_ylabel('brake torque per wheel (N m)')
    axes[0].legend(fontsize=7)
    axes[1].legend(fontsize=6)
    keyboard = [row for row in read_rows(os.path.join(directory, 'stop_keyboard.csv')) if row['time_s'] <= KEYBOARD_ZOOM_SECONDS]
    times = [row['time_s'] for row in keyboard]
    axes[2].plot(times, [row['pedal_force_N'] / 10.0 for row in keyboard], label='pedal force / 10 (N)')
    axes[2].plot(times, [row['master_bar'] for row in keyboard], label='master pressure (bar)')
    axes[2].plot(times, [row['booster_assist_N'] / 100.0 for row in keyboard], label='booster assist / 100 (N)')
    axes[2].set_xlabel('time after a full key press (s)')
    axes[2].legend(fontsize=7)
    for axis in axes:
        axis.grid(alpha=0.3)
    figure.suptitle('Experimental brakes: pedal response (healthy 70 kPa vacuum, half vacuum, dead booster) and a keyboard full press (foot speed 0.30 m/s plus booster lag)', fontsize=9)
    figure.tight_layout(rect=(0, 0, 1, 0.93))
    figure.savefig(os.path.join(directory, 'pedal_response.png'), dpi=FIGURE_DPI)


# @description - Entry point: renders both plots into the given diagnostics directory.
# @return {int} - process exit code
def main():
    if len(sys.argv) != 2:
        print('usage: plot_brakes.py <diagnostics_brakes_dir>')
        return 2
    plot_stopping(sys.argv[1])
    plot_pedal_response(sys.argv[1])
    return 0


if __name__ == '__main__':
    sys.exit(main())
