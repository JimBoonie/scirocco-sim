// Tire thermal + inflation-pressure model (settings option "Tire thermal model", default OFF -> handling identical to before).
// Lumped multi-node model in the TRT / TRT-EVO family (Farroni et al. 2014 / 2019): per tire 3 tread-surface zones (inner/mid/outer),
// tread bulk, carcass (belts+plies+sidewalls+liner), inflation gas, rim. Heat sources: friction power -F.Vs (split tire/road by thermal
// effusivity, Blok) and rolling-resistance hysteresis F_rr.V. Sinks: forced+natural convection to air, transient conduction into the road,
// internal convection carcass<->gas<->rim. Pressure: ideal gas at constant volume. Grip: road-test relation (Tambellini 2019) below ~151 F,
// Alcazar Vargas et al. 2022 measured mu(T) above, on the tread-mean temperature
// (summer passenger tire); pressure effects: MF6.1 terms (Besselink/Schmeitz/Pacejka 2010, TNO example coefficients).
// Every constant carries a tag: S sourced, SD sources disagree, D derived, E estimated. Sources: tire_thermal_research.md.
using UnityEngine;
public struct TireScale
{   // multipliers on the Pacejka terms; One = exact identity (x*1f is exact in IEEE, so thermal-off results are bit-identical)
    public float longitudinalStiffness, longitudinalFriction, lateralStiffness, lateralStiffnessLoadSaturation, lateralFriction, rollingResistance;
    public static readonly TireScale One = new TireScale { longitudinalStiffness = 1f, longitudinalFriction = 1f, lateralStiffness = 1f, lateralStiffnessLoadSaturation = 1f, lateralFriction = 1f, rollingResistance = 1f };
}
public class TireThermal
{
    // ---- fixed environment (Taylor: 66 F air, 70 F track) ----
    public const double AmbientTemperature = 18.9, TrackTemperature = 21.1;                     // degC (given)
    public const double AtmosphericPressure = 101325.0;                                 // Pa, standard atmosphere (S)
    // ---- geometry: 185/60R14 (S: ETRTO size code -> section width 185 mm, sidewall 111 mm, rim 14 in) ----
    public const double SectionWidth = 0.185, RimRadius = 0.1778, UnloadedRadius = 0.2888;
    public const double TreadWidth = 0.145;      // m tread (contact) width, ~78% of section width (E)
    public const double TreadDepth = 0.008;  // m new-tire tread depth (E: typical new passenger tire 8 mm)
    public const double SkinThickness = 0.001;       // m surface-node thickness (E, model resolution; ~ depth heated in a few s, see research doc)
    public const double CarcassThickness = 0.005;       // m crown carcass under the tread (belts + plies + liner) (E)
    public const double TireMass = 7.6;      // kg OE 185/60HR14 (S wheel-size.it)
    public const double RimWidth = 0.152;        // m 6J rim width (E: 6Jx14 assumed for the GT/GTI alloy)
    public const double RimMass = 6.0;       // kg 14x6 cast alloy (E)
    // ---- materials ----
    public const double RubberConductivity = 0.282, RubberSpecificHeat = 1741.0;     // W/mK, J/kgK tire rubber, Hot Disk at 28 C (S, PMC9182718)
    public const double RubberDensity = 1125.0;                 // kg/m3 tread compound, midpoint of 1094-1157 measured (S range)
    public const double CompoundMassFraction = 0.78, SteelMassFraction = 0.165, TextileMassFraction = 0.055;   // passenger tire mass fractions (S WRAP 2006; steel SD 14-16.5%)
    public const double SteelSpecificHeat = 490.0, TextileSpecificHeat = 1700.0, AluminiumSpecificHeat = 900.0;      // J/kgK (S handbook; textile = nylon 6)
    public const double AsphaltConductivity = 1.6, AsphaltDensity = 2290.0, AsphaltSpecificHeat = 884.0;        // asphalt (SD: k 0.74-2.88; Aston DBM 1.33-1.91 -> 1.6)
    public const double ContactRatio = 0.63;  // real/nominal tire-asphalt contact area (SD: 61-66% pressure film, MDPI Materials 2023; 5-10% older)
    public const double InnerConvectionCoefficient = 25.0;        // W/m2K internal convection liner/rim <-> gas (E, 10-100 plausible; checked vs measured pressure rise)
    public const double AirSpecificHeatConstantVolume = 718.0, AirGasConstant = 287.05;   // J/kgK (S)
    public const double AirConductivity = 0.0263, AirKinematicViscosity = 15.89e-6, AirPrandtlNumber = 0.707;   // air at 300 K (S Incropera Table A.4)
    public const double RollingLossTreadShare = 0.44;     // share of rolling-resistance loss in the tread (SD: tread 44% Tire Review; crown 70% Technia)
    // ---- grip vs temperature (S: Alcazar Vargas et al., Sensors 2022, Austone SP-6 205/65R15, Eq. mu = mumax + 1 - cosh((T-Topt)/Tdisp)) ----
    public const double PeakFriction = 1.1, OptimalTemperature = 88.0, TemperatureDispersion = 50.0;
    public const double BenchFitLowTemperature = 44.6;        // degC: bench fit gives 0.70 = lowest measured mu (D); bench curve held below (BenchFriction only)
    // ---- cold side from ROAD tests (option (b), Taylor 2026-10-13; replaces the 0.70 cold hold in FrictionAtTemperature) ----
    // Tambellini 2019 (Univ. Pisa / FCA, Balocco): ABS stops of 5 summer tires at road surface 15-58 C. Stopping distance grows
    // 0.211 %/C of road temperature on average (S; per tire 0.102-0.356 %/C, SD), referenced to 25 C (S, Balocco yearly mean road temp).
    // d = v^2/(2 mu g) -> d(mu)/mu = -d(d)/d (D). Road-tested tires run with the tread near road temperature (Linkoping diva2:1568991:
    // 11-36 C on the road, S), so the road temperature is taken as the bulk temperature (E). Below RoadReferenceTemperature: full grip held (S: Pokorski
    // 2019 summer 185/65R14, no deterioration down to ~7 C; ADAC/TUV/Tyre Reviews dry stops give mu 0.96-1.11 at road temperature).
    public const double RoadGripLossPerDegree = 0.00211;      // 1/degC relative mu loss per degC above RoadReferenceTemperature (S mean, SD spread)
    public const double RoadReferenceTemperature = 25.0;      // degC (S)
    // crossover where the rising bench curve meets the road relation (D, solved once by bisection): ~66 C
    public static readonly double CrossoverTemperature = SolveCrossoverTemperature();
    public const double BenchFitHighTemperature = 120.0;       // degC: highest measured zone temperature; held above (E)
    // ---- pressure (MF6.1; S coefficients of the TNO example tire, transferred to this tire: E) ----
    public const double NominalGaugePressure = 170000.0;      // Pa gauge, VW cold pressure 185/60R14 (SD: 1.7 bar three sources, 1.8 one)
    // MF6.1 pressure-effect coefficients: PPX1/PPX2 = longitudinal stiffness vs pressure change, PPX3/PPX4 = longitudinal peak friction; PPY1 = lateral stiffness,
    // PPY2 = lateral stiffness load saturation, PPY3/PPY4 = lateral peak friction; QSY8 = rolling-resistance pressure exponent
    public const double PPX1 = -0.3485, PPX2 = 0.37824, PPX3 = -0.09603, PPX4 = 0.06518;
    public const double PPY1 = -0.6255, PPY2 = -0.06523, PPY3 = -0.16666, PPY4 = -0.2811, QSY8 = -0.4089;

