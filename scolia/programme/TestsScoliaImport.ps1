$ErrorActionPreference='Stop'
Add-Type -Path (Join-Path $PSScriptRoot 'ScoliaLedger.cs') -ReferencedAssemblies System.Xml,System.Core,System.Runtime.Serialization
. (Join-Path $PSScriptRoot 'ImportsReseau.ps1')
. (Join-Path $PSScriptRoot 'FinanceSynchronisee.ps1')
function Save-ScoliaAppliedReceipt {}
function Assert-ScoliaFinanceWorkbook {}
function Get-ScoliaStateRoot {return [IO.Path]::GetTempPath()}
$tokens=$null;$errors=$null;$ast=[Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot 'ScoliaIntegration.ps1'),[ref]$tokens,[ref]$errors)
$function=$ast.Find({param($n)$n -is [Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -eq 'Import-ScoliaReceipt'},$true);. ([scriptblock]::Create($function.Extent.Text))
$script:scolia=[pscustomobject]@{Id='00112233445566778899aabbccddeeff'};$script:book=[pscustomobject]@{};$script:data=[pscustomobject]@{closed=$false};$script:store=[pscustomobject]@{};$script:writes=0;$script:receipts=@{};$script:failAck=$true;$script:rollback=0;$script:failMacro=$false
function Assert-ITMPermission {}
function Test-ScoliaReceiptYear {return $script:allowYear}
$script:allowYear=$true
function Find-ScoliaLearner {return @()}
function Ensure-ITMCapacity {}
function Start-ITMStoreTransaction {}
function Complete-ITMStoreTransaction {}
function Undo-ITMStoreTransaction {$script:rollback++}
function Invoke-ITMJsonDirect($name,$arguments){if($script:failMacro){throw 'macro failed'};if($arguments.Count -ne 8 -or $arguments[1].Split("`t").Count -ne 13 -or $arguments[2] -ne 'Septembre' -or $arguments[3] -ne '100'){throw 'Packed receipt malformed'};if(-not $script:receipts.ContainsKey($arguments[0])){$script:receipts[$arguments[0]]='INF101';$script:writes++};return [pscustomobject]@{ok=$true;matricule=$script:receipts[$arguments[0]]}}
function Set-ScoliaReceiptState {if($script:failAck){throw 'Acknowledgement interrupted'}}
function Refresh-Data {}
function Update-ITMAfterWrite {return $true}
function Write-ITMUserAudit {}
$entry=New-Object ScoliaEntry;$entry.Id='aabbccddeeff00112233445566778899';$entry.SchoolId=$script:scolia.Id;$entry.Kind='Paiement';$entry.Nom='Nom';$entry.PostNom='Post';$entry.Prenom='Prenom';$entry.Promotion='INF1';$entry.Annee='2026-2027';$entry.Reference='CAISSE-001';$entry.Compte='Comptabilité';$entry.Date='2026-10-09';$p=New-Object ScoliaPart;$p.Mois='Septembre';$p.Montant=100;$entry.Parts.Add($p)

$script:allowYear=$false;$rejected=$false;try{Import-ScoliaReceipt $entry $true}catch{$rejected=$true};if(-not $rejected -or $script:writes -ne 0){throw 'Autre année écrite dans le classeur'};$script:allowYear=$true
$failed=$false;try{Import-ScoliaReceipt $entry $true}catch{$failed=$true};if(-not $failed -or $script:writes -ne 1 -or $script:rollback -ne 0){throw 'Committed receipt incorrectly rolled back after acknowledgement failure'}
$script:failAck=$false;[void](Import-ScoliaReceipt $entry $true);if($script:writes -ne 1){throw 'Orchestration lost replay reference'}
$script:failMacro=$true;$failed=$false;try{Import-ScoliaReceipt $entry $true}catch{$failed=$true};if(-not $failed -or $script:rollback -ne 1){throw 'Failed macro not rolled back'}
'PASS orchestration import : référence stable après accusé interrompu, transaction conservée après commit, annulation après échec macro (Excel simulé)'
