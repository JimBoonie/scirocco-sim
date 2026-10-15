#!/usr/bin/python3
# @description - Fit of the complex-modulus model of the Experimental (viscoelastic) compound (ViscoTire.cs). Run: python3 tools/visco_fit.py
# Model (reference temperature Tg, WLF shift):  E*(w) = Eg + (E0 - Eg)/(1 + (i w tauG)^a)^b   (polymer, Havriliak-Negami)
#                                              + 1.5 E0 (i w / w_ref)^n                      (filler network, fractional element)
# Targets (S): CB-filled E-SBR tread, DMA 10 Hz: tan d(0 C)=0.206, tan d(60 C)=0.160 (Sae-oui et al. 2017, Table 4);
# tan d peak 0.8 (SD: filled SBR 0.6-1.0); |E*|(60 C,10 Hz)=8 MPa (E: filled tread 5-10 MPa).
# Fixed: Tg=-50 C (S, cold E-SBR 23.5 % styrene), WLF C1g=17.44, C2g=51.6 K (S, WLF 1955 universal), Eg=2 GPa (SD 1-3 GPa),
# filler/polymer modulus ratio 1.5 (SD: Payne ratio E0/Einf ~2.5 for ~50-80 phr carbon black).
import numpy as np
from scipy.optimize import least_squares

GLASS_TRANSITION_C = -50.0        # degC (S)
WLF_C1 = 17.44                    # dimensionless WLF constant (S)
WLF_C2_K = 51.6                   # K WLF constant (S)
GLASS_MODULUS_PA = 2e9            # Pa glassy modulus Eg (SD)
FILLER_TO_POLYMER_RATIO = 1.5     # filler-network modulus over polymer rubbery modulus (SD)
TEST_FREQUENCY_HZ = 10            # Hz DMA test frequency (S)
REFERENCE_TEMPERATURE_C = 60      # degC temperature at which the filler network is referenced (S)
TARGET_LOSS_TANGENT_0C = 0.206    # tan delta at 0 degC, 10 Hz (S)
TARGET_LOSS_TANGENT_60C = 0.160   # tan delta at 60 degC, 10 Hz (S)
TARGET_MODULUS_60C_PA = 8e6       # Pa |E*| at 60 degC, 10 Hz (E)
TARGET_PEAK_LOSS_TANGENT = 0.8    # peak tan delta over temperature (SD)


# @description - WLF time-temperature shift: log10 of the shift factor a_T = exp(-C1 (T - Tg) / (C2 + T - Tg)) in decades (S: Williams-Landel-Ferry 1955).
# @param {Float} temperature - temperature (degC)
# @return {Float} - log10 of the shift factor (decades)
def log_shift_factor(temperature):
    return -WLF_C1 * (temperature - GLASS_TRANSITION_C) / (WLF_C2_K + temperature - GLASS_TRANSITION_C)


REFERENCE_ANGULAR_FREQUENCY = 2 * np.pi * TEST_FREQUENCY_HZ * 10 ** log_shift_factor(REFERENCE_TEMPERATURE_C)   # rad/s filler-network reference frequency


# @description - Complex modulus E*(w) = Eg + (E0 - Eg)/(1 + (i w tau)^a)^b + ratio * E0 * (i w / w_ref)^n (Havriliak-Negami polymer plus fractional filler network).
# @param {Float} angular_frequency - angular frequency (rad/s, already shifted to the reference temperature)
# @param {Float} rubbery_modulus - polymer rubbery modulus E0 (Pa)
# @param {Float} alpha_exponent - Havriliak-Negami width exponent a (dimensionless)
# @param {Float} beta_exponent - Havriliak-Negami skew exponent b (dimensionless)
# @param {Float} filler_exponent - fractional-element exponent n (dimensionless)
# @param {Float} relaxation_time - glass relaxation time tauG (s)
# @return {Complex} - complex modulus (Pa)
def complex_modulus(angular_frequency, rubbery_modulus, alpha_exponent, beta_exponent, filler_exponent, relaxation_time):
    return GLASS_MODULUS_PA + (rubbery_modulus - GLASS_MODULUS_PA) / (1 + (1j * angular_frequency * relaxation_time) ** alpha_exponent) ** beta_exponent + FILLER_TO_POLYMER_RATIO * rubbery_modulus * (1j * angular_frequency / REFERENCE_ANGULAR_FREQUENCY) ** filler_exponent


# @description - Loss tangent and modulus magnitude at one temperature and the test frequency.
# @param {Float} temperature - temperature (degC)
# @param {Array} parameters - [E0 (Pa), b, n, log10 tauG (s)]
# @param {Float} alpha_exponent - Havriliak-Negami width exponent a (dimensionless)
# @return {Tuple} - (tan delta, |E*| in Pa)
def loss_tangent_and_modulus(temperature, parameters, alpha_exponent):
    rubbery_modulus, beta_exponent, filler_exponent, log_relaxation_time = parameters
    modulus = complex_modulus(2 * np.pi * TEST_FREQUENCY_HZ * 10 ** log_shift_factor(temperature), rubbery_modulus, alpha_exponent, beta_exponent, filler_exponent, 10 ** log_relaxation_time)
    return modulus.imag / modulus.real, abs(modulus)


# @description - Fits E0, b, n and tauG for one value of the exponent a so that the four targets are met.
# @param {Float} alpha_exponent - Havriliak-Negami width exponent a (dimensionless)
# @param {Array} temperatures - temperature grid searched for the tan delta peak (degC)
# @return {OptimizeResult} - the least-squares result
def fit_for_alpha(alpha_exponent, temperatures):
    # @description - Relative residuals of the four targets.
    # @param {Array} parameters - [E0 (Pa), b, n, log10 tauG (s)]
    # @return {List} - residuals (dimensionless)
    def residuals(parameters):
        tangent_0c, _ = loss_tangent_and_modulus(0, parameters, alpha_exponent)
        tangent_60c, modulus_60c = loss_tangent_and_modulus(60, parameters, alpha_exponent)
        peak_tangent = max(loss_tangent_and_modulus(temperature, parameters, alpha_exponent)[0] for temperature in temperatures)
        return [(tangent_0c - TARGET_LOSS_TANGENT_0C) / TARGET_LOSS_TANGENT_0C, (tangent_60c - TARGET_LOSS_TANGENT_60C) / TARGET_LOSS_TANGENT_60C, (modulus_60c - TARGET_MODULUS_60C_PA) / TARGET_MODULUS_60C_PA, (peak_tangent - TARGET_PEAK_LOSS_TANGENT) / TARGET_PEAK_LOSS_TANGENT]
    return least_squares(residuals, [3e6, 0.3, 0.1, 2], bounds=([1e5, 0.02, 0.0, -4], [2e7, 1, 0.5, 6]))


# @description - Fits every candidate exponent a, prints each result and the best one.
# @return {Void} - text on standard output
def main():
    temperatures = np.linspace(-80, 40, 241)
    best = None
    for alpha_exponent in (0.4, 0.5, 0.6, 0.7, 0.8):
        result = fit_for_alpha(alpha_exponent, temperatures)
        print(f"a={alpha_exponent}: E0={result.x[0]:.6e} b={result.x[1]:.6f} n={result.x[2]:.6f} log10 tauG={result.x[3]:.6f} cost={result.cost:.2e}")
        if best is None or result.cost < best[0]:
            best = (result.cost, alpha_exponent, result.x)
    print("best", best)


if __name__ == '__main__':
    main()
