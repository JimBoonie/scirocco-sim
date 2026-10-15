// Minimal firing-pulse model for the experimental driveline: instantaneous crank torque ripple of a four-cylinder four-stroke engine.
// Physics: slider-crank kinematics; single-zone first-law cylinder pressure with a Wiebe heat release (no heat loss, constant gamma);
// intake and exhaust strokes at manifold and exhaust pressure; reciprocating inertia torque. The heat released per cycle is NOT a free
// parameter: it is solved so that the cycle-mean gas work equals the mean indicated work of the mean-value engine, so the ripple is added
// on top of the mean torque without changing the mean. Spark timing is the standard maximum-brake-torque rule (50 % burned at a fixed angle ATDC).
// Not modeled (by decision): ignition system, knock, mixture effects, cycle-to-cycle variation, heat transfer.
using UnityEngine;

public class FiringPulseModel
{
    const int tableSamplesPerDegree = 2;                          // numerical resolution of the lookup tables (0.5 deg)
    const double fineStepDegrees = 0.05;                          // numerical step of the pressure integration
    readonly int cylinderCount;
    readonly double heatCapacityRatio, wiebeA, wiebeM, burnDurationRadians, sparkAngleRadians;
    readonly double clearanceVolume, pistonArea, crankRadius, rodLength, reciprocatingMass;
    readonly double[] compressionExpansionTable, heatReleaseTable, exhaustTable, ambientTable, inertiaTable;   // folded over 180 deg, summed over all cylinders
    readonly double compressionExpansionMean, heatReleaseMean, exhaustMean, ambientMean, heatReleaseWorkPerUnitHeat;
    double heatPerCycle, intakePressure, exhaustPressure, ambientPressure, meanWaveformTorque;
    int foldedSampleCount; readonly double[] motoredPressureTable, heatedPressureTable;   // per cylinder over 720 deg (validation)

    public double SparkAdvanceBeforeTopDeadCenterDegrees { get; }   // derived standard spark advance, deg
    public double HeatPerCycleJoules => heatPerCycle;               // heat released per cylinder per cycle at the current operating point

    // @description - builds the lookup tables from the engine geometry and the combustion parameters
    // @param {ShuffleDrivelineParameters} parameters - model parameters
    // @param {CarParams} carParams - engine parameters (displacement, compression ratio, cylinder count)
    public FiringPulseModel(ShuffleDrivelineParameters parameters, CarParams carParams)
    {
        cylinderCount = carParams.cylinders;
        heatCapacityRatio = parameters.cylinderHeatCapacityRatio; wiebeA = parameters.wiebeEfficiencyFactor; wiebeM = parameters.wiebeShapeFactor;
        burnDurationRadians = parameters.burnDurationDegrees * Mathf.Deg2Rad;
        crankRadius = parameters.strokeMeters * 0.5; rodLength = parameters.connectingRodLengthMeters; reciprocatingMass = parameters.reciprocatingMassPerCylinderKilograms;
        double sweptVolumePerCylinder = carParams.displacement / cylinderCount;
        pistonArea = sweptVolumePerCylinder / parameters.strokeMeters;            // D: area consistent with the 1781 cc displacement
        clearanceVolume = sweptVolumePerCylinder / (carParams.compressionRatio - 1.0);
        sparkAngleRadians = StandardSparkAngleRadians(parameters);
        SparkAdvanceBeforeTopDeadCenterDegrees = -sparkAngleRadians * Mathf.Rad2Deg;
        int cycleSamples = 720 * tableSamplesPerDegree; foldedSampleCount = 720 * tableSamplesPerDegree / cylinderCount;
        var unitPressure = new double[cycleSamples]; var heatPressure = new double[cycleSamples];
        IntegrateUnitPressures(cycleSamples, unitPressure, heatPressure); motoredPressureTable = unitPressure; heatedPressureTable = heatPressure;
        var perCylinderCompression = new double[cycleSamples]; var perCylinderHeat = new double[cycleSamples]; var perCylinderExhaust = new double[cycleSamples];
        var perCylinderAmbient = new double[cycleSamples]; var perCylinderInertia = new double[cycleSamples];
        double heatWork = 0.0, stepRadians = Mathf.Deg2Rad / tableSamplesPerDegree;
        for (int sample = 0; sample < cycleSamples; sample++)
        {
            double phaseDegrees = (double)sample / tableSamplesPerDegree, phaseRadians = phaseDegrees * Mathf.Deg2Rad;
            double volumeRate = pistonArea * PistonSpeedPerRadian(phaseRadians);
            bool intakeStroke = phaseDegrees >= 360.0 && phaseDegrees < 540.0, exhaustStroke = phaseDegrees >= 180.0 && phaseDegrees < 360.0;
            perCylinderCompression[sample] = (intakeStroke ? 1.0 : exhaustStroke ? 0.0 : unitPressure[sample]) * volumeRate;
            perCylinderHeat[sample] = (intakeStroke || exhaustStroke ? 0.0 : heatPressure[sample]) * volumeRate;
            perCylinderExhaust[sample] = (exhaustStroke ? 1.0 : 0.0) * volumeRate;
            perCylinderAmbient[sample] = volumeRate;
            perCylinderInertia[sample] = -reciprocatingMass * PistonSpeedPerRadian(phaseRadians) * PistonAccelerationPerRadianSquared(phaseRadians);
            heatWork += perCylinderHeat[sample] * stepRadians;
        }
        heatReleaseWorkPerUnitHeat = heatWork;
        compressionExpansionTable = Fold(perCylinderCompression); heatReleaseTable = Fold(perCylinderHeat); exhaustTable = Fold(perCylinderExhaust);
        ambientTable = Fold(perCylinderAmbient); inertiaTable = Fold(perCylinderInertia);
        compressionExpansionMean = Mean(compressionExpansionTable); heatReleaseMean = Mean(heatReleaseTable); exhaustMean = Mean(exhaustTable); ambientMean = Mean(ambientTable);
    }

