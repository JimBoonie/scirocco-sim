# Scirocco II (53B) 1984 EU 1.8 GTI/GTX – Suspension Data
Tags: **S** = sourced (URL), **SD** = sources disagree, **D** = derived (method given), **E** = estimate. Machine-readable version: `suspension_params.json` (vehicle frame in meters: origin on the ground at the front axle centerline, x forward, y left, z up; left side shown).

## Sources
- [A1] workshop-manuals.com, Golf Mk1 alignment (Golf/Jetta/Scirocco shared table): https://workshop-manuals.com/volkswagen/golf-mk1/running_gear/wheels_tyres._axle_align/wheels_and_tyres/checking_wheel_alignment/
- [A2] workshop-manuals.com, Golf Mk1 running-gear technical data: https://workshop-manuals.com/volkswagen/golf-mk1/running_gear/technical_data/wheels_tyres/running_gear/
- [G] garage.wiki, 1984 Scirocco chassis specs: https://garage.wiki/volkswagen/scirocco/second-generation/1984/chassis-specifications.html
- [SO] scirocco.org list post with the Mk2 alignment specs: http://www.scirocco.org/list/archives/2001/May/msg01250.html
- [DF] driversfound Scirocco spec sheet: https://driversfound.com/scirocco/history/thescirocco/specs1.html
- [VX1] vwvortex spring press test: https://www.vwvortex.com/threads/suspension-spring-rate-chart-for-mk1-rear-torsion-bars-all-the-same.3181671/
- [CC] Curbside Classic A1 chassis article: https://www.curbsideclassic.com/blog/tech/cc-tech-how-the-nimble-1975-volkswagen-golf-rabbit-and-scirocco-raised-the-standards/
- [EB] Eibach CH, Golf I GTI: https://eibach.ch/en/vw/golf-1-17/18-gti-112-ps-82-83
- [SS] specialstage forum, measured rack turns: https://www.specialstage.com/threads/p-s-or-quicker-steering-on-vw-scirocco.7292/

## Part sharing (A1 platform)
The Scirocco I/II, Golf I, Jetta I and Cabrio share the front subframe, struts, lower arms, ball joints, front bar, rear beam family, rack and alignment table. One VW table covers Golf/Jetta/Scirocco [A1]. Springs differ by model and weight (color-stripe coded); forum advice warns that Cabrio springs are wrong for a Scirocco. The Mk2 Golf (A2) is a different, larger design, so do not take its geometry.

## Values
| Item | Value | Tag | Confidence / note |
|---|---|---|---|
| Front type | MacPherson strut, lower wishbone, front ARB that also helps locate the arm | S [CC][DF] | high |
| Rear type | Twist/torsion beam with trailing arms, coil-over dampers, integrated bar on GTI | S [DF] | high |
| Front camber | +20′ ±30′ (81 kW: ±20′); "sporty" setting +40′ | S [A1][SO][G] | high |
| Front toe (total) | −15′ ±10′ (toe-out, not pressed) ≈ −3 to −0.5 mm; sporty −10′…0′ | S [A1] | high |
| Caster | +1°50′ ±30′ (not adjustable) | S [A1][G] | high |
| KPI (Spreizung) | 12°50′ | S [G]; [CC] says "~12°" | high |
| Toe-out on turns | outer 18°10′ ±30′ when inner is at 20° | S [G] | high |
| Rear camber | −1°15′ ±25…35′ (early chassis −1° ±35′) | S [A1][G] | high |
| Rear toe (total) | +20′ ±20′ toe-in | S [A1] | high |
| Scrub radius | −13.8 (145SR13), −13.2 (175/70SR13), −6.3 mm (175/70HR13) | S [A2] | for 185/60R14 on ET38 I use −10 mm (**D/E**, medium) |
| Steering ratio | 17.32:1 overall, 3.5 turns | S [A2]; **SD**: [CC] 3.3 turns, [SS] measured 3.4–4.1 | high for 17.32 |
| Lock angles | inner 40°22′ / outer 32°55′ | S [A2] (13 in) | medium for 14 in |
| Track | 1404/1372 (EU 14 in, report.md); A1 manual 1387/1358 (13 in) | S / SD by wheel | high |
| Ground clearance | 121 mm at GVW | S [A2] (Golf) | medium |
| Front ARB | 16 mm | SD [DF]: '84 US/Canada 1.8 column only, EU column blank (online_research_findings.md §3) | low-medium |
| Rear bar | 19 mm, inside the beam | SD [DF]: '84 US/Canada column only (online_research_findings.md §3); C/D 0.8 in (SD) | low-medium |
| Front spring rate | ≈17.2 N/mm (98 lb/in, one press test on a 1983 Golf GTI front spring with green/yellow stripes); Rabbit base 13.7 N/mm | S-ish [VX1], single amateur test, **SD** by variant | medium-low. VW never published rates. The Scirocco GTI should be in the 15–19 N/mm range |
| Rear spring rate | 14 N/mm | E | low. No source found; picked to give ~1.4 Hz rear vs ~1.2 Hz front (rear-higher frequency is typical of FWD) |
| Spring free length / wire | not found; front ~370 mm (E) | E | low. ETKA color-stripe tables not accessible publicly |
| Dampers | OE Boge (later Sachs) twin-tube; front is a cartridge in the strut housing [EB] | S (type) | curves **E** (see JSON; rebound ≈ 2× bump, knee ~0.1 m/s). No published force-velocity data found |
| Motion ratios | front spring 0.95, rear 0.92 | D/E | strut nearly on the steering axis; rear coilover just inboard of the hub |
| ARB wheel rates | front ≈4.5 N/mm; rear roll stiffness ≈350 Nm/deg (beam + bar) | D (derivation). Sim baseline since 2026-10-13: front 7.2 N/mm, rear 280 Nm/deg (twistBeamRoll 8520 N/m): **E**, within the estimated range, chosen by Taylor for the understeer balance (not sourced) | method: k_t = G·πd⁴/32/L (G = 79.3 GPa, L_active ≈ 0.9/1.0 m) mapped to the wheel through lever 0.2 m × arm MR 0.55 (front) or the 0.415 m trailing arm (rear); low-medium |
| Travel | front +80/−90 mm, rear +85/−90 mm | E | low |
| Unsprung mass | front 34 kg, rear 27 kg per corner | E | medium (component sum) |
| Wheels | 6J×14, 4×100, ET38 | rim/PCD S; ET is **E** (commonly cited, not verified) | |
| Bushings | rubber; rates in JSON | E | low |

