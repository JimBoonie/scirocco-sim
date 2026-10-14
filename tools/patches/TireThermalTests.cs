// Tire thermal model (TireThermal.cs): ideal gas, energy balance, published-data replicas (bench heat-up, highway pressure rise), cool-down, off = identity.
using NUnit.Framework; using UnityEngine;
public class TireThermalTests
{
    static void Log(string s) { Debug.Log("THERMAL " + s); TestContext.Out.WriteLine(s); }
    [Test] public void Pressure_Follows_Ideal_Gas_At_Constant_Volume()
    {
        var t = new TireThermal(170000);
        Assert.AreEqual(170000.0, t.pGauge, 1e-6, "cold gauge pressure at the set temperature");
        t.Tg = TireThermal.TAmb + 40.0; t.UpdatePressure();
        double ratio = (t.pGauge + TireThermal.PAtm) / (170000.0 + TireThermal.PAtm), tr = (t.Tg + 273.15) / (TireThermal.TAmb + 273.15);
        Assert.AreEqual(tr, ratio, 1e-12);
        Log($"ideal gas: +40 K gas -> {t.pGauge / TireThermal.PaPerPsi:0.00} psi gauge (cold 24.66), abs ratio {ratio:0.0000} = T ratio {tr:0.0000}");
    }
    [Test] public void Energy_Balance_Closes()
    {
        var t = new TireThermal(170000); double dt = 0.001;
        for (int i = 0; i < 300000; i++)
        {
            double s = i * dt, v = 10 + 15 * Mathf.Sin((float)(s * 0.05)), pf = (i / 20000) % 2 == 0 ? 6000 : 0;
            t.Step(dt, pf, 600, v, 3000 + 800 * Mathf.Sin((float)s), -0.03, 180000, (i / 50000) % 6 != 5);
        }
        double rel = System.Math.Abs(t.Residual) / t.EIn;
        Log($"energy: in {t.EIn / 1e6:0.000} MJ out {t.EOut / 1e6:0.000} MJ stored {(t.Stored() - t.E0) / 1e6:0.000} MJ residual {t.Residual:0.00} J ({rel:E1})");
        Assert.Less(rel, 1e-9);
    }
    [Test] public void Bench_Replica_AlcazarVargas2022()
    {   // published: 4000 N, 60 km/h, 8 deg slip, ~3 min: mu 0.70 -> ~1.05, tread zones up to ~120 C
        var t = new TireThermal(230000); double dt = 0.001, V = 60 / 3.6, a = 8 * Mathf.Deg2Rad, muMax = 0;
        for (int i = 1; i <= 180000; i++)
        {
            double mu = TireThermal.MuOfT(t.GripTemp()); muMax = System.Math.Max(muMax, mu);
            t.Step(dt, mu * 4000 * V * System.Math.Sin(a), 0.012 * 4000 * V, V, 4000, 0, 180000, true);
            if (i % 60000 == 0) Log($"bench {i / 1000} s: surface {t.Ts[1]:0.0} C bulk {t.Tb:0.0} C carcass {t.Tc:0.0} C mu {mu:0.000}");
        }
        Assert.That(t.Ts[1], Is.InRange(90.0, 140.0), "surface in the measured 100-120 C band (+-20 K)");
        Assert.That(muMax, Is.InRange(0.95, 1.1), "peak mu reached ~1.05 measured");
    }
    [Test] public void Highway_Pressure_Rise_vs_Measured()
    {   // published: +3.6-5.6% after 10 min, +9.8-12.2% after 37 min of driving (gauge pressure)
        var t = new TireThermal(170000); double dt = 0.001, V = 100 / 3.6, r10 = 0;
        for (int i = 1; i <= 37 * 60000; i++)
        {
            t.Step(dt, 0, (0.012 + 4e-8 * 100 * 100) * 3000 * V, V, 3000, -0.0175, 180000, true);
            if (i == 10 * 60000) r10 = t.pGauge / 170000 - 1;
        }
        double r37 = t.pGauge / 170000 - 1;
        Log($"highway 100 km/h (62 mph): pressure +{r10 * 100:0.0}% at 10 min, +{r37 * 100:0.0}% at 37 min; gas {TireThermal.CToF(t.Tg):0} F, carcass {TireThermal.CToF(t.Tc):0} F, tread surface {TireThermal.CToF(t.Ts[1]):0} F");
        Assert.That(r37, Is.InRange(0.06, 0.18));
    }
    [Test] public void Sliding_HeatUp_Then_Straight_CoolDown()
    {
        var t = new TireThermal(170000); double dt = 0.001;
        for (int i = 0; i < 30000; i++) t.Step(dt, 8000, 500, 20, 3500, 0, 180000, true);
        double hot = t.Ts[1]; for (int i = 0; i < 30000; i++) t.Step(dt, 0, 800, 27, 3000, 0, 180000, true);
        double cool = t.Ts[1]; Log($"slide 30 s @ 8 kW: surface {TireThermal.CToF(hot):0} F; then 30 s straight 60 mph: {TireThermal.CToF(cool):0} F (bulk {TireThermal.CToF(t.Tb):0} F)");
        Assert.Greater(hot, 60.0); Assert.Less(cool, hot - 20.0); Assert.Greater(cool, TireThermal.TTrack);
    }
    [Test] public void Off_Scale_Is_Identity_And_Optimum_Gives_Base()
    {
        var p = ScriptableObject.CreateInstance<CarParams>();
        foreach (float k in new[] { -0.3f, -0.05f, 0f, 0.02f, 0.2f }) foreach (float a in new[] { -0.2f, 0f, 0.05f, 0.3f })
            Assert.AreEqual(PacejkaTire.Combined(p, k, a, 3200f, 0.01f), PacejkaTire.Combined(p, k, a, 3200f, 0.01f, TireScale.One));
        var t = new TireThermal(TireThermal.PNom, true); var s = t.Scale();
        Assert.AreEqual(1f, s.muy, 1e-6f); Assert.AreEqual(1f, s.ky, 1e-6f); Assert.AreEqual(1f, s.rr, 1e-6f);
        Assert.AreEqual(0.7 / 1.1, TireThermal.MuOfT(TireThermal.TAmb) / 1.1, 1e-3, "cold tire held at the lowest measured mu");
    }
    [Test] public void Grip_Follows_Bulk_Not_Surface()
    {   // option (a): mu(T) is evaluated at the tread bulk temperature (TRT EVO); the surface zones do not set grip
        var t = new TireThermal(TireThermal.PNom);
        for (int i = 0; i < 3; i++) t.Ts[i] = TireThermal.TOpt; t.Tb = TireThermal.TAmb;
        Assert.AreEqual(TireThermal.MuOfT(TireThermal.TAmb) / TireThermal.MuMax, t.Scale().muy, 1e-6, "hot surface, cold bulk -> cold grip");
        for (int i = 0; i < 3; i++) t.Ts[i] = TireThermal.TAmb; t.Tb = TireThermal.TOpt;
        Assert.AreEqual(1.0, t.Scale().muy, 1e-6, "cold surface, bulk at Topt -> full grip");
        Assert.AreEqual(TireThermal.TOpt, t.GripTemp(), 0.0); Assert.AreEqual(TireThermal.TAmb, t.SurfaceTemp(), 1e-9);
    }
    [Test] public void Grip_Retained_After_Bench_Heat_And_Straight()
    {   // reported problem: the surface cools within ~15 s on a straight. Heat the tire as in the Alcazar Vargas bench run (3 min,
        // 4000 N, 37 mph, 8 deg), then 30 s straight at 60 mph: surface grip temp would collapse, bulk-based grip must hold
        var t = new TireThermal(230000); double dt = 0.001, V = 60 / 3.6, a = 8 * Mathf.Deg2Rad;
        for (int i = 0; i < 180000; i++) t.Step(dt, TireThermal.MuOfT(t.GripTemp()) * 4000 * V * System.Math.Sin(a), 0.012 * 4000 * V, V, 4000, 0, 180000, true);
        double muHot = t.Scale().muy, sHot = t.SurfaceTemp(), bHot = t.Tb;
        for (int i = 0; i < 30000; i++) t.Step(dt, 0, 0.012 * 3000 * 27, 27, 3000, 0, 180000, true);
        double muStr = t.Scale().muy, muSurf = TireThermal.MuOfT(t.SurfaceTemp()) / TireThermal.MuMax;
        Log($"bench 3 min then 30 s straight 60 mph: surface {TireThermal.CToF(sHot):0} -> {TireThermal.CToF(t.SurfaceTemp()):0} F, bulk {TireThermal.CToF(bHot):0} -> {TireThermal.CToF(t.Tb):0} F; grip {muHot * 100:0}% -> {muStr * 100:0}% (surface-based would be {muSurf * 100:0}%)");
        Assert.Less(t.SurfaceTemp(), sHot - 20.0, "surface cools fast");
        Assert.Greater(muStr, muHot - 0.05, "bulk-based grip holds through the straight");
        Assert.Greater(muStr, muSurf + 0.05, "and stays above what the surface-based factor would give");
    }
}
