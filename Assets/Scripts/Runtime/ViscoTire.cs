// Experimental (viscoelastic) tread compound. Selectable in the Quick menu ("Tyre compound"); the Default compound never calls this file.
// Physics (sources and tags in tools/viscoelastic_tyre_research.md):
//  - complex modulus E*(w) of a carbon-black-filled E-SBR summer tread: Havriliak-Negami polymer relaxation + fractional filler network,
//    fitted (tools/visco_fit.py, D) to sourced DMA indicators; time-temperature superposition by WLF (universal constants about Tg, S).
//  - Payne effect: Kraus (1984) strain softening of the filler part at the asperity strain (Persson: strain ~ rms slope / 2).
//  - rubber friction = hysteretic (Persson 2001 theory, self-affine road, Lorenz/Persson 2015 rms-slope cutoff) + adhesive
//    (Lorenz/Persson real-contact shear stress, Gaussian in log sliding speed) -> master curve mu(log10(aT v)), precomputed tables.
//  - flashTemperature temperature of the macro-asperity contacts (Tian & Kennedy 1994 moving heat source) raises the friction temperature.
//  - calibration (D): grip factor g = mu(vs, T)/mu(vs_ref, T) multiplies the sourced peak coefficients; at the reference sliding speed
//    (peak slip at V_ref) g = 1 exactly, so the sourced peak mu and mu(T) of TireThermal are kept and nothing is added. Slip stiffness
//    (B = K/(C D)) is unaffected by g.
//  - rate-dependent tread stiffness, load-dependent relaxation length (Pacejka 2012 ch. 7: sigma = C_Falpha / c_Fy),
//    rolling resistance ~ tan d at the rolling frequency and tread/carcass temperature (referenced to the ISO 28580 condition),
//    tread deformation-hysteresis share of the friction power deposited in the tread bulk (brush model, Pacejka ch. 3).
using System.Numerics;
public static class ViscoTire
{
    // ---- polymer: cold emulsion SBR, 23.5 % styrene ----
    public const double Tg = -50.0;                 // glass-transition temperature in degC (S: AZoM E-SBR; SD: NETZSCH DSC midpoint -45, range -55..-35)
    public const double C1g = 17.44, C2g = 51.6;    // WLF universal constants C1, C2 referenced to Tg (S: Williams, Landel, Ferry 1955)
    // ---- complex modulus fit (D: tools/visco_fit.py to S targets tan d(0 C,10 Hz)=0.206, tan d(60 C,10 Hz)=0.160, Sae-oui et al. 2017;
    //      tan d peak 0.8 SD; |E*|(60 C,10 Hz) = 8 MPa E) ----
    public const double GlassyModulus = 2e9;        // Pa glassy modulus E_glass (SD: 1-3 GPa)
    public const double RubberyModulus = 3.224467e6;   // Pa polymer rubbery modulus E_0 (D)
    public const double HavriliakNegamiAlpha = 0.8, HavriliakNegamiBeta = 1.0;   // Havriliak-Negami shape exponents a, b (D: best of the fit grid)
    public const double LogRelaxationTimeAtTg = -2.610193;   // log10 of the HN relaxation time at Tg, tau_g in s (D)
    public const double FillerRatio = 1.5;          // filler-network / polymer modulus at small strain (SD: Payne ratio ~2.5, 50-80 phr CB)
    public const double FillerExponent = 0.160231;  // fractional exponent n of the filler network: tan d_filler = tan(n pi/2) (D)
    public const double FillerReferenceTemperature = 60.0, FillerReferenceFrequency = 10.0;   // degC, Hz: reference of the filler element (S DMA indicator condition)
    // ---- Payne effect (Kraus 1984 form, S; parameters E/SD) ----
    public const double PayneCriticalStrain = 0.01; // critical strain amplitude eps_c (SD: Heinrich & Kluppel 2002, ~1 %)
    public const double KrausExponent = 0.6;        // Kraus exponent m (SD: 0.5-0.6 fits for CB-filled rubbers)
    // ---- road surface (self-affine fractal) ----
    public const double HurstExponent = 0.8;        // H (SD: Persson asphalt 0.7-0.9)
    public const double RmsRoughness = 0.25e-3;     // m, h_rms of the macrotexture (E: track asphalt MPD 0.5-1 mm)
    public const double RollOffWavelength = 5e-3;   // m, lambda_0 roll-off wavelength (E)
    public const double CutoffRmsSlope = 1.3;       // xi: cumulative rms slope at the cutoff wavenumber q1 (S: Lorenz, Oh, Persson 2015, J Chem Phys 142)
    public const double PoissonRatio = 0.5;         // nu, rubber incompressible (S)
    public const double PerssonEnergyCorrection = 0.5;   // gamma in Persson's elastic-energy correction S = gamma + (1-gamma) P^2 (S: Persson 2001, gamma ~0.5)
    public static readonly double RollOffWavenumber = 2 * System.Math.PI / RollOffWavelength;   // q0 = 2 pi / lambda_0 in 1/m
    // q1: cutoff wavenumber (1/m) where the cumulative rms slope of the self-affine surface reaches CutoffRmsSlope (D)
    public static readonly double CutoffWavenumber = System.Math.Pow(CutoffRmsSlope * CutoffRmsSlope * (2 - 2 * HurstExponent) / (2 * HurstExponent * RmsRoughness * RmsRoughness * System.Math.Pow(RollOffWavenumber, 2 * HurstExponent)) + System.Math.Pow(RollOffWavenumber, 2 - 2 * HurstExponent), 1 / (2 - 2 * HurstExponent));   // D
    public static readonly double FrictionStrain = CutoffRmsSlope / 2;   // asperity strain ~ rms slope / 2 (S: Persson 2006)
    // ---- adhesion (S: Lorenz, Oh, Persson 2015 tread compound on road: tau_f = tau_f0 exp(-c log10^2(v/v0))) ----
    public const double AdhesionShearStress0 = 6.5e6, AdhesionWidthFactor = 0.1, AdhesionSpeed0 = 6e-3;   // tau_f0 in Pa, c (dimensionless), v0 in m/s (S; tau_f0 range 5.3-8.5 MPa SD)
    public const double AdhesionReferenceTemperature = 20.0;   // degC reference temperature of that master curve (E: room-temperature tests)
    // ---- reference condition of the calibration ----
    public const double ReferenceSpeed = 80.0 / 3.6;           // m/s (E: handling-test speed, step-steer / ramp-steer 80 km/h)
    public const double ReferenceContactPressure = TireThermal.NominalGaugePressure;   // Pa nominal contact pressure ~ inflation pressure (D: membrane approx., as TireThermal)
    public const double RollingResistanceReferenceTemperature = 25.0, RollingResistanceReferenceSpeed = 80.0 / 3.6;   // ISO 28580 rolling-resistance reference: 25 C, 80 km/h (S)
    public const double TreadShare = 0.5;           // tread (brush) share of the tyre's tangential compliance (E)
    // ---- flashTemperature temperature ----
    public static readonly double AsperityRadius = RollOffWavelength / 4;   // m macro-asperity contact radius (E: quarter roll-off wavelength)
    static readonly double ThermalDiffusivity = TireThermal.RubberConductivity / (TireThermal.RubberDensity * TireThermal.RubberSpecificHeat);   // kappa in m^2/s (D, S data)

