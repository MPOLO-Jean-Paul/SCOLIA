$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$fiabilite = Get-Content -LiteralPath (Join-Path $root 'Corrections/ModuleFiabilite.txt') -Raw -Encoding UTF8
$app = Get-Content -LiteralPath (Join-Path $root 'Corrections/ModuleApplicationITM.txt') -Raw -Encoding UTF8
$form = Get-Content -LiteralPath (Join-Path $root 'Corrections/Apprenant.txt') -Raw -Encoding UTF8
if ($fiabilite -match 'If ThisWorkbook\.Saved Then\s+ITMRestaurerEtat\s+Else\s+Err\.Raise vbObjectError \+ 912') { throw 'Ancien verrou bloquant encore present.' }
if ($fiabilite -notmatch 'ITMAnnulerSaisie[\s\S]*?ITMJournalAnnuler') { throw 'La recuperation doit annuler les donnees avant le journal.' }
$validate = [regex]::Match($fiabilite, '(?s)Public Sub ITMValiderSaisie\(\)(.*?)End Sub').Value
if ($validate -match 'Echec:.*?ITMRestaurerEtat') { throw 'ITMValiderSaisie libere le verrou avant que l appelant puisse annuler.' }
if ($app -notmatch 'ITMDebutSaisie w: actif = True: ITMJournalDebut') { throw 'La transaction d inscription n initialise pas son etat.' }
if ($app -notmatch 'ITMAnnulerSaisie') { throw 'La passerelle ne restaure pas la ligne reservee en cas d erreur.' }
if ($form -notmatch 'ITMAppDossier\("", CStr\(TextNomApprenant\.Value\)') { throw 'Le formulaire ne demande pas une nouvelle inscription au service metier.' }
'PASS: recuperation de verrou, annulation transactionnelle, conservation du verrou jusqu au rollback et chemin d inscription controles statiquement.'

$start = [regex]::Match($fiabilite, '(?s)Public Sub ITMDebutSaisie\(ByVal ws As Worksheet\)(.*?)End Sub').Value
if ($start -notmatch 'ITMAnnulerSaisie[\s\S]*?ITMJournalAnnuler') { throw 'La recuperation doit annuler les donnees avant le journal et tenter les deux operations.' }
if ($start -notmatch 'mRecoveryRequired') { throw 'Une annulation incomplete doit interdire une nouvelle saisie.' }
if ($fiabilite -match 'Une operation precedente contient peut-etre des modifications non sauvegardees') { throw 'Ancien message de verrou persistant encore present dans le correctif.' }
if ($fiabilite -notmatch 'ITM-TRANSACTION-19') { throw 'Version transactionnelle V19 absente.' }
