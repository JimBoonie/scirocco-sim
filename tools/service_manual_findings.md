# Service manual findings (Taylor's 13 scanned PDFs)
Date 2026-10-13. Read-only review; no sim files were edited.
OCR: tesseract 200 dpi, own run in /workspace/ocr/ (Blender Bot's *.txt in reference/taylor_pdfs/ also used for sections 2-6). Every number below was checked against the 220-300 dpi page image unless marked "OCR only".
Page refs use the manual's page numbers (section-page), plus file/PDF page.

## 1. Identity of the manual
- These are 13 section chapters of a Bentley-format (Robert Bentley) "Scirocco and Cabriolet" service manual. Sections 1-13 are present (Fundamentals, Lube & Maint., Engine, Cooling, Fuel, Exhaust/Emissions, Ignition, Manual Trans & Clutch, Auto Trans, Suspension & Steering, Brakes, Body & Interior, Electrical). There is **no title or cover page** in the set, so the exact title can't be read off a page.
- Coverage taken from the text: 1-18 explains VIN model-year letters from "F" = 1985 up to "P" = 1993. 8-44 has a "Cabriolet, Scirocco 1985-1989", "Scirocco 16V" and "Cabriolet from 1990" table. 10-29 says "Cabriolet from 1990" has an airbag. Many "1985 through 1987" notes. US market only: ASE, ft-lb, psi, US transaxle codes 9A/ACD/AGB/2Y/AUG.
- Conclusion: this is consistent with Bentley "Volkswagen Cabriolet, Scirocco Service Manual 1985-1993" (US models: Scirocco 8V and 16V 1985-89, Cabriolet 1985-93). **It does not cover 1984.** Everything below is unconfirmed for a 1984 EU GTI unless a note says otherwise.

## 2. Extracted specs
### 2.1 Alignment: Table b, p.10-10 (file 07, PDF p.10). Verified on the image. Scirocco and Cabriolet share one column.
| Item | Spec | Tol |
|---|---|---|
| Caster, straight ahead (not adjustable) | +1°50' | ±30' |
| Camber difference, 20° L lock to 20° R lock (caster check) | 1°15' | ±20' |
| Max caster difference between sides | 1° | |
| Front camber, straight ahead | +20' | ±30' |
| Max camber difference between sides | 1° | |
| Front total toe, wheels not pressed | -15' (toe-out) | ±10' |
| Toe angle difference at 20° lock (each side, not pressed) | -1°30' | ±30' |
| Rear camber, each side | -1°15' | ±35' |
| Max rear camber difference between sides | 40' | |
| Rear total toe | +20' | ±30' |
| Max deviation from driving direction (thrust) | 30' (value read as 30', faint) | |
Notes in the text (10-9/10-10): camber is set with the eccentric upper strut-to-knuckle bolt (Fig. 4-4); toe is set on the right tie rod only; rear toe and camber are not adjustable, but the beam brackets allow a small centerline shift.

### 2.2 Steering: section 10.7, pp.10-29 to 10-35
- Fig. 7-1 (10-29): manual rack-and-pinion. The **left tie rod is non-adjustable** and the right is adjustable, with a lock ring on each rack end. That confirms the end take-off rack in proposed_geometry_fix.md.
- Fig. 7-7 (10-33): a replacement adjustable tie rod for manual steering is set to **dimension c = 379 mm (14.921 in)**. The drawing measures from the outer ball-stud center to the inner end of the inner joint housing (the rack-end socket).
- Fig. 7-8 (10-33): with the rack centered, the left inner joint sits at **b = 69.0 mm (2.717 in)**. OCR read "60.0"; the image says 69.0.
- Fig. 7-9 (10-33): right side **b = 67.0 mm (2.638 in)**. The drawing measures b from the rack-housing shoulder to the joint (it appears to be the screw-on depth of the joint on the rack).
- Fig. 7-11 (10-34): power steering tie rod length **362.0 mm**. Not our car.
- Fig. 7-12 (10-35): the rack is centered when protrusion a = b on both ends, measured from the first inside shoulder of the housing. **No numeric value is given.**
- **Not in this manual:** steering ratio, turns lock to lock, lock angles, rack travel, turning circle.
- Torques (10-41): tie rod to rack 50 Nm manual / 70 Nm PAS. Outer tie-rod end 30 Nm. Steering gear to body 30 Nm (OCR "80/22 ft-lb", so 30). U-joint 30 Nm.

