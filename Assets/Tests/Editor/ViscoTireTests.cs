using NUnit.Framework;
using UnityEngine;
// Experimental (viscoelastic) compound: calibration, physical trends, settings migration. Default compound untouched (see also smoke diff).
public class ViscoTireTests
{
    // @description - Creates a default CarParams asset (the Pacejka coefficients the viscoelastic grip factor is referenced to).
    // @return {CarParams} - the car parameters
    static CarParams CreateCarParams() => ScriptableObject.CreateInstance<CarParams>();
    static ViscoTire.Flags AllPhysicsEnabled => new ViscoTire.Flags { hysteresis = true, adhesion = true, williamsLandelFerry = true, flashTemperature = true, payneEffect = true, rateDependentStiffness = true, loadRelaxation = true, rollingResistance = true, bulkHeating = true };

    // @description - Verifies that the default tire compound is Default in CarParams and 0 in a new binding profile.
    // @return {Void} - nothing
    [Test] public void DefaultCompoundIsDefault()
    {
        var carParams = CreateCarParams(); Assert.AreEqual(CarParams.TireCompound.Default, carParams.tireCompound);
        Assert.AreEqual(0, new BindingProfile().tireCompound);
    }
    // @description - Verifies that a bulk friction power of 0 (the Default compound) gives exactly the same node temperatures as an identical run and that the energy balance closes (residual below 0.1 % of heat in).
    // @return {Void} - nothing
    [Test] public void ThermalBulkShareZeroIsNeutral()
    {   // pFricBulk = 0 (Default compound) -> same node temperatures as an identical run (energy closes)
        var referenceTire = new TireThermal(TireThermal.NominalGaugePressure); var bulkSharedTire = new TireThermal(TireThermal.NominalGaugePressure); bulkSharedTire.bulkFrictionPower = 0;
        for (int i = 0; i < 5000; i++) { referenceTire.Step(1e-3, 3000, 200, 20, 3000, 0, 180000, true); bulkSharedTire.Step(1e-3, 3000, 200, 20, 3000, 0, 180000, true); }
        Assert.AreEqual(referenceTire.bulkTemperature, bulkSharedTire.bulkTemperature); Assert.AreEqual(referenceTire.surfaceTemperatures[1], bulkSharedTire.surfaceTemperatures[1]); Assert.Less(System.Math.Abs(referenceTire.Residual), 1e-3 * referenceTire.energyIn);
    }
    // @description - Verifies that sending 500 W of friction power into the bulk makes the bulk hotter and the surface cooler without changing the total heat in, and that energy still balances.
    // @return {Void} - nothing
    [Test] public void BulkShareMovesHeatNotEnergy()
    {
        var referenceTire = new TireThermal(TireThermal.NominalGaugePressure); var bulkSharedTire = new TireThermal(TireThermal.NominalGaugePressure); bulkSharedTire.bulkFrictionPower = 500;
        for (int i = 0; i < 5000; i++) { referenceTire.Step(1e-3, 3000, 200, 20, 3000, 0, 180000, true); bulkSharedTire.Step(1e-3, 3000, 200, 20, 3000, 0, 180000, true); }
        Assert.Greater(bulkSharedTire.bulkTemperature, referenceTire.bulkTemperature); Assert.Less(bulkSharedTire.surfaceTemperatures[1], referenceTire.surfaceTemperatures[1]); Assert.AreEqual(referenceTire.energyIn, bulkSharedTire.energyIn, 1e-6 * referenceTire.energyIn); Assert.Less(System.Math.Abs(bulkSharedTire.Residual), 1e-3 * bulkSharedTire.energyIn);
    }
    // @description - Verifies that the viscoelastic grip factor is exactly 1 at the reference sliding speed and contact pressure for rubber temperatures from 10 to 120 C.
    // @return {Void} - nothing
    [Test] public void GripFactorIsOneAtReference()
    {
        var carParams = CreateCarParams(); double referenceSlipSpeed = ViscoTire.VsRef(carParams); var flags = AllPhysicsEnabled;
        foreach (double temperature in new[] { 10.0, 25.0, 60.0, 88.0, 120.0 })
            Assert.AreEqual(1.0, ViscoTire.GripFactor(referenceSlipSpeed, temperature, ViscoTire.ReferenceContactPressure, referenceSlipSpeed, flags, out _), 1e-12, "T " + temperature);
    }
    // @description - Verifies that the experimental compound's peak lateral force at the reference speed, nominal load and optimum temperature is within 3 % of the default compound's peak, so the viscoelastic factor adds no grip there.
    // @return {Void} - nothing
    [Test] public void ExperimentalPeakMatchesSourcedPeak()
    {   // lateral peak at V_ref, Fz0, optimum temperature: the viscoelastic slip-speed factor may not add grip
        var carParams = CreateCarParams(); double referenceSlipSpeed = ViscoTire.VsRef(carParams); var flags = AllPhysicsEnabled; float peakForceDefault = 0, peakForceExperimental = 0;
        for (float slipAngle = 0.002f; slipAngle < 0.4f; slipAngle += 0.001f)
        {
            peakForceDefault = Mathf.Max(peakForceDefault, PacejkaTire.PureLat(carParams, slipAngle, carParams.nominalVerticalLoad, 0f));
            double gripFactor = ViscoTire.GripFactor(ViscoTire.ReferenceSpeed * System.Math.Tan(slipAngle), TireThermal.OptimalTemperature, ViscoTire.ReferenceContactPressure, referenceSlipSpeed, flags, out _);
            var scale = TireScale.One; scale.lateralFriction = (float)gripFactor; peakForceExperimental = Mathf.Max(peakForceExperimental, PacejkaTire.PureLat(carParams, slipAngle, carParams.nominalVerticalLoad, 0f, scale));
        }
        Debug.Log($"peak Fy default {peakForceDefault:0} N, experimental {peakForceExperimental:0} N ({(peakForceExperimental / peakForceDefault - 1) * 100:+0.00;-0.00} %)");
        Assert.AreEqual(peakForceDefault, peakForceExperimental, 0.03f * peakForceDefault);
    }
    // @description - Verifies that scaling the friction does not change the cornering stiffness (dFy/dalpha at zero slip, within 1 %), because the stiffness factor B = K / (C D) absorbs the peak D.
    // @return {Void} - nothing
    [Test] public void SlipStiffnessUnchangedByGripFactor()
    {   // B = K/(C D): scaling D leaves dFy/dalpha at 0 unchanged
        var carParams = CreateCarParams(); var scale = TireScale.One; scale.lateralFriction = 0.7f; const float slipAngleStep = 1e-4f;
        Assert.AreEqual(PacejkaTire.PureLat(carParams, slipAngleStep, carParams.nominalVerticalLoad, 0f) / slipAngleStep, PacejkaTire.PureLat(carParams, slipAngleStep, carParams.nominalVerticalLoad, 0f, scale) / slipAngleStep, 0.01f * PacejkaTire.PureLat(carParams, slipAngleStep, carParams.nominalVerticalLoad, 0f) / slipAngleStep);
    }
    // @description - Verifies that with flash temperature on, the friction at 88 C has an interior maximum between 1 mm/s and 100 m/s, more than 5 % above the values at both ends (Grosch / Persson master-curve shape).
    // @return {Void} - nothing
    [Test] public void FrictionRisesThenFallsWithSlipSpeed()
    {   // with flashTemperature heating the master curve has an interior maximum (Grosch 1963 / Persson 2006 shape)
        var flags = AllPhysicsEnabled; double bestFriction = 0, bestSpeed = 0, frictionAtLowSpeed = ViscoTire.MuEff(1e-3, 88, ViscoTire.ReferenceContactPressure, flags, out _), frictionAtHighSpeed = ViscoTire.MuEff(100, 88, ViscoTire.ReferenceContactPressure, flags, out _);
        for (double logSpeed = -3; logSpeed <= 2; logSpeed += 0.05) { double slidingSpeed = System.Math.Pow(10, logSpeed), friction = ViscoTire.MuEff(slidingSpeed, 88, ViscoTire.ReferenceContactPressure, flags, out _); if (friction > bestFriction) { bestFriction = friction; bestSpeed = slidingSpeed; } }
        Debug.Log($"mu(88 C): 1 mm/s {frictionAtLowSpeed:0.000}, max {bestFriction:0.000} at {bestSpeed:0.000} m/s, 100 m/s {frictionAtHighSpeed:0.000}");
        Assert.Greater(bestFriction, frictionAtLowSpeed * 1.05); Assert.Greater(bestFriction, frictionAtHighSpeed * 1.05); Assert.Greater(bestSpeed, 1e-3); Assert.Less(bestSpeed, 100);
    }
    // @description - Verifies the Williams-Landel-Ferry shift: the log shift factor falls with temperature, so the speed of peak friction moves higher when the rubber is hotter (80 C above 20 C above -10 C).
    // @return {Void} - nothing
    [Test] public void WlfShiftsPeakToHigherSpeedWhenHot()
    {
        var flags = AllPhysicsEnabled; flags.flashTemperature = false;
        // @description - Scans log10 sliding speed from -8 to 3 (step 0.02) for the speed at which the friction coefficient peaks.
        // @param {double} temperature - tread temperature (degC)
        // @return {double} - log10 of the sliding speed (m/s) of maximum friction
        double PeakLogSpeed(double temperature) { double bestFriction = 0, bestLogSpeed = 0; for (double logSpeed = -8; logSpeed <= 3; logSpeed += 0.02) { double friction = ViscoTire.Mu(System.Math.Pow(10, logSpeed), temperature, flags); if (friction > bestFriction) { bestFriction = friction; bestLogSpeed = logSpeed; } } return bestLogSpeed; }
        Assert.Less(ViscoTire.LogTimeTemperatureShift(60), ViscoTire.LogTimeTemperatureShift(20));
        Assert.Greater(PeakLogSpeed(80), PeakLogSpeed(20)); Assert.Greater(PeakLogSpeed(20), PeakLogSpeed(-10));
    }
    // @description - Verifies the flash temperature rise: 0 K at zero speed, rising with speed (3 m/s above 1 m/s), more than 1 K at 1 m/s and less than 300 K at 3 m/s.
    // @return {Void} - nothing
    [Test] public void FlashTemperaturePhysical()
    {
        Assert.AreEqual(0, ViscoTire.Flash(0, 1, ViscoTire.ReferenceContactPressure), 1e-9);
        double riseAtOneMeter = ViscoTire.Flash(1, 1, ViscoTire.ReferenceContactPressure), riseAtThreeMeters = ViscoTire.Flash(3, 1, ViscoTire.ReferenceContactPressure);
        Debug.Log($"flash rise mu=1: 1 m/s {riseAtOneMeter:0.0} K, 3 m/s {riseAtThreeMeters:0.0} K");
        Assert.Greater(riseAtThreeMeters, riseAtOneMeter); Assert.Greater(riseAtOneMeter, 1); Assert.Less(riseAtThreeMeters, 300);
    }
    // @description - Verifies the viscoelastic modulus fit against sourced DMA data: tan delta 0.206 at 10 Hz and 0 C, 0.160 at 10 Hz and 60 C, a glassy modulus above 1e8 Pa at -60 C, and a stiffer modulus at higher rate.
    // @return {Void} - nothing
    [Test] public void ModulusFitReproducesSourcedDma()
    {
        Assert.AreEqual(0.206, ViscoTire.TanDelta(10, 0), 0.02);
        Assert.AreEqual(0.160, ViscoTire.TanDelta(10, 60), 0.012);
        Assert.Greater(ViscoTire.ModulusAbs(10, -60), 1e8);   // glassy below Tg
        Assert.Greater(ViscoTire.ModulusAbs(1000, 60), ViscoTire.ModulusAbs(10, 60));   // stiffer at higher rate
    }
    // @description - Verifies the rolling resistance factor: 1 at the reference speed and temperature, and a temperature slope between 0.2 and 2 % per degree C (ISO 28580 passenger-tire Kt of about 0.8 %/C).
    // @return {Void} - nothing
    [Test] public void RollingResistanceFactor()
    {
        double rollingRadius = 0.289; Assert.AreEqual(1.0, ViscoTire.RollingFactor(ViscoTire.RollingResistanceReferenceSpeed, rollingRadius, ViscoTire.RollingResistanceReferenceTemperature), 1e-12);
        double hot = ViscoTire.RollingFactor(ViscoTire.RollingResistanceReferenceSpeed, rollingRadius, 35);
        double temperatureSlope = (1 - hot) / 10;   // relative change per degC; ISO 28580 passenger Kt = 0.008 (S)
        Debug.Log($"RR temperature slope {temperatureSlope * 100:0.00} %/C (ISO 28580 Kt 0.80 %/C)");
        Assert.Greater(temperatureSlope, 0.002); Assert.Less(temperatureSlope, 0.02);
    }
    // @description - Verifies the stiffness factor is 1 at the reference speed and contact length, above 1 at 40 m/s and below 1 at 5 m/s.
    // @return {Void} - nothing
    [Test] public void StiffnessRisesWithSpeed()
    {
        double contactLength = 0.1; Assert.AreEqual(1.0, ViscoTire.StiffnessFactor(ViscoTire.ReferenceSpeed, contactLength, contactLength), 1e-12);
        Assert.Greater(ViscoTire.StiffnessFactor(40, contactLength, contactLength), 1.0); Assert.Less(ViscoTire.StiffnessFactor(5, contactLength, contactLength), 1.0);
    }
    // @description - Verifies that 8000 evaluations of the grip, rolling, stiffness and tan-delta functions (1 s of four wheels at 2 kHz) take under 200 ms.
    // @return {Void} - nothing
    [Test] public void RealTimeCost()
    {
        var flags = AllPhysicsEnabled; double referenceSlipSpeed = 0.5; ViscoTire.MuHys(0, true); ViscoTire.MuHys(0, false);
        var stopwatch = System.Diagnostics.Stopwatch.StartNew(); double accumulated = 0;
        for (int i = 0; i < 8000; i++) accumulated += ViscoTire.GripFactor(0.01 + i * 1e-3, 80, ViscoTire.ReferenceContactPressure, referenceSlipSpeed, flags, out _) + ViscoTire.RollingFactor(20, 0.289, 40) + ViscoTire.StiffnessFactor(20, 0.1, 0.1) + ViscoTire.TanDelta(200, 70);
        stopwatch.Stop(); Debug.Log($"1 s of 4 wheels at 2 kHz: {stopwatch.Elapsed.TotalMilliseconds:0.0} ms ({accumulated:0})");
        Assert.Less(stopwatch.Elapsed.TotalMilliseconds, 200);
    }
    // @description - Writes friction versus slip speed, force curves and modulus curves as CSV files for tools/visco_plots.py into the directory in VISCO_DUMP; passes without writing if that variable is unset.
    // @return {Void} - nothing
    [Test] public void DumpCurves()
    {   // CSVs for tools/visco_plots.py when VISCO_DUMP is set
        var directory = System.Environment.GetEnvironmentVariable("VISCO_DUMP"); if (string.IsNullOrEmpty(directory)) { Assert.Pass("VISCO_DUMP not set"); return; }
        System.IO.Directory.CreateDirectory(directory); var flags = AllPhysicsEnabled; var flagsWithoutFlash = flags; flagsWithoutFlash.flashTemperature = false;
        var frictionCsv = new System.Text.StringBuilder("temperature_c,sliding_speed_mps,friction_with_flash,friction_isothermal,friction_hysteretic,friction_adhesive,flash_temperature_rise_k\n");
        foreach (double temperature in new[] { 0.0, 20.0, 40.0, 66.0, 88.0, 110.0 })
            for (double logSpeed = -4; logSpeed <= 2.001; logSpeed += 0.05)
            {
                double slidingSpeed = System.Math.Pow(10, logSpeed), muWithFlash = ViscoTire.MuEff(slidingSpeed, temperature, ViscoTire.ReferenceContactPressure, flags, out double flashRise), muWithoutFlash = ViscoTire.Mu(slidingSpeed, temperature, flagsWithoutFlash);
                var flagsHysteresisOnly = flagsWithoutFlash; flagsHysteresisOnly.adhesion = false; var flagsAdhesionOnly = flagsWithoutFlash; flagsAdhesionOnly.hysteresis = false;
                frictionCsv.AppendLine(JoinInvariant(temperature, slidingSpeed, muWithFlash, muWithoutFlash, ViscoTire.Mu(slidingSpeed, temperature, flagsHysteresisOnly), ViscoTire.Mu(slidingSpeed, temperature, flagsAdhesionOnly), flashRise));
            }
        System.IO.File.WriteAllText(System.IO.Path.Combine(directory, "mu_vs_slipspeed.csv"), frictionCsv.ToString());
        var carParams = CreateCarParams(); double referenceSlipSpeed = ViscoTire.VsRef(carParams); var forceCsv = new System.Text.StringBuilder("speed_mps,temperature_c,slip_angle_rad,lateral_force_default_n,lateral_force_experimental_n\n");
        foreach (double speed in new[] { 10.0, ViscoTire.ReferenceSpeed, 35.0 }) foreach (double temperature in new[] { 40.0, 88.0 })
            for (float slipAngle = 0f; slipAngle < 0.35f; slipAngle += 0.0025f)
            {
                double gripFactor = ViscoTire.GripFactor(speed * System.Math.Tan(slipAngle), temperature, ViscoTire.ReferenceContactPressure, referenceSlipSpeed, flags, out _);
                var scale = TireScale.One; double thermalScale = TireThermal.FrictionAtTemperature(temperature) / TireThermal.PeakFriction; scale.lateralFriction = (float)(thermalScale * gripFactor); var baseScale = TireScale.One; baseScale.lateralFriction = (float)thermalScale;
                forceCsv.AppendLine(JoinInvariant(speed, temperature, slipAngle, PacejkaTire.PureLat(carParams, slipAngle, carParams.nominalVerticalLoad, 0f, baseScale), PacejkaTire.PureLat(carParams, slipAngle, carParams.nominalVerticalLoad, 0f, scale)));
            }
        System.IO.File.WriteAllText(System.IO.Path.Combine(directory, "force_curves.csv"), forceCsv.ToString());
        var modulusCsv = new System.Text.StringBuilder("temperature_c,frequency_hz,loss_tangent,modulus_magnitude_pa\n");
        foreach (double temperature in new[] { -60.0, -40.0, -20.0, 0.0, 20.0, 40.0, 60.0, 80.0, 100.0 }) for (double logFrequency = -1; logFrequency <= 6.001; logFrequency += 0.1) { double frequency = System.Math.Pow(10, logFrequency); modulusCsv.AppendLine(JoinInvariant(temperature, frequency, ViscoTire.TanDelta(frequency, temperature), ViscoTire.ModulusAbs(frequency, temperature))); }
        System.IO.File.WriteAllText(System.IO.Path.Combine(directory, "modulus.csv"), modulusCsv.ToString());
    }
    // @description - Joins numbers into one comma-separated CSV row using the round-trip format and the invariant culture.
    // @param {double[]} values - numbers for one CSV row
    // @return {string} - the CSV row
    static string JoinInvariant(params double[] values) => string.Join(",", System.Array.ConvertAll(values, value => value.ToString("R", System.Globalization.CultureInfo.InvariantCulture)));
}