    // ---- derived constants (D) ----
    static readonly double TreadCircumference = 2.0 * System.Math.PI * (UnloadedRadius - 0.5 * TreadDepth);
    static readonly double ZoneArea = TreadWidth / 3.0 * TreadCircumference;                                 // outer area of one surface zone
    public static readonly double TreadMass = RubberDensity * TreadWidth * TreadCircumference * TreadDepth;         // 2.34 kg
    public static readonly double SkinHeatCapacity = RubberDensity * ZoneArea * SkinThickness * RubberSpecificHeat;                // J/K per zone
    public static readonly double BulkHeatCapacity = (TreadMass - 3.0 * RubberDensity * ZoneArea * SkinThickness) * RubberSpecificHeat;
    static readonly double CarcassCompoundMass = CompoundMassFraction * TireMass - TreadMass;
    public static readonly double CarcassHeatCapacity = CarcassCompoundMass * RubberSpecificHeat + SteelMassFraction * TireMass * SteelSpecificHeat + TextileMassFraction * TireMass * TextileSpecificHeat;
    public static readonly double RimHeatCapacity = RimMass * AluminiumSpecificHeat;
    static readonly double CavityHeight = UnloadedRadius - TreadDepth - CarcassThickness - RimRadius;                        // cavity height
    public static readonly double GasVolume = 0.85 * (SectionWidth - 0.015) * CavityHeight * 2.0 * System.Math.PI * (RimRadius + 0.5 * CavityHeight);  // m3 (D, 0.85 = rounded section, E)
    static readonly double SidewallArea = 2.0 * System.Math.PI * (UnloadedRadius * UnloadedRadius - RimRadius * RimRadius);      // both sidewalls, outer
    static readonly double CarcassInnerArea = 2.0 * System.Math.PI * (UnloadedRadius - TreadDepth - CarcassThickness) * SectionWidth * 0.9 + SidewallArea;
    static readonly double RimInnerArea = 2.0 * System.Math.PI * RimRadius * RimWidth, RimOuterArea = 2.0 * RimInnerArea;   // outer exposed rim ~2x (E)
    static readonly double SkinBulkConductance = RubberConductivity * ZoneArea / (0.5 * SkinThickness + 0.5 * (TreadDepth - SkinThickness));
    static readonly double BulkCarcassConductance = RubberConductivity * TreadWidth * TreadCircumference / (0.5 * (TreadDepth - SkinThickness) + 0.5 * CarcassThickness);
    static readonly double LateralConductance = RubberConductivity * SkinThickness * TreadCircumference / (TreadWidth / 3.0);
    static readonly double TireEffusivity = System.Math.Sqrt(RubberConductivity * RubberDensity * RubberSpecificHeat), RoadEffusivity = System.Math.Sqrt(AsphaltConductivity * AsphaltDensity * AsphaltSpecificHeat);   // thermal effusivity sqrt(k rho c) in W s^0.5 / (m^2 K) of tire rubber and asphalt (S: material data)
    // friction-heat share into the tire (SD): 1.0 = rubber friction is mostly viscoelastic hysteresis, dissipated inside the rubber
    // (Persson 2006 flash-temperature theory; TRT puts all friction power into the tread surface). Interface (Blok) split would be
    // e_t/(e_t+e_r) = 0.29 (BlokShare) -> bench replica only 43 C vs 100-120 C measured; 1.0 gives 116 C (see research doc).
    public static readonly double TireHeatShare = 1.0, BlokShare = TireEffusivity / (TireEffusivity + RoadEffusivity);
    static readonly double EffectiveEffusivity = TireEffusivity * RoadEffusivity / (TireEffusivity + RoadEffusivity);

