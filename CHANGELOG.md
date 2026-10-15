# Changelog

All notable changes to this project are documented in this file.
The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/). The project has no version numbers, so changes are grouped by date. Dates and times are Pacific Time (PT).

**Every future commit must update this file** (add its entry under `[Unreleased]` or under the current date).

## [Unreleased]

### Changed (US spelling pass, part 1: Unity C# and serialized fields, PT 2026-10-15 00:50)

- All C# identifiers, comments and UI strings use American spelling (center, tire, color, curb, meter, behavior, normalized, regularization, initialized, ...). Old -> new names are listed in `docs/RENAMES.md` ("US spelling pass"). Includes the scenario `lagunakerb` -> `lagunacurb` and `SurfaceType.Kerb` -> `Curb`, the Laguna Seca centerline field `centrelineCsv` -> `centerlineCsv`, and the serialized field keys in `SciroccoParams.asset`, `SuspensionTables.asset`, `TestTrack.unity` and `LagunaSeca.unity` (values kept; only the keys were renamed).
- Kept as they are: Blender object/material/texture names (`COL_Kerbs`, `M_Kerb`, `T_Kerb_RedWhite`, `Track_Kerbs`), the file name `centreline.csv`, and Unity API names (`MonoBehaviour`, `Mathf.MoveTowards`).
- No behavior change: 118+ edit-mode tests pass, the 15 scenarios are row-identical, skidpad understeer 1.834 deg/g, peak lateral acceleration 0.785 g.

### Added (second Experimental placeholder, PT 2026-10-15 00:10)

- Experimental tab: a second disabled placeholder toggle, "Transmission and synchronizer model (coming soon)", with a short explanation, wired to `BindingProfile.experimentalTransmissionEnabled` and `CarParams.experimentalTransmissionEnabled` (default false; nothing reads them yet). It is independent of the driveline shuffle toggle (separate fields, separate row). The new JSON field defaults to false when absent, so the settings version stays 10 and bindings are not reset. Test `DrivelineAndTransmissionPlaceholdersAreIndependent` added.

## [2026-10-14] (PT)

### Added (Experimental settings tab, PT 2026-10-14 23:50)

- New **Experimental** tab (last tab) in the in-game settings menu (`ExperimentalSettingsTab.cs`, drawn with IMGUI so the VR cursor menu works with clicks and slider drags). It opens with a note that experimental options may change handling, and each option has a short plain-language description under it.
- Moved here from the **Quick** tab, each now in exactly one place: the tire temperature (thermal) model toggle, the front and rear cold tire pressure settings (sliders, 15..45 psi, 0.5 psi steps, "VW 24.7" reset), and the tire compound selector (Default / Experimental viscoelastic). Defaults, step sizes and behavior are unchanged. The Quick list is four lines shorter (the later Quick items renumbered 16..20; no external code used those numbers).
- New placeholder: a disabled toggle "Driveline shuffle model (coming soon)" wired to `BindingProfile.experimentalDrivelineEnabled` and `CarParams.experimentalDrivelineEnabled` (default false; nothing reads it yet).
- Settings file: storage of the existing fields is unchanged and the new field defaults to false when absent, so the settings version stays at 10 and bindings are not reset.
- Tests: `ExperimentalSettingsTabTests` (defaults reproduce previous behavior, adjust/navigation, JSON round trip incl. a pre-tab file, VR cursor click and slider drag).

### Changed (second pass part 5: tools, scripts and data-file keys, PT 2026-10-14 22:45)

- `tools/kinematics/solve_kinematics.py`, `export_unity.py` and the new `params_io.py` (its helper) are now byte-identical to the dev-box copies. They have the `#!/usr/bin/python3` shebang, a `# @description` / `@param` / `@return` header on every function, full-word names, and physics comments with units and S/SD/D/E tags. `export_unity.py` writes the renamed `SuspensionTables` field names (output byte-identical to `Assets/Scripts/Editor/SuspensionTablesData.cs`). `solve_kinematics.py` can now be imported without running the sweep.
- Data-file keys renamed end to end (old -> new in `docs/RENAMES.md`): `tables.json` (roll-center keys), `compliance.json` (load cases, joints, kingpin geometry, coefficients), `steer_ref.json` (`geometry`/`cases` with full-word keys). `SteeringTorqueTests` reads the new `steer_ref.json` keys; the values are bit-identical to the old file.
- `tools/visco_fit.py`, `visco_metrics.py`, `visco_plots.py`, `visco_smoke.py`: full-word names, per-function headers, shebang. Smoke CSV columns are now `time_s`, `speed_kph`, `lateral_g`, ... (full list in `docs/RENAMES.md`); the `ViscoTireTests.DumpCurves` CSV headers (`mu_vs_slipspeed.csv`, `force_curves.csv`, `modulus.csv`) were renamed to full words (header strings only) and `visco_plots.py` reads them.
- `tools/sync_tools.sh` rewritten so it can safely sync again: dry run by default; `--apply` copies `suspension_params.json`, the kinematics scripts, `visco_*.py` and the research notes, but refuses any file that still contains an old identifier or lacks the python shebang/header. Exit code 2 when something was refused.
- `tools/tire_thermal_research.md`: code names updated (`MuRoad`/`MuBench`/`MuOfT`/`TCross` -> `RoadFriction`/`BenchFriction`/`FrictionAtTemperature`/`CrossoverTemperature`).
- No behavior change: all 113 tests pass; skidpad understeer 1.834 deg/g and peak lateral acceleration 0.785 g; the 15 scenarios are row-identical.

