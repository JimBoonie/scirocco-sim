# Tire thermal and inflation-pressure model: research basis
Scirocco II (1984, EU 1.8 GTI spec) on 185/60R14 summer tyres (estimated MF5.2-style tyre, μ 1.10, relaxation length 0.45 m).
Implementation: `Assets/Scripts/Runtime/TireThermal.cs`. Option "Tire thermal model" in Quick Settings, **off by default**.
Tags (as in suspension_params.json): **S** sourced, **SD** sources disagree, **D** derived from S values by physics, **E** estimated.
Environment is fixed (Taylor): air 66 °F = 18.9 °C, track 70 °F = 21.1 °C. User displays show °F, psi and mph; the physics runs in SI.

## 1. Model family (literature)
- **Pacejka, Tire and Vehicle Dynamics (3rd ed.)**: MF has no thermal state. MF6.1 adds inflation pressure (Besselink, Schmeitz & Pacejka, "An improved Magic Formula/Swift tyre model that can handle inflation pressure changes", VSD 48 (2010) 337–352).
- **TRT** (Farroni, Giordano, Russo, Timpone, "TRT: thermo racing tyre…", Meccanica 49 (2014), doi:10.1007/s11012-013-9821-9): surface, bulk and inner-liner nodes. Heat sources are friction power and SEL. Sinks are road conduction, air convection and inner gas.
- **TRT EVO** (Farroni et al., Proc IMechE D 233(1) 2019:121–135, doi:10.1177/0954407018808992): multi-rib surface, bulk, carcass/liner, sidewall, gas. Radiation is neglected. For racing tyres, grip correlates best with bulk temperature.
- **Kelly / Sharp** (Kelly & Sharp, time-optimal control with a thermal tyre model): lumped 3-node layer models. The structure is the same.
- **Flash temperature** (Persson, J. Phys. Condens. Matter 18 (2006) 7789, "Rubber friction: role of the flash temperature"): on rough surfaces, rubber friction is mainly viscoelastic hysteresis, so its energy is dissipated **inside the rubber** close to the surface. Used for the heat partition in §2.
- **Chosen here:** a TRT-EVO-like lumped model per tyre. Nodes: 3 tread-surface zones (inner/mid/outer), tread bulk, carcass (belts + plies + sidewalls + liner), inflation gas, rim. Integrated at 1 kHz in double precision.

## 2. Heat sources
| Item | Value / law | Tag | Source |
|---|---|---|---|
| Friction power | P = −(Fx·Vsx + Fy·Vsy), with Vsx = vx − ωR and Vsy = vy. Uses the tyre force without RR, after the stick blend. Clamped ≥ 0 | D | standard (TRT "FP") |
| Share of friction heat into the tyre | 1.0 | SD | Persson 2006 (hysteretic friction dissipates in the rubber); TRT puts the full FP into the surface. An interface (Blok) split e_t/(e_t+e_r) = 0.29 would be the alternative. With 0.29 the bench replica (§7) reaches only 43 °C against 100–120 °C measured; with 1.0 it reaches 116 °C |
| Zone split of FP | ∝ zone contact pressure (elastic foundation, deflection d0 + y·tanγ, d0 = Fz/k_z) | D | linear spring-bed approximation |
| Rolling-resistance (hysteresis) heat | P = F_rr·v with the existing f(v)·Fz | D | rr law in WheelCorner (Wong, E) |
| RR heat split, tread : rest | 44 % : 56 % | SD | Tire Review component split (tread 44 %, bead 26 %, other 30 %). Technia/IntechOpen give crown 70 % / sidewall 15 % / bead 15 % |
| Tangential-deformation SEL (Fy²/2k·tanδ) | not modelled | E | needs tanδ of this compound (no source). Estimated ≤10 % of FP at the limit |

