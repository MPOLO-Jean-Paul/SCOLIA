$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'ImportsReseau.ps1')
. (Join-Path $PSScriptRoot 'PaiementsDirectsV71.ps1')
. (Join-Path $PSScriptRoot 'FinanceSynchronisee.ps1')
Add-Type -Path (Join-Path $PSScriptRoot 'ScoliaLedger.cs') -ReferencedAssemblies System.Xml,System.Core,System.Runtime.Serialization
$tokens=$null;$errors=$null;$ast=[Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot 'ScoliaIntegration.ps1'),[ref]$tokens,[ref]$errors)
foreach($name in @('Get-ScoliaConnectedYear','Test-ScoliaReceiptYear','Get-ScoliaInboxRows','Receive-ScoliaNext','Resolve-ScoliaReceipt','Publish-ScoliaRoster')){$node=$ast.Find({param($n)$n -is [Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -eq $name},$true);. ([scriptblock]::Create($node.Extent.Text))}
$root=Join-Path ([IO.Path]::GetTempPath()) ('scolia-reception-'+[guid]::NewGuid().ToString('N'));$script:scolia=[pscustomobject]@{Id=[guid]::NewGuid().ToString('N');Year='2026 - 2027'}
$script:book=[pscustomobject]@{};$script:data=[pscustomobject]@{closed=$false;promotions=@([pscustomobject]@{Annee='2026 - 2027'},[pscustomobject]@{Annee='2026-2027'})};$script:status=[pscustomobject]@{Text=''};$script:busy=$false;$script:scoliaImporting=$false;$script:scoliaAuto=$true;$script:received=@();$script:marked=@()
function Get-ScoliaStateRoot {return $root}
function Money($n){return [string]$n}
function Test-ScoliaPermission {return $true}
function Find-ScoliaLearner {return @([pscustomobject]@{Matricule='INF101'})}
function Import-ScoliaReceipt($e,$create){$script:received+= $e.Id}
function Do-Work($action,$label){& $action}
function Set-ScoliaReceiptState($e,$status,$mat,$message){$script:marked+=$e.Id}
function Receipt([string]$year,[string]$kind='Paiement'){$e=New-Object ScoliaEntry;$e.Id=[guid]::NewGuid().ToString('N');$e.SchoolId=$script:scolia.Id;$e.Annee=$year;$e.Kind=$kind;$e.Reference='CAISSE-001';$e.Montant=100;return $e}
try{
 $old=Receipt '2025 - 2026';$current=Receipt '2026-2027';$expense=Receipt '2026 - 2027' 'Depense';$future=Receipt '2027-2028';$script:scoliaEntries=@($old,$current,$expense,$future)
 Receive-ScoliaNext;if($script:received.Count -ne 1 -or $script:received[0] -ne $current.Id -or $script:marked.Count){throw 'Autre année importée ou marquée en erreur'}
 $years=[pscustomobject]@{Text='';Items=(New-Object Collections.ArrayList);SelectedItem=''};$node=$ast.Find({param($n)$n -is [Management.Automation.Language.AssignmentStatementAst] -and $n.Left.Extent.Text -eq '$fillYears'},$true);$block=[scriptblock]::Create($node.Right.Extent.Text.Trim().TrimStart('{').TrimEnd('}'));[void](& $block);if($years.Items.Count -ne 3 -or $years.SelectedItem -ne '2026-2027'){throw 'Sélecteur des années incohérent'}
 $rows=@(Get-ScoliaInboxRows);if($rows.Count -ne 1 -or $rows[0].Id -ne $current.Id){throw 'Brouillons mélangent les années'}
 if(@(Get-ScoliaInboxRows '2025-2026')[0].Id -ne $old.Id){throw 'Archives de transfert inaccessibles'}
 if((Resolve-ScoliaReceipt 'CAISSE-001' 'Depense' '2026-2027').Id -ne $expense.Id -or (Resolve-ScoliaReceipt 'CAISSE-001' 'Paiement' '2026 - 2027').Id -ne $current.Id){throw 'Dépense confondue avec paiement de même référence'}
 $script:scoliaEntries+= (Receipt '2026-2027');$rejected=$false;try{Resolve-ScoliaReceipt 'CAISSE-001' 'Paiement' '2026-2027'}catch{$rejected=$true};if(-not $rejected){throw 'Reçu ambigu imprimable'};$script:scoliaEntries=@($old,$current,$expense,$future)
 $state=New-Object ScoliaState;$state.Id=$current.Id;$state.SchoolId=$script:scolia.Id;$state.Status='Erreur';[ScoliaLedger]::SaveState($root,$state);$script:received=@();Receive-ScoliaNext;if($script:received.Count){throw 'Erreur rejouée sans décision utilisateur'}
 $state.Status='Importé';[ScoliaLedger]::SaveState($root,$state);if(@(Get-ScoliaInboxRows).Count){throw 'Paiement importé toujours en brouillon'}
 # Previously imported receipts synchronize corrections even with new imports disabled.
 $script:scoliaAuto=$false;$current.Revision='1';$script:received=@();Receive-ScoliaNext;if($script:received.Count -ne 1 -or $script:received[0] -ne $current.Id){throw 'Correction déjà importée non reçue automatiquement'}
 $state.Revision='1';[ScoliaLedger]::SaveState($root,$state);$script:received=@();Receive-ScoliaNext;if($script:received.Count){throw 'Correction appliquée rejouée'}
 $fresh=Receipt '2026-2027';$script:scoliaEntries=@($fresh);Receive-ScoliaNext;if($script:received.Count){throw 'Nouveau reçu importé malgré option désactivée'};$script:scoliaEntries=@($old,$current,$expense,$future)
 $script:data.promotions+= [pscustomobject]@{Annee='2027 - 2028'};if(Test-ScoliaReceiptYear $current){throw 'Classeur multiannée ambigu accepté'}
 $script:data.promotions=@();if(Test-ScoliaReceiptYear $current){throw 'Classeur sans année accepté'}
 $script:data=$null;if(Test-ScoliaReceiptYear $current){throw 'Classeur sans données accepté'}
 $script:scoliaSession=[pscustomobject]@{Role='Secrétariat'};$script:data=[pscustomobject]@{promotions=@([pscustomobject]@{Annee='2025-2026'})};function Invoke-ITMJsonDirect {throw 'Ancienne base envoyée au répertoire courant'};Publish-ScoliaRoster
 'PASS isolation des années, archives consultables, réception ciblée, reçus distincts paiement/dépense, ambiguïtés bloquées, états erreur/importé préservés (orchestration Excel simulée)'
}finally{if([IO.Directory]::Exists($root)){[IO.Directory]::Delete($root,$true)}}
