# No Office writes: test full rows, not names alone.
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'CotesDeliberation.ps1')
$matrix=[Array]::CreateInstance([object],[int[]]@(4,5),[int[]]@(1,1))
$records=@(@(99,'Zola Jean',7,'=RC[-1]*2','A'),@(1,'émile Ngoy',0,'=RC[-1]*2','A'),@($null,$null,$null,$null,$null),@(8,'  Anne Mpolo ',19,'=RC[-1]*2','P'))
for($r=0;$r -lt 4;$r++){for($c=0;$c -lt 5;$c++){$matrix.SetValue($records[$r][$c],$r+1,$c+1)}}
$order=[ITMGridAlphabetical]::Sort($matrix,[string[]]@())
if(-not $order.Changed -or $order.Names.Length -ne 3 -or $order.Matrix[0,1] -ne '  Anne Mpolo ' -or $order.Matrix[1,1] -ne 'émile Ngoy' -or $order.Matrix[2,1] -ne 'Zola Jean'){throw 'Alphabetical order incorrect'}
foreach($entry in $records){if(-not $entry[1]){continue};$index=0;while($order.Matrix[$index,1] -ne $entry[1]){$index++};for($c=1;$c -lt 5;$c++){if($order.Matrix[$index,$c] -ne $entry[$c]){throw 'Identity/grade/formula/decision moved separately'}}}
if($order.Matrix[0,0] -ne 1 -or $order.Matrix[1,0] -ne 2 -or $order.Matrix[2,0] -ne 3 -or $null -ne $order.Matrix[3,0]){throw 'Numbering/blank rows incorrect'}
$second=[ITMGridAlphabetical]::Sort($order.Matrix,[string[]]@());if($second.Changed){throw 'Repeated sorting is not stable'}
# Separate Nom/PostNom/Prenom ordering can differ from concatenated full names.
$special=[object[,]]::new(2,3);$special[0,0]=1;$special[0,1]='AB C Jean';$special[0,2]=12;$special[1,0]=2;$special[1,1]='AB D Jean';$special[1,2]=18
$ordered=[ITMGridAlphabetical]::Sort($special,[string[]]@('AB D Jean','AB C Jean'));if($ordered.Matrix[0,1] -ne 'AB D Jean' -or $ordered.Matrix[0,2] -ne 18){throw 'Separate identity ordering ignored'}
$special[1,1]='ab-c Jean';$rejected=$false;try{[void][ITMGridAlphabetical]::Sort($special,[string[]]@())}catch{$rejected=$true};if(-not $rejected){throw 'Duplicate names accepted'}
'PASS alphabetical ordering, accents, whitespace, full row preservation, zero grades, relative formulas, numbering, blank rows, stable rerun, separate identity keys and duplicates'