    // @description - standard (maximum-brake-torque) spark angle: spark so that 50 % of the charge is burned at the configured angle ATDC.
    //   Wiebe x = 1 - exp(-a ((t)/D)^(m+1)) reaches 0.5 at t = D (ln 2 / a)^(1/(m+1)) after the spark (D)
    // @param {ShuffleDrivelineParameters} parameters - model parameters
    // @return {double} - spark crank angle relative to firing TDC in rad (negative = before TDC)
    static double StandardSparkAngleRadians(ShuffleDrivelineParameters parameters)
    {
        double halfBurnDelayFraction = System.Math.Pow(System.Math.Log(2.0) / parameters.wiebeEfficiencyFactor, 1.0 / (parameters.wiebeShapeFactor + 1.0));
        return (parameters.halfBurnAngleAfterTopDeadCenterDegrees - parameters.burnDurationDegrees * halfBurnDelayFraction) * Mathf.Deg2Rad;
    }

    // @description - piston distance below TDC for a crank angle from TDC, x = a (1 - cos t) + l - sqrt(l^2 - a^2 sin^2 t)
    // @param {double} crankAngleRadians - rad from TDC
    // @return {double} - m
    double PistonPosition(double crankAngleRadians)
    {
        double sine = System.Math.Sin(crankAngleRadians);
        return crankRadius * (1.0 - System.Math.Cos(crankAngleRadians)) + rodLength - System.Math.Sqrt(rodLength * rodLength - crankRadius * crankRadius * sine * sine);
    }

    // @description - dx/dtheta of the piston
    // @param {double} crankAngleRadians - rad from TDC
    // @return {double} - m/rad
    double PistonSpeedPerRadian(double crankAngleRadians)
    {
        double sine = System.Math.Sin(crankAngleRadians), cosine = System.Math.Cos(crankAngleRadians);
        double root = System.Math.Sqrt(rodLength * rodLength - crankRadius * crankRadius * sine * sine);
        return crankRadius * sine + crankRadius * crankRadius * sine * cosine / root;
    }

    // @description - d2x/dtheta2 of the piston
    // @param {double} crankAngleRadians - rad from TDC
    // @return {double} - m/rad^2
    double PistonAccelerationPerRadianSquared(double crankAngleRadians)
    {
        double sine = System.Math.Sin(crankAngleRadians), cosine = System.Math.Cos(crankAngleRadians), a2 = crankRadius * crankRadius;
        double root = System.Math.Sqrt(rodLength * rodLength - a2 * sine * sine);
        return crankRadius * cosine + a2 * (cosine * cosine - sine * sine) / root + a2 * a2 * sine * sine * cosine * cosine / (root * root * root);
    }

    // @description - cylinder volume at a crank angle from TDC
    // @param {double} crankAngleRadians - rad from TDC
    // @return {double} - m^3
    double CylinderVolume(double crankAngleRadians) => clearanceVolume + pistonArea * PistonPosition(crankAngleRadians);