## 3. Heat sinks
| Item | Value / law | Tag | Source |
|---|---|---|---|
| Convection to air (tread zones, sidewalls, rim) | Churchill–Bernstein (cylinder in crossflow, D = 0.578 m) combined as (Nu_f³+Nu_n³)^(1/3) with Churchill–Chu natural convection. h = 3.8 / 15 / 34 / 60 W/m²K at 0 / 5 / 15 / 30 m/s | S law, D values | Incropera & DeWitt ch. 7/9. Measured on racing tyres: 17.9–36.7 W/m²K with no translational airflow (S, the same order) |
| Air properties | k 0.0263 W/mK, ν 15.89e-6 m²/s, Pr 0.707 (300 K) | S | Incropera Table A.4 |
| Wheel-house shielding, wind | not modelled | E | |
| Road conduction | two semi-infinite bodies in contact for t_c = L/V; mean h = 2·e_eff/√(π t_c), with e_eff = e_t·e_r/(e_t+e_r). Area = real-contact share × Fz/p | D | Carslaw & Jaeger contact solution |
| Real/nominal contact area | 0.63 | SD | 61–66 % pressure film (Materials 16(18) 2023:6323; also Materials 13(3) 2020:615). Older texture estimates give 5–10 % (ESMC 2006, hal-04398561) |
| Asphalt | k 1.6 W/mK, ρ 2290 kg/m³, c 884 J/kgK | SD | Aston DBM: k 1.33–1.91, c 884, ρ 2290. TRB 1984: k 0.74–2.88, c 879–963 |
| Track temperature | 21.1 °C fixed | S (given) | Taylor |
| Inner convection liner/rim ↔ gas | 25 W/m²K | E | no source. Plausible range 10–100. Checked against the measured highway pressure rise (§7) |
| Bead conduction to rim, brake heat into rim | not modelled | E | brake heat would raise rim and gas temperature in hard track driving, so hot pressure is underestimated there |
| Radiation | neglected | S | TRT EVO practice |

## 4. Material and geometry data (185/60R14, 6Jx14 ET38)
| Item | Value | Tag | Source |
|---|---|---|---|
| Size | width 185 mm, sidewall 111 mm, rim 14 in, R0 = 0.2888 m | S | ETRTO size code |
| Tyre mass | 7.6 kg | S | wheel-size.it (OE 185/60HR14) |
| Composition | compound 78 % (rubber 47 + fillers 21.5 + others), steel 16.5 %, textile 5.5 % | S (steel SD 14–16.5 %) | WRAP 2006 |
| Rubber k, c | 0.282 W/mK, 1741 J/kgK (28 °C) | S | Hot Disk, PMC9182718. Others: k ≈ 0.3 (VT/ATIIM); cp 1880–2260 at 0 °C, so SD with temperature dependence ignored (E) |
| Rubber ρ | 1125 kg/m³ | S | measured range 1094–1157 |
| Steel c / textile c / Al c | 490 / 1700 (nylon 6) / 900 J/kgK | S | handbooks |
| Tread width / depth | 145 mm / 8 mm (new) | E | |
| Surface node thickness | 1 mm | E | model resolution. Penetration depth √(αt) is 0.03 mm per contact, 0.12 mm per revolution and ~1 mm in ~7 s |
| Crown carcass thickness | 5 mm | E | |
| Derived | tread mass 2.34 kg; C_skin 169 J/K per zone; C_bulk 3557 J/K; C_carcass 7580 J/K (c_eff ≈ 1440 J/kgK) | D | |
| Gas volume | 20.2 L (torus with 0.85 rounded-section factor E); gas c_v 718 J/kgK, R 287.05 | D / S | |
| Rim | 6 kg aluminium (E); 6J width; gas-side area 0.17 m², outer area 2× (E) | E | |

## 5. Grip vs temperature (passenger summer tyre)
- **S:** Alcázar Vargas, Pérez et al., *Sensors* 22 (2022), PMC9459800. Austone Athena SP-6 205/65R15 summer tyre, bench, Fz 4000 N, 60 km/h (37 mph), slip angle 8°. μ rose from 0.70 to about 1.05 while the tread heated (some zones reached 120 °C after about 3 min).
  - Fit: **μ(T) = μmax + 1 − cosh((T − Topt)/Tdisp)**, with μmax 1.1, Topt 88 °C, Tdisp 50 °C.
  - In the paper T is the zone temperatures weighted by (T_i − T_amb) (their Eq. 3–4). The model keeps that weighting only as the displayed surface temperature (`SurfaceTemp()`). **Since 2026-10-13 (option (a), approved by Taylor) the grip temperature is the tread bulk temperature**, see §5a.
  - Linear tests at 30/60/90 °C and 1.8/2.3/2.8 bar: cornering stiffness and relaxation length were practically **independent of temperature**. So the model scales **peak μ only**, not stiffness (S).
