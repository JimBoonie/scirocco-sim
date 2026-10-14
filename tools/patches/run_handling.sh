#!/bin/bash
# handling battery on the current Linux build: ./run_handling.sh <outdir>
O=$1; mkdir -p $O; cd /workspace/scirocco_sim/builds/linux
for a in accel skidpad rampsteer stepsteer lanechange brake brakethr brakeinturn liftoff kbdlaunch "powerstep -radius 40 -gear 2 -thr 1 -ay 0.5"; do n=$(echo $a | cut -d' ' -f1)
 timeout 400 ./SciroccoSimDemo.x86_64 -batchmode -nographics -smoketest -scenario $a -logFile $O/$n.log >/dev/null 2>&1 & done; wait
cd $O; for f in *.log; do echo "== $f"; rg -N "RESULT|SUMMARY|RESULTS" $f | cut -c1-400; done > summary.txt
