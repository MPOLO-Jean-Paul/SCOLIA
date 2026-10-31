@echo off
setlocal
cd /d "%~dp0.."
powershell.exe -NoProfile -ExecutionPolicy Bypass -STA -File ".\programme\TestsCompteNominatifV74_1.ps1"
echo.
pause