- **Applied as a ratio** μ(T)/μmax on both μx and μy. The base coefficients therefore represent the tyre at Topt (D), and with a warm tyre the model returns the original handling. Using the same ratio on μx is E.
- **Limits of the fit:** below 44.6 °C the fit falls under the lowest measured μ (0.70) and goes nonsensical (0.02 at 20 °C), so it is held at 0.70 (D/E). Above 120 °C (highest measured) it is held (E).
- **Conflict (SD), important for feel:**
  - The road evidence says unheated summer tyres grip well. Real-road spring tread temperatures are 11–36 °C (Linköping thesis, diva2:1568991). ADAC 2024 summer test (215/55R17): 100–0 km/h (62–0 mph) dry braking in 35.4–40.9 m, i.e. μ ≈ 1.0–1.1 at road temperatures.
  - arXiv 2305.18422 notes a 20–25 % drop cold-vs-hot for a high-performance summer tyre. R888R window 71–105 °C (track tyre). Street tyres on track run 140–170 °F (60–77 °C) (949racing).
  - So a cold tyre in this model has 36 % less peak grip (0.70/1.10). That is the only measured μ(T) curve for a passenger summer tyre found, but it is a bench test on one budget tyre. The road data suggests the real cold penalty is much smaller (≈0–25 %). **This is the largest uncertainty of the feature.**
- **Not used:** WLF/master-curve friction models (no compound data).

## 5a. Grip temperature = tread bulk (change 2026-10-13, option (a))
| Item | Choice | Tag | Basis |
|---|---|---|---|
| Grip temperature | T_grip = T_bulk (tread bulk node, 7 mm under the 1 mm surface node) | S (model family) / E (transfer) | TRT EVO (Farroni et al. 2019) and the TRT/thermoracing literature: grip correlates best with the bulk temperature, which sets the viscoelastic state of the compound. Applying the Alcázar Vargas μ(T) curve (fitted against surface zone temperatures) to bulk temperature on a passenger tyre is E |
| Surface zones I/M/O | unchanged: friction heat in, road conduction / convection out, lateral and bulk conduction; shown in the HUD/logs | S/D | §2–3 |
| μ(T) curve, hold limits | unchanged (μmax 1.1, Topt 88 °C = 190 °F, Tdisp 50 °C = 90 °F; held below 44.6 °C = 112 °F and above 120 °C = 248 °F) | S / D / E | §5 |
| Why | the 1 mm surface node (C_skin 169 J/K per zone) loses its heat to the road and air within ~15 s on a straight; the bulk (C_bulk 3557 J/K ≈ 21× one zone, D) integrates the heat history | D | §7 runs |
| Thermal model off | not affected: TireThermal is not instantiated and the Pacejka multipliers are exactly 1 | D | verified byte-identical, §7 |

Consequence (D): the cold-start penalty now lasts longer than before, because the bulk warms more slowly than the surface: grip stays at the cold hold (64 %) until the bulk passes 112 °F (44.6 °C), and then no longer drops back on every straight. The bench replica now follows the measured μ rise better (μ 0.70 → 0.86 at 2 min → 1.03 at 3 min, measured 0.70 → ~1.05 over ~3 min; before: peak 0.99 at ~30 s, then falling as the surface passed Topt).

## 6. Pressure
| Item | Value / law | Tag | Source |
|---|---|---|---|
| VW cold pressure, 185/60R14 82H | 1.7 bar = 24.7 psi front and rear (default; settable per axle in 0.5 psi steps) | SD | tirewheelguide, avtoreference, wheel-size.it say 1.7. tpressure.com says 1.8 |
| Hot pressure | ideal gas at constant volume: P_abs = P_abs,cold·T_gas/T_cold, set cold at 18.9 °C | S law, E (volume constant) | |
| Measured hot rise | +3.6–5.6 % (10 min), +9.77–12.15 % (37 min) of driving | S | "Experimental Solution of the Influence of Tire Pressure on Vehicle Consumption" |
| MF6.1 pressure terms | dpi = (p − p0)/p0, with p0 = 1.7 bar (E: assumes the estimated coefficients are valid at the VW pressure) | S form | Besselink et al. 2010 |
| Coefficients | PPX1 −0.3485, PPX2 0.37824, PPX3 −0.09603, PPX4 0.06518, PPY1 −0.6255, PPY2 −0.06523, PPY3 −0.16666, PPY4 −0.2811, QSY8 −0.4089 | S for the TNO MF6.1 example tyre (205-width, file comment "225/50R17", NOMPRES 2.0 bar), **E transferred** to our 185/60R14 | TNO example .tir (Modelica ExternData `sample.tir`, which mirrors the Pacejka book Appendix 3). The values match the ones quoted in the literature |
| Applied | Kx·(1+PPX1·dpi+PPX2·dpi²); μx·(1+PPX3·dpi+PPX4·dpi²); Ky·(1+PPY1·dpi), with the load-peak term pKy2·(1+PPY2·dpi); μy·(1+PPY3·dpi+PPY4·dpi²); RR·(p/p0)^QSY8 | S form | MF6.1 Eqs. 4.E15, 4.E23, 4.E25, 4.E70 |
| Rolling-resistance pressure exponent | −0.4 | S | SAE J2452 α = −0.4 (QSY8 of the example: −0.41) |
| Contact patch | A = Fz/p_gauge (membrane approximation) | D | used for the conduction area and patch length |
| PFZ1 (vertical stiffness vs pressure, 0.7098) | not applied: it would change ride height and static deflection | E | |

