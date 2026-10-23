$ErrorActionPreference='Stop'
# Fake timer invokes the production handler synchronously, without Windows GUI.
Add-Type -TypeDefinition @'
namespace System.Windows.Forms {
 public class Timer { public int Interval; public event System.EventHandler Tick; public void Start(){} public void Stop(){} public void Fire(){if(Tick!=null)Tick(this,System.EventArgs.Empty);} }
 public static class Application {public static System.Collections.Generic.List<object> OpenForms=new System.Collections.Generic.List<object>();}
}
'@
$t=$null;$e=$null;$ast=[Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot 'ScoliaIntegration.ps1'),[ref]$t,[ref]$e)
$fn=$ast.Find({param($n)$n -is [Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -eq 'Start-ScoliaMonitor'},$true);. ([scriptblock]::Create($fn.Extent.Text))
function Get-ScoliaInboxRole {return 'secretariat'}
function Check($ok,$why){if(-not $ok){throw $why}}
function Refresh-ScoliaFinance {throw 'Hidden journal redrawn'}
function Save-ScoliaAutomaticInbox {throw 'Synchronous disk save on UI thread'}
function Receive-ScoliaNext {$script:receives++;$script:scoliaTimer.Fire()}
$script:receives=0;$script:scoliaSession=$true;$script:scoliaNetworkHost=$true;$script:busy=$false;$script:scoliaRoot='remote';$script:scoliaLocalRoot='local';$script:scolia=[pscustomobject]@{Id='school';Year='2026-2027'}
$script:scoliaSync=[pscustomobject]@{State='';LastSuccess='';LastError='';FilesRead=0};$script:status=[pscustomobject]@{Text=''};$script:scoliaReader=[pscustomobject]@{LastFilesRead=1};$script:currentPage='Apprenants'
$script:scoliaReadContext='old-year';$script:scoliaReadTask=[pscustomobject]@{IsCompleted=$true;IsFaulted=$false;Result=[pscustomobject]@{Entries=@([pscustomobject]@{Id='bad';Revision=''});Roster='bad'}}
Start-ScoliaMonitor;$script:scoliaTimer.Fire()
Check ($script:receives -eq 0 -and -not $script:scoliaEntries -and -not $script:scoliaMonitorRunning) 'Stale context applied'
$script:scoliaReadContext='remote|school|2026-2027|local|secretariat';$script:scoliaReadTask=[pscustomobject]@{IsCompleted=$true;IsFaulted=$false;Result=[pscustomobject]@{Entries=@([pscustomobject]@{Id='good';Revision=''});Roster='good'}}
$script:scoliaTimer.Fire()
Check ($script:receives -eq 1 -and $script:scoliaEntries[0].Id -eq 'good' -and -not $script:scoliaMonitorRunning) 'Receive failed or nested timer reentered'
$script:busy=$true;$script:scoliaTimer.Fire();Check (-not $script:scoliaMonitorRunning -and $script:receives -eq 1) 'Busy guard not released'
'PASS production monitor: stale school/year response discarded, hidden page untouched, no UI disk save, nested ticks blocked, finally releases guard.'
$script:busy=$false;$first=$script:scoliaEntries
$script:scoliaReadTask=[pscustomobject]@{IsCompleted=$true;IsFaulted=$false;Result=[pscustomobject]@{Entries=@([pscustomobject]@{Id='good';Revision=''});Roster='updated'}}
$script:scoliaTimer.Fire()
Check ([object]::ReferenceEquals($first,$script:scoliaEntries) -and $script:scoliaRoster -eq 'updated') 'Unchanged journal replaced / roster update lost'
$script:scoliaReadTask=[pscustomobject]@{IsCompleted=$true;IsFaulted=$false;Result=[pscustomobject]@{Entries=@([pscustomobject]@{Id='good';Revision='1'});Roster='updated'}}
$script:scoliaTimer.Fire()
Check (-not [object]::ReferenceEquals($first,$script:scoliaEntries) -and $script:scoliaEntries[0].Revision -eq '1') 'Revision update ignored'
'PASS monitor retains unchanged journal identity and applies changed receipt revisions.'
Add-Type -TypeDefinition 'namespace System.Windows.Forms {public class Form {public static object ActiveForm;}}'
$t=$null;$e=$null;$ast=[Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot 'PerformanceInterface.ps1'),[ref]$t,[ref]$e)
$fn=$ast.Find({param($n)$n -is [Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -eq 'Wait-ScoliaNetworkTask'},$true);. ([scriptblock]::Create($fn.Extent.Text))
function New-ScoliaDialog {throw [InvalidOperationException]::new('UI failure')}
$pending=[Threading.Tasks.TaskCompletionSource[int]]::new();$safe=$false
try{Wait-ScoliaNetworkTask $pending.Task}catch{$safe=$_.Exception.GetBaseException() -is [IO.IOException]}
Check ($safe -and -not $pending.Task.IsCompleted) 'UI failure misclassified as definite rejection of financial write'
'PASS UI failure during pending task remains an uncertain I/O failure; pending receipt must be retained.'