## Derived hardpoints (method)
1. Wheel center: z = loaded radius (185/60R14 free radius 0.289 m − 12 mm deflection); y = track/2.
2. Steering axis: meets the ground at y = track/2 + 10 mm (the outboard offset gives the negative scrub). It then rises at KPI 12.83° inboard and caster 1.83° rearward.
3. Strut top at z = 0.70 m (E, from the A1 strut length and tower height) → (−0.022, 0.553, 0.700). Lower ball joint at z = 0.165 → (−0.005, 0.674, 0.165). Both are **D** given E heights.
4. Lower-arm pivots, tie rods (high A1 steering arm on the strut, center take-off rack), the rear trailing-arm pivot (~415 mm ahead of the axle at z ≈ 0.30) and the damper mounts are all **E**, judged from the layout in published diagrams. Check them against a photo or scale drawing before relying on them.
5. Roll centers: front ≈ 70 mm (instant center from the strut perpendicular and the arm line), rear ≈ 180 mm (twist beam ≈ trailing-arm/pivot line). Both **D**, medium-low.

## Recommended Unity approach (no project files were edited)
- **Offline kinematic solver** (Python or an editor script): solve the MacPherson loop (strut-top spherical joint, strut slider, LBJ on an arm rotating about the pivot axis, tie rod at constant length). Sweep travel from −90 to +80 mm × rack travel. Tabulate camber, toe, caster, track change, roll-center height, spring/damper length → motion ratio. Twist beam: model it as two trailing arms about the pivot axis plus beam twist (shear center) to get camber/toe vs (z_L, z_R). Bake the results into AnimationCurves or 2D LUTs in a ScriptableObject.
- **Runtime:** keep the existing raycast spring-damper per corner. Use the LUTs to apply wheel orientation and to place the lateral force at the roll-center height (jacking). Use piecewise damper curves, progressive bump stops, ARB/beam roll stiffness as k·(zL−zR), and optional bushing compliance steer via lateral force × gain. Run at 500 Hz or faster.
- Tune spring rates and dampers within the stated ranges against the report.md validation targets.

## Gaps
Not found: OE spring rates (only one amateur measurement), rear spring rate, free lengths/wire data, ETKA color codes, damper force-velocity data, published hardpoints, and a confirmed GTI wheel ET. Best next sources: Bentley Scirocco 1974–1981/82 manual (paper), ETKA spring tables, measuring a real car.

