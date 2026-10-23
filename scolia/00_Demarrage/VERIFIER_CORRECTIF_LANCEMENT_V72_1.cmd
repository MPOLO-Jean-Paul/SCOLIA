@echo off
setlocal
cd /d "%~dp0..\programme"
PowerShell.exe -NoProfile -ExecutionPolicy Bypass -File "%CD%\TestsCorrectifNotificationsV721.ps1"
echo.
if errorlevel 1 (
  echo ECHEC - Le correctif de lancement doit etre revu.
) else (
  echo OK - Le correctif de lancement V72.1 est valide.
)
pause
