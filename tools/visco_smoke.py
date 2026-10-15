#!/usr/bin/env python3
# @description - Headless smoke comparison Default vs Experimental (viscoelastic) compound + one-at-a-time ablations.
# @param {String} arguments - command-line arguments (usage: visco_smoke.py <out_dir> <base_build_dir> <new_build_dir>   (Linux players; -overrides fil)
# @return {Int} - process exit status (0 = success)
"""Headless smoke comparison Default vs Experimental (viscoelastic) compound + one-at-a-time ablations.
usage: visco_smoke.py <out_dir> <base_build_dir> <new_build_dir>   (Linux players; -overrides files written per config)"""
import os, sys, subprocess, itertools
OUT, BASE, NEW = sys.argv[1:4]
SCEN = ['skidpad', 'stepsteer', 'brakethr', 'liftoff', 'lagunatherm']
FLAGS = ['veHysteresis', 'veAdhesion', 'veWlf', 'veFlash', 'vePayne', 'veRateStiffness', 'veLoadRelax', 'veRollingRes', 'veBulkHeat']
EXP = ['p.tireCompound=ExperimentalViscoelastic']
CFG = {'base_off': (BASE, []), 'base_th': (BASE, ['p.tireThermal=true']), 'new_off': (NEW, []), 'new_th': (NEW, ['p.tireThermal=true']),
       'exp_off': (NEW, EXP), 'exp_th': (NEW, EXP + ['p.tireThermal=true'])}
for f in FLAGS: CFG['abl_' + f] = (NEW, EXP + ['p.tireThermal=true', f'p.{f}=false'])
only = os.environ.get('ONLY'); cfgs = [c for c in CFG if not only or c in only.split(',')]
jobs = []
for c in cfgs:
    d = f'{OUT}/{c}'; os.makedirs(d, exist_ok=True); open(f'{d}/ov.txt', 'w').write('\n'.join(['# ' + c] + CFG[c][1]) + '\n')
    for s in SCEN:
        if s == 'lagunatherm' and c.endswith('_off'): continue
        jobs.append((c, s))
H = 'sc,t,x,z,v,ax,ay,r,beta,steer,thr,brk,gear,rpm,FzFL,FzFR,FzRL,FzRR,wFL,wFR,wRL,wRR,pitch,roll,phase\n'
def launch(c, s):
    d = f'{OUT}/{c}'; extra = '-track laguna' if s.startswith('laguna') else ''
    return subprocess.Popen(f'cd {CFG[c][0]} && timeout 900 ./SciroccoSimDemo.x86_64 -batchmode -nographics -smoketest -scenario {s} {extra} -overrides {d}/ov.txt -logFile {d}/{s}.log >/dev/null 2>&1', shell=True)
import time
running = []; PAR = int(os.environ.get('PAR', '6'))
for j in jobs:
    while len([p for p in running if p.poll() is None]) >= PAR: time.sleep(2)
    running.append(launch(*j))
for p in running: p.wait()
for c, s in jobs:
    d = f'{OUT}/{c}'; L = open(f'{d}/{s}.log', errors='replace').read().splitlines()
    open(f'{d}/{s}.csv', 'w').write(H + ''.join(l[4:] + '\n' for l in L if l.startswith('CSV,')))
    open(f'{d}/{s}.therm', 'w').write(''.join(l + '\n' for l in L if l.startswith('THERM') or l.startswith('SMOKE') or 'Exception' in l))
print('done', len(jobs))
