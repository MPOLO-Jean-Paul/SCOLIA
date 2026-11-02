$ErrorActionPreference='Stop'
$root=$PSScriptRoot
function Assert-V51([bool]$ok,[string]$message){if(-not $ok){throw $message}}
$lancer=Get-Content -LiteralPath (Join-Path $root 'Lancer.ps1') -Raw -Encoding UTF8
$app=Get-Content -LiteralPath (Join-Path $root 'Application.ps1') -Raw -Encoding UTF8
$config=Get-Content -LiteralPath (Join-Path $root 'ScoliaConfiguration.ps1') -Raw -Encoding UTF8
$session=Get-Content -LiteralPath (Join-Path $root 'InstallationSession.ps1') -Raw -Encoding UTF8
$perf=Get-Content -LiteralPath (Join-Path $root 'PerformanceInterface.ps1') -Raw -Encoding UTF8
Assert-V51 ($lancer -match 'TransitionExperience\.ps1') 'Le moteur de transition n est pas chargé au lancement.'
Assert-V51 ($session -match "Show-ScoliaTransition 'Installation terminée") 'La transition post-installation manque.'
Assert-V51 ($config -match "Show-ScoliaTransition 'Configuration enregistrée") 'La transition configuration vers connexion manque.'
Assert-V51 ($config -match "Show-ScoliaTransition 'Connexion réussie") 'La transition connexion vers accueil manque.'
Assert-V51 ($config -match 'Add_Shown\(\{Close-ScoliaTransition\}\)') 'La fermeture à affichage réel manque sur les écrans de préconnexion.'
Assert-V51 ($app -match '\$main\.Add_Shown\(\{Close-ScoliaTransition\}\)') 'La transition ne reste pas jusqu à l affichage réel de l accueil.'
Assert-V51 ($perf -match "if\(-not \('ITMActivity' -as \[type\]\)\)") 'La protection anti double chargement de l indicateur manque.'
'OK - transitions V51 présentes et reliées.'
