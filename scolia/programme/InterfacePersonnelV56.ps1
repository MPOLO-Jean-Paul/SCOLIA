# Interfaces du personnel : pointages immuables et calcul de paie sans versement automatique.
function Assert-ScoliaPersonnelInterfaceContext([object]$State,[string]$Permission='Lire',[bool]$Write=$false) {
    Assert-ITMPermission $Permission
    $current=Get-ScoliaFeatureContext
    if($current.Id -cne $State.Context.Id -or $current.Year -cne $State.Context.Year -or $current.LocalKey -cne $State.Context.LocalKey){throw 'La base ou la session a changé. Fermez cette fenêtre et rouvrez cet outil.'}
    if($Write -and $script:data.closed){throw 'Année clôturée : consultation uniquement.'}
}
function Set-ScoliaPersonnelInterfaceMessage([object]$State,[string]$Text,[bool]$Error=$false) {
    $State.Message.Text=$Text;$State.Message.Tag=$(if($Error){'scolia:feedback:error'}else{'scolia:feedback:success'})
    if(Test-Path function:Apply-ITMThemeForm){Apply-ITMThemeForm $State.Form}
}
function Add-ScoliaPersonnelColumn([object]$Grid,[string]$Name,[string]$Caption,[int]$Width,[switch]$Check) {
    $column=$(if($Check){New-Object Windows.Forms.DataGridViewCheckBoxColumn}else{New-Object Windows.Forms.DataGridViewTextBoxColumn})
    $column.Name=$Name;$column.HeaderText=$Caption;$column.Width=$Width;$column.ReadOnly=$true;$column.SortMode='NotSortable'
    [void]$Grid.Columns.Add($column)
}
function Format-ScoliaPersonnelTime([object]$Value) {
    if(-not [string]$Value){return ''}
    return ([datetimeoffset]::Parse([string]$Value,[Globalization.CultureInfo]::InvariantCulture)).ToString('HH:mm:ss')
}
function Get-ScoliaPersonnelOptionalField([object]$Value,[string]$Name,[object]$Default=$null) {
    if($Value -and $Value.PSObject.Properties[$Name]){return $Value.PSObject.Properties[$Name].Value}
    return $Default
}
function Get-ScoliaPersonnelLateGroups {
    param([object]$Teacher,[datetime]$First,[datetime]$Last,[object[]]$Calendars,[object[]]$Punches,[object[]]$Justifications,[string]$Year,[int]$StartMonth,[datetimeoffset]$AsOf)
    $workDates=@($Calendars|ForEach-Object {@($_.WorkDates)});$sourceRows=New-Object 'System.Collections.Generic.List[object]'
    $weekFirst=Get-ScoliaPayrollWeekStart $First
    for($date=$weekFirst;$date -le $Last;$date=$date.AddDays(1)){
        $sourceRows.Add((Get-ScoliaTeacherDailyStatus -Teacher $Teacher -Date $date -Punches $Punches -Justifications $Justifications -WorkDates $workDates -AsOf $AsOf))
    }
    return @(Get-ScoliaPayrollLateGroups -SourceRows @($sourceRows.ToArray()) -SourceCalendars $Calendars -Month $Last.ToString('yyyy-MM') -Year $Year -StartMonth $StartMonth)
}
function Set-ScoliaPersonnelLateDetail([object]$Day,[object]$Group) {
    foreach($entry in (@{LatePenalty=$true;LatePenaltyJustified=[bool]$Group.IsJustified;LatePenaltyReason=[string]$Group.Reason;PenaltyWeekStart=[string]$Group.WeekStart;ConvertedDates=@($Group.Dates)}).GetEnumerator()){
        $Day|Add-Member NoteProperty $entry.Key $entry.Value -Force
    }
}
function Get-ScoliaSelectedTeacherRow([object]$State) {
    if(-not $State.Grid.CurrentRow -or -not $State.Grid.CurrentRow.Tag){throw 'Sélectionnez un enseignant dans la liste.'}
    return $State.Grid.CurrentRow.Tag
}
function New-ScoliaTeacherAttendanceDialog([object]$Context) {
    $state=New-ScoliaFeatureDialog -Title 'Présences des enseignants' -Subtitle 'Entrée avant 08:00 et sortie au départ. Les heures sont automatiques ; le pointage reste accessible en retard.' -Width 1040 -Height 670
    foreach($entry in (@{Context=$Context;Loading=$true;Revision=0;Document=$null;LoadedDate=(Get-Date).Date;Rows=@()}).GetEnumerator()){$state|Add-Member NoteProperty $entry.Key $entry.Value}
    $state.Message.Height=100;$state.Grid.AccessibleName='Pointage des enseignants : entrée et sortie';$state.Grid.ReadOnly=$true
    $bounds=Get-ScoliaAttendanceYearBounds $Context.Year
    $date=New-Object Windows.Forms.DateTimePicker;$date.Format='Short';$date.Width=135;$date.AccessibleName='Journée consultée';$date.MinDate=$bounds.Start;$date.MaxDate=$bounds.End
    $today=(Get-Date).Date;$date.Value=$(if($today -lt $bounds.Start){$bounds.Start}elseif($today -gt $bounds.End){$bounds.End}else{$today});$state.Toolbar.Controls.Add($date)
    $state|Add-Member NoteProperty DatePicker $date
    $refresh=New-Button $state.Toolbar 'Actualiser' 0 0 115;$refresh.Name='PersonnelActualiser'
    $manage=New-Button $state.Toolbar 'Enseignants' 0 0 135;$manage.Name='PersonnelEnseignants'
    $justify=New-Button $state.Toolbar 'Justifier l''absence' 0 0 175;$justify.Name='PersonnelJustifier'
    $salary=New-Button $state.Toolbar 'Paie mensuelle' 0 0 150;$salary.Name='PersonnelPaie'
    $state|Add-Member NoteProperty ManageButton $manage;$state|Add-Member NoteProperty JustifyButton $justify;$state|Add-Member NoteProperty SalaryButton $salary
    Add-ScoliaPersonnelColumn $state.Grid 'Nom' 'Enseignant' 250
    Add-ScoliaPersonnelColumn $state.Grid 'Entry' 'Entrée' 65 -Check
    Add-ScoliaPersonnelColumn $state.Grid 'EntryAt' 'Heure entrée' 110
    Add-ScoliaPersonnelColumn $state.Grid 'Exit' 'Sortie' 65 -Check
    Add-ScoliaPersonnelColumn $state.Grid 'ExitAt' 'Heure sortie' 110
    Add-ScoliaPersonnelColumn $state.Grid 'Status' 'Situation' 235
    Add-ScoliaPersonnelColumn $state.Grid 'Reason' 'Justification' 260
    $refresh.Add_Click({param($sender,$e)[void](Update-ScoliaTeacherAttendanceDialog $sender.FindForm().Tag)})
    $date.Add_ValueChanged({param($sender,$e)$s=$sender.FindForm().Tag;if(-not $s.Loading){[void](Update-ScoliaTeacherAttendanceDialog $s)}})
    $charges=New-Button $state.Toolbar 'Charge des cours' 0 0 180
    $charges.Add_Click({param($sender,$e)try{Show-ScoliaTeachingManager Dashboard $sender.FindForm()}catch{Set-ScoliaPersonnelInterfaceMessage $sender.FindForm().Tag $_.Exception.Message $true}})
    $manage.Add_Click({param($sender,$e)$s=$sender.FindForm().Tag;try{Assert-ScoliaPersonnelInterfaceContext $s 'Administrer';Show-ScoliaTeacherManager -Owner $s.Form;[void](Update-ScoliaTeacherAttendanceDialog $s)}catch{Set-ScoliaPersonnelInterfaceMessage $s $_.Exception.Message $true}})
    $justify.Add_Click({param($sender,$e)$s=$sender.FindForm().Tag;try{Assert-ScoliaPersonnelInterfaceContext $s 'Administrer';Show-ScoliaTeacherJustification -AttendanceState $s;[void](Update-ScoliaTeacherAttendanceDialog $s)}catch{Set-ScoliaPersonnelInterfaceMessage $s $_.Exception.Message $true}})
    $salary.Add_Click({param($sender,$e)$s=$sender.FindForm().Tag;try{Assert-ScoliaPersonnelInterfaceContext $s 'Administrer';Show-ScoliaMonthlyPayroll -Owner $s.Form -Month $s.DatePicker.Value}catch{Set-ScoliaPersonnelInterfaceMessage $s $_.Exception.Message $true}})
    $state.Grid.Add_CellContentClick({param($sender,$e)if($e.RowIndex -ge 0 -and $e.ColumnIndex -ge 0){$column=$sender.Columns[$e.ColumnIndex].Name;if($column -in @('Entry','Exit')){Invoke-ScoliaTeacherPunchClick -State $sender.FindForm().Tag -RowIndex $e.RowIndex -Direction $column}}})
    $state.Grid.Add_KeyDown({param($sender,$e)if($e.KeyCode -eq 'Space' -and $sender.CurrentCell){$column=$sender.CurrentCell.OwningColumn.Name;if($column -in @('Entry','Exit')){$e.Handled=$true;$e.SuppressKeyPress=$true;Invoke-ScoliaTeacherPunchClick -State $sender.FindForm().Tag -RowIndex $sender.CurrentCell.RowIndex -Direction $column}}})
    $clock=New-Object Windows.Forms.Timer;$clock.Interval=15000
    $clock|Add-Member NoteProperty ScoliaState $state
    $state|Add-Member NoteProperty ClockTimer $clock
    $state|Add-Member NoteProperty ClockMinute ([datetimeoffset]::Now.ToString('yyyy-MM-dd HH:mm'))
    $state|Add-Member NoteProperty ClockDate ([datetimeoffset]::Now.Date)
    $state|Add-Member NoteProperty ClockTimerDisposed $false
    $clock.Add_Tick({param($sender,$e)Update-ScoliaTeacherAttendanceClock -State $sender.ScoliaState -Now ([datetimeoffset]::Now)})
    $state.Form.Add_Shown({param($sender,$e)if(-not $sender.Tag.ClockTimerDisposed){$sender.Tag.ClockTimer.Start()}})
    $state.Form.Add_FormClosed({param($sender,$e)Stop-ScoliaTeacherAttendanceClock $sender.Tag})
    $state.Form.Add_Disposed({param($sender,$e)Stop-ScoliaTeacherAttendanceClock $sender.Tag})
    $state.Loading=$false
    return $state
}
function Stop-ScoliaTeacherAttendanceClock([object]$State) {
    if(-not $State -or $State.ClockTimerDisposed){return}
    $State.ClockTimer.Stop();$State.ClockTimer.Dispose();$State.ClockTimerDisposed=$true
}
function Update-ScoliaTeacherAttendanceClock {
    param([object]$State,[datetimeoffset]$Now=[datetimeoffset]::Now)
    if(-not $State -or $State.ClockTimerDisposed -or $State.Form.IsDisposed -or -not $State.Form.Visible -or $State.Loading -or $script:busy){return}
    $minute=$Now.ToString('yyyy-MM-dd HH:mm')
    if($minute -ceq $State.ClockMinute){return}
    $selected=$(if($State.Grid.CurrentRow -and $State.Grid.CurrentRow.Tag){[string]$State.Grid.CurrentRow.Tag.Teacher.Id}else{''})
    if($Now.Date -ne $State.ClockDate -and $State.DatePicker.Value.Date -eq $State.ClockDate -and $Now.Date -ge $State.DatePicker.MinDate.Date -and $Now.Date -le $State.DatePicker.MaxDate.Date){
        $State.Loading=$true
        try{$State.DatePicker.Value=$Now.Date}finally{$State.Loading=$false}
    }
    $State.ClockDate=$Now.Date;$State.ClockMinute=$minute
    if(Update-ScoliaTeacherAttendanceDialog $State){
        if($selected){foreach($row in $State.Grid.Rows){if($row.Tag -and $row.Tag.Teacher.Id -ceq $selected){$State.Grid.CurrentCell=$row.Cells['Entry'];break}}}
    }
}
function Update-ScoliaTeacherAttendanceDialog([object]$State) {
    try{
        Assert-ScoliaPersonnelInterfaceContext $State
        $document=Read-ScoliaFeatureStore -Name 'personnel';Assert-ScoliaPersonnelDocument $document
        $salaries=Read-ScoliaFeatureStore -Name 'salaires';Assert-ScoliaSalaryDocument $salaries
        $day=$State.DatePicker.Value.Date;$month=$day.ToString('yyyy-MM');$calendar=@($salaries.Items|Where-Object {$_.Kind -eq 'Calendar' -and $_.Month -eq $month})
        $workDates=@($(if($calendar.Count -eq 1){@($calendar[0].WorkDates)}else{@()}))
        $teachers=@(Get-ScoliaTeacherRoster -Document $document -Date $day);$now=[datetimeoffset]::Now;$rows=@()
        $punches=@($document.Items|Where-Object {$_.Kind -eq 'Punch' -and $_.Date -eq $day.ToString('yyyy-MM-dd')});$justifications=@($document.Items|Where-Object {$_.Kind -eq 'Justification' -and $_.Date -eq $day.ToString('yyyy-MM-dd')})
        $State.Loading=$true
        try{
            $State.Grid.Rows.Clear()
            foreach($teacher in $teachers){
                $status=Get-ScoliaTeacherDailyStatus -Teacher $teacher -Date $day -Punches $punches -Justifications $justifications -WorkDates $workDates -AsOf $now
                $caption=$(if($calendar.Count -ne 1){'Calendrier à définir · '+[string]$status.Status}else{[string]$status.Status})
                $lateGroups=@();$lateToday=@()
                if($calendar.Count -eq 1){
                    $lateGroups=@(Get-ScoliaPersonnelLateGroups -Teacher $teacher -First $day -Last $day -Calendars @($salaries.Items|Where-Object {$_.Kind -eq 'Calendar'}) -Punches @($document.Items|Where-Object {$_.Kind -eq 'Punch' -and $_.TeacherId -eq $teacher.Id}) -Justifications @($document.Items|Where-Object {$_.Kind -eq 'Justification' -and $_.TeacherId -eq $teacher.Id}) -Year $State.Context.Year -StartMonth ([int]$calendar[0].StartMonth) -AsOf $now)
                    $lateToday=@($lateGroups|Where-Object {$_.AssignedDate -eq $day.ToString('yyyy-MM-dd')})
                    if($lateToday.Count -eq 1){$caption+=' · 1 absence équivalente par 3 retards'}
                }
                $index=$State.Grid.Rows.Add([string]$teacher.Nom,[bool]$status.EntryAt,(Format-ScoliaPersonnelTime $status.EntryAt),[bool]$status.ExitAt,(Format-ScoliaPersonnelTime $status.ExitAt),$caption,[string]$status.Reason)
                $row=$State.Grid.Rows[$index];$row.Tag=[pscustomobject]@{Teacher=$teacher;Status=$status;LateGroups=$lateGroups}
                $row.Cells['Entry'].ToolTipText=$(if($status.EntryAt){'Signature enregistrée et non modifiable.'}else{'Cocher pour enregistrer l''heure actuelle. À partir de 08:00, l''entrée est signalée en retard ; la case reste accessible.'})
                $row.Cells['Exit'].ToolTipText=$(if($status.ExitAt){'Signature enregistrée et non modifiable.'}elseif(-not $status.EntryAt){'Signez l''entrée avant la sortie.'}else{'Cocher pour enregistrer l''heure actuelle, uniquement aujourd''hui.'})
                if($lateToday.Count -eq 1){
                    $dateCaption=@($lateToday[0].Dates|ForEach-Object {[datetime]::ParseExact([string]$_,'yyyy-MM-dd',[Globalization.CultureInfo]::InvariantCulture).ToString('dd/MM')}) -join ', '
                    $row.Cells['Status'].ToolTipText='Trois retards : '+$dateCaption+'. '+$(if($lateToday[0].IsJustified){'Absence équivalente justifiée, sans retenue.'}else{'Absence équivalente non justifiée. Un même jour ne subit jamais deux retenues.'})
                }
                $rows+=@($row.Tag)
            }
            $State.Document=$document;$State.Revision=[int]$document.Revision;$State.LoadedDate=$day;$State.Rows=$rows
        }finally{$State.Loading=$false}
        $admin=(Test-ITMPermission 'Administrer');$write=(-not $script:data.closed)
        $State.ManageButton.Enabled=$admin -and $write;$State.JustifyButton.Enabled=$admin -and $write -and $teachers.Count -gt 0 -and $day -le $now.Date;$State.SalaryButton.Enabled=$admin
        $note=$(if($day -ne $now.Date){'Consultation : seule la journée actuelle permet de signer. '}else{'Cliquez une fois sur Entrée, puis sur Sortie au départ. '})
        if($calendar.Count -ne 1){$note+='Définissez les jours ouvrables dans Paie mensuelle pour calculer les absences.'}else{$note+='Entrée à partir de 08:00 : retard. Trois retards terminés dans une même semaine (lundi à dimanche) valent une absence. Une seule signature devient une absence en fin de journée.'}
        Set-ScoliaPersonnelInterfaceMessage $State ([string]::Format('{0} enseignant(s) · {1}. {2}',$teachers.Count,$day.ToString('dd/MM/yyyy'),$note))
        return $true
    }catch{$State.Grid.Rows.Clear();$State.Rows=@();Set-ScoliaPersonnelInterfaceMessage $State $_.Exception.Message $true;return $false}
}
function Invoke-ScoliaTeacherPunchClick([object]$State,[int]$RowIndex,[ValidateSet('Entry','Exit')][string]$Direction) {
    if($State.Loading){return}
    try{
        Assert-ScoliaPersonnelInterfaceContext $State 'Saisie' $true
        $now=[datetimeoffset]::Now
        if($State.LoadedDate -ne $now.Date -or $State.DatePicker.Value.Date -ne $now.Date){throw 'Le pointage est possible uniquement aujourd''hui. L''heure ne peut pas être saisie manuellement.'}
        $selected=$State.Grid.Rows[$RowIndex].Tag
        if(-not $selected){throw 'Actualisez la liste des enseignants.'}
        if(($Direction -eq 'Entry' -and $selected.Status.EntryAt) -or ($Direction -eq 'Exit' -and $selected.Status.ExitAt)){throw 'Cette signature est déjà enregistrée. Elle ne peut pas être retirée.'}
        if($Direction -eq 'Exit' -and -not $selected.Status.EntryAt){throw 'L''enseignant doit signer son entrée avant de signer sa sortie.'}
        $teacherId=[string]$selected.Teacher.Id;$revision=[int]$State.Revision;$actor=Get-ScoliaFeatureActor
        [void](Invoke-ScoliaFeatureUpdate -Name 'personnel' -Change {param($document)Set-ScoliaTeacherPunch -Document $document -TeacherId $teacherId -Direction $Direction -Now $now -Actor $actor -ExpectedRevision $revision})
        [void](Update-ScoliaTeacherAttendanceDialog $State)
        $notice='Signature '+$(if($Direction -eq 'Entry'){'d''entrée'}else{'de sortie'})+' enregistrée à '+$now.ToString('HH:mm:ss')+' pour '+$selected.Teacher.Nom+'.'
        if($Direction -eq 'Entry' -and $now.TimeOfDay -ge [timespan]::FromHours(8)){$notice+=' Retard enregistré : l''entrée devait être signée avant 08:00.'}
        Set-ScoliaPersonnelInterfaceMessage $State $notice
    }catch{[void](Update-ScoliaTeacherAttendanceDialog $State);Set-ScoliaPersonnelInterfaceMessage $State $_.Exception.Message $true}
}
function Show-ScoliaTeacherAttendance {
    Assert-ITMPermission 'Lire';$state=New-ScoliaTeacherAttendanceDialog -Context (Get-ScoliaFeatureContext)
    [void](Update-ScoliaTeacherAttendanceDialog $state)
    if((Test-Path function:Mount-ScoliaWorkspaceForm) -and (Mount-ScoliaWorkspaceForm $state.Form)){return}
    try{[void]$state.Form.ShowDialog($script:main)}finally{$state.Form.Dispose()}
}
function Show-ScoliaTeacherJustification([object]$AttendanceState) {
    $selected=Get-ScoliaSelectedTeacherRow $AttendanceState
    if($selected.Status.SignatureCount -eq 2 -and -not [bool](Get-ScoliaPersonnelOptionalField $selected.Status 'LateRecorded' $false)){throw 'Cet enseignant a signé son entrée à l''heure et sa sortie : cette journée est présente.'}
    $dialog=New-ScoliaDialog 'Justification d absence' 590 390;$dialog.MinimumSize=New-Object Drawing.Size(590,390)
    $panel=New-Object Windows.Forms.Panel;$panel.Dock='Fill';$panel.Padding=New-Object Windows.Forms.Padding(22);$dialog.Controls.Add($panel)
    $title=New-Label $panel ($selected.Teacher.Nom+' · '+$AttendanceState.LoadedDate.ToString('dd/MM/yyyy')) 22 18 500 45 12 $true;$title.Anchor='Top,Left,Right'
    $hintText='Une absence justifiée reste comptée, avec son motif, et ne réduit pas le salaire.'
    if($selected.Status.SignatureCount -eq 2){$hintText='Le motif protège l''absence équivalente uniquement si cette date est le troisième retard du groupe. Les heures et les retards restent comptés.'}
    $hint=New-Label $panel $hintText 22 66 500 42;$hint.Anchor='Top,Left,Right'
    $reason=New-Object Windows.Forms.TextBox;$reason.Multiline=$true;$reason.MaxLength=500;$reason.SetBounds(22,115,500,115);$reason.Anchor='Top,Bottom,Left,Right';$reason.Text=[string]$selected.Status.Reason;$reason.AccessibleName='Motif de justification';$panel.Controls.Add($reason)
    $save=New-Button $panel 'Enregistrer le motif' 330 249 192 $true;$save.Anchor='Bottom,Right'
    $revoke=New-Button $panel 'Annuler la justification' 22 249 235;$revoke.Anchor='Bottom,Left';$revoke.Visible=[bool]$selected.Status.Reason
    $errorLabel=New-Label $panel '' 22 288 500 40;$errorLabel.Anchor='Bottom,Left,Right';$errorLabel.Tag='scolia:feedback:error'
    $dialog.Tag=[pscustomobject]@{Attendance=$AttendanceState;Reason=$reason;OriginalReason=[string]$selected.Status.Reason;TeacherId=$selected.Teacher.Id;Date=$AttendanceState.LoadedDate;Revision=$AttendanceState.Revision;Error=$errorLabel}
    $save.Add_Click({param($sender,$e)$s=$sender.FindForm().Tag;try{
        Assert-ScoliaPersonnelInterfaceContext $s.Attendance 'Administrer' $true;$now=[datetimeoffset]::Now;$actor=Get-ScoliaFeatureActor
        [void](Invoke-ScoliaFeatureUpdate -Name 'personnel' -Change {param($document)Set-ScoliaTeacherJustification -Document $document -TeacherId $s.TeacherId -Date $s.Date -Reason $s.Reason.Text -Now $now -Actor $actor -ExpectedRevision $s.Revision})
        $sender.FindForm().DialogResult='OK';$sender.FindForm().Close()
    }catch{$s.Error.Text=$_.Exception.Message}})
    $revoke.Add_Click({param($sender,$e)$s=$sender.FindForm().Tag;try{
        Assert-ScoliaPersonnelInterfaceContext $s.Attendance 'Administrer' $true
        if([string]::IsNullOrWhiteSpace($s.Reason.Text) -or $s.Reason.Text.Trim() -ceq $s.OriginalReason.Trim()){throw 'Remplacez le texte par le motif de cette annulation avant de continuer.'}
        if([ITMMessageBox]::Show($sender.FindForm(),'Annuler cette justification ? Le motif initial et cette annulation restent dans l''historique ; le bilan de paie sera recalculé.','Annuler une justification','YesNo','Warning') -ne 'Yes'){return}
        $now=[datetimeoffset]::Now;$actor=Get-ScoliaFeatureActor
        [void](Invoke-ScoliaFeatureUpdate -Name 'personnel' -Change {param($document)Revoke-ScoliaTeacherJustification -Document $document -TeacherId $s.TeacherId -Date $s.Date -Reason $s.Reason.Text -Now $now -Actor $actor -ExpectedRevision $s.Revision})
        $sender.FindForm().DialogResult='OK';$sender.FindForm().Close()
    }catch{$s.Error.Text=$_.Exception.Message}})
    try{Apply-ITMThemeForm $dialog;[void]$dialog.ShowDialog($AttendanceState.Form)}finally{$dialog.Dispose()}
}
function Show-ScoliaTeacherManager([Windows.Forms.Form]$Owner) {
    Assert-ITMPermission 'Administrer';$context=Get-ScoliaFeatureContext
    $state=New-ScoliaFeatureDialog -Title 'Enseignants' -Subtitle 'Les dates d''activité déterminent les jours admissibles. Les archives et signatures sont conservées.' -Width 860 -Height 590
    $state|Add-Member NoteProperty Context $context;$state|Add-Member NoteProperty Revision 0;$state|Add-Member NoteProperty Teachers @()
    Add-ScoliaPersonnelColumn $state.Grid 'Nom' 'Enseignant' 300;Add-ScoliaPersonnelColumn $state.Grid 'From' 'Actif depuis' 140;Add-ScoliaPersonnelColumn $state.Grid 'To' 'Actif jusqu''au' 140
    $add=New-Button $state.Toolbar 'Ajouter' 0 0 115 $true;$edit=New-Button $state.Toolbar 'Modifier / archiver' 0 0 190;$refresh=New-Button $state.Toolbar 'Actualiser' 0 0 120
    $add.Add_Click({param($sender,$e)$s=$sender.FindForm().Tag;try{Show-ScoliaTeacherEditor -ManagerState $s;Update-ScoliaTeacherManager $s}catch{Set-ScoliaPersonnelInterfaceMessage $s $_.Exception.Message $true}})
    $edit.Add_Click({param($sender,$e)$s=$sender.FindForm().Tag;try{$teacher=Get-ScoliaSelectedTeacherRow $s;Show-ScoliaTeacherEditor -ManagerState $s -Teacher $teacher;Update-ScoliaTeacherManager $s}catch{Set-ScoliaPersonnelInterfaceMessage $s $_.Exception.Message $true}})
    $refresh.Add_Click({param($sender,$e)Update-ScoliaTeacherManager $sender.FindForm().Tag})
    $charges=New-Button $state.Toolbar 'Cours et charge horaire' 0 0 225
    $charges.Add_Click({param($sender,$e)try{Show-ScoliaTeachingManager Dashboard $sender.FindForm()}catch{Set-ScoliaPersonnelInterfaceMessage $sender.FindForm().Tag $_.Exception.Message $true}})
    try{Update-ScoliaTeacherManager $state;[void]$state.Form.ShowDialog($Owner)}finally{$state.Form.Dispose()}
}
function Update-ScoliaTeacherManager([object]$State) {
    try{Assert-ScoliaPersonnelInterfaceContext $State 'Administrer';$document=Read-ScoliaFeatureStore -Name 'personnel';Assert-ScoliaPersonnelDocument $document
        $State.Grid.Rows.Clear();$State.Teachers=@($document.Items|Where-Object {$_.Kind -eq 'Teacher'}|Sort-Object Nom,Id);$State.Revision=[int]$document.Revision
        foreach($teacher in $State.Teachers){$i=$State.Grid.Rows.Add($teacher.Nom,$teacher.ActiveFrom,$teacher.ActiveTo);$State.Grid.Rows[$i].Tag=$teacher}
        Set-ScoliaPersonnelInterfaceMessage $State ([string]::Format('{0} enseignant(s). Pour archiver, indiquez le dernier jour d''activité ; aucun historique n''est supprimé.',$State.Teachers.Count))
    }catch{Set-ScoliaPersonnelInterfaceMessage $State $_.Exception.Message $true}
}
function Show-ScoliaTeacherEditor([object]$ManagerState,[object]$Teacher=$null) {
    Assert-ScoliaPersonnelInterfaceContext $ManagerState 'Administrer' $true
    $dialog=New-ScoliaDialog $(if($Teacher){'Modifier un enseignant'}else{'Ajouter un enseignant'}) 610 455;$dialog.MinimumSize=New-Object Drawing.Size(520,400)
    $table=New-ScoliaTable $dialog
    $name=Add-ScoliaText $table 'Nom complet' $(if($Teacher){[string]$Teacher.Nom}else{''});$name.MaxLength=150;$name.AccessibleName='Nom complet de l''enseignant'
    $start=New-Object Windows.Forms.DateTimePicker;$start.Format='Short';$start.Value=$(if($Teacher){[datetime]::ParseExact($Teacher.ActiveFrom,'yyyy-MM-dd',[Globalization.CultureInfo]::InvariantCulture)}else{(Get-Date).Date});[void](Add-ScoliaField $table 'Début d''activité' $start)
    $end=New-Object Windows.Forms.DateTimePicker;$end.Format='Short';$end.ShowCheckBox=$true;$end.Checked=$false
    if($Teacher -and $Teacher.ActiveTo){$end.Value=[datetime]::ParseExact($Teacher.ActiveTo,'yyyy-MM-dd',[Globalization.CultureInfo]::InvariantCulture);$end.Checked=$true};[void](Add-ScoliaField $table 'Dernier jour actif (optionnel)' $end)
    $note=New-Object Windows.Forms.Label;$note.AutoSize=$true;$note.MaximumSize=New-Object Drawing.Size(320,0);$note.Text='Une date de fin archive la fiche après ce jour. Les signatures et salaires précédents restent consultables.';[void](Add-ScoliaField $table 'Historique' $note)
    $errorLabel=New-Object Windows.Forms.Label;$errorLabel.AutoSize=$true;$errorLabel.MaximumSize=New-Object Drawing.Size(320,0);$errorLabel.Tag='scolia:feedback:error';[void](Add-ScoliaField $table '' $errorLabel)
    $save=New-Button $table 'Enregistrer' 0 0 180 $true;[void](Add-ScoliaField $table '' $save)
    $dialog.Tag=[pscustomobject]@{Manager=$ManagerState;Teacher=$Teacher;Name=$name;Start=$start;End=$end;Error=$errorLabel;Revision=$ManagerState.Revision}
    $save.Add_Click({param($sender,$e)$s=$sender.FindForm().Tag;try{
        Assert-ScoliaPersonnelInterfaceContext $s.Manager 'Administrer' $true;$now=[datetimeoffset]::Now;$actor=Get-ScoliaFeatureActor
        $params=@{Nom=$s.Name.Text;ActiveFrom=$s.Start.Value.Date.ToString('yyyy-MM-dd');ActiveTo=$(if($s.End.Checked){$s.End.Value.Date.ToString('yyyy-MM-dd')}else{''});Now=$now;Actor=$actor}
        if($s.Teacher){$params.Id=[string]$s.Teacher.Id};$teacher=New-ScoliaTeacher @params
        [void](Invoke-ScoliaFeatureUpdate -Name 'personnel' -Change {param($document)Set-ScoliaTeacher -Document $document -Teacher $teacher -ExpectedRevision $s.Revision -Now $now -Actor $actor})
        $sender.FindForm().DialogResult='OK';$sender.FindForm().Close()
    }catch{$s.Error.Text=$_.Exception.Message}})
    try{Apply-ITMThemeForm $dialog;[void]$dialog.ShowDialog($ManagerState.Form)}finally{$dialog.Dispose()}
}
function New-ScoliaMonthlyPayrollDialog([object]$Context,[datetime]$Month=(Get-Date)) {
    $state=New-ScoliaFeatureDialog -Title 'Paie mensuelle des enseignants' -Subtitle 'Montants en FC · calendrier du premier au dernier jour · aperçu avant finalisation' -Width 1200 -Height 750
    foreach($entry in (@{Context=$Context;Loading=$true;SalaryDocument=$null;PersonnelDocument=$null;Calendar=$null;MonthKey='';PreviewRows=@();SalaryRevision=0}).GetEnumerator()){$state|Add-Member NoteProperty $entry.Key $entry.Value}
    $state.Form.MinimumSize=New-Object Drawing.Size(600,510);$state.Toolbar.Height=152;$state.Message.Height=100;$state.Grid.AccessibleName='Calcul mensuel des salaires en FC'
    $monthPicker=New-Object Windows.Forms.DateTimePicker;$monthPicker.Format='Custom';$monthPicker.CustomFormat='MMMM yyyy';$monthPicker.ShowUpDown=$true;$monthPicker.Width=170;$monthPicker.AccessibleName='Mois de paie';$state.Toolbar.Controls.Add($monthPicker)
    $bounds=Get-ScoliaAttendanceYearBounds $Context.Year;$monthPicker.MinDate=$bounds.Start;$monthPicker.MaxDate=$bounds.End
    $first=New-Object datetime($Month.Year,$Month.Month,1);$monthPicker.Value=$(if($first -lt $bounds.Start){$bounds.Start}elseif($first -gt $bounds.End){$bounds.End}else{$first})
    $calendar=New-Button $state.Toolbar 'Jours ouvrables' 0 0 170;$calendar.Name='PaieCalendrier'
    # Compatibilité de l''état de fenêtre : la règle métier reste fixe et n''est pas un choix utilisateur.
    $policy=New-Object Windows.Forms.CheckBox;$policy.Checked=$true;$policy.Enabled=$false;$policy.Visible=$false;$state.Toolbar.Controls.Add($policy)
    $policyNotice=New-Object Windows.Forms.Label;$policyNotice.AutoSize=$false;$policyNotice.Size=New-Object Drawing.Size(330,34);$policyNotice.Text='Absences justifiées : comptées, sans retenue';$policyNotice.Margin=New-Object Windows.Forms.Padding(8,9,8,0);$policyNotice.AccessibleName=$policyNotice.Text;$state.Toolbar.Controls.Add($policyNotice)
    $refresh=New-Button $state.Toolbar 'Actualiser' 0 0 120
    $teacher=New-Object Windows.Forms.ComboBox;$teacher.DropDownStyle='DropDownList';$teacher.DisplayMember='Nom';$teacher.ValueMember='Id';$teacher.Width=220;$teacher.AccessibleName='Enseignant dont le salaire est à renseigner';$state.Toolbar.Controls.Add($teacher)
    $amount=New-Object Windows.Forms.NumericUpDown;$amount.Maximum=[decimal]([long]::MaxValue);$amount.DecimalPlaces=0;$amount.ThousandsSeparator=$true;$amount.Width=150;$amount.Height=36;$amount.AccessibleName='Salaire mensuel en FC';$state.Toolbar.Controls.Add($amount)
    $save=New-Button $state.Toolbar 'Enregistrer salaire FC' 0 0 220 $true;$save.Name='PaieEnregistrerSalaire'
    $finalize=New-Button $state.Toolbar 'Finaliser le mois' 0 0 170;$finalize.Name='PaieFinaliser'
    $export=New-Button $state.Toolbar 'Exporter CSV' 0 0 140;$export.Name='PaieExporter'
    $detail=New-Button $state.Toolbar 'Détail du mois' 0 0 160;$detail.Name='PaieDetail'
    foreach($entry in (@{MonthPicker=$monthPicker;CalendarButton=$calendar;PolicyPicker=$policy;PolicyNotice=$policyNotice;TeacherPicker=$teacher;AmountPicker=$amount;SaveSalaryButton=$save;FinalizeButton=$finalize;ExportButton=$export}).GetEnumerator()){$state|Add-Member NoteProperty $entry.Key $entry.Value}
    foreach($c in @(@('Nom','Enseignant',200),@('Gross','Salaire FC',110),@('WorkDays','Jours ouvrables',90),@('Eligible','Jours actifs',85),@('Present','Présents (2 signatures)',125),@('Late','Retards',75),@('LateAbsences','Abs. équiv. retards',125),@('Unjustified','Abs. non just.',100),@('Justified','Abs. just.',95),@('Pending','À terminer',95),@('Rate','Taux / jour FC',115),@('Deductions','Retenues constatées FC',130),@('Net','Net projeté / final FC',140),@('State','État du calcul',200))){Add-ScoliaPersonnelColumn $state.Grid $c[0] $c[1] $c[2]}
    $state.Grid.Columns['Present'].HeaderCell.ToolTipText='Jours avec entrée et sortie signées, y compris les jours en retard.'
    $state.Grid.Columns['Late'].HeaderCell.ToolTipText='Entrées à partir de 08:00 sur jours ouvrables terminés. Une absence physique justifiée est exclue des groupes de retard.'
    $state.Grid.Columns['LateAbsences'].HeaderCell.ToolTipText='Trois retards dans une même semaine : une absence équivalente au troisième jour. Un jour déjà absent ne subit pas une deuxième retenue.'
    $state.Grid.Columns['Unjustified'].HeaderCell.ToolTipText='Absences physiques non justifiées : signatures manquantes après la fin du jour.'
    $state.Grid.Columns['Justified'].HeaderCell.ToolTipText='Absences physiques justifiées : elles restent comptées et ne réduisent pas le salaire.'
    $monthPicker.Add_ValueChanged({param($sender,$e)$s=$sender.FindForm().Tag;if(-not $s.Loading){[void](Update-ScoliaMonthlyPayrollDialog $s)}})
    $teacher.Add_SelectedIndexChanged({param($sender,$e)$s=$sender.FindForm().Tag;if(-not $s.Loading){Update-ScoliaSalaryEditor $s}})
    $calendar.Add_Click({param($sender,$e)Save-ScoliaPayrollCalendar $sender.FindForm().Tag})
    $save.Add_Click({param($sender,$e)Save-ScoliaTeacherMonthlySalary $sender.FindForm().Tag})
    $refresh.Add_Click({param($sender,$e)[void](Update-ScoliaMonthlyPayrollDialog $sender.FindForm().Tag)})
    $finalize.Add_Click({param($sender,$e)Complete-ScoliaPayrollMonth $sender.FindForm().Tag})
    $export.Add_Click({param($sender,$e)Export-ScoliaPayrollMonth $sender.FindForm().Tag})
    $detail.Add_Click({param($sender,$e)$s=$sender.FindForm().Tag;try{Show-ScoliaPayrollDayDetail -PayrollState $s}catch{Set-ScoliaPersonnelInterfaceMessage $s $_.Exception.Message $true}})
    $state.Grid.Add_SelectionChanged({param($sender,$e)$s=$sender.FindForm().Tag;if(-not $s.Loading -and $sender.CurrentRow -and $sender.CurrentRow.Tag){$id=[string]$sender.CurrentRow.Tag.Teacher.Id;for($i=0;$i -lt $s.TeacherPicker.Items.Count;$i++){if($s.TeacherPicker.Items[$i].Id -eq $id){$s.TeacherPicker.SelectedIndex=$i;break}}}})
    $state.Loading=$false
    return $state
}
function Update-ScoliaSalaryEditor([object]$State) {
    $teacher=$State.TeacherPicker.SelectedItem
    $amount=[decimal]0;$locked=$false
    if($teacher){
        $salary=@($State.SalaryDocument.Items|Where-Object {$_.Kind -eq 'Salary' -and $_.Month -eq $State.MonthKey -and $_.TeacherId -eq $teacher.Id})
        if($salary.Count -eq 1){$amount=[decimal]$salary[0].GrossFC}
        $locked=@($State.SalaryDocument.Items|Where-Object {$_.Kind -eq 'Payroll' -and $_.Month -eq $State.MonthKey -and $_.TeacherId -eq $teacher.Id}).Count -gt 0
    }
    $State.AmountPicker.Value=$amount;$enabled=[bool]$teacher -and -not $locked -and -not $script:data.closed -and (Test-ITMPermission 'Administrer')
    $State.AmountPicker.Enabled=$enabled;$State.SaveSalaryButton.Enabled=$enabled
}
function Update-ScoliaMonthlyPayrollDialog([object]$State) {
    try{
        Assert-ScoliaPersonnelInterfaceContext $State 'Administrer'
        $salaryDocument=Read-ScoliaFeatureStore -Name 'salaires';Assert-ScoliaSalaryDocument $salaryDocument
        $personnel=Read-ScoliaFeatureStore -Name 'personnel';Assert-ScoliaPersonnelDocument $personnel
        $month=$State.MonthPicker.Value.ToString('yyyy-MM');$first=New-Object datetime($State.MonthPicker.Value.Year,$State.MonthPicker.Value.Month,1);$last=$first.AddMonths(1).AddDays(-1)
        $calendars=@($salaryDocument.Items|Where-Object {$_.Kind -eq 'Calendar' -and $_.Month -eq $month});$calendar=$(if($calendars.Count -eq 1){$calendars[0]}else{$null})
        $teachers=@($personnel.Items|Where-Object {$_.Kind -eq 'Teacher' -and [datetime]::ParseExact($_.ActiveFrom,'yyyy-MM-dd',[Globalization.CultureInfo]::InvariantCulture) -le $last -and (-not $_.ActiveTo -or [datetime]::ParseExact($_.ActiveTo,'yyyy-MM-dd',[Globalization.CultureInfo]::InvariantCulture) -ge $first)}|Sort-Object Nom,Id)
        # Une semaine peut commencer dans le mois précédent ou finir dans le suivant.
        $weekFirst=$first.AddDays(-(([int]$first.DayOfWeek+6)%7));$weekLast=$last.AddDays(6-(([int]$last.DayOfWeek+6)%7))
        $weekFirstKey=$weekFirst.ToString('yyyy-MM-dd');$weekLastKey=$weekLast.ToString('yyyy-MM-dd');$yearCalendars=@($salaryDocument.Items|Where-Object {$_.Kind -eq 'Calendar'})
        $allPunches=@($personnel.Items|Where-Object {$_.Kind -eq 'Punch' -and $_.Date -ge $weekFirstKey -and $_.Date -le $weekLastKey});$allJustifications=@($personnel.Items|Where-Object {$_.Kind -eq 'Justification' -and $_.Date -ge $weekFirstKey -and $_.Date -le $weekLastKey})
        $oldTeacherId=$(if($State.TeacherPicker.SelectedItem){[string]$State.TeacherPicker.SelectedItem.Id}else{''});$rows=@();$finalized=0;$oldRuleSnapshots=0;$eligibleTeachers=0;$canFinalize=$calendar -and $teachers.Count -gt 0;$now=[datetimeoffset]::Now
        $State.Loading=$true
        try{
            $State.Grid.Rows.Clear();$State.TeacherPicker.Items.Clear()
            foreach($teacher in $teachers){
                [void]$State.TeacherPicker.Items.Add($teacher)
                $teacherPunches=@($allPunches|Where-Object {$_.TeacherId -eq $teacher.Id});$teacherJustifications=@($allJustifications|Where-Object {$_.TeacherId -eq $teacher.Id})
                $salaries=@($salaryDocument.Items|Where-Object {$_.Kind -eq 'Salary' -and $_.Month -eq $month -and $_.TeacherId -eq $teacher.Id})
                $snapshots=@($salaryDocument.Items|Where-Object {$_.Kind -eq 'Payroll' -and $_.Month -eq $month -and $_.TeacherId -eq $teacher.Id})
                $salary=$(if($salaries.Count -eq 1){$salaries[0]}else{$null})
                $preview=$(if($calendar -and $salary -and $snapshots.Count -eq 0){Get-ScoliaMonthlyTeacherPayroll -Teacher $teacher -Calendar $calendar -Punches $teacherPunches -Justifications $teacherJustifications -Salary $salary -AsOf $now -Calendars $yearCalendars}else{$null})
                $dailyRows=@();if($preview){$dailyRows=@($preview.Rows)}elseif($snapshots.Count -eq 1){$dailyRows=@($snapshots[0].Rows)}else{
                    for($date=$first;$date -le $last;$date=$date.AddDays(1)){$dailyRows+=@(Get-ScoliaTeacherDailyStatus -Teacher $teacher -Date $date -Punches $teacherPunches -Justifications $teacherJustifications -WorkDates @($(if($calendar){@($calendar.WorkDates)}else{@()})) -AsOf $now)}
                }
                $net='';$rate='';$deductions='';$caption='Calendrier à définir';$gross=$(if($salary){[decimal]$salary.GrossFC}else{''});$wd='';$eligible='';$present='';$unjustified='';$justified='';$pending='';$late='';$lateAbsences='';$lateJustified='';$lateUnjustified='';$extraLateDeductions='';$lateGroups=@()
                if($preview){
                    $wd=$preview.WorkDays;$eligible=$preview.EligibleWorkDays;$present=@($preview.Rows|Where-Object {$_.Code -in @('Present','Late')}).Count;$unjustified=$preview.UnjustifiedDays;$justified=$preview.JustifiedDays;$pending=$preview.PendingDays;$late=$preview.LateDays;$lateAbsences=$preview.LateAbsenceDays;$lateJustified=$preview.LateJustifiedAbsenceDays;$lateUnjustified=$preview.LateUnjustifiedAbsenceDays;$extraLateDeductions=Get-ScoliaPersonnelOptionalField $preview 'LateDeductibleDays' ''; $lateGroups=@($preview.LateGroups)
                    if($preview.CalculationAvailable){$rate=([decimal]$preview.DailyRateFC).ToString('N2',[Globalization.CultureInfo]::GetCultureInfo('fr-FR'));$deductions=[decimal]$preview.DeductionsToDateFC;$net=$(if($null -ne $preview.NetFinalFC){[decimal]$preview.NetFinalFC}else{[decimal]$preview.NetToDateFC})}
                    $caption=$(if(-not $salary){'Salaire à renseigner'}elseif(-not $preview.CalculationAvailable){[string]$preview.Message}elseif($preview.CanFinalize){'Prêt à finaliser'}elseif(-not $preview.SourceWeeksComplete){$preview.Message}else{'Provisoire · jours à terminer'})
                    if($preview.EligibleWorkDays -gt 0 -and -not $preview.CanFinalize){$canFinalize=$false}
                }elseif($calendar){
                    $wd=@($calendar.WorkDates).Count;$eligible=@($dailyRows|Where-Object {$_.IsActive -and $_.IsWorkday}).Count;$present=@($dailyRows|Where-Object {$_.Code -in @('Present','Late')}).Count;$late=@($dailyRows|Where-Object {$_.IsFinal -and $_.IsActive -and $_.IsWorkday -and [bool](Get-ScoliaPersonnelOptionalField $_ 'LateRecorded' $false) -and -not ($_.IsAbsent -and $_.IsJustified)}).Count
                    $lateGroups=@(Get-ScoliaPersonnelLateGroups -Teacher $teacher -First $first -Last $last -Calendars $yearCalendars -Punches $teacherPunches -Justifications $teacherJustifications -Year $State.Context.Year -StartMonth ([int]$calendar.StartMonth) -AsOf $now)
                    $lateAbsences=$lateGroups.Count;$lateJustified=@($lateGroups|Where-Object {$_.IsJustified}).Count;$lateUnjustified=@($lateGroups|Where-Object {-not $_.IsJustified}).Count
                    foreach($group in $lateGroups){$assigned=@($dailyRows|Where-Object {$_.Date -eq $group.AssignedDate});if($assigned.Count -eq 1){Set-ScoliaPersonnelLateDetail $assigned[0] $group}}
                    $extraLateDeductions=@($lateGroups|Where-Object {$group=$_;-not $group.IsJustified -and @($dailyRows|Where-Object {$_.Date -eq $group.AssignedDate -and $_.IsAbsent -and -not $_.IsJustified}).Count -eq 0}).Count
                    $unjustified=@($dailyRows|Where-Object {$_.IsFinal -and $_.Code -eq 'AbsenceUnjustified'}).Count;$justified=@($dailyRows|Where-Object {$_.IsFinal -and $_.Code -eq 'AbsenceJustified'}).Count;$pending=@($dailyRows|Where-Object {$_.IsActive -and $_.IsWorkday -and -not $_.IsFinal}).Count
                    $caption=$(if($eligible -gt 0){'Salaire à renseigner'}else{'Aucun jour ouvrable actif'});if($eligible -gt 0 -and $snapshots.Count -eq 0){$canFinalize=$false}
                }else{$canFinalize=$false}
                if($snapshots.Count -eq 1){
                    $snapshot=$snapshots[0];$finalized++;$caption='Finalisé · montant dû figé';$net=[decimal]$snapshot.NetFC;$gross=[decimal]$snapshot.GrossFC
                    $wd=$snapshot.WorkDays;$eligible=$snapshot.EligibleWorkDays;$present=@($snapshot.Rows|Where-Object {$_.Code -in @('Present','Late')}).Count;$unjustified=$snapshot.UnjustifiedDays;$justified=$snapshot.JustifiedDays;$pending=0
                    $rate=([decimal]::Parse([string]$snapshot.DailyRateFC,[Globalization.CultureInfo]::InvariantCulture)).ToString('N2',[Globalization.CultureInfo]::GetCultureInfo('fr-FR'));$deductions=[decimal]$snapshot.DeductionsFC;$dailyRows=@($snapshot.Rows)
                    # Les anciens relevés figés conservent leur calcul et n''inventent pas de compteurs de retard.
                    $late=Get-ScoliaPersonnelOptionalField $snapshot 'LateDays' '—';$lateAbsences=Get-ScoliaPersonnelOptionalField $snapshot 'LateAbsenceDays' '—';$lateJustified=Get-ScoliaPersonnelOptionalField $snapshot 'LateJustifiedAbsenceDays' '—';$lateUnjustified=Get-ScoliaPersonnelOptionalField $snapshot 'LateUnjustifiedAbsenceDays' '—';$lateGroups=@(Get-ScoliaPersonnelOptionalField $snapshot 'LateGroups' @());$extraLateDeductions=Get-ScoliaPersonnelOptionalField $snapshot 'LateDeductibleDays' '—'
                    if(-not $snapshot.PSObject.Properties['RuleVersion']){$caption='Finalisé V56 · règles historiques conservées';$oldRuleSnapshots++}
                }
                if([int]$eligible -gt 0){$eligibleTeachers++}
                $displayName=$(if($snapshots.Count -eq 1){[string]$snapshots[0].TeacherName}else{[string]$teacher.Nom})
                $index=$State.Grid.Rows.Add($displayName,$gross,$wd,$eligible,$present,$late,$lateAbsences,$unjustified,$justified,$pending,$rate,$deductions,$net,$caption)
                $row=[pscustomobject]@{Teacher=$teacher;Preview=$preview;DailyRows=$dailyRows;Snapshot=$(if($snapshots.Count -eq 1){$snapshots[0]}else{$null});Nom=$displayName;Month=$month;GrossFC=$gross;WorkDays=$wd;EligibleWorkDays=$eligible;PresentDays=$present;LateDays=$late;LateAbsenceDays=$lateAbsences;LateJustifiedAbsenceDays=$lateJustified;LateUnjustifiedAbsenceDays=$lateUnjustified;LateDeductibleDays=$extraLateDeductions;LateGroups=$lateGroups;UnjustifiedDays=$unjustified;JustifiedDays=$justified;PendingDays=$pending;DailyRateFC=$rate;DeductionsFC=$deductions;NetFC=$net;State=$caption}
                $State.Grid.Rows[$index].Tag=$row;$rows+=@($row)
            }
            $State.Calendar=$calendar;$State.SalaryDocument=$salaryDocument;$State.PersonnelDocument=$personnel;$State.SalaryRevision=[int]$salaryDocument.Revision;$State.MonthKey=$month;$State.PreviewRows=$rows
            $State.PolicyPicker.Checked=$true;$State.PolicyPicker.Enabled=$false;$State.PolicyPicker.Visible=$false
            if($State.TeacherPicker.Items.Count -gt 0){$selected=0;for($i=0;$i -lt $State.TeacherPicker.Items.Count;$i++){if($State.TeacherPicker.Items[$i].Id -eq $oldTeacherId){$selected=$i}};$State.TeacherPicker.SelectedIndex=$selected}
        }finally{$State.Loading=$false}
        $writable=-not $script:data.closed;$frozen=$finalized -gt 0;$State.CalendarButton.Enabled=$writable -and -not $frozen;$State.PolicyPicker.Enabled=$false
        $State.TeacherPicker.Enabled=$teachers.Count -gt 0;$State.FinalizeButton.Enabled=$writable -and [bool]$canFinalize -and $eligibleTeachers -gt 0 -and $finalized -lt $eligibleTeachers;$State.ExportButton.Enabled=(Test-ITMPermission 'Exporter') -and $rows.Count -gt 0
        Update-ScoliaSalaryEditor $State
        $days=$(if($calendar){@($calendar.WorkDates).Count}else{0})
        $note=$first.ToString('dd/MM/yyyy')+' au '+$last.ToString('dd/MM/yyyy')+' · '+$days+' jour(s) ouvrable(s) · '+$finalized+' paie(s) finalisée(s). Trois retards terminés par semaine : 1 absence équivalente au troisième jour, sans double retenue. Le net reste projeté jusqu''à la fin du mois, sans versement.'
        if($oldRuleSnapshots -gt 0){$note+=' Les paies finalisées V56 conservent leurs règles historiques.'}
        Set-ScoliaPersonnelInterfaceMessage $State $note
        return $true
    }catch{
        $State.Loading=$true;try{$State.Grid.Rows.Clear();$State.TeacherPicker.Items.Clear();$State.AmountPicker.Value=0;$State.SalaryDocument=$null;$State.PersonnelDocument=$null;$State.Calendar=$null;$State.PreviewRows=@()}finally{$State.Loading=$false}
        $State.TeacherPicker.Enabled=$false;$State.AmountPicker.Enabled=$false;$State.CalendarButton.Enabled=$false;$State.PolicyPicker.Enabled=$false;$State.FinalizeButton.Enabled=$false;$State.SaveSalaryButton.Enabled=$false;$State.ExportButton.Enabled=$false
        Set-ScoliaPersonnelInterfaceMessage $State $_.Exception.Message $true;return $false
    }
}
function Save-ScoliaPayrollCalendar([object]$State) {
    try{
        Assert-ScoliaPersonnelInterfaceContext $State 'Administrer' $true;$month=$State.MonthPicker.Value;$old=$State.Calendar;$dates=Show-ScoliaMonthCalendar -Month $month -SelectedDates @($(if($old){@($old.WorkDates)}else{@()}))
        if($null -eq $dates){return}
        $actor=Get-ScoliaFeatureActor;$calendar=New-ScoliaMonthlyCalendar -Month $month.ToString('yyyy-MM') -Year $State.Context.Year -ContextId $State.Context.Id -WorkDates @($dates) -JustifiedPaid $true -Actor $actor -StartMonth $(if($old){[int]$old.StartMonth}else{(Get-ScoliaAttendanceYearBounds $State.Context.Year).StartMonth})
        if($old){$difference=Get-ScoliaCalendarDifference -Previous $old -Proposed $calendar
            $prompt='Modifier ce calendrier recalculera les aperçus : '+@($difference.Added).Count+' jour(s) ajouté(s), '+@($difference.Removed).Count+' retiré(s).'
            if($difference.PaymentRuleChanged){$prompt+=' Les absences justifiées seront désormais comptées sans retenue.'}
            if([ITMMessageBox]::Show($State.Form,$prompt,'Confirmer le calendrier','YesNo','Question') -ne 'Yes'){return}
        }
        $revision=[int]$State.SalaryRevision
        [void](Invoke-ScoliaFeatureUpdate -Name 'salaires' -Change {param($document)Set-ScoliaMonthlyCalendar -Document $document -Calendar $calendar -ExpectedRevision $revision})
        [void](Update-ScoliaMonthlyPayrollDialog $State)
    }catch{Set-ScoliaPersonnelInterfaceMessage $State $_.Exception.Message $true}
}
function Save-ScoliaTeacherMonthlySalary([object]$State) {
    try{Assert-ScoliaPersonnelInterfaceContext $State 'Administrer' $true;$teacher=$State.TeacherPicker.SelectedItem;if(-not $teacher){throw 'Sélectionnez un enseignant.'}
        $salary=New-ScoliaMonthlySalary -Month $State.MonthKey -Year $State.Context.Year -ContextId $State.Context.Id -TeacherId $teacher.Id -GrossFC $State.AmountPicker.Value -Actor (Get-ScoliaFeatureActor) -StartMonth $(if($State.Calendar){[int]$State.Calendar.StartMonth}else{(Get-ScoliaAttendanceYearBounds $State.Context.Year).StartMonth})
        $revision=[int]$State.SalaryRevision
        [void](Invoke-ScoliaFeatureUpdate -Name 'salaires' -Change {param($document)Set-ScoliaMonthlySalary -Document $document -Salary $salary -ExpectedRevision $revision})
        [void](Update-ScoliaMonthlyPayrollDialog $State);Set-ScoliaPersonnelInterfaceMessage $State ('Salaire mensuel enregistré pour '+$teacher.Nom+' : '+$salary.GrossFC+' FC. Le montant net tient compte des absences selon le calendrier.')
    }catch{Set-ScoliaPersonnelInterfaceMessage $State $_.Exception.Message $true}
}
function Complete-ScoliaPayrollMonth([object]$State) {
    try{Assert-ScoliaPersonnelInterfaceContext $State 'Administrer' $true
        if(-not (Update-ScoliaMonthlyPayrollDialog $State) -or -not $State.FinalizeButton.Enabled){throw 'Le mois ne peut pas être finalisé : vérifiez le calendrier, les salaires et la fin de toutes les journées.'}
        $prompt='Finaliser '+$State.MonthKey+' figera les montants dus, le calendrier et les signatures utilisés. Cette action ne verse aucun salaire. Confirmez après vérification du bilan.'
        if([ITMMessageBox]::Show($State.Form,$prompt,'Finaliser la paie mensuelle','YesNo','Warning') -ne 'Yes'){return}
        [void](Invoke-ScoliaFinalizeMonthlyPayroll -Month $State.MonthKey)
        [void](Update-ScoliaMonthlyPayrollDialog $State);Set-ScoliaPersonnelInterfaceMessage $State 'Mois finalisé. Les montants dus sont figés ; les versements restent à enregistrer dans votre procédure comptable.'
    }catch{Set-ScoliaPersonnelInterfaceMessage $State $_.Exception.Message $true}
}
function Export-ScoliaPayrollMonth([object]$State) {
    try{Assert-ScoliaPersonnelInterfaceContext $State 'Administrer';Assert-ITMPermission 'Exporter';if(-not (Update-ScoliaMonthlyPayrollDialog $State)){return}
        if($State.PreviewRows.Count -eq 0){throw 'Aucun enseignant à exporter pour ce mois.'}
        $picker=New-Object Windows.Forms.SaveFileDialog
        try{$picker.Filter='Fichier CSV (*.csv)|*.csv';$picker.FileName='Paie_enseignants_'+$State.MonthKey+'.csv'
            if($picker.ShowDialog($State.Form) -eq 'OK'){
                $csv=@($State.PreviewRows|Select-Object Nom,Month,GrossFC,WorkDays,EligibleWorkDays,PresentDays,LateDays,LateAbsenceDays,LateJustifiedAbsenceDays,LateUnjustifiedAbsenceDays,LateDeductibleDays,UnjustifiedDays,JustifiedDays,PendingDays,DailyRateFC,DeductionsFC,NetFC,State|ConvertTo-Csv -NoTypeInformation -Delimiter ';') -join "`r`n"
                [IO.File]::WriteAllText($picker.FileName,$csv+"`r`n",(New-Object Text.UTF8Encoding($true)));Set-ScoliaPersonnelInterfaceMessage $State ('Bilan exporté : '+$picker.FileName)
            }
        }finally{$picker.Dispose()}
    }catch{Set-ScoliaPersonnelInterfaceMessage $State $_.Exception.Message $true}
}
function Show-ScoliaMonthlyPayroll([Windows.Forms.Form]$Owner,[datetime]$Month=(Get-Date)) {
    Assert-ITMPermission 'Administrer';$state=New-ScoliaMonthlyPayrollDialog -Context (Get-ScoliaFeatureContext) -Month $Month
    [void](Update-ScoliaMonthlyPayrollDialog $state)
    if((Test-Path function:Mount-ScoliaWorkspaceForm) -and (Mount-ScoliaWorkspaceForm $state.Form)){return}
    try{if($Owner){[void]$state.Form.ShowDialog($Owner)}else{[void]$state.Form.ShowDialog($script:main)}}finally{$state.Form.Dispose()}
}
function Show-ScoliaPayrollDayDetail([object]$PayrollState) {
    Assert-ScoliaPersonnelInterfaceContext $PayrollState 'Administrer'
    $selected=Get-ScoliaSelectedTeacherRow $PayrollState
    $state=New-ScoliaFeatureDialog -Title ('Détail mensuel · '+$selected.Nom) -Subtitle ($PayrollState.MonthKey+' · tous les jours du mois, y compris les jours non ouvrables') -Width 1040 -Height 680
    $state|Add-Member NoteProperty Context $PayrollState.Context;$state.Toolbar.Height=8;$state.Message.Height=96
    foreach($c in @(@('Date','Date',115),@('Workday','Ouvrable',90),@('Entry','Heure entrée',120),@('Exit','Heure sortie',120),@('Signatures','Signatures',85),@('Late','Retard',110),@('LateAbsence','Abs. équivalente par retards',245),@('Status','Situation',245),@('Reason','Motif / détail',360))){Add-ScoliaPersonnelColumn $state.Grid $c[0] $c[1] $c[2]}
    foreach($day in @($selected.DailyRows)){
        $late=$(if([bool](Get-ScoliaPersonnelOptionalField $day 'LateRecorded' $false)){'Oui'}elseif([bool](Get-ScoliaPersonnelOptionalField $day 'IsLate' $false)){'Entrée attendue'}else{''})
        $lateAbsence='';$reason=[string]$day.Reason
        if([bool](Get-ScoliaPersonnelOptionalField $day 'LatePenalty' $false)){
            $lateAbsence=$(if([bool](Get-ScoliaPersonnelOptionalField $day 'LatePenaltyJustified' $false)){'1 · justifiée, sans retenue'}else{'1 · non justifiée, sans double retenue'})
            $convertedDates=@(Get-ScoliaPersonnelOptionalField $day 'ConvertedDates' @());$dateCaption=@($convertedDates|ForEach-Object {[datetime]::ParseExact([string]$_,'yyyy-MM-dd',[Globalization.CultureInfo]::InvariantCulture).ToString('dd/MM')}) -join ', '
            $detail='Trois retards : '+$dateCaption+'.';$penaltyReason=[string](Get-ScoliaPersonnelOptionalField $day 'LatePenaltyReason' '')
            if($penaltyReason){$detail+=' Motif : '+$penaltyReason+'.'}
            $reason=(@($reason,$detail)|Where-Object {-not [string]::IsNullOrWhiteSpace($_)}) -join ' '
        }
        [void]$state.Grid.Rows.Add([datetime]::ParseExact($day.Date,'yyyy-MM-dd',[Globalization.CultureInfo]::InvariantCulture).ToString('dd/MM/yyyy'),$(if($day.IsWorkday){'Oui'}else{'Non'}),(Format-ScoliaPersonnelTime $day.EntryAt),(Format-ScoliaPersonnelTime $day.ExitAt),$day.SignatureCount,$late,$lateAbsence,$day.Status,$reason)
    }
    $caption=$(if($selected.Snapshot){'Historique figé à la finalisation de la paie.'}elseif(-not $PayrollState.Calendar){'Définissez le calendrier pour classer les jours et calculer les absences.'}else{'Entrée avant 08:00. Trois retards terminés par semaine (lundi à dimanche) valent une absence au troisième jour, sans double retenue. Une absence justifiée reste comptée sans retenue. Les jours futurs restent à venir.'})
    Set-ScoliaPersonnelInterfaceMessage $state $caption
    try{[void]$state.Form.ShowDialog($PayrollState.Form)}finally{$state.Form.Dispose()}
}
