@echo off
"%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe" -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0..\programme\TestsSyntaxeV52.ps1"
echo.
if errorlevel 1 (echo Verification en echec. Consultez les erreurs ci-dessus.) else (echo Vous pouvez maintenant lancer APPLICATION.vbs.)
pause
