$ErrorActionPreference='Stop'
function Assert-V43([bool]$condition,[string]$message){if(-not $condition){throw $message}}
$root=$PSScriptRoot
$targets=@('Application.ps1','ScoliaConfiguration.ps1','StockageClasseurs.ps1','MiseAJourPerformance.ps1','PrisesEnCharge.ps1','PerformanceInterface.ps1')
foreach($name in $targets){
    $tokens=$null;$errors=$null
    [void][Management.Automation.Language.Parser]::ParseFile((Join-Path $root $name),[ref]$tokens,[ref]$errors)
    Assert-V43 ($errors.Count -eq 0) ($name+' contient une erreur de syntaxe : '+(($errors|ForEach-Object {$_.Message}) -join ' | '))
}
$app=Get-Content -LiteralPath (Join-Path $root 'Application.ps1') -Raw -Encoding UTF8
$config=Get-Content -LiteralPath (Join-Path $root 'ScoliaConfiguration.ps1') -Raw -Encoding UTF8
$store=Get-Content -LiteralPath (Join-Path $root 'StockageClasseurs.ps1') -Raw -Encoding UTF8
$perf=Get-Content -LiteralPath (Join-Path $root 'MiseAJourPerformance.ps1') -Raw -Encoding UTF8
$pec=Get-Content -LiteralPath (Join-Path $root 'PrisesEnCharge.ps1') -Raw -Encoding UTF8
Assert-V43 ($config -match 'function ConvertFrom-ScoliaClassText') 'Validation structurée des classes absente.'
Assert-V43 ($config -match 'function Test-ScoliaDataRootCandidate') 'Contrôle du dossier de données absent.'
Assert-V43 ($config -match 'scolia-ecriture-') 'Test d''écriture du dossier de données absent.'
Assert-V43 ($config -match 'Ajouter la classe') 'Ajout rapide de classe absent dans la configuration initiale.'
Assert-V43 ($config -match 'function Show-ScoliaLaunchTransition') 'Transition de démarrage absente.'
Assert-V43 ($app -match 'Show-ScoliaLaunchTransition') 'La transition n''est pas utilisée après la connexion.'
Assert-V43 ($app -match 'Close-ScoliaLaunchTransition') 'La transition n''est pas fermée à l''ouverture de la fenêtre principale.'
Assert-V43 ($app -notmatch 'MinimumSize=New-Object Drawing.Size\(\[Math\]::Min\(360,\$workArea.Width-24\)') 'Ancien minimum de fenêtre trop petit encore présent.'
Assert-V43 ($app -match 'minimumWidth=\[Math\]::Min\(900') 'Minimum responsive professionnel absent.'
Assert-V43 ($app -match 'Apply-ITMInstitution[\s\S]*?Set-ScoliaWorkspace') 'Le rôle connecté n''est pas réappliqué après la construction de l''interface.'
Assert-V43 ($app -match 'Show-Page \$\(if\(\$script:scoliaSession.*?Comptabilité') 'La page initiale n''est pas adaptée au rôle Comptabilité.'
Assert-V43 ($app -match 'ITMSuccessToast.*Inscription enregistrée') 'Confirmation moderne de l''inscription absente.'
Assert-V43 ($app -match 'Vous pouvez saisir immédiatement le prochain apprenant') 'Flux d''inscriptions successives absent.'
Assert-V43 ($app -match 'paySearch.Focus\(\).*paySearch.SelectAll\(\)') 'Préparation du reçu suivant absente.'
Assert-V43 ($perf -match 'Reset-ITMCompletedSaveLock') 'Nettoyage du verrou de sauvegarde absent.'
Assert-V43 ($store -match 'Complete-ITMStoreTransaction') 'Commit transactionnel du dossier apprenant absent.'
Assert-V43 ($pec -match 'Timer;\$timer.Interval=130') 'Recherche instantanée des prises en charge absente ou non temporisée.'
Assert-V43 ($app -match 'finally \{\s*# Release the operation gate[\s\S]*?\$script:busy=\$false') 'Le verrou UI n''est pas garanti dans finally.'
'PASS V43 : configuration, transition, fenêtre responsive, enregistrements successifs, paiements, transactions, verrou et recherche instantanée contrôlés.'
