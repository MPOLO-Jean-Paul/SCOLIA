# Pointage et paie : coordination des fichiers sous deux verrous, dans un ordre constant.
function Get-ScoliaFeatureActor {
 $id=[string]$script:accountSession.Id;$name=[string]$script:accountSession.Name;$role=[string]$script:accountSession.Role
 if(-not $id){$id=[Environment]::UserName};if(-not $name){$name=[string]$script:scoliaSession.Name};if(-not $role){$role=[string]$script:scoliaSession.Role}
 $actor=($id+' | '+$name+' | '+$role).Trim();return $actor.Substring(0,[Math]::Min(160,$actor.Length))
}
function Get-ScoliaPersonnelActor {return Get-ScoliaFeatureActor}
function Get-ScoliaFinalizedPersonnelFingerprint($Personnel,$Salaries,[string]$Month){
 $calendar=@($Salaries.Items|Where-Object {$_.Kind -eq 'Calendar' -and $_.Month -ceq $Month})
 if($calendar.Count -ne 1){throw 'Calendrier du mois finalisé introuvable ou en double.'}
 $dates=@($calendar[0].WorkDates|Sort-Object);$teachers=@()
 foreach($teacher in @($Personnel.Items|Where-Object {$_.Kind -eq 'Teacher'}|Sort-Object Id)){
  $eligible=@($dates|Where-Object {Test-ScoliaTeacherActive $teacher $_})
  if($eligible.Count){$teachers+=([pscustomobject]@{Id=$teacher.Id;EligibleDates=$eligible})}
 }
 $ids=@($teachers|ForEach-Object {$_.Id})
 $records=@($Personnel.Items|Where-Object {$_.Kind -in @('Punch','Justification') -and $_.TeacherId -in $ids -and $_.Date -in $dates}|Sort-Object Kind,Id)
 return ([pscustomobject]@{Teachers=$teachers;Records=$records}|ConvertTo-Json -Depth 20 -Compress)
}
# Une semaine de retards peut commencer dans le mois précédent. Son périmètre
# est celui conservé dans la paie, même lors du contrôle du document modifié.
function Get-ScoliaFinalizedWeeklySourcesFingerprint($Personnel,$Salaries,[object[]]$Snapshots){
 $sources=@()
 foreach($snapshot in @($Snapshots|Where-Object {$_.PSObject.Properties['RuleVersion'] -and [int]$_.RuleVersion -eq 2}|Sort-Object Id)){
  $dates=@($snapshot.SourceDays|Sort-Object);$calendars=@();$workDates=@()
  foreach($sourceMonth in @($snapshot.SourceCalendars|ForEach-Object {$_.Month}|Sort-Object -Unique)){
   $matches=@($Salaries.Items|Where-Object {$_.Kind -eq 'Calendar' -and $_.Month -ceq $sourceMonth})
   if($matches.Count -ne 1){throw 'Un calendrier utilisé par une paie finalisée est absent ou en double.'}
   $calendar=$matches[0];$selected=@($calendar.WorkDates|Where-Object {$_ -cin $dates}|Sort-Object)
   $workDates+=@($selected)
   # Les dates antérieures au début de la semaine source restent modifiables.
   $calendars+=([pscustomobject][ordered]@{Month=$calendar.Month;Year=$calendar.Year;ContextId=$calendar.ContextId;StartMonth=$calendar.StartMonth;WorkDates=$selected})
  }
  $teachers=@($Personnel.Items|Where-Object {$_.Kind -eq 'Teacher' -and $_.Id -ceq $snapshot.TeacherId})
  if($teachers.Count -ne 1){throw 'Un enseignant utilisé par une paie finalisée est absent ou en double.'}
  $eligible=@($workDates|Where-Object {Test-ScoliaTeacherActive $teachers[0] $_}|Sort-Object -Unique)
  $records=@($Personnel.Items|Where-Object {$_.Kind -in @('Punch','Justification') -and $_.TeacherId -ceq $snapshot.TeacherId -and $_.Date -cin $dates}|Sort-Object Kind,Id)
  $sources+=([pscustomobject][ordered]@{PayrollId=$snapshot.Id;TeacherId=$snapshot.TeacherId;EligibleDates=$eligible;Calendars=$calendars;Records=$records})
 }
 return (ConvertTo-Json -InputObject @($sources) -Depth 32 -Compress)
}
function Assert-ScoliaFinalizedStaffUnchanged($BeforePersonnel,$AfterPersonnel,$BeforeSalaries,$AfterSalaries){
 foreach($month in @($BeforeSalaries.Items|Where-Object {$_.Kind -eq 'Payroll'}|ForEach-Object {$_.Month}|Select-Object -Unique)){
  if((Get-ScoliaFinalizedPersonnelFingerprint $BeforePersonnel $BeforeSalaries $month) -cne (Get-ScoliaFinalizedPersonnelFingerprint $AfterPersonnel $AfterSalaries $month)){throw ('La paie de '+$month+' est finalisée. Les présences et périodes d activité utilisées sont conservées.')}
  $frozenSources=@($BeforeSalaries.Items|Where-Object {$_.Kind -eq 'Payroll' -and $_.Month -ceq $month})
  if((Get-ScoliaFinalizedWeeklySourcesFingerprint $BeforePersonnel $BeforeSalaries $frozenSources) -cne (Get-ScoliaFinalizedWeeklySourcesFingerprint $AfterPersonnel $AfterSalaries $frozenSources)){throw ('La paie de '+$month+' est finalisée. Les jours, signatures et motifs des semaines utilisées sont conservés, y compris dans le mois précédent.')}
  $before=@($BeforeSalaries.Items|Where-Object {$_.Month -ceq $month}|Sort-Object Kind,Id)|ConvertTo-Json -Depth 20 -Compress
  $after=@($AfterSalaries.Items|Where-Object {$_.Month -ceq $month}|Sort-Object Kind,Id)|ConvertTo-Json -Depth 20 -Compress
  if($before -cne $after){throw ('Le mois '+$month+' est finalisé. Son calendrier et ses montants sont conservés.')}
 }
}
function Assert-ScoliaPunchesImmutable($Before,$After,[datetimeoffset]$Now=[datetimeoffset]::Now){
 foreach($old in @($Before.Items|Where-Object {$_.Kind -eq 'Punch'})){
  $match=@($After.Items|Where-Object {$_.Kind -eq 'Punch' -and $_.Id -ceq $old.Id})
  if($match.Count -ne 1){throw 'Une signature existante ne peut pas être supprimée.'}
  foreach($field in @('TeacherId','Date','EntryAt','EntryBy')){if($match[0].$field -cne $old.$field){throw 'Les informations et heures d entrée déjà enregistrées sont conservées.'}}
  if($old.ExitAt -and ($match[0].ExitAt -cne $old.ExitAt -or $match[0].ExitBy -cne $old.ExitBy)){throw 'La signature de sortie déjà enregistrée est conservée.'}
 }
 foreach($punch in @($After.Items|Where-Object {$_.Kind -eq 'Punch'})){
  $old=@($Before.Items|Where-Object {$_.Kind -eq 'Punch' -and $_.Id -ceq $punch.Id})
  $newStamps=@();if(-not $old.Count){$newStamps+=([string]$punch.EntryAt)}
  if($punch.ExitAt -and (-not $old.Count -or -not $old[0].ExitAt)){$newStamps+=([string]$punch.ExitAt)}
  foreach($stamp in $newStamps){$instant=ConvertTo-ScoliaPersonnelInstant $stamp;if($instant.Date -ne $Now.Date -or [Math]::Abs(($Now-$instant).TotalSeconds) -gt 30){throw 'Une nouvelle signature utilise uniquement l heure automatique du jour.'}}
 }
}
function Invoke-ScoliaStaffFeatureUpdate([ValidateSet('personnel','salaires')][string]$Name,[scriptblock]$Change){
 Assert-ITMPermission 'Lire'
 if(-not $script:store -or $script:data.closed){throw 'Connectez un dossier data scolaire ouvert avant de saisir le personnel ou la paie.'}
 $context=Get-ScoliaFeatureContext;$gates=@()
 try{
  foreach($storeName in @('personnel','salaires')){
   $path=Get-ScoliaFeatureStorePath -Name $storeName;[void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($path))
   try{$gates+=[IO.File]::Open($path+'.lock',[IO.FileMode]::OpenOrCreate,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None)}catch{throw 'Une signature, une sauvegarde ou un calcul de paie est en cours. Actualisez puis réessayez.'}
  }
  $personnel=Read-ScoliaFeatureStore -Name personnel;$salaries=Read-ScoliaFeatureStore -Name salaires
  Assert-ScoliaPersonnelDocument $personnel;Assert-ScoliaSalaryDocument $salaries
  $beforePersonnel=$personnel|ConvertTo-Json -Depth 20|ConvertFrom-ScoliaJson;$beforeSalaries=$salaries|ConvertTo-Json -Depth 20|ConvertFrom-ScoliaJson
  $document=$(if($Name -eq 'personnel'){$personnel}else{$salaries});$staffInitialRevisionV56=[int]$document.Revision
  [void](& $Change $document)
  if($Name -eq 'salaires'){Assert-ITMPermission 'Administrer'}else{
   $beforeAdmin=@($beforePersonnel.Items|Where-Object {$_.Kind -ne 'Punch'}|Sort-Object Kind,Id)|ConvertTo-Json -Depth 20 -Compress
   $afterAdmin=@($personnel.Items|Where-Object {$_.Kind -ne 'Punch'}|Sort-Object Kind,Id)|ConvertTo-Json -Depth 20 -Compress
   Assert-ITMPermission $(if($beforeAdmin -cne $afterAdmin){'Administrer'}else{'Saisie'})
  }
  Assert-ScoliaFeatureDocument $document $context
  Assert-ScoliaPersonnelDocument $personnel;Assert-ScoliaSalaryDocument $salaries
  Assert-ScoliaPunchesImmutable $beforePersonnel $personnel
  Assert-ScoliaFinalizedStaffUnchanged $beforePersonnel $personnel $beforeSalaries $salaries
  $teacherIds=@($personnel.Items|Where-Object {$_.Kind -eq 'Teacher'}|ForEach-Object {$_.Id})
  foreach($salary in @($salaries.Items|Where-Object {$_.Kind -in @('Salary','Payroll')})){if($salary.TeacherId -notin $teacherIds){throw 'Une ligne de salaire fait référence à un enseignant absent du registre.'}}
  foreach($punch in @($personnel.Items|Where-Object {$_.Kind -eq 'Punch'})){
   if(@($beforePersonnel.Items|Where-Object {$_.Kind -eq 'Punch' -and $_.Id -ceq $punch.Id}).Count){continue}
   $bounds=Get-ScoliaAttendanceYearBounds $context.Year;$date=ConvertTo-ScoliaPersonnelDate $punch.Date
   if($date -lt $bounds.Start -or $date -gt $bounds.End){throw 'Le jour de pointage ne correspond pas à l année scolaire connectée.'}
  }
  $current=Get-ScoliaFeatureContext;if($current.Id -cne $context.Id -or $current.Year -cne $context.Year -or $current.LocalKey -cne $context.LocalKey){throw 'La base, l année ou le compte ont changé. Aucune modification enregistrée.'}
  $document.Revision=$staffInitialRevisionV56+1;Write-ITMAtomicJson (Get-ScoliaFeatureStorePath -Name $Name) $document
  return $document
 }finally{for($i=$gates.Count-1;$i -ge 0;$i--){$gates[$i].Dispose()}}
}
function Invoke-ScoliaFinalizeMonthlyPayroll([string]$Month){
 Assert-ITMPermission 'Administrer'
 $actor=Get-ScoliaFeatureActor;$instant=[datetimeoffset]::Now
 return Invoke-ScoliaStaffFeatureUpdate -Name salaires -Change {param($document)
  if(@($document.Items|Where-Object {$_.Kind -eq 'Payroll' -and $_.Month -ceq $Month}).Count){throw 'Ce mois comporte déjà une paie finalisée. Consultez le relevé conservé.'}
  $personnel=Read-ScoliaFeatureStore -Name personnel
  $allCalendars=@($document.Items|Where-Object {$_.Kind -eq 'Calendar'})
  $calendars=@($allCalendars|Where-Object {$_.Month -ceq $Month})
  if($calendars.Count -ne 1){throw 'Déclarez d abord les jours ouvrables de ce mois.'};$calendar=$calendars[0]
  $punches=@($personnel.Items|Where-Object {$_.Kind -eq 'Punch'});$justifications=@($personnel.Items|Where-Object {$_.Kind -eq 'Justification'})
  $snapshots=@();$revision=[int]$document.Revision
  foreach($teacher in @($personnel.Items|Where-Object {$_.Kind -eq 'Teacher'}|Sort-Object Nom,Id)){
   if(-not @($calendar.WorkDates|Where-Object {Test-ScoliaTeacherActive $teacher $_}).Count){continue}
   $salaries=@($document.Items|Where-Object {$_.Kind -eq 'Salary' -and $_.Month -ceq $Month -and $_.TeacherId -ceq $teacher.Id})
   if($salaries.Count -ne 1){throw ('Renseignez le salaire mensuel de '+$teacher.Nom+' avant de finaliser.')}
   $preview=Get-ScoliaMonthlyTeacherPayroll -Teacher $teacher -Calendar $calendar -Calendars $allCalendars -Punches $punches -Justifications $justifications -Salary $salaries[0] -AsOf $instant
   if(-not $preview.CanFinalize){throw ('Le mois ne peut pas être finalisé pour '+$teacher.Nom+'. Vérifiez la fin du mois et les calendriers des semaines utilisées. '+$preview.Message)}
   $snapshots+=New-ScoliaPayrollSnapshot -Preview $preview -Actor $actor -PersonnelRevision ([int]$personnel.Revision) -SalaryRevision $revision
  }
  if(-not $snapshots.Count){throw 'Aucun enseignant actif sur les jours ouvrables de ce mois.'}
  foreach($snapshot in $snapshots){Set-ScoliaPayrollSnapshot -Document $document -Snapshot $snapshot -ExpectedRevision $revision}
 }
}
function Show-ScoliaMonthCalendar([datetime]$Month,[string[]]$SelectedDates=@()){
 $first=New-Object datetime($Month.Year,$Month.Month,1);$last=$first.AddMonths(1).AddDays(-1)
 $form=New-ScoliaDialog ('Jours ouvrables · '+$first.ToString('MM/yyyy')) 570 650
 $form.MinimumSize=New-Object Drawing.Size(380,380)
 $bar=New-Object Windows.Forms.FlowLayoutPanel;$bar.Dock='Top';$bar.Height=105;$bar.Padding=New-Object Windows.Forms.Padding(16)
 $note=New-Label $bar 'Cochez les dates réellement prestables. Retirez les congés et jours fériés.' 0 0 500 46;$note.AutoSize=$true
 $week=New-Button $bar 'Lundi à vendredi' 0 0 175;$week.AutoSize=$true
 $clear=New-Button $bar 'Tout décocher' 0 0 155;$clear.AutoSize=$true
 $list=New-Object Windows.Forms.CheckedListBox;$list.Dock='Fill';$list.CheckOnClick=$true;$list.IntegralHeight=$false;$list.Font=$form.Font;$list.AccessibleName='Dates ouvrables du mois'
 $footer=New-Object Windows.Forms.FlowLayoutPanel;$footer.Dock='Bottom';$footer.Height=95;$footer.Padding=New-Object Windows.Forms.Padding(16)
 $count=New-Label $footer '0 jour ouvrable' 0 0 460 24;$count.AutoSize=$true
 $save=New-Button $footer 'Confirmer les jours' 0 0 230 $true;$save.AutoSize=$true;$save.DialogResult='OK';$form.AcceptButton=$save
 $cancel=New-Button $footer 'Annuler' 0 0 140;$cancel.AutoSize=$true;$cancel.DialogResult='Cancel';$form.CancelButton=$cancel
 $dates=@();for($day=$first;$day -le $last;$day=$day.AddDays(1)){$iso=$day.ToString('yyyy-MM-dd');$dates+=$day;[void]$list.Items.Add($day.ToString('dddd dd/MM/yyyy'),($iso -in $SelectedDates))}
 $state=[pscustomobject]@{List=$list;Dates=$dates;Count=$count};$form.Tag=$state
 $list.Add_ItemCheck({param($sender,$e)$s=$sender.FindForm().Tag;$n=$sender.CheckedIndices.Count;if($e.CurrentValue -eq 'Checked'){$n--};if($e.NewValue -eq 'Checked'){$n++};$s.Count.Text=$n.ToString()+' jour(s) ouvrable(s) retenu(s)'})
 $week.Add_Click({param($sender,$e)$s=$sender.FindForm().Tag;for($i=0;$i -lt $s.Dates.Count;$i++){$s.List.SetItemChecked($i,($s.Dates[$i].DayOfWeek -notin @([DayOfWeek]::Saturday,[DayOfWeek]::Sunday)))}})
 $clear.Add_Click({param($sender,$e)$s=$sender.FindForm().Tag;for($i=0;$i -lt $s.Dates.Count;$i++){$s.List.SetItemChecked($i,$false)}})
 $form.Controls.Add($list);$form.Controls.Add($bar);$form.Controls.Add($footer);$list.BringToFront();$count.Text=$list.CheckedIndices.Count.ToString()+' jour(s) ouvrable(s) retenu(s)'
 try{Apply-ITMThemeForm $form;if($form.ShowDialog($script:main) -ne 'OK'){return $null};return ,@($list.CheckedIndices|ForEach-Object {$dates[[int]$_].ToString('yyyy-MM-dd')})}finally{$form.Dispose()}
}

