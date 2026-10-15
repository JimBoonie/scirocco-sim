#!/usr/bin/env python3
# @description - Metrics from visco_smoke.py runs. usage: visco_metrics.py <smoke_dir> -> metrics.csv + markdown table on stdout
# @param {String} arguments - command-line arguments (usage: visco_metrics.py <smoke_dir> -> metrics.csv + markdown table on stdout)
# @return {Int} - process exit status (0 = success)
"""Metrics from visco_smoke.py runs. usage: visco_metrics.py <smoke_dir> -> metrics.csv + markdown table on stdout"""
import sys, os, re, glob, numpy as np, pandas as pd
S = sys.argv[1]; R = 40.0
def load(c, s):
    f = f'{S}/{c}/{s}.csv'
    if not os.path.exists(f): return None
    d = pd.read_csv(f); return d if len(d) > 10 else None
def skid(d):
    d = d[d.t > 8]; ok = d[abs(np.hypot(d.x - R, d.z) - R) < 1.5]; lin = ok[abs(ok.ay) < 0.5]
    return dict(skid_ay_max=abs(ok.ay).rolling(100, center=True).mean().max(), skid_us=np.polyfit(abs(lin.ay), lin.steer, 1)[0])
def step(d):
    p = d[d.phase == 1]; t0 = p.t.iloc[0]; r = p.r.abs(); ss = r[p.t > t0 + 4].mean(); ay = p.ay.abs()[p.t > t0 + 4].mean()
    t90 = p.t[r >= 0.9 * ss].iloc[0] - t0
    return dict(step_ay=ay, step_r_ss=ss, step_overshoot=(r.max() / ss - 1) * 100, step_t90=t90)
def brake(d):
    p = d[d.phase == 1]; s = np.hypot(np.diff(p.x), np.diff(p.z)).sum(); return dict(brake_dist=s, brake_v0=p.v.iloc[0])
def lift(d):
    p0 = d[d.phase == 0].iloc[-50:]; p = d[d.phase == 1]; t0 = p.t.iloc[0]; a = p[p.t <= t0 + 1.0].iloc[-1]
    return dict(lift_ay0=p0.ay.abs().mean(), lift_dr_1s=abs(a.r) - p0.r.abs().mean(), lift_dbeta_max=(p.beta.abs().max() - p0.beta.abs().mean()))
def lag(c):
    f = f'{S}/{c}/lagunatherm.log'
    if not os.path.exists(f): return {}
    L = [l for l in open(f, errors='replace') if l.startswith('THERM')]; out = {}
    laps = [(int(m.group(1)), float(m.group(2))) for l in L for m in [re.search(r'LAP (\d+) ([\d.]+)s', l)] if m]
    prev = 0
    for n, t in laps: out[f'lap{n}'] = t - prev; prev = t
    def temps(l):
        b = [float(x) for x in re.findall(r'bulk (-?\d+)', l)]; g = [float(x) for x in re.findall(r'grip (\d+)%', l)]
        return np.mean(b) if b else np.nan, np.mean(g) if g else np.nan
    for l in L:
        m = re.search(r't (\d+) ', l)
        if m and int(m.group(1)) in (30, 60, 120): out[f'bulkF_t{m.group(1)}'], _ = temps(l)
    lapl = [l for l in L if 'LAP' in l]
    if lapl: out['bulkF_end'], out['grip_end'] = temps(lapl[-1])
    out['fail'] = any('FAIL' in l for l in L)
    return out
rows = []
for c in sorted(os.listdir(S)):
    r = dict(cfg=c)
    for s, fn in (('skidpad', skid), ('stepsteer', step), ('brakethr', brake), ('liftoff', lift)):
        d = load(c, s)
        if d is not None:
            try: r.update(fn(d))
            except Exception as e: r[s + '_err'] = str(e)[:40]
    r.update(lag(c)); rows.append(r)
df = pd.DataFrame(rows).set_index('cfg'); df.to_csv(f'{S}/metrics.csv')
pd.set_option('display.width', 250); print(df.round(3).T.to_string())
