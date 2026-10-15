// ASSUMPTIONS: coefficients are engineering estimates for a generic modern 185/60R14 summer tire (no test data).
// Lecture graphs (Rouelle/OptimumG) informed curve SHAPES only. Sign convention: +x forward, +y right (wheel frame).
using UnityEngine;
public static class PacejkaTire
{
    // @description - Pacejka "magic formula" curve y = D sin(C atan(B x - E (B x - atan(B x)))): the basic tyre force-vs-slip shape
    // @param {float} stiffnessFactor - B, slope factor (1/slip unit)
    // @param {float} shapeFactor - C, shape factor (sets the curve family, lateral ~1.3, longitudinal ~1.6)
    // @param {float} peakFactor - D, peak value of the curve in N
    // @param {float} curvatureFactor - E, curvature near and past the peak
    // @param {float} slip - x, the slip input (rad for slip angle, dimensionless for slip ratio)
    // @return {float} - force in N
    static float MagicFormula(float stiffnessFactor, float shapeFactor, float peakFactor, float curvatureFactor, float slip)
    { float stiffnessTimesSlip = stiffnessFactor * slip; return peakFactor * Mathf.Sin(shapeFactor * Mathf.Atan(stiffnessTimesSlip - curvatureFactor * (stiffnessTimesSlip - Mathf.Atan(stiffnessTimesSlip)))); }   // Pacejka magic formula F = D sin(C atan(B x - E (B x - atan(B x)))); B stiffness, C shape, D peak (N), E curvature (S: Pacejka)

    // @description - pure lateral force with no tyre scaling (all scale factors 1)
    // @param {CarParams} carParams - tyre coefficients
    // @param {float} slipAngle - alpha, slip angle in rad
    // @param {float} verticalLoad - Fz, wheel load in N
    // @param {float} camberAngle - gamma, inclination angle in rad
    // @return {float} - lateral force in N
    public static float PureLat(CarParams carParams, float slipAngle, float verticalLoad, float camberAngle) => PureLat(carParams, slipAngle, verticalLoad, camberAngle, TireScale.One);

    // @description - pure lateral force: peak D = (pDy1 + pDy2 dfz) muy (1 - 4 gamma^2) Fz, cornering stiffness Ky = pKy1 ky Fz0 sin(2 atan(Fz / (pKy2 kyLoad Fz0))),
    //   B = Ky / (C D), plus the camber thrust term camberThrust x gamma x Fz
    // @param {CarParams} carParams - tyre coefficients
    // @param {float} slipAngle - alpha, slip angle in rad
    // @param {float} verticalLoad - Fz, wheel load in N
    // @param {float} camberAngle - gamma, inclination angle in rad
    // @param {TireScale} scale - multiplicative scale factors (friction, stiffness) from the thermal / compound models
    // @return {float} - lateral force in N
    public static float PureLat(CarParams carParams, float slipAngle, float verticalLoad, float camberAngle, in TireScale scale)
    {
        float normalisedLoadChange = (verticalLoad - carParams.nominalVerticalLoad) / carParams.nominalVerticalLoad;   // dfz
        float peakFactor = (carParams.lateralPeakFrictionCoefficient + carParams.lateralPeakFrictionLoadSlope * normalisedLoadChange) * scale.lateralFriction * (1f - 4f * camberAngle * camberAngle) * verticalLoad;
        float corneringStiffness = carParams.lateralStiffnessCoefficient * scale.lateralStiffness * carParams.nominalVerticalLoad * Mathf.Sin(2f * Mathf.Atan(verticalLoad / (carParams.lateralStiffnessLoadCoefficient * scale.lateralStiffnessLoadSaturation * carParams.nominalVerticalLoad)));   // cornering stiffness in N/rad: rises with load and saturates, c1 x Fz0 x sin(2 atan(Fz / (c2 x Fz0))) (S: Pacejka load dependence)
        float stiffnessFactor = corneringStiffness / (carParams.lateralShapeCoefficient * peakFactor + 1e-3f);
        return MagicFormula(stiffnessFactor, carParams.lateralShapeCoefficient, peakFactor, Mathf.Min(carParams.lateralCurvatureCoefficient, 0.99f), slipAngle) + carParams.camberThrustCoefficient * camberAngle * verticalLoad;
    }

