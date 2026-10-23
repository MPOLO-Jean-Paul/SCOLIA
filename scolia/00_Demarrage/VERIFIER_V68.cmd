@echo off
setlocal
cd /d "%~dp0.."
powershell.exe -NoProfile -STA -ExecutionPolicy Bypass -File "programme\TestsReactiviteV68.ps1"
if errorlevel 1 goto failed
powershell.exe -NoProfile -STA -ExecutionPolicy Bypass -File "programme\TestsInterfaceEspaceV67.ps1"
if errorlevel 1 goto failed
powershell.exe -NoProfile -STA -ExecutionPolicy Bypass -File "programme\TestsNavigation.ps1"
if errorlevel 1 goto failed
echo Verification V68 terminee.
pause
exit /b 0
:failed
echo Un controle a echoue. Conservez le message affiche pour le diagnostic.
pause
exit /b 1
