@echo off
setlocal
cd /d "%~dp0.."
powershell.exe -NoProfile -ExecutionPolicy Bypass -File ".\programme\TestsPaiementsMultiplesV72.ps1"
echo.
pause