### Changed (second pass part 4: editor scripts, tests, runtime leftovers, physics comments, PT 2026-10-14 21:35)

- Editor scripts (LfsPointerCheck, XrSetup, SciroccoSetup, TrackSetup, SuspensionTablesData) and the `export_unity.py` template: hand-written headers, full-word locals, lambda parameters and loop variables (`MultiTC` -> `MultiTerrainRaycaster`, `C0/R0` -> `InnerColumnStart/InnerRowStart`).
- All 15 test files: hand-written test and helper descriptions (what is checked, units, tolerances, why), real parameter texts, full-word locals; helper renames (for example `Db` -> `MeanPowerDecibels`, `Goertzel` -> `GoertzelAmplitude`, `P()` -> `CreateCarParams()`). JSON field names of `steer_ref.json` stay as in the Python reference.
- Runtime leftovers renamed (Engine, FfbController, ViscoTire, TireScrub, SteeringTorque, SuspensionTables, CarAudio, CarController, CarParams, HidReportFilter, VrManager, VrPanel, SmokeTest, InputRouter `vr` -> `vrManager`, `st` -> `labelStyle`).
- `in`/`ref`/`out` removed from the `@param` types in WheelCorner.
- Physics comments with equation, units and source tags added for the throttle area, orifice flow, residual-gas and backflow terms (Engine), the magic formula and combined-slip weights (PacejkaTire), the viscoelastic modulus, roughness spectrum, contact area and flash temperature (ViscoTire), the convection correlations and road conduction (TireThermal), and slip speed, slip angle and the friction ellipse (WheelCorner).
- `tools/visco_smoke.py`: the BASE configurations now translate `carParams.` override keys to the legacy `p.` prefix.
- InputBindings: header of `IsMenuAction`/`IsGameplay` rewritten, stale header above `DefaultPath` replaced.
- No behavior change (comments, names, headers only).

### Changed (second pass part 3: input, VR, audio, HUD, track, SmokeTest headers and locals, PT 2026-10-14 21:50)

- Hand-written headers (what, units, why) and full-word locals/parameters/loop variables for InputBindings, InputRouter, CarAudio, VrManager, VrPanel, DebugHUD, TrackManager and SmokeTest.
- Renames: `GamepadSteering.s` -> `settings` (tests and SmokeTest updated), `VrManager.I` -> `Instance`, `UiSurface.W/H` -> `LogicalWidth/LogicalHeight`, `InputMapper.E()` -> `GetEdgeState()`, `DebugHUD.F()` -> `ToFahrenheit()`, `DebugHUD.FJ/RJ` -> `FrontJointNames/RearJointNames`, SmokeTest `sc/rb/t/R/U/K1` -> `scenario/body/elapsedTime/skidpadRadius/FrictionUsage/curvatureAtOneSecond`, `tm_FallResets` -> `FallResetCount`, `TrackManager.cs` `cs` -> `creditStyle`.
- Multi-function one-liners split (`VrManager.OnEnable/OnDisable`, `SmokeTest.ContactDiag`).
- No behavior change: 113/113 tests, 15 scenarios row-identical, understeer 1.834 deg/g, peak 0.785 g.

### Changed (second pass part 2: TireScrub hand-written headers, PT 2026-10-14)

- TireScrub.cs: every function now has a real description with units for each parameter and return value; locals, parameters and loop variables renamed to full words (`frameIndex`, `tireIndex`, `centerHz`, `qualityFactor`, ...). No behavior change: 113/113 tests, 15 scenarios row-identical, 0.785 g.

### Changed (second pass part 1: overrides keys, retired enum slot, sync_tools, hand-written headers, PT 2026-10-14)

