// Rear pressure limiter (pressure-dependent proportioning valve, ATE "3/35" type): rear pressure equals the master pressure up to the knee, above it the rear pressure rises by 1/ratio of the master increase.
// One valve sits in each master cylinder outlet; it only acts on the rear line (Bentley 11, section 4.4).

public static class RearPressureLimiter
{
    // @description - rear line pressure for a master cylinder pressure
    // @param {double} masterPressurePascals - master cylinder pressure, Pa gauge
    // @param {double} kneePressurePascals - pressure where the valve starts to limit, Pa
    // @param {double} slopeRatio - master increase per rear increase above the knee (3 = one third)
    // @return {double} - rear pressure in Pa gauge, never negative
    public static double RearPressure(double masterPressurePascals, double kneePressurePascals, double slopeRatio)
    {
        if (masterPressurePascals <= 0.0) return 0.0;
        if (masterPressurePascals <= kneePressurePascals) return masterPressurePascals;
        return kneePressurePascals + (masterPressurePascals - kneePressurePascals) / slopeRatio;
    }
}
