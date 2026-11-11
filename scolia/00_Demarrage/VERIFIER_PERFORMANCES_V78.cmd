@echo off
setlocal
set "SCOLIA_PS=%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe"
for %%T in (TestsSyntaxePowerShellV722 TestsPerformancesV78 TestsFinanceIndexV78 TestsReceptionAsynchroneV78 TestsMoniteurV78 TestsPublicationV78 TestsAttenteReseauWindowsV78 TestsDisciplineV60 TestsEnregistrementRapideV76 TestsPaiementsMultiplesV72 TestsReactivitePages) do (
  "%SCOLIA_PS%" -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0..\programme\%%T.ps1"
  if errorlevel 1 goto failed
)
echo Verification terminee.
pause
exit /b 0
:failed
echo Verification interrompue : conservez le message affiche.
pause
exit /b 1
