# Experimental driveline shuffle model: sources, tags and bounds

Tags: **S** sourced, **SD** sourced-derived, **D** derived, **E** estimate (bounds given; the value is the default in `Assets/Resources/ShuffleDrivelineParameters.asset`).
Sourced values override estimates. Everything marked E must be replaced when a measurement exists (what to measure is in `diagnostics/driveline/driveline_shuffle_research.md` section 3.3).
Switch: Experimental tab, "Driveline shuffle model" (`CarParams.experimentalDrivelineEnabled`, default off). Off = the default `Drivetrain.Clutch` + `Engine.Step` path, untouched.

## Structure (new files in `Assets/Scripts/Runtime/Driveline/`)
| File | Role |
|---|---|
| `DrivelineInterfaces.cs` | `ICrankshaftTorqueSource` (engine side) and `ITorsionalDriveline` (driveline side) plus the input/result structs. These are the seams to move when engine, ignition, driveline, transmission and brake code are reorganized. |
| `EngineCrankTorqueSource.cs` | Adapter: existing mean-value `Engine` (manifold, combustion state, mean torques) + `FiringPulseModel` ripple. |
| `FiringPulseModel.cs` | Minimal firing pulses: slider-crank, single-zone first-law pressure, Wiebe burn, standard MBT spark, reciprocating inertia torque. |
| `TorsionalDriveline.cs` | The chain: crank, Karnopp clutch, disc, 3-stage damper with hysteresis, input shaft, lash mesh, carrier, combined halfshafts with lash, two wheels, engine block roll on mounts. Micro-stepped semi-implicit Euler. |
| `ClutchFrictionModel.cs`, `ClutchEngagementCurve.cs`, `ClutchDamperModel.cs` | Friction capacity with thermal fade, pedal to clamp curve, damper element. |
| `ExperimentalDriveline.cs` | Orchestrator called by `CarController`; replaces the default clutch/engine pair while the toggle is on. |
| `ShuffleDrivelineParameters.cs` | Parameter asset (ScriptableObject). |
Additive hooks outside the new folder: `Engine.BeginExternalCrankInterval/EndExternalCrankInterval` (new methods; `Step` untouched), `WheelCorner.longitudinalRelaxationEnabled` (first-order slip-ratio relaxation, off = byte-identical force path), `CarController` dispatch.

## Parameters
### Engine and firing pulses
| Parameter | Value | Tag | Bounds / source |
|---|---|---|---|
| Bore x stroke | 81.0 x 86.4 mm | S | VW EA827 1.8 8V, https://www.motor-car.net/audi-engines/item/20085-vw-ea827-1-8 , https://www.rwcmotorsport.com/engines.php |
| Connecting rod length | 144 mm | SD | piston/rod vendor listing (Wiseco, 1.8 8V 83-92): https://www.realstreetperformance.com/wiseco-pistons-1-8l-20v-81mm-std-8-51-86-4mm-stroke.html |
| Firing order | 1-3-4-2 | S | same sources; identical cylinders 180 deg apart, so only the summed waveform (period 180 deg) is tabulated |
| Compression ratio | 10.0 | S | existing `CarParams.compressionRatio` (DX 10.0:1) |
| Reciprocating mass per cylinder | 0.64 kg | E | 0.50-0.80. Anchors: forged aftermarket piston 0.298-0.318 kg, pin about 0.116 kg (vendor listings, not OE), rings and one third of the rod added |
| Wiebe a, m | 5, 2 | S | Heywood, Internal Combustion Engine Fundamentals (1988); Johansson et al., https://lucris.lub.lu.se/ws/files/5614960/1623903.pdf ; Chemkin SI zonal defaults |
| Burn duration 0-100 % | 60 deg | E | 45-80 deg (10-90 % duration is then 0.496 x this, 30 deg) |
| 50 % burned angle at standard spark | 9 deg ATDC | SD | empirical optimum 8-10 deg ATDC, SAE 2012-36-0498, https://doi.org/10.4271/2012-36-0498 |
| Spark advance | 22.1 deg BTDC | D | `theta_spark = CA50 - D (ln2 / a)^(1/(m+1))`; no ignition-system model, no knock, no mixture effects, one value at every speed/load (by decision) |
| Effective gamma | 1.3 | E | 1.25-1.38 (Heywood mid-cycle value) |
| Heat per cycle | solved | D | chosen so the cycle-mean gas work equals the mean indicated work of the mean-value engine; the ripple is added on top, the mean torque is unchanged |
| Intake / exhaust pressure | manifold / `exhaustBackPressure` | existing values | intake and exhaust strokes at constant pressure; no valve timing, no blowdown |
| Crank inertia | `engineInertia` 0.13 kg m^2 | E (existing) | 0.10-0.17 |
Checks (tests): peak pressure 49.7 bar at 15.5 deg ATDC at 150 N m indicated (the existing `peakCylinderPressureWideOpenThrottle` E is 60 bar); ripple mean zero, 2 pulses per revolution.