    // ---- state ----
    public readonly double[] surfaceTemperatures = new double[3];   // surface zones 0 inner, 1 mid, 2 outer (degC)
    public double bulkTemperature, carcassTemperature, gasTemperature, rimTemperature;   // degC: tread bulk, carcass, inflation gas, rim
    public double coldGaugePressure, coldTemperature = AmbientTemperature, gasMassKg;   // Pa gauge at set-up, degC at set-up, kg of inflation gas
    public double gaugePressure;                            // current gauge pressure, Pa
    public double bulkFrictionPower;                         // W of the friction power dissipated as tread deformation hysteresis in the bulk (experimental compound; 0 = Default)
    public readonly double[] zoneLoadShare = { 1.0 / 3, 1.0 / 3, 1.0 / 3 };
    // energy bookkeeping (J): in = heat into the tire system, out = convection + road conduction; residual should stay ~0
    public double energyIn, energyOut, energyFrictionToRoad, initialStoredEnergy; readonly double[] surfaceTemperatureSteps = new double[3];

    // @description - creates the tyre thermal model at ambient (or warm) temperature and computes the inflation-gas mass for the cold pressure
    // @param {double} coldGauge - cold inflation pressure in Pa gauge
    // @param {bool} warm - true = start at the optimal grip temperature
    // @param {double} warmCarcass - carcass / gas / rim start temperature in degC when warm
    // @return {Void} - constructor
    public TireThermal(double coldGauge, bool warm = false, double warmCarcass = AmbientTemperature)
    {
        coldGaugePressure = coldGauge;
        for (int i = 0; i < 3; i++) surfaceTemperatures[i] = warm ? OptimalTemperature : AmbientTemperature;
        bulkTemperature = warm ? OptimalTemperature : AmbientTemperature; carcassTemperature = gasTemperature = rimTemperature = warm ? warmCarcass : AmbientTemperature;
        gasMassKg = (coldGaugePressure + AtmosphericPressure) * GasVolume / (AirGasConstant * (coldTemperature + 273.15));
        UpdatePressure(); initialStoredEnergy = StoredEnergy();
    }

