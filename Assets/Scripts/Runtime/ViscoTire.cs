// Experimental (viscoelastic) tread compound. Selectable in the Quick menu ("Tyre compound"); the Default compound never calls this file.
// Physics (sources and tags in tools/viscoelastic_tyre_research.md):
//  - complex modulus E*(w) of a carbon-black-filled E-SBR summer tread: Havriliak-Negami polymer relaxation + fractional filler network,
//    fitted (tools/visco_fit.py, D) to sourced DMA indicators; time-temperature superposition by WLF (universal constants about Tg, S).
//  - Payne effect: Kraus (1984) strain softening of the filler part at the asperity strain (Persson: strain ~ rms slope / 2).
//  - rubber friction = hysteretic (Persson 2001 theory, self-affine road, Lorenz/Persson 2015 rms-slope cutoff) + adhesive
//    (Lorenz/Persson real-contact shear stress, Gaussian in log sliding speed) -> master curve mu(log10(aT v)), precomputed tables.
//  - flash temperature of the macro-asperity contacts (Tian & Kennedy 1994 moving heat source) raises the friction temperature.
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
    public const double Tg = -50.0;                 // degC (S: AZoM E-SBR; SD: NETZSCH DSC midpoint -45, range -55..-35)
    public const double C1g = 17.44, C2g = 51.6;    // WLF universal constants referenced to Tg (S: Williams, Landel, Ferry 1955)
    // ---- complex modulus fit (D: tools/visco_fit.py to S targets tan d(0 C,10 Hz)=0.206, tan d(60 C,10 Hz)=0.160, Sae-oui et al. 2017;
    //      tan d peak 0.8 SD; |E*|(60 C,10 Hz) = 8 MPa E) ----
    public const double EGlass = 2e9;               // Pa glassy modulus (SD: 1-3 GPa)
    public const double E0 = 3.224467e6;            // Pa polymer rubbery modulus (D)
    public const double HnA = 0.8, HnB = 1.0;       // Havriliak-Negami shape (D: best of the fit grid)
    public const double LogTauG = -2.610193;        // log10 s, HN relaxation time at Tg (D)
    public const double FillerRatio = 1.5;          // filler-network / polymer modulus at small strain (SD: Payne ratio ~2.5, 50-80 phr CB)
    public const double FillerN = 0.160231;         // fractional exponent of the filler network: tan d_filler = tan(n pi/2) (D)
    public const double FitTRef = 60.0, FitFRef = 10.0;   // degC, Hz: reference of the filler element (S DMA indicator condition)
    // ---- Payne effect (Kraus 1984 form, S; parameters E/SD) ----
    public const double PayneEpsC = 0.01;           // critical strain amplitude (SD: Heinrich & Kluppel 2002, ~1 %)
    public const double KrausM = 0.6;               // Kraus exponent (SD: 0.5-0.6 fits for CB-filled rubbers)
    // ---- road surface (self-affine fractal) ----
    public const double Hurst = 0.8;                // (SD: Persson asphalt 0.7-0.9)
    public const double HRms = 0.25e-3;             // m rms roughness of the macrotexture (E: track asphalt MPD 0.5-1 mm)
    public const double Lambda0 = 5e-3;             // m roll-off wavelength (E)
    public const double XiCut = 1.3;                // cumulative rms slope at the cutoff q1 (S: Lorenz, Oh, Persson 2015, J Chem Phys 142)
    public const double Poisson = 0.5;              // rubber incompressible (S)
    public const double PersGamma = 0.5;            // Persson's elastic-energy correction S = gamma + (1-gamma) P^2 (S: Persson 2001, gamma ~0.5)
    public static readonly double Q0 = 2 * System.Math.PI / Lambda0;
    public static readonly double Q1 = System.Math.Pow(XiCut * XiCut * (2 - 2 * Hurst) / (2 * Hurst * HRms * HRms * System.Math.Pow(Q0, 2 * Hurst)) + System.Math.Pow(Q0, 2 - 2 * Hurst), 1 / (2 - 2 * Hurst));   // D
    public static readonly double FrictionStrain = XiCut / 2;   // asperity strain ~ rms slope / 2 (S: Persson 2006)
    // ---- adhesion (S: Lorenz, Oh, Persson 2015 tread compound on road: tau_f = tau_f0 exp(-c log10^2(v/v0))) ----
    public const double TauF0 = 6.5e6, CAdh = 0.1, V0Adh = 6e-3;   // Pa, -, m/s (S; tau_f0 range 5.3-8.5 MPa SD)
    public const double TAdhRef = 20.0;             // degC reference temperature of that master curve (E: room-temperature tests)
    // ---- reference condition of the calibration ----
    public const double VRef = 80.0 / 3.6;          // m/s (E: handling-test speed, step-steer / ramp-steer 80 km/h)
    public const double SigmaRef = TireThermal.PNom; // Pa nominal contact pressure ~ inflation pressure (D: membrane approx., as TireThermal)
    public const double TRrRef = 25.0, VRrRef = 80.0 / 3.6;   // ISO 28580 rolling-resistance reference: 25 C, 80 km/h (S)
    public const double TreadShare = 0.5;           // tread (brush) share of the tyre's tangential compliance (E)
    // ---- flash temperature ----
    public static readonly double AsperityRadius = Lambda0 / 4;   // m macro-asperity contact radius (E: quarter roll-off wavelength)
    static readonly double Kappa = TireThermal.kRub / (TireThermal.rhoRub * TireThermal.cRub);   // m^2/s (D, S data)

    public struct Flags { public bool hysteresis, adhesion, wlf, flash, payne, rateStiffness, loadRelax, rollingRes, bulkHeat; }
    public static Flags FromParams(CarParams p) => new Flags { hysteresis = p.veHysteresis, adhesion = p.veAdhesion, wlf = p.veWlf, flash = p.veFlash, payne = p.vePayne, rateStiffness = p.veRateStiffness, loadRelax = p.veLoadRelax, rollingRes = p.veRollingRes, bulkHeat = p.veBulkHeat };

    public static double LogAT(double T) => -C1g * (T - Tg) / (C2g + T - Tg);   // valid above Tg
    static readonly double WRefFill = 2 * System.Math.PI * FitFRef * System.Math.Pow(10, LogAT(FitTRef));
    /// complex Young's modulus at reduced angular frequency w (rad/s at Tg), strain amplitude (Payne on the filler network)
    public static Complex E(double w, double strain, bool payne)
    {
        w = System.Math.Max(System.Math.Abs(w), 1e-300);
        Complex hn = EGlass + (E0 - EGlass) / Complex.Pow(1 + Complex.Pow(new Complex(0, w * System.Math.Pow(10, LogTauG)), HnA), HnB);
        double pay = payne && strain > 0 ? 1.0 / (1.0 + System.Math.Pow(strain / PayneEpsC, 2 * KrausM)) : 1.0;
        Complex fil = FillerRatio * E0 * pay * Complex.Pow(new Complex(0, w / WRefFill), FillerN);
        return hn + fil;
    }
    public static double TanDelta(double fHz, double T) { var e = E(2 * System.Math.PI * fHz * System.Math.Pow(10, LogAT(T)), 0, false); return e.Imaginary / e.Real; }
    public static double ModulusAbs(double fHz, double T) => E(2 * System.Math.PI * fHz * System.Math.Pow(10, LogAT(T)), 0, false).Magnitude;

    // ---- precomputed master curves on x = log10(aT v) (v in m/s, aT referenced to Tg) ----
    public const double XMin = -18, XMax = 4, XStep = 0.05;
    public static readonly int NX = (int)System.Math.Round((XMax - XMin) / XStep) + 1;
    const int NQ = 96, NPhi = 16;   // integration resolution (model resolution, checked: halving changes mu < 1 %)
    static double[][] muHysT = new double[2][], areaT = new double[2][];
    static double C(double q) => Hurst / System.Math.PI * HRms * HRms / (Q0 * Q0) * System.Math.Pow(q / Q0, -2 * (Hurst + 1));
    static void Build(int pi)
    {
        bool payne = pi == 1; var mh = new double[NX]; var ar = new double[NX];
        double[] q = new double[NQ], cphi = new double[NPhi], wphi = new double[NPhi];
        for (int i = 0; i < NQ; i++) q[i] = Q0 * System.Math.Pow(Q1 / Q0, i / (double)(NQ - 1));
        for (int j = 0; j < NPhi; j++) { double ph = (j + 0.5) * 0.5 * System.Math.PI / NPhi; cphi[j] = System.Math.Cos(ph); wphi[j] = 0.5 * System.Math.PI / NPhi; }   // midpoint rule on [0, pi/2]
        double s0 = (1 - Poisson * Poisson) * SigmaRef;
        var imf = new double[NQ]; var absf = new double[NQ];
        for (int k = 0; k < NX; k++)
        {
            double vr = System.Math.Pow(10, XMin + k * XStep);
            for (int i = 0; i < NQ; i++)
            {
                double si = 0, sa = 0;
                for (int j = 0; j < NPhi; j++) { var e = E(q[i] * vr * cphi[j], FrictionStrain, payne) / s0; si += wphi[j] * cphi[j] * e.Imaginary; sa += wphi[j] * (e.Real * e.Real + e.Imaginary * e.Imaginary); }
                imf[i] = 4 * si; absf[i] = 4 * sa;   // full 0..2pi integrals by symmetry
            }
            double G = 0, mu = 0, P = 1; double prevI = 0;
            for (int i = 0; i < NQ; i++)
            {
                if (i > 0) G += 0.125 * 0.5 * (q[i] * q[i] * q[i] * C(q[i]) * absf[i] + q[i - 1] * q[i - 1] * q[i - 1] * C(q[i - 1]) * absf[i - 1]) * (q[i] - q[i - 1]);
                P = G > 0 ? Erf(1 / (2 * System.Math.Sqrt(G))) : 1.0;
                double I = q[i] * q[i] * q[i] * C(q[i]) * P * (PersGamma + (1 - PersGamma) * P * P) * imf[i];
                if (i > 0) mu += 0.5 * 0.5 * (I + prevI) * (q[i] - q[i - 1]);
                prevI = I;
            }
            mh[k] = mu; ar[k] = P;   // P(q1) = A/A0 real contact area
        }
        muHysT[pi] = mh; areaT[pi] = ar;
    }
    static double Erf(double x)
    {   // Abramowitz & Stegun 7.1.26 (|err| < 1.5e-7)
        double t = 1 / (1 + 0.3275911 * x); double y = 1 - (((((1.061405429 * t - 1.453152027) * t) + 1.421413741) * t - 0.284496736) * t + 0.254829592) * t * System.Math.Exp(-x * x);
        return y;
    }
    static double Lookup(double[] tab, double x)
    {
        double f = (System.Math.Min(System.Math.Max(x, XMin), XMax) - XMin) / XStep; int i = System.Math.Min((int)f, NX - 2); double a = f - i;
        return tab[i] * (1 - a) + tab[i + 1] * a;
    }
    static void Ensure(int pi) { if (muHysT[pi] == null) lock (muHysT) { if (muHysT[pi] == null) Build(pi); } }
    public static double MuHys(double x, bool payne) { int pi = payne ? 1 : 0; Ensure(pi); return Lookup(muHysT[pi], x); }
    public static double Area(double x, bool payne) { int pi = payne ? 1 : 0; Ensure(pi); return Lookup(areaT[pi], x); }
    /// friction coefficient at sliding speed v (m/s) and rubber temperature T (degC), isothermal (no flash)
    public static double Mu(double v, double T, in Flags f)
    {
        double x = System.Math.Log10(System.Math.Max(v, 1e-12)) + LogAT(T);
        double m = 0;
        if (f.hysteresis) m += MuHys(x, f.payne);
        if (f.adhesion) { double la = x - LogAT(TAdhRef) - System.Math.Log10(V0Adh); m += TauF0 * System.Math.Exp(-CAdh * la * la) * Area(x, f.payne) / SigmaRef; }
        return m;
    }
    /// flash temperature rise of the macro-asperity contacts (Tian & Kennedy 1994, circular source, all Peclet numbers):
    /// dT = 2 q a / (k sqrt(pi) sqrt(1.273 + Pe)), Pe = v a / (2 kappa); q = mu sigma v / contact ratio (all heat into the rubber, as TireThermal)
    public static double Flash(double v, double mu, double sigma)
    {
        double q = mu * sigma * v / TireThermal.ContactRatio, a = AsperityRadius, pe = v * a / (2 * Kappa);
        return 2 * q * a / (TireThermal.kRub * System.Math.Sqrt(System.Math.PI) * System.Math.Sqrt(1.273 + pe));
    }
    public const int FlashIter = 4;   // fixed-point iterations (converged to < 0.1 % in tests)
    /// effective friction with flash temperature; T = macro rubber (grip) temperature
    public static double MuEff(double v, double T, double sigma, in Flags f, out double dT)
    {
        dT = 0; if (!f.wlf) T = TireThermal.TOpt;   // WLF ablated: friction temperature frozen at the reference
        double mu = Mu(v, T, f);
        if (f.flash && f.wlf) for (int i = 0; i < FlashIter; i++) { dT = Flash(v, mu, sigma); mu = Mu(v, T + dT, f); }
        return mu;
    }
    /// grip factor on the sourced peak coefficients; = 1 at the reference sliding speed for any temperature (D)
    public static double GripFactor(double vs, double T, double sigma, double vsRef, in Flags f, out double dT)
    {
        double num = MuEff(vs, T, sigma, f, out dT), den = MuEff(vsRef, T, sigma, f, out _);
        return den > 0 ? num / den : 1.0;
    }
    /// reference sliding speed: lateral peak slip of the base Pacejka curve at Fz0 (golden search), times V_ref (D)
    public static double VsRef(CarParams p)
    {
        double a = 0.001, b = 0.5, g = 0.5 * (System.Math.Sqrt(5) - 1);
        for (int i = 0; i < 80; i++) { double c = b - g * (b - a), d = a + g * (b - a); if (PacejkaTire.PureLat(p, (float)c, p.Fz0, 0f) > PacejkaTire.PureLat(p, (float)d, p.Fz0, 0f)) b = d; else a = c; }
        return VRef * System.Math.Tan(0.5 * (a + b));
    }
    /// tread stiffness factor at speed v (deformation frequency v / contact length), at the reference temperature: grip-temperature
    /// independence of slip stiffness is sourced (Alcazar Vargas 2022, TireThermal) and overrides the WLF estimate. Series springs:
    /// K = K_ref / ((1 - share) + share |E*(f_ref)| / |E*(f)|)
    public static double StiffnessFactor(double v, double contactLen, double contactLenRef)
    {
        double f = System.Math.Max(System.Math.Abs(v), 0.5) / contactLen, fr = VRef / contactLenRef;
        return 1.0 / ((1 - TreadShare) + TreadShare * ModulusAbs(fr, TireThermal.TOpt) / ModulusAbs(f, TireThermal.TOpt));
    }
    /// rolling-resistance factor: loss ~ tan d at the once-per-revolution deformation frequency (Schuring 1980 energy-controlled loss),
    /// referenced to the ISO 28580 condition (25 C, 80 km/h)
    public static double RollingFactor(double v, double radius, double T)
    {
        double f = System.Math.Max(System.Math.Abs(v), 0.5) / (2 * System.Math.PI * radius), fr = VRrRef / (2 * System.Math.PI * radius);
        return TanDelta(f, T) / TanDelta(fr, TRrRef);
    }
}
