#!/usr/bin/python3
# @description - Renders the shift-success window (clutch pedal position versus engine/input-shaft speed mismatch) from the CSV files the integration test writes. usage: plot_shift_window.py <diagnostics_dir>
import csv
import glob
import os
import sys

import matplotlib
matplotlib.use('Agg')
import matplotlib.pyplot as plt


# @description - Reads one window CSV into sorted axes and a success grid.
# @param {str} path - CSV file path (columns clutch_pedal, rev_mismatch_percent, success, time_s, grinds, throttle)
# @return {tuple} - (pedals, mismatches, grid[row mismatch][column pedal] = 1 success / 0 failure, throttle)
def read_window(path):
    rows = list(csv.DictReader(open(path)))
    pedals = sorted({float(r['clutch_pedal']) for r in rows})
    mismatches = sorted({float(r['rev_mismatch_percent']) for r in rows})
    grid = [[0] * len(pedals) for _ in mismatches]
    for r in rows:
        grid[mismatches.index(float(r['rev_mismatch_percent']))][pedals.index(float(r['clutch_pedal']))] = int(r['success'])
    return pedals, mismatches, grid, float(rows[0]['throttle'])


# @description - Draws all window CSVs of a directory side by side and saves shift_window.png there.
# @param {str} directory - diagnostics directory holding shift_window_throttle_*.csv
# @return {None}
def main(directory):
    files = sorted(glob.glob(os.path.join(directory, 'shift_window_throttle_*.csv')))
    figure, axes = plt.subplots(1, len(files), figsize=(6.5 * len(files), 5), squeeze=False)
    for axis, path in zip(axes[0], files):
        pedals, mismatches, grid, throttle = read_window(path)
        axis.pcolormesh(pedals, mismatches, grid, cmap='RdYlGn', vmin=0, vmax=1, shading='nearest')
        axis.set_xlabel('clutch pedal position (0 = engaged, 1 = fully pressed)')
        axis.set_ylabel('engine speed minus target input-shaft speed (% of target)')
        axis.set_title('Shift success, 2nd to 3rd, throttle %.2f (green = gear in, red = grind or refusal)' % throttle, fontsize=9)
    figure.tight_layout()
    figure.savefig(os.path.join(directory, 'shift_window.png'), dpi=130)


if __name__ == '__main__':
    main(sys.argv[1])