    // @description - total heat content of all thermal nodes (sum of heat capacity x temperature) for the energy balance check
    // @return {double} - stored energy in J (relative to 0 degC)
    public double StoredEnergy() => SkinHeatCapacity * (surfaceTemperatures[0] + surfaceTemperatures[1] + surfaceTemperatures[2]) + BulkHeatCapacity * bulkTemperature + CarcassHeatCapacity * carcassTemperature + gasMassKg * AirSpecificHeatConstantVolume * gasTemperature + RimHeatCapacity * rimTemperature;

    // Energy balance residual: stored-energy change minus (heat in - heat out); should stay near 0.
    public double Residual => (StoredEnergy() - initialStoredEnergy) - (energyIn - energyOut);

    // @description - changes the cold inflation pressure (re-computes the gas mass at the cold temperature); ignores changes below 1 Pa
    // @param {double} gauge - new cold pressure in Pa gauge
    // @return {Void}
    public void SetColdPressure(double gauge) { if (System.Math.Abs(gauge - coldGaugePressure) < 1.0) return; coldGaugePressure = gauge; gasMassKg = (gauge + AtmosphericPressure) * GasVolume / (AirGasConstant * (coldTemperature + 273.15)); initialStoredEnergy = StoredEnergy() - (energyIn - energyOut); UpdatePressure(); }

    // @description - inflation pressure from the gas temperature: ideal gas at constant volume, p_abs = p_abs,cold T / T_cold (S)
    // @return {Void} - updates gaugePressure
    public void UpdatePressure() => gaugePressure = (coldGaugePressure + AtmosphericPressure) * (gasTemperature + 273.15) / (coldTemperature + 273.15) - AtmosphericPressure;   // ideal gas, constant volume (S)

