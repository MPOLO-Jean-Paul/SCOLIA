# Politique commune des fenêtres, sans modification des données ou des Form.Tag.
$script:scoliaWindowQuality=@{}
function Get-ScoliaWindowGeometry([int]$AreaX,[int]$AreaY,[int]$AreaWidth,[int]$AreaHeight,[int]$X,[int]$Y,[int]$Width,[int]$Height,[int]$MinWidth=0,[int]$MinHeight=0,[int]$Margin=16){
 if($AreaWidth -lt 64 -or $AreaHeight -lt 64){throw 'Zone de travail de l écran invalide.'}
 $margin=[Math]::Max(0,[Math]::Min($Margin,[int]([Math]::Min($AreaWidth,$AreaHeight)/8)))
 $limitWidth=$AreaWidth-2*$margin;$limitHeight=$AreaHeight-2*$margin
 $minimumWidth=[Math]::Min($limitWidth,[Math]::Max(0,$MinWidth));$minimumHeight=[Math]::Min($limitHeight,[Math]::Max(0,$MinHeight))
 $w=[Math]::Max([Math]::Max(32,$minimumWidth),[Math]::Min($Width,$limitWidth));$h=[Math]::Max([Math]::Max(32,$minimumHeight),[Math]::Min($Height,$limitHeight))
 $left=[Math]::Max($AreaX+$margin,[Math]::Min($X,$AreaX+$AreaWidth-$margin-$w));$top=[Math]::Max($AreaY+$margin,[Math]::Min($Y,$AreaY+$AreaHeight-$margin-$h))
 return [pscustomobject]@{X=$left;Y=$top;Width=$w;Height=$h;MinWidth=$minimumWidth;MinHeight=$minimumHeight}
}
function Test-ScoliaPairedFieldTable($Table){
 if($Table.Tag -eq 'scolia:cash-layout'){return $false}
 if($Table -isnot [Windows.Forms.TableLayoutPanel] -or $Table.ColumnCount -ne 2){return $false}
 $pairs=0
 for($row=0;$row -lt $Table.RowCount;$row++){
  $controls=@($Table.Controls|Where-Object {$Table.GetRow($_) -eq $row})
  if($controls.Count -eq 0){continue}
  if($controls.Count -ne 2){return $false}
  foreach($control in $controls){if($Table.GetColumnSpan($control) -ne 1 -or $Table.GetRowSpan($control) -ne 1){return $false}}
  $labels=@($controls|Where-Object {$_ -is [Windows.Forms.Label]})
  $inputs=@($controls|Where-Object {$_ -is [Windows.Forms.TextBoxBase] -or $_ -is [Windows.Forms.ComboBox] -or $_ -is [Windows.Forms.NumericUpDown] -or $_ -is [Windows.Forms.DateTimePicker]})
  if($labels.Count -eq 1 -and $inputs.Count -eq 1){$pairs++}else{return $false}
 }
 return ($pairs -ge 1)
}
function Update-ScoliaProfessionalControls($Root,$State,[bool]$Reflow){
 if($Root.IsDisposed){return}
 if($Reflow -and $Root -is [Windows.Forms.FlowLayoutPanel]){Update-ScoliaFlowSpacing $Root}
 if($Reflow -and $script:fluidTables -and $script:fluidTables.ContainsKey($Root)){Update-ITMFluidTable $Root}
 if($Root -is [Windows.Forms.ButtonBase] -and [string]::IsNullOrWhiteSpace($Root.AccessibleName)){$Root.AccessibleName=$Root.Text}
 if($Root -is [Windows.Forms.Label] -or $Root -is [Windows.Forms.PictureBox]){$Root.TabStop=$false}
 if($Root -is [Windows.Forms.Label] -and $Root.AutoSize -and $Root.Parent -is [Windows.Forms.TableLayoutPanel] -and $Root.Parent.ColumnCount -eq 1){$usable=[Math]::Max(32,$Root.Parent.ClientSize.Width-$Root.Parent.Padding.Horizontal-$Root.Margin.Horizontal);$Root.MaximumSize=New-Object Drawing.Size($usable,0)}
 if($Root -is [Windows.Forms.TextBoxBase] -and $Root.Multiline -and -not $Root.ReadOnly){if($Root -is [Windows.Forms.TextBox]){$Root.AcceptsReturn=$true}}
 if($Root -is [Windows.Forms.ComboBox]){
  $width=$Root.Width
  for($i=0;$i -lt [Math]::Min(100,$Root.Items.Count);$i++){
   $item=$Root.Items[$i];$text=[string]$item
   if($Root.DisplayMember -and $item -and $item.PSObject.Properties[$Root.DisplayMember]){$text=[string]$item.PSObject.Properties[$Root.DisplayMember].Value}
   $measured=[Windows.Forms.TextRenderer]::MeasureText($text,$Root.Font).Width+32
   $width=[Math]::Max($width,$measured)
  }
  $Root.DropDownWidth=[Math]::Max(1,[Math]::Min($width,$State.Area.Width-32))
 }
 if($Root -is [Windows.Forms.ButtonBase] -and $Root.Text -and [string]::IsNullOrWhiteSpace($Root.AccessibleDescription)){
  if([Windows.Forms.TextRenderer]::MeasureText($Root.Text,$Root.Font).Width -gt $Root.Width-20){$State.Tips.SetToolTip($Root,$Root.Text)}
 }
 if($Root -is [Windows.Forms.DataGridView] -and (Test-Path function:Set-ITMGridReadability)){Set-ITMGridReadability $Root}
 if($Reflow -and (Test-ScoliaPairedFieldTable $Root) -and (Test-Path function:Enable-ITMFluidTable)){
  if($null -ne $script:fluidTables -and -not $script:fluidTables.ContainsKey($Root)){Enable-ITMFluidTable $Root}
 }
 foreach($child in @($Root.Controls)){Update-ScoliaProfessionalControls $child $State $Reflow}
}
function Update-ScoliaProfessionalWindow($Form){
 $state=$script:scoliaWindowQuality[$Form];if(-not $state -or $state.Busy -or $Form.IsDisposed){return}
 $state.Busy=$true;$Form.SuspendLayout()
 try{
  $owner=$(if($Form.Owner -and -not $Form.Owner.IsDisposed){$Form.Owner}elseif($script:main -and -not $script:main.IsDisposed){$script:main}else{$Form})
  $area=[Windows.Forms.Screen]::FromControl($owner).WorkingArea;$state.Area=$area
  $secondary=($Form -ne $script:main)
  if($secondary -and $Form.TopLevel -and $Form.WindowState -eq 'Normal'){
   $g=Get-ScoliaWindowGeometry $area.X $area.Y $area.Width $area.Height $Form.Left $Form.Top $Form.Width $Form.Height $Form.MinimumSize.Width $Form.MinimumSize.Height
   $Form.MinimumSize=New-Object Drawing.Size($g.MinWidth,$g.MinHeight);$Form.SetBounds($g.X,$g.Y,$g.Width,$g.Height)
  }
  $reflow=$secondary -and $Form.GetType().Name -notin @('ITMLoginForm','ITMPasswordForm')
  Update-ScoliaProfessionalControls $Form $state $reflow
  if($Form.Tag -and $Form.Tag.PSObject.Properties['Viewport'] -and (Test-Path function:Update-ScoliaFeatureDialogLayout)){Update-ScoliaFeatureDialogLayout $Form.Tag}
  if($script:adaptivePanels -and $script:adaptivePanels.ContainsKey($Form) -and $script:adaptivePanels[$Form].PSObject.Properties['Wrappers']){Update-ITMDossierLayout $Form}
  if($Form.Tag -and $Form.Tag.PSObject.Properties['BatchViewport'] -and (Test-Path function:Update-ScoliaBatchPaymentLayout)){Update-ScoliaBatchPaymentLayout $Form}
  $state.Error=''
 }catch{$state.Error=$_.Exception.Message;Write-Verbose ('Ajustement de fenêtre : '+$state.Error)}
 finally{$Form.ResumeLayout();$state.Busy=$false}
}
function Enable-ScoliaProfessionalWindow($Form){
 if($Form -isnot [Windows.Forms.Form] -or $Form.IsDisposed){return}
 if($script:scoliaWindowQuality.ContainsKey($Form)){if($Form.Visible){Update-ScoliaProfessionalWindow $Form};return}
 $tips=New-Object Windows.Forms.ToolTip;$tips.InitialDelay=400;$tips.ReshowDelay=120;$tips.AutoPopDelay=10000
 $script:scoliaWindowQuality[$Form]=[pscustomobject]@{Busy=$false;Tips=$tips;Area=[Windows.Forms.Screen]::FromControl($Form).WorkingArea;Error=''}
 if(Test-Path function:Enable-ITMDialogShortcuts){Enable-ITMDialogShortcuts $Form}
 $timer=New-Object Windows.Forms.Timer;$timer.Interval=100
 $script:scoliaWindowQuality[$Form]|Add-Member NoteProperty Timer $timer
 $timer.Tag=$Form
 $timer.Add_Tick({param($sender,$e)$sender.Stop();if(-not $sender.Tag.IsDisposed){Update-ScoliaProfessionalWindow $sender.Tag}})
 $schedule={param($sender,$e)$state=$script:scoliaWindowQuality[$sender];if($state -and -not $state.Busy){$state.Timer.Stop();$state.Timer.Start()}}
 $Form.Add_SizeChanged($schedule);$Form.Add_FontChanged($schedule)
 $Form.Add_Shown({param($sender,$e)Update-ScoliaProfessionalWindow $sender})
 $Form.Add_Disposed({param($sender,$e)$state=$script:scoliaWindowQuality[$sender];if($state){$state.Timer.Stop();$state.Timer.Dispose();$state.Tips.Dispose();$script:scoliaWindowQuality.Remove($sender)}})
 # Déjà ouverte lors d un changement de thème : seules les mesures sont actualisées.
 if($Form.Visible){Update-ScoliaProfessionalWindow $Form}
}

