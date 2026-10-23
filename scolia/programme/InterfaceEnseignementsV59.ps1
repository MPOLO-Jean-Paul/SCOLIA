function Add-ScoliaTeachingAction($Parent,[string]$Caption,[scriptblock]$Action){
 $button=Add-ScoliaButton $Parent $Caption $Action
 $button|Add-Member NoteProperty TeachingAction $Action
 return $button
}
# Fenêtres pédagogiques basées sur les composants adaptatifs et le thème de SCOLIA.
function Assert-ScoliaTeachingDialogContext($State,[switch]$Write){
 Assert-ScoliaTeachingAccess -Write:$Write;$now=Get-ScoliaFeatureContext
 if($now.Id -cne $State.Context.Id -or $now.Year -cne $State.Context.Year -or $now.LocalKey -cne $State.Context.LocalKey){throw 'La base, l année ou le compte a changé. Fermez puis rouvrez cette fenêtre.'}
 if($script:busy){throw 'Attendez la fin du traitement en cours.'}
}
function Update-ScoliaTeachingDialog($State,[switch]$FilterOnly){
 Assert-ScoliaTeachingDialogContext $State
 if(-not $FilterOnly){$State.Model=Read-ScoliaTeachingState};$State.Message.Tag=$null
 $all=@(Get-ScoliaTeachingRows $State.Model.Document $State.Model.Personnel -IncludeArchived:$State.Archives.Checked)
 $promo=$(if($State.Promotion.SelectedItem -eq 'Toutes les promotions'){''}else{[string]$State.Promotion.SelectedItem})
 $query=[string]$State.Search.Text
 if($State.Mode -eq 'Dashboard'){
  $State.Rows=@(Get-ScoliaTeacherTeachingSummary $State.Model.Document $State.Model.Personnel|Where-Object {Test-ScoliaSearchText ($_.Teacher+' '+($_.Details.Course -join ' ')+' '+($_.Details.Promotion -join ' ')) $query})
  Set-Grid $State.Grid $State.Rows @(@('Key','Clé'),@('Teacher','Enseignant'),@('Courses','Cours attribués',[int]),@('Promotions','Promotions',[int]),@('Hours','Heures prévues',[decimal]),@('ActiveFrom','Actif depuis'),@('ActiveTo','Fin d activité'))
  $hours=[decimal]0;foreach($row in $State.Rows){$hours+=$row.Hours}
  $State.Message.Text=$State.Context.Year+' · '+$State.Rows.Count+' enseignant(s) affiché(s) · '+$hours+' h prévues. Les heures sont comptées pour chaque cours et chaque promotion ; ce total ne mesure pas les heures réalisées.'
 }else{
  $State.Rows=@($all|Where-Object {(-not $promo -or $_.Promotion -ceq $promo) -and (Test-ScoliaSearchText ($_.Promotion+' '+$_.Number+' '+$_.Course+' '+$_.Teacher) $query)})
  Set-Grid $State.Grid $State.Rows @(@('Key','Clé'),@('Promotion','Promotion / classe'),@('Number','N° cours',[int]),@('Course','Cours'),@('Hours','Volume horaire (h)',[decimal]),@('Maximum','Cotation maximale',[decimal]),@('Teacher','Enseignant attribué'),@('State','État'))
  $active=@($State.Rows|Where-Object {$_.State -eq 'Actif'});$unassigned=@($active|Where-Object {-not $_.AssignmentId});$hours=[decimal]0;foreach($row in $active){$hours+=$row.Hours}
  $State.Message.Text=$State.Context.Year+' · '+$active.Count+' cours actif(s) · '+$hours+' h · '+$unassigned.Count+' cours non attribué(s). Un cours possède au plus un enseignant dans sa promotion.'
 }
 $State.Grid.Columns['Clé'].Visible=$false;Set-ITMGridReadability $State.Grid
 foreach($column in @('Heures prévues','Volume horaire (h)','Cotation maximale')){if($State.Grid.Columns.Contains($column)){$State.Grid.Columns[$column].DefaultCellStyle.Format='N2';$State.Grid.Columns[$column].DefaultCellStyle.Alignment='MiddleRight'}}
 foreach($button in $State.WriteButtons){$button.Enabled=(-not $script:data.closed -and (Test-ITMPermission 'Saisie'))}
}
function Show-ScoliaTeachingManager([ValidateSet('Courses','Assignments','Dashboard')][string]$Mode='Courses',[Windows.Forms.Form]$Owner=$script:main,[switch]$LayoutOnly){
 Assert-ScoliaTeachingAccess;$model=Read-ScoliaTeachingState
 $title=switch($Mode){'Courses'{'Gestion des cours'};'Assignments'{'Attribution des cours'};default{'Charge pédagogique des enseignants'}}
 $subtitle=switch($Mode){'Courses'{'Numéro, volume horaire et cotation maximale pour chaque promotion.'};'Assignments'{'Un enseignant par cours et par classe ; plusieurs cours et promotions possibles pour chaque enseignant.'};default{'Détail des cours attribués et total des heures prévues pour l année scolaire.'}}
 $state=New-ScoliaFeatureDialog -Title $title -Subtitle $subtitle -Width 1120 -Height 730
 foreach($entry in (@{Context=$model.Context;Model=$model;Mode=$Mode;Rows=@();WriteButtons=@()}).GetEnumerator()){$state|Add-Member NoteProperty $entry.Key $entry.Value}
 $filterTimer=New-Object Windows.Forms.Timer;$filterTimer.Interval=45
 $state|Add-Member NoteProperty FilterTimer $filterTimer
 $filterTimer.Add_Tick(({param($sender,$e)$sender.Stop();try{Update-ScoliaTeachingFilteredView $state}catch{Set-ScoliaTeachingError $state $_}}).GetNewClosure())
 $search=New-Object Windows.Forms.TextBox;$search.BorderStyle='None';$search.Font=New-Object Drawing.Font('Segoe UI',11)
 $promo=New-Object Windows.Forms.ComboBox;$promo.DropDownStyle='DropDownList';$promo.Items.AddRange([object[]](@('Toutes les promotions')+$model.Promotions));$promo.SelectedIndex=0
 $fields=New-Object ITMSearchFields($search,$promo);$fields.SearchLabel.Text=$(if($Mode -eq 'Dashboard'){'Enseignant, cours ou promotion'}else{'Cours, numéro ou enseignant'});$fields.KindLabel.Text='Promotion / classe'
 $state.Toolbar.AutoSize=$false;$state.Toolbar.WrapContents=$false;$state.Toolbar.Padding=New-Object Windows.Forms.Padding(18,2,18,10);$state.Toolbar.Controls.Add($fields)
 $state|Add-Member NoteProperty SearchFields $fields;$state|Add-Member NoteProperty Search $search;$state|Add-Member NoteProperty Promotion $promo
 $archives=New-Object ITMReadableCheckBox;$archives.Text='Afficher les cours archivés';$archives.AutoSize=$true;$state.Actions.Controls.Add($archives);$state|Add-Member NoteProperty Archives $archives
 if($Mode -eq 'Dashboard'){$archives.Visible=$false;$promo.Enabled=$false}
 if($Mode -eq 'Courses'){
  $new=Add-ScoliaTeachingAction $state.Actions 'Ajouter un cours' {param($sender,$e)$s=$sender.FindForm().Tag;try{Show-ScoliaCourseEditor $s;Update-ScoliaTeachingDialog $s}catch{Set-ScoliaTeachingError $s $_}};$new.Primary=$true
  $edit=Add-ScoliaTeachingAction $state.Actions 'Modifier' {param($sender,$e)$s=$sender.FindForm().Tag;try{$row=Get-ScoliaFeatureSelectedRow $s;Show-ScoliaCourseEditor $s $row.Key;Update-ScoliaTeachingDialog $s}catch{Set-ScoliaTeachingError $s $_}}
  $archive=Add-ScoliaTeachingAction $state.Actions 'Archiver / réactiver' {param($sender,$e)$s=$sender.FindForm().Tag;try{Set-ScoliaCourseArchiveFromDialog $s;Update-ScoliaTeachingDialog $s}catch{Set-ScoliaTeachingError $s $_}}
  $state.WriteButtons=@($new,$edit,$archive)
 }
 if($Mode -ne 'Dashboard'){
  $assign=Add-ScoliaTeachingAction $state.Actions 'Attribuer / réattribuer' {param($sender,$e)$s=$sender.FindForm().Tag;try{Show-ScoliaAssignmentEditor $s;Update-ScoliaTeachingDialog $s}catch{Set-ScoliaTeachingError $s $_}}
  $release=Add-ScoliaTeachingAction $state.Actions 'Retirer l attribution' {param($sender,$e)$s=$sender.FindForm().Tag;try{Remove-ScoliaAssignmentFromDialog $s;Update-ScoliaTeachingDialog $s}catch{Set-ScoliaTeachingError $s $_}}
  $state.WriteButtons+=@($assign,$release)
  [void](Add-ScoliaTeachingAction $state.Actions 'Historique du cours' {param($sender,$e)$s=$sender.FindForm().Tag;try{Show-ScoliaTeachingHistory $s}catch{Set-ScoliaTeachingError $s $_}})
  [void](Add-ScoliaTeachingAction $state.Actions 'Charges des enseignants' {param($sender,$e)try{Show-ScoliaTeachingManager Dashboard $sender.FindForm()}catch{Set-ScoliaTeachingError $sender.FindForm().Tag $_}})
 }else{
  [void](Add-ScoliaTeachingAction $state.Actions 'Détail de l enseignant' {param($sender,$e)$s=$sender.FindForm().Tag;try{Show-ScoliaTeacherTeachingDetails $s}catch{Set-ScoliaTeachingError $s $_}})
  $manage=Add-ScoliaTeachingAction $state.Actions 'Gérer les enseignants' {param($sender,$e)$s=$sender.FindForm().Tag;try{Assert-ScoliaTeachingDialogContext $s;Show-ScoliaTeacherManager $s.Form;Update-ScoliaTeachingDialog $s}catch{Set-ScoliaTeachingError $s $_}};$manage.Enabled=Test-ITMPermission 'Administrer'
 }
 [void](Add-ScoliaTeachingAction $state.Actions 'Actualiser' {param($sender,$e)$s=$sender.FindForm().Tag;try{Update-ScoliaTeachingDialog $s}catch{Set-ScoliaTeachingError $s $_}})
 $export=Add-ScoliaTeachingAction $state.Actions 'Exporter CSV' {param($sender,$e)$s=$sender.FindForm().Tag;try{Export-ScoliaTeachingDialog $s}catch{Set-ScoliaTeachingError $s $_}};$export.Enabled=Test-ITMPermission 'Exporter'
 $close=Add-ScoliaTeachingAction $state.Actions 'Fermer' {param($sender,$e)$sender.FindForm().Close()};$state.Form.CancelButton=$close
 $state.Form.Add_FormClosing({param($sender,$e)if($script:busy){$e.Cancel=$true}else{try{$sender.Tag.FilterTimer.Stop()}catch{}}})
 $state.Form.Add_Disposed({param($sender,$e)try{$sender.Tag.FilterTimer.Dispose()}catch{}})
 $search.Add_TextChanged({param($sender,$e)$s=$sender.FindForm().Tag;$s.FilterTimer.Stop();$s.FilterTimer.Start()})
 $promo.Add_SelectedIndexChanged({param($sender,$e)$s=$sender.FindForm().Tag;try{Update-ScoliaTeachingFilteredView $s}catch{Set-ScoliaTeachingError $s $_}})
 $archives.Add_CheckedChanged({param($sender,$e)$s=$sender.FindForm().Tag;try{Update-ScoliaTeachingFilteredView $s}catch{Set-ScoliaTeachingError $s $_}})
 $state.Grid.Add_CellDoubleClick({param($sender,$e)if($e.RowIndex -lt 0){return};$s=$sender.FindForm().Tag;try{if($s.Mode -eq 'Dashboard'){Show-ScoliaTeacherTeachingDetails $s}else{Show-ScoliaAssignmentEditor $s;Update-ScoliaTeachingDialog $s}}catch{Set-ScoliaTeachingError $s $_}})
 # Keep frequent actions visible; secondary commands remain reachable on small screens.
 $more=Add-ScoliaButton $state.Actions 'Actions' {param($sender,$e)$s=$sender.FindForm().Tag;$s.MoreMenu.Show($sender,(New-Object Drawing.Point(0,$sender.Height)))}
 $menu=New-Object Windows.Forms.ContextMenuStrip;$menu.ShowItemToolTips=$true;$state|Add-Member NoteProperty MoreMenu $menu
 $visible=@('Ajouter un cours','Attribuer / réattribuer','Détail de l enseignant','Actualiser','Fermer')
 foreach($button in @($state.Actions.Controls)){
  if($button -isnot [Windows.Forms.Button] -or $button -eq $more){continue}
  if($button.Text -notin $visible){
   $item=New-Object Windows.Forms.ToolStripMenuItem;$item.Text=$button.Text;$item.Tag=[pscustomobject]@{Button=$button;Action=$button.TeachingAction}
   $item.Add_Click({param($sender,$e)$command=$sender.Tag;if($command.Button.Enabled){& $command.Action $command.Button $e}})
   [void]$menu.Items.Add($item);$button.Visible=$false
  }else{
   switch($button.Text){'Ajouter un cours'{$button.Text='Ajouter'};'Attribuer / réattribuer'{$button.Text='Attribuer'};'Détail de l enseignant'{$button.Text='Détail'}}
   $button.AutoSize=$false;$button.Width=104;$button.Height=38
  }
 }
 $more.AutoSize=$false;$more.Width=96;$more.Height=38
 $menu.Add_Opening({param($sender,$e)foreach($item in $sender.Items){$item.Enabled=$item.Tag.Button.Enabled};if(Test-Path function:Apply-ITMThemeForm){Apply-ITMThemeForm $sender}})
 Update-ScoliaFeatureDialogLayout $state;Update-ScoliaTeachingDialog $state
 if($LayoutOnly){return $state}
 if((Test-Path function:Mount-ScoliaWorkspaceForm) -and (Mount-ScoliaWorkspaceForm $state.Form)){$state.Form.Add_Disposed({$menu.Dispose()}.GetNewClosure());return}
 try{[void]$state.Form.ShowDialog($Owner)}finally{$menu.Dispose();$state.Form.Dispose()}
}
function Set-ScoliaTeachingError($State,$ErrorRecord){$State.Message.Tag='scolia:feedback:error';$State.Message.Text=$ErrorRecord.Exception.Message}
function Update-ScoliaTeachingFilteredView($State){
 # Reuse the loaded model while typing; only Actualiser performs disk reads.
 if(-not $State -or -not $State.Model){return}
 Update-ScoliaTeachingDialog $State -FilterOnly
}
function Show-ScoliaCourseEditor($Manager,[string]$CourseId='',[switch]$LayoutOnly){
 Assert-ScoliaTeachingDialogContext $Manager -Write
 $course=$null;if($CourseId){$found=@($Manager.Model.Document.Items|Where-Object {$_.Kind -eq 'Course' -and $_.Id -ceq $CourseId});if($found.Count -ne 1){throw 'Cours absent. Actualisez.'};$course=$found[0]}
 $dialog=New-ScoliaDialog $(if($course){'Modifier le cours'}else{'Ajouter un cours'}) 640 560;$table=New-ScoliaTable $dialog
 $promo=New-Object Windows.Forms.ComboBox;$promo.DropDownStyle='DropDownList';$promo.Items.AddRange([object[]]$Manager.Model.Promotions);[void](Add-ScoliaField $table 'Promotion / classe' $promo)
 if($course){$promo.Items.Clear();[void]$promo.Items.Add($course.Promotion);$promo.SelectedIndex=0;$promo.Enabled=$false}elseif($Manager.Promotion.SelectedItem -ne 'Toutes les promotions'){$promo.SelectedItem=$Manager.Promotion.SelectedItem}
 $number=New-Object Windows.Forms.NumericUpDown;$number.Minimum=1;$number.Maximum=9999;$number.Value=$(if($course){$course.Number}else{1});[void](Add-ScoliaField $table 'Numéro du cours dans la classe' $number)
 $name=Add-ScoliaText $table 'Nom du cours' $(if($course){$course.Name}else{''});$name.MaxLength=160
 $hours=New-Object Windows.Forms.NumericUpDown;$hours.Minimum=.01;$hours.Maximum=10000;$hours.DecimalPlaces=2;$hours.Increment=.5;$hours.Value=$(if($course){$course.Hours}else{30});[void](Add-ScoliaField $table 'Volume horaire annuel (heures)' $hours)
 $maximum=New-Object Windows.Forms.NumericUpDown;$maximum.Minimum=.01;$maximum.Maximum=10000;$maximum.DecimalPlaces=2;$maximum.Value=$(if($course){$course.Maximum}else{20});[void](Add-ScoliaField $table 'Cotation maximale (points)' $maximum)
 $error=New-Object ITMReadableLabel;$error.AutoSize=$true;$error.Tag='scolia:feedback:error';[void](Add-ScoliaField $table '' $error)
 $save=New-Button $table 'Enregistrer le cours' 0 0 220 $true;[void](Add-ScoliaField $table '' $save)
 $dialog.Tag=[pscustomobject]@{Manager=$Manager;Course=$course;Promotion=$promo;Number=$number;Name=$name;Hours=$hours;Maximum=$maximum;Error=$error;Save=$save;Revision=[int]$Manager.Model.Document.Revision}
 $name.Add_TextChanged({param($sender,$e)Update-ScoliaCourseEditorSubmit $sender.FindForm()})
 $promo.Add_SelectedIndexChanged({param($sender,$e)$form=$sender.FindForm();$s=$form.Tag;if(-not $s.Course -and $sender.SelectedIndex -ge 0){$s.Number.Value=Get-ScoliaNextCourseNumber $s.Manager.Model.Document ([string]$sender.SelectedItem)};Update-ScoliaCourseEditorSubmit $form})
 if(-not $course){if($promo.Items.Count -eq 1 -and $promo.SelectedIndex -lt 0){$promo.SelectedIndex=0};if($promo.SelectedIndex -ge 0){$number.Value=Get-ScoliaNextCourseNumber $Manager.Model.Document ([string]$promo.SelectedItem)}}
 Update-ScoliaCourseEditorSubmit $dialog
 $save.Add_Click({param($sender,$e)$s=$sender.FindForm().Tag;try{
  Assert-ScoliaTeachingDialogContext $s.Manager -Write
  if($s.Promotion.SelectedIndex -lt 0){throw 'Choisissez une promotion.'}
  $params=@{Promotion=[string]$s.Promotion.SelectedItem;Number=[int]$s.Number.Value;Name=$s.Name.Text;Hours=[decimal]$s.Hours.Value;Maximum=[decimal]$s.Maximum.Value}
  if($s.Course){$params.Id=$s.Course.Id;$params.Active=$s.Course.Active}
  $course=New-ScoliaCourse @params;$actor=Get-ScoliaFeatureActor
  [void](Invoke-ScoliaTeachingUpdate -ExpectedRevision $s.Revision -Change {param($document,$personnel,$promotions)Set-ScoliaCourse $document $personnel $course $s.Revision $promotions $actor})
  $sender.FindForm().DialogResult='OK';$sender.FindForm().Close()
 }catch{$s.Error.Text=$_.Exception.Message}})
 if($LayoutOnly){return $dialog}
 try{Apply-ITMThemeForm $dialog;[void]$dialog.ShowDialog($Manager.Form)}finally{$dialog.Dispose()}
}
function Show-ScoliaAssignmentEditor($Manager,[switch]$LayoutOnly){
 Assert-ScoliaTeachingDialogContext $Manager -Write;$row=Get-ScoliaFeatureSelectedRow $Manager
 if($row.State -ne 'Actif'){throw 'Réactivez ce cours avant de l attribuer.'}
 $dialog=New-ScoliaDialog 'Attribution du cours' 680 430;$table=New-ScoliaTable $dialog
 $courseLabel=New-Object ITMReadableLabel;$courseLabel.AutoSize=$true;$courseLabel.Text=$row.Promotion+' · N° '+$row.Number+' · '+$row.Course+' · '+$row.Hours+' h';[void](Add-ScoliaField $table 'Cours sélectionné' $courseLabel)
 $teacher=New-Object Windows.Forms.ComboBox;$teacher.DropDownStyle='DropDownList';$teacher.DisplayMember='Nom';$teacher.ValueMember='Id'
 $bounds=Get-ScoliaAttendanceYearBounds $Manager.Context.Year
 $teachers=@($Manager.Model.Personnel.Items|Where-Object {$_.Kind -eq 'Teacher' -and (ConvertTo-ScoliaPersonnelDate $_.ActiveFrom) -le $bounds.End -and (-not $_.ActiveTo -or (ConvertTo-ScoliaPersonnelDate $_.ActiveTo) -ge $bounds.Start)}|Sort-Object Nom,Id)
 $teacher.Items.AddRange([object[]]$teachers);[void](Add-ScoliaField $table 'Enseignant' $teacher)
 foreach($t in $teachers){if($t.Id -ceq $row.TeacherId){$teacher.SelectedItem=$t;break}}
 $notice=New-Object ITMReadableLabel;$notice.AutoSize=$true;$notice.Text='La réattribution remplace l enseignant de ce cours et conserve l historique. Elle ne modifie aucun salaire ni aucune signature.';[void](Add-ScoliaField $table 'Règle' $notice)
 $error=New-Object ITMReadableLabel;$error.AutoSize=$true;$error.Tag='scolia:feedback:error';[void](Add-ScoliaField $table '' $error)
 $save=New-Button $table 'Enregistrer l attribution' 0 0 240 $true;[void](Add-ScoliaField $table '' $save)
 $dialog.Tag=[pscustomobject]@{Manager=$Manager;Row=$row;Teacher=$teacher;Error=$error;Save=$save;Revision=[int]$Manager.Model.Document.Revision}
 $save.Enabled=$teacher.SelectedIndex -ge 0;$teacher.Add_SelectedIndexChanged({param($sender,$e)$sender.FindForm().Tag.Save.Enabled=$sender.SelectedIndex -ge 0});if(-not $teachers.Count){$error.Text='Ajoutez d abord un enseignant actif pour cette année dans Gestion des enseignants.'}
 $save.Add_Click({param($sender,$e)$s=$sender.FindForm().Tag;try{
  Assert-ScoliaTeachingDialogContext $s.Manager -Write
  if($s.Teacher.SelectedIndex -lt 0){throw 'Sélectionnez un enseignant.'};$teacherId=[string]$s.Teacher.SelectedItem.Id
  if($s.Row.AssignmentId -and $s.Row.TeacherId -cne $teacherId){if([ITMMessageBox]::Show($sender.FindForm(),'Remplacer '+$s.Row.Teacher+' par '+$s.Teacher.SelectedItem.Nom+' pour ce cours ?','Réattribuer','YesNo','Question',[Windows.Forms.MessageBoxDefaultButton]::Button2) -ne 'Yes'){return}}
  $actor=Get-ScoliaFeatureActor
  [void](Invoke-ScoliaTeachingUpdate -ExpectedRevision $s.Revision -Change {param($document,$personnel,$promotions)Set-ScoliaCourseAssignment -Document $document -Personnel $personnel -CourseId $s.Row.Key -TeacherId $teacherId -ExpectedRevision $s.Revision -Actor $actor -Id $s.Row.AssignmentId})
  $sender.FindForm().DialogResult='OK';$sender.FindForm().Close()
 }catch{$s.Error.Text=$_.Exception.Message}})
 if($LayoutOnly){return $dialog}
 try{Apply-ITMThemeForm $dialog;[void]$dialog.ShowDialog($Manager.Form)}finally{$dialog.Dispose()}
}
function Set-ScoliaCourseArchiveFromDialog($State){
 Assert-ScoliaTeachingDialogContext $State -Write;$row=Get-ScoliaFeatureSelectedRow $State
 $course=@($State.Model.Document.Items|Where-Object {$_.Kind -eq 'Course' -and $_.Id -ceq $row.Key})[0]
 if($row.AssignmentId -and $course.Active){throw 'Retirez d abord l attribution de ce cours, puis archivez-le.'}
 $action=$(if($course.Active){'Archiver'}else{'Réactiver'})
 if([ITMMessageBox]::Show($State.Form,$action+' le cours '+$course.Name+' de '+$course.Promotion+' ?',$action,'YesNo','Question',[Windows.Forms.MessageBoxDefaultButton]::Button2) -ne 'Yes'){return}
 $copy=New-ScoliaCourse -Promotion $course.Promotion -Number $course.Number -Name $course.Name -Hours $course.Hours -Maximum $course.Maximum -Id $course.Id -Active:(-not $course.Active)
 $revision=[int]$State.Model.Document.Revision;$actor=Get-ScoliaFeatureActor
 [void](Invoke-ScoliaTeachingUpdate -ExpectedRevision $revision -Change {param($document,$personnel,$promotions)Set-ScoliaCourse $document $personnel $copy $revision $promotions $actor})
}
function Remove-ScoliaAssignmentFromDialog($State){
 Assert-ScoliaTeachingDialogContext $State -Write;$row=Get-ScoliaFeatureSelectedRow $State
 if(-not $row.AssignmentId){throw 'Ce cours ne possède aucune attribution active.'}
 if([ITMMessageBox]::Show($State.Form,'Retirer l attribution de '+$row.Course+' à '+$row.Teacher+' ? L historique sera conservé.','Retirer l attribution','YesNo','Question',[Windows.Forms.MessageBoxDefaultButton]::Button2) -ne 'Yes'){return}
 $revision=[int]$State.Model.Document.Revision;$actor=Get-ScoliaFeatureActor
 [void](Invoke-ScoliaTeachingUpdate -ExpectedRevision $revision -Change {param($document,$personnel,$promotions)Set-ScoliaCourseAssignment -Document $document -Personnel $personnel -CourseId $row.Key -TeacherId $row.TeacherId -ExpectedRevision $revision -Actor $actor -Id $row.AssignmentId -Active $false})
}
function Show-ScoliaTeacherTeachingDetails($State){
 Assert-ScoliaTeachingDialogContext $State;$row=Get-ScoliaFeatureSelectedRow $State
 $detail=New-ScoliaFeatureDialog -Title $row.Teacher -Subtitle ($State.Context.Year+' · '+$row.Courses+' cours · '+$row.Promotions+' promotion(s) · '+$row.Hours+' heures prévues') -Width 950 -Height 620
 Set-Grid $detail.Grid $row.Details @(@('Promotion','Promotion / classe'),@('Number','N° cours',[int]),@('Course','Cours'),@('Hours','Volume horaire (h)',[decimal]),@('Maximum','Cotation maximale',[decimal]))
 $detail.Message.Text='Charge pédagogique annuelle prévue : '+$row.Hours+' h. Le même cours dans deux promotions compte deux volumes horaires. Les cours non attribués ou archivés sont exclus.'
 $close=Add-ScoliaButton $detail.Actions 'Fermer' {param($sender,$e)$sender.FindForm().Close()};$detail.Form.CancelButton=$close
 try{[void]$detail.Form.ShowDialog($State.Form)}finally{$detail.Form.Dispose()}
}
function Show-ScoliaTeachingHistory($State){
 Assert-ScoliaTeachingDialogContext $State;$row=Get-ScoliaFeatureSelectedRow $State;$lines=New-Object 'System.Collections.Generic.List[string]'
 foreach($item in @($State.Model.Document.Items|Where-Object {$_.Id -ceq $row.Key -or ($_.Kind -eq 'Assignment' -and $_.CourseId -ceq $row.Key)})){
  foreach($event in @($item.Audit)){
   $date=([datetimeoffset]::Parse($event.At)).ToLocalTime().ToString('dd/MM/yyyy HH:mm')
   $kind=$(if($item.Kind -eq 'Course'){'Cours'}else{'Attribution'})
   $lines.Add($date+' · '+$event.By+' · '+$kind)
   $lines.Add('Avant : '+(ConvertTo-ScoliaTeachingAuditText $event.Before $item.Kind $State.Model.Personnel))
   $lines.Add('Après : '+(ConvertTo-ScoliaTeachingAuditText $event.After $item.Kind $State.Model.Personnel));$lines.Add('')
  }
 }
 Show-TextDetail ('Historique · '+$row.Promotion+' · '+$row.Course) ($lines.ToArray() -join "`r`n")
}
function Export-ScoliaTeachingDialog($State){
 Assert-ScoliaTeachingDialogContext $State;Assert-ITMPermission 'Exporter'
 if(-not $State.Rows.Count){throw 'Aucune ligne à exporter.'}
 $picker=New-Object Windows.Forms.SaveFileDialog;$picker.Filter='Fichier CSV (*.csv)|*.csv';$picker.FileName='SCOLIA-'+$State.Mode+'-'+$State.Context.Year+'.csv';$picker.OverwritePrompt=$true
 try{if($picker.ShowDialog($State.Form) -ne 'OK'){return}
  if($State.Mode -eq 'Dashboard'){$State.Rows|Select-Object Teacher,Courses,Promotions,Hours,ActiveFrom,ActiveTo|Export-Csv -LiteralPath $picker.FileName -Delimiter ';' -NoTypeInformation -Encoding UTF8}
  else{$State.Rows|Select-Object Promotion,Number,Course,Hours,Maximum,Teacher,State|Export-Csv -LiteralPath $picker.FileName -Delimiter ';' -NoTypeInformation -Encoding UTF8}
  $State.Message.Text='Export enregistré : '+$picker.FileName
 }finally{$picker.Dispose()}
}

function ConvertTo-ScoliaTeachingAuditText($Snapshot,[string]$Kind,$Personnel){
 if(-not $Snapshot -or @($Snapshot.PSObject.Properties).Count -eq 0){return 'Aucun enregistrement précédent'}
 $state=$(if($Snapshot.Active){'Actif'}else{'Archivé / retiré'})
 if($Kind -eq 'Course'){return ($Snapshot.Promotion+' · N° '+$Snapshot.Number+' · '+$Snapshot.Name+' · '+$Snapshot.Hours+' h · '+$Snapshot.Maximum+' points · '+$state)}
 $teacher=@($Personnel.Items|Where-Object {$_.Kind -eq 'Teacher' -and $_.Id -ceq $Snapshot.TeacherId})
 $name=$(if($teacher.Count){$teacher[0].Nom}else{'Enseignant non disponible'})
 return ($name+' · '+$state)
}

function Update-ScoliaCourseEditorSubmit($Form){
 if(-not $Form -or -not $Form.Tag){return};$s=$Form.Tag
 $s.Save.Enabled=($s.Promotion.SelectedIndex -ge 0 -and -not [string]::IsNullOrWhiteSpace($s.Name.Text))
}