    // outer convection: Churchill-Bernstein forced crossflow on a cylinder D = 2 R0, combined with Churchill-Chu natural convection (S)
    // @description - outer convection coefficient: Nu_forced (Churchill-Bernstein, cylinder in crossflow) and Nu_natural (Churchill-Chu) combined as (Nu_f^3 + Nu_n^3)^(1/3)
    // @param {double} speed - air speed over the tyre in m/s
    // @param {double} temperatureDifference - surface minus ambient temperature in K (sign ignored; floor 0.1 K)
    // @return {double} - heat transfer coefficient in W/(m^2 K)
    public static double OuterConvectionCoefficient(double speed, double temperatureDifference)
    {
        double diameter = 2.0 * UnloadedRadius, reynolds = System.Math.Max(speed, 0.0) * diameter / AirKinematicViscosity;
        double forcedNusselt = 0.3 + 0.62 * System.Math.Sqrt(reynolds) * System.Math.Pow(AirPrandtlNumber, 1.0 / 3) / System.Math.Pow(1 + System.Math.Pow(0.4 / AirPrandtlNumber, 2.0 / 3), 0.25) * System.Math.Pow(1 + System.Math.Pow(reynolds / 282000.0, 5.0 / 8), 4.0 / 5);   // forced convection over a cylinder, Churchill-Bernstein correlation (S), dimensionless Nusselt number
        double rayleigh = 9.81 / (AmbientTemperature + 273.15) * System.Math.Max(System.Math.Abs(temperatureDifference), 0.1) * diameter * diameter * diameter / (AirKinematicViscosity * AirKinematicViscosity / AirPrandtlNumber);
        double naturalNusselt = System.Math.Pow(0.60 + 0.387 * System.Math.Pow(rayleigh, 1.0 / 6) / System.Math.Pow(1 + System.Math.Pow(0.559 / AirPrandtlNumber, 9.0 / 16), 8.0 / 27), 2);   // natural convection, Churchill-Chu correlation (S), dimensionless Nusselt number
        return System.Math.Pow(forcedNusselt * forcedNusselt * forcedNusselt + naturalNusselt * naturalNusselt * naturalNusselt, 1.0 / 3) * AirConductivity / diameter;   // mixed convection: cube-root blend of forced and natural Nusselt numbers, times air conductivity / diameter = W/(m^2 K)
    }

    // @description - road conduction coefficient: two semi-infinite bodies brought into contact for t_c = L / V give the mean flux 2 e_eff dT / sqrt(pi t_c) (D)
    // @param {double} speed - rolling speed in m/s (floor 0.5)
    // @param {double} contactLength - contact patch length in m (floor 1 mm)
    // @return {double} - heat transfer coefficient in W/(m^2 K)
    public static double RoadConductionCoefficient(double speed, double contactLength) => 2.0 * EffectiveEffusivity / System.Math.Sqrt(System.Math.PI * System.Math.Max(contactLength, 1e-3) / System.Math.Max(speed, 0.5));   // transient conduction into the road for a contact time L/v: 2 e / sqrt(pi t), W/(m^2 K) (S: semi-infinite solid)

    // @description - splits the load over the three tread zones with an elastic foundation: deflection d0 + y tan(camber), y outward (D); normalised so the shares sum to 1
    // @param {double} verticalLoad - wheel load in N
    // @param {double} camberRad - static camber in rad (+ = top out, loads the outer shoulder)
    // @param {double} verticalRate - tyre vertical stiffness in N/m
    // @param {bool} onGround - false = no load, all shares 0
    // @return {Void} - updates zoneLoadShare
    void ComputeZoneLoadShares(double verticalLoad, double camberRad, double verticalRate, bool onGround)
    {
        double sum = 0; if (onGround && verticalLoad > 0)
        {
            double centreDeflection = verticalLoad / verticalRate;
            for (int i = 0; i < 3; i++) { double offset = (i - 1) * TreadWidth / 3.0; zoneLoadShare[i] = System.Math.Max(0.0, centreDeflection + offset * System.Math.Tan(camberRad)); sum += zoneLoadShare[i]; }
        }
        if (sum > 0) for (int i = 0; i < 3; i++) zoneLoadShare[i] /= sum; else for (int i = 0; i < 3; i++) zoneLoadShare[i] = 0;
    }

