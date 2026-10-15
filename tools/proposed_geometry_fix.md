# Proposed front steering geometry fix (NOT applied to the live physics)
Date 2026-10-13. Solver: copy of the front loop in kinematics/solve_kinematics.py (same constraints: strut axis through top mount, LBJ on the arm pivot axis, tie rod at fixed length, wheel-center height), in /workspace/scirocco_m1_check/prop_kin.py + opt.py. Hardpoints other than the tie rod come from suspension_params.json as it is now.
Frame: JSON vehicle frame (x fwd, y left, z up, origin on the ground at the front axle). Left side; mirror y for the right side.

## 0. Stale-data audit (after the 22:11 PT Oct 12 rollback)
| Fix | suspension_params.json | tables.json / SuspensionTablesData.cs | CarParams.cs (runtime) |
|---|---|---|---|
| Tie-rod inner z 0.333 | **MISSING**: still 0.42 (E). | Present but only at solve time: the solver re-derives z = 0.3322 internally (design.tie_rod_inner_z_used). frontBumpSteer in the .cs is identical to tables.json. | n/a |
| Strut top + LBJ +8.87 mm in x, so trail is 8.84 mm | Present (x = -0.01353 / 0.00357). Note the tag says "zero caster offset". | Present: caster 1.83 deg, trail 8.84 mm (trail_check.txt) | n/a |
| Lock 1269 deg, rack 17.32 | ratio 17.32 present. **turns_lock_to_lock is still 3.5**, not 3.525/1269 deg. | n/a | Present: handwheelLockDeg 634.7 x 2 = 1269.4 deg, maxSteerAngle 36.645 |
Result: the M1 FBX was built from the JSON, so it carries the stale tie-rod z of 0.42. That is why Blender Bot measures 4.2 deg of bump steer while the sim does not.

## 1. Tie rod / steering arm
Sources:
- [S] 171 419 804 is the tie rod for manual steering, used **left and right**, M12x1.5 / M22x1.5; "for axial joint refer to 171 419 821" (jarparts.com/EN/Part/7788/171419804.html). Heritage Parts: one locknut on the rack on each side (heritagepartscentre.com/uk/171419804...). An axial (inner) joint screwed onto each rack end means an **end take-off rack**. The JSON's "centre take-off, inner y = 0.05" is therefore wrong. That E assumption is what forced the 0.548 m pivot spacing.
- [S] Rod length 0.3751 m (TOPRAN/SKF via wortox). [E] I take it as joint-center to joint-center. The real axial-joint center may add a few cm; adjust the inner y by the same amount if a measurement turns up.
- [S] Lock 40d22' inner / 32d55' outer and toe-out on turns: outer 18d10' when inner is at 20d (A1 manual).
- Bump steer: no sourced number found for the A1/53B. Target "near zero" is **E** (OE design intent).

| Point (left) | Before (JSON, E) | Proposed | Tag |
|---|---|---|---|
| tie_rod_outer (steering arm ball) | (-0.120, 0.590, 0.330) | (-0.120, 0.600, 0.362) | E (fitted) |
| tie_rod_inner (axial joint at rack end) | (-0.140, 0.050, 0.420) | (-0.0997, 0.2255, 0.356) | E (fitted; end take-off is S) |
| pivot-to-pivot | 0.548 | 0.375 | S |
| comment for tie_rod_inner | "rack centre take-off" | "rack end take-off via axial joint 171 419 821" | S |

