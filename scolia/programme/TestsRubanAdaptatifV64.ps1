$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'RubanAdaptatifV64.ps1')
function Assert-RubanV64($Value,[string]$Message){if(-not $Value){throw $Message}}
$cases=@([int[]]@(2,4,3,5),[int[]]@(4,6,8,3,2,5),[int[]]@(8,8,8,8,8,8,8,8),[int[]]@(1,17,2,12,3,1))
$tested=0
foreach($scale in @(1,1.25,1.5,2)){
 foreach($width in @(330,480,680,900,1280,1600)){
  foreach($height in @(100,180,300)){
   foreach($counts in $cases){
    $w=[int]($width*$scale);$h=[int]($height*$scale);$textWidths=[int[]]@($counts|ForEach-Object {[int](150*$scale)});$textHeights=[int[]]@($counts|ForEach-Object {[int](34*$scale)})
    $plan=Get-ScoliaRibbonLayout $counts $w $h $scale $textWidths $textHeights
    Assert-RubanV64 ($plan.Bands -eq 1) 'Des groupes passent sur une seconde rangée.'
    Assert-RubanV64 $plan.Fits 'Le ruban ne tient pas dans la hauteur disponible.'
    Assert-RubanV64 ($plan.Height -le $h) 'Le ruban déborde en hauteur.'
    Assert-RubanV64 ($plan.Items.Count -eq $counts.Count) 'Un groupe a disparu.'
    $rectangles=New-Object 'System.Collections.Generic.List[object]';$sum=0
    foreach($item in $plan.Items){
     Assert-RubanV64 ($item.Band -eq 0 -and $item.Y -eq 0) 'Un groupe se trouve sous les autres.'
     Assert-RubanV64 ($item.Count -eq $counts[$item.Index]) 'Des commandes ont disparu.'
     Assert-RubanV64 ($item.X -ge 0 -and $item.X+$item.Width -le $plan.Width -and $item.Y+$item.Height -le $h) 'Un groupe est hors du ruban.'
     $sum+=$item.Count
     if($plan.IconOnly){Assert-RubanV64 ($item.CellWidth -ge [int](32*$scale) -and $item.CellHeight -ge [int](32*$scale)) 'Commande trop petite pour être utilisée.'}
     for($i=0;$i -lt $item.Count;$i++){
      $col=$i%$item.Columns;$row=[int][Math]::Floor($i/[double]$item.Columns)
      $left=$item.X+$plan.Gap+$col*($item.CellWidth+$plan.Gap);$top=$item.Y+$plan.Gap+$row*($item.CellHeight+$plan.Gap)
      $rectangle=[pscustomobject]@{Left=$left;Right=$left+$item.CellWidth;Top=$top;Bottom=$top+$item.CellHeight}
      Assert-RubanV64 ($rectangle.Right -le $item.X+$item.Width -and $rectangle.Bottom -le $item.Y+$item.Height) 'Une commande sort de son groupe.'
      foreach($previous in $rectangles){Assert-RubanV64 (-not ($rectangle.Left -lt $previous.Right -and $rectangle.Right -gt $previous.Left -and $rectangle.Top -lt $previous.Bottom -and $rectangle.Bottom -gt $previous.Top)) 'Deux commandes se recouvrent.'}
      $rectangles.Add($rectangle)
     }
    }
    Assert-RubanV64 ($sum -eq ($counts|Measure-Object -Sum).Sum) 'Nombre de commandes incorrect.'
    $tested++
   }
  }
 }
}
$wide=Get-ScoliaRibbonLayout ([int[]]@(2,4,3,5)) 1600 300 1 ([int[]]@(150,150,150,150)) ([int[]]@(34,34,34,34))
$small=Get-ScoliaRibbonLayout ([int[]]@(2,4,3,5)) 330 100
Assert-RubanV64 (-not $wide.IconOnly -and $small.IconOnly) 'Les libellés ne reviennent pas quand la fenêtre est agrandie.'
Write-Host ('OK : '+$tested+' dispositions, groupes sur une seule rangée, toutes les commandes conservées, sans recouvrement, défilement horizontal si nécessaire, cibles de 32 pixels minimum, DPI de 100 à 200 %.') -ForegroundColor Green
