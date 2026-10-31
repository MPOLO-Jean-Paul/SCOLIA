@echo off
powershell.exe -NoProfile -STA -ExecutionPolicy Bypass -File "%~dp0..\programme\TestsDisciplineV60.ps1"
if errorlevel 1 goto fin
powershell.exe -NoProfile -STA -ExecutionPolicy Bypass -File "%~dp0..\programme\TestsInterfaceDisciplineV60.ps1"
:fin
pause