    // Switches for the individual physics terms (ablation tests turn single ones off).
    public struct Flags { public bool hysteresis, adhesion, williamsLandelFerry, flashTemperature, payneEffect, rateDependentStiffness, loadRelaxation, rollingResistance, bulkHeating; }

    // @description - reads the experimental-compound switches from the car parameters
    // @param {CarParams} carParams - car parameters holding the ve* switches
    // @return {Flags} - the switches as a value struct
    public static Flags FromParams(CarParams carParams) => new Flags { hysteresis = carParams.viscoHysteresisEnabled, adhesion = carParams.viscoAdhesionEnabled, williamsLandelFerry = carParams.viscoWilliamsLandelFerryEnabled, flashTemperature = carParams.viscoFlashTemperatureEnabled, payneEffect = carParams.viscoPayneEffectEnabled, rateDependentStiffness = carParams.viscoRateStiffnessEnabled, loadRelaxation = carParams.viscoLoadRelaxationEnabled, rollingResistance = carParams.viscoRollingResistanceEnabled, bulkHeating = carParams.viscoBulkHeatEnabled };

    // @description - WLF time-temperature shift log10(aT) = -C1 (T - Tg) / (C2 + T - Tg); valid above Tg
    // @param {double} temperature - rubber temperature in degC
    // @return {double} - log10 of the shift factor aT (reduced time = time x aT)
    public static double LogTimeTemperatureShift(double temperature) => -C1g * (temperature - Tg) / (C2g + temperature - Tg);
    static readonly double FillerReferenceAngularFrequency = 2 * System.Math.PI * FillerReferenceFrequency * System.Math.Pow(10, LogTimeTemperatureShift(FillerReferenceTemperature));   // reference angular frequency of the filler network in rad/s, shifted to Tg with the WLF factor (aT = 10^LogTimeTemperatureShift)

