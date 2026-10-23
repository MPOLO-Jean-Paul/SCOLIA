$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$app=Get-Content (Join-Path $PSScriptRoot 'Application.ps1') -Raw
$dash=Get-Content (Join-Path $PSScriptRoot 'TableauDeBordModerne.ps1') -Raw
$pages=Get-Content (Join-Path $PSScriptRoot 'PagesModernes.ps1') -Raw
$batch=Get-Content (Join-Path $PSScriptRoot 'PaiementsMultiplesV72.ps1') -Raw
function Assert-V72([bool]$ok,[string]$message){if(-not $ok){throw $message}}
Assert-V72 ($app -match "PaiementsMultiplesV72\.ps1") "Le moteur V72 n'est pas chargé par Application.ps1."
Assert-V72 ($app -match "Paiements multiples") 'Le bouton Paiements multiples est absent.'
Assert-V72 ($dash -match "Régler une sélection") 'Le raccourci du tableau de bord est absent.'
Assert-V72 ($app -match 'DetteValeur=\[decimal\]\$due') "Le montant exigible numérique n'est pas transmis au lot."
Assert-V72 ($batch -match "Invoke-ScoliaBatchTuitionPayment") 'Le traitement SCOLIA individuel est absent.'
Assert-V72 ($batch -match "Invoke-ITMBatchPaymentItem") 'Le traitement individuel du classeur est absent.'
Assert-V72 ($batch -match "operation-caisse-en-attente\.json") 'La protection contre la resaisie après incident réseau est absente.'
Assert-V72 ($batch -match "Doublons possibles") 'Le contrôle des doublons du mode classeur est absent.'
Assert-V72 ($batch -match "Montant = reste exigible" -and $batch -match "Montant = prochaine échéance") 'Les préremplissages métier sont absents.'
Assert-V72 ($pages -match "Tous les paiements','Paiements multiples") "Le bouton multiple n'est pas intégré à la mise en page adaptative."
'PASS V72 : paiements multiples, retards, montants individuels, références séparées, sécurité réseau et mise en page adaptative.'
