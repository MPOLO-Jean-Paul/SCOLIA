# Plan géométrique du ruban : aucun contrôle Windows ni accès aux données ici.
function New-ScoliaRibbonCandidate([int[]]$Counts,[int[]]$CellWidths,[int[]]$CellHeights,[int]$Width,[int]$TargetRows,[int]$Gap,[int]$Margin,[int]$CaptionHeight,[bool]$IconOnly){
 $items=New-Object 'System.Collections.Generic.List[object]';$bands=New-Object 'System.Collections.Generic.List[object]';$row=New-Object 'System.Collections.Generic.List[object]';$used=0;$band=0
 for($i=0;$i -lt $Counts.Count;$i++){
  $cols=[Math]::Max(1,[int][Math]::Ceiling($Counts[$i]/[double]$TargetRows));$cellWidth=[Math]::Max(1,$CellWidths[$i])
  $maxCols=[Math]::Max(1,[int][Math]::Floor(($Width-$Margin-$Gap)/[double]($cellWidth+$Gap)))
  $cols=[Math]::Min($cols,$maxCols);$cellWidth=[Math]::Min($cellWidth,[Math]::Max(1,[int][Math]::Floor(($Width-$Margin-($cols+1)*$Gap)/[double]$cols)))
  $groupWidth=$cols*($cellWidth+$Gap)+$Gap;$required=$groupWidth+$Margin
  if($row.Count -gt 0 -and $used+$required -gt $Width){$bands.Add($row.ToArray());$row=New-Object 'System.Collections.Generic.List[object]';$used=0;$band++}
  $item=[pscustomobject]@{Index=$i;Count=$Counts[$i];Band=$band;Width=$groupWidth;Columns=$cols;Rows=[int][Math]::Ceiling($Counts[$i]/[double]$cols);CellWidth=$cellWidth;CellHeight=$CellHeights[$i];CaptionHeight=$CaptionHeight;X=$used;Y=0;Height=0;IconOnly=$IconOnly}
  $row.Add($item);$items.Add($item);$used+=$required
 }
 if($row.Count){$bands.Add($row.ToArray())};$y=0
 foreach($members in $bands){
  $rowCount=[int](($members|Measure-Object Rows -Maximum).Maximum);$cellHeight=[int](($members|Measure-Object CellHeight -Maximum).Maximum)
  $height=$Gap+$rowCount*($cellHeight+$Gap)+$CaptionHeight+$Gap
  foreach($item in $members){$item.Y=$y;$item.Height=$height;$item.CellHeight=$cellHeight}
  $y+=$height
 }
 return [pscustomobject]@{Items=$items.ToArray();Width=$Width;Height=$y;Bands=$bands.Count;Gap=$Gap;Margin=$Margin;IconOnly=$IconOnly;Fits=$false}
}
function Get-ScoliaRibbonLayout([int[]]$Counts,[int]$Width,[int]$MaxHeight,[double]$Scale=1,[int[]]$TextWidths=@(),[int[]]$TextHeights=@()){
 if($Width -lt 8 -or $MaxHeight -lt 8 -or $Scale -le 0){throw 'Dimensions du ruban invalides.'}
 if(@($Counts|Where-Object {$_ -lt 1}).Count){throw 'Un groupe de ruban doit contenir au moins une commande.'}
 if(-not $Counts.Count){return [pscustomobject]@{Items=@();Width=$Width;Height=0;Bands=0;Gap=0;Margin=0;IconOnly=$false;Fits=$true}}
 $maximum=[int](($Counts|Measure-Object -Maximum).Maximum);$maximum=[Math]::Min(128,$maximum)
 $modes=New-Object 'System.Collections.Generic.List[object]'
 if($Width/$Scale -ge 900 -and $TextWidths.Count -eq $Counts.Count -and $TextHeights.Count -eq $Counts.Count){$modes.Add([pscustomobject]@{IconOnly=$false;Widths=$TextWidths;Heights=$TextHeights;Gap=[Math]::Max(1,[int](3*$Scale));Margin=[Math]::Max(1,[int](6*$Scale));Caption=[int](24*$Scale)})}
 foreach($size in @(46,40,36,32)){
  $physical=[Math]::Max(8,[int]($size*$Scale));$g=[Math]::Max(1,[int]($(if($size -ge 28){2}else{1})*$Scale))
  $modes.Add([pscustomobject]@{IconOnly=$true;Widths=[int[]]@($Counts|ForEach-Object {$physical});Heights=[int[]]@($Counts|ForEach-Object {$physical});Gap=$g;Margin=[Math]::Max(1,[int](3*$Scale));Caption=$(if($size -ge 18){[int](24*$Scale)}else{0})})
 }
 $fallback=$null
 foreach($mode in $modes){
  $best=$null
  for($rows=1;$rows -le $maximum;$rows++){
   $candidate=New-ScoliaRibbonCandidate $Counts $mode.Widths $mode.Heights $Width $rows $mode.Gap $mode.Margin $mode.Caption $mode.IconOnly
   # Tous les groupes restent sur une seule rangée ; les commandes peuvent
   # se répartir sur plusieurs lignes à l’intérieur de leur propre groupe.
   if($candidate.Bands -ne 1){continue}
   if(-not $best -or $candidate.Height -lt $best.Height -or ($candidate.Height -eq $best.Height -and $candidate.Bands -lt $best.Bands)){$best=$candidate}
  }
  if(-not $best){continue}
  if(-not $fallback -or $best.Height -lt $fallback.Height){$fallback=$best}
  if($best.Height -le $MaxHeight){$best.Fits=$true;return $best}
 }
 # Preserve usable hit targets: overflow scrolls horizontally instead of shrinking icons.
 $cell=[Math]::Max(32,[int](32*$Scale));$gap=[Math]::Max(2,[int](2*$Scale));$margin=[int](3*$Scale);$caption=[int](24*$Scale)
 $rows=[Math]::Max(1,[int][Math]::Floor(($MaxHeight-$caption-2*$gap)/[double]($cell+$gap)))
 $virtualWidth=0
 foreach($count in $Counts){$cols=[int][Math]::Ceiling($count/[double]$rows);$virtualWidth+=$cols*($cell+$gap)+$gap+$margin}
 $fallback=New-ScoliaRibbonCandidate $Counts ([int[]]@($Counts|ForEach-Object {$cell})) ([int[]]@($Counts|ForEach-Object {$cell})) ([Math]::Max($Width,$virtualWidth)) $rows $gap $margin $caption $true
 $fallback.Fits=($fallback.Height -le $MaxHeight)
 return $fallback
}
