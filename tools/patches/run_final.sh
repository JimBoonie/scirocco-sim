#!/bin/bash
L=/workspace/scirocco_sim/builds/logs; rm -f $L/status.txt $L/rest.txt $L/bump.txt
./run_linux.sh
cd /workspace/scirocco_sim/builds/linux
for a in "bumpstart" "bumpstart -ignoff" "stallrestart"; do n=$(echo $a | tr -d ' -'); timeout 200 ./SciroccoSimDemo.x86_64 -batchmode -nographics -smoketest -scenario $a -logFile $L/s_$n.log >/dev/null 2>&1; echo "$n exit $?" >> $L/bump.txt; done
timeout 400 ./SciroccoSimDemo.x86_64 -batchmode -nographics -smoketest -scenario powerstep -radius 40 -gear 2 -thr 1 -ay 0.5 -logFile $L/pstep100/R40_t1_regress.log >/dev/null 2>&1; echo "pstep exit $?" >> $L/bump.txt
timeout 200 ./SciroccoSimDemo.x86_64 -batchmode -nographics -smoketest -scenario park -logFile $L/s_parkflat.log >/dev/null 2>&1; echo "parkflat exit $?" >> $L/bump.txt
cd /workspace/scirocco_sim/patches && ./run_rest.sh
