# Brake model sources and estimates (experimental brakes)

Date: 2026-10-15 (PT). Applies to `Assets/Scripts/Runtime/Brakes/*.cs`, enabled by the Experimental tab row "Brake model". Research behind it: `brakes_research.md` (dev tree). Nothing here is read while the toggle is off.

**Tag legend (every value in `BrakeSystemParameters` carries one):** **S** sourced (URL or manual page given) | **SD** sourced but sources disagree (range shown) | **D** derived (method given) | **E** estimate (bounds given, replace as soon as a source or measurement exists) | **U** user-reported (Taylor, Oct 15 2026).
A tag describes the value for this car. A source for a different car (Golf, US 1985-93 Scirocco) is tagged SD or E and the proxy is named. The Bentley manual in hand is for US 1985-93 cars; the 1984 EU GTI is not covered by it.

## What the model is

Chain per 2 kHz substep: pedal (force or rate-limited travel) -> input rod (pedal ratio) -> vacuum booster (two chamber pressures, pneumatic lag, run-out, vacuum supply, repeated-pumping depletion) -> master cylinder pressure (tandem, 20.64 mm) -> two diagonal circuits (A: front left and rear right, B: front right and rear left) -> front line unchanged, rear line through the pressure limiter -> front 239 x 20 mm vented disc with a 48 mm single-piston floating caliper, rear 180 mm drum with 17.46 mm dual-opposed wheel cylinders -> four wheel brake torques. The handbrake is unchanged (the larger of handbrake and hydraulic torque acts on the rear wheels).

Booster to engine: the vacuum chamber drains into the intake manifold through a check valve; the air mass that flows back is added to the manifold (`Engine.AddExternalManifoldMass`, additive, `Engine.Step` untouched). A hard release at idle returns about 0.1 g of air: a small idle bump. The fault interface (`BoosterFault`: extra leak area, blocked fraction of the vacuum supply) is already in the step input, so a leak or a pinched hose can be simulated later without model changes.

Pedal input: with "Load-cell brake pedal" checked, the brake axis is force over the full-scale force the user enters (default **70 lb = 311.4 N, U: user-reported, Heusinkveld Sprint**) and the force goes into the model directly. Otherwise the axis is a travel fraction (keyboard, gamepad trigger, potentiometer pedal): the pedal pad moves at a limited foot speed (below) and the force follows from the car's own force-travel relation (travel -> master pressure -> output force -> input rod force at the present vacuum).

## Foot rate limit (position inputs only, experimental path only)