## 7. Validation targets (published data) and results
Runs use the model alone (EditMode tests, `TireThermalTests.cs`) and the full car (smoke scenarios). See the final report and `dist/README.md`.
### Results (2026-10-13)
| Check | Published | Model |
|---|---|---|
| Bench: 4000 N, 37 mph, 8°, 3 min (Alcázar Vargas 2022) | tread zones up to ~248 °F (120 °C); μ 0.70 → ~1.05 | bulk-based grip (2026-10-13): surface 250 °F (121 °C), bulk 157 °F (69 °C); μ 0.70 → 0.86 (2 min) → 1.03 (3 min). Before (surface-based): peak μ 0.99 at ~30 s, then 0.94 |
| Highway 62 mph, pressure rise | +3.6–5.6 % at 10 min, +9.8–12.2 % at 37 min | +10.4 % at 10 min, +12.5 % at 37 min (warms too fast; h_in E) |
| Ideal gas | P/P0 = T/T0 | exact (1e-12) |
| Energy balance | n/a | residual 5.6e-12 of input over 300 s |
| Sliding 60 s on the 40 m circle, then 62 mph straight | n/a | outer front surface up to ~470 °F (243 °C, 1 mm skin); falls to ~75 °F within ~15 s on the straight; bulk still 98–113 °F after 2 min; hot pressure only +0.8 psi |
| Laguna laps, cold start | n/a | driver at 0.75, 0.6 and 0.45 g leaves the road in the first lap: grip stays ~64 % because the surface stays below the fitted range |

Result of option (a), 2026-10-13 (full car, thermal on; speeds mph, distances m):

| Run | Surface-based (before) | Bulk-based (after) | Thermal off |
|---|---|---|---|
| Cold start: threshold braking from 62 mph | 58.0 m | 74.1 m (front locks 73 %) | 40.8 m |
| Cold start: lift-off in a turn, max Δβ | 168° (spin) | 4.5° (no spin, but the car cannot reach the 0.7 g entry: ay before 0.16 g) | 1.3° |
| Warm start: threshold braking | 57.6 m | 40.8 m | 40.8 m |
| Warm start: skidpad max ay / understeer gradient | 0.66 g / 2.57 °/g | 0.77 g / 1.16 °/g | 0.81 g / 1.06 °/g |
| Warm start: lift-off Δβ | 79° | 1.5° | 1.3° |
| Laguna cold start (0.45 g driver) | leaves the road in lap 1 | leaves the road at s ≈ 474 m in lap 1; bulk only 71–74 °F, grip 64 % | n/a |

A warm tyre now keeps its grip on straights. A cold tyre stays at the 64 % hold longer than before, because the bulk warms slowly (it never reaches the 112 °F fit limit in these short tests). The cold-start problem therefore remains; options (b) (replace the below-range hold with road-test data) is still open for Taylor.

Previous open issue (SD): the surface-temperature grip curve combined with strong road conduction keeps a street-driven tire at the cold-grip hold (64 %). Road tests (ADAC) show about full grip at road temperature. Options for Taylor (2026-10-13: (a) applied):
- (a) drive grip from the bulk temperature (TRT EVO);
- (b) replace the below-range hold with road-test data (μ ≈ 1.0 at 20–35 °C);
- (c) keep as is.