---
## Update (follow-up): part dimensions and multibody model
Diagram: `suspension_hardpoints.png` (front view, front side view, rear side view). The JSON now also has `components` (dimensions, masses, bushings, sources) and `multibody_links` (joint list).

### New sourced part data (A1 parts shared by Golf I, Jetta I, Scirocco I/II, Cabrio)
| Part | Data | Tag / source |
|---|---|---|
| Front lower arm kit (Febi 07167; arm 171 407 153 D) | 2.97 kg complete (arm alone 1.98 kg, ball joint 0.29 kg); 17 mm ball-joint cone | S https://www.simplycarparts.co.uk/products/7167 |
| Arm front bush 171 407 182 D | 61 mm long, 12 mm ID, OD 20/32 mm | S (same) |
| Arm rear bush 171 407 181 A | 55 mm long, 16 mm ID, 38 mm OD; pressed onto the rear pin and held in a body bracket with 2 bolts. On GTI the bracket nut also holds the ARB bracket | S (same) + https://www.vwmanual.ru/en/golf/1/chassis/fsuspension/rychagi-peredney-podveski-i-vtulki |
| Arm mounting | Front pivot bolt into the subframe (7.0 kg·m), rear bracket bolts (4.5 kg·m) | S (vwmanual.ru) |
| Front strut | Piston runs directly in the strut tube; knuckle clamped with 2 bolts, the upper one eccentric for camber; hollow rubber bump stop under the top mount; springs in VW weight "load groups"; ball joint holds knuckle to arm | S https://workshop-manuals.com/volkswagen/golf-mk1/running_gear/technical_data/general_view_of_front_axle/ |
| Front strut housing (Bilstein B4 22-239594) | 3.78 kg | S https://www.viamoto.co.uk (Bilstein B4 22-239594 listing) |
| Front insert (Bilstein B6 34-001370) | about 565 mm overall | S-weak (listing dimension, probably the package size) |
| Tie rod 171 419 804 (manual steering; only the right one is adjustable) | Rod 375.1 mm, threads M12×1.5 / M22×1.5; the outer end (171 419 811) and inner joint (171 419 821) are separate parts | S https://wortox.com/en/oen/171419804/68388/tie-rod-end, http://www.jarparts.com/EN/Part/7788/171419804.html |
| Rear damper (171 513 033 family) | Extended 533 / 535 / 543 mm, compressed 331 / 324 / 330 mm (TRW / Stark / Monroe); 12 mm rod, 39 mm body, M10×1 top pin, bottom eye, about 1.7–1.83 kg | **SD** (small spread) https://www.rexbo.co.uk/trw/shock-absorber-jhs101t, https://www.autodoc.parts/monroe/7437416 |

### Derived lengths (from the hardpoints; their accuracy follows the E inputs)
- Lower arm pivot spacing 0.290 m; ball joint to front pivot 0.329 m; ball joint to rear pivot 0.400 m (D).
- Strut top mount to lower ball joint 0.549 m (D). This fits a roughly 565 mm insert plus the knuckle offset.
- Tie rod pivot-to-pivot 0.548 m (D). This is longer than the 375 mm rod because the outer end and the inner joint add length. The inner pivot is placed at y = 0.05 m because the A1 manual rack takes the tie rods off its center, so treat this as **SD/E**.
- Rear damper static length 0.445 m = compressed + 55 % of the ~207 mm stroke (D). I moved the damper top to z = 0.675 m to match.
- Trailing arm, pivot to wheel center: 0.425 m (E).

