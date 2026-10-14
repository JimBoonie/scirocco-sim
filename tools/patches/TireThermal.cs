// Tire thermal + inflation-pressure model (settings option "Tire thermal model", default OFF -> handling identical to before).
// Lumped multi-node model in the TRT / TRT-EVO family (Farroni et al. 2014 / 2019): per tire 3 tread-surface zones (inner/mid/outer),
// tread bulk, carcass (belts+plies+sidewalls+liner), inflation gas, rim. Heat sources: friction power -F.Vs (split tire/road by thermal
// effusivity, Blok) and rolling-resistance hysteresis F_rr.V. Sinks: forced+natural convection to air, transient conduction into the road,
// internal convection carcass<->gas<->rim. Pressure: ideal gas at constant volume. Grip: Alcazar Vargas et al. 2022 measured mu(T)
// (summer passenger tire); pressure effects: MF6.1 terms (Besselink/Schmeitz/Pacejka 2010, TNO example coefficients).
// Every constant carries a tag: S sourced, SD sources disagree, D derived, E estimated. Sources: tire_thermal_research.md.
using UnityEngine;
public struct TireScale
{   // multipliers on the Pacejka terms; One = exact identity (x*1f is exact in IEEE, so thermal-off results are bit-identical)
    public float kx, mux, ky, kyLoad, muy, rr;
    public static readonly TireScale One = new TireScale { kx = 1f, mux = 1f, ky = 1f, kyLoad = 1f, muy = 1f, rr = 1f };
}
public class TireThermal
{
    // ---- fixed environment (Taylor: 66 F air, 70 F track) ----
    public const double TAmb = 18.9, TTrack = 21.1;                     // degC (given)
    public const double PAtm = 101325.0;                                 // Pa, standard atmosphere (S)
    // ---- geometry: 185/60R14 (S: ETRTO size code -> section width 185 mm, sidewall 111 mm, rim 14 in) ----
    public const double SecW = 0.185, RimR = 0.1778, R0 = 0.2888;
    public const double TreadW = 0.145;      // m tread (contact) width, ~78% of section width (E)
    public const double TreadDepth = 0.008;  // m new-tire tread depth (E: typical new passenger tire 8 mm)
    public const double SkinT = 0.001;       // m surface-node thickness (E, model resolution; ~ depth heated in a few s, see research doc)
    public const double CarcT = 0.005;       // m crown carcass under the tread (belts + plies + liner) (E)
    public const double TireMass = 7.6;      // kg OE 185/60HR14 (S wheel-size.it)
    public const double RimW = 0.152;        // m 6J rim width (E: 6Jx14 assumed for the GT/GTI alloy)
    public const double RimMass = 6.0;       // kg 14x6 cast alloy (E)
    // ---- materials ----
    public const double kRub = 0.282, cRub = 1741.0;     // W/mK, J/kgK tire rubber, Hot Disk at 28 C (S, PMC9182718)
    public const double rhoRub = 1125.0;                 // kg/m3 tread compound, midpoint of 1094-1157 measured (S range)
    public const double fCompound = 0.78, fSteel = 0.165, fTextile = 0.055;   // passenger tire mass fractions (S WRAP 2006; steel SD 14-16.5%)
    public const double cSteel = 490.0, cTextile = 1700.0, cAl = 900.0;      // J/kgK (S handbook; textile = nylon 6)
    public const double kAsph = 1.6, rhoAsph = 2290.0, cAsph = 884.0;        // asphalt (SD: k 0.74-2.88; Aston DBM 1.33-1.91 -> 1.6)
    public const double ContactRatio = 0.63;  // real/nominal tire-asphalt contact area (SD: 61-66% pressure film, MDPI Materials 2023; 5-10% older)
    public const double HInner = 25.0;        // W/m2K internal convection liner/rim <-> gas (E, 10-100 plausible; checked vs measured pressure rise)
    public const double CvAir = 718.0, RAir = 287.05;   // J/kgK (S)
    public const double KAir = 0.0263, NuAir = 15.89e-6, PrAir = 0.707;   // air at 300 K (S Incropera Table A.4)
    public const double RrToTread = 0.44;     // share of rolling-resistance loss in the tread (SD: tread 44% Tire Review; crown 70% Technia)
    // ---- grip vs temperature (S: Alcazar Vargas et al., Sensors 2022, Austone SP-6 205/65R15, Eq. mu = mumax + 1 - cosh((T-Topt)/Tdisp)) ----
    public const double MuMax = 1.1, TOpt = 88.0, TDisp = 50.0;
    public const double TFitLo = 44.6;        // degC: fit gives 0.70 = lowest measured mu (D); held below (E: no data)
    public const double TFitHi = 120.0;       // degC: highest measured zone temperature; held above (E)
    // ---- pressure (MF6.1; S coefficients of the TNO example tire, transferred to this tire: E) ----
    public const double PNom = 170000.0;      // Pa gauge, VW cold pressure 185/60R14 (SD: 1.7 bar three sources, 1.8 one)
    public const double PPX1 = -0.3485, PPX2 = 0.37824, PPX3 = -0.09603, PPX4 = 0.06518;
    public const double PPY1 = -0.6255, PPY2 = -0.06523, PPY3 = -0.16666, PPY4 = -0.2811, QSY8 = -0.4089;

