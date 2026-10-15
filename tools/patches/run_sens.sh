#!/bin/bash
# @description - understeer sensitivity: 40 m / 2nd gear, throttle 1.0 step, one estimated parameter changed per run (override files in builds/ov/us)
# @param {String} arguments - command-line arguments (see the script body)
# @return {Int} - process exit status (0 = success)
# understeer sensitivity: 40 m / 2nd gear, throttle 1.0 step, one estimated parameter changed per run (override files in builds/ov/us)
L=/workspace/scirocco_sim/builds/logs/sens; mkdir -p $L; cd /workspace/scirocco_sim/builds/linux
n=0; for f in /workspace/scirocco_sim/builds/ov/us/*.txt; do b=$(basename "$f" .txt)
  timeout 300 ./SciroccoSimDemo.x86_64 -batchmode -nographics -smoketest -scenario powerstep -radius ${RAD:-40} -gear ${GEAR:-2} -thr 1 -ay 0.5 -overrides "$f" -logFile "$L/${RAD:-40}_$b.log" >/dev/null 2>&1 &
  n=$((n+1)); [ $((n%7)) -eq 0 ] && wait; done; wait; echo DONE > $L/done${RAD:-40}
