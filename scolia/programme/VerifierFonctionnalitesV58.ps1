# Tests isolés, chacun dans un processus distinct pour éviter les conflits de types.
$ErrorActionPreference='Stop'
$hostPath=Join-Path $PSHOME 'powershell.exe'
$failures=@()
$tests=@(
    'TestsAuditCotes',
    'TestsCatalogueCommandes',
    'TestsCentreRechercheV55',
    'TestsComptes',
    'TestsCotes',
    'TestsDeliberation',
    'TestsDoublonsIdentite',
    'TestsExploitation',
    'TestsFinancePasserelle',
    'TestsFinanceSynchronisee',
    'TestsFonctionnalitesV55',
    'TestsHistorique',
    'TestsImportsReseau',
    'TestsNettoyageSauvegardes',
    'TestsPersonnelV56',
    'TestsPresenceFinJourV57',
    'TestsPresencesV55',
    'TestsPrisesEnCharge',
    'TestsReinscriptionV55',
    'TestsRetardsCalendrier',
    'TestsRetardsPaieV57',
    'TestsRevueFonctionnelle',
    'TestsRevueFonctionnelleV58',
    'TestsSalairesV56',
    'TestsSauvegardesPersonnalisees',
    'TestsSauvegardesV55',
    'TestsSauvegardesV56',
    'TestsScoliaCatalogue',
    'TestsScoliaConfiguration',
    'TestsScoliaFiabilite',
    'TestsScoliaImport',
    'TestsScoliaLedger',
    'TestsScoliaOptimisations',
    'TestsScoliaReception',
    'TestsServicesPersonnelV56',
    'TestsSeuilHoraireV57',
    'TestsSoldesEtRechercheV52',
    'TestsSourcesPaieV57',
    'TestsSuiviApprenantsV56',
    'TestsSuppressionApprenant',
    'TestsTransactionApprenant',
    'TestsValidationConnexion',
    'TestsEnseignementsV59',
    'TestsGrillesCoursV59',
    'TestsDisciplineV60',
    'TestsPaiementsFiabiliteV61'
)
foreach($name in $tests){
    Write-Host ('Vérification : '+$name)
    & $hostPath -NoProfile -STA -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot ($name+'.ps1'))
    if($LASTEXITCODE -ne 0){$failures+=@($name)}
}
if($failures.Count){Write-Host ('Échecs : '+($failures -join ', '));exit 1}
Write-Host ('PASS : '+$tests.Count+' suites de vérification.')
exit 0
