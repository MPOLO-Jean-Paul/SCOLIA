# Vérifications statiques V37. Aucune donnée Excel n'est modifiée.
$ErrorActionPreference='Stop'
function Assert-V37([bool]$ok,[string]$message){if(-not $ok){throw $message}}
$root=$PSScriptRoot
$app=Get-Content -LiteralPath (Join-Path $root 'Application.ps1') -Raw -Encoding UTF8
$perf=Get-Content -LiteralPath (Join-Path $root 'PerformanceInterface.ps1') -Raw -Encoding UTF8
$storage=Get-Content -LiteralPath (Join-Path $root 'StockageClasseurs.ps1') -Raw -Encoding UTF8
$pec=Get-Content -LiteralPath (Join-Path $root 'PrisesEnCharge.ps1') -Raw -Encoding UTF8
$launch=Get-Content -LiteralPath (Join-Path $root 'Lancer.ps1') -Raw -Encoding UTF8
$notify=Get-Content -LiteralPath (Join-Path $root 'NotificationsModernes.cs') -Raw -Encoding UTF8
$bridge=Get-Content -LiteralPath (Join-Path $root 'Corrections/ModuleApplicationITM.txt') -Raw -Encoding UTF8
foreach($name in @('Tableau de bord','Apprenants','Paiements','Exports','Diagnostic','Administration','Sécurité','Préférences','Données','Scolarité','Comptabilité')){
    Assert-V37 ($app -match [regex]::Escape("'$name'")) ('Icône ou navigation absente : '+$name)
}
Assert-V37 ($app -match 'ITMSuccessToast') 'Toast de succès moderne absent de l inscription/paiement.'
Assert-V37 ($app -match 'Update-ITMAfterWrite \$savedMat \$true') 'Rafraîchissement différé de l inscription absent.'
Assert-V37 ($perf -match 'function Update-ITMAfterWrite\(\[string\]\$mat,\[bool\]\$deferView=\$false\)') 'Actualisation ciblée après écriture absente.'
Assert-V37 ($storage -match 'Start-ITMStoreTransaction @\(\$script:book\)') 'Transaction ciblée du dossier absente.'
Assert-V37 ($storage -match "Invoke-ITMJsonDirect 'ITMAppEtatApprenant'") 'Vérification ciblée du dossier absente.'
Assert-V37 ($bridge -match 'Public Function ITMAppEtatApprenant') 'Passerelle de lecture ciblée absente.'
Assert-V37 ($pec -match 'Rechercher un apprenant' -and $pec -match '\$timer\.Interval=130') 'Recherche instantanée des prises en charge absente.'
Assert-V37 ($launch -match 'LastWriteTimeUtc\.Ticks' -and $launch -match '\$cached\.StartsWith\(\$meta') 'Cache de démarrage rapide absent.'
Assert-V37 ($notify -match 'public sealed class ITMSuccessToast' -and $notify -match 'timer=new Timer') 'Notification moderne temporisée absente.'
'PASS : icônes distinctes, recherche prise en charge, confirmation moderne, actualisation ciblée et optimisations de démarrage présentes.'