function Assert-ScoliaPupilSignaturesImmutable($Before,$After,[datetimeoffset]$Now=[datetimeoffset]::Now){
 $old=@{};$new=@{}
 foreach($pair in @(@{Document=$Before;Map=$old},@{Document=$After;Map=$new})){
  foreach($sheet in @($pair.Document.Items)){
   foreach($entry in @($sheet.Entries)){
    if(-not $entry -or -not $entry.PSObject.Properties['SignedAt'] -or -not $entry.SignedAt){continue}
    $key=[string]$sheet.Date+'|'+(Get-ScoliaAttendanceKey $entry.Matricule)
    if($pair.Map.ContainsKey($key)){throw 'Une présence signée ne peut pas être dupliquée entre promotions.'}
    $pair.Map[$key]=$entry
   }
  }
 }
 foreach($key in @($old.Keys)){
  if(-not $new.ContainsKey($key)){throw 'Une présence signée ne peut pas être supprimée.'}
  foreach($field in @('SignedAt','Actor','Status')){if([string]$old[$key].$field -cne [string]$new[$key].$field){throw 'Les informations et heures d une présence signée sont conservées.'}}
 }
 foreach($key in @($new.Keys)){
  if($old.ContainsKey($key)){continue}
  $instant=ConvertTo-ScoliaPersonnelInstant ([string]$new[$key].SignedAt)
  if($instant.Date -ne $Now.Date -or [Math]::Abs(($Now-$instant).TotalSeconds) -gt 30){throw 'Une nouvelle présence utilise seulement l heure automatique du jour.'}
 }
}
