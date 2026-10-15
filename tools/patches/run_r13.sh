#!/bin/bash
# @description - Rebuilds the Linux player via run_linux.sh, then runs the powerstep throttle sweep (-1, 0, 0.25, 0.5, 1) into builds/logs/pstep100.
# @param {String} arguments - command-line arguments (see the script body)
# @return {Int} - process exit status (0 = success)
./run_linux.sh; L=/workspace/scirocco_sim/builds/logs; P=$L/pstep100; mkdir -p $P; cd /workspace/scirocco_sim/builds/linux
for th in -1 0 0.25 0.5 1; do timeout 400 ./SciroccoSimDemo.x86_64 -batchmode -nographics -smoketest -scenario powerstep -radius 100 -gear 3 -thr $th -ay 0.5 -logFile $P/R100_t$th.log >/dev/null 2>&1 & done; wait; echo P100DONE >> $L/status.txt
