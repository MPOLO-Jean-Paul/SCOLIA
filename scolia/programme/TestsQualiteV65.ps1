$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'JsonCompatV65.ps1')
. (Join-Path $PSScriptRoot 'InterfaceQualiteV65.ps1')
function Check-V65($Value,[string]$Message){if(-not $Value){throw $Message};$script:checkedV65++}
$script:checkedV65=0
$json='{"Type":"document","stamp":"2026-10-13T00:05:00.1234567+02:00","utc":"2026-10-12T22:05:00Z","date":"2026-10-13","amount":1500,"enabled":false,"missing":null,"items":[],"nested":{"text":"École Cœur de Dieu"}}'
$value=ConvertFrom-ScoliaJson $json
Check-V65 ($value.Type -eq 'document') 'La propriété Type est perdue.'
Check-V65 ($value.stamp -is [string] -and $value.stamp -ceq '2026-10-13T00:05:00.1234567+02:00') 'Décalage ou précision de l horodatage modifiés.'
Check-V65 ($value.utc -is [string] -and $value.date -is [string]) 'Une chaîne de date est devenue un objet DateTime.'
Check-V65 ($value.amount -eq 1500 -and $value.enabled -eq $false -and $null -eq $value.missing -and $value.items -is [array] -and $value.items.Count -eq 0) 'Valeurs JSON altérées.'
Check-V65 ($value.nested.text -ceq 'École Cœur de Dieu') 'Accents JSON altérés.'
Check-V65 ((ConvertFrom-ScoliaJson -InputObject $json).stamp -ceq $value.stamp) 'Le chargement nommé InputObject ne conserve pas l horodatage.'
$copy=$value|ConvertTo-Json -Depth 15|ConvertFrom-ScoliaJson
Check-V65 ($copy.stamp -ceq $value.stamp -and $copy.items.Count -eq 0) 'Une copie modifie les dates ou les tableaux vides.'
foreach($screen in @(@{X=0;Y=0;W=1366;H=728},@{X=-1920;Y=0;W=1920;H=1040},@{X=0;Y=-1080;W=1280;H=1024},@{X=20;Y=30;W=320;H=240})){
 foreach($size in @(@{W=1800;H=1200;MW=1600;MH=1000},@{W=860;H=660;MW=400;MH=420},@{W=360;H=300;MW=0;MH=0})){
  foreach($position in @(@{X=-32000;Y=-32000},@{X=5000;Y=3000},@{X=0;Y=0})){
   $g=Get-ScoliaWindowGeometry $screen.X $screen.Y $screen.W $screen.H $position.X $position.Y $size.W $size.H $size.MW $size.MH
   Check-V65 ($g.X -ge $screen.X -and $g.Y -ge $screen.Y -and $g.X+$g.Width -le $screen.X+$screen.W -and $g.Y+$g.Height -le $screen.Y+$screen.H) 'Une fenêtre dépasse de son écran.'
   Check-V65 ($g.Width -ge $g.MinWidth -and $g.Height -ge $g.MinHeight -and $g.Width -gt 0 -and $g.Height -gt 0) 'Dimensions ou minimums contradictoires.'
  }
 }
}
Write-Host ('OK : '+$script:checkedV65+' contrôles JSON, dates, écran réduit, multi-écrans et limites des fenêtres.') -ForegroundColor Green
