# Scirocco Sim: 1984 VW Scirocco II physics sim (Unity)

A physics-first driving simulator of a **1984 VW Scirocco II, EU 1.8 GTI spec** (112 PS, 020 gearbox code 4Y, 185/60R14). It uses custom raycast suspension, a Pacejka (MF5.2-style) tire model with thermal and scrub models, a manifold-pressure throttle/engine model, steering-linkage kinematics with force feedback, and a Laguna Seca track built from USGS terrain data. Unity's WheelCollider is **not** used.

This repository root **is the Unity project folder**.

## Opening in Unity
1. Install **Unity 2022.3.62f3** (the version in `ProjectSettings/ProjectVersion.txt`) through Unity Hub.
2. Run `git lfs install` once, then clone. LFS content (textures, FBX, heightmaps, terrain data, DLL) downloads with the clone; if it didn't, run `git lfs pull`.
3. Unity Hub → *Add project from disk* → select the repo root. The first open regenerates `Library/` (this takes a few minutes).
4. Open the scene under `Assets/` (or use the **Scirocco** menu → Build Test Scene / track setup) and press Play.

The project uses Force Text serialization and Visible Meta Files. Always commit `.meta` files together with their assets.

## Layout
- `Assets/`: scripts (`Scripts/Runtime`, `Scripts/Editor`, tests), materials, the `Tracks/LagunaSeca` terrain, heightmaps, and the aerial texture.
- `Packages/`, `ProjectSettings/`: Unity configuration.
- `tools/`: offline physics and data pipeline that generates the project's data.
  - `kinematics/solve_kinematics.py`: suspension/steering kinematics solver driven by `suspension_params.json`.
  - `kinematics/export_unity.py`: exports the solved tables into the Unity project.
  - `suspension_params.json`: hardpoints and parameters, each value tagged with a source tag (see below).
  - `suspension_data.md`, `tire_thermal_research.md`, `service_manual_findings.md`, `online_research_findings.md`, `proposed_geometry_fix.md`: research notes and justification for each value.
  - `sync_tools.sh`: refreshes `tools/` from the working tree.

## Data-tagging convention
Every physical parameter (in `suspension_params.json`, `CarParams.cs` comments and the notes) carries a provenance tag:
- **S**: Sourced. Taken directly from a primary or credible source (factory manual, VW part data, spec sheet). `S?` / `S-weak` marks a weaker or aggregator source.
- **SD**: Sources Disagree. The sources conflict, or the source covers a different market/model year (e.g. the US 1985+ manual applied to an EU 1984 car). The chosen value and the alternatives are noted.
- **D**: Derived. Computed from S values (e.g. turns lock-to-lock = 2 × lock angle × ratio).
- **E**: Estimated. An engineering estimate or proxy-vehicle value with no direct source. Replace it when a source turns up.

Copyrighted reference material (manual scans, PDFs, OCR text) is deliberately **not** in this repository. The notes cite those sources by name only.

---

# Original demo notes
# SciroccoSimDemo (Unity): first playable physics demo
Placeholder-art demo of a 1984 VW Scirocco II (EU 1.8 GTI 112 PS, 020 gearbox code 4Y, 185/60R14 summer tire).
All tire forces are custom. **Unity's WheelCollider is not used.**

## Open it
1. Install Unity **2022.3 LTS** (any 2022.3.x; ProjectVersion says 2022.3.40f1, and Unity will offer to upgrade/downgrade). Unity 6 should also work.
2. Unity Hub → *Add project from disk* → select this folder. On first open Unity generates Library/ and the missing ProjectSettings files.
3. Menu **Scirocco → Build Test Scene**. This creates `Assets/SciroccoParams.asset` (the car spec ScriptableObject) and `Assets/TestTrack.unity`: a 2 km flat plane with cones, a primitive car (box body, cylinder wheels), a follow camera and the debug HUD.
4. Press **Play**.

Project Settings → Player → *Active Input Handling* must include **Input Manager (Old)** or **Both**. Unity 6 defaults to the new Input System only.

