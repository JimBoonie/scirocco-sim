# Experimental transmission and synchronizer model: sources, tags and bounds

Switch: Experimental tab, "Transmission and synchronizer model" (`CarParams.experimentalTransmissionEnabled`, default off), independent of the driveline toggle (`experimentalDrivelineEnabled`).
Off (both) = the default `Drivetrain` path, byte for byte (proof: per-step float-bit hash of the 15 diagnostic scenarios, see `CHANGELOG.md`). The research report with all derivations is `transmission_synchro_research.md` (kept in the shared notes folder).
Tags: **S** sourced, **SD** sourced-derived, **D** derived, **E** estimate (bounds given). Sourced values override estimates. Everything marked E must be replaced when a measurement exists (section "What to measure").

## Structure (new files in `Assets/Scripts/Runtime/Transmission/`)
| File | Role |
|---|---|
| `TransmissionInterfaces.cs` | The two-port: `ITransmission.Advance(TransmissionStepInput) -> TransmissionStepResult`. Input port = input-shaft node (speed, inertia, torque into the gear train); output port = differential carrier (speed). The result carries the kinematic tie (`lockedTotalRatioSigned`, 0 = free node), the external torques on both ports, the mesh efficiency and the lash. A future unifying refactor keeps these structs and merges the adapters. |
| `TransmissionModel.cs` | Gate state machine: Neutral, Releasing, Traveling, Synchronizing, FreeFlight, DogCrossing, Engaged, Grinding. Owns cone, blocking ring, dog crossing, reverse clash, ring wear state, counters and events (`GrindCount`, `ScratchCount`, `DoubleBumpCount`, `LastSynchronizationSeconds`, impact energy). No sounds: state and events only. |
| `SynchronizerPhysics.cs` | Pure equations: cone torque, blocking torque, minimum blocking friction, sync time, speed-dependent cone friction, capture angle, impact energy. |
| `ShiftLinkage.cs` | Hand to sleeve: mechanical advantage, efficiency, assumed driver forces and lever speed, gate travel times, dog retention force. |
| `GearSetGeometry.cs` | Ratios from tooth counts, loose-gear inertia from geometry, shaft of each loose gear, lash referred to the input shaft, mesh loss factor per gear. |
| `GearboxOilModel.cs` | Walther viscosity, spin loss anchored to a measured unloaded box, loose-gear drag, output bearing drag, lumped oil temperature. |
| `GearboxDragLoads.cs`, `LooseGearRattle.cs` | Drag on both ports, drag of loose gears 3-5, rattle of loose 1st and 2nd gears (lash, impact law, drag). |
| `TransmissionDriveAdapterA.cs` | Transmission on, driveline off: input-shaft node with the Coulomb-capped clutch retargeted to it, rigid wheels. |
| `TransmissionDriveAdapterB.cs` | Both on: the transmission inside the torsional chain (tie, external torques, efficiency, lash owned by the transmission). |
| `ExperimentalTransmission.cs` | Orchestrator called by `CarController`; keyboard auto-clutch assist. |
| `TransmissionParameters.cs` | Parameter asset (ScriptableObject); `Assets/Resources/TransmissionParameters.asset`. |
Additive hooks outside the new folder (all default to zero or off, so the other paths are unchanged): `DrivelineStepInput` gets `inputShaftExternalTorque`, `carrierExternalTorque`, `overrideMeshLash`, `meshHalfLashAtInputShaftRadians`; `TorsionalDriveline` adds them in two lines and applies the lash override; `CarController` has one dispatch branch and one Deactivate call; `ExperimentalSettingsTab` row 5; `DebugHUD` tag.

## Four toggle combinations
| Transmission | Driveline | What runs |
|---|---|---|
| off | off | Default `Drivetrain.Clutch` + `Engine.Step`, untouched. |
| on | off | Adapter A: input node J = clutch disc + shaft assembly; clutch coupling retargeted to the node with the physical pedal curve; while tied the node is rigid with the wheels (`WheelCorner.DriveInertia` carries its reflected inertia); no lash in gear and no clutch stick state (penalty coupling, documented limit). |
| off | on | Existing driveline model with its own lumped lash and instant gear selection (unchanged). |
| on | on | Adapter B: torsional chain; its `inputShaftInertia` is replaced by the transmission's (one owner per inertia); chain mesh lash replaced by the transmission's lash (one owner of lash); chain efficiency input = the transmission's per-gear mesh efficiency; all synchronizer, drag and rattle torques enter as external torques. |

