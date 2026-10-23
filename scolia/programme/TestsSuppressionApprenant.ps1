# Isolated tests: no Excel, registry or school files are modified.
$ErrorActionPreference='Stop'
foreach($spec in @(@('Application.ps1','Key'),@('StockageClasseurs.ps1','Assert-ITMLearnerDeletion'),@('StockageClasseurs.ps1','Invoke-ITMLearnerDeletion'))){
    $tokens=$null;$errors=$null;$ast=[Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot $spec[0]),[ref]$tokens,[ref]$errors)
    if($errors.Count){throw ('Syntaxe invalide : '+$spec[0])}
    $name=$spec[1];$node=$ast.Find({param($n)$n -is [Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -eq $name},$true)
    . ([scriptblock]::Create($node.Extent.Text))
}
$learner=[pscustomobject]@{Matricule='ABC001';Source='classeur-000001.xlsm'}
$script:snapshot=[pscustomobject]@{learners=@($learner);payments=@()}
function MustReject($snapshot,$mat,$reason='motif valide'){
    $rejected=$false;try{$null=Assert-ITMLearnerDeletion $snapshot $mat $reason}catch{$rejected=$true}
    if(-not $rejected){throw 'Suppression non sûre acceptée.'}
}
if((Assert-ITMLearnerDeletion $script:snapshot ' abc001 ' 'Suppression administrative').Matricule -ne 'ABC001'){throw 'Normalisation incorrecte.'}
MustReject $script:snapshot ''
MustReject $script:snapshot 'ABC001' ''
MustReject $script:snapshot 'ABSENT'
MustReject $null 'ABC001'
MustReject ([pscustomobject]@{learners=@($learner)}) 'ABC001'
MustReject ([pscustomobject]@{learners=@($learner,$learner);payments=@()}) 'ABC001'
if((Assert-ITMLearnerDeletion ([pscustomobject]@{learners=@($learner);payments=@([pscustomobject]@{Matricule='ABC001';Source='classeur-000002.xlsm'})}) 'ABC001' 'Suppression administrative').Matricule -ne 'ABC001'){throw 'Un dossier avec paiement devrait pouvoir être sélectionné avec un motif.'}
$script:book=[pscustomobject]@{Name='classeur-000002.xlsm'};$active=$script:book
$script:store=$true;$script:storeBooks=@([pscustomobject]@{Name='classeur-000001.xlsm'},$active)
$script:writeCalled=$false;$script:failWrite=$false;$script:deny=$false
function Assert-ITMPermission {param($permission);if($script:deny){throw 'Accès refusé.'}}
function Invoke-ITMJsonDirect {
    param($name,$arguments)
    if($name -eq 'ITMAppSnapshot'){return $script:snapshot}
    if($name -ne 'ITMAppSupprimerApprenant' -or $arguments[0] -ne 'ABC001' -or $script:book.Name -ne 'classeur-000001.xlsm'){throw 'Mauvaise commande ou mauvais classeur.'}
    $script:writeCalled=$true;if($script:failWrite){throw 'Échec simulé.'};return [pscustomobject]@{ok=$true}
}
$null=Invoke-ITMLearnerDeletion ' abc001 ' 'Suppression administrative'
if(-not $script:writeCalled -or $script:book -ne $active){throw 'Routage ou restauration du classeur incorrect.'}
$script:failWrite=$true;$rejected=$false;try{$null=Invoke-ITMLearnerDeletion 'ABC001' 'Suppression administrative'}catch{$rejected=$true}
if(-not $rejected -or $script:book -ne $active){throw 'Classeur actif non restauré après échec.'}
$script:failWrite=$false;$script:deny=$true;$script:writeCalled=$false
try{$null=Invoke-ITMLearnerDeletion 'ABC001' 'Suppression administrative'}catch{}
if($script:writeCalled){throw 'Suppression sans permission.'}
$script:deny=$false;$script:storeBooks=@($active);$script:writeCalled=$false
try{$null=Invoke-ITMLearnerDeletion 'ABC001' 'Suppression administrative'}catch{}
if($script:writeCalled -or $script:book -ne $active){throw 'Suppression autorisée sans classeur propriétaire.'}
Write-Output 'PASS : motif obligatoire, suppression avec historique de paiement, sélection, permission, routage et restauration après échec.'
