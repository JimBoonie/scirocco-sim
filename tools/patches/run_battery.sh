#!/bin/bash
# handling battery with CSV export: run_battery.sh <bindir> <outdir> <seq|par> [extra args, e.g. -thermal -tirewarm]
B=$1; O=$2; M=$3; shift 3; mkdir -p $O; H="sc,t,x,z,v,ax,ay,r,beta,steer,thr,brk,gear,rpm,FzFL,FzFR,FzRL,FzRR,wFL,wFR,wRL,wRR,pitch,roll,phase"
cd $B; for s in skidpad rampsteer stepsteer lanechange brake brakethr brakeinturn liftoff; do
  if [ $M = seq ]; then timeout 300 ./SciroccoSimDemo.x86_64 -batchmode -nographics -smoketest -scenario $s "$@" -logFile $O/$s.log >/dev/null 2>&1
  else timeout 300 ./SciroccoSimDemo.x86_64 -batchmode -nographics -smoketest -scenario $s "$@" -logFile $O/$s.log >/dev/null 2>&1 & fi; done; wait
for s in skidpad rampsteer stepsteer lanechange brake brakethr brakeinturn liftoff; do (echo $H; grep -h "^CSV," $O/$s.log | cut -d, -f2-) > $O/$s.csv; done
python3 /workspace/scirocco_sim/builds/analyze_tests.py $O > $O/metrics.txt 2>$O/analyze.err; echo BATTERY_DONE $O