    // @description - complex Young's modulus E*(w) = Havriliak-Negami polymer relaxation + fractional filler network (softened by the Payne effect)
    //   E_HN = E_glass + (E_0 - E_glass) / (1 + (i w tau_g)^a)^b;  filler = ratio E_0 pay (i w / w_ref)^n;  pay = 1 / (1 + (strain / eps_c)^(2 m))  (Kraus)
    // @param {double} reducedAngularFrequency - angular frequency in rad/s referenced to Tg
    // @param {double} strain - strain amplitude (dimensionless)
    // @param {bool} payneEffect - true = apply the Payne strain softening to the filler network
    // @return {Complex} - complex modulus in Pa (real = storage, imaginary = loss)
    public static Complex ComplexModulus(double reducedAngularFrequency, double strain, bool payneEffect)
    {
        reducedAngularFrequency = System.Math.Max(System.Math.Abs(reducedAngularFrequency), 1e-300);
        Complex polymer = GlassyModulus + (RubberyModulus - GlassyModulus) / Complex.Pow(1 + Complex.Pow(new Complex(0, reducedAngularFrequency * System.Math.Pow(10, LogRelaxationTimeAtTg)), HavriliakNegamiAlpha), HavriliakNegamiBeta);   // Havriliak-Negami polymer relaxation E_glass + (E_0 - E_glass) / (1 + (i w tau)^a)^b, in Pa
        double paynePrefactor = payneEffect && strain > 0 ? 1.0 / (1.0 + System.Math.Pow(strain / PayneCriticalStrain, 2 * KrausExponent)) : 1.0;   // Payne (Kraus) strain softening of the filler network, dimensionless 0..1
        Complex filler = FillerRatio * RubberyModulus * paynePrefactor * Complex.Pow(new Complex(0, reducedAngularFrequency / FillerReferenceAngularFrequency), FillerExponent);
        return polymer + filler;
    }

    // @description - loss tangent tan(delta) = E'' / E' of the unstrained compound
    // @param {double} frequencyHz - deformation frequency in Hz
    // @param {double} temperature - rubber temperature in degC
    // @return {double} - tan delta (dimensionless)
    public static double TanDelta(double frequencyHz, double temperature) { var modulus = ComplexModulus(2 * System.Math.PI * frequencyHz * System.Math.Pow(10, LogTimeTemperatureShift(temperature)), 0, false); return modulus.Imaginary / modulus.Real; }   // reduced angular frequency = 2 pi f x aT (rad/s); tan(delta) = E'' / E' (dimensionless)

    // @description - magnitude |E*| of the unstrained compound
    // @param {double} frequencyHz - deformation frequency in Hz
    // @param {double} temperature - rubber temperature in degC
    // @return {double} - |E*| in Pa
    public static double ModulusAbs(double frequencyHz, double temperature) => ComplexModulus(2 * System.Math.PI * frequencyHz * System.Math.Pow(10, LogTimeTemperatureShift(temperature)), 0, false).Magnitude;   // reduced angular frequency = 2 pi f x aT (rad/s); returns |E*| in Pa

    // ---- precomputed master curves on x = log10(aT v) (v in m/s, aT referenced to Tg) ----
    public const double MasterCurveMin = -18, MasterCurveMax = 4, MasterCurveStep = 0.05;
    public static readonly int MasterCurveSamples = (int)System.Math.Round((MasterCurveMax - MasterCurveMin) / MasterCurveStep) + 1;
    const int WavenumberSamples = 96, AngleSamples = 16;   // integration resolution (model resolution, checked: halving changes mu < 1 %)
    static double[][] hysteresisFrictionTable = new double[2][], contactAreaTable = new double[2][];   // index 0 = without, 1 = with the Payne effect

