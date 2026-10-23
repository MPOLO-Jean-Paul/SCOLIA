@echo off
title SCOLIA - Mise a jour V57
echo Fermez SCOLIA avant de poursuivre.
echo Choisissez APPLICATION.vbs dans votre installation actuelle.
"%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe" -NoLogo -NoProfile -STA -ExecutionPolicy Bypass -File "%~dp0..\programme\MettreAJourV57.ps1"
set "SCOLIA_UPDATE_CODE=%ERRORLEVEL%"
echo.
if not "%SCOLIA_UPDATE_CODE%"=="0" echo Conservez les messages ci-dessus pour le diagnostic.
pause
exit /b %SCOLIA_UPDATE_CODE%