    // @description - Wiebe burn rate d(x_b)/d(theta), renormalized so the burned fraction reaches 1 at the end of the duration
    // @param {double} crankAngleRadians - rad from TDC
    // @return {double} - 1/rad
    double BurnRatePerRadian(double crankAngleRadians)
    {
        double elapsed = crankAngleRadians - sparkAngleRadians;
        if (elapsed <= 0.0 || elapsed >= burnDurationRadians) return 0.0;
        double fraction = elapsed / burnDurationRadians, normalization = 1.0 - System.Math.Exp(-wiebeA);
        return wiebeA * (wiebeM + 1.0) / burnDurationRadians * System.Math.Pow(fraction, wiebeM) * System.Math.Exp(-wiebeA * System.Math.Pow(fraction, wiebeM + 1.0)) / normalization;
    }

    // @description - first-law pressure derivatives. p = p_intake m(t) + Q g(t) is linear, so m (motored, unit start pressure) and g (per joule
    //   of heat) are integrated separately: dm/dt = -gamma m V'/V, dg/dt = -gamma g V'/V + (gamma - 1) w/V (Heywood, ch. 9)
    // @param {double} crankAngleRadians - rad from TDC
    // @param {double} motored - m at that angle
    // @param {double} heated - g at that angle in Pa/J
    // @param {double} motoredRate - (out) dm/dtheta
    // @param {double} heatedRate - (out) dg/dtheta
    void PressureDerivatives(double crankAngleRadians, double motored, double heated, out double motoredRate, out double heatedRate)
    {
        double volume = CylinderVolume(crankAngleRadians), volumeRate = pistonArea * PistonSpeedPerRadian(crankAngleRadians);
        motoredRate = -heatCapacityRatio * motored * volumeRate / volume;
        heatedRate = -heatCapacityRatio * heated * volumeRate / volume + (heatCapacityRatio - 1.0) * BurnRatePerRadian(crankAngleRadians) / volume;
    }

    // @description - integrates m and g from intake valve closing (BDC, -180 deg) through TDC to +180 deg with RK4 and samples them at the table angles
    // @param {int} cycleSamples - table length over 720 deg
    // @param {double[]} unitPressure - (out) m at each table phase (compression/expansion strokes only valid)
    // @param {double[]} heatPressure - (out) g at each table phase
    void IntegrateUnitPressures(int cycleSamples, double[] unitPressure, double[] heatPressure)
    {
        double stepRadians = fineStepDegrees * Mathf.Deg2Rad, angle = -Mathf.PI, motored = 1.0, heated = 0.0;
        int fineStepsPerSample = (int)System.Math.Round(1.0 / tableSamplesPerDegree / fineStepDegrees), samplesToFill = 360 * tableSamplesPerDegree + 1;
        for (int sample = 0; sample < samplesToFill; sample++)
        {
            int phaseSample = (sample + 540 * tableSamplesPerDegree) % cycleSamples;   // -180 deg is phase 540 deg
            unitPressure[phaseSample] = motored; heatPressure[phaseSample] = heated;
            for (int fine = 0; fine < fineStepsPerSample && sample < samplesToFill - 1; fine++)
            {
                PressureDerivatives(angle, motored, heated, out double k1m, out double k1h);
                PressureDerivatives(angle + 0.5 * stepRadians, motored + 0.5 * stepRadians * k1m, heated + 0.5 * stepRadians * k1h, out double k2m, out double k2h);
                PressureDerivatives(angle + 0.5 * stepRadians, motored + 0.5 * stepRadians * k2m, heated + 0.5 * stepRadians * k2h, out double k3m, out double k3h);
                PressureDerivatives(angle + stepRadians, motored + stepRadians * k3m, heated + stepRadians * k3h, out double k4m, out double k4h);
                motored += stepRadians / 6.0 * (k1m + 2.0 * k2m + 2.0 * k3m + k4m); heated += stepRadians / 6.0 * (k1h + 2.0 * k2h + 2.0 * k3h + k4h); angle += stepRadians;
            }
        }
    }

