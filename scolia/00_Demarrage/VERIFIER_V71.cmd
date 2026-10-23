@echo off
setlocal
cd /d "%~dp0.."
powershell.exe -NoProfile -STA -ExecutionPolicy Bypass -File "programme\TestsPaiementsDirectsV71.ps1"
if errorlevel 1 goto failed
powershell.exe -NoProfile -STA -ExecutionPolicy Bypass -File "programme\TestsInterfacesV71.ps1"
if errorlevel 1 goto failed
powershell.exe -NoProfile -STA -ExecutionPolicy Bypass -File "programme\TestsInterfaceEncaissementV69.ps1"
if errorlevel 1 goto failed
echo Verification V71 terminee.
pause
exit /b 0
:failed
echo Un controle a echoue. Conservez le message pour le diagnostic.
pause
exit /b 1
