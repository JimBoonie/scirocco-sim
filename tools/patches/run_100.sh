#!/bin/bash
# @description - Builds the Linux player with Unity batchmode (SciroccoSetup.BuildLinux) and runs the 100 m scenario set, logging under builds/logs.
# @param {String} arguments - command-line arguments (see the script body)
# @return {Int} - process exit status (0 = success)
L=/workspace/scirocco_sim/builds/logs; cd /workspace/scirocco_sim/unity/SciroccoSimDemo
unity --no-banner --non-interactive build . --target StandaloneLinux64 --execute-method SciroccoSetup.BuildLinux -o /workspace/scirocco_sim/builds/linux/SciroccoSimDemo.x86_64 > $L/lb.out 2>&1; echo "BUILD_EXIT $?" > $L/status100.txt
P=$L/pstep100; mkdir -p $P; cd /workspace/scirocco_sim/builds/linux
for th in -1 0 0.25 0.5 1; do timeout 400 ./SciroccoSimDemo.x86_64 -batchmode -nographics -smoketest -scenario powerstep -radius 100 -gear 3 -thr $th -ay 0.5 -logFile $P/R100_t$th.log >/dev/null 2>&1 & done; wait; echo DONE >> $L/status100.txt
