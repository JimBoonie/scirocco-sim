// Pure equations of a single-cone Borg-Warner / ZF-B type synchronizer (no state). Sources: Schaeffler / INA "Synchronizer systems" (API 06) eq. for the cone torque
// M_K and the blocking (index) torque M_S; Chalmers 2018 eqs. 2.172-2.176 for the synchronization time; Farokhi Nejad et al. 2017 for the 0.69 s test-rig check.
using UnityEngine;

public static class SynchronizerPhysics
{
    // @description - cone (synchronizing) torque of a single cone, M_K = mu F_a R / sin(alpha); the wedge multiplies the axial force by 1 / sin(alpha)
    // @param {double} axialForceNewtons - sleeve force on the ring in N
    // @param {double} frictionCoefficient - cone friction coefficient at the current sliding speed
    // @param {double} meanConeRadiusMeters - mean cone radius in m
    // @param {double} coneHalfAngleRadians - cone half angle in rad
    // @return {double} - cone torque in N m (magnitude)
    public static double ConeTorque(double axialForceNewtons, double frictionCoefficient, double meanConeRadiusMeters, double coneHalfAngleRadians)
        => frictionCoefficient * axialForceNewtons * meanConeRadiusMeters / System.Math.Sin(coneHalfAngleRadians);

    // @description - geometry factor f(beta, mu_S) of the roof-shaped blocking teeth, f = (cos(beta/2) - mu_S sin(beta/2)) / (sin(beta/2) + mu_S cos(beta/2))
    // @param {double} roofIncludedAngleRadians - included roof angle beta in rad
    // @param {double} toothFrictionCoefficient - friction coefficient mu_S of the roof flanks
    // @return {double} - dimensionless factor, M_S = F_a R_S f
    public static double RoofFactor(double roofIncludedAngleRadians, double toothFrictionCoefficient)
    {
        double half = 0.5 * roofIncludedAngleRadians;
        return (System.Math.Cos(half) - toothFrictionCoefficient * System.Math.Sin(half)) / (System.Math.Sin(half) + toothFrictionCoefficient * System.Math.Cos(half));
    }

    // @description - blocking (index) torque of the roof teeth, M_S = F_a R_S f (Schaeffler eq. 11). Blocking holds while the cone torque is at least this large
    // @param {double} axialForceNewtons - sleeve force in N
    // @param {double} blockingToothRadiusMeters - mean radius R_S of the roof teeth in m
    // @param {double} roofIncludedAngleRadians - roof angle beta in rad
    // @param {double} toothFrictionCoefficient - roof flank friction mu_S
    // @return {double} - blocking torque in N m
    public static double BlockingTorque(double axialForceNewtons, double blockingToothRadiusMeters, double roofIncludedAngleRadians, double toothFrictionCoefficient)
        => axialForceNewtons * blockingToothRadiusMeters * RoofFactor(roofIncludedAngleRadians, toothFrictionCoefficient);

    // @description - smallest cone friction coefficient that still blocks, mu_min = sin(alpha) (R_S / R_C) f; both torques scale with F_a so the threshold does not depend on the shift force
    // @param {double} coneHalfAngleRadians - cone half angle in rad
    // @param {double} toothToConeRadiusRatio - R_S / R_C
    // @param {double} roofIncludedAngleRadians - roof angle in rad
    // @param {double} toothFrictionCoefficient - mu_S
    // @return {double} - minimum blocking friction coefficient
    public static double MinimumBlockingFriction(double coneHalfAngleRadians, double toothToConeRadiusRatio, double roofIncludedAngleRadians, double toothFrictionCoefficient)
        => System.Math.Sin(coneHalfAngleRadians) * toothToConeRadiusRatio * RoofFactor(roofIncludedAngleRadians, toothFrictionCoefficient);

    // @description - synchronization time at constant torques, t = J |d omega| / (T_K + sign T_drag) (Chalmers eqs. 2.174-2.176); drag helps when it acts against the speed error and hurts otherwise
    // @param {double} effectiveInertiaKilogramMetersSquared - inertia being synchronized in kg m^2 (input side, plus the engine when the clutch transmits)
    // @param {double} speedErrorRadiansPerSecond - speed difference to remove in rad/s
    // @param {double} coneTorqueNewtonMeters - cone torque in N m
    // @param {double} dragTorqueAlongConeNewtonMeters - drag torque in N m, positive when it acts in the same direction as the cone torque (shortens the shift)
    // @return {double} - time in s (infinite if the net torque is not positive)
    public static double SynchronizationTime(double effectiveInertiaKilogramMetersSquared, double speedErrorRadiansPerSecond, double coneTorqueNewtonMeters, double dragTorqueAlongConeNewtonMeters)
    {
        double net = coneTorqueNewtonMeters + dragTorqueAlongConeNewtonMeters;
        return net > 0.0 ? effectiveInertiaKilogramMetersSquared * System.Math.Abs(speedErrorRadiansPerSecond) / net : double.PositiveInfinity;
    }

    // @description - piecewise-linear interpolation of a table with ascending abscissae, held constant outside the table
    // @param {float[]} abscissae - ascending x values
    // @param {float[]} values - y values
    // @param {double} x - query
    // @return {double} - interpolated y
    public static double Interpolate(float[] abscissae, float[] values, double x)
    {
        if (x <= abscissae[0]) return values[0];
        for (int index = 1; index < abscissae.Length; index++)
            if (x <= abscissae[index]) return values[index - 1] + (values[index] - values[index - 1]) * (x - abscissae[index - 1]) / (abscissae[index] - abscissae[index - 1]);
        return values[values.Length - 1];
    }

    // @description - cone friction coefficient from the sliding-speed table (negative slope at low speed: the last part of the synchronization grabs)
    // @param {TransmissionParameters} parameters - friction table and scale
    // @param {double} slidingSpeedMetersPerSecond - cone surface sliding speed in m/s
    // @return {double} - friction coefficient
    public static double ConeFriction(TransmissionParameters parameters, double slidingSpeedMetersPerSecond)
        => parameters.coneFrictionScale * Interpolate(parameters.coneFrictionSlidingSpeedsMetersPerSecond, parameters.coneFrictionCoefficientAtSpeed, System.Math.Abs(slidingSpeedMetersPerSecond));

    // @description - angle the sleeve chamfers can index while the sleeve crosses them, a fraction of the tooth pitch
    // @param {int} toothCount - teeth around the circle
    // @param {double} captureFraction - fraction of the pitch
    // @return {double} - angle in rad
    public static double CaptureAngle(int toothCount, double captureFraction) => 2.0 * System.Math.PI / toothCount * captureFraction;

    // @description - kinetic energy lost when two rotating inertias with a speed difference are forced to one speed (inelastic tooth impact), 0.5 J d_omega^2
    // @param {double} reducedInertiaKilogramMetersSquared - inertia on the fast side in kg m^2 (the other side is much heavier)
    // @param {double} speedDifferenceRadiansPerSecond - speed difference in rad/s
    // @return {double} - energy in J
    public static double ImpactEnergy(double reducedInertiaKilogramMetersSquared, double speedDifferenceRadiansPerSecond)
        => 0.5 * reducedInertiaKilogramMetersSquared * speedDifferenceRadiansPerSecond * speedDifferenceRadiansPerSecond;
}
