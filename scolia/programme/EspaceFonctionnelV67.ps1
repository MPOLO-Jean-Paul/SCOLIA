# Une interface métier active dans la zone centrale, sans menu de cartes dupliqué.
$script:scoliaRibbonSources=New-Object 'System.Collections.Generic.HashSet[System.Windows.Forms.Control]'
$script:scoliaInlineViews=@{};$script:scoliaInlineRequest=$null;$script:scoliaInlineActive='';$script:scoliaInlineIdentity=''
function Get-ScoliaWorkspaceViewNames {return @('Recherche globale','À traiter','Présences','Réinscription','Gestion des cours','Attribution des cours','Charges des enseignants','Discipline','Enseignants','Salaires','Cotes et délibération')}
function Get-ScoliaWorkspaceViewPermission([string]$Name){if($Name -eq 'Salaires'){return 'Administrer'};return (Get-ITMCommandPermission $Name)}
function Get-ScoliaWorkspaceIdentity {
 if(-not $script:book -or -not $script:data){return ''}
 $bookPath=$(if($script:connectedWorkbookPath){[string]$script:connectedWorkbookPath}else{[string]$script:book.FullName})
 return ($bookPath+'|'+[string]$script:scolia.Id+'|'+[string]$script:scolia.Year+'|'+[string]$script:accountSession.Id+'|'+[string]$script:accountSession.Role)
}
function Reset-ScoliaInlineViews {
 foreach($form in @($script:scoliaInlineViews.Values)){if(-not $form.IsDisposed){$form.Dispose()}}
 $script:scoliaInlineViews=@{};$script:scoliaInlineActive='';$script:scoliaInlineIdentity=''
}
function Update-ScoliaActiveWorkspaceCommand {
 if(-not $script:ribbonFlow){return}
 foreach($group in $script:ribbonFlow.Controls){foreach($button in $group.Controls){
  if($button -is [ITMRibbonCommand]){
   $label=[string]$button.Text
   $selected=($script:currentPage -eq 'Scolarité' -and $label -eq $script:scoliaInlineActive) -or ($script:currentPage -eq 'Exports' -and (($label -eq 'Rapports' -and $script:exportTabs.SelectedIndex -eq 0) -or ($label -eq 'Cartes dʼexamen' -and $script:exportTabs.SelectedIndex -eq 1)))
   $button.Selected=$selected;$button.Invalidate()
  }
 }}
}
function Mount-ScoliaWorkspaceForm($Form){
 $request=$script:scoliaInlineRequest
 if(-not $request -or $request.Mounted){return $false}
 $request.Mounted=$true;$hostPanel=$script:pages['Scolarité']
 foreach($other in @($script:scoliaInlineViews.Values)){if(-not $other.IsDisposed){$other.Hide()}}
 foreach($control in @($hostPanel.Controls)){if($control -isnot [Windows.Forms.Form]){$control.Visible=$false}}
 $Form.TopLevel=$false;$Form.FormBorderStyle='None';$Form.ShowInTaskbar=$false;$Form.MinimumSize=New-Object Drawing.Size(0,0);$Form.Dock='Fill'
 $hostPanel.AutoScroll=$false;$hostPanel.AutoScrollMinSize=New-Object Drawing.Size(0,0);$hostPanel.Controls.Add($Form)
 $script:scoliaInlineViews[$request.Name]=$Form;$script:scoliaInlineActive=$request.Name
 # Seules les interfaces principales sont intégrées ; les éditeurs restent modaux.
 if($Form.Tag -and $Form.Tag.PSObject.Properties['Actions']){foreach($control in @($Form.Tag.Actions.Controls)){if($control -is [Windows.Forms.Button] -and $control.Text -eq 'Fermer'){$control.Visible=$false}}}
 $Form.Add_Disposed({param($sender,$e)foreach($key in @($script:scoliaInlineViews.Keys)){if([object]::ReferenceEquals($script:scoliaInlineViews[$key],$sender)){$script:scoliaInlineViews.Remove($key)}}})
 $Form.Show();$Form.BringToFront();$Form.PerformLayout();Update-ScoliaActiveWorkspaceCommand
 return $true
}
function Invoke-ScoliaWorkspaceView([string]$Name){
 if($script:busy){return};if($Name -notin (Get-ScoliaWorkspaceViewNames)){throw 'Interface inconnue.'}
 Assert-ITMPermission (Get-ScoliaWorkspaceViewPermission $Name)
 $identity=Get-ScoliaWorkspaceIdentity
 if(-not $identity){$script:scoliaInlineActive=$Name;return}
 if($identity -cne $script:scoliaInlineIdentity){Reset-ScoliaInlineViews;$script:scoliaInlineIdentity=$identity}
 if($script:currentPage -ne 'Scolarité'){$script:scoliaWorkspaceSelecting=$true;try{Show-Page 'Scolarité'}finally{$script:scoliaWorkspaceSelecting=$false}}
 if($script:scoliaInlineViews.ContainsKey($Name) -and -not $script:scoliaInlineViews[$Name].IsDisposed){
  foreach($form in @($script:scoliaInlineViews.Values)){$form.Visible=[object]::ReferenceEquals($form,$script:scoliaInlineViews[$Name])}
  $script:scoliaInlineViews[$Name].BringToFront();$script:scoliaInlineActive=$Name;Update-ScoliaActiveWorkspaceCommand;return
 }
 $previous=$script:scoliaInlineRequest;$script:scoliaInlineRequest=[pscustomobject]@{Name=$Name;Mounted=$false}
 try{
  switch($Name){
   'Recherche globale'{Show-ScoliaGlobalSearch}
   'À traiter'{Show-ScoliaActionCenter}
   'Présences'{Show-ScoliaPupilMonthlyAttendance}
   'Réinscription'{Show-ScoliaReenrollment}
   'Gestion des cours'{Show-ScoliaTeachingManager Courses}
   'Attribution des cours'{Show-ScoliaTeachingManager Assignments}
   'Charges des enseignants'{Show-ScoliaTeachingManager Dashboard}
   'Discipline'{Show-ScoliaDiscipline}
   'Enseignants'{Show-ScoliaTeacherAttendance}
   'Salaires'{Show-ScoliaMonthlyPayroll}
   'Cotes et délibération'{Show-ITMGrades}
  }
 }finally{$script:scoliaInlineRequest=$previous}
}
function Ensure-ScoliaDefaultWorkspaceView {
 if($script:currentPage -ne 'Scolarité' -or $script:scoliaWorkspaceSelecting -or $script:busy){return}
 $identity=Get-ScoliaWorkspaceIdentity
 if(-not $identity){Reset-ScoliaInlineViews;foreach($control in @($script:pages['Scolarité'].Controls)){if($control -isnot [Windows.Forms.Form]){$control.Visible=$true}};$script:scoliaInlineActive='Recherche globale';Update-ScoliaActiveWorkspaceCommand;return}
 if($identity -cne $script:scoliaInlineIdentity -or -not $script:scoliaInlineViews.Count){Invoke-ScoliaWorkspaceView 'Recherche globale'}
}
function Set-ScoliaExportView([int]$Index){
 if($Index -notin @(0,1)){throw 'Interface dʼexport inconnue.'}
 $script:exportTabs.SelectedIndex=$Index;$script:exportReportsPage.Visible=($Index -eq 0);$script:exportCardsPage.Visible=($Index -eq 1)
 $page=$(if($Index -eq 0){$script:exportReportsPage}else{$script:exportCardsPage});$page.BringToFront();Update-ITMResponsivePanel $page
 $script:ribbonPage='';Update-ITMRibbonState;Update-ScoliaActiveWorkspaceCommand
}

function Register-ScoliaRibbonSource($Control){
 if($Control -is [Windows.Forms.Button]){[void]$script:scoliaRibbonSources.Add($Control);$Control.Visible=$false}
}
function Test-ScoliaRibbonSource($Control){return ($script:scoliaRibbonSources -and $script:scoliaRibbonSources.Contains($Control))}