    // @description - road roughness power spectrum of the self-affine surface: C(q) = H/pi h_rms^2 / q0^2 (q/q0)^(-2(H+1))
    // @param {double} wavenumber - q in 1/m
    // @return {double} - spectral density C(q) in m^4
    static double RoughnessPowerSpectrum(double wavenumber) => HurstExponent / System.Math.PI * RmsRoughness * RmsRoughness / (RollOffWavenumber * RollOffWavenumber) * System.Math.Pow(wavenumber / RollOffWavenumber, -2 * (HurstExponent + 1));   // self-affine roughness spectrum C(q) in m^4 (S: Persson 2001; H Hurst exponent, q0 roll-off wavenumber)

    // @description - fills the hysteretic-friction and real-contact-area master curves (Persson 2001 theory) for one Payne setting
    // @param {int} payneIndex - 0 = no Payne softening, 1 = Payne softening on the filler network
    // @return {Void} - stores the tables in hysteresisFrictionTable / contactAreaTable
    static void BuildMasterCurves(int payneIndex)
    {
        bool payneEffect = payneIndex == 1; var frictionCurve = new double[MasterCurveSamples]; var areaCurve = new double[MasterCurveSamples];
        double[] wavenumbers = new double[WavenumberSamples], cosines = new double[AngleSamples], angleWeights = new double[AngleSamples];
        for (int i = 0; i < WavenumberSamples; i++) wavenumbers[i] = RollOffWavenumber * System.Math.Pow(CutoffWavenumber / RollOffWavenumber, i / (double)(WavenumberSamples - 1));   // wavenumbers spaced logarithmically from the roll-off wavenumber to the cutoff (1/m)
        for (int j = 0; j < AngleSamples; j++) { double angle = (j + 0.5) * 0.5 * System.Math.PI / AngleSamples; cosines[j] = System.Math.Cos(angle); angleWeights[j] = 0.5 * System.Math.PI / AngleSamples; }   // midpoint rule on [0, pi/2]
        double normalisingStress = (1 - PoissonRatio * PoissonRatio) * ReferenceContactPressure;
        var imaginaryIntegral = new double[WavenumberSamples]; var absoluteIntegral = new double[WavenumberSamples];
        for (int k = 0; k < MasterCurveSamples; k++)
        {
            double reducedSpeed = System.Math.Pow(10, MasterCurveMin + k * MasterCurveStep);   // master curve abscissa: sliding speed shifted by aT, log-spaced (m/s)
            for (int i = 0; i < WavenumberSamples; i++)
                AngleIntegrals(wavenumbers[i] * reducedSpeed, cosines, angleWeights, normalisingStress, payneEffect, out imaginaryIntegral[i], out absoluteIntegral[i]);
            IntegrateOverWavenumber(wavenumbers, imaginaryIntegral, absoluteIntegral, out frictionCurve[k], out areaCurve[k]);   // P(q1) = A/A0 real contact area
        }
        hysteresisFrictionTable[payneIndex] = frictionCurve; contactAreaTable[payneIndex] = areaCurve;
    }

    // @description - angular integrals of Im E* / (1 - nu^2) sigma0 (weighted by cos phi) and |E*/(1 - nu^2) sigma0|^2 over the sliding direction phi
    //   (full 0..2pi integrals by symmetry: factor 4 on the quarter range)
    // @param {double} wavenumberTimesSpeed - q v in rad/s (reduced)
    // @param {double[]} cosines - cos(phi) at the midpoint-rule nodes
    // @param {double[]} angleWeights - midpoint-rule weights
    // @param {double} normalisingStress - (1 - nu^2) sigma0 in Pa
    // @param {bool} payneEffect - Payne softening on/off
    // @param {double} imaginaryIntegral - (out) 4 sum w cos Im(E*)/s0
    // @param {double} absoluteIntegral - (out) 4 sum w |E*/s0|^2
    // @return {Void}
    static void AngleIntegrals(double wavenumberTimesSpeed, double[] cosines, double[] angleWeights, double normalisingStress, bool payneEffect, out double imaginaryIntegral, out double absoluteIntegral)
    {
        double sumImaginary = 0, sumAbsolute = 0;
        for (int j = 0; j < AngleSamples; j++) { var modulus = ComplexModulus(wavenumberTimesSpeed * cosines[j], FrictionStrain, payneEffect) / normalisingStress; sumImaginary += angleWeights[j] * cosines[j] * modulus.Imaginary; sumAbsolute += angleWeights[j] * (modulus.Real * modulus.Real + modulus.Imaginary * modulus.Imaginary); }
        imaginaryIntegral = 4 * sumImaginary; absoluteIntegral = 4 * sumAbsolute;
    }

