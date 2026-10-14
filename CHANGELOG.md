# Changelog

## 2026-10-13 (PT) — Car audio: overrun, engine smoothness, tire sound v4
- Engine overrun/off-throttle stays clearly audible: exhaust level now follows exhaust-valve-opening pressure (p_EVO ~ MAP x pressure ratio; motoring still pulses), amp ~ p_EVO^0.75, softer lowpass off load, subtle overrun burble. Rendered on/off difference 11-14 dB -> 7-8 dB (2000-6000 rpm).
- Engine "grain" removed: per-cycle amplitude jitter was a hard step every firing (broadband clicks); jitter is now subtle and glided, amplitude de-zippered, 2-pole lowpass, tanh instead of hard clip.
- Tire sound v4: noise-only resonant bandpass (150 -> ~174 Hz centre, ~2.5 semitones max, Q 3-4) + gentle low-Q broadband band; impulse-train tone and 2nd harmonic ("angry bees") removed. v3 envelope and engine-minus-4 dB ceiling unchanged.
