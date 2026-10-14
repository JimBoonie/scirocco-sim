// ASSUMPTIONS: coefficients are engineering estimates for a generic modern 185/60R14 summer tire (no test data).
// Lecture graphs (Rouelle/OptimumG) informed curve SHAPES only. Sign convention: +x forward, +y right (wheel frame).
using UnityEngine;
public static class PacejkaTire
{
    static float MF(float B, float C, float D, float E, float x)
    { float bx = B * x; return D * Mathf.Sin(C * Mathf.Atan(bx - E * (bx - Mathf.Atan(bx)))); }
    public static float PureLat(CarParams p, float alpha, float Fz, float gamma) => PureLat(p, alpha, Fz, gamma, TireScale.One);
    public static float PureLat(CarParams p, float alpha, float Fz, float gamma, in TireScale s)
    {
        float dfz = (Fz - p.Fz0) / p.Fz0;
        float D = (p.pDy1 + p.pDy2 * dfz) * s.muy * (1f - 4f * gamma * gamma) * Fz;
        float Ky = p.pKy1 * s.ky * p.Fz0 * Mathf.Sin(2f * Mathf.Atan(Fz / (p.pKy2 * s.kyLoad * p.Fz0)));
        float B = Ky / (p.pCy1 * D + 1e-3f);
        return MF(B, p.pCy1, D, Mathf.Min(p.pEy1, 0.99f), alpha) + p.camberThrust * gamma * Fz;
    }
    public static float PureLong(CarParams p, float kappa, float Fz) => PureLong(p, kappa, Fz, TireScale.One);
    public static float PureLong(CarParams p, float kappa, float Fz, in TireScale s)
    {
        float dfz = (Fz - p.Fz0) / p.Fz0;
        float D = (p.pDx1 + p.pDx2 * dfz) * s.mux * Fz;
        float B = p.pKx1 * s.kx * Fz / (p.pCx1 * D + 1e-3f);
        return MF(B, p.pCx1, D, p.pEx1, kappa);
    }
    // MF5.2-style cosine weighting for combined slip
    public static Vector2 Combined(CarParams p, float kappa, float alpha, float Fz, float gamma) => Combined(p, kappa, alpha, Fz, gamma, TireScale.One);
    public static Vector2 Combined(CarParams p, float kappa, float alpha, float Fz, float gamma, in TireScale s)
    {
        if (Fz <= 0f) return Vector2.zero;
        float gxa = Mathf.Cos(Mathf.Atan(p.rBx1 * alpha));
        float gyk = Mathf.Cos(Mathf.Atan(p.rBy1 * kappa));
        return new Vector2(PureLong(p, kappa, Fz, s) * gxa, PureLat(p, alpha, Fz, gamma, s) * gyk);
    }
}
