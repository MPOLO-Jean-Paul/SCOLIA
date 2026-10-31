# Native PowerShell test, without Excel, registry or school data writes.
$ErrorActionPreference='Stop'
foreach($spec in @(@('Application.ps1','Key'),@('Application.ps1','Promo'),@('StockageClasseurs.ps1','Get-ITMIdentityPart'),@('StockageClasseurs.ps1','Assert-ITMLearnerIdentity'),@('StockageClasseurs.ps1','Invoke-ITMDossier'),@('StockageClasseurs.ps1','Get-ITMDuplicateLearners'),@('StockageClasseurs.ps1','Assert-ITMLearnerMerge'))){
    $tokens=$null;$errors=$null;$ast=[Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot $spec[0]),[ref]$tokens,[ref]$errors)
    if($errors.Count){throw ('Syntaxe invalide : '+$spec[0])}
    $name=$spec[1];$node=$ast.Find({param($n)$n -is [Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -eq $name},$true)
    . ([scriptblock]::Create($node.Extent.Text))
}
$script:sample=@([pscustomobject]@{Matricule='ABC001';Nom='MPOLO';PostNom='LONO';Prenom='JEAN PAUL';Promotion='TLM1';Naissance='01/01/2000';Eligible=$false;Source='classeur-000001.xlsm'})
function MustReject($mat,$nom,$post,$prenom,$promo){
    $rejected=$false
    try{Assert-ITMLearnerIdentity $script:sample $mat $nom $post $prenom $promo}catch{$rejected=$true;if($_.Exception.Message -notlike '*ABC001*'){throw 'Le message ne précise pas le dossier existant.'}}
    if(-not $rejected){throw 'Le doublon est accepté.'}
}
MustReject '' 'MPOLO' 'LONO' 'JEAN PAUL' 'TLM1'
MustReject '' ' mpolo ' 'lono' 'jean   paul' 'TML-1'
MustReject '' 'mpolo' 'lono' ("JEAN"+[char]160+'PAUL') 'TLM1'
MustReject 'AUT002' 'MPOLO' 'LONO' 'JEAN PAUL' 'TLM1'
Assert-ITMLearnerIdentity $script:sample 'ABC001' 'MPOLO' 'LONO' 'JEAN PAUL' 'TLM1'
Assert-ITMLearnerIdentity $script:sample '' 'MPOLO' 'LONO' 'JEAN PAUL' 'TLM2'
Assert-ITMLearnerIdentity $script:sample '' 'MPOLO' 'LONO' 'JEAN PIERRE' 'TLM1'
$script:sample[0].Prenom='ÉLÈNE'
MustReject '' 'mpolo' 'lono' 'elene' 'TLM1'
$script:sample[0].Prenom='JEAN PAUL'
$script:capacityCalled=$false;$script:writeCalled=$false;$script:readCalled=$false
function Assert-ITMPermission {param($permission)}
function Invoke-ITMJsonDirect {param($name);$script:readCalled=$true;return [pscustomobject]@{learners=$script:sample}}
function Ensure-ITMCapacity {param($kind);$script:capacityCalled=$true}
$script:book=[pscustomobject]@{Name='classeur-000002.xlsm'}
$script:excel=New-Object PSObject
$script:excel | Add-Member -MemberType ScriptMethod -Name Run -Value {$script:writeCalled=$true;return '{"ok":true}'}
$blocked=$false
try{Invoke-ITMDossier @('','MPOLO','LONO','JEAN PAUL','M','01/01/2006','LUBUMBASHI','','0990000000','','TLM1','2026-2027','Actif','')}catch{$blocked=$true}
if(-not $blocked -or -not $script:readCalled -or $script:capacityCalled -or $script:writeCalled){throw 'Le doublon est contrôlé après création de segment ou écriture.'}
Write-Output 'PASS : identité/promotion, casse, espaces, accents, dossier incomplet, modification de soi et refus avant toute écriture.'

$other=[pscustomobject]@{Matricule='ABC002';Nom='mpolo';PostNom='lono';Prenom='jean   paul';Promotion='TML-1';Naissance='02/02/2005'}
$found=@(Get-ITMDuplicateLearners @($script:sample[0],$other))
if($found.Count -ne 1 -or $found[0].Matricule -ne 'ABC002' -or $found[0].AutreMatricule -ne 'ABC001'){throw 'Diagnostic différent du contrôle avant inscription.'}
$other.Promotion='TLM2'
if(@(Get-ITMDuplicateLearners @($script:sample[0],$other)).Count){throw 'Deux promotions différentes sont signalées comme doublons.'}
Write-Output 'PASS : diagnostic conforme à la règle identité/promotion, indépendamment de la naissance.'

$mergeSnapshot=[pscustomobject]@{learners=@([pscustomobject]@{Matricule='ABC001';Nom='MPOLO';PostNom='LONO';Prenom='JEAN PAUL';Promotion='TLM1';Source='classeur-000001.xlsm'},[pscustomobject]@{Matricule='ABC002';Nom='mpolo';PostNom='lono';Prenom='jean   paul';Promotion='TML-1';Source='classeur-000002.xlsm'});payments=@()}
$pair=Assert-ITMLearnerMerge $mergeSnapshot 'ABC001' 'ABC002' 'Fusion des doublons'
if($pair.Keep.Matricule -ne 'ABC001' -or $pair.Merge.Matricule -ne 'ABC002'){throw 'Sélection des dossiers à fusionner incorrecte.'}
$blocked=$false;try{$null=Assert-ITMLearnerMerge $mergeSnapshot 'ABC001' 'ABC002' ''}catch{$blocked=$true};if(-not $blocked){throw 'Fusion acceptée sans motif.'}
$mergeSnapshot.learners[1].Promotion='TLM2';$blocked=$false;try{$null=Assert-ITMLearnerMerge $mergeSnapshot 'ABC001' 'ABC002' 'Fusion des doublons'}catch{$blocked=$true};if(-not $blocked){throw 'Fusion acceptée entre promotions différentes.'}
Write-Output 'PASS : validation de fusion, motif obligatoire et refus des promotions différentes.'