- `-overrides` keys now use `carParams.<field>` (was `p.<field>`); `tools/visco_smoke.py` and docs updated. Note: `visco_smoke.py` BASE builds from before this change still expect the old `p.` keys.
- Removed the retired `InAction.RetiredProfileToggle` enum slot and its skip logic; `BindingProfile.CurrentVersion` is now 10. Old settings files are discarded and defaults are used (breaking, no migration).
- `tools/sync_tools.sh` is now a dry run unless `--apply` is passed, copies only `suspension_params.json`, and no longer overwrites the renamed `export_unity.py` / `solve_kinematics.py` / notes (reports them as guarded). The dev-box copies still use the old names and must be updated before syncing back.
- Hand-written header descriptions with units, and full-word locals, for Cockpit, FollowCam, FfbController, GamepadSteering, HidReportFilter, AmbientLighting, SurfaceTag, TrackInfo. FfbController fields renamed (`clipHistory`, `clippingPercent`, ...), DebugHUD updated.
- No behavior change: tests, all 15 scenarios row-identical, skidpad 1.834 deg/g, 0.785 g.

### Changed (readability pass 6/6 part 3: audio, SmokeTest, track, editor scripts, tests, PT 22:30)

- Every remaining method in `Assets/Scripts` and `Assets/Tests` now has an `// @description / @param / @return` header (audio: `CarAudio`, `TireScrub`; `SmokeTest`, `TrackManager`, `TrackInfo`, `SurfaceTag`, `AmbientLighting`, `CarParams`, editor scripts, all test files). `/// @description` headers from earlier passes were normalized to `//`. `SuspensionTablesData.cs` header is mirrored in `tools/kinematics/export_unity.py`. No behavior change.

### Changed (readability pass 6/6 part 2: input, settings, UI/VR, PT 21:55)

- **Old saved settings are broken on purpose:** `input_bindings.json` settings version is now 9 and files of any other version are discarded (defaults used, a warning is logged). JSON keys were renamed (see `docs/RENAMES.md`). The settings migration code and its 5 migration-only tests were removed and 1 test added (edit-mode tests: 113/113 pass; the 112 retained tests are unchanged).
- Headers (`@description/@param/@return`) on all methods of `InputBindings`, `InputRouter`, `GamepadSteering`, `HidReportFilter`, `VrManager`, `VrPanel`, `Cockpit`, `FfbController`, `DebugHUD`, `FollowCam` and their tests; cryptic parameters renamed. No simulation change.

### Changed (readability pass 6/6 part 1: parameter, table and scene field names, PT 21:20)

- **Old scenes data, saved assets and `-overrides` files from before this change no longer match: serialized field names were renamed (breaking change approved by Taylor).** `CarParams`, `SuspensionTables`, `TireScale`, `ViscoTire.Flags` members now use full names; `Assets/SciroccoParams.asset`, `Assets/SuspensionTables.asset` and `Assets/TestTrack.unity` were re-keyed with the values preserved (checked with a SerializedObject dump before/after). `FormerlySerializedAs` attributes and the legend comments were removed. `tools/kinematics/export_unity.py` writes the new names; `-overrides` files must use the new `p.<name>` keys (see `docs/RENAMES.md`).
- Removed `tools/patches/` (stale dev-box copies and `.bak` files; unreferenced).
- No behavior change: 117/117 tests, 15 scenarios bit-identical to the baseline.

### Changed (readability pass 5/6: WheelCorner, CarController, PT 20:30)

- `WheelCorner.cs`, `CarController.cs`: full-word members and locals, `@description/@param/@return` headers, `FormerlySerializedAs` on scene-serialized fields. Call sites in HUD/audio/input/SmokeTest/editor updated. No behavior change: 117/117 tests, 15 scenarios bit-identical to baseline, serialized scene values identical.

### Changed (readability pass 4/6: tools scripts, PT 20:35)

- All `tools/**/*.py` and `*.sh`: correct shebang (`#!/usr/bin/env python3`, `#!/bin/bash`, `#!/usr/bin/env bash`), executable bit on shell scripts, and a `# @description / # @param / # @return` header comment. Comment-only change; no code lines touched.

### Changed (readability pass 3/6: steering linkage / torque / suspension tables, PT 20:10)

- `SteeringLinkage.cs`, `SteeringTorque.cs`, `SuspensionTables.cs`: full-word identifiers, `@description/@param/@return` headers, equation comments. Serialized table field names deliberately unchanged. No behavior change (117/117 edit-mode tests; 15 scenarios, 0 deviations in all CSV rows, bit-identical). Names in `docs/RENAMES.md`.

### Changed (readability pass 2/6: tires, PT 21:00)

- `PacejkaTire.cs`, `ViscoTire.cs`, `TireThermal.cs`: full-word identifiers (constants, state, methods), `@description/@param/@return` headers, equation/unit comments (S/SD/D/E tags kept), `ViscoTire.Build` and `TireThermal.Step` split into small functions with identical arithmetic. Call sites (WheelCorner, DebugHUD, SmokeTest, tests) updated. No behavior change (tests + diagnostics compared with the pre-change baseline). Names in `docs/RENAMES.md`.

