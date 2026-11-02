$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'CotesDeliberation.ps1')
function Check($grades,$maxima,$decision,$missing,$failed){
 $r=[ITMDeliberation]::Evaluate([object[]]$grades,[decimal[]]$maxima)
 if($r.Decision -ne $decision -or $r.Missing -ne $missing -or $r.Failed -ne $failed){throw ('Wrong result '+($r|ConvertTo-Json))}
}
Check @(20,20) @(20,20) P 0 0
Check @(10,10) @(20,20) P 0 0
Check @(9,20) @(20,20) A 0 1
Check @(0,1) @(20,20) A 0 2
Check @($null,20) @(20,20) AA 1 0
Check @($null,19.999) @(20,20) D 1 0
Check @('',20) @(20,20) AA 1 0
Check @(' ',20) @(20,20) AA 1 0
Check @($null,$null) @(20,20) D 2 0
Check @(0,20,$null,20) @(20,20,20,20) AA 1 1
Check @($null,15,80) @(20,20,80) AA 1 0
Check @('9,5',20) @(20,20) A 0 1
Check @(10,10,$null) @(20,20,20) D 1 0
foreach($invalid in @('abc','#VALUE!',-1,21,$true)){
 $caught=$false;try{[void][ITMDeliberation]::Evaluate([object[]]@($invalid,10),[decimal[]]@(20,20))}catch{$caught=$true};if(-not $caught){throw 'Invalid grade accepted'}
}
$layout=[pscustomobject]@{Courses=@([pscustomobject]@{Column=3;Maximum=40},[pscustomobject]@{Column=5;Maximum=20})}
$formula=Get-ITMDecisionFormula $layout 14
if($formula -ne '=IF(COUNT(C14,E14)<2,IF(SUM(C14,E14)*100/60>=50,"AA","D"),IF(OR(C14<20,E14<10),"A","P"))'){throw ('Wrong formula '+$formula)}
'PASS decisions P/A/AA/D, 50% boundary, missing versus zero, weighted maxima, mixed missing/failures, invalid inputs and formula coordinates'

$cellStore=@{3=[pscustomobject]@{Value2=$null};5=[pscustomobject]@{Value2=20}}
$cells=[pscustomobject]@{Store=$cellStore};$cells | Add-Member ScriptMethod Item {param($row,$col)return $this.Store[$col]}
$sheet=[pscustomobject]@{Cells=$cells}
$r=Get-ITMRowDecision $sheet $layout 14
if($r.Missing -ne 1 -or $r.Decision -ne 'D' -or $r.Total -ne 20){throw 'Excel row missing not preserved'}
'PASS Excel-row null preservation'