## Physical model (equations)
- Speed error at the input shaft `d_omega = omega_in - i_k omega_pinion` (reverse: `omega_in + i_R omega_pinion`). Cone sliding speed `R_C d_omega kappa_k` with `kappa_k = 1` (3rd-5th, cone on the input shaft) or `1/i_k` (1st, 2nd, cone on the output shaft).
- Cone torque `T_K = mu(v) F_a R_C / sin(alpha)` (Schaeffler API 06); `mu(v)` interpolates the INA brass-on-steel boundary curve (higher at low speed). Blocking torque `M_S = F_a R_S f(beta, mu_S)` (Schaeffler eq. 11). Blocking holds while `T_K >= M_S`; otherwise the ring is passed (scratch) and the sleeve reaches the dogs unsynchronized.
- Torque on the input node `-sign(d_omega) T_K kappa_k` (limited to what removes the error in one step), on the carrier `+sign(d_omega) T_K kappa_k i_k FD`. Karnopp dead band `T_K kappa_k dt / J` sticks the cone; a stuck cone ties input and output kinematically. The ring unblocks only when the torque the cone still carries (`|T_in| / kappa_k`) is below `M_S`; above `T_K` the cone slips again. So a partly engaged clutch drags the input toward engine speed (narrower speed error), its inertia adds to the node (slower shift), and too much clutch torque holds the ring blocked (refusal).
- Dog crossing: the teeth slip past by `|d_omega| t_cross`; if that exceeds the chamfer capture angle (fraction of the dog pitch) the dogs clash (impact energy `1/2 J d_omega^2`, sleeve thrown back, the driver pushes again: grinding). If the dogs are mis-indexed (hub-to-gear angle phase, integrated continuously) the chamfers index them: double bump, crossing time doubled. Reverse has no cone: same clash test on the idler teeth at the input gear pitch.
- Release from gear: the gear leaves only when the assumed driver pull force at the sleeve (`MA eta F_pull`) is at least `F_detent + |T_dogs| / R_dog (mu_dog + tan gamma)`: stays in gear under load, leaves near zero torque (lift-off, or clutch in).
- Losses: mesh `eta = prod (1 - H_V mu_mz)` per gear (ISO/TR 14179-2 form), applied on power and divided on overrun (no second factor anywhere on the transmission path); spin loss `T = T_ref s (omega/omega_ref)^p (nu/nu_ref)^q` anchored to Szweda 2008 (p from the 1000/4000 rpm points, q from the 30 -> 80 C drop), minus what the five loose gears already own; loose-gear drag = needle floor + viscous shear; output bearing preload drag (clamped so it never reverses the carrier within a step).
- Rattle: loose 1st/2nd driven gear on the stationary output shaft, DOF with lash `x in [-h, h]`, restitution impact (reduced inertia), drag, stick latch. Onset (derived): `J_g |alpha| / i > T_drag`; measured onset reported by `TransmissionTests.RattleOnsetFollowsInertiaTorqueVersusDrag`.
- Keyboard auto-clutch assist (only with the transmission on): the pedal opens in `autoClutchOpenSeconds` when a shift starts, stays open until the requested gear is engaged, then closes in `autoClutchCloseSeconds`; the existing launch law is the floor and is unchanged.

## Parameters (generated from `TransmissionParameters.cs`; values in the asset `Assets/Resources/TransmissionParameters.asset`)

Tags: **S** sourced, **SD** sourced-derived, **D** derived, **E** estimate (bounds given). Every row is a value in the parameter asset; none is inline in code.


### Gear set (tooth counts, driven : driving) and shafts

| Parameter = default | Unit, tag, bounds, source |
|---|---|
| `drivenToothCount = { 38, 36, 39, 35, 42 }` | - (SD: brokevw 020 ratios, match Bentley table 8-44 rounded ratios) |
| `drivingToothCount = { 11, 17, 27, 31, 47 }` | - (SD: same) |
| `reverseInputToothCount = 12, reverseIdlerToothCount = 20, reverseDrivenToothCount = 38` | - (SD: brokevw; idler 20, sleeve gear 38 on the 1st/2nd sleeve) |
| `finalDrivePinionToothCount = 18, finalDriveRingToothCount = 66` | - (SD: 66:18 = 3.667, Bentley FD 3.67) |
| `gearIsLooseOnInputShaft = { false, false, true, true, true }` | - (S: Mk1 pages: 1st/2nd driven gears loose on the output shaft, 3rd-5th drive gears loose on the input shaft) |
| `helixAngleDegrees = 20f` | deg (E: bounds 15..30; helical forward gears, VW 020 family) |
| `normalModuleMillimeters = 2.0f` | mm (E: bounds 1.75..2.5; every pair the same) |
| `gearFaceWidthMillimeters = 18f` | mm (E: bounds 14..24) |
| `normalPressureAngleDegrees = 20f` | deg (S: standard transmission gears; E for the 020, bounds 17.5..22.5) |
| `normalBacklashMillimeters = 0.12f` | mm normal backlash of every mesh (E: bounds 0.05..0.20; measure with a dial indicator) |
| `gearFillFactor = 0.6f` | - fraction of the solid disc inertia that a web gear has (E: bounds 0.5..1.0) |
| `steelDensityKilogramsPerCubicMeter = 7850f` | kg/m^3 (S: textbook) |