    // @description - integrates Persson's theory over wavenumber (trapezoid rule): G(q) = 1/8 int q^3 C |E|^2 dq, P = erf(1 / (2 sqrt G)),
    //   mu = 1/2 int q^3 C P (gamma + (1 - gamma) P^2) Im(E) dq
    // @param {double[]} wavenumbers - q grid in 1/m (ascending)
    // @param {double[]} imaginaryIntegral - angular Im integral per wavenumber
    // @param {double[]} absoluteIntegral - angular |E|^2 integral per wavenumber
    // @param {double} friction - (out) hysteretic friction coefficient
    // @param {double} contactArea - (out) real contact area ratio P(q1)
    // @return {Void}
    static void IntegrateOverWavenumber(double[] wavenumbers, double[] imaginaryIntegral, double[] absoluteIntegral, out double friction, out double contactArea)
    {
        double contactIntegral = 0, frictionIntegral = 0, contactAreaRatio = 1; double previousIntegrand = 0;
        for (int i = 0; i < WavenumberSamples; i++)
        {
            if (i > 0) contactIntegral += 0.125 * 0.5 * (wavenumbers[i] * wavenumbers[i] * wavenumbers[i] * RoughnessPowerSpectrum(wavenumbers[i]) * absoluteIntegral[i] + wavenumbers[i - 1] * wavenumbers[i - 1] * wavenumbers[i - 1] * RoughnessPowerSpectrum(wavenumbers[i - 1]) * absoluteIntegral[i - 1]) * (wavenumbers[i] - wavenumbers[i - 1]);
            contactAreaRatio = contactIntegral > 0 ? ErrorFunction(1 / (2 * System.Math.Sqrt(contactIntegral))) : 1.0;   // real contact area ratio P = erf(1 / (2 sqrt(G))) (S: Persson contact mechanics), dimensionless
            double integrand = wavenumbers[i] * wavenumbers[i] * wavenumbers[i] * RoughnessPowerSpectrum(wavenumbers[i]) * contactAreaRatio * (PerssonEnergyCorrection + (1 - PerssonEnergyCorrection) * contactAreaRatio * contactAreaRatio) * imaginaryIntegral[i];
            if (i > 0) frictionIntegral += 0.5 * 0.5 * (integrand + previousIntegrand) * (wavenumbers[i] - wavenumbers[i - 1]);
            previousIntegrand = integrand;
        }
        friction = frictionIntegral; contactArea = contactAreaRatio;
    }

    // @description - error function, Abramowitz & Stegun 7.1.26 (|err| < 1.5e-7)
    // @param {double} argument - argument (>= 0)
    // @return {double} - erf(x)
    static double ErrorFunction(double argument)
    {
        double rationalTerm = 1 / (1 + 0.3275911 * argument); double result = 1 - (((((1.061405429 * rationalTerm - 1.453152027) * rationalTerm) + 1.421413741) * rationalTerm - 0.284496736) * rationalTerm + 0.254829592) * rationalTerm * System.Math.Exp(-argument * argument);
        return result;
    }

    // @description - linear interpolation in a master-curve table, clamped to the table range
    // @param {double[]} table - samples at MasterCurveMin + i x MasterCurveStep
    // @param {double} logReducedSpeed - log10(aT v)
    // @return {double} - interpolated value
    static double InterpolateTable(double[] table, double logReducedSpeed)
    {
        double position = (System.Math.Min(System.Math.Max(logReducedSpeed, MasterCurveMin), MasterCurveMax) - MasterCurveMin) / MasterCurveStep; int index = System.Math.Min((int)position, MasterCurveSamples - 2); double fraction = position - index;
        return table[index] * (1 - fraction) + table[index + 1] * fraction;
    }

    // @description - builds the master curves for one Payne setting on first use (thread-safe)
    // @param {int} payneIndex - 0 or 1
    // @return {Void}
    static void EnsureBuilt(int payneIndex) { if (hysteresisFrictionTable[payneIndex] == null) lock (hysteresisFrictionTable) { if (hysteresisFrictionTable[payneIndex] == null) BuildMasterCurves(payneIndex); } }

