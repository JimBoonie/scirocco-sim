# Experimental (viscoelastic) tire compound: research basis and design

Date: 2026-10-13 (PT). Code: `Assets/Scripts/Runtime/ViscoTire.cs`, hooks in `WheelCorner.cs` / `TireThermal.cs`. Fit: `tools/visco_fit.py`.
Smoke / ablation harness: `tools/visco_smoke.py`, `tools/visco_metrics.py`. Plots: `tools/visco_plots.py`.
Tags: **S** sourced, **SD** sources disagree / range, **D** derived, **E** estimated.

The **Default** compound (sourced Pacejka coefficients + `TireThermal`) is unchanged. The Experimental compound is selected in
Quick settings ("Tire compound"); the settings file is v8 (v7 files migrate to Default).

## 1. Literature summary

| Topic | Key result used | Source |
|---|---|---|
| Time-temperature superposition | Viscoelastic response at temperature T and frequency w equals that at T_ref and w a_T; WLF: log10 a_T = -C1 (T-Tg)/(C2 + T - Tg), universal C1g = 17.44, C2g = 51.6 K about Tg | Williams, Landel, Ferry, JACS 77 (1955) 3701 (**S**) |
| Rubber friction master curves | Friction of rubber on rough tracks vs log(a_T v) collapses to one master curve; two peaks: adhesion (molecular length scale, low speed) and hysteresis (roughness wavelength, higher speed); peak velocities scale with the wavelength / relaxation time | Grosch, Proc. R. Soc. A 274 (1963) 21 (**S**) |
| Hysteretic friction theory | mu_visc = 1/2 Int q^3 C(q) P(q) S(q) Int cos(phi) Im E(q v cos phi)/((1-nu^2) sigma0) dphi dq; P(q) = erf(1/(2 sqrt G)), G = 1/8 Int q^3 C Int abs(E/((1-nu^2) sigma0))^2; S = gamma + (1-gamma) P^2, gamma ~0.5 | Persson, J. Chem. Phys. 115 (2001) 3840; Persson, J. Phys.: Cond. Matter 18 (2006) 7789 (**S**) |
| Flash temperature | Frictional heating in the asperity contacts raises the local rubber temperature (tens of K at m/s), shifting the relevant modulus via WLF and making friction fall at high slip speed | Persson 2006; Lorenz & Persson; Fortunato, Ciaravola, Persson et al. Tribol. Lett. (2015) (**S**) |
| Adhesive (real-contact) contribution | mu_adh = tau_f(v) A(v)/(A0 sigma0); tau_f = tau_f0 exp(-c log10^2(v/v0)); tread compound on road: tau_f0 = 6.5 MPa (5.3-8.5 across compounds), c = 0.1, v0 = 6e-3 m/s; q1 cutoff at cumulative rms slope 1.3 | Lorenz, Oh, Persson, J. Chem. Phys. 142 (2015) 194701 (**S**); filled SBR: Lorenz, Persson, Dieluweit, Tada, Eur. Phys. J. E 34 (2011) 129 (**S**) |
| Asperity strain | Relevant rubber strain ~ rms slope / 2 (large: nonlinear, Payne-softened modulus) | Persson 2006; Lorenz 2011 (**S**) |
| Payne effect | Storage modulus of filled rubber drops from E0 to E_inf with strain amplitude; Kraus model E'(eps) = E_inf + (E0 - E_inf)/(1 + (eps/eps_c)^(2m)), m ~0.5-0.6; filler-network (cluster-cluster aggregation) origin | Payne, J. Appl. Polym. Sci. 6 (1962) 57; Kraus, J. Appl. Polym. Sci. Appl. Polym. Symp. 39 (1984) 75; Heinrich & Kluppel, Adv. Polym. Sci. 160 (2002) 1 (**S**) |
| Mullins effect | First-load stress softening of filled rubber; recovers only partly; relevant for a new tire, not a run-in one | Mullins, Rubber Chem. Technol. 42 (1969) 339 (**S**) - **not modeled** (run-in tire assumed, E) |
| DMA indicators | CB-filled E-SBR tread: tan d(0 C, 10 Hz) = 0.206, tan d(60 C, 10 Hz) = 0.160 (E-SBR/BR 70/30: 0.181 / 0.151) | Sae-oui et al., Adv. Mater. Sci. Eng. 2017, 2476101, Table 4 (**S**) |
| Polymer Tg | cold E-SBR, 23.5 % styrene: Tg about -50 C (DSC midpoint -45 C, range -55..-35 C) | AZoM E-SBR article; NETZSCH SBR data (**S / SD**) |
| Rolling resistance and hysteresis | RR of a tread is an energy-controlled loss ~ tan d at the rolling deformation frequency; tan d(60 C) is the industry RR indicator; ISO temperature correction Fr25 = Fr [1 + Kt (t - 25)], Kt = 0.008 /C (passenger) | Schuring, Rubber Chem. Technol. 53 (1980) 600; ISO 28580:2018 sec. 9.2 (**S**) |
| Transient response / relaxation length | sigma_alpha = C_Falpha / c_Fy (cornering stiffness over lateral carcass stiffness), so sigma grows with load like C_Falpha; brush model C_Falpha = 2 c_p a^2, C_Fkappa = 2 c_px a^2 | Pacejka, *Tire and Vehicle Dynamics* 3rd ed. (2012) ch. 3 (brush), ch. 7 (transient, stretched-string) (**S**) |
| Relaxation length measurement | step steer / sine sweep: "you create a slip angle but the grip has some delay" | Rouelle (OptimumG) lecture transcript `transcripts/496pDIeu7Mk.txt` (**S**, qualitative) |

