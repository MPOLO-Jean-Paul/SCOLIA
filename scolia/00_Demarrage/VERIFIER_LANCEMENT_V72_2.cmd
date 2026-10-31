@echo off
setlocal
cd /d "%~dp0..\programme"
echo ============================================================
echo SCOLIA V72.2 - VERIFICATION DE LANCEMENT
echo ============================================================
echo.
echo [1/3] Analyse syntaxique de tous les scripts PowerShell...
PowerShell.exe -NoProfile -ExecutionPolicy Bypass -File "%CD%\TestsSyntaxePowerShellV722.ps1"
if errorlevel 1 goto :fail

echo.
echo [2/3] Verification du correctif NotificationsModernes...
PowerShell.exe -NoProfile -ExecutionPolicy Bypass -File "%CD%\TestsCorrectifNotificationsV721.ps1"
if errorlevel 1 goto :fail

echo.
echo [3/3] Verification des paiements multiples...
PowerShell.exe -NoProfile -ExecutionPolicy Bypass -File "%CD%\TestsPaiementsMultiplesV72.ps1"
if errorlevel 1 goto :fail

echo.
echo ============================================================
echo OK - SCOLIA V72.2 a passe les controles de lancement.
echo ============================================================
pause
exit /b 0

:fail
echo.
echo ============================================================
echo ECHEC - Un controle a echoue. Copiez le message affiche.
echo ============================================================
pause
exit /b 1