    // @description - hysteretic friction coefficient from the master curve
    // @param {double} logReducedSpeed - log10(aT v)
    // @param {bool} payneEffect - Payne softening on/off
    // @return {double} - hysteretic mu
    public static double MuHys(double logReducedSpeed, bool payneEffect) { int payneIndex = payneEffect ? 1 : 0; EnsureBuilt(payneIndex); return InterpolateTable(hysteresisFrictionTable[payneIndex], logReducedSpeed); }

    // @description - real contact area ratio A/A0 from the master curve
    // @param {double} logReducedSpeed - log10(aT v)
    // @param {bool} payneEffect - Payne softening on/off
    // @return {double} - contact area ratio (0..1)
    public static double ContactAreaRatio(double logReducedSpeed, bool payneEffect) { int payneIndex = payneEffect ? 1 : 0; EnsureBuilt(payneIndex); return InterpolateTable(contactAreaTable[payneIndex], logReducedSpeed); }

    // @description - friction coefficient at sliding speed and rubber temperature, isothermal (no flashTemperature): hysteresis + adhesion
    //   adhesion = tau_f0 exp(-c log10^2(v/v0)) A/A0 / sigma
    // @param {double} slidingSpeed - v in m/s
    // @param {double} temperature - rubber temperature in degC
    // @param {Flags} flags - enabled physics terms
    // @return {double} - friction coefficient mu
    public static double Mu(double slidingSpeed, double temperature, in Flags flags)
    {
        double shiftedLogSpeed = System.Math.Log10(System.Math.Max(slidingSpeed, 1e-12)) + LogTimeTemperatureShift(temperature);   // log10 of the temperature-shifted sliding speed (master curve coordinate)
        double friction = 0;
        if (flags.hysteresis) friction += MuHys(shiftedLogSpeed, flags.payneEffect);
        if (flags.adhesion) { double logSpeedOffset = shiftedLogSpeed - LogTimeTemperatureShift(AdhesionReferenceTemperature) - System.Math.Log10(AdhesionSpeed0); friction += AdhesionShearStress0 * System.Math.Exp(-AdhesionWidthFactor * logSpeedOffset * logSpeedOffset) * ContactAreaRatio(shiftedLogSpeed, flags.payneEffect) / ReferenceContactPressure; }
        return friction;
    }

    // @description - flashTemperature temperature rise of the macro-asperity contacts (Tian & Kennedy 1994, circular source, all Peclet numbers):
    //   dT = 2 q a / (k sqrt(pi) sqrt(1.273 + Pe)), Pe = v a / (2 kappa); q = mu sigma v / contact ratio (all heat into the rubber, as TireThermal)
    // @param {double} slidingSpeed - v in m/s
    // @param {double} frictionCoefficient - mu
    // @param {double} contactPressure - sigma in Pa
    // @return {double} - temperature rise in K
    public static double Flash(double slidingSpeed, double frictionCoefficient, double contactPressure)
    {
        double heatFlux = frictionCoefficient * contactPressure * slidingSpeed / TireThermal.ContactRatio, radius = AsperityRadius, pecletNumber = slidingSpeed * radius / (2 * ThermalDiffusivity);
        return 2 * heatFlux * radius / (TireThermal.RubberConductivity * System.Math.Sqrt(System.Math.PI) * System.Math.Sqrt(1.273 + pecletNumber));   // flash temperature rise in K for a circular contact: 2 q r / (k sqrt(pi) sqrt(1.273 + Pe)), q heat flux (W/m^2), Pe Peclet number
    }
    public const int FlashIterations = 4;   // fixed-point iterations (converged to < 0.1 % in tests)

    // @description - effective friction with flashTemperature temperature (fixed-point iteration of mu(T + dT(mu))); T = macro rubber (grip) temperature
    // @param {double} slidingSpeed - v in m/s
    // @param {double} temperature - macro rubber temperature in degC
    // @param {double} contactPressure - sigma in Pa
    // @param {Flags} flags - enabled physics terms (WLF off freezes the friction temperature at the reference)
    // @param {double} flashRise - (out) flashTemperature temperature rise in K
    // @return {double} - effective friction coefficient
    public static double MuEff(double slidingSpeed, double temperature, double contactPressure, in Flags flags, out double flashRise)
    {
        flashRise = 0; if (!flags.williamsLandelFerry) temperature = TireThermal.OptimalTemperature;   // WLF ablated: friction temperature frozen at the reference
        double friction = Mu(slidingSpeed, temperature, flags);
        if (flags.flashTemperature && flags.williamsLandelFerry) for (int i = 0; i < FlashIterations; i++) { flashRise = Flash(slidingSpeed, friction, contactPressure); friction = Mu(slidingSpeed, temperature + flashRise, flags); }
        return friction;
    }