### Changed (readability pass 1/6: engine / drivetrain, 19:30 PT)

- `Engine.cs`, `Drivetrain.cs`: full-word identifiers, `// @description / @param / @return` header on every function, physics comments (equations, units, S/SD/D/E tags kept), and the constructor / `FreshFraction` / `Step` split into small single-purpose functions with identical arithmetic. **No behavior change**: 117/117 edit-mode tests pass, and the 15 diagnostic scenarios match the pre-change baseline: skidpad 0.785 g, understeer gradient 1.834 deg/g, step-steer yaw 13.834 deg/s / overshoot 10.52 %, brake 100-0 47.85 m, 0-100 km/h 9.87 s, gamepad results identical; all 8 battery metrics identical to 3 decimals, and the engine-only scenarios (rev sweep, stall restart) are bit-identical row by row. (Vehicle scenarios jitter by +-1 N / <=2 % on a few rows between any two runs of the same build because the player is not frame-deterministic; see docs/RENAMES.md notes.) Old -> new names in `docs/RENAMES.md`.

### Fixed (14:55 PT)

- **Wheel steering dead when a device is bound as 'wheel'** (regression from acdb0e1). In `InputMapper.StepSteer` the gamepad-assist bookkeeping was added as `else if (assist != null)` in front of the wheel branch; InputRouter always creates the assist, so the wheel angle was never applied and the column stayed put, with the assist on or off (pedals unaffected; only rebinding the wheel as a gamepad "worked"). A wheel-role device now drives the column directly again (1:1 over the full 1260 deg / scaled mapping, no rate or grip limit); the assist only shapes stick/key (gamepad-role) input and is just kept in sync with the wheel. Settings migration (v5/v6/v7 -> v8) verified to keep the wheel steer binding, its calibration and rotation. Tests: `Wheel_Steers_Directly_With_Assist_On_And_Off`, `Gamepad_Assist_Applies_Only_To_Stick`, `Migration_Keeps_Wheel_Binding`.
- **VR menu mouse cursor could not click** (VrPanel, acdb0e1). The panel only overwrote `Event.current.mousePosition`, but IMGUI's clip stack re-derives the mouse from the real window position at every `BeginArea`/`BeginScrollView`, so the menu's buttons and sliders were hit-tested at the desktop mouse, not at the drawn cursor; and while SteamVR owns window focus no native clicks arrive at all. Now the Input System mouse (button, drag, wheel) is queued as IMGUI events in panel space (`VrPointerQueue`, delivered in place of a Repaint pass) and drawn/dispatched inside a parent clip whose input transform maps onto the cursor (`VrMouse.WithMouse`); native desktop mouse events are ignored while the VR cursor is active. Clicks, slider drags and scrolling work through the cursor. Tests: `VrMouseClickTests` (headless IMGUI: layout button onClick, slider drag inside group + scroll view, queue, mapping).

### Added
- **Tires: selectable "Experimental (viscoelastic)" compound** (01:40 PT). Quick settings -> "Tire compound: Default / Experimental (viscoelastic)"; settings file v8 (v7 files migrate to Default, everything else kept). Default compound unchanged (smoke CSVs byte-identical to e4004aa).
  - `ViscoTire.cs`: complex modulus of a carbon-black E-SBR tread (Havriliak-Negami + fractional filler network, fitted by `tools/visco_fit.py` to sourced DMA tan d(0/60 C), Sae-oui 2017, **D**), WLF shift about Tg -50 C (**S**), Payne softening (Kraus, **SD**), Persson hysteretic friction + Lorenz/Persson adhesion (**S** laws, road texture **E**) precomputed as master curves in log10(a_T v), flash temperature (Tian-Kennedy, **E** asperity size).
  - Calibration: grip factor mu(v_s)/mu(v_s,ref) on the sourced peak coefficients, = 1 at the reference slip speed at every temperature (no added grip; slip stiffness unchanged). Rate-dependent tread stiffness, load-dependent relaxation length (Pacejka sigma = C_Falpha/c_Fy), rolling resistance ~ tan d (referenced to ISO 28580), tread-hysteresis share of friction heat into the TireThermal bulk (`pFricBulk`, 0 for Default).
  - Effect (thermal on): skidpad 0.784 -> 0.780 g, understeer 1.84 -> 1.94 deg/g, step yaw overshoot 10.5 -> 9.7 %, threshold brake +0.1 m. Ablation per principle in `tools/viscoelastic_tyre_research.md`; `ViscoTireTests` (15); harness `tools/visco_smoke.py`, `visco_metrics.py`, `visco_plots.py`.

## [2026-10-13] (PT)