    // @description - pure longitudinal force with no tyre scaling (all scale factors 1)
    // @param {CarParams} carParams - tyre coefficients
    // @param {float} slipRatio - kappa, longitudinal slip ratio
    // @param {float} verticalLoad - Fz, wheel load in N
    // @return {float} - longitudinal force in N
    public static float PureLong(CarParams carParams, float slipRatio, float verticalLoad) => PureLong(carParams, slipRatio, verticalLoad, TireScale.One);

    // @description - pure longitudinal force: peak D = (pDx1 + pDx2 dfz) mux Fz, B = pKx1 kx Fz / (C D)
    // @param {CarParams} carParams - tyre coefficients
    // @param {float} slipRatio - kappa, longitudinal slip ratio
    // @param {float} verticalLoad - Fz, wheel load in N
    // @param {TireScale} scale - multiplicative scale factors (friction, stiffness)
    // @return {float} - longitudinal force in N
    public static float PureLong(CarParams carParams, float slipRatio, float verticalLoad, in TireScale scale)
    {
        float normalisedLoadChange = (verticalLoad - carParams.nominalVerticalLoad) / carParams.nominalVerticalLoad;   // dfz
        float peakFactor = (carParams.longitudinalPeakFrictionCoefficient + carParams.longitudinalPeakFrictionLoadSlope * normalisedLoadChange) * scale.longitudinalFriction * verticalLoad;
        float stiffnessFactor = carParams.longitudinalStiffnessCoefficient * scale.longitudinalStiffness * verticalLoad / (carParams.longitudinalShapeCoefficient * peakFactor + 1e-3f);
        return MagicFormula(stiffnessFactor, carParams.longitudinalShapeCoefficient, peakFactor, carParams.longitudinalCurvatureCoefficient, slipRatio);
    }

    // @description - combined slip with no tyre scaling (all scale factors 1)
    // @param {CarParams} carParams - tyre coefficients
    // @param {float} slipRatio - kappa, longitudinal slip ratio
    // @param {float} slipAngle - alpha, slip angle in rad
    // @param {float} verticalLoad - Fz, wheel load in N
    // @param {float} camberAngle - gamma, inclination angle in rad
    // @return {Vector2} - (longitudinal, lateral) force in N
    public static Vector2 Combined(CarParams carParams, float slipRatio, float slipAngle, float verticalLoad, float camberAngle) => Combined(carParams, slipRatio, slipAngle, verticalLoad, camberAngle, TireScale.One);

    // @description - MF5.2-style cosine weighting for combined slip: each pure force is multiplied by cos(atan(r x other slip)),
    //   Gxa = cos(atan(rBx1 alpha)) on the longitudinal force, Gyk = cos(atan(rBy1 kappa)) on the lateral force
    // @param {CarParams} carParams - tyre coefficients
    // @param {float} slipRatio - kappa, longitudinal slip ratio
    // @param {float} slipAngle - alpha, slip angle in rad
    // @param {float} verticalLoad - Fz, wheel load in N
    // @param {float} camberAngle - gamma, inclination angle in rad
    // @param {TireScale} scale - multiplicative scale factors (friction, stiffness)
    // @return {Vector2} - (longitudinal, lateral) force in N; zero if the wheel carries no load
    public static Vector2 Combined(CarParams carParams, float slipRatio, float slipAngle, float verticalLoad, float camberAngle, in TireScale scale)
    {
        if (verticalLoad <= 0f) return Vector2.zero;
        float longitudinalWeight = Mathf.Cos(Mathf.Atan(carParams.combinedSlipLongitudinalCoefficient * slipAngle));   // combined slip: cos(atan(c x other slip)) reduces this force as the other slip grows (dimensionless weight, 0..1)
        float lateralWeight = Mathf.Cos(Mathf.Atan(carParams.combinedSlipLateralCoefficient * slipRatio));   // same reduction for the lateral force as longitudinal slip grows (dimensionless weight, 0..1)
        return new Vector2(PureLong(carParams, slipRatio, verticalLoad, scale) * longitudinalWeight, PureLat(carParams, slipAngle, verticalLoad, camberAngle, scale) * lateralWeight);
    }
}