## Controls
| Action | Keyboard | Gamepad (legacy Input Manager) |
|---|---|---|
| Throttle / brake | W / S (arrows) | Left stick Y (optional axes "Throttle"/"Brake" for triggers, add in Input Manager) |
| Steer | A / D | Left stick X |
| Shift up / down | E / Q | RB / X (button 5 / 2) |
| Clutch (hold) | Left Shift | LB |
| Handbrake | Space | B |
| Reset | R | Back |

You start in **neutral**: press E for 1st, blip the throttle, and go. Shifting gives 0.25 s of neutral automatically. Holding the clutch while shifting is optional.

## Architecture (Assets/Scripts/Runtime)
- `CarParams.cs`: ScriptableObject with the full spec. Comments mark (S) sourced and (E) estimated values; see `../../report.md` §6.
- `PacejkaTire.cs`: Magic Formula (MF5.2-style subset): load sensitivity, camber thrust, combined slip by cosine weighting.
- `WheelCorner.cs`: raycast suspension (spring, bilinear damper, progressive bump stop), tire slip with a relaxation-length lag, wheel spin with Coulomb brakes, and wheel visuals (accumulated spin angle and steer).
- `Engine.cs`: torque curve plus a **manifold-pressure throttle model** (after Niels Heusinkveld): butterfly area → orifice flow vs cylinder pumping → MAP → torque. Part-throttle torque falls with rpm, closed throttle gives engine braking, plus idle governor, limiter and stall.
- `Drivetrain.cs`: clutch with stability-sized coupling and a torque cap, 5 speeds + R, final drive 3.667, open diff (equal split), FWD.
- `CarController.cs`: input, steering linkage (constant pinion -> rack -> per-wheel angles from the suspension kinematics, `SteeringLinkage.cs`), roll camber (MacPherson front, twist beam rear), front ARB and twist-beam roll stiffness, aero drag. FixedUpdate at **500 Hz** (fixedDeltaTime 0.002, set in ProjectSettings/TimeManager.asset and enforced at Start) with **4 substeps** for tire, wheel and engine (2 kHz).
- `DebugHUD.cs`: speed, rpm, gear, MAP, throttle area, clutch slip, and per-wheel Fz, slip ratio, slip angle, Fx, Fy and camber.
- `Editor/SciroccoSetup.cs`: builds the scene from primitives.

## Build & verification (done on the box, 2026-10-12)
- Editor: Unity **2022.3.62f3** (installed with the Unity CLI `unity install`), Unity Personal license via Hub.
- Batch build: `unity build . --target StandaloneLinux64 --execute-method SciroccoSetup.BuildLinux -o ../../builds/linux/SciroccoSimDemo.x86_64`
  (`SciroccoSetup.BuildLinux` builds the scene, then a Development Linux Mono player). Result: **Succeeded, 0 errors**.
- Smoke test: `./SciroccoSimDemo.x86_64 -batchmode -nographics -smoketest -logFile smoke.log`. The `SmokeTest` component drives a scripted
  full-throttle run with auto upshifts at 6000 rpm and logs `SMOKE ...` lines. Last run: the car settles at 0.51 m with static loads 2950/2950/1930/1930 N
  (60.4 % front), with no oscillation. 0–100 km/h in **9.41 s** (factory claim 8.6 s). About 198 km/h after 60 s and still rising slightly (claimed Vmax 200).
- Not yet tested: cornering, braking, or interactive play with a screen and keyboard.
## Known simplifications / next steps
Suspension forces are computed once per 500 Hz step, while the tire and drivetrain substeps reuse that step's chassis velocity. The bushings aren't modelled as compliance. Roll camber is approximate, with no toe or roll-steer curves. There is no tire thermal/pressure model or force feedback yet, and no torque steer.

## Running the build (Windows / Linux)
- Windows: unzip `SciroccoSimDemo_Windows_x64.zip` and run `SciroccoSimDemo.exe` (1280x720, windowed).
- Linux: `SciroccoSimDemo.x86_64`.