### Fixed
- **Engine: realistic closed-throttle manifold pressure (closed-plate leakage + residual-gas floor) and friction/pumping double-count** (22:42 PT). Cause: the air-path engine (ece5ae0) had no leakage past the closed plate and inducted fresh air in proportion to MAP down to vacuum, so overrun MAP fell to 7/3/2 kPa at 2000/4000/6000 rpm (real SI overrun ~15-34 kPa abs: aa1car.com MAP sensor article, decel vacuum 4-5 inHg above a 16-20 inHg idle, **S-web**).
  - Leakage (`Engine.cs`, `CarParams`): the stop screw holds the primary plate stopScrewTurns x pitch / lever arm off its seat (Bentley paper + 1/4 turn, **S**; M4 pitch, 15 mm arm **E**) plus a 0.02 mm radial plate-to-bore annulus on both plates (**E**): 6.4 mm^2 total, in parallel with the idle bypass.
  - Fresh charge: VE(rpm) x phi(MAP); phi = Heywood (1988) ideal throttled-cycle volumetric efficiency (r_c - (p_e/p_i)^(1/n))/(r_c - 1) (residual at exhaust pressure expands into the intake; r_c 10.0 **S**, n 1.3 **E**) minus the volume of exhaust gas blown back through the valve-overlap curtain (33 mm exhaust valve **SD**; 0.6 mm lift at TDC, 20 deg overlap, 600 K **E**: stock DX cam timing not found).
  - Friction: the Heywood/Barnes-Moss tfmep correlation 0.97 + 0.15 N + 0.05 N^2 bar is total MOTORED friction at WOT for 845-2000 cc 4-cyl SI engines and includes pumping (tfmep = pmep + rfmep + afmep). Pumping is now (p_man,WOT(rpm) - p_man) instead of (p_amb - p_man), so the WOT throttle-body drop is not counted twice. The throttling loss below WOT MAP was never in tfmep, so it was not double-counted. Effect: less than 1 N m.
  - Idle bypass re-sized (**D**) from 10.1 to 3.3 mm^2: idle is still a self-restoring steady state at 900 rpm, and idle MAP is 31 kPa (was 21).
  - Result (steady, closed pedal): MAP 15.0/11.3/9.6/8.2 kPa at 2000/3000/4000/6000 rpm (was 7.1/4.2/3.0/2.0). This sits at the low end of the cited range at 2000 rpm and below it at high rpm; it was not forced to a target. Engine braking (fired) changed by 0.3-0.6 N m, e.g. -28.5 N m at 3000 rpm (was -28.9). WOT is within 0.06 N m of the sourced curve. Powerstep corner tests changed by 0.1 % curvature or less.
- **Audio: exhaust level v3 for the higher overrun MAP** (22:42 PT). With MAP ~0.09-0.15 bar the backflow pulse is smaller, and the coastdown test drifted to 9.2-10.1 dB under WOT (above the 6-10 dB band at 2200 rpm). `ExhaustAmp` now uses the gas volume moved at pipe pressure: blowdown V((p_EVO/p_back)^(1/g) - 1), backflow V(1 - (p_EVO/p_back)^(1/g)) (isentropic, g = 1.3). BackflowWeight 0.60 (**E**, rarefaction transmission; was 0.47 on the linear step). PDisp is held at 9.2 % of the WOT source, and OfflineMapZero is now 0.10. Result: 7.2-8.8 dB under WOT through both in-game coastdowns (before: 7.9-8.6 dB); EditMode coastdown 7.4-9.6 dB. Pitch still tracks rpm. Audio prefs (no intake hiss, smooth filtered engine, noise tires, pause gate) unchanged. `EngineOverrunAudioTests` MAP check is now 0.05-0.35 bar (was < 0.15).

### Fixed
- **Audio: engine audible on overrun again with the air-path engine** (22:11 PT). Cause: the exhaust level model (8aa2941) scaled with p_EVO ~ MAP and was tuned for the old ~0.22 bar closed-throttle MAP; the physical air-path engine (ece5ae0) gives a physically correct 0.02-0.06 bar on overrun, so the engine fell 16-22 dB under WOT (in-game coastdown, 3rd/4th from 5000 rpm) and was inaudible. Combusting/limiter/rpm were correct; tires were silent on the straight (not masking); no wind source exists.
  - Fix (`CarAudio.cs` `ExhaustAmp`): the tailpipe source is the pressure step at exhaust-valve opening |p_EVO - p_back| (p_back 1.05 atm, **E**): +3.95 atm blowdown at WOT; on overrun a reverse pulse of ~0.95 atm (exhaust gas back into the near-vacuum cylinder), weighted 0.47 (**E**: cool rarefaction, damped by cat/muffler), plus an exhaust-stroke displacement term 0.4 atm (**E**) so part-load cruise never nulls; level ~ source^0.75 as before.
  - Result: overrun 7.3-9.1 dB under WOT through both in-game coastdowns (was 15.7-22.7 dB); fuel-cut motoring ~8 dB under. Pitch = firing frequency rpm/30 (within 0.1 Hz). No intake hiss, smooth filtered engine, noise-only tire and pause gate unchanged.
  - `EngineOverrunAudioTests` (real Engine coastdown: overrun 6-10 dB under WOT, pitch tracks rpm, fuel cut audible); `OfflineMapZero` 0.22 -> 0.03 (real overrun MAP). `-audiocoast` player flag (with `-smoketest -scenario audiocoast`) logs an in-game coastdown with engine/tire RMS.