### Clutch
| Parameter | Value | Tag | Bounds / source |
|---|---|---|---|
| Disc outer diameter | 210 mm | S | Sachs 210 mm kit for 1.8 8V Mk2: https://techtonicstuning.com/product/sachs-clutch-kit-210mm-83-92-8v-18l-mk1-85-92-mk2/ |
| Inner/outer radius ratio | 0.70 | E | 0.62-0.76 |
| Effective radius | (Ro + Ri)/2 = 89.25 mm | D | uniform wear |
| Faces | 2 | S | one disc |
| Static capacity at full clamp | `clutchMaxTorque` 260 N m | E (existing) | clamp force derived `F = T / (n mu_s R_eff)` |
| Kinetic friction coefficient | 0.35 | E | 0.25-0.45 |
| Static / kinetic ratio | 1.10 | E | 1.00-1.30 |
| Friction slope with rubbing speed | 0 per m/s | E | -0.01 to +0.01; negative slope is the textbook judder driver (Crowther et al., https://doi.org/10.1016/j.jsv.2004.07.022 ) |
| Thermal fade | onset 573 K, complete 673 K, max 30 % | onset/complete E (520-620 / 620-750 K), 30 % S | up to 30 % friction loss with temperature: Haramina et al., https://www.mdpi.com/2075-1702/10/9/819 |
| Rubbing mass | flywheel 5.4 kg (SD, vendor 12 lb), pressure plate 2.0 kg (E, 1.5-3), c = 490 J/(kg K) (S, 460-500) | | https://www.eurosportacc.com/collections/oem-equivalent/products/oem-steel-flywheel-2-pin-vw-mk1-mk2-all |
| Cooling time constant | 240 s | E | 60-600 s |
| Karnopp dead band | `T_static dt / J_reduced` | D | the largest speed change one micro-step can produce |
### Pedal to clamp
| Parameter | Value | Tag | Bounds / source |
|---|---|---|---|
| Release-lever free play | 6 mm | S | Bentley (VW Golf/Jetta 1985-92 manual, clutch adjustment) |
| Lever travel at pedal stop | 16 mm | E | 12-22 mm |
| Finger lever ratio | 2.0 | E | 1.5-3.0 |
| Cushion spring travel | 1 mm | S | Sachs facing cushion, "up to about 1 mm" |
| Derived | bite begins at pedal 0.38, fully released at 0.50 | D | engagement speed follows from how fast the pedal moves through this range; keyboard auto-clutch keeps its existing launch law (its capacity fraction limits the clamp) |
### Damper, gearbox, shafts, mounts
| Parameter | Value | Tag | Bounds / source |
|---|---|---|---|
| Damper stage stiffness | 0.5 / 24 / 291 N m/deg | E | 0.2-1.5 / 10-56 / 150-400; examples: UFSC 1.0 L data 8.6-12.6 N m/deg, https://labsistemasveiculares.ufsc.br/en/dados-e-ferramentas-computacionais/dados-do-teste-de-frenagem/powertrain/ ; larger-engine table [unverified] |
| Stage end angles drive / coast | 5, 16 / 5, 12 deg | E | 3-8, 10-22 / 3-8, 8-16 |
| Stage hysteresis | 1 / 10 / 20 N m | E | 0.3-4 / 4-36 / 10-60. Presliding twist = largest friction / stiffest stage (D): below about twice the stage friction the damper locks and the small-amplitude frequency rises |
| Disc inertia | 0.0077 kg m^2 | E | 0.002-0.030 |
| Input shaft inertia | 0.020 kg m^2 | E | 0.005-0.040 (Serrarens uses 0.02 for another car) |
| Carrier + output gear inertia | 0.015 kg m^2 | E | 0.005-0.030 |
| Mesh stiffness at carrier | 1.0e6 N m/rad | E | 1e5-1e7 |
| Mesh damping ratio | 0.05 | E | 0.01-0.20 |
| Gear lash at input shaft | 1.6 deg | E | 0.5-5 (no usable source) |
| Gear ratios, final drive | 3.455 / 2.118 / 1.444 / 1.129 / 0.894, 3.667 | S | existing `CarParams` (5-speed only) |
| Halfshaft lengths | 447 mm left, 681.2 mm right | SD | Golf II GTI parts data (research report 3.1) |
| Left shaft solid-equivalent diameter | 22 mm | E | 21-25 mm |
| Right/left stiffness ratio | 0.83 | E | 0.656 (same solid diameter) - 1.0 (right sized to match) |
| Shear modulus | 79.3 GPa | S | https://www.engineeringtoolbox.com/modulus-rigidity-d_946.html |
| Combined stiffness, open differential | `4 kL kR/(kL+kR)` = 129 N m/deg (7.4 kN m/rad) at defaults | D | the carrier torque splits equally, so the carrier lags the mean wheel angle by `T (1/kL + 1/kR)/4`; symmetric mode only |
| Shaft/joint loss factor | 0.01 at 4 Hz | E | 0.002-0.05; viscous `c = eta k / omega_ref` |
| Shaft/joint lash at wheel | 0.3 deg | E | 0.1-1.0 |
| Engine block roll inertia | 4.2 kg m^2 | E | 3-6 |
| Roll natural frequency | 11.6 Hz | E | 7-20 Hz (Gaudin ISMA 2018: https://past.isma-isaac.be/downloads/isma2018/proceedings/Contribution_151_proceeding_3.pdf ) |
| Roll damping ratio | 0.15 | E | 0.05-0.30 |
| Mesh efficiency | 0.90 | E (existing `drivetrainEfficiency`) | applied in the power-flow direction: driving needs more input torque, coasting returns less; the default path applies it differently and is untouched |
| Tire longitudinal relaxation | `relaxationLength` 0.45 m | E (existing) | first-order slip-ratio relaxation, same form as the existing slip-angle relaxation, only while the toggle is on |
| Micro-step | fastest mode advances at most 0.1 rad per micro-step | D | step-size convergence test (0.1 vs 0.05 rad: identical to 4 digits); about 40 micro-steps per 0.5 ms outer step |

## Not modeled / known limits
- Engine torque ripple is the minimal model above; no cycle-to-cycle variation, no valve-timing pressure details, no friction ripple.
- The damper is the only hysteresis element; no mount hysteresis beyond viscous damping; no clutch pre-damper separate from the main damper.
- Lash is a single dead zone per element, not per gear; reverse uses the same values.
- Brake torque is not predicted inside the micro-steps (the wheels are resynchronized to the tire integrator each 0.5 ms).
- All E values are unmeasured for this car. The model is a physics-structured plausibility model, not a validated reproduction of a Scirocco.