### Synchronizer geometry and friction (single cone, one blocking ring per gear)

| Parameter = default | Unit, tag, bounds, source |
|---|---|
| `coneHalfAngleDegrees = 6.5f` | deg (E: bounds 6.0..7.0; family S 6..7 deg, Schaeffler API 06 p. 18) |
| `meanConeRadiusMeters = { 0.038f, 0.036f, 0.034f, 0.032f, 0.030f }` | m (E: bounds 0.025..0.040) |
| `blockingToothToConeRadiusRatio = 1.176f` | - R_S / R_C, mean radius of the ring/sleeve roof teeth over the cone radius (E: bounds 1.0..1.43, i.e. R_C/R_S 0.7..1.0) |
| `roofIncludedAngleDegrees = { 110f, 110f, 110f, 110f, 110f }` | deg (S for 1st: Mk1 fig. 2 "110 deg tooth angle"; E for 2nd..5th, bounds 100..120) |
| `blockingToothFrictionCoefficient = 0.10f` | - roof tooth / sleeve chamfer friction (E: bounds 0.08..0.15) |
| `coneFrictionSlidingSpeedsMetersPerSecond = { 0f, 0.5f, 1.0f, 3.5f }` | m/s (S: INA/Schaeffler API 06 plot abscissa) |
| `coneFrictionCoefficientAtSpeed = { 0.17f, 0.13f, 0.115f, 0.11f }` | - (SD: base mineral oil band read from the plot, +-0.01; brass ring on case-hardened cone) |
| `coneFrictionScale = 1.0f` | - multiplies the curve for the actual oil/ring (E: bounds 0.55..1.10; 0.085/0.115 = friction-modified oil, 1.0 = INA base-oil curve) |
| `ringToConeAxialGapMeters = { 0.0014f, 0.0014f, 0.00145f, 0.0016f, 0.0016f }` | m (S: Mk1 new 1.1..1.7 / 1.15..1.75 / 1.3..1.9 mm; mid-range of the new band, wear limit 0.5 mm) |
| `ringGapWearLimitMeters = 0.0005f` | m (S: Mk1, all gears) |

### Dog teeth (positive engagement) and reverse idler

| Parameter = default | Unit, tag, bounds, source |
|---|---|
| `dogToothRadiusMeters = 0.030f` | m mean radius of the sleeve/gear dog teeth (E: bounds 0.025..0.040) |
| `dogToothCount = 30` | - teeth around the hub (E: bounds 24..40) |
| `dogFlankFrictionCoefficient = 0.10f` | - steel on steel in oil (E: bounds 0.06..0.15) |
| `dogRetentionUndercutDegrees = 3.0f` | deg back-taper that holds the gear in (E: bounds 0..5) |
| `dogAngularLashDegrees = 1.5f` | deg total dog play at the hub (E: bounds 1..4) |
| `dogChamferCaptureFraction = 0.25f` | - fraction of the tooth pitch the chamfers can index while the sleeve crosses (E: bounds 0.1..0.4) |
| `dogChamferTravelMeters = 0.003f` | m sleeve travel through the chamfer (E: bounds 0.002..0.005) |
| `postUnblockSleeveTravelMeters = 0.002f` | m sleeve travel from the unblocked ring to the dog chamfer (E: bounds 0.001..0.004) |
| `reverseToothCaptureFraction = 0.25f` | - fraction of the idler tooth pitch the straight-tooth chamfers can index (E: bounds 0.1..0.4) |
| `reverseChamferTravelMeters = 0.004f` | m idler slide through the chamfer (E: bounds 0.002..0.006) |
| `impactRestitution = 0.3f` | - coefficient of restitution of a tooth impact in oil (E: bounds 0.1..0.6) |

### Shift linkage and driver

