function Get-ITMGridMapFile {
 $identity=$(if($script:scolia){$script:scolia.Id}else{'local'});return (Join-Path ([IO.Path]::GetDirectoryName($PSScriptRoot)) ('configuration\grilles-'+$identity+'.json'))
}
function Read-ITMGridMaps { $file=Get-ITMGridMapFile;if([IO.File]::Exists($file)){return @([IO.File]::ReadAllText($file)|ConvertFrom-Json)};return @() }
function Get-ITMGridMap([string]$promotion){return @(Read-ITMGridMaps|Where-Object {$_.Promotion -eq (Promo $promotion)})|Select-Object -First 1}
function Test-ITMGridMap($map){
 if((Promo $map.Promotion) -notmatch '^[A-Z0-9]{4}$' -or [string]::IsNullOrWhiteSpace([string]$map.Sheet)){throw 'Précisez la promotion et le nom exact de la feuille.'}
 foreach($key in @('FirstRow','LastRow','NameColumn','CourseRow','MaximumRow','LastColumn','NameHeaderRow')){if([string]$map.$key -notmatch '^[1-9][0-9]*$' -or [int]$map.$key -gt 50000){throw ('Repère invalide : '+$key)}}
 if($map.NameColumn -lt 2 -or $map.NameColumn -gt 199 -or $map.LastColumn -gt 200 -or $map.LastColumn -le $map.NameColumn+1 -or $map.FirstRow -gt $map.LastRow -or $map.MaximumRow -ge $map.FirstRow -or $map.CourseRow -ge $map.FirstRow -or $map.NameHeaderRow -ge $map.FirstRow){throw 'Les entêtes et barèmes doivent précéder les apprenants. Les colonnes numéro, identité, cours et décision doivent être distinctes.'}
}
function Get-ITMGridSheet($book,[string]$promotion){$map=Get-ITMGridMap $promotion;$name=$(if($map){$map.Sheet}else{Promo $promotion});return $book.Worksheets.Item($name)}
function Show-ITMGridMapping([string]$path,[string]$promotion){
 Assert-ITMPermission 'Administrer';if(-not [IO.File]::Exists($path) -or -not $promotion){throw 'Choisissez dʼabord une promotion et une grille XLSX.'}
 $existing=Get-ITMGridMap $promotion;$d=New-ScoliaDialog 'Paramétrer la grille de '+$promotion 760 740;$t=New-ScoliaTable $d;$controls=@{}
 $defaults=@{Sheet=$promotion;NameHeaderRow=6;FirstRow=12;LastRow=59;NameColumn=2;CourseRow=9;MaximumRow=11;LastColumn=60}
 $labels=@{Sheet='Nom exact de la feuille';NameHeaderRow='Ligne de lʼentête des noms';FirstRow='Première ligne des apprenants';LastRow='Dernière ligne disponible';NameColumn='Colonne du nom complet (numéro)';CourseRow='Ligne des intitulés de cours';MaximumRow='Ligne des barèmes';LastColumn='Colonne de la décision (numéro)'}
 foreach($key in @('Sheet','NameHeaderRow','FirstRow','LastRow','NameColumn','CourseRow','MaximumRow','LastColumn')){$value=$(if($existing){$existing.$key}else{$defaults[$key]});$controls[$key]=Add-ScoliaText $t $labels[$key] ([string]$value)}
 $text=New-Label $t 'La colonne du numéro précède celle du nom. Les cours occupent les colonnes entre le nom et la décision ; un barème positif identifie un cours, une formule identifie un total. Vérifiez lʼaperçu avant tout remplissage.' 0 0 580 95;$text.Dock='Top';$t.SetCellPosition($text,(New-Object Windows.Forms.TableLayoutPanelCellPosition(0,$t.RowCount)));$t.SetColumnSpan($text,2);$t.RowCount++
 $bar=New-Object Windows.Forms.FlowLayoutPanel;$bar.Dock='Bottom';$bar.AutoSize=$true;$bar.AutoSizeMode='GrowAndShrink';$bar.MinimumSize=New-Object Drawing.Size(0,60);$d.Controls.Add($bar)
 [void](Add-ScoliaButton $bar 'Vérifier et mémoriser' {try{
  $map=[pscustomobject]@{Promotion=(Promo $promotion)};foreach($key in $controls.Keys){$value=$controls[$key].Text.Trim();if($key -ne 'Sheet'){$value=[int]$value};$map|Add-Member -NotePropertyName $key -NotePropertyValue $value};Test-ITMGridMap $map
  $excel=$null;$book=$null;try{$excel=Open-ITMGradeExcel;$book=$excel.Workbooks.Open($path,0,$true);$sheet=$book.Worksheets.Item($map.Sheet);$layout=Get-ITMGridLayout $sheet $promotion $map;if(-not @($layout.Courses).Count){throw 'Aucun cours avec barème positif détecté.'}}finally{if($book){$book.Close($false)};if($excel){$excel.Quit();$excel.Dispose()}}
  $maps=@(Read-ITMGridMaps|Where-Object {$_.Promotion -ne $map.Promotion})+@($map);Write-ScoliaJson (Get-ITMGridMapFile) $maps;Write-ITMUserAudit 'Grille paramétrée' $promotion;$d.DialogResult='OK';$d.Close()
 }catch{$text.Text=$_.Exception.Message}})
 [void](Add-ScoliaButton $bar 'Fermer' {$d.Close()});Apply-ITMThemeForm $d;try{return ($d.ShowDialog() -eq 'OK')}finally{$d.Dispose()}
}
