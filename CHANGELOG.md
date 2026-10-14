# Changelog

All notable changes to this project are documented in this file.
The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/). The project has no version numbers, so changes are grouped by date. Dates and times are Pacific Time (PT).

**Every future commit must update this file** (add its entry under `[Unreleased]` or under the current date).

## [Unreleased]

## [2026-10-13] (PT)

### Changed
- **Engine: physical air-path throttle model** (20:55 PT). Replaces the normalised throttle/manifold model in `Engine.cs`:
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
  - Dropped: beam shear centre x -2.00 m (back to -2.06, E). -2.00 moved rear ground camber-vs-roll to 0.96 and rear RC to 11 mm, away from the D targets 0.85 / 180 mm. Now 0.80 / 55 mm.
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
  - beam shear centre x -2.00 m (was -2.06).
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
  - Tire sound v4: noise-only resonant bandpass (150 → ~174 Hz centre, ~2.5 semitones max). The impulse-train tone and its 2nd harmonic are removed.
- Tire sound v3 (19:34 PT): low slip tone (150 Hz, +2.5 semitones max), audible from slip onset.

### Added
- **Gamepad / keyboard steering assist** (22:10 PT). Input layer only (user preferences, not vehicle physics); never applied to a wheel bound as `SteerSource.Wheel` (Simucube path unchanged). New `GamepadSteering.cs`:
  - Stick -> deadzone with rescale -> response gamma -> low-pass smoothing -> *target* road-wheel angle; the steering moves to the target rate-limited (steering speed), like a driver turning the wheel.
  - Target range is physics-aware: full mechanical lock at parking speed, blended (speed sensitivity, 2-6 m/s fade-in) to *front-axle travel angle x caster follow + stick x grip limit x the front tire's peak-grip slip angle*. The peak slip angle is found from `PacejkaTire.PureLat` at the live front load (BeamNG understeer-reduction / Advanced Steering approach).
  - Countersteer: steering speed x countersteer factor when unwinding / crossing centre, or when steering into a rear slide (rear-axle travel angle beyond the peak slip angle). Released stick lets the wheels follow the front-axle travel direction by the caster-follow fraction (self-aligning; BeamNG oversteer-reduction idea).
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
- **Laguna Seca terrain invisible in player builds** (21:45 PT). None of the four DEM Terrain components (Outer 1902 m plus Inner_A/B1/B2) drew anything in builds, on Linux and Windows alike. Only the road, kerb and building meshes showed. Cause: `materialTemplate` was null, so the terrains used Unity's built-in default terrain material, and its shader (`Nature/Terrain/Standard`) was not included in the player. Fix: new `Materials/M_Terrain_DEM.mat` (Nature/Terrain/Standard) assigned to all four terrains in `LagunaSeca.unity`. `TrackSetup` now assigns it when it rebuilds the terrain (menu: Scirocco > Assign Laguna Seca terrain material). Terrain data, the raw heightmaps and Git LFS were all correct.
- `InputBindings.FromControl` (19:57 PT) threw ArgumentOutOfRangeException when a control path is not `<device>/<control>`. It now falls back to the control name.