### Changed
- **Engine: physical air-path throttle model** (20:55 PT). Replaces the normalized throttle/manifold model in `Engine.cs`:
  - Pedal drives the primary butterfly through a linear cable lever (E). A progressive link opens the secondary late (two-stage CIS body 35/44 mm, **SD**). Closed plate 7 deg, full open 90 deg, secondary starts at a primary angle of 60 deg, Cd 0.8: all **E**.
  - Plate area uses Heywood's butterfly formula (including the shaft). Flow through each plate uses the compressible isentropic orifice equation, choked below the critical pressure ratio.
  - Manifold pressure fills and empties isothermally, dp/dt = RT/V (mdot_in - mdot_out). The manifold volume of 2.5 L is **E**.
  - Cylinder air = VE(rpm) x rho_manifold x Vd x rpm/120, with Vd = 1781 cc (**S**). The old coupling of air demand to revLimit is removed.
  - Torque = eta_i x air per cycle x LHV/AFR/(4 pi) - friction - pumping. Friction is Chen-Flynn FMEP with coefficients from Heywood's correlation; pumping is (p_amb - p_man) Vd/(4 pi).
  - **Part-throttle curves are extrapolated from the WOT curve (D/E).** VE(rpm) is derived so that steady WOT brake torque reproduces the sourced curve. The curve at any other pedal position then follows from the airflow physics, not from scaling the WOT curve. This follows Niels Heusinkveld's argument: at a fixed throttle, torque falls with rpm and crosses zero. No part-throttle dyno data exists.
  - The idle governor is removed. The idle bypass area is derived at load time from steady closed-pedal net torque = 0 at idleRpm (**D**). Idle is self-restoring. idleRpm changes 850 -> 900 (**S**: DX 900 +- 100 rpm).
  - The rev limiter now cuts fuel (**S**: DX fuel-pump-relay cut at 6500-6900 rpm) instead of closing the throttle.
  - Removed: throttleFlowCoeff, throttleLeakArea, idleMaxPedal, manifoldRate, pumpingTorque, frictionTorqueBase/PerKrpm. mapZeroTorque is kept for audio only.
  - At 3000 rpm: 10% pedal gives -9 N m (was -32), zero torque at ~13.5% pedal (was ~16%), 30% pedal gives 74 N m (was 114 N m).
  - Tests: new `EngineAirPathTests`.
- Handling baseline re-solve (20:44 PT, approved by Taylor): kept only the 3df3038 estimates that do not conflict with sourced/derived data.
  - Dropped: front lower-arm pivots z +20 mm (back to 0.195 / 0.205 m, E). The sourced 0.379 m tie rod with inner point (-0.09949, ±0.22160, 0.35594) was fitted for this height. Front bump steer -18.4 → +0.55 deg/m, front RC 188 → 134 mm, camber gain -37.9 → -32.4 deg/m.
  - Dropped: beam shear center x -2.00 m (back to -2.06, E). -2.00 moved rear ground camber-vs-roll to 0.96 and rear RC to 11 mm, away from the D targets 0.85 / 180 mm. Now 0.80 / 55 mm.
  - Kept (E, no conflict found): front arm bushes x2; rear trailing-arm pivot z -30 mm (rear roll steer now slight understeer, +0.0033 deg/deg, matching the E target); front ARB 7200 N/m; rear roll 8520 N/m; steering stiffness 12000 Nm/rad; pneumatic trail 0.035 m.
  - Tables re-solved (`solve_kinematics.py`, `multibody_compliance.py`, `export_unity.py`). Lock at ±68.6 mm rack: 40.34 / 32.91 deg (sourced 40°22′ / 32°55′). Rear anti-lift coefficient 0.60 (unchanged).
  - Handling battery (thermal off), re-solve vs 3df3038 / old baseline: understeer gradient 1.84 vs 2.40 / 1.06 deg/g; skidpad 0.785 vs 0.773 / 0.811 g; step-steer steady yaw rate 13.8 vs 12.5 / 16.3 deg/s; power-on R40 2nd (full throttle) curvature change at 3 s -52.3 % vs -46.7 % / -48.1 %.
  - Tests: `SingleBaselineTests` checks front bump steer near zero and rear RC 55 mm; `SteeringLinkageTests` lock reference 40.34 / 32.91.