### Controls (Unity Input System; keyboard, gamepad, wheels, pedals, shifters, handbrakes)
Default bindings (all can be changed in the in-game **Input settings**, F1 / Esc / gamepad Start):
| Action | Keyboard | Gamepad |
|---|---|---|
| Steer | A/D, Left/Right (rate-limited to full lock) | left stick X (rate-limited, deadzone 0.10) |
| Throttle / Brake | W / S, Up / Down | RT / LT |
| Clutch | Left Shift | LB |
| Shift up / down (sequential) | E / Q | RB / X(West) |
| H-pattern gears 1-5, R | 1-5, 6 = Reverse (latched) | bind to your shifter |
| Handbrake | Space | B(East) |
| Reset | R | Back(Select) |
| Look (camera orbit) | – | right stick |
| Input settings | F1 or Esc | Start |
The car starts in neutral: press E (or select 1st) to drive.

### Steering wheel, pedals, shifter (multiple devices/brands)
1. Plug in all devices **before** starting the game. Reconnects are handled: bindings are stored by product name + layout (+ index for identical devices) and re-resolved whenever a device is added or removed.
2. Press F1. For each action press **+ Bind**, then move the axis fully or press the button on any device. Esc cancels.
3. Press **Calibrate** for each axis. For a wheel, turn it fully both ways, centre it, then press **Done (centred)**. For a pedal, press it fully, release it, then press **Done (released)**. Min/max are captured, and inversion is detected from the rest position.
4. Per axis you can set:
   - **inv**: invert;
   - **dz**: deadzone;
   - **Full / Positive / Negative**: half-axis, for combined pedals where one axis carries throttle on one half and brake on the other. Bind the same axis twice and set Positive and Negative.
   - For steer: **Wheel / Stick**. Wheel is an absolute angle; Stick is rate-limited to full lock.