    // ---- derived constants (D) ----
    static readonly double Circ = 2.0 * System.Math.PI * (R0 - 0.5 * TreadDepth);
    static readonly double AZone = TreadW / 3.0 * Circ;                                 // outer area of one surface zone
    public static readonly double MTread = rhoRub * TreadW * Circ * TreadDepth;         // 2.34 kg
    public static readonly double CSkin = rhoRub * AZone * SkinT * cRub;                // J/K per zone
    public static readonly double CBulk = (MTread - 3.0 * rhoRub * AZone * SkinT) * cRub;
    static readonly double MCarcComp = fCompound * TireMass - MTread;
    public static readonly double CCarc = MCarcComp * cRub + fSteel * TireMass * cSteel + fTextile * TireMass * cTextile;
    public static readonly double CRim = RimMass * cAl;
    static readonly double HIn = R0 - TreadDepth - CarcT - RimR;                        // cavity height
    public static readonly double GasVol = 0.85 * (SecW - 0.015) * HIn * 2.0 * System.Math.PI * (RimR + 0.5 * HIn);  // m3 (D, 0.85 = rounded section, E)
    static readonly double ASide = 2.0 * System.Math.PI * (R0 * R0 - RimR * RimR);      // both sidewalls, outer
    static readonly double ACarcIn = 2.0 * System.Math.PI * (R0 - TreadDepth - CarcT) * SecW * 0.9 + ASide;
    static readonly double ARimIn = 2.0 * System.Math.PI * RimR * RimW, ARimOut = 2.0 * ARimIn;   // outer exposed rim ~2x (E)
    static readonly double GSkinBulk = kRub * AZone / (0.5 * SkinT + 0.5 * (TreadDepth - SkinT));
    static readonly double GBulkCarc = kRub * TreadW * Circ / (0.5 * (TreadDepth - SkinT) + 0.5 * CarcT);
    static readonly double GLat = kRub * SkinT * Circ / (TreadW / 3.0);
    static readonly double ETire = System.Math.Sqrt(kRub * rhoRub * cRub), ERoad = System.Math.Sqrt(kAsph * rhoAsph * cAsph);
    // friction-heat share into the tire (SD): 1.0 = rubber friction is mostly viscoelastic hysteresis, dissipated inside the rubber
    // (Persson 2006 flash-temperature theory; TRT puts all friction power into the tread surface). Interface (Blok) split would be
    // e_t/(e_t+e_r) = 0.29 (BlokShare) -> bench replica only 43 C vs 100-120 C measured; 1.0 gives 116 C (see research doc).
    public static readonly double BetaTire = 1.0, BlokShare = ETire / (ETire + ERoad);
    static readonly double EEff = ETire * ERoad / (ETire + ERoad);

    // ---- state ----
    public readonly double[] Ts = new double[3];   // surface zones 0 inner, 1 mid, 2 outer (degC)
    public double Tb, Tc, Tg, Tr;                   // bulk, carcass, gas, rim
    public double pColdGauge, TCold = TAmb, gasMass;
    public double pGauge;                            // current gauge pressure, Pa
    public readonly double[] zoneW = { 1.0 / 3, 1.0 / 3, 1.0 / 3 };
    // energy bookkeeping (J): in = heat into the tire system, out = convection + road conduction; residual should stay ~0
    public double EIn, EOut, EFricRoad, E0; readonly double[] dTs = new double[3];

