@echo off
powershell.exe -NoProfile -STA -ExecutionPolicy Bypass -File "%~dp0..\programme\TestsSauvegardesPersonnalisees.ps1"
if errorlevel 1 goto fin
powershell.exe -NoProfile -STA -ExecutionPolicy Bypass -File "%~dp0..\programme\TestsRechercheSauvegardesDesign.ps1"
:fin
pause