    // @description - grip factor on the sourced peak coefficients: mu(vs, T) / mu(vs_ref, T); = 1 at the reference sliding speed for any temperature (D)
    // @param {double} slidingSpeed - current sliding speed in m/s
    // @param {double} temperature - macro rubber temperature in degC
    // @param {double} contactPressure - sigma in Pa
    // @param {double} referenceSlidingSpeed - sliding speed at the reference condition in m/s
    // @param {Flags} flags - enabled physics terms
    // @param {double} flashRise - (out) flashTemperature temperature rise at the current speed in K
    // @return {double} - multiplicative grip factor
    public static double GripFactor(double slidingSpeed, double temperature, double contactPressure, double referenceSlidingSpeed, in Flags flags, out double flashRise)
    {
        double numerator = MuEff(slidingSpeed, temperature, contactPressure, flags, out flashRise), denominator = MuEff(referenceSlidingSpeed, temperature, contactPressure, flags, out _);
        return denominator > 0 ? numerator / denominator : 1.0;
    }

    // @description - reference sliding speed: lateral peak slip of the base Pacejka curve at Fz0 (golden-section search), times the reference speed (D)
    // @param {CarParams} carParams - Pacejka coefficients
    // @return {double} - reference sliding speed in m/s
    public static double VsRef(CarParams carParams)
    {
        double low = 0.001, high = 0.5, goldenRatio = 0.5 * (System.Math.Sqrt(5) - 1);
        for (int i = 0; i < 80; i++) { double lowerProbe = high - goldenRatio * (high - low), upperProbe = low + goldenRatio * (high - low); if (PacejkaTire.PureLat(carParams, (float)lowerProbe, carParams.nominalVerticalLoad, 0f) > PacejkaTire.PureLat(carParams, (float)upperProbe, carParams.nominalVerticalLoad, 0f)) high = upperProbe; else low = lowerProbe; }
        return ReferenceSpeed * System.Math.Tan(0.5 * (low + high));
    }

    // @description - tread stiffness factor at speed v (deformation frequency v / contact length), at the reference temperature: grip-temperature
    //   independence of slip stiffness is sourced (Alcazar Vargas 2022, TireThermal) and overrides the WLF estimate. Series springs:
    //   K = K_ref / ((1 - share) + share |E*(f_ref)| / |E*(f)|)
    // @param {double} speed - vehicle speed in m/s
    // @param {double} contactLength - current contact patch length in m
    // @param {double} contactLengthReference - contact patch length at the reference condition in m
    // @return {double} - stiffness multiplier relative to the reference condition
    public static double StiffnessFactor(double speed, double contactLength, double contactLengthReference)
    {
        double frequency = System.Math.Max(System.Math.Abs(speed), 0.5) / contactLength, referenceFrequency = ReferenceSpeed / contactLengthReference;
        return 1.0 / ((1 - TreadShare) + TreadShare * ModulusAbs(referenceFrequency, TireThermal.OptimalTemperature) / ModulusAbs(frequency, TireThermal.OptimalTemperature));
    }

    // @description - rolling-resistance factor: loss ~ tan d at the once-per-revolution deformation frequency (Schuring 1980 energy-controlled loss),
    //   referenced to the ISO 28580 condition (25 C, 80 km/h)
    // @param {double} speed - vehicle speed in m/s
    // @param {double} radius - rolling radius in m
    // @param {double} temperature - tread/carcass temperature in degC
    // @return {double} - rolling resistance multiplier relative to the reference condition
    public static double RollingFactor(double speed, double radius, double temperature)
    {
        double frequency = System.Math.Max(System.Math.Abs(speed), 0.5) / (2 * System.Math.PI * radius), referenceFrequency = RollingResistanceReferenceSpeed / (2 * System.Math.PI * radius);
        return TanDelta(frequency, temperature) / TanDelta(referenceFrequency, RollingResistanceReferenceTemperature);
    }
}