### 2.3 Front suspension: pp.10-10 to 10-24
- Fig. 5-1: strut with an integral damper; the whole strut is replaced if the damper is worn, so OE has no cartridge. Parts: spacer, strut bearing, upper retainer, washer, rubber bump stop, splash shield, coil spring. Springs carry a **paint color code**. "Replace in matched pairs" (10-12). **No rates, free lengths or color table.**
- Ball joint freeplay: new about 1.0 mm, limit 2.5 mm (10-21, OCR only).
- Stabilizer bar ends bolt to the control arms (25 Nm). That matches the JSON layout. **No diameter.**
- Torques (10-41): strut top 20 Nm; strut to knuckle 80 Nm (10-12 text says 86 Nm/59 ft-lb, and 59 ft-lb = 80 Nm); arm front pivot 70 Nm, rear 45 Nm (matches JSON); ball joint to arm 25 Nm, clamp 50 Nm; axle nut 240 Nm; lug bolts 110 Nm.

### 2.4 Rear suspension: pp.10-24 to 10-28
- Separate shock absorbers inside the coil springs (coilover); no compressor is needed (Fig. 6-1). The beam acts as an anti-roll bar. There is also a **rear stabilizer bar held by two steel straps** (10-28 "Removing and installing rear stabilizer bar"). **No diameter.**
- Torques: beam bracket to body 45 Nm, pivot bolt 60 Nm, stub axle 60 Nm, shock lower 45 Nm, shock upper 31.5 Nm.
- **No spring or damper data and no ride heights.**

### 2.5 Wheels and tires
- 10-8: wheels have "negative offset" in the text's own definition (rim center inboard of the mounting face, i.e. positive ET). Sizes and pressures are deferred to the door/glovebox sticker. **No tire or wheel size table is in section 2 or 10.**

### 2.6 Brakes: 11-23 Technical Data (file 12, PDF p.23). Verified on the image.
- Front vented disc thickness 20.0 mm new, 18.0 mm minimum, runout 0.06 mm. **No diameter.**
- Rear drum ID **180.00 mm new**, 180.50 mm maximum after resurfacing, 181.00 mm wear limit. Lateral runout 0.2 mm, radial 0.05 mm.
- Rear disc (16V) thickness 10.0 mm new, 8.0 mm minimum.
- Front caliper bolt 25 Nm (35 Nm on 16V); front pad carrier 70 Nm.

### 2.7 Manual transaxle: 8-44 Table II/III (file 06, PDF p.43). Verified on the image.
| | Cab/Scirocco 1985-89 except 16V | Scirocco 16V | Cabriolet from 1990 |
|---|---|---|---|
| Type / code | 020 / 9A, ACD | 020 / AGB, 2Y | 020 / AUG |
| 1st-5th | 3.45 / 2.12 / 1.44 / 1.13 / 0.89 | 3.45 / 2.12 / 1.44 / 1.13 / 0.91 | 3.45 / 1.94 / 1.44 / 1.13 / 0.89 |
| Final drive | 3.67 | 3.67 | 3.67 |
Clutch: lever freeplay 12 mm, pedal freeplay 6 mm, disc runout 0.5 mm, pressure plate taper 0.2 mm. **No disc diameter.** These are US codes; the 1984 EU GTI gearbox and code are **unconfirmed**.

### 2.8 Not found anywhere in the 13 sections
Curb weight, axle loads, wheelbase, track, ride height, spring rates and free lengths, damper data, anti-roll bar diameters, steering ratio, turns, lock angles, rack travel, front disc diameter, clutch diameter, mounting-point dimensions. Section 1 (Fundamentals) has no dimensions or weights table.

## 3. Comparison with suspension_params.json / proposed_geometry_fix.md
### 3.1 S values: agreement and conflicts
| JSON item | JSON value | Manual (10-10 Table b) | Status |
|---|---|---|---|
| caster_deg | 1.833 (±0.5) | +1°50' ±30' | agrees (1985+ US) |
| alignment_front.camber | +20' ±30' | +20' ±30' | agrees |
| toe_total front | -15' ±10' | -15' ±10' | agrees |
| rear static_camber | -1°15' ±25..35' | -1°15' ±35' | agrees; tolerance is ±35' |
| rear toe | +20' | +20' ±30' | agrees |
| toe_out_on_turns_20deg | "outer 18°10' at inner 20°" (A1 manual) | toe difference -1°30' ±30', so **outer 18°30' at inner 20°** (18°00'..19°00') | **CONFLICT with an S value**: A1 18°10' vs Bentley Scirocco 18°30'. Both sit inside the ±30' band. Proposed geometry gives 18.48°, which matches the Scirocco figure almost exactly. Unconfirmed for 1984. |
| tie_rod rod_length 0.3751 (S, TOPRAN listing) and pivot spacing 0.3751 | 0.3751 | replacement rod set to **379 mm** (Fig. 7-7), outer stud center to inner joint end | **SD**: 3.9 mm longer. The figure's end point is the joint housing end, not necessarily the inner ball center, so the true pivot-to-pivot length may be about 0.379 m or a little more. |
| front_arm torques/mounts | 70/45 Nm | 70/45 Nm | agrees |
| rear brakes 180 mm drum | 180 | 180.00 mm new | agrees (confirmed, 11-23) |
| caster cross-check | | camber change 20°L→20°R = 1°15' ±20' | Small-angle check: 2·caster·sin20° = 2·1.833·0.342 = 1.25° = 1°15'. That is consistent with the JSON caster and KPI. It is a new **validation target** for the kinematics solver. |