Not found / not available: the Niels Heusinkveld tire-physics transcripts in the queue (`transcripts/STATUS.md`) were not fetched
(YouTube 429); available transcripts are throttle-model videos and the OptimumG tire lectures.

## 2. Design (what is computed, where)

1. **Complex modulus** E*(w) at the Tg reference: Havriliak-Negami polymer relaxation + fractional (constant-loss-angle) filler-network
   element (Heinrich & Kluppel: broad filler spectrum). Five values fitted (**D**, `tools/visco_fit.py`) to the sourced tan d(0 C),
   tan d(60 C) and a tan d peak 0.8 (**SD**) and |E*|(60 C) = 8 MPa (**E**). Result: tan d(0 C) 0.214, tan d(60 C) 0.152, peak at -45 C (10 Hz).
2. **WLF shift** with the universal constants about Tg = -50 C (**S**). Valid above Tg (always, in the sim).
3. **Payne** (Kraus form) on the filler element at the friction strain = XiCut/2 = 0.65 (**D** from Persson). eps_c 1 %, m 0.6 (**SD**).
4. **Persson hysteretic friction** on a self-affine road (H 0.8 **SD**, h_rms 0.25 mm **E**, lambda0 5 mm **E**, q1 from rms slope 1.3 **S**
   = 8.05e4 1/m **D**), nominal pressure = inflation pressure 170 kPa (**D**, membrane approximation already used by TireThermal).
   Precomputed once (lazy, ~0.1 s) on x = log10(a_T v) in 0.05 decade steps from -18 to +4; runtime = table lookup.
5. **Adhesion** (Lorenz/Persson 2015 law, **S** values; reference temperature of that master curve 20 C **E**) times the real-contact
   area P(q1) from the same table.
6. **Flash temperature** of the macro-asperity contacts: Tian & Kennedy (1994) moving circular heat source,
   dT = 2 q a/(k sqrt(pi) sqrt(1.273 + Pe)), q = mu sigma v / contact ratio (0.63, **SD** from TireThermal), a = lambda0/4 (**E**),
   rubber k, rho, c from TireThermal (**S**). Solved by 4 fixed-point iterations with the friction law.
7. **Calibration (no extra grip):** grip factor g = mu_eff(v_s, T)/mu_eff(v_s,ref, T) multiplies the Pacejka peak coefficients
   (TireScale mux, muy) of the slip-based path. v_s,ref = lateral peak slip of the base curve at Fz0 times V_ref 80 km/h (**D/E**).
   At v_s,ref g = 1 at every temperature, so the sourced peak mu, the sourced mu(T) of TireThermal (Alcazar Vargas 2022 /
   Tambellini 2019) and the Pacejka shape stay authoritative; only the deviation of the slip speed from the reference (and the
   flash heating) changes grip. Because MF uses B = K/(C D), the slip stiffness is unchanged by g. The low-speed stick model keeps
   the static limit (rubber at rest is not sliding: the kinetic master curve does not apply).
8. **Rate-dependent stiffness:** tread share 0.5 (**E**) of the tangential compliance follows |E*(f)|, f = V / contact length,
   referenced to V_ref; evaluated at the reference temperature because the temperature independence of slip stiffness is sourced
   (Alcazar Vargas 2022) and overrides the WLF estimate.
9. **Load-dependent relaxation length:** sigma = sigma0 C_Falpha(Fz, speed)/C_Falpha(Fz0) (Pacejka, **S** relation, c_Fy constant **E**).
10. **Rolling resistance:** f = f_Default(v) tan d(f_rev, T_rr)/tan d(f_ISO, 25 C), f_rev = V/(2 pi R), T_rr = tread/carcass mix with the
    TireThermal RR split (0.44 tread, **SD**). = 1 at the ISO condition. Model slope 0.55 %/C vs ISO 28580 Kt 0.80 %/C (**S**): same sign, 70 %.
11. **Hysteresis heat in the bulk:** the tread-deformation part of the friction power, pi tan d V share sum F^2/(4 C) (brush model, **D**;
    pi tan d half-cycle loss **E**), is deposited in the TireThermal bulk node instead of the 1 mm surface (energy unchanged, clamped to
    the friction power). RR heat already went to tread/carcass.

