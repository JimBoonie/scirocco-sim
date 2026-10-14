#!/usr/bin/env python3
"""Fit of the complex-modulus model of the Experimental (viscoelastic) compound (ViscoTire.cs). Run: python3 tools/visco_fit.py
Model (reference temperature Tg, WLF shift):  E*(w) = Eg + (E0 - Eg)/(1 + (i w tauG)^a)^b   (polymer, Havriliak-Negami)
                                             + 1.5 E0 (i w / w_ref)^n                      (filler network, fractional element)
Targets (S): CB-filled E-SBR tread, DMA 10 Hz: tan d(0 C)=0.206, tan d(60 C)=0.160 (Sae-oui et al. 2017, Table 4);
tan d peak 0.8 (SD: filled SBR 0.6-1.0); |E*|(60 C,10 Hz)=8 MPa (E: filled tread 5-10 MPa).
Fixed: Tg=-50 C (S, cold E-SBR 23.5 % styrene), WLF C1g=17.44, C2g=51.6 K (S, WLF 1955 universal), Eg=2 GPa (SD 1-3 GPa),
filler/polymer modulus ratio 1.5 (SD: Payne ratio E0/Einf ~2.5 for ~50-80 phr carbon black)."""
import numpy as np; from scipy.optimize import least_squares
Tg, C1, C2, Eg, RATIO = -50.0, 17.44, 51.6, 2e9, 1.5
def log_aT(T): return -C1 * (T - Tg) / (C2 + T - Tg)
wref = 2 * np.pi * 10 * 10 ** log_aT(60)
def E(w, E0, a, b, n, tau): return Eg + (E0 - Eg) / (1 + (1j * w * tau) ** a) ** b + RATIO * E0 * (1j * w / wref) ** n
def td(T, x, a):
    E0, b, n, lt = x; e = E(2 * np.pi * 10 * 10 ** log_aT(T), E0, a, b, n, 10 ** lt); return e.imag / e.real, abs(e)
Ts = np.linspace(-80, 40, 241)
best = None
for a in (0.4, 0.5, 0.6, 0.7, 0.8):
    def res(x):
        t0, _ = td(0, x, a); t60, m60 = td(60, x, a); tm = max(td(T, x, a)[0] for T in Ts)
        return [(t0 - 0.206) / 0.206, (t60 - 0.160) / 0.16, (m60 - 8e6) / 8e6, (tm - 0.8) / 0.8]
    r = least_squares(res, [3e6, 0.3, 0.1, 2], bounds=([1e5, 0.02, 0.0, -4], [2e7, 1, 0.5, 6]))
    print(f"a={a}: E0={r.x[0]:.6e} b={r.x[1]:.6f} n={r.x[2]:.6f} log10 tauG={r.x[3]:.6f} cost={r.cost:.2e}")
    if best is None or r.cost < best[0]: best = (r.cost, a, r.x)
print("best", best)