### 3.2 E/D values the manual can replace
| Item | Old | New | Ref | Confidence |
|---|---|---|---|---|
| components.tie_rod.rod_length_m / tie-rod pivot spacing | 0.3751 (S-listing) | 0.379 (lower bound for joint-center spacing) | 10-33 Fig. 7-7 | S for the 1985+ manual rack; unconfirmed for 1984 |
| alignment_front.toe_out_on_turns_20deg | 18°10' (A1) | 18°30' ±30' (toe difference -1°30') | 10-10 Table b | S (1985+), replaces the A1 value for the Scirocco |
| new: front camber change ±20° lock | none | 1°15' ±20' | 10-10 | validation target |
| new: side-to-side limits | none | caster 1°, front camber 1°, rear camber 40', thrust 30' | 10-10 | S |
| new: inner joint position on rack | none | L 69.0 / R 67.0 mm (dimension b, rack centered) | 10-33 Figs 7-8/7-9 | S. Gives about 2 mm L/R asymmetry of the inner pivots; absolute rack-end y still needs the rack length. |
| steering_arm: left rod non-adjustable | text only | confirmed | 10-29 Fig. 7-1 | S |
| front strut damper type | "cartridge on replacements" | OE integral (strut replaced as a unit) | 10-10 | S |
| rear anti-roll | 19 mm inside beam (S?) | rear stabilizer present (2 straps); **no diameter** | 10-28 | presence only |
**The manual can NOT replace:** spring rates and free lengths, damper curves, bump stops, travel, ARB diameters and rates, all hardpoint coordinates, steering ratio 17.32, turns 3.525, lock 40°22'/32°55', track, weights, unsprung masses, bushing rates. Those stay E/D/S from their current sources.

### 3.3 Effect on the geometry
- **Tie-rod length 0.375 → 0.379 m:** the rod runs almost along y (Δx 0.020, Δz 0.006). Keep the outer point (-0.120, 0.600, 0.362) and move the inner point 3.9 mm inboard along the rod line: tie_rod_inner ≈ (-0.0995, 0.2216, 0.3559). If the real inner ball center is further inboard than the joint housing end, subtract that extra too. The inner point stays on the same line, so the zero-bump-steer property of the proposed fit (inner-point height on the instant axis) is almost unchanged. Expect less than 0.1° at ±50 mm (E). Re-run opt.py to confirm.
- **Toe-out on turns target 18°30':** the proposed fit gives 18.48°, which already matches. The previous A1 target of 18.17° was the outlier, so no steering-arm change is needed. Re-score the optimization against 18.5° ±0.5°.
- **L/R inner-joint asymmetry (69.0 vs 67.0 mm):** the left inner pivot sits about 2 mm further out than the right. Rack travel stays symmetric. A toe-zero offset can be absorbed by the right-rod adjustment, so model both sides at their mean, or offset the left by +1 mm and the right by -1 mm in y (optional).
- **Steering-ratio curve:** the manual gives no ratio, turns or lock data, so 17.32 and 3.525 turns stay as they are. With the 4 mm longer rod, the road-wheel-degrees-per-mm-of-rack curve changes by well under 1%. The proposed "0.480 deg/mm at centre, ±68.6 mm rack to lock" stands (D). The outer-lock check at inner 40.37° is not covered by this manual.
- **Caster/camber check:** the solver should reproduce camber(20°L) - camber(20°R) = 1.25° ±0.33°. The current caster 1.833° and KPI 12.83° predict about 1.25°, which supports the strut-top and LBJ points. It says nothing about the +8.9 mm x shift (trail), because that check depends only on the axis angles.

## 4. 1984 caveat
The manual starts at model year 1985 (VIN "F"), and the Scirocco II was largely unchanged across 1984/85. Still, all values above are **unconfirmed for 1984 and for EU spec**, especially the gear ratios (US 9A/ACD codes), the tie-rod length, and the toe-out on turns.