5. Set **Wheel rotation** to match your wheel driver (default 900° lock-to-lock).
6. Settings are saved on close to `input_bindings.json` in Application.persistentDataPath (Windows: `%USERPROFILE%\AppData\LocalLow\DefaultCompany\SciroccoSimDemo\`). PlayerPrefs stores the path. **Reset defaults** restores the table above.

Axis ranges: a raw axis can report 0..1 or -1..1. Calibration maps whatever it reports to 0..1 (pedals) or -1..1 (steer/look). The layout's own deadzone processors are bypassed (unprocessed values are read), so the only deadzone is the one you set.

### Steering mapping: no assists
Your wheel angle is treated as the car's steering-wheel (column) angle, and the column drives the rack through a constant pinion (0.1081 mm rack per degree, ±68.6 mm at the 634.7° stop), and each front wheel's angle comes from the suspension kinematics (40.4°/32.9° at full lock, ~5.2° mean at 100°; on-centre ratio 19.26:1).
- **1:1 column (default).** The real car turns ~3.5 turns lock-to-lock (±630° column → ±36.4° road wheel). A 900° wheel (±450°) reaches ±26.0° road wheel, which is a soft lock at your wheel's limit. The last 180° each side of the real car's lock is not reachable, but every degree on your wheel equals a real degree. Set rotation to 1260° on wheels that support it for full lock. Above 1260°, the car's rack stop (±630°) applies.
- **Scale to car lock (option).** Your lock maps to the car's lock. With 900° the effective wheel→road ratio becomes about 13.6:1 on centre instead of 19.26:1, so the menu shows the effective ratio.
- There is no speed-sensitive steering, filtering, counter-steer or other assist. Keys and gamepad stick approach full lock at the existing 90°/s road-wheel rate (E), as before.
- The HUD shows the steering-wheel angle (top right) and the road-wheel angle.

### Force feedback
Not implemented. The Input System has no generic DirectInput force-feedback API, so FFB needs a native plugin: the Logitech Steering Wheel SDK (Logitech only, Windows), a vendor SDK, or a custom DirectInput / SDL haptic plugin. It is feasible later. The natural signal would be the kingpin moment already computed for steering compliance (tire lateral force × trail), sent as a torque command.

### Limitations
- No real hardware was tested here, only virtual devices (see the tests below) and keyboard in the Linux player.
- On Windows, most wheels, pedals and shifters appear as generic HID joysticks and work. A device that only exposes axes through DirectInput-specific drivers may not show up.
- Some wheels report combined pedals by default. Use the half-axis option, or switch the driver to separate axes.
- Vendor software (e.g. G HUB, Thrustmaster Control Panel) sets the hardware rotation. Match it in the menu.

### Tests
- `Assets/Tests/Editor/InputBindingTests.cs` (EditMode, Input System `InputTestFixture`) simulates three devices from different brands (wheel, pedals, shifter) plus a gamepad. It checks:
  - calibration maths;
  - combined-pedal halves;
  - 1:1, scaled and rack-stop steering;
  - capture on any device;
  - JSON persistence and reconnect by product name.
- Run: `unity test . --mode EditMode`.
- Physics regression: `builds/run_tests.sh` (the InputRouter disables itself in `-smoketest` runs).

## Handling baseline and model options (CarParams asset)
- There is **one** handling baseline (since 2026-10-13). The former 'ProposedUndersteer' profile became the only set of inputs; the profile switch (P key / d-pad up), the HandlingProfile enum and the second table set are gone. No gains or scalers.
- These baseline inputs are **estimates (E)**, inside the estimated range and chosen by Taylor for the understeer balance. They are not sourced data:
  - front ARB `arbFront` 7200 N/m (was 4500 D from the 16 mm bar);
  - rear `twistBeamRoll` 8520 N/m (0.8 x 10650; rear roll stiffness 280 Nm/deg);
  - `steeringStiffness` 12000 Nm/rad (was 20000 E);
  - `pneumaticTrail0` 0.035 m (was 0.025 E);
  - in suspension_params.json: front arm bush rates x2, rear trailing-arm pivot z -30 mm.
  - Dropped in the re-solve (2026-10-13) because they conflicted with sourced/derived data: front lower-arm pivots z +20 mm (with the sourced 0.379 m tie rod, inner point (-0.09949, +-0.22160, 0.35594), bump steer went to -18 deg/m; back at the original height it is +0.55 deg/m) and beam shear centre x -2.00 m (moved rear camber-vs-roll to 0.96 and rear RC to 11 mm, away from the D targets 0.85 / 180 mm; back at -2.06: 0.80 / 55 mm).
  - The kinematic/compliance tables are re-solved from these on top of the current geometry (caster-trail fix, tie rod above, steering linkage grid). Lock at the +-68.6 mm rack stop is 40.34/32.91 deg (sourced 40d22'/32d55').
  - Result (thermal off): understeer gradient 1.84 deg/g (3df3038: 2.40; old baseline 1.06).
  - Spring and wheel rates are unchanged.
- The 16/19 mm anti-roll bar diameters are tagged **SD**: the source lists them only for the '84 US/Canada car (online_research_findings.md).
- Saved input settings: old files (version <= 5) load fine. Their handling-toggle bindings are dropped and every other setting is kept (`InputMapper.Migrate`, version 6).
- `rearModel`: FullTwistBeam (default) | SimpleTable. `frontModel`: CompliantMultibody (default) | KinematicTable. `useUnsprungMass` (default on).
- `tirePreset`: ModernSummer (default) | PeriodMXV (estimated 1980s MXV-era tire).
- `steeringStiffness`, `pneumaticTrail0`, `mechanicalTrail`: steering-system compliance (0 stiffness = off).
- The HUD shows joint reaction forces on the body (arm bushes, tie rod, top mount, ball joint; rear pivot bushes, damper mounts) from the linear maps in kinematics/compliance.json.

## Rebuilding
- `python3 kinematics/solve_kinematics.py && python3 kinematics/multibody_compliance.py && python3 kinematics/export_unity.py` regenerates the single baseline table set (`SuspensionTablesData.cs`).
- Builds: `-executeMethod SciroccoSetup.BuildLinux` or `SciroccoSetup.BuildWindows`, or open in Unity 2022.3.62f3, run the menu setup, and press Play.

## Non-physical scalar audit
- No yaw, understeer or steering-assist gains exist.
- Numerical-only terms (flagged):
  - clutch coupling gain sized for stability (Drivetrain.cs);
  - low-speed slip denominators and relaxation floor (WheelCorner.TireStep);
  - hard travel stops 2e6 / top-out 2e5 N/m (E stop stiffness).
- Empirical tire term: camber peak-force reduction (1 - 4 gamma^2) in PacejkaTire (E shape, not data).
- Engine idle-valve controller (control logic).
