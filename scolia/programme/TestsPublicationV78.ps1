$ErrorActionPreference='Stop'
Add-Type -TypeDefinition @'
using System;using System.Collections.Generic;using System.Threading.Tasks;
public class ScoliaRoster {public string SchoolId,Year;public List<string> Imported=new List<string>();}
public static class ScoliaSharing {public static int Calls;public static string YearKey(string v){return v;}public static Task PublishAsync(string root,ScoliaRoster r){Calls++;return Task.FromResult(0);}}
'@
function Load-TestFunction($file,$name){
 $t=$null;$e=$null;$ast=[Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot $file),[ref]$t,[ref]$e)
 $fn=$ast.Find({param($n)$n -is [Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -eq $name},$true)
 . ([scriptblock]::Create('function global:'+ $name + $fn.Extent.Text.Substring(('function '+$name).Length)))
}
Load-TestFunction 'ScoliaIntegration.ps1' 'Publish-ScoliaRoster'
Load-TestFunction 'PerformanceInterface.ps1' 'Sync-ScoliaRosterForPayment'
function Test-ScoliaPermission {return $true}
function Get-ScoliaConnectedYear {return '2026-2027'}
function Invoke-ITMJsonDirect {return @()}
function Add-ScoliaPublishedPayments {}
function Check($ok,$why){if(-not $ok){throw $why}}
$script:scolia=[pscustomobject]@{Id='school';Year='2026-2027';Revision=1};$script:scoliaSession=[pscustomobject]@{AccountId='test'};$script:book=$true;$script:data=[pscustomobject]@{learners=@()};$script:dataRevision=1;$script:storeLock=[pscustomobject]@{CanWrite=$true};$script:status=[pscustomobject]@{Text=''}
Publish-ScoliaRoster;Publish-ScoliaRoster;Check ([ScoliaSharing]::Calls -eq 1) 'Identical private publication repeated'
$script:scoliaRosterPending=$true;Publish-ScoliaRoster;Check ([ScoliaSharing]::Calls -eq 2) 'Explicit invalidation ignored'
$script:dataRevision++;Publish-ScoliaRoster;Check ([ScoliaSharing]::Calls -eq 3) 'New financial data ignored'
$script:excelBorrowed=$true;Publish-ScoliaRoster;Publish-ScoliaRoster;Check ([ScoliaSharing]::Calls -eq 5) 'Borrowed workbook cached unsafely';$script:excelBorrowed=$false
$pending=[Threading.Tasks.TaskCompletionSource[int]]::new();$script:scoliaRosterTask=$pending.Task;$script:dataRevision++
$script:waits=0
function Wait-ScoliaNetworkTask($task,$message){$script:waits++;if(-not $task.IsCompleted){$pending.SetResult(0)}}
Sync-ScoliaRosterForPayment
Check ([ScoliaSharing]::Calls -eq 6 -and $script:waits -eq 2 -and -not $script:scoliaRosterPending) 'Payment used stale in-flight publication'
$failure=[Threading.Tasks.TaskCompletionSource[int]]::new();$failure.SetException([IO.IOException]::new('test'));$script:scoliaRosterTask=$failure.Task;Publish-ScoliaRoster
Check ([ScoliaSharing]::Calls -eq 7) 'Failed publication treated as successful cache'
'PASS publications: private revision reuse; explicit invalidation; new data; borrowed workbook fresh; in-flight older revision followed by latest; failure retried.'
function Test-ScoliaPermission {return $false}
$blocked=$false;try{Sync-ScoliaRosterForPayment}catch{$blocked=$true}
Check ($blocked -and [ScoliaSharing]::Calls -eq 7) 'Missing authorization entered a publication loop'
'PASS missing permission rejects immediately without publication.'
