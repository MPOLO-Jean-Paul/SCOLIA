@echo off
setlocal
powershell.exe -NoProfile -STA -ExecutionPolicy Bypass -File "%~dp0..\programme\TestsPresentationProfessionnelle.ps1"
if errorlevel 1 goto fin
powershell.exe -NoProfile -STA -ExecutionPolicy Bypass -File "%~dp0..\programme\TestsTableauxAdaptatifs.ps1"
:fin
pause
