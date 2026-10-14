# Changelog

## 2026-10-13 (PT) — Tire thermal: road-test cold side (option b) + tread-mean grip temperature
- Below the bench curve, grip now follows road-test data (Tambellini 2019 summer-tire stopping distance vs road temperature, 0.211 %/°C above 77 °F; full grip at/below 77 °F) instead of the 64 % cold hold; continuous blend into the Alcázar Vargas bench curve at 151.3 °F.
- Grip temperature = heat-capacity-weighted tread mean (surface zones + bulk) instead of bulk only (a 270 °F surface over a 77 °F bulk now gives 97 % instead of 64 %).
- Thermal model stays OFF by default; thermal-off handling unchanged. Tests: TireThermalTests (road/bench continuity, tread mean, 270 °F case). Docs: tire_thermal_research.md §5b, §7.

## 2026-10-13 (PT) — Car audio: overrun, engine smoothness, tire sound v4
- Engine overrun/off-throttle stays clearly audible: exhaust level now follows exhaust-valve-opening pressure (p_EVO ~ MAP x pressure ratio; motoring still pulses), amp ~ p_EVO^0.75, softer lowpass off load, subtle overrun burble. Rendered on/off difference 11-14 dB -> 7-8 dB (2000-6000 rpm).
- Engine "grain" removed: per-cycle amplitude jitter was a hard step every firing (broadband clicks); jitter is now subtle and glided, amplitude de-zippered, 2-pole lowpass, tanh instead of hard clip.
- Tire sound v4: noise-only resonant bandpass (150 -> ~174 Hz centre, ~2.5 semitones max, Q 3-4) + gentle low-Q broadband band; impulse-train tone and 2nd harmonic ("angry bees") removed. v3 envelope and engine-minus-4 dB ceiling unchanged.
