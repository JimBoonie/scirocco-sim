#!/usr/bin/env python3
# @description - Offline suspension kinematics for Scirocco II (A1 platform).
# @param {String} arguments - command-line arguments (see the script body)
# @return {Int} - process exit status (0 = success)
"""Offline suspension kinematics for Scirocco II (A1 platform).
Inputs: ../suspension_params.json (hardpoints tagged S/SD/D/E; many hardpoints are ESTIMATES -> results inherit that).
Vehicle frame: x fwd, y left, z up, origin on ground at front axle centreline. Left side solved; right mirrored.
Outputs: tables.json (consumed by Unity editor setup -> SuspensionTables ScriptableObject) + PNG plots."""
import json, numpy as np
from scipy.optimize import least_squares
from scipy.spatial.transform import Rotation as Rot
import matplotlib; matplotlib.use('Agg'); import matplotlib.pyplot as plt
import params_io; P = params_io.load()
F = P['front_macpherson']; Rr = P['rear_twist_beam']
v = lambda k: np.array(F[k][0], float)
T, LBJ0, AF, AR, WC0, TRO0, TRI0 = v('strut_top_mount'), v('lower_ball_joint'), v('lower_arm_front_pivot'), v('lower_arm_rear_pivot'), v('wheel_centre'), v('tie_rod_outer'), v('tie_rod_inner')
rl = P['vehicle']['tyre_loaded_radius'][0]
u0 = (T - LBJ0) / np.linalg.norm(T - LBJ0)            # strut axis (= steering axis here: strut top + LBJ)
axis = (AR - AF) / np.linalg.norm(AR - AF)            # lower arm pivot axis
def arm_point(th):                                     # LBJ rotated about pivot axis by th
    r = LBJ0 - AF; return AF + Rot.from_rotvec(axis * th).apply(r)
TR_LEN = np.linalg.norm(TRO0 - TRI0)
SPIN0 = np.array([0, 1.0, 0])                          # design spin axis (alignment applied separately at runtime)
def knuckle(x):
    th, rv = x[0], x[1:4]; L = arm_point(th); R = Rot.from_rotvec(rv)
    tf = lambda p: L + R.apply(p - LBJ0)
    return L, R, tf
def residual(x, dz, rack):
    L, R, tf = knuckle(x); u = R.apply(u0); d = T - L
    perp = d - d.dot(u) * u                            # strut axis passes through top mount
    tri = TRI0 + np.array([0, rack, 0])
    return np.concatenate([perp * 10, [np.linalg.norm(tf(TRO0) - tri) - TR_LEN, tf(WC0)[2] - (WC0[2] + dz)]])
def solve_front(dz, rack, x0):
    s = least_squares(residual, x0, args=(dz, rack), xtol=1e-12, ftol=1e-12); return s.x, s.cost
def outputs(x):
    L, R, tf = knuckle(x); a = R.apply(SPIN0); wc = tf(WC0)
    camber = -np.degrees(np.arcsin(a[2]))              # + = top out (left wheel)
    f = np.cross(a, [0, 0, 1.0]); toe = -np.degrees(np.arctan2(f[1], f[0]))   # + = toe-in (front points to -y)
    strut_len = (T - L).dot(R.apply(u0))
    # front-view roll centre: IC = intersection of (line through T perpendicular to strut axis) and (lower arm line)
    u = R.apply(u0); n1 = np.array([u[1], u[2]]); p1 = np.array([T[1], T[2]])          # line1: points q with (q-p1).dir1=... use normal form
    d1 = np.array([-u[2], u[1]])                       # perpendicular to strut axis in y-z plane
    piv = AF + axis * ((L - AF).dot(axis))             # arm pivot point in plane of LBJ
    p2 = np.array([piv[1], piv[2]]); d2 = np.array([L[1], L[2]]) - p2
    A = np.array([d1, -d2]).T; s = np.linalg.solve(A, p2 - p1); ic = p1 + s[0] * d1
    cp = np.array([wc[1], wc[2] - rl])                 # contact point (approx below wheel centre)
    t = -cp[0] / (ic[0] - cp[0]); rc = cp[1] + t * (ic[1] - cp[1])
    return dict(camber=camber, toe=toe, wc_y=wc[1], wc_z=wc[2], strut=strut_len, rc=rc)
