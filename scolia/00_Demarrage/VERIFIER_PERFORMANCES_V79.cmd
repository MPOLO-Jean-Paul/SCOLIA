@echo off
setlocal
set "SCOLIA_PS=%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe"
for %%T in (TestsSyntaxePowerShellV722 TestsPerformancesV79 TestsPerformancesV78 TestsMiseAJourPerformance TestsEnregistrementRapideV76 TestsPaiementsMultiplesV72 TestsReactivitePages TestsScolarite TestsInterfaceEnseignementsV59 TestsInterfacesV71 TestsInterfaceDisciplineV60) do (
  "%SCOLIA_PS%" -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0..\programme\%%T.ps1"
  if errorlevel 1 goto failed
)
echo Verification V79 terminee.
pause
exit /b 0
:failed
echo Verification interrompue : conservez le message affiche.
pause
exit /b 1
