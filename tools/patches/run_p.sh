#!/bin/bash
L=/workspace/scirocco_sim/builds/logs; cd /workspace/scirocco_sim/unity/SciroccoSimDemo
unity --no-banner --non-interactive build . --target StandaloneLinux64 --execute-method SciroccoSetup.BuildLinux -o /workspace/scirocco_sim/builds/linux/SciroccoSimDemo.x86_64 > $L/lb.out 2>&1; echo "BUILD_EXIT $?" > $L/statusp.txt
P=$L/pstep100; rm -rf $P; mkdir -p $P; cd /workspace/scirocco_sim/builds/linux
for th in -1 0 0.25 0.5 1; do timeout 400 ./SciroccoSimDemo.x86_64 -batchmode -nographics -smoketest -scenario powerstep -radius 100 -gear 3 -thr $th -ay 0.5 -logFile $P/R100_t$th.log >/dev/null 2>&1 & done
timeout 400 ./SciroccoSimDemo.x86_64 -batchmode -nographics -smoketest -scenario powerstep -radius 40 -gear 2 -thr 1 -ay 0.5 -logFile $P/R40_t1_regress.log >/dev/null 2>&1 &
wait; echo PDONE >> $L/statusp.txt