    // @description - folds a per-cylinder table over 720 deg into the sum over all cylinders as a function of crank angle modulo 180 deg
    //   (cylinders are 720/N deg apart and identical, so the total repeats every 720/N deg)
    // @param {double[]} perCylinder - table over 720 deg
    // @return {double[]} - table over 180 deg
    double[] Fold(double[] perCylinder)
    {
        var folded = new double[foldedSampleCount]; int cycleSamples = perCylinder.Length, offsetSamples = cycleSamples / cylinderCount;
        for (int sample = 0; sample < foldedSampleCount; sample++) for (int cylinder = 0; cylinder < cylinderCount; cylinder++) folded[sample] += perCylinder[(sample + cylinder * offsetSamples) % cycleSamples];
        return folded;
    }

    // @description - arithmetic mean of a table
    // @param {double[]} table - values
    // @return {double} - mean
    static double Mean(double[] table) { double sum = 0.0; foreach (double value in table) sum += value; return sum / table.Length; }

    // @description - linear interpolation in a folded table at a crank angle (periodic over 180 deg)
    // @param {double[]} table - folded table
    // @param {double} crankAngleRadians - crank angle in rad
    // @return {double} - interpolated value
    double Lookup(double[] table, double crankAngleRadians)
    {
        double periodRadians = Mathf.PI * 4.0 / cylinderCount, position = crankAngleRadians / periodRadians; position -= System.Math.Floor(position);
        double index = position * foldedSampleCount; int lower = (int)index; if (lower >= foldedSampleCount) lower = foldedSampleCount - 1; double fraction = index - lower;
        return table[lower] * (1.0 - fraction) + table[(lower + 1) % foldedSampleCount] * fraction;
    }

    // @description - sets the operating point for the coming interval and solves the heat per cycle from the mean indicated work
    // @param {double} intakePressurePascals - manifold pressure at intake valve closing (Pa)
    // @param {double} exhaustPressurePascals - exhaust port pressure (Pa)
    // @param {double} ambientPressurePascals - crankcase / ambient pressure under the pistons (Pa)
    // @param {double} indicatedMeanTorque - mean indicated torque of the whole engine in N m (0 when fuel is cut)
    // @return {Void}
    public void SetOperatingPoint(double intakePressurePascals, double exhaustPressurePascals, double ambientPressurePascals, double indicatedMeanTorque)
    {
        intakePressure = intakePressurePascals; exhaustPressure = exhaustPressurePascals; ambientPressure = ambientPressurePascals;
        double workPerCylinderPerCycle = indicatedMeanTorque * 4.0 * Mathf.PI / cylinderCount;   // W = T 4 pi for a four-stroke
        heatPerCycle = heatReleaseWorkPerUnitHeat > 0.0 ? workPerCylinderPerCycle / heatReleaseWorkPerUnitHeat : 0.0;
        meanWaveformTorque = WaveformMean();
    }

    // @description - cycle mean of the gas torque waveform at the current operating point
    // @return {double} - N m
    double WaveformMean() => intakePressure * compressionExpansionMean + heatPerCycle * heatReleaseMean + exhaustPressure * exhaustMean - ambientPressure * ambientMean;

    // @description - torque ripple about the mean: gas torque of all cylinders minus its cycle mean, plus reciprocating inertia torque
    // @param {double} crankAngleRadians - crank angle in rad
    // @param {double} crankAngularVelocity - crank speed in rad/s
    // @return {double} - N m, zero mean over a cycle
    public double RippleTorque(double crankAngleRadians, double crankAngularVelocity)
    {
        double gas = intakePressure * Lookup(compressionExpansionTable, crankAngleRadians) + heatPerCycle * Lookup(heatReleaseTable, crankAngleRadians)
                   + exhaustPressure * Lookup(exhaustTable, crankAngleRadians) - ambientPressure * Lookup(ambientTable, crankAngleRadians);
        return gas - meanWaveformTorque + crankAngularVelocity * crankAngularVelocity * Lookup(inertiaTable, crankAngleRadians);
    }

    // @description - in-cylinder pressure of one cylinder during compression and expansion, for validation
    // @param {double} crankAngleFromTopDeadCenterRadians - rad in [-pi, pi]
    // @return {double} - Pa
    public double CylinderPressure(double crankAngleFromTopDeadCenterRadians)
    {
        double phaseDegrees = crankAngleFromTopDeadCenterRadians * Mathf.Rad2Deg; if (phaseDegrees < 0.0) phaseDegrees += 720.0;
        int sample = (int)System.Math.Round(phaseDegrees * tableSamplesPerDegree) % motoredPressureTable.Length;
        return intakePressure * motoredPressureTable[sample] + heatPerCycle * heatedPressureTable[sample];
    }
}
