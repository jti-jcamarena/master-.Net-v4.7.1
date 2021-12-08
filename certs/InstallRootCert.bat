@echo OFF
echo Installing ofs root certificate...
certmgr.exe -add -c tylerofsefmrootsha2.crt -s -r localMachine root
echo Done
pause