    // @description - advances all thermal nodes by one time step (explicit Euler): surface zones (friction in, convection, road conduction, to bulk, lateral),
    //   bulk, carcass, gas, rim; then updates the pressure and the energy bookkeeping.
    // @param {double} dt - time step in s
    // @param {double} frictionPower - total friction power -F.Vs at the patch in W (>= 0)
    // @param {double} rollingPower - rolling-resistance power in W
    // @param {double} speed - rolling speed in m/s
    // @param {double} verticalLoad - wheel load in N
    // @param {double} camberRad - standard camber in rad (+ = top out, loads the outer shoulder)
    // @param {double} verticalRate - tyre vertical rate in N/m
    // @param {bool} onGround - true while the tyre touches the road
    // @return {Void}
    public void Step(double dt, double frictionPower, double rollingPower, double speed, double verticalLoad, double camberRad, double verticalRate, bool onGround)
    {
        double absoluteSpeed = System.Math.Abs(speed);
        ComputeZoneLoadShares(verticalLoad, camberRad, verticalRate, onGround);
        double patchPressure = System.Math.Max(gaugePressure, 20000.0);
        double contactArea = onGround && verticalLoad > 0 ? verticalLoad / patchPressure : 0.0;                 // nominal contact area = Fz / inflation pressure (D, membrane approx.)
        double contactLength = contactArea / TreadWidth, roadCoefficient = RoadConductionCoefficient(absoluteSpeed, contactLength);
        double bulkHysteresisPower = System.Math.Min(System.Math.Max(bulkFrictionPower, 0.0), System.Math.Max(frictionPower, 0.0));   // 0 unless the experimental compound sets it
        double surfaceFrictionPower = TireHeatShare * (System.Math.Max(frictionPower, 0.0) - bulkHysteresisPower);
        double heatInToSurface = 0, heatOutOfSurface = 0;
        double heatSurfaceToBulk = 0;
        for (int i = 0; i < 3; i++)
        {
            double convectionHeat = OuterConvectionCoefficient(absoluteSpeed, surfaceTemperatures[i] - AmbientTemperature) * ZoneArea * (surfaceTemperatures[i] - AmbientTemperature);
            double roadHeat = zoneLoadShare[i] > 0 ? roadCoefficient * ContactRatio * contactArea / 3.0 * (surfaceTemperatures[i] - TrackTemperature) : 0.0;
            double toBulkHeat = SkinBulkConductance * (surfaceTemperatures[i] - bulkTemperature);
            double lateralHeat = (i > 0 ? LateralConductance * (surfaceTemperatures[i - 1] - surfaceTemperatures[i]) : 0) + (i < 2 ? LateralConductance * (surfaceTemperatures[i + 1] - surfaceTemperatures[i]) : 0);
            double frictionHeat = surfaceFrictionPower * zoneLoadShare[i];
            surfaceTemperatureSteps[i] = (frictionHeat - convectionHeat - roadHeat - toBulkHeat + lateralHeat) / SkinHeatCapacity * dt; heatSurfaceToBulk += toBulkHeat; heatInToSurface += frictionHeat; heatOutOfSurface += convectionHeat + roadHeat;
        }
        double bulkToCarcassHeat = BulkCarcassConductance * (bulkTemperature - carcassTemperature), carcassToGasHeat = InnerConvectionCoefficient * CarcassInnerArea * (carcassTemperature - gasTemperature), gasToRimHeat = InnerConvectionCoefficient * RimInnerArea * (gasTemperature - rimTemperature);
        double sidewallHeat = OuterConvectionCoefficient(absoluteSpeed, carcassTemperature - AmbientTemperature) * SidewallArea * (carcassTemperature - AmbientTemperature), rimOutHeat = OuterConvectionCoefficient(absoluteSpeed, rimTemperature - AmbientTemperature) * RimOuterArea * (rimTemperature - AmbientTemperature);
        double rollingHeat = System.Math.Max(rollingPower, 0.0);
        bulkTemperature += (heatSurfaceToBulk + TireHeatShare * bulkHysteresisPower + RollingLossTreadShare * rollingHeat - bulkToCarcassHeat) / BulkHeatCapacity * dt;
        carcassTemperature += ((1 - RollingLossTreadShare) * rollingHeat + bulkToCarcassHeat - carcassToGasHeat - sidewallHeat) / CarcassHeatCapacity * dt;
        gasTemperature += (carcassToGasHeat - gasToRimHeat) / (gasMassKg * AirSpecificHeatConstantVolume) * dt;
        rimTemperature += (gasToRimHeat - rimOutHeat) / RimHeatCapacity * dt;
        for (int i = 0; i < 3; i++) surfaceTemperatures[i] += surfaceTemperatureSteps[i];
        energyIn += (heatInToSurface + TireHeatShare * bulkHysteresisPower + rollingHeat) * dt; energyOut += (heatOutOfSurface + sidewallHeat + rimOutHeat) * dt; energyFrictionToRoad += (1 - TireHeatShare) * System.Math.Max(frictionPower, 0.0) * dt;
        UpdatePressure();
    }

