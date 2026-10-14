#!/bin/bash
L=/workspace/scirocco_sim/builds/logs; P=/workspace/scirocco_sim/unity/SciroccoSimDemo; R=/workspace/scirocco_sim/builds; D=/workspace/scirocco_sim/dist; cd $P
unity --no-banner --non-interactive test . --mode EditMode --output $R/unit_tests.xml > $L/t.out 2>&1; echo "TEST_EXIT $?" > $L/rest.txt
rm -rf $R/windows/SciroccoSimDemo; unity --no-banner --non-interactive build . --target StandaloneWindows64 --execute-method SciroccoSetup.BuildWindows -o $R/windows/SciroccoSimDemo/SciroccoSimDemo.exe > $L/wb.out 2>&1; echo "WIN_EXIT $?" >> $L/rest.txt
cd $R/linux && timeout 300 ./SciroccoSimDemo.x86_64 -batchmode -nographics -smoketest -scenario kbdlaunch -logFile $L/s_kbdlaunch.log >/dev/null 2>&1; echo "kbdlaunch exit $?" >> $L/rest.txt
mkdir -p $D; cd $D && rm -f SciroccoSimDemo_*.7z SciroccoSimDemo_*.zip
(cd $R/windows && 7z a -t7z -m0=lzma2 -mx=9 -md=256m -mfb=273 -ms=on -mqs=on $D/SciroccoSimDemo_Windows_x64.7z SciroccoSimDemo >/dev/null && zip -qr $D/SciroccoSimDemo_Windows_x64.zip SciroccoSimDemo)
(cd $R && zip -qr $D/SciroccoSimDemo_Linux_x64.zip linux)
(cd $P && zip -qr $D/SciroccoSimDemo_UnityProject.zip Assets Packages ProjectSettings README.md UserSettings)
cp $R/laguna_import_check.txt $D/; echo ALLDONE >> $L/rest.txt
