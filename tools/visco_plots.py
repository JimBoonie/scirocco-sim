#!/usr/bin/python3
# @description - Plots of the Experimental (viscoelastic) compound from the EditMode dump (VISCO_DUMP=<dir> unity test ... ViscoTireTests.DumpCurves).
# usage: visco_plots.py <dump_dir> <out_dir>
import sys
import pandas as pd
import matplotlib
matplotlib.use('Agg')
import matplotlib.pyplot as plt

RADIANS_TO_DEGREES = 57.2958   # deg per rad


# @description - Converts degrees Celsius to degrees Fahrenheit.
# @param {Float} celsius - temperature (degC)
# @return {Float} - temperature (degF)
def to_fahrenheit(celsius):
    return celsius * 9 / 5 + 32


# @description - Plots rubber friction versus sliding speed (per temperature, with and without flash temperature) and its components at 88 degC.
# @param {String} dump_directory - directory holding mu_vs_slipspeed.csv
# @param {String} output_directory - directory for the PNG
# @return {Void} - writes visco_mu_vs_slipspeed.png
def plot_friction(dump_directory, output_directory):
    friction = pd.read_csv(f'{dump_directory}/mu_vs_slipspeed.csv')
    figure, axes = plt.subplots(1, 2, figsize=(13, 5))
    for temperature, group in friction.groupby('temperature_c'):
        line, = axes[0].semilogx(group.sliding_speed_mps, group.friction_with_flash, label=f'{temperature:.0f} C ({to_fahrenheit(temperature):.0f} F)')
        axes[0].semilogx(group.sliding_speed_mps, group.friction_isothermal, ls=':', c=line.get_color())
    axes[0].set_xlabel('sliding speed [m/s]')
    axes[0].set_ylabel('mu (absolute model value, before calibration)')
    axes[0].set_title('Rubber friction vs slip speed\nsolid: with flash temperature, dotted: isothermal')
    axes[0].legend(fontsize=8)
    axes[0].grid(alpha=.3)
    group = friction[friction['temperature_c'] == 88]
    axes[1].semilogx(group.sliding_speed_mps, group.friction_hysteretic, label='hysteretic (Persson)')
    axes[1].semilogx(group.sliding_speed_mps, group.friction_adhesive, label='adhesive (real contact)')
    axes[1].semilogx(group.sliding_speed_mps, group.friction_isothermal, 'k', label='sum (isothermal)')
    axes[1].semilogx(group.sliding_speed_mps, group.flash_temperature_rise_k / 100, 'r--', label='flash dT / 100 K')
    axes[1].set_xlabel('sliding speed [m/s]')
    axes[1].set_title('Components at 88 C (190 F) tread')
    axes[1].legend(fontsize=8)
    axes[1].grid(alpha=.3)
    plt.tight_layout()
    plt.savefig(f'{output_directory}/visco_mu_vs_slipspeed.png', dpi=110)


# @description - Plots lateral force against slip angle (default versus experimental compound) for each speed, at Fz 3000 N.
# @param {String} dump_directory - directory holding force_curves.csv
# @param {String} output_directory - directory for the PNG
# @return {Void} - writes visco_force_curves.png
def plot_force_curves(dump_directory, output_directory):
    forces = pd.read_csv(f'{dump_directory}/force_curves.csv')
    figure, axes = plt.subplots(1, 3, figsize=(15, 4.5), sharey=True)
    for index, (speed, speed_group) in enumerate(forces.groupby('speed_mps')):
        for temperature, group in speed_group.groupby('temperature_c'):
            line, = axes[index].plot(group.slip_angle_rad * RADIANS_TO_DEGREES, group.lateral_force_default_n, ls='--', label=f'Default {temperature:.0f} C')
            axes[index].plot(group.slip_angle_rad * RADIANS_TO_DEGREES, group.lateral_force_experimental_n, c=line.get_color(), label=f'Experimental {temperature:.0f} C')
        axes[index].set_title(f'Lateral force, Fz 3000 N, V {speed * 3.6:.0f} km/h')
        axes[index].set_xlabel('slip angle [deg]')
        axes[index].grid(alpha=.3)
        axes[index].legend(fontsize=8)
    axes[0].set_ylabel('Fy [N]')
    plt.tight_layout()
    plt.savefig(f'{output_directory}/visco_force_curves.png', dpi=110)


# @description - Plots the complex-modulus magnitude (MPa) and loss tangent against frequency for each temperature, with the sourced tan delta targets at 10 Hz.
# @param {String} dump_directory - directory holding modulus.csv
# @param {String} output_directory - directory for the PNG
# @return {Void} - writes visco_modulus.png
def plot_modulus(dump_directory, output_directory):
    modulus = pd.read_csv(f'{dump_directory}/modulus.csv')
    figure, axes = plt.subplots(1, 2, figsize=(12, 4.5))
    for temperature, group in modulus.groupby('temperature_c'):
        axes[0].loglog(group.frequency_hz, group.modulus_magnitude_pa / 1e6, label=f'{temperature:.0f} C')
        axes[1].semilogx(group.frequency_hz, group.loss_tangent, label=f'{temperature:.0f} C')
    axes[0].set_ylabel('|E*| [MPa]')
    axes[1].set_ylabel('tan delta')
    for axis in axes:
        axis.set_xlabel('frequency [Hz]')   # the original one-liner `a.set_xlabel() or a.grid() or a.legend()` short-circuited after set_xlabel, so this figure has never had a grid or legend; kept for identical output
    axes[1].plot([10, 10], [0.206, 0.160], 'ko')
    axes[1].set_title('dots: sourced tan d(0 C / 60 C, 10 Hz), Sae-oui 2017')
    plt.tight_layout()
    plt.savefig(f'{output_directory}/visco_modulus.png', dpi=110)


# @description - Draws the three figures.
# @param {List} arguments - command-line arguments: dump directory, output directory
# @return {Void} - prints "ok" at the end
def main(arguments):
    dump_directory, output_directory = arguments[0], arguments[1]
    plot_friction(dump_directory, output_directory)
    plot_force_curves(dump_directory, output_directory)
    plot_modulus(dump_directory, output_directory)
    print('ok')


if __name__ == '__main__':
    main(sys.argv[1:])
