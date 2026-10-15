using NUnit.Framework;
using UnityEngine;
// Experimental (viscoelastic) compound: calibration, physical trends, settings migration. Default compound untouched (see also smoke diff).
public class ViscoTireTests
{
    static CarParams P() => ScriptableObject.CreateInstance<CarParams>();
    static ViscoTire.Flags All => new ViscoTire.Flags { hysteresis = true, adhesion = true, williamsLandelFerry = true, flashTemperature = true, payneEffect = true, rateDependentStiffness = true, loadRelaxation = true, rollingResistance = true, bulkHeating = true };

    [Test] public void DefaultCompoundIsDefault()
    {
        var p = P(); Assert.AreEqual(CarParams.TireCompound.Default, p.tireCompound);
        Assert.AreEqual(0, new BindingProfile().tireCompound);
    }
    [Test] public void ThermalBulkShareZeroIsNeutral()
    {   // pFricBulk = 0 (Default compound) -> same node temperatures as an identical run (energy closes)
        var a = new TireThermal(TireThermal.NominalGaugePressure); var b = new TireThermal(TireThermal.NominalGaugePressure); b.bulkFrictionPower = 0;
        for (int i = 0; i < 5000; i++) { a.Step(1e-3, 3000, 200, 20, 3000, 0, 180000, true); b.Step(1e-3, 3000, 200, 20, 3000, 0, 180000, true); }
        Assert.AreEqual(a.bulkTemperature, b.bulkTemperature); Assert.AreEqual(a.surfaceTemperatures[1], b.surfaceTemperatures[1]); Assert.Less(System.Math.Abs(a.Residual), 1e-3 * a.energyIn);
    }
    [Test] public void BulkShareMovesHeatNotEnergy()
    {
        var a = new TireThermal(TireThermal.NominalGaugePressure); var b = new TireThermal(TireThermal.NominalGaugePressure); b.bulkFrictionPower = 500;
        for (int i = 0; i < 5000; i++) { a.Step(1e-3, 3000, 200, 20, 3000, 0, 180000, true); b.Step(1e-3, 3000, 200, 20, 3000, 0, 180000, true); }
        Assert.Greater(b.bulkTemperature, a.bulkTemperature); Assert.Less(b.surfaceTemperatures[1], a.surfaceTemperatures[1]); Assert.AreEqual(a.energyIn, b.energyIn, 1e-6 * a.energyIn); Assert.Less(System.Math.Abs(b.Residual), 1e-3 * b.energyIn);
    }
    [Test] public void GripFactorIsOneAtReference()
    {
        var p = P(); double vr = ViscoTire.VsRef(p); var f = All;
        foreach (double T in new[] { 10.0, 25.0, 60.0, 88.0, 120.0 })
            Assert.AreEqual(1.0, ViscoTire.GripFactor(vr, T, ViscoTire.ReferenceContactPressure, vr, f, out _), 1e-12, "T " + T);
    }
    [Test] public void ExperimentalPeakMatchesSourcedPeak()
    {   // lateral peak at V_ref, Fz0, optimum temperature: the viscoelastic slip-speed factor may not add grip
        var p = P(); double vr = ViscoTire.VsRef(p); var f = All; float pk0 = 0, pk1 = 0;
        for (float a = 0.002f; a < 0.4f; a += 0.001f)
        {
            pk0 = Mathf.Max(pk0, PacejkaTire.PureLat(p, a, p.nominalVerticalLoad, 0f));
            double g = ViscoTire.GripFactor(ViscoTire.ReferenceSpeed * System.Math.Tan(a), TireThermal.OptimalTemperature, ViscoTire.ReferenceContactPressure, vr, f, out _);
            var s = TireScale.One; s.lateralFriction = (float)g; pk1 = Mathf.Max(pk1, PacejkaTire.PureLat(p, a, p.nominalVerticalLoad, 0f, s));
        }
        Debug.Log($"peak Fy default {pk0:0} N, experimental {pk1:0} N ({(pk1 / pk0 - 1) * 100:+0.00;-0.00} %)");
        Assert.AreEqual(pk0, pk1, 0.03f * pk0);
    }
    [Test] public void SlipStiffnessUnchangedByGripFactor()
    {   // B = K/(C D): scaling D leaves dFy/dalpha at 0 unchanged
        var p = P(); var s = TireScale.One; s.lateralFriction = 0.7f; const float da = 1e-4f;
        Assert.AreEqual(PacejkaTire.PureLat(p, da, p.nominalVerticalLoad, 0f) / da, PacejkaTire.PureLat(p, da, p.nominalVerticalLoad, 0f, s) / da, 0.01f * PacejkaTire.PureLat(p, da, p.nominalVerticalLoad, 0f) / da);
    }
    [Test] public void FrictionRisesThenFallsWithSlipSpeed()
    {   // with flashTemperature heating the master curve has an interior maximum (Grosch 1963 / Persson 2006 shape)
        var f = All; double best = 0, vBest = 0, lo = ViscoTire.MuEff(1e-3, 88, ViscoTire.ReferenceContactPressure, f, out _), hi = ViscoTire.MuEff(100, 88, ViscoTire.ReferenceContactPressure, f, out _);
        for (double lv = -3; lv <= 2; lv += 0.05) { double v = System.Math.Pow(10, lv), m = ViscoTire.MuEff(v, 88, ViscoTire.ReferenceContactPressure, f, out _); if (m > best) { best = m; vBest = v; } }
        Debug.Log($"mu(88 C): 1 mm/s {lo:0.000}, max {best:0.000} at {vBest:0.000} m/s, 100 m/s {hi:0.000}");
        Assert.Greater(best, lo * 1.05); Assert.Greater(best, hi * 1.05); Assert.Greater(vBest, 1e-3); Assert.Less(vBest, 100);
    }
    [Test] public void WlfShiftsPeakToHigherSpeedWhenHot()
    {
        var f = All; f.flashTemperature = false;
        double Peak(double T) { double b = 0, vb = 0; for (double lv = -8; lv <= 3; lv += 0.02) { double m = ViscoTire.Mu(System.Math.Pow(10, lv), T, f); if (m > b) { b = m; vb = lv; } } return vb; }
        Assert.Less(ViscoTire.LogTimeTemperatureShift(60), ViscoTire.LogTimeTemperatureShift(20));
        Assert.Greater(Peak(80), Peak(20)); Assert.Greater(Peak(20), Peak(-10));
    }
    [Test] public void FlashTemperaturePhysical()
    {
        Assert.AreEqual(0, ViscoTire.Flash(0, 1, ViscoTire.ReferenceContactPressure), 1e-9);
        double d1 = ViscoTire.Flash(1, 1, ViscoTire.ReferenceContactPressure), d3 = ViscoTire.Flash(3, 1, ViscoTire.ReferenceContactPressure);
        Debug.Log($"flash rise mu=1: 1 m/s {d1:0.0} K, 3 m/s {d3:0.0} K");
        Assert.Greater(d3, d1); Assert.Greater(d1, 1); Assert.Less(d3, 300);
    }
    [Test] public void ModulusFitReproducesSourcedDma()
    {
        Assert.AreEqual(0.206, ViscoTire.TanDelta(10, 0), 0.02);
        Assert.AreEqual(0.160, ViscoTire.TanDelta(10, 60), 0.012);
        Assert.Greater(ViscoTire.ModulusAbs(10, -60), 1e8);   // glassy below Tg
        Assert.Greater(ViscoTire.ModulusAbs(1000, 60), ViscoTire.ModulusAbs(10, 60));   // stiffer at higher rate
    }
    [Test] public void RollingResistanceFactor()
    {
        double r = 0.289; Assert.AreEqual(1.0, ViscoTire.RollingFactor(ViscoTire.RollingResistanceReferenceSpeed, r, ViscoTire.RollingResistanceReferenceTemperature), 1e-12);
        double hot = ViscoTire.RollingFactor(ViscoTire.RollingResistanceReferenceSpeed, r, 35);
        double kt = (1 - hot) / 10;   // relative change per degC; ISO 28580 passenger Kt = 0.008 (S)
        Debug.Log($"RR temperature slope {kt * 100:0.00} %/C (ISO 28580 Kt 0.80 %/C)");
        Assert.Greater(kt, 0.002); Assert.Less(kt, 0.02);
    }
    [Test] public void StiffnessRisesWithSpeed()
    {
        double L = 0.1; Assert.AreEqual(1.0, ViscoTire.StiffnessFactor(ViscoTire.ReferenceSpeed, L, L), 1e-12);
        Assert.Greater(ViscoTire.StiffnessFactor(40, L, L), 1.0); Assert.Less(ViscoTire.StiffnessFactor(5, L, L), 1.0);
    }
    [Test] public void RealTimeCost()
    {
        var f = All; double vr = 0.5; ViscoTire.MuHys(0, true); ViscoTire.MuHys(0, false);
        var sw = System.Diagnostics.Stopwatch.StartNew(); double acc = 0;
        for (int i = 0; i < 8000; i++) acc += ViscoTire.GripFactor(0.01 + i * 1e-3, 80, ViscoTire.ReferenceContactPressure, vr, f, out _) + ViscoTire.RollingFactor(20, 0.289, 40) + ViscoTire.StiffnessFactor(20, 0.1, 0.1) + ViscoTire.TanDelta(200, 70);
        sw.Stop(); Debug.Log($"1 s of 4 wheels at 2 kHz: {sw.Elapsed.TotalMilliseconds:0.0} ms ({acc:0})");
        Assert.Less(sw.Elapsed.TotalMilliseconds, 200);
    }
    [Test] public void DumpCurves()
    {   // CSVs for tools/visco_plots.py when VISCO_DUMP is set
        var dir = System.Environment.GetEnvironmentVariable("VISCO_DUMP"); if (string.IsNullOrEmpty(dir)) { Assert.Pass("VISCO_DUMP not set"); return; }
        System.IO.Directory.CreateDirectory(dir); var f = All; var nf = f; nf.flashTemperature = false;
        var sb = new System.Text.StringBuilder("T,v,mu_flash,mu_noflash,mu_hys,mu_adh,dT\n");
        foreach (double T in new[] { 0.0, 20.0, 40.0, 66.0, 88.0, 110.0 })
            for (double lv = -4; lv <= 2.001; lv += 0.05)
            {
                double v = System.Math.Pow(10, lv), mf = ViscoTire.MuEff(v, T, ViscoTire.ReferenceContactPressure, f, out double dT), mn = ViscoTire.Mu(v, T, nf);
                var fh = nf; fh.adhesion = false; var fa = nf; fa.hysteresis = false;
                sb.AppendLine(FormattableString(T, v, mf, mn, ViscoTire.Mu(v, T, fh), ViscoTire.Mu(v, T, fa), dT));
            }
        System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "mu_vs_slipspeed.csv"), sb.ToString());
        var p = P(); double vr = ViscoTire.VsRef(p); var sc = new System.Text.StringBuilder("V,T,alpha,Fy_default,Fy_exp\n");
        foreach (double V in new[] { 10.0, ViscoTire.ReferenceSpeed, 35.0 }) foreach (double T in new[] { 40.0, 88.0 })
            for (float a = 0f; a < 0.35f; a += 0.0025f)
            {
                double g = ViscoTire.GripFactor(V * System.Math.Tan(a), T, ViscoTire.ReferenceContactPressure, vr, f, out _);
                var s = TireScale.One; double mt = TireThermal.FrictionAtTemperature(T) / TireThermal.PeakFriction; s.lateralFriction = (float)(mt * g); var s0 = TireScale.One; s0.lateralFriction = (float)mt;
                sc.AppendLine(FormattableString(V, T, a, PacejkaTire.PureLat(p, a, p.nominalVerticalLoad, 0f, s0), PacejkaTire.PureLat(p, a, p.nominalVerticalLoad, 0f, s)));
            }
        System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "force_curves.csv"), sc.ToString());
        var md = new System.Text.StringBuilder("T,f,tand,absE\n");
        foreach (double T in new[] { -60.0, -40.0, -20.0, 0.0, 20.0, 40.0, 60.0, 80.0, 100.0 }) for (double lf = -1; lf <= 6.001; lf += 0.1) { double fr = System.Math.Pow(10, lf); md.AppendLine(FormattableString(T, fr, ViscoTire.TanDelta(fr, T), ViscoTire.ModulusAbs(fr, T))); }
        System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "modulus.csv"), md.ToString());
    }
    static string FormattableString(params double[] x) => string.Join(",", System.Array.ConvertAll(x, v => v.ToString("R", System.Globalization.CultureInfo.InvariantCulture)));
}
