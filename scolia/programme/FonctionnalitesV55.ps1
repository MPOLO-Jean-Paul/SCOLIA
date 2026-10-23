. (Join-Path $PSScriptRoot 'JsonCompatV65.ps1')
# Services communs des fonctions SCOLIA V55. Aucun accès Excel en écriture ici.
function Get-ScoliaFeatureHash([string]$value){
 $algorithm=[Security.Cryptography.SHA256]::Create()
 try{return ([BitConverter]::ToString($algorithm.ComputeHash([Text.Encoding]::UTF8.GetBytes($value)))).Replace('-','').ToLowerInvariant()}finally{$algorithm.Dispose()}
}
function Get-ScoliaFeatureContext {
 if(-not $script:data -or -not $script:book){throw 'Connectez les données scolaires avant de poursuivre.'}
 $years=@($script:data.promotions|Where-Object {$_.Active}|ForEach-Object {([string]$_.Annee).Trim()}|Select-Object -Unique)
 if($years.Count -ne 1 -or $years[0] -notmatch '^\d{4}-\d{4}$'){throw 'Une seule année scolaire active est requise. Vérifiez les promotions.'}
 $parts=$years[0].Split('-');if([int]$parts[1] -ne [int]$parts[0]+1){throw 'Année scolaire incohérente.'}
 $dataset=$(if($script:store){[string]$script:store.Id}else{[IO.Path]::GetFullPath([string]$script:book.FullName).ToUpperInvariant()})
 $school=$(if($script:scolia){[string]$script:scolia.Id}else{'local'})
 $account=$(if($script:accountSession){[string]$script:accountSession.Id+'|'+[string]$script:accountSession.Role}else{[string]$script:scoliaSession.Role})
 return [pscustomobject]@{Id=(Get-ScoliaFeatureHash ($school+'|'+$dataset));Year=$years[0];LocalKey=(Get-ScoliaFeatureHash ([Environment]::UserName+'|'+$account))}
}
function Get-ScoliaFeatureStorePath([ValidateSet('inscriptions','presences','reinscriptions','personnel','salaires','suiviapprenants','enseignements','discipline')][string]$Name,[switch]$Local){
 $context=Get-ScoliaFeatureContext
 if($Name -eq 'inscriptions' -and -not $Local){throw 'Les brouillons d inscription sont propres à chaque utilisateur.'}
 $folder=$(if($Local){Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) ('SCOLIA/fonctionnalites-v55/'+$context.LocalKey)}elseif($script:storeFolder){$script:storeFolder}else{[IO.Path]::GetDirectoryName([string]$script:book.FullName)})
 $key=Get-ScoliaFeatureHash ($context.Id+'|'+$context.Year+$(if($Local){'|'+$context.LocalKey}else{''}))
 return (Join-Path $folder ('scolia-v55-'+$Name+'-'+$key+'.json'))
}
function Assert-ScoliaFeatureDocument($Document,$Context,[switch]$Local){
 if(-not $Document -or $Document.Schema -ne 1 -or -not $Document.PSObject.Properties['Items'] -or [string]$Document.Revision -notmatch '^\d+$' -or [decimal]$Document.Revision -ge [int]::MaxValue){throw 'Fichier de fonctionnalité invalide. Le fichier existant est conservé.'}
 if($Document.Context.Id -cne $Context.Id -or $Document.Context.Year -cne $Context.Year -or ($Local -and $Document.Context.LocalKey -cne $Context.LocalKey)){throw 'Ces informations appartiennent à une autre base, année ou session.'}
}
function Read-ScoliaFeatureStore([ValidateSet('inscriptions','presences','reinscriptions','personnel','salaires','suiviapprenants','enseignements','discipline')][string]$Name,[switch]$Local){
 $context=Get-ScoliaFeatureContext;$path=Get-ScoliaFeatureStorePath -Name $Name -Local:$Local
 if(-not [IO.File]::Exists($path)){return [pscustomobject]@{Schema=1;Context=$context;Revision=0;Items=@()}}
 try{$document=[IO.File]::ReadAllText($path,[Text.Encoding]::UTF8)|ConvertFrom-ScoliaJson -ErrorAction Stop}catch{throw ('Lecture impossible : '+$path+'. Aucune donnée réinitialisée. '+$_.Exception.Message)}
 Assert-ScoliaFeatureDocument $document $context -Local:$Local
 $document.Items=@($document.Items|Where-Object {$null -ne $_});return $document
}
function Invoke-ScoliaFeatureUpdate([ValidateSet('inscriptions','presences','reinscriptions','personnel','salaires','suiviapprenants','enseignements','discipline')][string]$Name,[scriptblock]$Change,[switch]$Local){
 if(-not $Change){throw 'Modification absente.'}
 if($Name -eq 'discipline'){throw 'Utilisez le service de discipline pour contrôler les décisions et les récidives.'}
 if($Name -eq 'enseignements'){throw 'Utilisez le service de cours et attributions pour vérifier les enseignants et les doublons.'}
 if($Name -in @('personnel','salaires')){return Invoke-ScoliaStaffFeatureUpdate -Name $Name -Change $Change}
 Assert-ITMPermission $(if($Name -eq 'reinscriptions'){'Administrer'}else{'Saisie'})
 if($script:data.closed){throw 'Année clôturée : les informations sont consultables uniquement.'}
 if($Name -ne 'inscriptions' -and -not $script:store){throw 'Préparez votre classeur avec INSTALLER puis connectez le dossier data pour activer les présences et les réinscriptions.'}
 $context=Get-ScoliaFeatureContext;$path=Get-ScoliaFeatureStorePath -Name $Name -Local:$Local
 [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($path));$gate=$null
 try{
  try{$gate=[IO.File]::Open($path+'.lock',[IO.FileMode]::OpenOrCreate,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None)}catch{throw 'Un autre utilisateur enregistre ces informations. Réessayez après actualisation.'}
  $document=Read-ScoliaFeatureStore -Name $Name -Local:$Local;$featureInitialRevisionV56=[int]$document.Revision
  $beforePresence=$null;if($Name -eq 'presences' -and (Get-Command Assert-ScoliaPupilSignaturesImmutable -ErrorAction SilentlyContinue)){$beforePresence=$document|ConvertTo-Json -Depth 20|ConvertFrom-ScoliaJson}
  [void](& $Change $document)
  Assert-ScoliaFeatureDocument $document $context -Local:$Local
  if($beforePresence){Assert-ScoliaPupilSignaturesImmutable $beforePresence $document}
  $current=Get-ScoliaFeatureContext;if($current.Id -cne $context.Id -or $current.Year -cne $context.Year -or ($Local -and $current.LocalKey -cne $context.LocalKey)){throw 'Les données connectées ont changé. Enregistrement interrompu.'}
  $document.Revision=$featureInitialRevisionV56+1;$document.Items=@($document.Items|Where-Object {$null -ne $_})
  Write-ITMAtomicJson $path $document
  return $document
 }finally{if($gate){$gate.Dispose()}}
}
function Update-ScoliaFeatureDialogLayout($State) {
 if(-not $State -or $State.Form.IsDisposed -or $State.LayoutBusy){return}
 $State.LayoutBusy=$true
 try{
  $q=Get-ITMDpiScale $State.Form;$pad=[int](18*$q)
  $hostWidth=[Math]::Max(80,$State.Viewport.ClientSize.Width-[Windows.Forms.SystemInformation]::VerticalScrollBarWidth)
  $State.Canvas.Width=$hostWidth;$width=[Math]::Max(20,$hostWidth-2*$pad)
  Update-ScoliaFlowSpacing $State.Toolbar;Update-ScoliaFlowSpacing $State.Actions
  $height=Get-ITMTextHeight $State.TitleLabel $width ([int](30*$q));$State.TitleLabel.SetBounds($pad,[int](12*$q),$width,$height)
  $height=Get-ITMTextHeight $State.SubtitleLabel $width ([int](24*$q));$State.SubtitleLabel.SetBounds($pad,($State.TitleLabel.Bottom+[int](3*$q)),$width,$height)
  $State.Header.Height=$State.SubtitleLabel.Bottom+[int](10*$q)
  $State.Message.Visible=-not [string]::IsNullOrWhiteSpace($State.Message.Text)
  if($State.Message.Visible){$State.Message.Height=Get-ITMTextHeight $State.Message $hostWidth ([int](32*$q))}
  if($State.PSObject.Properties['SearchFields']){
   $State.SearchFields.Width=[Math]::Max(20,$State.Toolbar.ClientSize.Width-$State.Toolbar.Padding.Horizontal)
   $State.SearchFields.ArrangeFields()
   $State.Toolbar.PerformLayout()
   $State.Toolbar.Height=$State.Toolbar.GetPreferredSize((New-Object Drawing.Size($State.Toolbar.ClientSize.Width,0))).Height
  }
  $minimum=$State.Header.Height+$State.Toolbar.Height+$(if($State.Actions.Visible){$State.Actions.Height}else{0})+$(if($State.Message.Visible){$State.Message.Height}else{0})+[int](210*$q)
  $State.Canvas.Height=[Math]::Max($minimum,$State.Viewport.ClientSize.Height)
  $State.Viewport.AutoScrollMinSize=New-Object Drawing.Size(0,$State.Canvas.Height)
 }finally{$State.LayoutBusy=$false}
}
function New-ScoliaFeatureDialog([string]$Title,[string]$Subtitle,[int]$Width=980,[int]$Height=660){
 $form=New-ScoliaDialog $Title $Width $Height;$form.MinimumSize=New-Object Drawing.Size(320,380)
 $header=New-Object Windows.Forms.Panel;$header.Dock='Top'
 $heading=New-Label $header $Title 18 12 ($Width-60) 30 18 $true;$heading.Tag='scolia:heading'
 $note=New-Label $header $Subtitle 18 48 ($Width-60) 38 10;$note.ForeColor=[Drawing.Color]::FromArgb(91,105,122)
 $toolbar=New-Object Windows.Forms.FlowLayoutPanel;$toolbar.Dock='Top';$toolbar.Padding=New-Object Windows.Forms.Padding(18,4,18,8);$toolbar.AutoSize=$true;$toolbar.AutoSizeMode='GrowAndShrink';$toolbar.WrapContents=$true
 $message=New-Label $form '' 0 0 800 32;$message.Dock='Bottom';$message.Padding=New-Object Windows.Forms.Padding(18,6,18,6);$message.AccessibleRole='StatusBar'
 $actions=New-Object Windows.Forms.FlowLayoutPanel;$actions.Dock='Bottom';$actions.AutoSize=$true;$actions.AutoSizeMode='GrowAndShrink';$actions.WrapContents=$true;$actions.Padding=New-Object Windows.Forms.Padding(12,6,12,6);$actions.Visible=$false;$actions.Add_ControlAdded({param($sender,$e)$sender.Visible=$true})
 $body=New-Object Windows.Forms.Panel;$body.Dock='Fill';$body.Padding=New-Object Windows.Forms.Padding(18,4,18,6)
 $grid=New-Grid $body 0 0 700 400;$grid.Dock='Fill';$grid.Anchor='Top,Bottom,Left,Right'
 $form.Controls.Add($body);$form.Controls.Add($toolbar);$form.Controls.Add($header);$form.Controls.Add($message);$form.Controls.Add($actions);$body.BringToFront()
 $viewport=New-Object Windows.Forms.Panel;$viewport.Dock='Fill';$viewport.AutoScroll=$true
 $canvas=New-Object Windows.Forms.Panel;$canvas.Location=New-Object Drawing.Point(0,0)
 foreach($part in @($body,$toolbar,$header,$message,$actions)){$canvas.Controls.Add($part)}
 $body.BringToFront();$viewport.Controls.Add($canvas);$form.Controls.Add($viewport)
 $state=[pscustomobject]@{Form=$form;Viewport=$viewport;Canvas=$canvas;Toolbar=$toolbar;Actions=$actions;Grid=$grid;Message=$message;Body=$body;Header=$header;TitleLabel=$heading;SubtitleLabel=$note;LayoutBusy=$false};$form.Tag=$state
 $form.Add_SizeChanged({param($sender,$e)Update-ScoliaFeatureDialogLayout $sender.Tag})
 foreach($part in @($toolbar,$actions)){$part.Add_SizeChanged({param($sender,$e)$owner=$sender.FindForm();if($owner -and $owner.Tag -and $owner.Tag.PSObject.Properties['Viewport']){Update-ScoliaFeatureDialogLayout $owner.Tag}})}
 foreach($line in @($heading,$note,$message)){$line.Add_TextChanged({param($sender,$e)$owner=$sender.FindForm();if($owner -and $owner.Tag -and $owner.Tag.PSObject.Properties['Viewport']){Update-ScoliaFeatureDialogLayout $owner.Tag}})}
 $form.Add_Shown({param($sender,$e)Update-ScoliaFeatureDialogLayout $sender.Tag;Apply-ITMThemeForm $sender})
 Update-ScoliaFeatureDialogLayout $state
 return $state
}
function Invoke-ScoliaFeatureCommand([string]$Name){
 if($script:busy){return}
 try{
  $view=$(if($Name -eq 'Présences et absences'){'Présences'}else{$Name})
  if((Get-Command Get-ScoliaWorkspaceViewNames -ErrorAction SilentlyContinue) -and $view -in (Get-ScoliaWorkspaceViewNames)){Invoke-ScoliaWorkspaceView $view;return}
  switch($Name){
   'Recherche globale'{Show-ScoliaGlobalSearch}
   'À traiter'{Show-ScoliaActionCenter}
   'Présences et absences'{Show-ScoliaPupilMonthlyAttendance}
   'Enseignants'{Show-ScoliaTeacherAttendance}
   'Gestion des cours'{Show-ScoliaTeachingManager Courses}
   'Attribution des cours'{Show-ScoliaTeachingManager Assignments}
   'Charges des enseignants'{Show-ScoliaTeachingManager Dashboard}
   'Discipline'{Show-ScoliaDiscipline}
   'Salaires'{Show-ScoliaMonthlyPayroll}
   'Réinscription'{Show-ScoliaReenrollment}
  }
 }catch{App-Error $_}
}
function Update-ScoliaFeatureCommands {
 if(-not $script:featureMenuItems){return}
 foreach($entry in @($script:featureMenuItems.GetEnumerator())){
  $button=$entry.Value;if(-not $button -or $button.IsDisposed){continue}
  $button.Enabled=(-not $script:busy -and (Test-ITMPermission 'Lire'))
  $tip=$(if($entry.Key -eq 'Outils scolaires'){'Ouvrir le suivi scolaire, les présences, les enseignants et les salaires.'}else{'Rechercher rapidement un apprenant, un paiement ou un reçu. Raccourci : Ctrl+K.'})
  $button.AccessibleDescription=$tip
  if($script:featureCommandTips){try{$script:featureCommandTips.SetToolTip($button,$tip)}catch{}}
 }
 foreach($entry in @($script:featureToolItems.GetEnumerator())){
  $permission=$(if($entry.Key -in @('Réinscription','Salaires')){'Administrer'}else{'Lire'})
  $enabled=(-not $script:busy -and (Test-ITMPermission $permission) -and [bool]$script:data -and [bool]$script:book)
  if($entry.Key -in @('Présences et absences','Réinscription','Enseignants','Salaires','Gestion des cours','Attribution des cours','Charges des enseignants','Discipline')){
   $enabled=$enabled -and [bool]$script:store -and $(if($script:scolia){Test-ScoliaPermission 'Scolarite'}else{$true})
  }
  $entry.Value.Enabled=$enabled
  $entry.Value.ToolTipText=$(if($enabled){'Ouvrir '+$entry.Key+'.'}else{'Connectez les données scolaires préparées et vérifiez les droits du compte.'})
 }
}
function Update-ScoliaFeatureCommandLayout([int]$X,[int]$Y,[int]$Width,[double]$Scale=1,[bool]$Compact=$false) {
 if(-not $script:featureMenu -or $script:featureMenu.IsDisposed){return 0}
 $available=[Math]::Max(0,$Width)
 if($available -lt 40){$script:featureMenu.Visible=$false;return 0}
 $q=[Math]::Max(1,[Math]::Min(2.5,$Scale));$logical=$available/$q
 $dense=($Compact -or $logical -lt 720)
 $gap=[Math]::Max(2,[int]($(if($logical -lt 520){3}else{5})*$q))
 $baseHeight=[int]($(if($dense){30}else{34})*$q)
 $shortLabels=@{
  'Recherche globale'='Recherche'
  'Outils scolaires'='Outils scolaires ▾'
 }
 $names=@('Recherche globale','Outils scolaires')
 $items=@();$index=0
 foreach($name in $names){
  if(-not $script:featureMenuItems -or -not $script:featureMenuItems.ContainsKey($name)){continue}
  $button=$script:featureMenuItems[$name]
  if(-not $button -or $button.IsDisposed){continue}
  $button.Text=$(if($dense){$shortLabels[$name]}elseif($name -eq 'Outils scolaires'){'Outils scolaires ▾'}else{$name});$button.Compact=$false;$button.TabIndex=$index;$index++
  $button.AccessibleName=$name
  if(Get-Command Set-ITMAdaptiveFont -ErrorAction SilentlyContinue){Set-ITMAdaptiveFont $button $(if($dense){8.5}else{9.25}) ([Drawing.FontStyle]::Regular)}
  $measure=[Windows.Forms.TextRenderer]::MeasureText($button.Text,$button.Font)
  $reserve=[int]($(if($dense){18}else{28})*$q);$minimum=[int]($(if($dense){54}else{74})*$q)
  if($names.Count -eq 1){$minimum=[Math]::Max($minimum,[int](170*$q));$reserve=[Math]::Max($reserve,[int](34*$q))}
  $natural=[Math]::Max($minimum,$measure.Width+$reserve)
  $items+=([pscustomobject]@{Button=$button;Width=[Math]::Min($available,$natural)})
 }
 if(-not $items.Count){$script:featureMenu.Visible=$false;return 0}

 # First try natural widths. In compact mode, use balanced columns so the header
 # never grows into six tiny rows on narrow/high-DPI windows.
 $rows=@();$denseColumns=0;$denseCell=0
 if($dense){
  if($items.Count -eq 1){$denseColumns=1}else{$denseColumns=$(if($logical -lt 400){2}elseif($logical -lt 760){3}else{$items.Count})}
  $denseColumns=[Math]::Max(1,[Math]::Min($items.Count,$denseColumns))
  $denseCell=[int][Math]::Floor(($available-($denseColumns-1)*$gap)/$denseColumns)
  while($denseColumns -gt 1 -and $denseCell -lt [int](74*$q)){
   $denseColumns--; $denseCell=[int][Math]::Floor(($available-($denseColumns-1)*$gap)/$denseColumns)
  }
  for($i=0;$i -lt $items.Count;$i+=$denseColumns){$last=[Math]::Min($items.Count-1,$i+$denseColumns-1);$rows+=,@($items[$i..$last])}
 }else{
  $row=@();$used=0
  foreach($item in $items){
   $need=$(if($row.Count){$gap}else{0})+$item.Width
   if($row.Count -and $used+$need -gt $available){$rows+=,@($row);$row=@();$used=0;$need=$item.Width}
   if($row.Count){$used+=$gap};$row+=,$item;$used+=$item.Width
  }
  if($row.Count){$rows+=,@($row)}
 }

 $rowHeights=@();$height=0
 foreach($line in $rows){
  $rowHeight=$baseHeight
  foreach($item in $line){
   $targetWidth=$(if($dense){$denseCell}else{$item.Width})
   $textWidth=[Math]::Max(20,$targetWidth-[int](16*$q))
   $textHeight=[Windows.Forms.TextRenderer]::MeasureText($item.Button.Text,$item.Button.Font,(New-Object Drawing.Size($textWidth,10000)),[Windows.Forms.TextFormatFlags]::WordBreak).Height
   $rowHeight=[Math]::Max($rowHeight,$textHeight+[int](8*$q))
  }
  $rowHeights+=,$rowHeight;$height+=$rowHeight
 }
 $height+=([Math]::Max(0,$rows.Count-1)*$gap)
 $script:featureMenu.SuspendLayout()
 try{
  $script:featureMenu.SetBounds([Math]::Max(0,$X),[Math]::Max(0,$Y),$available,[Math]::Max($baseHeight,$height))
  $cy=0
  for($r=0;$r -lt $rows.Count;$r++){
   $line=$rows[$r];$rowHeight=$rowHeights[$r]
   if($dense){
    $lineWidth=$line.Count*$denseCell+[Math]::Max(0,$line.Count-1)*$gap;$cx=[Math]::Max(0,[int](($available-$lineWidth)/2))
    for($i=0;$i -lt $line.Count;$i++){
     $line[$i].Button.SetBounds($cx,$cy,[Math]::Max(1,$denseCell),$rowHeight);$cx+=$denseCell+$gap
    }
   }else{
    $lineWidth=0;foreach($item in $line){$lineWidth+=$item.Width};$lineWidth+=([Math]::Max(0,$line.Count-1)*$gap)
    $cx=[Math]::Max(0,[int](($available-$lineWidth)/2))
    foreach($item in $line){$item.Button.SetBounds($cx,$cy,$item.Width,$rowHeight);$cx+=$item.Width+$gap}
   }
   $cy+=$rowHeight+$gap
  }
  $script:featureMenu.Visible=$true;$script:featureMenu.BringToFront()
 }finally{$script:featureMenu.ResumeLayout()}
 return [Math]::Max($baseHeight,$height)
}
function Initialize-ScoliaFeatureCommands {
 if($script:featureMenu -and -not $script:featureMenu.IsDisposed){Update-ScoliaFeatureCommands;return}
 $script:featureMenu=New-Object Windows.Forms.Panel;$script:featureMenu.AccessibleName='Outils SCOLIA';$script:featureMenu.TabStop=$false;$script:featureMenu.Margin=New-Object Windows.Forms.Padding(0)
 $script:featureMenuItems=@{};$script:featureToolItems=@{};$script:featureCommandTips=New-Object Windows.Forms.ToolTip;$script:featureCommandTips.ShowAlways=$true
 $script:featureToolsMenu=New-Object Windows.Forms.ContextMenuStrip;$script:featureToolsMenu.ShowItemToolTips=$true;$script:featureToolsMenu.AccessibleName='Outils scolaires'
 foreach($name in @('À traiter','Présences et absences','Réinscription','Enseignants','Salaires','Gestion des cours','Attribution des cours','Charges des enseignants','Discipline')){
  $command=New-Object Windows.Forms.ToolStripMenuItem;$command.Text=$name;$command.Tag=$name;$command.AccessibleName=$name
  $command.Add_Click({param($sender,$e)if($sender.Enabled){Invoke-ScoliaFeatureCommand ([string]$sender.Tag)}})
  [void]$script:featureToolsMenu.Items.Add($command);$script:featureToolItems[$name]=$command
 }
 $script:featureToolsMenu.Add_Opening({param($sender,$e)Update-ScoliaFeatureCommands;if(Get-Command Apply-ITMThemeForm -ErrorAction SilentlyContinue){Apply-ITMThemeForm $sender}})
 $tabIndex=0
 foreach($name in @('Recherche globale')){
  $item=New-Object ITMWorkspaceTab;$item.Text=$name;$item.Tag=$name;$item.AccessibleName=$name;$item.TabStop=$true;$item.TabIndex=$tabIndex;$tabIndex++;$item.Cursor='Hand';$item.UseMnemonic=$false
  $item.Add_Click({param($sender,$e)
   if(-not $sender -or -not $sender.Enabled){return}
   if([string]$sender.Tag -eq 'Outils scolaires'){
    Update-ScoliaFeatureCommands
    $script:featureToolsMenu.Show($sender,(New-Object Drawing.Point(0,$sender.Height)))
   }else{Invoke-ScoliaFeatureCommand ([string]$sender.Tag)}
  })
  $script:featureMenu.Controls.Add($item);$script:featureMenuItems[$name]=$item
 }
 $owner=$(if($script:brand){$script:brand}elseif($hostPanel){$hostPanel}else{$script:main});$owner.Controls.Add($script:featureMenu);$script:featureMenu.BringToFront()
 $script:main.KeyPreview=$true
 if(-not $script:featureCommandKeyHandler){
  $script:featureCommandKeyHandler={param($sender,$e)
   if($e.Handled -or $script:busy -or -not $e.Control -or $e.Alt -or $e.KeyCode -ne [Windows.Forms.Keys]::K){return}
   $name=$(if($e.Shift){'À traiter'}else{'Recherche globale'})
   Update-ScoliaFeatureCommands
   $command=$(if($e.Shift){$script:featureToolItems[$name]}else{$script:featureMenuItems[$name]})
   if(-not $command -or $command.IsDisposed -or -not $command.Enabled){return}
   $e.Handled=$true;$e.SuppressKeyPress=$true;Invoke-ScoliaFeatureCommand $name
  }
  $script:main.Add_KeyDown($script:featureCommandKeyHandler)
 }
 $script:main.Add_FormClosed({
  if($script:featureCommandTips){try{$script:featureCommandTips.Dispose()}catch{};$script:featureCommandTips=$null}
  if($script:featureToolsMenu){try{$script:featureToolsMenu.Dispose()}catch{};$script:featureToolsMenu=$null}
  $script:featureMenuItems=@{};$script:featureToolItems=@{};$script:featureMenu=$null;$script:featureCommandKeyHandler=$null
 })
 Update-ScoliaFeatureCommands
 if(Get-Command Update-ITMNavigation -ErrorAction SilentlyContinue){Update-ITMNavigation}
}
