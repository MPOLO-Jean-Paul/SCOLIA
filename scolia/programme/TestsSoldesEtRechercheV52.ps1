# Tests isolés : vrais calculs PowerShell, sans Excel ni données de production.
$ErrorActionPreference='Stop'
function Import-V52Function([string]$file,[string]$name){
    $tokens=$null;$errors=$null
    $ast=[Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot $file),[ref]$tokens,[ref]$errors)
    if($errors.Count){throw ('Syntaxe invalide : '+$file)}
    $node=$ast.Find({param($n)$n -is [Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -eq $name},$true)
    if(-not $node){throw $name}
    . ([scriptblock]::Create('function global:'+$name+$node.Extent.Text.Substring(('function '+$name).Length)))
}
foreach($spec in @(@('Application.ps1','Key'),@('Application.ps1','Paid'),@('PerformanceInterface.ps1','Get-ITMBalanceKey'),@('PerformanceInterface.ps1','Apply-ITMLearnerSlice'))){Import-V52Function $spec[0] $spec[1]}
function Rebuild-ITMLearnerIndex {$script:dataRevision++}
function Assert-V52([bool]$value,[string]$message){if(-not $value){throw $message}}
$script:data=[pscustomobject]@{learners=@();payments=@();balances=@();months=@('Septembre','Octobre')}
$script:balance=@{'ABC001::SEPTEMBRE'=[decimal]100;'ABC001::OCTOBRE'=[decimal]30;'ABC002::SEPTEMBRE'=[decimal]75}
$slice=[pscustomobject]@{ok=$true;matricule='abc001';learner=[pscustomobject]@{Matricule='ABC001'};payments=@();balances=@([pscustomobject]@{Matricule='ABC001';Mois='Septembre';Verse=150})}
Apply-ITMLearnerSlice $slice
Assert-V52 ((Paid 'ABC001' 'Septembre') -eq 150) 'Versement ciblé absent du solde mensuel.'
Assert-V52 ((Paid 'ABC001' 'Octobre') -eq 0) 'Ancien solde conservé après correction.'
Assert-V52 ((Paid 'ABC001') -eq 150) 'Total annuel incorrect après actualisation ciblée.'
Assert-V52 ((Paid 'ABC002' 'Septembre') -eq 75) 'Solde d''un autre apprenant modifié.'
$slice.balances=@();Apply-ITMLearnerSlice $slice
Assert-V52 ((Paid 'ABC001') -eq 0) 'Annulation du versement non reflétée.'
. (Join-Path $PSScriptRoot 'EtatEspace.ps1')
$entry=[pscustomobject]@{Reference='CAISSE-017';Nom='Émile';PostNom='MPOLO';Prenom='Jean';Promotion='INF1';Description='Mensualité';Kind='Paiement';OperatorName='Caisse'}
Assert-V52 (Test-ITMFinanceSearch $entry 'emile inf1') 'Recherche accentuée ou multicritère incorrecte.'
Assert-V52 (Test-ITMFinanceSearch $entry '  ') 'Recherche vide incorrecte.'
Assert-V52 (-not (Test-ITMFinanceSearch $entry 'emile inf2')) 'Recherche acceptant une promotion différente.'
$state=@{Session=$true;Role='Mixte';DataReady=$true;Pending=$false;SyncReady=$true;SyncError=$false;Store=$true;BackupEnabled=$true;BackupToday=$true;BackupError=$false;Issues=0;Closed=$false}
Assert-V52 (@(Get-ITMHealthChecks $state | Where-Object {$_.Etat -eq 'À vérifier'}).Count -eq 0) 'Session prête signalée en erreur.'
$state.Pending=$true;$state.BackupError=$true;$state.SyncError=$true;$state.DataReady=$false
Assert-V52 (@(Get-ITMHealthChecks $state | Where-Object {$_.Etat -eq 'À vérifier'}).Count -eq 4) 'Incident de session non signalé.'
$state.Role='Comptabilité'
Assert-V52 (@(Get-ITMHealthChecks $state | Where-Object {$_.Controle -eq 'Données scolaires'}).Count -eq 0) 'Classeur exigé à tort pour la comptabilité.'
Import-V52Function 'StockageClasseurs.ps1' 'Restore-ITMStore'
function Assert-ITMPermission {}
$tempRoot=Join-Path ([IO.Path]::GetTempPath()) ('v52-restauration-'+[guid]::NewGuid().ToString('N'))
try{
    $script:storeFolder=$tempRoot;$script:store=[pscustomobject]@{Id=[guid]::NewGuid().ToString('N')}
    $backup=Join-Path $tempRoot 'sauvegardes/test';[void][IO.Directory]::CreateDirectory($backup)
    [IO.File]::WriteAllText((Join-Path $backup 'registre.json'),('{"Id":"'+[guid]::NewGuid().ToString('N')+'"}'))
    $rejected=$false
    try{Restore-ITMStore $backup}catch{if($_.Exception.Message -notlike '*autre dossier*'){throw};$rejected=$true}
    Assert-V52 $rejected 'Sauvegarde d''un autre dossier acceptée avant restauration.'
}finally{if([IO.Directory]::Exists($tempRoot)){[IO.Directory]::Delete($tempRoot,$true)}}
'PASS : soldes, isolation des apprenants, recherche, contrôles de session et refus de restauration d''un autre dossier.'
