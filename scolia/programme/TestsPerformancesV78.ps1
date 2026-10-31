$ErrorActionPreference='Stop'
function Import-TestFunction([string]$File,[string]$Name){
 $tokens=$null;$errors=$null;$ast=[Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot $File),[ref]$tokens,[ref]$errors)
 $fn=$ast.Find({param($n)$n -is [Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -eq $Name},$true)
 if(-not $fn){throw $Name};. ([scriptblock]::Create('function global:'+ $Name + $fn.Extent.Text.Substring(('function '+$Name).Length)))
}
function Check($ok,$why){if(-not $ok){throw $why}}
Import-TestFunction 'Application.ps1' 'Key'
foreach($n in @('Get-ScoliaPaymentRows','Wait-ScoliaNetworkTask')){Import-TestFunction 'PerformanceInterface.ps1' $n}
$script:data=[pscustomobject]@{payments=@(
 [pscustomobject]@{Matricule='A';Mois='Septembre';Date='01/10/2026';ID=2;Verse=20},
 [pscustomobject]@{Matricule='A';Mois='Octobre';Date='01/10/2026';ID=3;Verse=30},
 [pscustomobject]@{Matricule='B';Mois='Septembre';Date='02/10/2026';ID=1;Verse=10},
 [pscustomobject]@{Matricule='A';Mois='Septembre';Date='invalide';ID=9;Verse=40}
)}
Check (((Get-ScoliaPaymentRows 'a'|ForEach-Object ID)-join ',') -eq '3,2,9') 'Sort/identity incorrect'
Check (((Get-ScoliaPaymentRows ''|ForEach-Object ID)-join ',') -eq '1,3,2,9') 'All-payment order incorrect'
Check (((Get-ScoliaPaymentRows 'A' 'SEPTEMBRE'|ForEach-Object ID)-join ',') -eq '2,9') 'Month filter incorrect'
Check (@(Get-ScoliaPaymentRows 'absent').Count -eq 0) 'Absent learner leaked rows'
$old=$script:paymentLookup;[void](Get-ScoliaPaymentRows 'A');Check ([object]::ReferenceEquals($old,$script:paymentLookup)) 'Unchanged index rebuilt'
$script:data.payments=@([pscustomobject]@{Matricule='A';Mois='Octobre';Date='02/10/2026';ID=4;Verse=50})
Check ((Get-ScoliaPaymentRows 'A').Verse -eq 50) 'Stale financial values after slice'
Check (-not [object]::ReferenceEquals($old,$script:paymentLookup)) 'Source replacement did not invalidate'
1..150|ForEach-Object {[void](Get-ScoliaPaymentRows ('missing'+$_))};Check ($script:paymentLookup.Sorted.Count -le 128) 'Unbounded cache'
$script:data.payments=@();Check (@(Get-ScoliaPaymentRows).Count -eq 0) 'Empty source retained history'
$done=[Threading.Tasks.Task]::FromResult([int]42);Check ((Wait-ScoliaNetworkTask $done) -eq 42) 'Completed task result lost'
$failed=[Threading.Tasks.TaskCompletionSource[int]]::new();$failed.SetException([IO.IOException]::new('network-test'))
$rejected=$false;try{Wait-ScoliaNetworkTask $failed.Task}catch{$rejected=$_.Exception.GetBaseException().Message -eq 'network-test'};Check $rejected 'Network error swallowed'
'PASS index: all/single/month/order/invalid dates/absence, invalidation, bounds; completed and failed network tasks.'
# Compare disciplinary indexed lookup to the original full traversal, including case-sensitive keys.
. (Join-Path $PSScriptRoot 'DisciplineV60.ps1')
function Get-ScoliaDisciplineDate([string]$Value){return [datetime]::ParseExact($Value,'yyyy-MM-dd',[Globalization.CultureInfo]::InvariantCulture)}
$events=@([pscustomobject]@{Matricule='A';State='Validated';Type='Temporary';Year='2026-2027';StartDate='2026-10-01';EndDate='2026-10-14'},[pscustomobject]@{Matricule='B';State='Validated';Type='Permanent';Year='2025-2026';DecisionDate='2026-01-01'},[pscustomobject]@{Matricule='a';State='Validated';Type='Permanent';Year='2026-2027';DecisionDate='2026-01-01'})
$docs=@([pscustomobject]@{Items=$events});$index=New-Object 'System.Collections.Generic.Dictionary[string,object]' ([StringComparer]::Ordinal)
foreach($e in $events){$index.Add($e.Matricule,@($e))}
foreach($day in @('2026-10-14','2026-10-15')){foreach($mat in @('A','B','C')){
 $full=Get-ScoliaDisciplineLearnerStatus $docs $mat '2026-2027' ([datetime]$day)
 $fast=Get-ScoliaDisciplineLearnerStatus $docs $mat '2026-2027' ([datetime]$day) $index
 Check ($full.Mode -ceq $fast.Mode -and $full.Description -ceq $fast.Description) 'Indexed discipline changed decision'
}}
'PASS indexed discipline matches full traversal, end-date boundary, permanent previous year, missing learner, case-sensitive keys.'
# Synthetic reproducible measure; no Windows/Excel latency guarantee.
$rows=New-Object 'System.Collections.Generic.List[object]'
1..10000 | ForEach-Object {$rows.Add([pscustomobject]@{Matricule=('M'+($_%1000));Mois='Septembre';Date='01/10/2026';ID=$_;Verse=10})}
$script:data.payments=$rows.ToArray();[void](Get-ScoliaPaymentRows 'M1')
$watch=[Diagnostics.Stopwatch]::StartNew()
1..20 | ForEach-Object {$m='M'+$_;$legacy=@($script:data.payments|Where-Object {(Key $_.Matricule) -eq (Key $m)}|Sort-Object @{Expression={[datetime]::ParseExact($_.Date,'dd/MM/yyyy',[Globalization.CultureInfo]::InvariantCulture)}},ID -Descending)}
$watch.Stop();$before=$watch.Elapsed.TotalMilliseconds;$watch.Restart()
1..20 | ForEach-Object {$fast=@(Get-ScoliaPaymentRows ('M'+$_));Check ($fast.Count -eq 10) 'Synthetic lookup lost payments'}
$watch.Stop();'BENCH synthetic 10000 payments / 20 selections: previous='+[Math]::Round($before,2)+' ms; indexed='+[Math]::Round($watch.Elapsed.TotalMilliseconds,2)+' ms (initial index excluded).'
