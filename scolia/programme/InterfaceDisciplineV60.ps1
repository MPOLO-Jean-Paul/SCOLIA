function Assert-ScoliaDisciplineDialog($State,[switch]$Write){
 Assert-ScoliaDisciplineAccess -Write:$Write;$context=Get-ScoliaFeatureContext
 if($context.Id -cne $State.Context.Id -or $context.Year -cne $State.Context.Year -or $context.LocalKey -cne $State.Context.LocalKey){throw 'Base, année ou compte modifié : rouvrez Discipline.'}
 if($Write -and [string]$State.Year.SelectedItem -cne $context.Year){throw 'Les archives des autres années sont consultables uniquement.'}
 if($script:busy){throw 'Attendez la fin du traitement en cours.'}
}
function Set-ScoliaDisciplineError($State,$ErrorRecord){$State.Message.Tag='scolia:feedback:error';$State.Message.Text=$ErrorRecord.Exception.Message}
function Update-ScoliaDisciplineView($State,[switch]$Reload){
 Assert-ScoliaDisciplineDialog $State
 if($Reload){
  $State.Model=Read-ScoliaDisciplineState;$State.Documents=@(Get-ScoliaDisciplineDocuments)
  $years=@($State.Context.Year)+@($State.Documents.Context.Year|Sort-Object -Descending -Unique)
  $selected=[string]$State.Year.SelectedItem;$State.Loading=$true
  try{$State.Year.Items.Clear();$State.Year.Items.AddRange([object[]]@($years|Select-Object -Unique));$State.Year.SelectedItem=$(if($State.Year.Items.Contains($selected)){$selected}else{$State.Context.Year})}finally{$State.Loading=$false}
  [void](Sync-ScoliaDisciplineSnapshot)
 }
 $document=$(if([string]$State.Year.SelectedItem -ceq $State.Context.Year){$State.Model.Document}else{@($State.Documents|Where-Object {$_.Context.Year -ceq [string]$State.Year.SelectedItem})[0]})
 $State.SelectedDocument=$document;$query=$State.Search.Text;$kind=[string]$State.Kind.SelectedItem
 $State.Rows=@(Get-ScoliaDisciplineRows $document -IncludeCancelled:$State.Cancelled.Checked|Where-Object {($kind -eq 'Toutes les mesures' -or $_.Type -ceq $kind) -and (Test-ScoliaSearchText ($_.Identity+' '+$_.Matricule+' '+$_.Promotion+' '+$_.Reason+' '+$_.State) $query)})
 Set-Grid $State.Grid $State.Rows @(@('Key','Clé'),@('Matricule','Matricule'),@('Identity','Apprenant'),@('Promotion','Promotion'),@('DecisionDate','Décision du',[datetime]),@('Type','Mesure'),@('Reason','Motif / cause'),@('Period','Période / rendez-vous'),@('Days','Durée (jours)',[int]),@('ReturnDate','Retour autorisé dès'),@('State','Situation'))
 $State.Grid.Columns['Clé'].Visible=$false;$State.Grid.Columns['Décision du'].DefaultCellStyle.Format='dd/MM/yyyy';Set-ITMGridReadability $State.Grid
 $active=@($State.Rows|Where-Object {$_.Record.State -eq 'Validated'});$pupils=@($active.Matricule|Sort-Object -Unique).Count
 $definitive=@($active|Where-Object {$_.Record.Type -eq 'Permanent'}).Count;$parents=@($active|Where-Object {$_.Record.Type -eq 'Parents'}).Count
 $State.Message.Tag=$null;$State.Message.Text=[string]$State.Year.SelectedItem+' · '+$State.Rows.Count+' décision(s) · '+$pupils+' apprenant(s).'+[Environment]::NewLine+$definitive+' renvoi(s) définitif(s) · '+$parents+' convocation(s).'
 $write=(-not $script:data.closed -and (Test-ITMPermission 'Saisie') -and [string]$State.Year.SelectedItem -ceq $State.Context.Year)
 $State.NewButton.Enabled=$write;$State.ReceivedButton.Enabled=$State.AbsentButton.Enabled=$write;$State.CancelButton.Enabled=$write -and (Test-ITMPermission 'Administrer')
}
function Show-ScoliaDiscipline([switch]$LayoutOnly){
 Assert-ScoliaDisciplineAccess;$model=Read-ScoliaDisciplineState
 $state=New-ScoliaFeatureDialog -Title 'Discipline des apprenants' -Subtitle 'Sanctions, convocations des parents et suivi des renvois temporaires. Chaque décision conserve son motif et son auteur.' -Width 1180 -Height 760
 foreach($entry in (@{Context=$model.Context;Model=$model;Documents=@();Rows=@();Loading=$true;SelectedDocument=$model.Document}).GetEnumerator()){$state|Add-Member NoteProperty $entry.Key $entry.Value}
 $search=New-Object Windows.Forms.TextBox;$search.BorderStyle='None';$search.Font=New-Object Drawing.Font('Segoe UI',11)
 $kind=New-Object Windows.Forms.ComboBox;$kind.DropDownStyle='DropDownList';$kind.Items.AddRange([object[]]@('Toutes les mesures','Renvoi temporaire','Renvoi définitif','Convocation des parents'));$kind.SelectedIndex=0
 $fields=New-Object ITMSearchFields($search,$kind);$fields.SearchLabel.Text='Nom, matricule, promotion ou motif';$fields.KindLabel.Text='Mesure disciplinaire'
 $state.Toolbar.AutoSize=$false;$state.Toolbar.WrapContents=$false;$state.Toolbar.Padding=New-Object Windows.Forms.Padding(18,2,18,10);$state.Toolbar.Controls.Add($fields)
 $state|Add-Member NoteProperty SearchFields $fields;$state|Add-Member NoteProperty Search $search;$state|Add-Member NoteProperty Kind $kind
 $year=New-Object Windows.Forms.ComboBox;$year.DropDownStyle='DropDownList';$year.Width=135;$year.AccessibleName='Année du registre disciplinaire';[void]$year.Items.Add($model.Context.Year);$year.SelectedIndex=0;$state.Actions.Controls.Add($year);$state|Add-Member NoteProperty Year $year
 $cancelled=New-Object ITMReadableCheckBox;$cancelled.Text='Afficher les annulations';$cancelled.AutoSize=$true;$state.Actions.Controls.Add($cancelled);$state|Add-Member NoteProperty Cancelled $cancelled
 $new=Add-ScoliaTeachingAction $state.Actions 'Nouvelle décision' {param($sender,$e)$s=$sender.FindForm().Tag;try{Show-ScoliaDisciplineEditor $s;Update-ScoliaDisciplineView $s -Reload;if(Test-Path function:Refresh-ITMCurrentPage){Refresh-ITMCurrentPage}}catch{Set-ScoliaDisciplineError $s $_}};$new.Primary=$true;$state|Add-Member NoteProperty NewButton $new
 $received=Add-ScoliaTeachingAction $state.Actions 'Parent reçu' {param($sender,$e)$s=$sender.FindForm().Tag;try{Show-ScoliaDisciplineFollowup $s Received;Update-ScoliaDisciplineView $s -Reload}catch{Set-ScoliaDisciplineError $s $_}};$state|Add-Member NoteProperty ReceivedButton $received
 $absent=Add-ScoliaTeachingAction $state.Actions 'Parent absent' {param($sender,$e)$s=$sender.FindForm().Tag;try{Show-ScoliaDisciplineFollowup $s Absent;Update-ScoliaDisciplineView $s -Reload}catch{Set-ScoliaDisciplineError $s $_}};$state|Add-Member NoteProperty AbsentButton $absent
 $cancel=Add-ScoliaTeachingAction $state.Actions 'Annuler la décision' {param($sender,$e)$s=$sender.FindForm().Tag;try{Show-ScoliaDisciplineFollowup $s Cancel;Update-ScoliaDisciplineView $s -Reload;if(Test-Path function:Refresh-ITMCurrentPage){Refresh-ITMCurrentPage}}catch{Set-ScoliaDisciplineError $s $_}};$state|Add-Member NoteProperty CancelButton $cancel
 [void](Add-ScoliaTeachingAction $state.Actions 'Historique' {param($sender,$e)$s=$sender.FindForm().Tag;try{Show-ScoliaDisciplineHistory $s}catch{Set-ScoliaDisciplineError $s $_}})
 [void](Add-ScoliaTeachingAction $state.Actions 'Convocation à imprimer' {param($sender,$e)$s=$sender.FindForm().Tag;try{Export-ScoliaParentConvocation $s}catch{Set-ScoliaDisciplineError $s $_}})
 [void](Add-ScoliaTeachingAction $state.Actions 'Exporter CSV' {param($sender,$e)$s=$sender.FindForm().Tag;try{Export-ScoliaDisciplineCsv $s}catch{Set-ScoliaDisciplineError $s $_}})
 [void](Add-ScoliaTeachingAction $state.Actions 'Actualiser' {param($sender,$e)$s=$sender.FindForm().Tag;try{Update-ScoliaDisciplineView $s -Reload}catch{Set-ScoliaDisciplineError $s $_}})
 $close=Add-ScoliaTeachingAction $state.Actions 'Fermer' {param($sender,$e)$sender.FindForm().Close()};$state.Form.CancelButton=$close
 $menu=New-Object Windows.Forms.ContextMenuStrip;$state|Add-Member NoteProperty MoreMenu $menu
 $more=Add-ScoliaButton $state.Actions 'Actions' {param($sender,$e)$sender.FindForm().Tag.MoreMenu.Show($sender,(New-Object Drawing.Point(0,$sender.Height)))};$more.AutoSize=$false;$more.Width=96
 foreach($button in @($state.Actions.Controls)){
  if($button -isnot [Windows.Forms.Button] -or $button -eq $more){continue}
  if($button.Text -in @('Nouvelle décision','Actualiser','Fermer')){$button.AutoSize=$false;$button.Width=$(if($button -eq $new){165}else{104});$button.Height=38}
  else{$item=New-Object Windows.Forms.ToolStripMenuItem;$item.Text=$button.Text;$item.Tag=[pscustomobject]@{Button=$button;Action=$button.TeachingAction};$item.Add_Click({param($sender,$e)$c=$sender.Tag;if($c.Button.Enabled){& $c.Action $c.Button $e}});[void]$menu.Items.Add($item);$button.Visible=$false}
 }
 $menu.Add_Opening({param($sender,$e)foreach($item in $sender.Items){$item.Enabled=$item.Tag.Button.Enabled};if(Test-Path function:Apply-ITMThemeForm){Apply-ITMThemeForm $sender}})
 $filterTimer=New-Object Windows.Forms.Timer;$filterTimer.Interval=45;$state|Add-Member NoteProperty FilterTimer $filterTimer
 $filterTimer.Add_Tick({param($sender,$e)$sender.Stop();$s=$state;if(-not $s.Form.IsDisposed -and -not $s.Loading){try{Update-ScoliaDisciplineView $s}catch{Set-ScoliaDisciplineError $s $_}}}.GetNewClosure())
 foreach($control in @($search,$kind,$year,$cancelled)){
  $handler={param($sender,$e)$s=$sender.FindForm().Tag;if(-not $s.Loading){try{Update-ScoliaDisciplineView $s}catch{Set-ScoliaDisciplineError $s $_}}}
  if($control -is [Windows.Forms.TextBox]){$control.Add_TextChanged({param($sender,$e)$s=$sender.FindForm().Tag;$s.FilterTimer.Stop();$s.FilterTimer.Start()})}elseif($control -is [Windows.Forms.CheckBox]){$control.Add_CheckedChanged($handler)}else{$control.Add_SelectedIndexChanged($handler)}
 }
 $state.Form.Add_Disposed({$filterTimer.Stop();$filterTimer.Dispose()}.GetNewClosure())
 $state.Form.Add_FormClosing({param($sender,$e)if($script:busy){$e.Cancel=$true}})
 $state.Loading=$false;Update-ScoliaFeatureDialogLayout $state;Update-ScoliaDisciplineView $state -Reload
 if($LayoutOnly){return $state}
 if((Test-Path function:Mount-ScoliaWorkspaceForm) -and (Mount-ScoliaWorkspaceForm $state.Form)){$state.Form.Add_Disposed({$menu.Dispose()}.GetNewClosure());return}
 try{[void]$state.Form.ShowDialog($script:main)}finally{$menu.Dispose();$state.Form.Dispose()}
}
function Select-ScoliaDisciplineLearner($Owner){
 [void](Sync-ScoliaDisciplineSnapshot);$context=Get-ScoliaFeatureContext
 $matches=@($script:data.learners|Where-Object {([string]$_.Annee -replace '\s','') -ceq $context.Year})
 $counts=@{};foreach($l in $matches){$key=Get-ScoliaDisciplineMat $l.Matricule;if($key){if(-not $counts.ContainsKey($key)){$counts[$key]=0};$counts[$key]++}}
 $source=@(foreach($l in $matches){$key=Get-ScoliaDisciplineMat $l.Matricule;if(-not $key -or $counts[$key] -ne 1){continue};[pscustomobject]@{Key=$key;Identity=($l.Nom+' '+$l.PostNom+' '+$l.Prenom).Trim();Promotion=$l.Promotion;Status=$l.Statut;Phone=$l.Contact1;Learner=$l}})
 $state=New-ScoliaFeatureDialog -Title 'Sélectionner un apprenant' -Subtitle 'Recherchez par nom ou matricule puis sélectionnez une seule ligne. Les matricules en double doivent être corrigés dans Diagnostic.' -Width 950 -Height 650
 $state|Add-Member NoteProperty Rows $source;$state|Add-Member NoteProperty Source $source;$state|Add-Member NoteProperty Selection $null
 $search=New-Object Windows.Forms.TextBox;$search.BorderStyle='None';$search.Font=New-Object Drawing.Font('Segoe UI',11);$search.AccessibleName='Rechercher un apprenant'
 $promotion=New-Object Windows.Forms.ComboBox;$promotion.DropDownStyle='DropDownList';$promotion.Items.AddRange([object[]](@('Toutes les promotions')+@($source.Promotion|Sort-Object -Unique)));$promotion.SelectedIndex=0
 $fields=New-Object ITMSearchFields($search,$promotion);$fields.SearchLabel.Text='Nom ou matricule';$fields.KindLabel.Text='Promotion'
 $state.Toolbar.AutoSize=$false;$state.Toolbar.WrapContents=$false;$state.Toolbar.Padding=New-Object Windows.Forms.Padding(18,2,18,10);$state.Toolbar.Controls.Add($fields);$state|Add-Member NoteProperty SearchFields $fields;$state|Add-Member NoteProperty Promotion $promotion;$state|Add-Member NoteProperty Search $search
 $select=Add-ScoliaButton $state.Actions 'Sélectionner' {param($sender,$e)$s=$sender.FindForm().Tag;try{$row=Get-ScoliaFeatureSelectedRow $s;$s.Selection=$row.Learner;$s.Form.Close()}catch{Set-ScoliaDisciplineError $s $_}};$select.Primary=$true
 $close=Add-ScoliaButton $state.Actions 'Annuler' {param($sender,$e)$sender.FindForm().Close()};$state.Form.CancelButton=$close
 $state.Grid.Add_CellDoubleClick({param($sender,$e)if($e.RowIndex -ge 0){$s=$sender.FindForm().Tag;try{$s.Selection=(Get-ScoliaFeatureSelectedRow $s).Learner;$s.Form.Close()}catch{Set-ScoliaDisciplineError $s $_}}})
 $render={param($s,$query)$s.Rows=@($s.Source|Where-Object {($s.Promotion.SelectedItem -eq 'Toutes les promotions' -or $_.Promotion -ceq [string]$s.Promotion.SelectedItem) -and (Test-ScoliaSearchText ($_.Key+' '+$_.Identity+' '+$_.Promotion) $query)});Set-Grid $s.Grid $s.Rows @(@('Key','Clé'),@('Key','Matricule'),@('Identity','Apprenant'),@('Promotion','Promotion'),@('Status','Statut'),@('Phone','Contact parent'));$s.Grid.Columns['Clé'].Visible=$false;$rowCount=@($s.Rows).Count;$s.Message.Text=('{0} apprenant(s) trouvé(s).' -f $rowCount)}
 $state|Add-Member NoteProperty Render $render
 $filterTimer=New-Object Windows.Forms.Timer;$filterTimer.Interval=45
 $filterTimer.Add_Tick({param($sender,$e)$sender.Stop();if(-not $state.Form.IsDisposed){& $state.Render $state $state.Search.Text}}.GetNewClosure())
 $search.Add_TextChanged({$filterTimer.Stop();$filterTimer.Start()}.GetNewClosure())
 $promotion.Add_SelectedIndexChanged({param($sender,$e)$s=$sender.FindForm().Tag;& $s.Render $s $s.Search.Text})
 $state.Form.Add_Disposed({$filterTimer.Stop();$filterTimer.Dispose()}.GetNewClosure())
 $state.Form.AcceptButton=$select;Update-ScoliaFeatureDialogLayout $state
 & $render $state '';try{[void]$state.Form.ShowDialog($Owner);return $state.Selection}finally{$state.Form.Dispose()}
}
function Add-ScoliaDisciplineEditorField($Table,[string]$Caption,$Control){
 [void](Add-ScoliaField $Table $Caption $Control);$label=@($Table.Controls|Where-Object {$_ -is [Windows.Forms.Label] -and $_.Text -ceq $Caption})[-1]
 return [pscustomobject]@{Control=$Control;Label=$label}
}
function Update-ScoliaDisciplineEditor($Form){
 if(-not $Form -or -not $Form.Tag){return};$s=$Form.Tag;$temporary=$s.Type.SelectedIndex -eq 0;$parents=$s.Type.SelectedIndex -eq 2;$severe=($s.Type.SelectedIndex -eq 1 -or ($temporary -and $s.PreviousCount -ge 1))
 foreach($pair in $s.PeriodFields){$pair.Control.Visible=$pair.Label.Visible=($temporary -and -not $severe)}
 foreach($pair in $s.ParentFields){$pair.Control.Visible=$pair.Label.Visible=$parents}
 $s.Confirm.Visible=$s.ConfirmPair.Label.Visible=$severe
 if($severe){$s.Notice.Tag='scolia:feedback:error';$s.Notice.Text=$(if($temporary){'ATTENTION : '+$s.PreviousCount+' renvoi temporaire déjà validé dans cette année. Cette deuxième décision entraînera un RENVOI DÉFINITIF.'}else{'Le renvoi définitif bloque l admission et les cartes tant que la décision reste valide.'})}
 elseif($temporary){$s.Notice.Tag=$null;$s.Notice.Text='Durée inclusive : '+(($s.End.Value.Date-$s.Start.Value.Date).Days+1)+' jour(s). Retour autorisé dès le '+$s.End.Value.Date.AddDays(1).ToString('dd/MM/yyyy')+'.'}
 else{$s.Notice.Tag=$null;$s.Notice.Text='La convocation ne compte pas comme renvoi. Le rendez-vous et le résultat de la réception du parent seront conservés.'}
 $valid=$s.Type.SelectedIndex -ge 0 -and $s.Reason.Text.Trim().Length -ge 5
 if($temporary -and -not $severe){$valid=$valid -and $s.Start.Value.Date -ge $s.Decision.Value.Date -and $s.End.Value.Date -ge $s.Start.Value.Date}
 if($parents){$valid=$valid -and $s.Meeting.Value.Date -ge $s.Decision.Value.Date}
 $s.Save.Enabled=$valid -and (-not $severe -or $s.Confirm.Checked)
 if(Test-Path function:Update-ITMFluidTable){Update-ITMFluidTable $s.Table}
}
function Show-ScoliaDisciplineEditor($Manager,$Learner=$null,[switch]$LayoutOnly){
 Assert-ScoliaDisciplineDialog $Manager -Write
 if(-not $Learner){$Learner=Select-ScoliaDisciplineLearner $Manager.Form};if(-not $Learner){return}
 $fresh=Read-ScoliaDisciplineState;$count=Get-ScoliaDisciplineTemporaryCount $fresh.Document $Learner.Matricule;$bounds=Get-ScoliaAttendanceYearBounds $Manager.Context.Year
 if((Get-Date).Date -lt $bounds.Start){throw 'Les décisions ne peuvent pas précéder le début de cette année scolaire.'}
 $dialog=New-ScoliaDialog 'Nouvelle décision disciplinaire' 760 750;$table=New-ScoliaTable $dialog
 $identity=New-Object ITMReadableLabel;$identity.AutoSize=$true;$identity.Text=$Learner.Matricule+' · '+$Learner.Nom+' '+$Learner.PostNom+' '+$Learner.Prenom+' · '+$Learner.Promotion;[void](Add-ScoliaField $table 'Apprenant sélectionné' $identity)
 $type=New-Object Windows.Forms.ComboBox;$type.DropDownStyle='DropDownList';$type.Items.AddRange([object[]]@('Renvoi temporaire','Renvoi définitif','Convocation des parents'));$type.SelectedIndex=0;[void](Add-ScoliaField $table 'Décision' $type)
 $reason=New-Object Windows.Forms.TextBox;$reason.Multiline=$true;$reason.Height=90;$reason.MaxLength=2000;[void](Add-ScoliaField $table 'Motif ou cause (obligatoire)' $reason)
 $decision=New-Object Windows.Forms.DateTimePicker;$decision.Format='Short';$decision.MinDate=$bounds.Start;$decision.MaxDate=$(if((Get-Date).Date -lt $bounds.End){(Get-Date).Date}else{$bounds.End});$decision.Value=$decision.MaxDate;[void](Add-ScoliaField $table 'Date de décision' $decision)
 $start=New-Object Windows.Forms.DateTimePicker;$start.Format='Short';$start.MinDate=$bounds.Start;$start.MaxDate=$bounds.End;$start.Value=$decision.Value;$startPair=Add-ScoliaDisciplineEditorField $table 'Premier jour de renvoi' $start
 $end=New-Object Windows.Forms.DateTimePicker;$end.Format='Short';$end.MinDate=$bounds.Start;$end.MaxDate=$bounds.End;$end.Value=$decision.Value;$endPair=Add-ScoliaDisciplineEditorField $table 'Dernier jour de renvoi inclus' $end
 $meeting=New-Object Windows.Forms.DateTimePicker;$meeting.Format='Custom';$meeting.CustomFormat='dd/MM/yyyy HH:mm';$meeting.Value=(Get-Date).AddDays(1);$meetingPair=Add-ScoliaDisciplineEditorField $table 'Rendez-vous du parent' $meeting
 $confirm=New-Object ITMReadableCheckBox;$confirm.Text='Je confirme le renvoi définitif';$confirm.AutoSize=$true;$confirmPair=Add-ScoliaDisciplineEditorField $table 'Confirmation' $confirm
 $notice=New-Object ITMReadableLabel;$notice.AutoSize=$true;[void](Add-ScoliaField $table 'Conséquence' $notice)
 $errorLabel=New-Object ITMReadableLabel;$errorLabel.AutoSize=$true;$errorLabel.Tag='scolia:feedback:error';[void](Add-ScoliaField $table '' $errorLabel)
 $save=New-Button $table 'Valider la décision' 0 0 240 $true;[void](Add-ScoliaField $table '' $save)
 $dialog.Tag=[pscustomobject]@{Manager=$Manager;Learner=$Learner;Type=$type;Reason=$reason;Decision=$decision;Start=$start;End=$end;Meeting=$meeting;Confirm=$confirm;ConfirmPair=$confirmPair;Notice=$notice;Error=$errorLabel;Save=$save;Table=$table;PeriodFields=@($startPair,$endPair);ParentFields=@($meetingPair);PreviousCount=$count;Revision=[int]$fresh.Document.Revision;OperationId=[guid]::NewGuid().ToString('D');Saved=$false}
 foreach($control in @($reason,$type,$decision,$start,$end,$meeting,$confirm)){
  $handler={param($sender,$e)Update-ScoliaDisciplineEditor $sender.FindForm()}
  if($control -is [Windows.Forms.TextBox]){$control.Add_TextChanged($handler)}elseif($control -is [Windows.Forms.ComboBox]){$control.Add_SelectedIndexChanged($handler)}elseif($control -is [Windows.Forms.CheckBox]){$control.Add_CheckedChanged($handler)}else{$control.Add_ValueChanged($handler)}
 }
 $save.Add_Click({param($sender,$e)$s=$sender.FindForm().Tag;if(-not $sender.Enabled){return};try{
  Assert-ScoliaDisciplineDialog $s.Manager -Write;$requested=@('Temporary','Permanent','Parents')[$s.Type.SelectedIndex];$severe=($requested -eq 'Permanent' -or ($requested -eq 'Temporary' -and $s.PreviousCount -ge 1))
  if($severe -and (-not $s.Confirm.Checked -or [ITMMessageBox]::Show($sender.FindForm(),'Confirmer le RENVOI DÉFINITIF de cet apprenant ?','Discipline','YesNo','Warning',[Windows.Forms.MessageBoxDefaultButton]::Button2) -ne 'Yes')){return}
  $start='';$end='';$meeting=''
  if($requested -eq 'Temporary'){$start=$(if($severe){$s.Decision.Value.Date}else{$s.Start.Value.Date}).ToString('yyyy-MM-dd');$end=$(if($severe){$s.Decision.Value.Date}else{$s.End.Value.Date}).ToString('yyyy-MM-dd')}
  if($requested -eq 'Parents'){$meeting=([datetimeoffset]::new($s.Meeting.Value)).ToString('o')}
  $request=New-ScoliaDisciplineRequest -Learner $s.Learner -Type $requested -Reason $s.Reason.Text -DecisionDate $s.Decision.Value.Date.ToString('yyyy-MM-dd') -StartDate $start -EndDate $end -MeetingAt $meeting -Id $s.OperationId
  $actor=Get-ScoliaFeatureActor;$sender.Enabled=$false
  [void](Invoke-ScoliaDisciplineUpdate -ExpectedRevision $s.Revision -Change {param($document)
   $matches=@($script:data.learners|Where-Object {(Get-ScoliaDisciplineMat $_.Matricule) -ceq $request.Matricule -and ([string]$_.Annee -replace '\s','') -ceq $document.Context.Year});if($matches.Count -ne 1){throw 'Dossier absent ou matricule en double. Actualisez avant de valider.'}
   [void](Add-ScoliaDisciplineDecision -Document $document -Request $request -ExpectedRevision $s.Revision -Actor $actor -PriorDocuments @(Get-ScoliaDisciplineDocuments))
  })
  $s.Saved=$true;[void](Sync-ScoliaDisciplineSnapshot);$sender.FindForm().DialogResult='OK';$sender.FindForm().Close()
 }catch{if($s.Saved){$s.Manager.Message.Text='Décision enregistrée ; actualisation des statuts à reprendre : '+$_.Exception.Message;$sender.FindForm().DialogResult='OK';$sender.FindForm().Close()}else{$s.Error.Text=$_.Exception.Message;Update-ScoliaDisciplineEditor $sender.FindForm()}}})
 Update-ScoliaDisciplineEditor $dialog
 if($LayoutOnly){return $dialog}
 try{Apply-ITMThemeForm $dialog;[void]$dialog.ShowDialog($Manager.Form)}finally{$dialog.Dispose()}
}
function Show-ScoliaDisciplineFollowup($State,[ValidateSet('Cancel','Received','Absent')][string]$Action){
 Assert-ScoliaDisciplineDialog $State -Write;if($Action -eq 'Cancel'){Assert-ITMPermission 'Administrer'}
 $row=Get-ScoliaFeatureSelectedRow $State
 if($row.Record.State -ne 'Validated'){throw 'Cette décision est déjà annulée.'}
 if($Action -ne 'Cancel' -and $row.Record.Type -ne 'Parents'){throw 'Sélectionnez une convocation de parents.'}
 $title=switch($Action){'Cancel'{'Annuler une décision'};'Received'{'Réception du parent'};default{'Parent absent au rendez-vous'}}
 $dialog=New-ScoliaDialog $title 650 480;$table=New-ScoliaTable $dialog
 $label=New-Object ITMReadableLabel;$label.AutoSize=$true;$label.Text=$row.Identity+' · '+$row.Type+' · '+$row.Date;[void](Add-ScoliaField $table 'Décision sélectionnée' $label)
 $reason=New-Object Windows.Forms.TextBox;$reason.Multiline=$true;$reason.Height=100;$reason.MaxLength=2000;[void](Add-ScoliaField $table $(if($Action -eq 'Cancel'){'Motif de l annulation'}else{'Compte rendu / observation'}) $reason)
 $note=New-Object ITMReadableLabel;$note.AutoSize=$true;$note.Text='Les décisions et les sanctions restantes ne sont pas effacées. L annulation de ce dossier ne supprime pas une autre décision de renvoi définitif.';[void](Add-ScoliaField $table 'Historique' $note)
 $errorLabel=New-Object ITMReadableLabel;$errorLabel.AutoSize=$true;$errorLabel.Tag='scolia:feedback:error';[void](Add-ScoliaField $table '' $errorLabel)
 $save=New-Button $table 'Confirmer' 0 0 170 $true;$save.Enabled=$false;[void](Add-ScoliaField $table '' $save)
 $dialog.Tag=[pscustomobject]@{Manager=$State;Row=$row;Action=$Action;Reason=$reason;Error=$errorLabel;Save=$save;Revision=[int]$State.Model.Document.Revision}
 $reason.Add_TextChanged({param($sender,$e)$sender.FindForm().Tag.Save.Enabled=$sender.Text.Trim().Length -ge 5})
 $save.Add_Click({param($sender,$e)$s=$sender.FindForm().Tag;try{
  Assert-ScoliaDisciplineDialog $s.Manager -Write;$actor=Get-ScoliaFeatureActor
  [void](Invoke-ScoliaDisciplineUpdate -ExpectedRevision $s.Revision -Administrative:($s.Action -eq 'Cancel') -Change {param($document)Update-ScoliaDisciplineDecision $document $s.Row.Key $s.Action $s.Reason.Text $s.Revision $actor})
  [void](Sync-ScoliaDisciplineSnapshot);$sender.FindForm().Close()
 }catch{$s.Error.Text=$_.Exception.Message}})
 try{Apply-ITMThemeForm $dialog;[void]$dialog.ShowDialog($State.Form)}finally{$dialog.Dispose()}
}
function Show-ScoliaDisciplineHistory($State){
 Assert-ScoliaDisciplineDialog $State;$row=Get-ScoliaFeatureSelectedRow $State;$lines=New-Object 'System.Collections.Generic.List[string]'
 foreach($event in @($row.Record.Audit)){$lines.Add(([datetimeoffset]::Parse($event.At)).ToLocalTime().ToString('dd/MM/yyyy HH:mm')+' · '+$event.By+' · '+$(switch($event.Action){'Create'{'Création'};'Cancel'{'Annulation'};'Received'{'Parent reçu'};'Absent'{'Parent absent'};default{$event.Action}}));$lines.Add($event.Reason);$lines.Add('')}
 Show-TextDetail ('Discipline · '+$row.Identity) ($lines.ToArray() -join "`r`n")
}
function Export-ScoliaDisciplineCsv($State){
 Assert-ScoliaDisciplineDialog $State;Assert-ITMPermission 'Exporter';if(-not $State.Rows.Count){throw 'Aucune décision à exporter.'}
 $picker=New-Object Windows.Forms.SaveFileDialog;$picker.Filter='CSV (*.csv)|*.csv';$picker.FileName='Discipline-'+[string]$State.Year.SelectedItem+'.csv'
 try{if($picker.ShowDialog($State.Form) -ne 'OK'){return};$State.Rows|Select-Object Matricule,@{Name='Apprenant';Expression={$_.Identity}},Promotion,@{Name='Date décision';Expression={$_.Date}},@{Name='Mesure';Expression={$_.Type}},@{Name='Motif / cause';Expression={$_.Reason}},@{Name='Période / rendez-vous';Expression={$_.Period}},@{Name='Durée (jours calendaires)';Expression={$_.Days}},@{Name='Retour autorisé dès';Expression={$_.ReturnDate}},@{Name='Situation';Expression={$_.State}}|Export-Csv -LiteralPath $picker.FileName -Delimiter ';' -NoTypeInformation -Encoding UTF8;$State.Message.Text='Registre exporté : '+$picker.FileName}finally{$picker.Dispose()}
}
function New-ScoliaParentConvocationHtml($Record,[string]$Institution,[string]$LogoData=''){
 if($Record.Type -cne 'Parents' -or $Record.State -cne 'Validated'){throw 'Choisissez une convocation valide.'}
 $school=[Net.WebUtility]::HtmlEncode($Institution);$name=[Net.WebUtility]::HtmlEncode($Record.Identity);$mat=[Net.WebUtility]::HtmlEncode($Record.Matricule);$promo=[Net.WebUtility]::HtmlEncode($Record.Promotion);$year=[Net.WebUtility]::HtmlEncode($Record.Year)
 $reason=([Net.WebUtility]::HtmlEncode($Record.Reason)).Replace("`r`n","`n").Replace("`n",'<br>');$parent=$(if($Record.Learner.Tuteur){[Net.WebUtility]::HtmlEncode($Record.Learner.Tuteur)}else{'Madame, Monsieur, parent ou responsable légal'})
 $meeting=([datetimeoffset]::Parse($Record.MeetingAt)).ToLocalTime().ToString('dd/MM/yyyy à HH:mm');$date=(Get-ScoliaDisciplineDate $Record.DecisionDate).ToString('dd/MM/yyyy')
 $logo=$(if($LogoData -match '^data:image/(png|jpeg|gif);base64,[a-zA-Z0-9+/=]+$'){'<img alt="Logo de l institution" src="'+$LogoData+'">'}else{''})
 $signatureLabel='La direction / scolarité';$signatureName=''
 if(Test-Path function:Get-ScoliaDocumentSignatory){$person=Get-ScoliaDocumentSignatory 'ConvocationParents';$signatureLabel=[Net.WebUtility]::HtmlEncode($person.Label);$signatureName=[Net.WebUtility]::HtmlEncode($(if($person.Configured){$person.Name}else{'Nom à renseigner dans Administration'}))}
 return @"
<!doctype html><html lang="fr"><meta charset="utf-8"><title>Convocation des parents — $mat</title>
<style>@page{size:A4;margin:20mm}*{box-sizing:border-box}body{max-width:170mm;margin:20px auto;color:#23344b;font:15px/1.65 'Segoe UI',Arial,sans-serif}header{display:flex;gap:20px;align-items:center;border-bottom:3px solid #2458a0;padding-bottom:18px}header img{width:70px;height:70px;object-fit:contain}h1{font-size:24px;text-align:center;margin:32px 0 24px;color:#2458a0}.meta{background:#f1f5fa;border:1px solid #d9e1ec;padding:16px;margin:22px 0}.reason{border-left:3px solid #2458a0;padding:12px 18px;margin:22px 0;white-space:normal;overflow-wrap:anywhere}.signature{margin-top:60px;display:flex;justify-content:space-between}.print{background:#2458a0;color:white;border:0;border-radius:7px;padding:12px 20px;cursor:pointer}@media print{body{margin:0}.print{display:none}}</style>
<button class="print" onclick="window.print()">Imprimer / enregistrer en PDF</button>
<header>$logo<div><strong>$school</strong><br>Service de scolarité · Année $year</div></header>
<h1>CONVOCATION DES PARENTS</h1><p>Décision du $date</p><p>$parent,</p>
<p>Nous vous invitons à vous présenter à l’établissement le <strong>$meeting</strong> au sujet de votre enfant.</p>
<div class="meta"><strong>Apprenant :</strong> $name<br><strong>Matricule :</strong> $mat<br><strong>Promotion / classe :</strong> $promo</div>
<strong>Motif de la convocation</strong><div class="reason">$reason</div>
<p>Votre présence permettra d’échanger sur la situation de l’apprenant et de convenir des suites à donner.</p>
<div class="signature"><span>Le parent / responsable<br><br>Signature : __________________</span><span>$signatureLabel<br><strong>$signatureName</strong><br><br>Signature et cachet</span></div></html>
"@
}
function Export-ScoliaParentConvocation($State){
 Assert-ScoliaDisciplineDialog $State;Assert-ITMPermission 'Exporter';$row=Get-ScoliaFeatureSelectedRow $State
 $logoData='';$logo=Get-ITMInstitutionLogo
 if($logo -and [IO.File]::Exists($logo)){$mime=switch([IO.Path]::GetExtension($logo).ToLowerInvariant()){'.png'{'png'};'.jpg'{'jpeg'};'.jpeg'{'jpeg'};'.gif'{'gif'};default{''}};if($mime){$logoData='data:image/'+$mime+';base64,'+[Convert]::ToBase64String([IO.File]::ReadAllBytes($logo))}}
 $html=New-ScoliaParentConvocationHtml $row.Record (Get-ITMInstitutionName) $logoData
 $picker=New-Object Windows.Forms.SaveFileDialog;$picker.Filter='Convocation imprimable (*.html)|*.html';$picker.FileName='Convocation-'+($row.Matricule -replace '[^a-zA-Z0-9_-]','_')+'.html'
 try{if($picker.ShowDialog($State.Form) -ne 'OK'){return};[IO.File]::WriteAllText($picker.FileName,$html,[Text.UTF8Encoding]::new($true));Start-Process -FilePath $picker.FileName;$State.Message.Text='Convocation prête à remettre au parent : utilisez Imprimer / enregistrer en PDF.'}finally{$picker.Dispose()}
}