- **Single handling baseline** (20:30 PT). The former 'ProposedUndersteer' profile is now the only handling profile. Its inputs are folded into `CarParams.cs` and `tools/suspension_params.json`. Every one is an **estimate (E)** inside the estimated range, chosen by Taylor for the understeer balance (not sourced):
  - front ARB 7200 N/m (was 4500);
  - rear twist-beam roll 8520 N/m (was 10650; rear roll stiffness 280 Nm/deg, was 350);
  - steering stiffness 12000 Nm/rad (was 20000);
  - pneumatic trail at zero slip 0.035 m (was 0.025);
  - front lower-arm pivots z +20 mm;
  - front arm bush rates x2;
  - rear trailing-arm pivot z -30 mm;
  - beam shear center x -2.00 m (was -2.06).
  - Spring and wheel rates are unchanged.
- Kinematic and compliance tables re-solved (`solve_kinematics.py`, `multibody_compliance.py`, `export_unity.py`) with these changes on top of the current geometry. The run keeps the caster-trail fix, the 0.379 m tie rod with inner point (-0.09949, ±0.22160, 0.35594), and the steering linkage grid. Results:
  - front RC 134 → 188 mm; camber gain -32.5 → -38.0 deg/m;
  - front bump steer +1.1 → -17.8 deg/m. The tie-rod inner point was fitted for zero bump steer at the old pivot height;
  - rear RC 55 → 11 mm; rear anti-lift 0.66 → 0.60;
  - full lock at the ±68.6 mm rack stop: 40.34/32.91 → 40.50/32.90 deg (sourced 40°22′/32°55′).
  - KPI, caster, trail 8.84 mm and scrub -10.0 mm at design are unchanged.
- Front 16 mm / rear 19 mm anti-roll bar diameters downgraded from S? to **SD**. The source lists them only for the '84 US/Canada car (online_research_findings.md §3).
- Handling battery (thermal off) for the single baseline vs the old Baseline / old Proposed:
  - understeer gradient 2.40 deg/g (1.06 / 1.88);
  - skidpad 0.773 g (0.811 / 0.782);
  - step-steer steady yaw rate 12.5 deg/s (16.3 / 13.9).
- Input settings file version 5 → 6.
- README and HUD updated: the HUD shows "handling baseline (single)"; the P / d-pad-up hint is gone.
- Tire thermal (20:11 PT): below the bench curve, grip now follows road-test data (Tambellini 2019 summer-tire stopping distance vs road temperature, 0.211 %/°C above 77 °F; full grip at or below 77 °F) instead of the 64 % cold hold. It blends continuously into the Alcázar Vargas bench curve at 151.3 °F. Grip temperature is now the heat-capacity-weighted tread mean (surface zones + bulk) instead of bulk only, so a 270 °F surface over a 77 °F bulk gives 97 % instead of 64 %. The thermal model stays OFF by default, so thermal-off handling is unchanged. Tests: TireThermalTests. Docs: tire_thermal_research.md §5b, §7.
- Car audio (20:07 PT):
  - Engine overrun stays audible: exhaust level follows exhaust-valve-opening pressure, with a softer lowpass off load and a subtle overrun burble. The on/off difference goes from 11-14 dB to 7-8 dB.
  - Engine "grain" removed: amplitude jitter is now subtle and glided, with a 2-pole lowpass and tanh instead of hard clip.
  - Tire sound v4: noise-only resonant bandpass (150 → ~174 Hz center, ~2.5 semitones max). The impulse-train tone and its 2nd harmonic are removed.
- Tire sound v3 (19:34 PT): low slip tone (150 Hz, +2.5 semitones max), audible from slip onset.

