# Tests Windows PowerShell 5.1 sans Excel ni donnees scolaires reelles.
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'MiseAJourPerformance.ps1')
function Assert-Operation([bool]$ok,[string]$message){if(-not $ok){throw $message}}
function Import-ITMTestFunction([string]$file,[string]$name){
    $tokens=$null;$errors=$null;$ast=[Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot $file),[ref]$tokens,[ref]$errors)
    if($errors.Count){throw 'Syntaxe invalide.'}
    $node=$ast.Find({param($n)$n -is [Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -eq $name},$true)
    return [scriptblock]::Create($node.Extent.Text)
}
. (Import-ITMTestFunction 'PerformanceInterface.ps1' 'Refresh-ITMCurrentPage')
$script:viewRevision=@{};$script:dataRevision=1;$script:data=[pscustomobject]@{learners=@()};$script:payMat=''
$script:calls=@{Learners=0;Payments=0;Dashboard=0;Admin=0;Cards=0}
function Refresh-Learners {param($target);$script:calls.Learners++}
function Refresh-Payments {$script:calls.Payments++}
function Refresh-Dashboard {$script:calls.Dashboard++}
function Refresh-Administration {$script:calls.Admin++}
function Refresh-ITMSchoolCards {$script:calls.Cards++}
$script:currentPage='Apprenants';Refresh-ITMCurrentPage;Refresh-ITMCurrentPage
Assert-Operation ($script:calls.Learners -eq 1 -and $script:calls.Admin -eq 0 -and $script:calls.Cards -eq 0) 'Pages masquees rechargees ou page reconstruite deux fois.'
$script:currentPage='Administration';Refresh-ITMCurrentPage
Assert-Operation ($script:calls.Admin -eq 1) 'Journal non charge a son ouverture.'
$script:dataRevision++;$script:viewRevision.Clear();$script:currentPage='Paiements';Refresh-ITMCurrentPage
Assert-Operation ($script:calls.Payments -eq 1 -and $script:calls.Admin -eq 1) 'Paiements dupliques ou journal recharge lors de la saisie.'
$script:currentPage='Scolarité';Refresh-ITMCurrentPage
Assert-Operation ($script:calls.Cards -eq 1) 'Cartes non actualisees a leur ouverture.'
# Real synchronous write boundary, with a fake Excel server.
$script:book=[pscustomobject]@{Name='test.xlsm';Saved=$true};$script:storeFolder='';$script:resetCalls=0;$script:writeCalls=0;$script:fakeReply='{"ok":true}'
function Api-Name {param($name);return "'test.xlsm'!ModuleApplicationITM."+$name}
$script:excel=New-Object PSObject
$script:excel | Add-Member -MemberType ScriptMethod -Name Run -Value {
    param($macro)
    if($macro.Contains('ITMFinSaisieSansSituation')){$script:resetCalls++;return}
    $script:writeCalls++;return $script:fakeReply
}
$script:committedThisAction=$false
[void](Invoke-ITMWriteMacro 'ITMAppPaiement' @('ABC001',1000,'2026-10-08'))
Assert-Operation ($script:resetCalls -eq 2 -and $script:writeCalls -eq 1 -and $script:committedThisAction) 'Verrou termine non libere ou succes durable non marque.'
$script:book.Saved=$false;$script:resetCalls=0
[void](Invoke-ITMWriteMacro 'ITMAppPaiement' @('ABC001',1000,'2026-10-08'))
Assert-Operation ($script:resetCalls -eq 0) 'Verrou de travail non sauvegarde efface.'
$script:book.Saved=$true;$script:resetCalls=0;$script:committedThisAction=$false;$script:fakeReply='{"ok":false,"error":"test"}'
[void](Invoke-ITMWriteMacro 'ITMAppPaiement' @('ABC001',1000,'2026-10-08'))
Assert-Operation (-not $script:committedThisAction -and $script:resetCalls -eq 1) 'Echec traite comme un succes.'
$temp=Join-Path ([IO.Path]::GetTempPath()) ('ITM-verrou-'+[guid]::NewGuid().ToString('N'))
try{
    [void][IO.Directory]::CreateDirectory($temp);[IO.File]::WriteAllText((Join-Path $temp 'transaction.json'),'test')
    $script:storeFolder=$temp;$script:resetCalls=0;Reset-ITMCompletedSaveLock
    Assert-Operation ($script:resetCalls -eq 0) 'Transaction non validee deprotegee.'
}finally{if([IO.Directory]::Exists($temp)){[IO.Directory]::Delete($temp,$true)};$script:storeFolder=''}
# UI gate releases even when setup, the action or spinner disposal throws.
Add-Type -AssemblyName System.Windows.Forms
. (Import-ITMTestFunction 'Application.ps1' 'Do-Work')
$script:busy=$false;$script:paySearch=$null;$script:main=$null;$script:setupFailure=$false;$script:stopFailure=$false;$script:actionCalls=0
function Get-ITMActivityAnchor {return $null}
function Update-WorkspaceState {if($script:setupFailure){$script:setupFailure=$false;throw 'setup'}}
function Start-ITMActivity {param($message,$anchor)}
function Stop-ITMActivity {if($script:stopFailure){throw 'spinner'}}
function App-Error {param($errorRecord,$committed);$script:lastError=[string]$errorRecord}
$script:setupFailure=$true;Do-Work {$script:actionCalls++}
Assert-Operation (-not $script:busy -and $script:actionCalls -eq 0) 'Verrou bloque apres erreur de preparation.'
Do-Work {throw 'action'};Assert-Operation (-not $script:busy) 'Verrou bloque apres erreur de saisie.'
$script:stopFailure=$true;Do-Work {$script:actionCalls++}
Assert-Operation (-not $script:busy -and $script:actionCalls -eq 1) 'Verrou bloque apres erreur du spinner.'
$script:stopFailure=$false;Do-Work {$script:actionCalls++}
Assert-Operation ($script:actionCalls -eq 2 -and -not $script:busy) 'Saisie suivante impossible.'
$script:busy=$true;Do-Work {$script:actionCalls++}
Assert-Operation ($script:actionCalls -eq 2) 'Operation imbriquee executee.'
$script:busy=$false
Write-Output 'PASS : pages visibles, cache des vues, garde de saisie, erreurs et transactions non validees.'
