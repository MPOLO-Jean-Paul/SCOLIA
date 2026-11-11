@echo off
powershell.exe -NoProfile -STA -ExecutionPolicy Bypass -File "%~dp0..\programme\TestsRubanAdaptatifV64.ps1"
if errorlevel 1 goto fin
powershell.exe -NoProfile -STA -ExecutionPolicy Bypass -File "%~dp0..\programme\TestsNavigation.ps1"
:fin
pause