### Added
- **Gamepad / keyboard steering assist** (22:10 PT). Input layer only (user preferences, not vehicle physics); never applied to a wheel bound as `SteerSource.Wheel` (Simucube path unchanged). New `GamepadSteering.cs`:
  - Stick -> deadzone with rescale -> response gamma -> low-pass smoothing -> *target* road-wheel angle; the steering moves to the target rate-limited (steering speed), like a driver turning the wheel.
  - Target range is physics-aware: full mechanical lock at parking speed, blended (speed sensitivity, 2-6 m/s fade-in) to *front-axle travel angle x caster follow + stick x grip limit x the front tire's peak-grip slip angle*. The peak slip angle is found from `PacejkaTire.PureLat` at the live front load (BeamNG understeer-reduction / Advanced Steering approach).
  - Countersteer: steering speed x countersteer factor when unwinding / crossing center, or when steering into a rear slide (rear-axle travel angle beyond the peak slip angle). Released stick lets the wheels follow the front-axle travel direction by the caster-follow fraction (self-aligning; BeamNG oversteer-reduction idea).
  - Defaults: on, steering speed 30 road deg/s (~500 deg/s handwheel), gamma 2.0, deadzone 5%, smoothing 0.04 s, speed sensitivity 100%, grip limit 100% of peak slip, countersteer x2.0, caster follow 30%.
  - New settings tab **Gamepad Steering**: on/off plus 8 sliders, each with label, value, default, Reset button; gamepad navigation (Up/Down, Left/Right step, Select/A = reset item; last item resets all). Saved in the bindings file.
  - Settings file **v7**: `gamepadSteer` block; v6 and older files keep every existing binding/setting and gain the defaults.
  - Tests: `GamepadSteeringTests` (shape, rate limit, grip limit, caster follow, countersteer rate, noisy-stick smoothness, tire peak slip, v6 -> v7 migration, round trip, menu adjust/reset, wheel path not assisted). Existing version asserts 6 -> 7.
  - SmokeTest scenarios `gplane` (80 km/h 3.5 m lane change) and `gpoversteer` (90 km/h, handbrake-induced oversteer then recovery), with a scripted noisy over-driving stick; `-gpassist 0|1`, `-gpseed`, `-gphb`, `-gpcs`, `-gpcf`. Lane change: direct mode spun in 1 of 4 seeds, peak yaw rate 50-109 deg/s, steering rate RMS ~89 deg/s; with the assist: no spins, peak yaw rate ~29 deg/s, steering rate RMS 16-20 deg/s, similar path error. Oversteer (handbrake 150/250/350 ms): direct spun every time; assisted recovered every time (1.9/1.9/3.7 s).
- **VR menu mouse cursor** (22:10 PT). With XR (or `-vrpanels`) active and the menu open, the desktop mouse position is mapped proportionally (whole game window -> whole menu panel, iRacing-style) onto the menu render texture, an arrow cursor is drawn into the panel, and IMGUI events get the mapped position so hover, click, slider drag and scroll work. Hidden when XR is off or the menu is closed. `VrMouse` in `VrPanel.cs`; mapping covered by `GamepadSteeringTests.VrMouse_MapsWindowToPanel`. Not checked in a headset yet.
- Initial import (18:27 PT): Scirocco II physics sim Unity project (2022.3.62f3) plus tools.
- `CHANGELOG.md` (this file), backfilled from git log for 2026-10-13.
- `SingleBaselineTests` (EditMode):
  - checks the baseline defaults;
  - checks that the profile enum, `kinProposed` and the toggle are gone;
  - checks the re-solved tables include the steering grid;
  - checks that a v5 settings file with a P / d-pad-up handling-toggle binding loads with that binding dropped and every other setting kept.
- Saved-settings migration (`InputMapper.Migrate`, v6): bindings on the retired handling-toggle slot are cleared on load. The int slot 14 stays as `InAction.RetiredProfileToggle`, so old `input_bindings.json` files still parse.
- Editor check for Git LFS pointer files under Assets (19:57 PT).
- Pause/menu audio fade (19:40 PT): all car audio fades to silence on pause or menu, with a 30 ms click-free gate. Includes a test.

### Removed
- Handling-profile switch (20:30 PT):
  - `CarParams.HandlingProfile` enum, `profile`, `kinProposed`, `prop*` fields and `ForProfile` (replaced by `RuntimeCopy`);
  - `CarController.ToggleProfile`;
  - the P key and the gamepad d-pad-up default binding;
  - the profile row in the bindings menu;
  - `SuspensionTablesDataProposed.cs` and `Assets/SuspensionTablesProposed.asset`.
- Engine intake hiss (19:34 PT, with tire sound v3).
- Research notes and car documents removed from the repo (18:30 PT), then restored by revert (18:35 PT).

### Fixed
- **Laguna Seca terrain invisible in player builds** (21:45 PT). None of the four DEM Terrain components (Outer 1902 m plus Inner_A/B1/B2) drew anything in builds, on Linux and Windows alike. Only the road, curb and building meshes showed. Cause: `materialTemplate` was null, so the terrains used Unity's built-in default terrain material, and its shader (`Nature/Terrain/Standard`) was not included in the player. Fix: new `Materials/M_Terrain_DEM.mat` (Nature/Terrain/Standard) assigned to all four terrains in `LagunaSeca.unity`. `TrackSetup` now assigns it when it rebuilds the terrain (menu: Scirocco > Assign Laguna Seca terrain material). Terrain data, the raw heightmaps and Git LFS were all correct.
- `InputBindings.FromControl` (19:57 PT) threw ArgumentOutOfRangeException when a control path is not `<device>/<control>`. It now falls back to the control name.
