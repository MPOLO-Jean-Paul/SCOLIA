@echo off
"%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe" -NoLogo -NoProfile -STA -ExecutionPolicy Bypass -File "%~dp0..\programme\VerifierV57.ps1"
set "SCOLIA_CHECK_CODE=%ERRORLEVEL%"
echo.
if not "%SCOLIA_CHECK_CODE%"=="0" echo Consultez les resultats et les journaux de verification.
pause
exit /b %SCOLIA_CHECK_CODE%
