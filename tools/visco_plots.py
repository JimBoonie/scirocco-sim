#!/usr/bin/env python3
"""Plots of the Experimental (viscoelastic) compound from the EditMode dump (VISCO_DUMP=<dir> unity test ... ViscoTireTests.DumpCurves).
usage: visco_plots.py <dump_dir> <out_dir>"""
import sys, pandas as pd, matplotlib; matplotlib.use('Agg'); import matplotlib.pyplot as plt
D, O = sys.argv[1], sys.argv[2]
m = pd.read_csv(f'{D}/mu_vs_slipspeed.csv'); F = lambda c: c * 9 / 5 + 32
fig, ax = plt.subplots(1, 2, figsize=(13, 5))
for T, g in m.groupby('T'):
    l, = ax[0].semilogx(g.v, g.mu_flash, label=f'{T:.0f} C ({F(T):.0f} F)'); ax[0].semilogx(g.v, g.mu_noflash, ls=':', c=l.get_color())
ax[0].set_xlabel('sliding speed [m/s]'); ax[0].set_ylabel('mu (absolute model value, before calibration)'); ax[0].set_title('Rubber friction vs slip speed\nsolid: with flash temperature, dotted: isothermal'); ax[0].legend(fontsize=8); ax[0].grid(alpha=.3)
g = m[m['T'] == 88]
ax[1].semilogx(g.v, g.mu_hys, label='hysteretic (Persson)'); ax[1].semilogx(g.v, g.mu_adh, label='adhesive (real contact)'); ax[1].semilogx(g.v, g.mu_noflash, 'k', label='sum (isothermal)'); ax[1].semilogx(g.v, g.dT / 100, 'r--', label='flash dT / 100 K')
ax[1].set_xlabel('sliding speed [m/s]'); ax[1].set_title('Components at 88 C (190 F) tread'); ax[1].legend(fontsize=8); ax[1].grid(alpha=.3)
plt.tight_layout(); plt.savefig(f'{O}/visco_mu_vs_slipspeed.png', dpi=110)
f = pd.read_csv(f'{D}/force_curves.csv'); fig, ax = plt.subplots(1, 3, figsize=(15, 4.5), sharey=True)
for i, (V, g0) in enumerate(f.groupby('V')):
    for T, g in g0.groupby('T'):
        l, = ax[i].plot(g.alpha * 57.2958, g.Fy_default, ls='--', label=f'Default {T:.0f} C'); ax[i].plot(g.alpha * 57.2958, g.Fy_exp, c=l.get_color(), label=f'Experimental {T:.0f} C')
    ax[i].set_title(f'Lateral force, Fz 3000 N, V {V * 3.6:.0f} km/h'); ax[i].set_xlabel('slip angle [deg]'); ax[i].grid(alpha=.3); ax[i].legend(fontsize=8)
ax[0].set_ylabel('Fy [N]'); plt.tight_layout(); plt.savefig(f'{O}/visco_force_curves.png', dpi=110)
e = pd.read_csv(f'{D}/modulus.csv'); fig, ax = plt.subplots(1, 2, figsize=(12, 4.5))
for T, g in e.groupby('T'): ax[0].loglog(g.f, g.absE / 1e6, label=f'{T:.0f} C'); ax[1].semilogx(g.f, g.tand, label=f'{T:.0f} C')
ax[0].set_ylabel('|E*| [MPa]'); ax[1].set_ylabel('tan delta'); [a.set_xlabel('frequency [Hz]') or a.grid(alpha=.3) or a.legend(fontsize=7) for a in ax]
ax[1].plot([10, 10], [0.206, 0.160], 'ko'); ax[1].set_title('dots: sourced tan d(0 C / 60 C, 10 Hz), Sae-oui 2017'); plt.tight_layout(); plt.savefig(f'{O}/visco_modulus.png', dpi=110)
print('ok')