    public TireThermal(double coldGauge, bool warm = false, double warmCarcass = TAmb)
    {
        pColdGauge = coldGauge;
        for (int i = 0; i < 3; i++) Ts[i] = warm ? TOpt : TAmb;
        Tb = warm ? TOpt : TAmb; Tc = Tg = Tr = warm ? warmCarcass : TAmb;
        gasMass = (pColdGauge + PAtm) * GasVol / (RAir * (TCold + 273.15));
        UpdatePressure(); E0 = Stored();
    }
    public double Stored() => CSkin * (Ts[0] + Ts[1] + Ts[2]) + CBulk * Tb + CCarc * Tc + gasMass * CvAir * Tg + CRim * Tr;
    public double Residual => (Stored() - E0) - (EIn - EOut);
    public void SetColdPressure(double g) { if (System.Math.Abs(g - pColdGauge) < 1.0) return; pColdGauge = g; gasMass = (g + PAtm) * GasVol / (RAir * (TCold + 273.15)); E0 = Stored() - (EIn - EOut); UpdatePressure(); }
    public void UpdatePressure() => pGauge = (pColdGauge + PAtm) * (Tg + 273.15) / (TCold + 273.15) - PAtm;   // ideal gas, constant volume (S)

    // outer convection: Churchill-Bernstein forced crossflow on a cylinder D = 2 R0, combined with Churchill-Chu natural convection (S)
    public static double HOut(double v, double dT)
    {
        double D = 2.0 * R0, Re = System.Math.Max(v, 0.0) * D / NuAir;
        double nuF = 0.3 + 0.62 * System.Math.Sqrt(Re) * System.Math.Pow(PrAir, 1.0 / 3) / System.Math.Pow(1 + System.Math.Pow(0.4 / PrAir, 2.0 / 3), 0.25) * System.Math.Pow(1 + System.Math.Pow(Re / 282000.0, 5.0 / 8), 4.0 / 5);
        double Ra = 9.81 / (TAmb + 273.15) * System.Math.Max(System.Math.Abs(dT), 0.1) * D * D * D / (NuAir * NuAir / PrAir);
        double nuN = System.Math.Pow(0.60 + 0.387 * System.Math.Pow(Ra, 1.0 / 6) / System.Math.Pow(1 + System.Math.Pow(0.559 / PrAir, 9.0 / 16), 8.0 / 27), 2);
        return System.Math.Pow(nuF * nuF * nuF + nuN * nuN * nuN, 1.0 / 3) * KAir / D;
    }
    // road conduction: two semi-infinite bodies brought into contact for t_c = L/V, mean flux 2 e_eff dT/sqrt(pi t_c) (D)
    public static double HRoad(double v, double L) => 2.0 * EEff / System.Math.Sqrt(System.Math.PI * System.Math.Max(L, 1e-3) / System.Math.Max(v, 0.5));

