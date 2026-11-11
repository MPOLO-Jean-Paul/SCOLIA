@echo off
powershell.exe -NoProfile -STA -ExecutionPolicy Bypass -File "%~dp0..\programme\TestsSignatairesV63.ps1"
if errorlevel 1 goto fin
powershell.exe -NoProfile -STA -ExecutionPolicy Bypass -File "%~dp0..\programme\TestsRapportRentreeV62.ps1"
:fin
pause