    // grip temperature = tread BULK temperature (TRT EVO, Farroni et al. 2019: grip correlates best with bulk temperature, S; applying
    // that to this passenger tire and to the Alcazar Vargas mu(T) curve, which was fitted against zone/surface temperatures, is E).
    // The 1 mm surface zones follow every slide and cool within ~15 s on a straight; the bulk (C_bulk ~21x C_skin, D) integrates the
    // heat history, which is what the compound's viscoelastic state follows. Surface zones still drive heat flow and the display.
    // 2026-10-13 (option (b) + Taylor's 270 F report): grip temperature = TREAD MEAN temperature, i.e. the heat-capacity-weighted mean of
    // the three 1 mm surface zones and the 7 mm bulk (D: weights are the node capacities 3*C_skin : C_bulk = 507 : 3557 J/K, no free factor).
    // Basis: in the contact patch the whole tread block shears over its full height (brush model, Pacejka ch. 3, S), and hysteretic
    // friction dissipates in the rubber deformed by the road texture (Persson 2006, S), so the viscoelastic state that sets grip is that of
    // the deformed tread, not of the 1 mm skin alone (surface-only: 1 mm skin flashes past 248 F in a lock-up -> runaway, seen in the
    // warm threshold-brake run: 55.8 m vs 40.8 m) nor of the bulk alone (a 270 F skin over a 77 F bulk read 64 %). Applying the bench
    // curve (fitted vs IR surface temps, S) and the road relation (vs road temperature, S) to this mean is E.
    // @description - grip temperature: heat-capacity-weighted mean of the three surface zones and the bulk (see the note above)
    // @return {double} - grip temperature in degC
    public double GripTemp() => (SkinHeatCapacity * (surfaceTemperatures[0] + surfaceTemperatures[1] + surfaceTemperatures[2]) + BulkHeatCapacity * bulkTemperature) / (3.0 * SkinHeatCapacity + BulkHeatCapacity);

    // @description - surface (display) temperature: zone temperatures weighted by (T_i - T_ambient) (Alcazar Vargas Eq. 3-4, S); plain mean when all are at/below ambient
    // @return {double} - surface temperature in degC
    public double SurfaceTemp()
    {
        double weightedSum = 0, weightSum = 0; for (int i = 0; i < 3; i++) { double weight = System.Math.Max(surfaceTemperatures[i] - AmbientTemperature, 0); weightedSum += weight * surfaceTemperatures[i]; weightSum += weight; }
        return weightSum > 1e-6 ? weightedSum / weightSum : (surfaceTemperatures[0] + surfaceTemperatures[1] + surfaceTemperatures[2]) / 3.0;
    }