Not modeled: Mullins (run-in tire), wet friction, wear, viscoelastic damping of the low-speed stick model (kept critically damped).

## 3. Validation (2026-10-14, Linux headless players built from origin/main e4004aa and from this change)

EditMode: 95/95 before, 110/110 after (15 new `ViscoTireTests`: grip factor = 1 at the reference speed for 10-120 C; experimental lateral
peak 3300 N vs 3300 N (+0.01 %); slip stiffness unchanged; mu rises then falls with slip speed (88 C: 0.57 at 1 mm/s, max 1.64 at 3.2 m/s,
1.52 at 100 m/s, absolute model values before calibration); WLF moves the peak to higher speed when hot; flash dT 20 K at 1 m/s, 36 K at
3 m/s (mu 1); DMA fit; RR slope 0.55 %/C vs ISO 0.80 %/C; stiffness rises with speed; settings v7 -> v8 migration; cost 17 ms of CPU per
simulated second for 4 wheels at 2 kHz).

Default compound bit-identity: the CSV logs of skidpad, step steer, threshold brake, lift-off (thermal off and on) are byte-identical
between the origin/main build and the new build. The Laguna run differs from the first sample on (spawn drop Fz) because the Laguna scene
and terrain are regenerated by every build (`TrackSetup.Build`, new object ids); lap times (182.5/181.7/180.7 s) and tire temperatures are
identical to 0.1 s / 1 F. (Tooling note: some SciroccoSetup.BuildLinux runs produce a player whose level0 is "corrupted"; rebuilding fixes it.)

Smoke metrics (thermal model on; skidpad R 40 m; step steer 80 km/h 2.5 deg; threshold brake 0.85 pedal from 105 km/h; lift-off at 0.69 g):

| metric | Default | Experimental | effect |
|---|---|---|---|
| skidpad max ay [g] | 0.784 | 0.780 | -0.5 % |
| understeer gradient [deg/g] | 1.84 | 1.94 | +0.10 |
| step steer ay / yaw rate | 0.548 g / 13.83 deg/s | 0.540 g / 13.62 deg/s | -1.5 % |
| step yaw overshoot | 10.5 % | 9.7 % | calmer |
| threshold-brake distance | 45.48 m | 45.59 m | +0.1 m |
| lift-off yaw-rate rise after 1 s / beta rise | 1.12 deg/s / 0.35 deg | 1.09 / 0.35 | same |
| Laguna 3-lap times (scripted, sub-limit driver) | 182.5/181.7/180.7 s | 182.5/181.8/180.7 s | same |
| tread bulk after 3 laps | 84.8 F | 85.5 F | +0.7 F |

Ablation (Experimental with one principle off; the difference to Experimental is that principle's effect):

| principle off | skid ay | US grad | step yaw | overshoot | brake | lift dr 1 s | bulk 3 laps | impact |
|---|---|---|---|---|---|---|---|---|
| none (Experimental) | 0.780 | 1.94 | 13.62 | 9.7 % | 45.59 | 1.09 | 85.5 F | |
| flash temperature | 0.825 | 1.77 | 13.86 | 8.7 % | 45.57 | 0.31 | 85.5 | **high** |
| adhesion (hysteresis only) | 0.737 | 2.26 | 13.40 | 10.7 % | 46.47 | (circle not held) | 86.0 | **high** (carries the master-curve shape) |
| Payne softening | 0.790 | 1.87 | 13.68 | 9.5 % | 45.42 | 0.76 | 85.5 | medium |
| rate-dependent stiffness | 0.784 | 1.86 | 13.82 | 9.5 % | 45.63 | 1.22 | 85.5 | medium (steady-state balance) |
| load-dependent relaxation | 0.780 | 1.94 | 13.62 | 10.7 % | 45.50 | 1.09 | 85.5 | low-medium (transients only) |
| WLF temperature shift | 0.781 | 1.92 | 13.64 | 9.6 % | 45.56 | 1.02 | 85.5 | low (at these temperatures) |
| bulk hysteresis heat | 0.780 | 1.94 | 13.62 | 9.7 % | 45.59 | 1.09 | 84.5 | low (+1 F bulk) |
| rolling resistance (tan d) | 0.780 | 1.94 | 13.62 | 9.7 % | 45.59 | 1.09 | 85.8 | negligible on handling |
| hysteretic friction | 0.780 | 1.94 | 13.62 | 9.7 % | 45.59 | 1.09 | 85.5 | negligible (see below) |

Hysteretic friction is ~0.003 of mu because the asperity strain (0.65) fully Payne-softens the filler network and the low-Tg (-50 C)
E-SBR is deep in its rubbery plateau at road frequencies; with Payne off it is ~0.1-0.15. This is a model result for this compound with
these estimated road parameters, not a general claim: with a higher-Tg compound or a finer road cutoff the hysteretic share grows.
Plots: `visco_mu_vs_slipspeed.png`, `visco_force_curves.png`, `visco_modulus.png` (scirocco_sim/diagnostics/visco/).