# tie-rod points are used as given in the JSON (2026-10-13 end take-off geometry; the old 0.333 m inner-height re-derivation is removed)
TIE_ROD_Z = (float(TRI0[2]), float(TRI0[2]))
# ---- front sweep: travel x rack
travel = np.round(np.arange(-0.09, 0.0801, 0.01), 3)
racks = np.round(np.linspace(-0.07, 0.07, 71), 4)   # +-70 mm rack in 2 mm steps (D; linear-interpolation error < 0.02 deg at lock): covers the +-68.6 mm stop; per-wheel steer comes from this grid at runtime
x0 = np.zeros(4); base, c = solve_front(0, 0, x0); o0 = outputs(base)
grid = {}; maxc = []
order = sorted(range(len(racks)), key=lambda i: abs(racks[i]))
seed0 = {}
for i in order:
    r = racks[i]
    prev = min(seed0, key=lambda k: abs(k - r)) if seed0 else None
    x_r, cost = solve_front(0.0, r, seed0[prev] if prev is not None else base); assert cost < 1e-10, (r, cost)
    seed0[r] = x_r
    i0 = int(np.argmin(abs(travel)))
    for rng in (range(i0, len(travel)), range(i0, -1, -1)):
        xs = x_r.copy()
        for j in rng:
            xs, cost = solve_front(travel[j], r, xs); maxc.append(cost)
            grid[(travel[j], r)] = outputs(xs)
iR0 = int(np.argmin(abs(racks)))
mid = [grid[(dz, racks[iR0])] for dz in travel]
cam = np.array([m['camber'] - o0['camber'] for m in mid]); toe = np.array([m['toe'] - o0['toe'] for m in mid])
track = np.array([m['wc_y'] - o0['wc_y'] for m in mid]); rc = np.array([m['rc'] for m in mid])
strut = np.array([m['strut'] for m in mid]); mr = np.gradient(-strut, travel)   # spring/damper compression per wheel travel
# steer angle (left wheel, + = wheel points right = toe-in) vs rack at design height, and camber vs steer angle
steer_l = np.array([grid[(0.0, r)]['toe'] - o0['toe'] for r in racks])
toe_tr = np.array([[grid[(dz, r)]['toe'] - o0['toe'] for r in racks] for dz in travel])   # [travel, rack] left-wheel toe-in = steer (+ right), bump steer included
cam_steer = np.array([[grid[(dz, r)]['camber'] - o0['camber'] for r in racks] for dz in travel])  # [travel, rack]
# ---- rear twist beam (trailing arms about lateral pivot axis + beam twist)
piv = np.array(Rr['trailing_arm_pivot'][0]); wcr = np.array(Rr['wheel_centre'][0]); xs_ = Rr['beam_shear_centre_x'][0]
Larm = np.hypot(*(wcr[[0, 2]] - piv[[0, 2]])); dsh = piv[0] - xs_; b = 2 * piv[1]; tr = P['vehicle']['track_rear'][0]
cam_frac = dsh * tr / (b * Larm)          # wheel tilt relative to body per unit body-roll (counteracts trailing-arm camber)
# rear roll centre (D in json; simple swing-arm estimate kept for comparison)
rc_rear = P['kinematics_targets']['rear_roll_centre_height_m'][0]
dt_ = np.array(Rr['damper_top'][0]); db0 = np.array(Rr['damper_bottom'][0])
def rear_damper_len(dz):                  # damper bottom rotates with arm about pivot (lateral axis)
    ang = np.arcsin(dz / Larm); r = db0 - piv; R = Rot.from_rotvec([0, ang, 0]); return np.linalg.norm(dt_ - (piv + R.apply(r)))