| Parameter = default | Unit, tag, bounds, source |
|---|---|
| `handToSleeveMechanicalAdvantage = 8f` | - sleeve force / hand force and knob travel / sleeve travel (E: bounds 5..15) |
| `linkageEfficiency = 0.8f` | - (E: bounds 0.6..0.95; plastic rod ends, bushings) |
| `driverPushForceNewtons = 30f` | N hand force while pushing into gear (E: bounds 10..60; Giacomin and Mackenzie perceptual anchors 4.9..49 N) |
| `driverPullForceNewtons = 20f` | N hand force while pulling out of gear, the assumed driver arm force (E: bounds 10..50) |
| `knobPushSpeedMetersPerSecond = 0.4f` | m/s lever speed of a normal shift (E: bounds 0.2..0.8) |
| `neutralToRingContactKnobTravelMeters = 0.03f` | m knob travel from the neutral gate to the blocking ring contact (E: bounds 0.02..0.05) |
| `railSpacingKnobMeters = 0.04f` | m knob travel between the 1/2, 3/4 and 5/R gates (E: bounds 0.03..0.06) |
| `detentForceAtSleeveNewtons = 40f` | N spring-ball detent force the driver also overcomes when leaving a gear (E: bounds 15..80) |

### Input side inertia and sleeves

| Parameter = default | Unit, tag, bounds, source |
|---|---|
| `inputShaftAssemblyInertiaKilogramMetersSquared = 0.012f` | kg m^2 shaft, fixed gears, hubs, reverse gear at the input speed, WITHOUT the loose 1st/2nd gears and WITHOUT the clutch disc (E: bounds 0.004..0.030) |
| `carrierSideDragInertiaKilogramMetersSquared = 0.5f` | kg m^2 inertia that the output-side bearing drag clamp assumes (D: two driven wheels, 2 x 0.25 reflected to the wheels; only limits the clamp, not the dynamics) |

### Losses

| Parameter = default | Unit, tag, bounds, source |
|---|---|
| `looseGearRollingDragNewtonMeters = 0.05f` | N m needle bearing drag floor at a loose gear (E: bounds 0.02..0.30) |
| `looseGearViscousDragNewtonMetersSecondPerRadian = 2.0e-4f` | N m s/rad at the reference oil viscosity (E: bounds 5e-5..1e-3) |
| `meshFrictionCoefficient = 0.05f` | - mean tooth friction mu_mz (E: bounds 0.03..0.10) |
| `meshContactRatio = 1.4f` | - transverse contact ratio of every pair (E: bounds 1.2..1.8) |
| `outputPinionBearingTurningTorqueNewtonMeters = 0.30f` | N m used taper pair at the pinion shaft (S: Mk1, used >= 30 N cm) |
| `differentialBearingTurningTorqueNewtonMeters = 0.30f` | N m used taper pair of the differential (S: Mk1, used >= 30 N cm) |
| `spinLossReferenceTorqueNewtonMeters = 1.43f` | N m unloaded input torque of a six-speed FWD box at the reference speed and oil temperature (SD: Szweda 2008, 0.15 kW at 1000 rpm in 2nd, 80 C) |
| `spinLossReferenceSpeedRevolutionsPerMinute = 1000f` | rpm (SD: same) |
| `spinLossSecondSpeedRevolutionsPerMinute = 4000f` | rpm (SD: same) |
| `spinLossSecondTorqueNewtonMeters = 2.91f` | N m (SD: same, 1.22 kW at 4000 rpm) |
| `spinLossReferenceOilTemperatureKelvin = 353f` | K (SD: same, 80 C) |
| `spinLossColdOilTemperatureKelvin = 303f` | K (SD: same, 30 C) |
| `spinLossDropFromColdToReference = 0.465f` | - fraction of the spin loss gone between 30 and 80 C (SD: Szweda, 44..49 %) |
| `spinLossScaleForThisGearbox = 0.35f` | - 020 has two shafts and 2.0 L of oil versus the six-speed 3.1 L box (E: bounds 0.2..1.0) |

### Gear oil (75W-90 GL-4, 2.0 L)

| Parameter = default | Unit, tag, bounds, source |
|---|---|
| `oilKinematicViscosityAt40CMillimetersSquaredPerSecond = 100f` | mm^2/s (E: bounds 85..120, typical 75W-90) |
| `oilKinematicViscosityAt100CMillimetersSquaredPerSecond = 15f` | mm^2/s (E: bounds 13..17) |
| `oilMassKilograms = 1.76f` | kg (SD: 2.0 L at 0.88 kg/L) |
| `oilSpecificHeatJoulesPerKilogramKelvin = 2000f` | J/(kg K) (S: mineral oil, 1900..2100) |
| `casingThermalConductanceWattsPerKelvin = 10f` | W/K oil to ambient through the casing (E: bounds 5..25) |