## Before / after kinematics (left wheel; toe + = toe-in)
| Metric | JSON as is (z 0.42) | Runtime tables (z 0.332, solver-derived) | Proposed |
|---|---|---|---|
| Bump steer at -50 / +50 mm [deg] | -3.33 / +4.25 | +0.54 / +0.51 | 0.00 / 0.00 |
| Bump steer linear slope [deg/m] | 76 | -0.2 (curved) | 0.18 |
| Rack travel for inner 40.37 / outer 32.92 [mm] | -58.3 / +68.8 (asymmetric) | -59.8 / +71.5 (asymmetric) | -68.6 / +68.6 (symmetric) |
| Outer angle at inner 20 deg (S: 18.17) | 17.07 | 16.96 | 18.48 (within the +-30' tolerance) |
| Mechanical trail at center / inner lock / outer lock [mm] | 8.84 / 48.8 / -27.0 | same | same (set by the steering axis, not the tie rod) |
| Road-wheel deg per mm of rack at center | 0.509 | 0.492 | 0.480 |
| Implied turns if 17.32 is the on-center ratio (D) | 3.11 | 3.11 | 3.17 (43.3 mm rack/rev) |
Blender Bot reported -84 / +35 mm of rack travel. I cannot reproduce that from the JSON points (I get -58 / +69), so their rack sign or zero is probably different. The asymmetry is real either way and goes away with the proposal.

## 2. Steering turns (b)
- 17.32:1 and 3.5 turns: A1 workshop manual [S, A2] (suspension_data.md).
- 3.9 turns: carfolio, 1983 US Wolfsburg Edition (aggregator, S-weak; carfolio.com/volkswagen-scirocco-wolfsburg-edition-228448). Forum measurements give 3.3-4.1 (SD).
- Resolution: keep **17.32 [S]** and the sourced lock. Handwheel lock-to-lock = 2 x 36.645 x 17.32 = **1269 deg = 3.525 turns (D)**. Rounded, that is the manual's "3.5" [S], so the two agree. 3.9 turns would mean a mean ratio of 19.2:1, which conflicts with the manual; I treat it as an SD outlier. If 17.32 is read as the on-center ratio instead, this geometry gives 3.17 turns (D). So the manual's figure is best read as an average ratio. Proposed JSON: `"turns_lock_to_lock": [3.525, "D: 2 x mean sourced lock 36.645 x 17.32 = 1269 deg; manual '3.5' (S) = rounded; carfolio 3.9 (US 1983) SD"]`.

## 3. Wheel offset (c)
**ET38 [S]** for the factory 6Jx14 alloys. VW part list: 171 601 125A, 6x14 ET38, standard on Scirocco GTX; 321 601 025H/N and 191 601 025B/F are also 6x14 ET38 (quizzical-wescoff-a5c0f3.netlify.app/vw_original_wheels.htm). wheelfitment.eu also lists ET38 for the Scirocco II. The ET42 figure (tirewheelguide.com) is for generic **5.5J** fitments (13x5.5 / 14x5.5), not the 6J alloy. Change the tag "E/unverified" to S.

## 4. Rear brakes (d)
**180 mm drums [S]**: carfolio 1983 US Wolfsburg (Di/Dr-S, front 239 mm, rear 180 mm). Consistent with report.md. Some late 16V cars had rear discs (not the 1984 8V).

## 5. Spec options (e). Not chosen; for the user to decide.
| | EU 1.8 GTI/GTX (current JSON) | 1984 US Scirocco |
|---|---|---|
| Tires | 185/60 HR14 on 6Jx14 ET38 [S] | 175/70 HR13 on 5.5Jx13 alloy [S, 1984 VW US brochure xr793.com/.../1984-VW-Scirocco.pdf]; Firestone lists 175/70SR13 for the base car |
| Track F/R | 1404 / 1372 mm [S] | 54.7 / 53.5 in = 1389 / 1359 mm [S brochure]; carfolio 1390/1360 |
| Rim offset | ET38 [S] | 13x5.5 ET42 [S-weak, tirewheelguide] |
| Engine | 1.8 112 PS | 1.8 JH 90 hp |
| Lock data | the A1 manual lock (40d22'/32d55') is for 13 in wheels | same |

## 6. Tie rod 0.379 m and Bentley alignment targets (applied 2026-10-13 ~14:30 PT, approved by Taylor)
Source: Bentley "Volkswagen Cabriolet, Scirocco Service Manual 1985-1993" (US models), see service_manual_findings.md. **SD: the manual starts at MY1985 (US); unconfirmed for a 1984 EU car.**
| Item | Before | After | Tag |
|---|---|---|---|
| components.tie_rod.rod_length_m | 0.3751 (S TOPRAN listing) | **0.379** (p.10-33 Fig. 7-7, dimension c) | SD |
| tie_rod_outer (left) | (-0.120, 0.600, 0.362) | unchanged | E |
| tie_rod_inner (left) | (-0.0997, 0.2255, 0.356) | **(-0.09949, 0.22160, 0.35594)**: moved 3.9 mm inboard along the rod line | SD length / E fit |
| toe-out on turns target | outer 18d10' at inner 20d (A1) | **outer 18d30' ±30'** at inner 20d (p.10-10: toe difference -1d30' ±30') | SD |
| new check: camber change 20d L to 20d R | none | **1d15' ±20'** (p.10-10) | SD |
| new check: side-to-side limits | none | caster 1d, front camber 1d, rear camber 40', thrust 30' (p.10-10) | SD |
| L/R inner joint 69.0 / 67.0 mm (p.10-33) | n/a | recorded in the JSON, **not applied** (both sides at the mean) | SD |

Results (solver kinematics/solve_kinematics.py and /workspace/scirocco_m1_check/check379.py, left wheel, D):
| Metric | 0.3751 m rod | 0.379 m rod | Target |
|---|---|---|---|
| Bump steer at -50 / +50 mm [deg] | +0.005 / -0.005 | +0.019 / +0.010 | near zero (E) |
| Max abs bump steer within ±50 mm [deg] | 0.021 | 0.026 | |
| Lock at ±68.6 mm rack, inner / outer [deg] | 40.37 / 32.91 | 40.34 / 32.91 | 40d22' / 32d55' (S [A1]) |
| Rack for exact lock, inner / outer [mm] | -68.62 / +68.62 | -68.65 / +68.61 | symmetric |
| Outer angle at inner 20 deg | 18.48 | 18.48 | 18.5 ±0.5 (SD) |
| Camber change 20d L to 20d R | 1.22 | 1.22 | 1.25 ±0.33 (SD) |
| Road-wheel deg per mm rack at center / on-center ratio | 0.480 / 19.26:1 | 0.480 / 19.26:1 | |
Rack ratio and handwheel lock (0.1081 mm/deg, 634.7 deg) are unchanged: the lock stays within 0.03 deg of before, well inside the test tolerance of 0.1 deg, so no re-fit was needed.