rl_len = np.array([rear_damper_len(dz) for dz in travel]); mr_r = np.gradient(-rl_len, travel)
# arm-arc fore/aft wheel motion -> negligible toe for symmetric bump on a beam axle (both wheels move together)
out = dict(
  _note="Generated by kinematics/solve_kinematics.py from suspension_params.json. Hardpoints partly ESTIMATED (E); tables inherit that.",
  travel=travel.tolist(), front_camber_body=cam.tolist(), front_bumpsteer_toe=toe.tolist(), front_track_change=track.tolist(),
  front_rc_height=rc.tolist(), front_motion_ratio=mr.tolist(), rack=racks.tolist(), front_steer_vs_rack=steer_l.tolist(), front_toe_travel_rack=toe_tr.tolist(),
  front_camber_travel_rack=cam_steer.tolist(), rear_camber_roll_fraction=float(cam_frac), rear_rc_height=float(rc_rear),
  rear_motion_ratio=mr_r.tolist(), design=dict(tie_rod_inner_z_json=TIE_ROD_Z[0], tie_rod_inner_z_used=TIE_ROD_Z[1], front_rc=o0['rc'], kpi_deg=float(np.degrees(np.arctan2(-u0[1], u0[2]))), caster_deg=float(np.degrees(np.arctan2(-u0[0], u0[2])))))
json.dump(out, open(params_io.out('tables.json'), 'w'), indent=1)
print("max residual cost", max(maxc), "points>1e-10:", sum(c>1e-10 for c in maxc));print(f"front RC at design {o0['rc']*1000:.0f} mm, KPI {out['design']['kpi_deg']:.2f} deg, caster {out['design']['caster_deg']:.2f} deg")
print(f"camber gain {np.polyfit(travel[5:14],cam[5:14],1)[0]:.1f} deg/m, bump steer {np.polyfit(travel[5:14],toe[5:14],1)[0]:.2f} deg/m (+=toe-in)")
print(f"front MR mean {mr.mean():.3f}, rear MR mean {mr_r.mean():.3f}, rear camber-vs-body-roll correction {cam_frac:.2f} (ground camber follows {1-cam_frac:.2f} x roll)")
print(f"steer at rack +-70mm: {steer_l[0]:.1f} / {steer_l[-1]:.1f} deg; camber change at that steer: {cam_steer[9,0]:.2f}/{cam_steer[9,-1]:.2f}")
fig, ax = plt.subplots(2, 3, figsize=(15, 8))
for a_, y, t in [(ax[0,0], cam, 'front camber change vs body [deg] (+ top out)'), (ax[0,1], toe, 'front bump steer [deg] (+ toe-in)'), (ax[0,2], track*1000, 'front half-track change [mm]'),
                 (ax[1,0], rc*1000, 'front roll centre height [mm]'), (ax[1,1], mr, 'motion ratio (front strut, rear damper dashed)')]:
    a_.plot(travel*1000, y); a_.set_title(t, fontsize=9); a_.set_xlabel('wheel travel [mm] (+bump)'); a_.grid()
ax[1,1].plot(travel*1000, mr_r, '--')
for i, dz in enumerate(travel):
    if i % 3 == 0: ax[1,2].plot(steer_l, cam_steer[i], label=f'{dz*1000:.0f} mm')
ax[1,2].set_title('front camber change vs wheel steer angle', fontsize=9); ax[1,2].set_xlabel('left wheel steer [deg] (+ right)'); ax[1,2].legend(fontsize=7); ax[1,2].grid()
plt.suptitle('Scirocco II kinematics from suspension_params.json (hardpoints partly ESTIMATED)'); plt.tight_layout(); plt.savefig(params_io.out('kinematics.png'), dpi=100)
