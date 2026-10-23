@echo off
setlocal
for %%T in (TestsCompilation.ps1 TestsQualiteV65.ps1 TestsPersonnelV56.ps1 TestsEnseignementsV59.ps1 TestsDisciplineV60.ps1 TestsPaiementsFiabiliteV61.ps1 TestsCotes.ps1 TestsSignatairesV63.ps1 TestsRapportRentreeV62.ps1 TestsRubanAdaptatifV64.ps1 TestsSeuilHoraireV57.ps1 TestsRetardsCalendrier.ps1 TestsGrillesCoursV59.ps1 TestsServicesPersonnelV56.ps1 TestsSourcesPaieV57.ps1 TestsInterfaceQualiteV65.ps1 TestsNavigation.ps1) do (
    powershell.exe -NoProfile -STA -ExecutionPolicy Bypass -File "%~dp0..\programme\%%T"
    if errorlevel 1 goto erreur
)
echo VERIFICATIONS TERMINEES SANS ERREUR
pause
exit /b 0
:erreur
echo Une verification a echoue. Conservez le message affiche avant cette ligne.
pause
exit /b 1