### Still not found
- Published lower-arm, subframe or knuckle drawings with coordinates.
- VW body dimension charts (the Scirocco 53 body-repair manual exists as a paid kfz-verlag e-book: https://www.kfz-verlag.de/scirocco-1974-1992-general-information-body-repairs-workshop-manual/p-21326.html).
- Front strut extended/compressed lengths, spring seat heights, rear beam section, engine mount positions, and CAD files.
That body-repair e-book is the single best purchase for real hardpoints. Measuring a car is the other option.

### Multibody recommendation for Unity
Use ArticulationBodies, or better a custom constraint solver at 1–2 kHz. Recreate each joint from `multibody_links`:
- **Front:** lower arm with the front bush modeled as a stiff revolute joint and the rear bush as a compliant spherical one; knuckle and strut tube as one rigid body; strut rod as a slider carrying the spring, damper and bump stop; top mount as a compliant joint; tie rod as a spherical–spherical link from the rack.
- **Rear:** each trailing arm on a compliant revolute bush, with the two arms coupled by a torsion spring for the beam and bar.
- **Masses:** use the masses in `components`.
- **Validate:** check that camber, toe and roll center against travel match the kinematic lookup tables. Then read the joint reaction forces to see the force path into the body.

## Sourced-vs-estimate audit (2026-10-12)
| Item | Conflict | Resolution |
|---|---|---|
| Caster trail | Estimated strut top/LBJ x gave a steering axis 8.9 mm behind the wheel center at hub height, so mechanical trail was ~0 despite sourced caster 1d50' | Both points shifted +8.87 mm in x (D: zero caster offset). KPI 12.84 / caster 1.83 / scrub -10.0 mm unchanged; trail now 8.84 mm = 0.2768 x tan(1.83 deg) |
| Steering lock | 630/17.32 = 36.37 deg nominal gave 40.04/32.70 deg, short of the sourced 40d22'/32d55' [A1] | maxSteerAngle = 36.645 deg (mean of sourced lock); handwheel lock = 36.645 x 17.32 = 634.7 deg (3.53 turns; "3.5" treated as rounded). Default rotation 1269 deg |
| Checked, no conflict | KPI, caster, scrub, tracks 1.404/1.372, wheelbase 2.400, rear wheel center x, alignment F/R camber + toe, toe-out on turns (fit), mass 920 + 75 driver, gear ratios 4Y + final 3.667, peak power 112 PS, Cd 0.38, front ARB 16 mm (baseline), tire free radius 0.2888 | none |
| Open (sources disagree, documented, not changed) | rear bar 19 mm vs C/D 0.8 in; peak torque 153 vs 155-157 Nm; scrub -6.3..-13.8 mm; turns 3.3-4.1 | kept the primary-source value |

## Steering linkage in the runtime (2026-10-13, approved by Taylor)
| Item | Value | Tag | Basis |
|---|---|---|---|
| Pinion | constant: rack 0.1081 mm per handwheel degree; ±68.6 mm at the 634.7° stop | D | symmetric rack travel that gives the sourced lock 40°22′/32°55′ (S [A1]) with the fitted end-take-off tie rods (proposed_geometry_fix.md) |
| Per-wheel road angle | `SuspensionTables.frontToeTravelRack`: left-wheel angle on a grid of wheel travel (−90…+80 mm, 10 mm) × rack (±70 mm, 2 mm), from `kinematics/solve_kinematics.py`; right wheel mirrored; bilinear interpolation | D | tie_rod_inner/outer of suspension_params.json (outer E fitted; inner on the rod line at 0.379 m, SD Bentley 1985-93 p.10-33) |
| Bump steer | comes from the same grid (the rack = 0 column is the old bump-steer curve; it now also varies with rack travel) | D | |
| Results (rod 0.379 m, 2026-10-13 14:30 PT) | full lock 40.35° inner / 32.92° outer (solver exact 40.34 / 32.91; was 40.37 / 32.92 with the 0.3751 m rod); 100° handwheel → 5.25° / 5.15°, mean 5.20°; on-center overall ratio 19.26:1 | D | EditMode tests `SteeringLinkageTests` |
| Toe-out on turns | outer 18.48° at inner 20° | D vs SD target 18°30′ ±30′ (Bentley p.10-10; was A1 18°10′) | `Toe_Out_On_Turns_Matches_Manual` |
| Camber change 20° L → 20° R | 1.22° | D vs SD target 1°15′ ±20′ (Bentley p.10-10) | `Camber_Change_Across_20deg_Lock_Matches_Manual` |
| Side-to-side limits | model is mirrored L/R (69.0/67.0 mm inner-joint offset not applied), so L/R differences are 0 | SD limits: caster 1°, camber 1°, rear camber 40′, thrust 30′ | `Side_To_Side_Within_Manual_Limits` |
| Bump steer ±50 mm | +0.019 / +0.010° | D | |
| Removed from the runtime | fixed ratio 17.32:1 (S [A2]) and the Ackermann fit g(d)=A·d+B·d² | | the linkage replaces them; the old 17.32 figure is an on-center catalog ratio, the geometry gives 19.26 on center (SD, open) |
| Column torque lever | d(wheel angle)/d(handwheel) from the grid at the current rack and wheel travel | D | `SteeringLinkage.Lever` |
| FFB end stop / UI / keyboard rate | use the on-center linkage ratio (D) where a single ratio is needed; FFB and the cockpit wheel run on handwheel degrees | D | |
| Test driver / keyboard | command a mean road angle; the linkage is inverted at design ride height (bisection) | D | |
