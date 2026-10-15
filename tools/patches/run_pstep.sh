#!/bin/bash
# @description - powerstep matrix: radius/gear x throttle; extra args via EXTRA (e.g. -overrides file), tag via TAG
# @param {String} arguments - command-line arguments (see the script body)
# @return {Int} - process exit status (0 = success)
# powerstep matrix: radius/gear x throttle; extra args via EXTRA (e.g. -overrides file), tag via TAG
L=/workspace/scirocco_sim/builds/logs/pstep${TAG}; mkdir -p $L; cd /workspace/scirocco_sim/builds/linux
for rg in "40 2" "100 3"; do set -- $rg; for th in -1 0 0.25 0.5 1; do
  timeout 300 ./SciroccoSimDemo.x86_64 -batchmode -nographics -smoketest -scenario powerstep -radius $1 -gear $2 -thr $th -ay 0.5 $EXTRA -logFile $L/R$1_t$th.log >/dev/null 2>&1 &
done; done; wait; echo DONE > $L/done