    // @description - bench curve alone (Alcazar Vargas 2022): mu = mu_max + 1 - cosh((T - T_opt) / T_disp), temperature held inside its measured range
    // @param {double} temperature - tread temperature in degC
    // @return {double} - friction coefficient
    public static double BenchFriction(double temperature) { temperature = System.Math.Min(System.Math.Max(temperature, BenchFitLowTemperature), BenchFitHighTemperature); return PeakFriction + 1.0 - System.Math.Cosh((temperature - OptimalTemperature) / TemperatureDispersion); }   // bench friction versus temperature: 1 - cosh curve around the optimum, peak at PeakFriction (D: fitted)

    // @description - road-test relation (Tambellini 2019 slope, full grip at/below the road reference temperature): mu = mu_max (1 - s (T - T_ref))
    // @param {double} temperature - road/tread temperature in degC
    // @return {double} - friction coefficient
    public static double RoadFriction(double temperature) => PeakFriction * (1.0 - RoadGripLossPerDegree * System.Math.Max(temperature - RoadReferenceTemperature, 0.0));

    // @description - grip vs temperature: road data below the crossover, bench curve above; continuous at the crossover (max of the two below the optimum)
    // @param {double} temperature - tread temperature in degC
    // @return {double} - friction coefficient
    public static double FrictionAtTemperature(double temperature) => temperature < CrossoverTemperature ? RoadFriction(temperature) : BenchFriction(temperature);

    // @description - crossover temperature where the rising bench curve meets the road relation (bisection to 1e-9 C; MuRoad - MuBench is > 0 at the low fit limit and < 0 at the optimum)
    // @return {double} - crossover temperature in degC
    static double SolveCrossoverTemperature()
    {
        double low = BenchFitLowTemperature, high = OptimalTemperature;
        for (int i = 0; i < 100; i++) { double middle = 0.5 * (low + high); if (RoadFriction(middle) > BenchFriction(middle)) low = middle; else high = middle; }
        return 0.5 * (low + high);
    }

    // @description - Pacejka multipliers: temperature acts on peak mu only (stiffness ~independent of T, S Vargas); MF6.1 pressure terms act on stiffness + mu.
    //   Pressure change dpi = (p - p_nom) / p_nom; e.g. longitudinalStiffness = 1 + PPX1 dpi + PPX2 dpi^2.
    // @return {TireScale} - multipliers for the longitudinal/lateral stiffness, peak friction and rolling resistance
    public TireScale Scale()
    {
        double temperatureFactor = FrictionAtTemperature(GripTemp()) / PeakFriction;     // = 1 at Topt: the base coefficients represent the tire at its optimum (D)
        double pressureChange = (gaugePressure - NominalGaugePressure) / NominalGaugePressure;
        return new TireScale
        {
            longitudinalStiffness = (float)(1 + PPX1 * pressureChange + PPX2 * pressureChange * pressureChange), longitudinalFriction = (float)(temperatureFactor * (1 + PPX3 * pressureChange + PPX4 * pressureChange * pressureChange)),
            lateralStiffness = (float)(1 + PPY1 * pressureChange), lateralStiffnessLoadSaturation = (float)(1 + PPY2 * pressureChange), lateralFriction = (float)(temperatureFactor * (1 + PPY3 * pressureChange + PPY4 * pressureChange * pressureChange)),
            rollingResistance = (float)System.Math.Pow(System.Math.Max(gaugePressure, 20000.0) / NominalGaugePressure, QSY8)
        };
    }

    // @description - temperature unit conversion
    // @param {double} celsius - temperature in degC
    // @return {double} - temperature in degF
    public static double CelsiusToFahrenheit(double celsius) => celsius * 9.0 / 5.0 + 32.0;
    public const double PaPerPsi = 6894.757;
}