### Auto-clutch assist (keyboard) while the transmission model is on

| Parameter = default | Unit, tag, bounds, source |
|---|---|
| `autoClutchOpenSeconds = 0.08f` | s pedal travel time when a shift starts (E: bounds 0.05..0.2) |
| `autoClutchCloseSeconds = 0.35f` | s pedal travel time after the gear is in (E: bounds 0.15..0.8) |

### Numerics

| Parameter = default | Unit, tag, bounds, source |
|---|---|
| `stickDeadBandSafetyFactor = 1f` | - multiplies the Karnopp dead band T dt / J (D: 1 = the largest speed change one step can produce) |

## Sources
- Schaeffler/INA, Synchronizer systems (API 06): https://www.schaeffler.com/remotemedien/media/_shared_media/08_media_library/01_publications/schaeffler_2/api/downloads_13/api06_de_en.pdf (cone torque, blocking torque, cone angle family 6-7 deg, friction curve vs sliding speed).
- Farokhi Nejad et al. 2017, synchronizer test rigs (SC-74: d 74 mm, mu 0.06, 1400 N, J 0.17 kg m^2, 1000 rpm, 0.69 s measured) - used as the formula check.
- Chalmers 2018 (Medleri Hire Math & Lund): https://publications.lib.chalmers.se/records/fulltext/255874/255874.pdf (sync time eqs. 2.174-2.176, mesh loss eqs. 2.157-2.159, bearing model).
- Szweda, Ohio State MS thesis 2008 (six-speed FWD box unloaded spin loss and efficiency map): anchor of the spin loss and its temperature drop.
- Lovas et al. 2006 (phases of a shift, double bump), Kim et al. 2003 (driver lever model, chamfer contact), Giacomin and Mackenzie (shift force perceptual anchors): https://bura.brunel.ac.uk/bitstream/2438/1444/1/gm2001hst.pdf
- Singh, Xie, Comparin, J. Sound Vib. 131(2) 1989 (neutral rattle): https://doi.org/10.1016/0022-460x(89)90485-9 ; Theodossiades, Tangasawi, Rahnejat, J. Sound Vib. 303 2007 (idle rattle with lubricated impacts).
- VW 020 gearbox: Bentley (VW Golf/Jetta 1985-92) section 8; Mk1 workshop pages (ring gaps, 1st-gear ring 110 deg, bearing turning torques): https://workshop-manuals.com/volkswagen/golf-mk1/power_transmission/5_speed_manual_gearbox_020/ ; tooth counts and oil http://www.brokevw.com/020ratios.html , http://brokevw.com/020oil.html , reverse http://www.brokevw.com/gear_selection.html

## What is sourced and what is not
Sourced (S/SD): tooth counts and ratios, which gears are loose on which shaft, single-cone with one ring per gear, ring gap new range and wear limit, 1st-gear ring roof angle, bearing turning torques (used limit), cone friction curve shape (plot read), spin-loss anchor figures (for a different six-speed box), oil volume, equations.
Estimated (E, the bulk): cone and tooth radii, other roof angles, roof and dog friction, dog geometry and lash, mesh backlash, module/helix/width/fill (gear inertias), input assembly inertia, linkage ratio and efficiency, driver forces and lever speed, detent force, drag floors, mesh friction, restitution, oil viscosity points, casing conductance, auto-clutch pedal times. No measured 020 lash, inertia, friction, sync time or shift force was found anywhere.

## What to measure (each lifts an E to S)
Ring gap and wear per gear, cone and tooth radii and roof angles, loose-gear spin-down, lash at the input shaft per gear, linkage ratio and stiffness, detent forces, gear and shaft weights and center distance, oil datasheet viscosity points, dog tooth count and chamfers.

## Known limits of this build
No lever force feedback or shift force output (T4 of the plan not done); no ring temperature or wear evolution (the ring gap is a settable state, constant in time); no 5th-to-reverse lockout plunger; squeeze-film delay before the cone engages is not modeled; adapter A has no lash and no clutch stick; adapter B takes the gear-train torque for the release test from the mean clutch torque; axial thrust of helical gears is not modeled; the reverse idler and 12-tooth gear dog test uses the input-referred speed error only.
