@echo off
"%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe" -NoLogo -NoProfile -STA -ExecutionPolicy Bypass -File "%~dp0..\programme\VerifierV52.ps1"
echo.
echo Consultez les resultats et les journaux affiches ci-dessus.
pause
