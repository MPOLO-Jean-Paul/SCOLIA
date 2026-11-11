$ErrorActionPreference='Stop'
Add-Type -Path (Join-Path $PSScriptRoot 'CotesMatching.cs')
function Promo($value){return ([string]$value).ToUpperInvariant()}
. (Join-Path $PSScriptRoot 'GrillesParametrables.ps1')
$tokens=$null;$errors=$null;$ast=[Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot 'CotesDeliberation.ps1'),[ref]$tokens,[ref]$errors);$f=$ast.Find({param($n)$n -is [Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -eq 'Get-ITMGridLayout'},$true);. ([scriptblock]::Create($f.Extent.Text))
$map=[pscustomobject]@{Promotion='INF3';Sheet='Grille infirmiers';NameHeaderRow=3;FirstRow=8;LastRow=30;NameColumn=2;CourseRow=5;MaximumRow=7;LastColumn=6}
Test-ITMGridMap $map
$cells=[pscustomobject]@{};$cells|Add-Member ScriptMethod Item {param($row,$col)if($row -lt 1){throw 'Ligne zéro interdite'};$text='';$value=$null;$formula=$false;if($row -eq 3 -and $col -eq 2){$text='Nom complet'};if($row -eq 5 -and $col -eq 3){$text='Anatomie'};if($row -eq 5 -and $col -eq 4){$text='Soins'};if($row -eq 7 -and $col -in @(3,4)){$value=20};if($row -eq 7 -and $col -eq 5){$value=40;$formula=$true};if($script:extraGridRows -and $row -in @(31,32)){if($col -eq 1){$value=$row-7};if($col -eq 2){$text='Apprenant '+$row}};if($row -eq 33 -and $col -eq 1){$value=99};if($row -eq 33 -and $col -eq 2){$text='Total'};return [pscustomobject]@{Text=$text;Value2=$value;HasFormula=$formula;FormulaR1C1='=SUM(RC[-2]:RC[-1])';MergeCells=$false}}
$sheet=[pscustomobject]@{Name=$map.Sheet;Cells=$cells;UsedRange=[pscustomobject]@{Row=1;Rows=[pscustomobject]@{Count=35}}}
$l=Get-ITMGridLayout $sheet INF3 $map
if($l.Code -ne 'INF3' -or $l.Sheet -ne $map.Sheet -or $l.Courses.Count -ne 2 -or -not $l.Calculated.ContainsKey(5) -or $l.LastRow -ne 30){throw 'Lecture de grille paramétrée incorrecte.'}
foreach($change in @('MaximumRow','NameColumn','LastColumn')){$bad=$map.PSObject.Copy();$bad.$change=0;$failed=$false;try{Test-ITMGridMap $bad}catch{$failed=$true};if(-not $failed){throw ('Repère invalide accepté : '+$change)}}
$bad=$map.PSObject.Copy();$bad.MaximumRow=9;$failed=$false;try{Test-ITMGridMap $bad}catch{$failed=$true};if(-not $failed){throw 'Barème dans les données accepté.'}
'PASS INF3 sur feuille personnalisée, repères et bornes, cours/barèmes détectés, totaux calculés exclus ; cellules Excel simulées.'

$script:extraGridRows=$true;$expanded=Get-ITMGridLayout $sheet INF3 $map;if($expanded.LastRow -ne 32){throw 'Lignes ajoutées ignorées ou total intégré parmi les apprenants.'}
$script:extraGridRows=$false;$firstLine=$map.PSObject.Copy();$firstLine.CourseRow=1;[void](Get-ITMGridLayout $sheet INF3 $firstLine)
'PASS grille personnalisée agrandie reconnue, total exclu, entête à la première ligne sans accès à la ligne zéro.'
