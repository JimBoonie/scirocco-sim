#!/usr/bin/env python3
# @description - Builds the understeer sensitivity table from the PSTEP steady-state lines of the run_sens.sh logs.
# @param {String} arguments - command-line arguments (see the script body)
# @return {Int} - process exit status (0 = success)
import re,glob,os,sys
def get(f):
    s=open(f).read(); st=re.search(r'PSTEP steady.*',s); t1=re.search(r'PSTEP t\+1\.00s.*',s)
    if not st or not t1: return None
    num=lambda l,k: float(re.search(k+r' ([+-]?[\d.]+)',l).group(1))
    pw=[l for l in s.splitlines() if l.startswith('PW,1.000,')]; fr=pw[0].split(',') if pw else None
    return dict(dUS=num(t1.group(0),'aF-aR')-num(st.group(0),'aF-aR'), us0=num(st.group(0),'aF-aR'), ax=num(t1.group(0),'ax'), dFzF=num(t1.group(0),'FzF')-num(st.group(0),'FzF'),
                kFR=float(fr[11]) if fr else None, uFR=float(fr[15]) if fr else None, FxF=float(fr[5])+float(fr[13]) if fr else None, v=num(t1.group(0),'v'))
R=sys.argv[1] if len(sys.argv)>1 else '40'
base=get(f'/workspace/scirocco_sim/builds/logs/pstep/R{R}_t1.log'); print('variant'.ljust(20),'dUS(deg) us0  ax(g)  dFzF(N) kappaFR uFR  FxF(N) v1s'); 
def row(n,d): print(n.ljust(20), '%+.2f  %+.2f %.3f %+5.0f  %.3f  %.2f %5.0f %.1f'%(d['dUS'],d['us0'],d['ax'],d['dFzF'],d['kFR'],d['uFR'],d['FxF'],d['v']) if d else 'n/a')
row('BASE',base)
for f in sorted(glob.glob(f'/workspace/scirocco_sim/builds/logs/sens/{R}_*.log')): row(os.path.basename(f)[len(R)+1:-4], get(f))
