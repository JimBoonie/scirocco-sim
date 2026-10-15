// Tire thermal model (TireThermal.cs): ideal gas, energy balance, published-data replicas (bench heat-up, highway pressure rise), cool-down, off = identity.
using NUnit.Framework; using UnityEngine;
public class TireThermalTests
{
    // @description - Writes a labeled line to the Unity console and the NUnit output so the thermal results appear in the test report.
    // @param {string} message - text to log after the prefix
    // @return {Void} - nothing
    static void LogThermal(string message) { Debug.Log("THERMAL " + message); TestContext.Out.WriteLine(message); }
    // @description - Verifies that the cold gauge pressure equals the set pressure and that heating the gas by 40 K raises the absolute pressure in the ratio of absolute temperatures (p/T constant, 1e-12).
    // @return {Void} - nothing
    [Test] public void Pressure_Follows_Ideal_Gas_At_Constant_Volume()
    {
        var tire = new TireThermal(170000);
        Assert.AreEqual(170000.0, tire.gaugePressure, 1e-6, "cold gauge pressure at the set temperature");
        tire.gasTemperature = TireThermal.AmbientTemperature + 40.0; tire.UpdatePressure();
        double ratio = (tire.gaugePressure + TireThermal.AtmosphericPressure) / (170000.0 + TireThermal.AtmosphericPressure), temperatureRatio = (tire.gasTemperature + 273.15) / (TireThermal.AmbientTemperature + 273.15);
        Assert.AreEqual(temperatureRatio, ratio, 1e-12);
        LogThermal($"ideal gas: +40 K gas -> {tire.gaugePressure / TireThermal.PaPerPsi:0.00} psi gauge (cold 24.66), abs ratio {ratio:0.0000} = T ratio {temperatureRatio:0.0000}");
    }
    // @description - Verifies energy conservation: after 300 s of varying speed, load and friction power the heat in, heat out and stored energy balance with a relative residual below 1e-9.
    // @return {Void} - nothing
    [Test] public void Energy_Balance_Closes()
    {
        var tire = new TireThermal(170000); double dt = 0.001;
        for (int i = 0; i < 300000; i++)
        {
            double elapsedSeconds = i * dt, speed = 10 + 15 * Mathf.Sin((float)(elapsedSeconds * 0.05)), frictionPower = (i / 20000) % 2 == 0 ? 6000 : 0;
            tire.Step(dt, frictionPower, 600, speed, 3000 + 800 * Mathf.Sin((float)elapsedSeconds), -0.03, 180000, (i / 50000) % 6 != 5);
        }
        double rel = System.Math.Abs(tire.Residual) / tire.energyIn;
        LogThermal($"energy: in {tire.energyIn / 1e6:0.000} MJ out {tire.energyOut / 1e6:0.000} MJ stored {(tire.StoredEnergy() - tire.initialStoredEnergy) / 1e6:0.000} MJ residual {tire.Residual:0.00} J ({rel:E1})");
        Assert.Less(rel, 1e-9);
    }
    // @description - Replicates the published bench test (Alcazar-Vargas 2022: 4000 N, 60 km/h, 8 deg slip, 3 min): verifies surface temperature of 90 to 140 C and a peak friction coefficient of 0.95 to 1.1.
    // @return {Void} - nothing
    [Test] public void Bench_Replica_AlcazarVargas2022()
    {   // published: 4000 N, 60 km/h, 8 deg slip, ~3 min: mu 0.70 -> ~1.05, tread zones up to ~120 C
        var tire = new TireThermal(230000); double dt = 0.001, speed = 60 / 3.6, slipAngle = 8 * Mathf.Deg2Rad, muMax = 0;
        for (int i = 1; i <= 180000; i++)
        {
            double mu = TireThermal.BenchFriction(tire.SurfaceTemp()); muMax = System.Math.Max(muMax, mu);
            tire.Step(dt, mu * 4000 * speed * System.Math.Sin(slipAngle), 0.012 * 4000 * speed, speed, 4000, 0, 180000, true);
            if (i % 60000 == 0) LogThermal($"bench {i / 1000} s: surface {tire.surfaceTemperatures[1]:0.0} C bulk {tire.bulkTemperature:0.0} C carcass {tire.carcassTemperature:0.0} C mu {mu:0.000}");
        }
        Assert.That(tire.surfaceTemperatures[1], Is.InRange(90.0, 140.0), "surface in the measured 100-120 C band (+-20 K)");
        Assert.That(muMax, Is.InRange(0.95, 1.1), "peak mu reached ~1.05 measured");
    }
    // @description - Replicates published highway data (100 km/h): verifies that the gauge pressure rise after 37 minutes is between 6 % and 18 % (published +3.6-5.6 % at 10 min, +9.8-12.2 % at 37 min).
    // @return {Void} - nothing
    [Test] public void Highway_Pressure_Rise_vs_Measured()
    {   // published: +3.6-5.6% after 10 min, +9.8-12.2% after 37 min of driving (gauge pressure)
        var tire = new TireThermal(170000); double dt = 0.001, speed = 100 / 3.6, pressureRise10Min = 0;
        for (int i = 1; i <= 37 * 60000; i++)
        {
            tire.Step(dt, 0, (0.012 + 4e-8 * 100 * 100) * 3000 * speed, speed, 3000, -0.0175, 180000, true);
            if (i == 10 * 60000) pressureRise10Min = tire.gaugePressure / 170000 - 1;
        }
        double pressureRise37Min = tire.gaugePressure / 170000 - 1;
        LogThermal($"highway 100 km/h (62 mph): pressure +{pressureRise10Min * 100:0.0}% at 10 min, +{pressureRise37Min * 100:0.0}% at 37 min; gas {TireThermal.CelsiusToFahrenheit(tire.gasTemperature):0} F, carcass {TireThermal.CelsiusToFahrenheit(tire.carcassTemperature):0} F, tread surface {TireThermal.CelsiusToFahrenheit(tire.surfaceTemperatures[1]):0} F");
        Assert.That(pressureRise37Min, Is.InRange(0.06, 0.18));
    }
    // @description - Verifies that 30 s of sliding at 8 kW heats the surface above 60 C and 30 s straight at 27 m/s cools it by more than 20 K, but not below the track temperature.
    // @return {Void} - nothing
    [Test] public void Sliding_HeatUp_Then_Straight_CoolDown()
    {
        var tire = new TireThermal(170000); double dt = 0.001;
        for (int i = 0; i < 30000; i++) tire.Step(dt, 8000, 500, 20, 3500, 0, 180000, true);
        double hot = tire.surfaceTemperatures[1]; for (int i = 0; i < 30000; i++) tire.Step(dt, 0, 800, 27, 3000, 0, 180000, true);
        double cool = tire.surfaceTemperatures[1]; LogThermal($"slide 30 s @ 8 kW: surface {TireThermal.CelsiusToFahrenheit(hot):0} F; then 30 s straight 60 mph: {TireThermal.CelsiusToFahrenheit(cool):0} F (bulk {TireThermal.CelsiusToFahrenheit(tire.bulkTemperature):0} F)");
        Assert.Greater(hot, 60.0); Assert.Less(cool, hot - 20.0); Assert.Greater(cool, TireThermal.TrackTemperature);
    }
    // @description - Verifies that with the thermal model off the tire scale is identity (combined force equals the unscaled force; friction, stiffness and rolling resistance scales are 1) and that the friction curves give their documented reference values.
    // @return {Void} - nothing
    [Test] public void Off_Scale_Is_Identity_And_Optimum_Gives_Base()
    {
        var carParams = ScriptableObject.CreateInstance<CarParams>();
        foreach (float slipRatio in new[] { -0.3f, -0.05f, 0f, 0.02f, 0.2f }) foreach (float slipAngle in new[] { -0.2f, 0f, 0.05f, 0.3f })
            Assert.AreEqual(PacejkaTire.Combined(carParams, slipRatio, slipAngle, 3200f, 0.01f), PacejkaTire.Combined(carParams, slipRatio, slipAngle, 3200f, 0.01f, TireScale.One));
        var tire = new TireThermal(TireThermal.NominalGaugePressure, true); var scale = tire.Scale();
        Assert.AreEqual(1f, scale.lateralFriction, 1e-6f); Assert.AreEqual(1f, scale.lateralStiffness, 1e-6f); Assert.AreEqual(1f, scale.rollingResistance, 1e-6f);
        Assert.AreEqual(0.7 / 1.1, TireThermal.BenchFriction(TireThermal.AmbientTemperature) / 1.1, 1e-3, "bench curve alone still holds the lowest measured mu");
        Assert.AreEqual(1.0, TireThermal.FrictionAtTemperature(TireThermal.TrackTemperature) / TireThermal.PeakFriction, 1e-12, "road data: full grip at 70 F (option b)");
    }
    // @description - Verifies that the grip temperature is the heat-capacity-weighted mean of the three surface zones and the bulk, and that a tread at the optimal temperature gives the base friction.
    // @return {Void} - nothing
    [Test] public void Grip_Follows_Tread_Mean()
    {   // grip temperature = heat-capacity-weighted tread mean (skin zones + bulk)
        var tire = new TireThermal(TireThermal.NominalGaugePressure);
        for (int i = 0; i < 3; i++) tire.surfaceTemperatures[i] = TireThermal.OptimalTemperature; tire.bulkTemperature = TireThermal.OptimalTemperature;
        Assert.AreEqual(1.0, tire.Scale().lateralFriction, 1e-6, "whole tread at Topt -> base grip");
        tire.surfaceTemperatures[0] = 100; tire.surfaceTemperatures[1] = 60; tire.surfaceTemperatures[2] = 20; tire.bulkTemperature = 40;
        double skinWeight = 3 * TireThermal.SkinHeatCapacity / (3 * TireThermal.SkinHeatCapacity + TireThermal.BulkHeatCapacity);
        Assert.AreEqual(skinWeight * 60 + (1 - skinWeight) * 40, tire.GripTemp(), 1e-9);
    }
    // @description - Verifies that the cold-side road friction data joins the bench curve continuously at the crossover temperature, falls monotonically towards it, and gives full grip at 20 C and the road slope at 35 C.
    // @return {Void} - nothing
    [Test] public void ColdSide_Road_Data_Continuous_Into_Bench()
    {
        double crossoverTemperature = TireThermal.CrossoverTemperature;
        Assert.That(crossoverTemperature, Is.InRange(TireThermal.BenchFitLowTemperature, TireThermal.OptimalTemperature));
        Assert.AreEqual(TireThermal.BenchFriction(crossoverTemperature), TireThermal.RoadFriction(crossoverTemperature), 1e-8, "continuous at the crossover");
        Assert.AreEqual(TireThermal.FrictionAtTemperature(crossoverTemperature - 1e-7), TireThermal.FrictionAtTemperature(crossoverTemperature + 1e-7), 1e-6);
        double previousFriction = 2; for (double celsius = -10; celsius < crossoverTemperature; celsius += 0.5) { double friction = TireThermal.FrictionAtTemperature(celsius); Assert.LessOrEqual(friction, previousFriction + 1e-12); previousFriction = friction; }
        Assert.AreEqual(1.0, TireThermal.FrictionAtTemperature(20.0) / TireThermal.PeakFriction, 1e-12, "68 F: full grip");
        Assert.AreEqual(1.0 - TireThermal.RoadGripLossPerDegree * 10.0, TireThermal.FrictionAtTemperature(35.0) / TireThermal.PeakFriction, 1e-12, "95 F: road slope");
        LogThermal($"crossover {crossoverTemperature:0.00} C = {TireThermal.CelsiusToFahrenheit(crossoverTemperature):0.0} F, mu ratio {TireThermal.FrictionAtTemperature(crossoverTemperature) / TireThermal.PeakFriction:0.0000}");
    }
    // @description - Verifies the reported case of a 270 F surface over a 77 F bulk: grip is taken from the tread mean (not the surface only) and stays above 95 % of base, so the tire does not feel icy.
    // @return {Void} - nothing
    [Test] public void Hot_Surface_270F_Over_Cool_Bulk()
    {   // Taylor's reading: fronts ~270 F on the I/M/O display but only ~70% grip (bulk-based grip, cold hold)
        var tire = new TireThermal(TireThermal.NominalGaugePressure); double surfaceTemperature = (270.0 - 32.0) * 5.0 / 9.0;
        for (int i = 0; i < 3; i++) tire.surfaceTemperatures[i] = surfaceTemperature; tire.bulkTemperature = 25.0;
        double before = TireThermal.BenchFriction(tire.bulkTemperature) / TireThermal.PeakFriction, surfOnly = TireThermal.BenchFriction(surfaceTemperature) / TireThermal.PeakFriction, after = tire.Scale().lateralFriction;
        LogThermal($"270 F surface / 77 F bulk: grip temp {TireThermal.CelsiusToFahrenheit(tire.GripTemp()):0} F; grip before (bulk, cold hold) {before * 100:0}%, surface-only would be {surfOnly * 100:0}%, now {after * 100:0}%");
        Assert.AreEqual(TireThermal.FrictionAtTemperature(tire.GripTemp()) / TireThermal.PeakFriction, after, 1e-6); Assert.Greater(after, 0.95, "no ice feel");
    }
    // @description - Verifies that after a 3 min bench-style heat-up and 30 s straight at 60 mph the surface cools by more than 20 K while grip stays within 0.1 of the hot value and above the surface-only grip.
    // @return {Void} - nothing
    [Test] public void Grip_Retained_After_Bench_Heat_And_Straight()
    {   // reported problem: the surface cools within ~15 s on a straight. Heat the tire as in the Alcazar Vargas bench run (3 min,
        // 4000 N, 37 mph, 8 deg), then 30 s straight at 60 mph: a surface-only grip temp would collapse, tread-mean grip must hold
        var tire = new TireThermal(230000); double dt = 0.001, speed = 60 / 3.6, slipAngle = 8 * Mathf.Deg2Rad;
        for (int i = 0; i < 180000; i++) tire.Step(dt, TireThermal.BenchFriction(tire.SurfaceTemp()) * 4000 * speed * System.Math.Sin(slipAngle), 0.012 * 4000 * speed, speed, 4000, 0, 180000, true);
        double muHot = tire.Scale().lateralFriction, surfaceHot = tire.SurfaceTemp(), bulkHot = tire.bulkTemperature;
        for (int i = 0; i < 30000; i++) tire.Step(dt, 0, 0.012 * 3000 * 27, 27, 3000, 0, 180000, true);
        double muStraight = tire.Scale().lateralFriction, muSurfaceOnly = TireThermal.BenchFriction(tire.SurfaceTemp()) / TireThermal.PeakFriction;
        LogThermal($"bench 3 min then 30 s straight 60 mph: surface {TireThermal.CelsiusToFahrenheit(surfaceHot):0} -> {TireThermal.CelsiusToFahrenheit(tire.SurfaceTemp()):0} F, bulk {TireThermal.CelsiusToFahrenheit(bulkHot):0} -> {TireThermal.CelsiusToFahrenheit(tire.bulkTemperature):0} F; grip {muHot * 100:0}% -> {muStraight * 100:0}% (old bench-only curve on the surface would be {muSurfaceOnly * 100:0}%)");
        Assert.Less(tire.SurfaceTemp(), surfaceHot - 20.0, "surface cools fast");
        Assert.Greater(muStraight, muHot - 0.1, "grip holds through the straight (tread mean passes the ~151 F road/bench crossover dip, SD)");
        Assert.Greater(muStraight, muSurfaceOnly + 0.05, "and stays above the old bench-only cold hold");
    }
}