    /// One step. pFric = total friction power -F.Vs at the patch (W, >=0); pRR = rolling-resistance power (W); v = rolling speed (m/s);
    /// fz (N); camberRad standard camber (+ = top out, loads the outer shoulder); kz tire vertical rate (N/m); onGround.
    public void Step(double dt, double pFric, double pRR, double v, double fz, double camberRad, double kz, bool onGround)
    {
        double av = System.Math.Abs(v);
        // zone load split: elastic foundation, deflection d0 + y tan(gamma), y outward (D)
        double sum = 0; if (onGround && fz > 0)
        {
            double d0 = fz / kz;
            for (int i = 0; i < 3; i++) { double y = (i - 1) * TreadW / 3.0; zoneW[i] = System.Math.Max(0.0, d0 + y * System.Math.Tan(camberRad)); sum += zoneW[i]; }
        }
        if (sum > 0) for (int i = 0; i < 3; i++) zoneW[i] /= sum; else for (int i = 0; i < 3; i++) zoneW[i] = 0;
        double pg = System.Math.Max(pGauge, 20000.0);
        double aC = onGround && fz > 0 ? fz / pg : 0.0;                 // nominal contact area = Fz / inflation pressure (D, membrane approx.)
        double L = aC / TreadW, hR = HRoad(av, L);
        double qFric = BetaTire * System.Math.Max(pFric, 0.0);
        double inS = 0, outS = 0;
        double qToBulk = 0;
        for (int i = 0; i < 3; i++)
        {
            double qConv = HOut(av, Ts[i] - TAmb) * AZone * (Ts[i] - TAmb);
            double qRoad = zoneW[i] > 0 ? hR * ContactRatio * aC / 3.0 * (Ts[i] - TTrack) : 0.0;
            double qB = GSkinBulk * (Ts[i] - Tb);
            double qL = (i > 0 ? GLat * (Ts[i - 1] - Ts[i]) : 0) + (i < 2 ? GLat * (Ts[i + 1] - Ts[i]) : 0);
            double qIn = qFric * zoneW[i];
            dTs[i] = (qIn - qConv - qRoad - qB + qL) / CSkin * dt; qToBulk += qB; inS += qIn; outS += qConv + qRoad;
        }
        double qBC = GBulkCarc * (Tb - Tc), qCG = HInner * ACarcIn * (Tc - Tg), qGR = HInner * ARimIn * (Tg - Tr);
        double qSide = HOut(av, Tc - TAmb) * ASide * (Tc - TAmb), qRimOut = HOut(av, Tr - TAmb) * ARimOut * (Tr - TAmb);
        double rr = System.Math.Max(pRR, 0.0);
        Tb += (qToBulk + RrToTread * rr - qBC) / CBulk * dt;
        Tc += ((1 - RrToTread) * rr + qBC - qCG - qSide) / CCarc * dt;
        Tg += (qCG - qGR) / (gasMass * CvAir) * dt;
        Tr += (qGR - qRimOut) / CRim * dt;
        for (int i = 0; i < 3; i++) Ts[i] += dTs[i];
        EIn += (inS + rr) * dt; EOut += (outS + qSide + qRimOut) * dt; EFricRoad += (1 - BetaTire) * System.Math.Max(pFric, 0.0) * dt;
        UpdatePressure();
    }
    // grip temperature = tread BULK temperature (TRT EVO, Farroni et al. 2019: grip correlates best with bulk temperature, S; applying
    // that to this passenger tire and to the Alcazar Vargas mu(T) curve, which was fitted against zone/surface temperatures, is E).
    // The 1 mm surface zones follow every slide and cool within ~15 s on a straight; the bulk (C_bulk ~21x C_skin, D) integrates the
    // heat history, which is what the compound's viscoelastic state follows. Surface zones still drive heat flow and the display.
    public double GripTemp() => Tb;
    // surface (display) temperature: zone temps weighted by (T_i - T_amb) (Alcazar Vargas Eq. 3-4, S); plain mean when all at/below ambient
    public double SurfaceTemp()
    {
        double s = 0, sw = 0; for (int i = 0; i < 3; i++) { double w = System.Math.Max(Ts[i] - TAmb, 0); s += w * Ts[i]; sw += w; }
        return sw > 1e-6 ? s / sw : (Ts[0] + Ts[1] + Ts[2]) / 3.0;
    }
    public static double MuOfT(double T) { T = System.Math.Min(System.Math.Max(T, TFitLo), TFitHi); return MuMax + 1.0 - System.Math.Cosh((T - TOpt) / TDisp); }
    /// Pacejka multipliers: temperature on peak mu only (stiffness ~independent of T, S Vargas); MF6.1 pressure terms on stiffness + mu.
    public TireScale Scale()
    {
        double mt = MuOfT(GripTemp()) / MuMax;     // = 1 at Topt: the base coefficients represent the tire at its optimum (D)
        double dpi = (pGauge - PNom) / PNom;
        return new TireScale
        {
            kx = (float)(1 + PPX1 * dpi + PPX2 * dpi * dpi), mux = (float)(mt * (1 + PPX3 * dpi + PPX4 * dpi * dpi)),
            ky = (float)(1 + PPY1 * dpi), kyLoad = (float)(1 + PPY2 * dpi), muy = (float)(mt * (1 + PPY3 * dpi + PPY4 * dpi * dpi)),
            rr = (float)System.Math.Pow(System.Math.Max(pGauge, 20000.0) / PNom, QSY8)
        };
    }
    public static double CToF(double c) => c * 9.0 / 5.0 + 32.0;
    public const double PaPerPsi = 6894.757;
}