Apply speed 0.30 m/s and release speed 0.60 m/s of the pedal pad, both **E** (bounds 0.10..0.60 and 0.30..1.2 m/s). Physical basis (SD): in a driver study 96 % of subjects exceeded a maximum instantaneous pedal speed of 100 mm/s in an emergency stop (UNECE GRRF-62-inf11, "Study on the Activation ... of BAS", https://unece.org/DAM/trans/doc/2007/wp29grrf/ECE-TRANS-WP29-GRRF-62-inf11e.pdf); brake-assist triggers are set at 50..250 mm/s (patent US 2005/0046272: 50 and 150 mm/s; VW SSP 264: about 100..250 mm/s depending on speed). The SAE J1808 bench rate (135 N/s) is a static bench rate, not a driver rate, and is not used. A full press travels the pedal pad full travel (D, about 65 mm, below) in about 0.2 s.

Full travel (keyboard 1.0) is derived (D): it is the pedal travel at which the model reproduces the old fixed full-pedal front torque of 1400 N m, so a full key press keeps its old meaning.

## Parameters


### Units

| Parameter (code name) | Default | Unit | Tag | Meaning, source and bounds |
|---|---|---|---|---|
| `pascalsPerBar` | 1.0e5 | Pa/bar | **S** | Pa/bar (S: definition of the bar) |
| `NewtonsPerPoundForce` | 4.4482216152605 | N/lbf | **S** | N/lbf (S: definition of the pound-force) |

### Pedal and foot

| Parameter (code name) | Default | Unit | Tag | Meaning, source and bounds |
|---|---|---|---|---|
| `pedalRatio` | 4.0 | - | **E** | - pedal pad force to booster input rod force (E: bounds 3.5..5.0; measure pivot-to-pad over pivot-to-clevis) |
| `pedalFreePlayMeters` | 0.004 | m | **E** | m pedal pad travel before the rod moves (E: bounds 0.002..0.008) |
| `footApplySpeedMetersPerSecond` | 0.30 | m/s | **E** | m/s pedal pad speed of a hurried foot, position inputs only (E: bounds 0.10..0.60; 96 % of drivers exceed 0.10 m/s in an emergency stop, UNECE GRRF-62-inf11; brake-assist triggers sit at 0.05..0.25 m/s) |
| `footReleaseSpeedMetersPerSecond` | 0.60 | m/s | **E** | m/s pedal pad speed when the foot lifts, position inputs only (E: bounds 0.30..1.2; the foot lifts faster than it presses, the pedal spring helps) |
| `loadCellDefaultMaxForcePounds` | 70.0 | lbf | **U** | lbf default entry for a load-cell pedal's full-scale force (U: user-reported, Heusinkveld Sprint; about 311 N) |

### Master cylinder and hydraulics (per circuit)

| Parameter (code name) | Default | Unit | Tag | Meaning, source and bounds |
|---|---|---|---|---|
| `masterCylinderBoreMeters` | 0.02064 | m | **SD** | m tandem master cylinder bore, 13/16 in (SD: ATE 20.6, Brembo 20.64, Heritage 171 611 019 E; 22.2 on the 16V) |
| `masterCylinderLostTravelMeters` | 0.001 | m | **E** | m travel before the compensation port closes (E: bounds 0.0005..0.002) |
| `hydraulicComplianceCubicMetersPerPascal` | 4.0e-13 | m^3/Pa | **E** | m^3/Pa fluid, hoses, caliper, wheel cylinder and master cylinder together (E: bounds 2.5e-13..6.0e-13) |

### Rear pressure limiter (one per circuit)

| Parameter (code name) | Default | Unit | Tag | Meaning, source and bounds |
|---|---|---|---|---|
| `rearLimiterKneePressurePascals` | 35.0e5 | Pa | **D** | Pa (D: fit to Bentley Table b, ATE "3/35"; bounds 33e5..37e5) |
| `rearLimiterSlopeRatio` | 3.0 | - | **D** | - master pressure increase per rear pressure increase above the knee (D: same fit; bounds 2.5..3.5) |

### Front brake: ventilated disc 239 x 20 mm, single-piston floating caliper

| Parameter (code name) | Default | Unit | Tag | Meaning, source and bounds |
|---|---|---|---|---|
| `frontDiscOuterDiameterMeters` | 0.239 | m | **S** | m (S: 239 x 20 vented, race.fi and vwgolfmk2.co.uk) |
| `frontDiscThicknessMeters` | 0.020 | m | **S** | m new (S: Bentley p. 11-23); the minimum is 0.018 m |
| `frontCaliperPistonDiameterMeters` | 0.048 | m | **S** | m (S: Budweg and Cool Air VW, 48.0 mm) |
| `frontEffectiveFrictionRadiusMeters` | 0.091 | m | **E** | m (E: bounds 0.085..0.097; measure the pad position on the disc) |
| `frontPadFrictionCoefficient` | 0.40 | - | **E** | - cold organic pad on cast iron (E: bounds 0.30..0.50) |
| `frontCaliperEfficiency` | 1.0 | - | **D** | - slide-pin friction loss factor (D: bounds 0.90..1.00) |
| `frontPadRunningClearanceMeters` | 0.2e-3 | m | **E** | m total pad to disc clearance (E: bounds 0.1e-3..0.3e-3) |
| `frontBitePressurePascals` | 0.5e5 | Pa | **E** | Pa pressure below which the pads do not clamp (seal roll-back; E: bounds 0.1e5..1.0e5) |

### Rear brake: self-adjusting drum 180 mm, dual-opposed wheel cylinder

| Parameter (code name) | Default | Unit | Tag | Meaning, source and bounds |
|---|---|---|---|---|
| `rearDrumInsideDiameterMeters` | 0.180 | m | **S** | m (S: Bentley p. 11-23, 180.00 new) |
| `rearWheelCylinderBoreMeters` | 0.01746 | m | **SD** | m 11/16 in (SD: Brembo 17.46, Parts Place 17) |
| `rearBrakeFactorOfShoePair` | 2.4 | - | **E** | - drum brake factor, torque / (wheel cylinder force x drum radius) (E: bounds 1.8..3.0 until the shoe geometry is known) |
| `rearShoeClearanceTravelAtPistonMeters` | 0.4e-3 | m | **E** | m travel of each wheel cylinder piston before the shoe touches (E: bounds 0.2e-3..0.8e-3) |
| `rearBitePressurePascals` | 1.5e5 | Pa | **E** | Pa return spring pressure the wheel cylinder must exceed (E: bounds 0.5e5..3.0e5) |

### Vacuum booster (single, check valve in the vacuum hose)

| Parameter (code name) | Default | Unit | Tag | Meaning, source and bounds |
|---|---|---|---|---|
| `boosterShellDiameterMeters` | 0.203 | m | **SD** | m nominal 8 in shell (SD: 7, 8 and 9 in claimed for the Golf family; no Scirocco II figure; bounds 0.178..0.229) |
| `boosterEffectiveAreaFraction` | 0.70 | - | **E** | - effective diaphragm area / shell frontal area (E: bounds 0.55..0.85) |
| `boosterServoRatio` | 5.0 | - | **E** | - output force / input force on the power slope (E: bounds 3..8) |
| `boosterCutInRodForceNewtons` | 40.0 | N | **E** | N input rod force before any assist (E: bounds 20..80) |
| `boosterJumpInOutputForceNewtons` | 150.0 | N | **E** | N output force at cut-in, the jump-in (E: bounds 50..300) |
| `boosterCutInRampWidthNewtons` | 5.0 | N | **E** | N input force over which the jump-in is smoothed (E: numerical smoothing, bounds 2..20) |
| `boosterValveDischargeCoefficient` | 0.7 | - | **E** | - discharge coefficient of the air, vacuum and check valve orifices (E: bounds 0.5..0.9) |
| `boosterAirValveFlowAreaSquareMeters` | 30.0e-6 | m^2 | **E** | m^2 air valve fully open (E: bounds 10e-6..60e-6; sets the apply time) |
| `boosterVacuumValveFlowAreaSquareMeters` | 15.0e-6 | m^2 | **E** | m^2 vacuum valve fully open (E: bounds 5e-6..40e-6; sets the release time) |
| `boosterValveFullOpenPressureErrorPascals` | 3000.0 | Pa | **E** | Pa working chamber pressure error at which a valve is fully open (E: control gain, bounds 1000..10000) |
| `boosterVacuumChamberVolumeCubicMeters` | 3.0e-3 | m^3 | **E** | m^3 (E: bounds 2.0e-3..4.5e-3) |
| `boosterWorkingChamberVolumeCubicMeters` | 0.5e-3 | m^3 | **E** | m^3 dead plus swept volume, held constant (E: bounds 0.3e-3..0.9e-3) |
| `checkValveAndHoseFlowAreaSquareMeters` | 40.0e-6 | m^2 | **E** | m^2 effective area of the check valve and hose (E: bounds 20e-6..80e-6; sets the recharge time) |
| `boosterLeakFlowAreaSquareMeters` | 0.05e-6 | m^2 | **E** | m^2 healthy leak into the vacuum chamber (E: bounds 0.01e-6..0.2e-6; about 3.5 kPa per 15 s at 70 kPa vacuum) |
| `minimumChamberPressurePascals` | 1000.0 | Pa | **D** | Pa numerical floor of a chamber pressure (D: avoids a zero pressure in the flow law) |

## Derived quantities (D) and the sanity check against the old fixed torques

- Front disc gain 2 x mu x A_piston x R_eff x efficiency = 2 x 0.40 x 1.810e-3 m^2 x 0.091 m = 1.318e-4 N m/Pa = 13.18 N m per bar per wheel.
- Old full pedal front torque 1400 N m needs 106.2 bar plus the bite pressure 0.5 bar (this is the master pressure that a full key press produces).
- Rear at that pressure: limiter 35 bar knee, slope 1/3 gives 58.9 bar, minus the 1.5 bar bite = 57.4 bar; torque = BF 2.4 x (57.4e5 Pa x 2.394e-4 m^2) x 0.09 m = 297 N m versus the old 300 N m (test `EstimatesReproduceTheLegacyFixedTorques`, tolerance 5 %). This is a consistency check, not a calibration: brake factor, radius and mu are still estimates.
- Limiter fit to Bentley Table b (page 11-11): front 55 bar -> rear 41.7 bar (band 39-43); front 100 bar -> rear 56.7 bar (band 51-57). The forum claim "3/25" does not fit Bentley and is rejected.
- Pedal force with the estimates: a 70 lb (311 N) load-cell pedal gives rod force 1245 N, output 2835 N (booster at run-out), 85 bar, front 1120 N m per wheel. A full travel-based press needs about 490 N (110 lb) at the pedal, hard but possible. Without vacuum the same travel needs more than 1.5 times that force.
- Booster run-out at idle vacuum (70 kPa): maximum assist (101325 - 31000) Pa x 0.0227 m^2 = 1.59 kN, reached at an input rod force of about 360 N.

## Known limits and what is not included

- **Not included:** ABS (the car has none; not added), pad and drum temperature (no fade), booster hysteresis, hydraulic line inertia (quasi-static pressure), brake noise, knock-back, per-wheel pressure unbalance (circuit failure can be added through the two circuit objects). Reported as not included, as agreed.
- The working chamber volume is held constant (the swept volume part is folded in).
- The booster reservoir is a real extra volume on the manifold: with the toggle on the intake manifold sees it even without braking (after a throttle lift the chamber drains into the manifold). Toggle off: no effect.
- The pedal travel and the hydraulic compliance matter only for position inputs (a load-cell pedal sets the force directly).
- The default tire stops from 100 km/h in about 39 m (full-car scenario) while period tests show about 46.5 m (70-0 mph in 192 ft and 196 ft); this is the tire, and the tire was not tuned. The old fixed torques lock the front wheels at full pedal; the model with a 70 lb pedal does not (see `diagnostics/brakes/`).

## What to measure to replace the estimates

Pedal ratio and pad travel to bite; booster shell diameter or part number; master cylinder casting mark (20.64 or 22.2); rear limiter marking (3/35 or 3/25) and the 1984 EU valve; rear shoe geometry (brake factor); pad friction coefficient from a deceleration test; hydraulic compliance from a volume-pressure test; disc position of the pads (effective radius); booster apply and release time, jump-in and run-out on a bench (SAE J1808 style).

## Test coverage

`BrakeModelTests` (pedal force to line pressure, dead booster, booster lag and release, run-out and vacuum dependence, static inverse, vacuum depletion by mass mixing, recharge, hard release returning air to the manifold, engine hook, booster faults, rear limiter points from Bentley Table b, sanity check against the fixed torques, drum brake factor, exact zero torque without pedal, diagonal symmetry, foot rate limit, plausible full-pedal force, pedal travel inverse, 70 lb default, robustness, reset, orifice law), `BrakeIntegrationTests` (Off -> On -> Off, unchanged default branch text, defaults off, stopping-distance comparison, pedal-response table), `BrakeDocumentationTests` (every parameter is documented and tagged here), `ExperimentalSettingsTabTests` (new rows).
