#!/bin/bash
L=/workspace/scirocco_sim/builds/logs; cd /workspace/scirocco_sim/unity/SciroccoSimDemo
unity --no-banner --non-interactive build . --target StandaloneLinux64 --execute-method SciroccoSetup.BuildLinux -o /workspace/scirocco_sim/builds/linux/SciroccoSimDemo.x86_64 > $L/lb.out 2>&1; echo "BUILD_EXIT $?" > $L/status.txt
grep -q "BUILD_EXIT 0" $L/status.txt || exit 1
cd /workspace/scirocco_sim/builds/linux
for s in ${SCEN:-lagunaoff lagunagrass laguna lagunakerb park}; do a="-track laguna"; [ $s = kbdlaunch ] && a=""; timeout 450 ./SciroccoSimDemo.x86_64 -batchmode -nographics -smoketest $a -scenario $s -logFile $L/s_$s.log >/dev/null 2>&1; echo "$s exit $?" >> $L/status.txt; done
echo ALLDONE >> $L/status.txt
