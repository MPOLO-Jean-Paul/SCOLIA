# Calendrier mensuel et paie en FC. Services métier purs, sans écriture Excel ni versement.
function Get-ScoliaSalaryMonth {
 param([string]$Month,[string]$Year,[int]$StartMonth=0)
 if($Month -notmatch '^\d{4}-\d{2}$'){throw 'Le mois doit utiliser le format AAAA-MM.'}
 try{$first=[datetime]::ParseExact($Month+'-01','yyyy-MM-dd',[Globalization.CultureInfo]::InvariantCulture)}catch{throw 'Mois invalide.'}
 $bounds=Get-ScoliaAttendanceYearBounds -Year $Year -StartMonth $StartMonth
 $last=$first.AddMonths(1).AddDays(-1)
 if($first -lt $bounds.Start -or $last -gt $bounds.End){throw 'Le mois doit appartenir à l''année scolaire connectée.'}
 return [pscustomobject]@{Month=$Month;First=$first.Date;Last=$last.Date;DayCount=[datetime]::DaysInMonth($first.Year,$first.Month);Year=$Year;StartMonth=$bounds.StartMonth}
}
function ConvertTo-ScoliaSalaryTeacherId([string]$TeacherId){
 $parsed=[guid]::Empty
 if(-not [guid]::TryParseExact($TeacherId,'D',[ref]$parsed) -or $parsed -eq [guid]::Empty){throw 'Identifiant enseignant invalide.'}
 return $parsed.ToString('D')
}
function Assert-ScoliaSalaryActor([string]$Actor){
 if([string]::IsNullOrWhiteSpace($Actor) -or $Actor.Length -gt 250){throw 'Le compte qui enregistre la modification est obligatoire.'}
}
function Get-ScoliaSalaryAmount([object]$GrossFC){
 if($null -eq $GrossFC -or $GrossFC -is [bool]){throw 'Indiquez un salaire mensuel en FC.'}
 try{$amount=[decimal]$GrossFC}catch{throw 'Le salaire doit être un montant numérique en FC.'}
 if($amount -le 0 -or $amount -ne [decimal]::Truncate($amount) -or $amount -gt [long]::MaxValue){throw 'Le salaire en FC doit être un nombre entier strictement positif.'}
 return [long]$amount
}
function New-ScoliaMonthlyCalendar {
 param([string]$Month,[string]$Year,[string]$ContextId,[AllowEmptyCollection()][string[]]$WorkDates=@(),[bool]$JustifiedPaid=$true,[string]$Actor,[int]$StartMonth=0)
 $window=Get-ScoliaSalaryMonth -Month $Month -Year $Year -StartMonth $StartMonth
 if([string]::IsNullOrWhiteSpace($ContextId)){throw 'Le contexte scolaire est obligatoire.'}
 Assert-ScoliaSalaryActor $Actor
 $seen=@{};$dates=New-Object 'System.Collections.Generic.List[string]'
 foreach($key in @($WorkDates)){
  if($key -notmatch '^\d{4}-\d{2}-\d{2}$'){throw 'Chaque jour ouvrable doit utiliser le format AAAA-MM-JJ.'}
  try{$date=[datetime]::ParseExact($key,'yyyy-MM-dd',[Globalization.CultureInfo]::InvariantCulture)}catch{throw ('Jour ouvrable invalide : '+$key)}
  if($date -lt $window.First -or $date -gt $window.Last){throw 'Tous les jours ouvrables doivent appartenir au mois sélectionné.'}
  if($seen.ContainsKey($key)){throw 'Un jour ouvrable ne peut pas être sélectionné deux fois.'};$seen[$key]=$true;$dates.Add($key)
 }
 return [pscustomobject]@{Kind='Calendar';Id=$Month;Month=$Month;Year=$Year;ContextId=$ContextId;StartMonth=$window.StartMonth;WorkDates=@($dates.ToArray() | Sort-Object);JustifiedPaid=$true;Revision=0;UpdatedAt=[datetimeoffset]::UtcNow.ToString('o');UpdatedBy=$Actor;Audit=@()}
}
function New-ScoliaMonthlySalary {
 param([string]$Month,[string]$Year,[string]$ContextId,[string]$TeacherId,[object]$GrossFC,[string]$Actor,[int]$StartMonth=0)
 $window=Get-ScoliaSalaryMonth -Month $Month -Year $Year -StartMonth $StartMonth
 if([string]::IsNullOrWhiteSpace($ContextId)){throw 'Le contexte scolaire est obligatoire.'}
 Assert-ScoliaSalaryActor $Actor
 $teacherKey=ConvertTo-ScoliaSalaryTeacherId $TeacherId;$amount=Get-ScoliaSalaryAmount $GrossFC
 return [pscustomobject]@{Kind='Salary';Id=($Month+'|'+$teacherKey);Month=$Month;Year=$Year;ContextId=$ContextId;StartMonth=$window.StartMonth;TeacherId=$teacherKey;GrossFC=$amount;Revision=0;UpdatedAt=[datetimeoffset]::UtcNow.ToString('o');UpdatedBy=$Actor;Audit=@()}
}
function Copy-ScoliaSalaryValue([object]$Value){
 if($Value -is [array] -and $Value.Count -eq 0){return ,@()}
 $copy=ConvertFrom-ScoliaJson -InputObject (ConvertTo-Json -InputObject $Value -Depth 32 -Compress)
 if($Value -is [array]){return ,@($copy)}
 return $copy
}
function Get-ScoliaSalaryRecordHash([object]$Value){
 $json=ConvertTo-Json -InputObject $Value -Depth 32 -Compress
 $algorithm=[Security.Cryptography.SHA256]::Create()
 try{return ([BitConverter]::ToString($algorithm.ComputeHash([Text.Encoding]::UTF8.GetBytes($json)))).Replace('-','').ToLowerInvariant()}finally{$algorithm.Dispose()}
}
function Get-ScoliaCalendarDifference {
 param([object]$Previous,[object]$Proposed)
 if(-not $Proposed){throw 'Calendrier proposé absent.'}
 $old=@();if($Previous){$old=@($Previous.WorkDates)};$next=@($Proposed.WorkDates)
 return [pscustomobject]@{Added=@($next | Where-Object {$_ -cnotin $old});Removed=@($old | Where-Object {$_ -cnotin $next});PaymentRuleChanged=($null -ne $Previous -and [bool]$Previous.JustifiedPaid -ne [bool]$Proposed.JustifiedPaid)}
}
function Assert-ScoliaSalaryRecordRevision([object]$Record){
 if(-not $Record.PSObject.Properties['Revision'] -or [string]$Record.Revision -notmatch '^\d+$' -or [decimal]$Record.Revision -ge [int]::MaxValue){throw 'Révision de paie invalide.'}
}
function Assert-ScoliaLegacyPayrollCalculation {
 param([object]$Snapshot,[object]$Calendar)
 if($Snapshot.JustifiedPaid -isnot [bool]){throw 'Règle de paiement de la paie invalide.'}
 $window=Get-ScoliaSalaryMonth -Month $Snapshot.Month -Year $Snapshot.Year -StartMonth $Calendar.StartMonth
 try{$asOf=[datetimeoffset]::ParseExact([string]$Snapshot.AsOf,'o',[Globalization.CultureInfo]::InvariantCulture);$finalized=[datetimeoffset]::ParseExact([string]$Snapshot.FinalizedAt,'o',[Globalization.CultureInfo]::InvariantCulture)}catch{throw 'Horodatage de finalisation invalide.'}
 if($asOf.Date -le $window.Last.Date){throw 'Une paie finalisée contient un mois encore ouvert.'}
 if([string]$Snapshot.SourceHash -notmatch '^[a-f0-9]{64}$'){throw 'Empreinte des sources de paie absente.'}
 $workDates=@($Calendar.WorkDates);$count=$workDates.Count;$seen=@{};$eligible=0;$unjustified=0;$justified=0;$deductDays=0
 if($count -eq 0 -or @($Snapshot.Rows).Count -ne $window.DayCount){throw 'Le détail quotidien de la paie est incomplet.'}
 foreach($row in @($Snapshot.Rows)){
  try{$date=[datetime]::ParseExact([string]$row.Date,'yyyy-MM-dd',[Globalization.CultureInfo]::InvariantCulture)}catch{throw 'Date du détail de paie invalide.'}
  if($date -lt $window.First -or $date -gt $window.Last -or $seen.ContainsKey([string]$row.Date)){throw 'Le détail quotidien est hors mois ou dupliqué.'};$seen[[string]$row.Date]=$true
  foreach($name in @('IsWorkday','IsActive','IsFinal','IsAbsent','IsJustified','Deductible')){if($row.$name -isnot [bool]){throw 'Statut du détail de paie invalide.'}}
  if([bool]$row.IsWorkday -ne ([string]$row.Date -cin $workDates)){throw 'Le détail ne correspond pas aux jours ouvrables.'}
  if([string]$row.SignatureCount -notmatch '^[012]$'){throw 'Nombre de signatures incohérent dans la paie.'}
  if($row.IsWorkday -and $row.IsActive){
   $eligible++;if(-not $row.IsFinal){throw 'Une paie finalisée contient une journée en cours.'}
   if([int]$row.SignatureCount -eq 2){if($row.IsAbsent -or $row.Code -cne 'Present'){throw 'Une double signature doit compter une présence.'}}
   elseif(-not $row.IsAbsent -or $row.Code -cnotin @('AbsenceUnjustified','AbsenceJustified')){throw 'Un jour ouvrable terminé sans double signature doit compter une absence.'}
   if($row.IsAbsent -and [bool]$row.IsJustified -ne ([string]$row.Code -ceq 'AbsenceJustified')){throw 'Le motif et le statut de justification sont incohérents.'}
   if($row.IsAbsent){if($row.IsJustified){$justified++;if(-not $Snapshot.JustifiedPaid){$deductDays++}}else{$unjustified++;$deductDays++}}
  }
  $shouldDeduct=$row.IsWorkday -and $row.IsActive -and $row.IsFinal -and $row.IsAbsent -and (-not $row.IsJustified -or -not $Snapshot.JustifiedPaid)
  if([bool]$row.Deductible -ne [bool]$shouldDeduct){throw 'Déduction quotidienne incohérente.'}
 }
 if($eligible -eq 0 -or [int]$Snapshot.WorkDays -ne $count -or [int]$Snapshot.EligibleWorkDays -ne $eligible -or [int]$Snapshot.UnjustifiedDays -ne $unjustified -or [int]$Snapshot.JustifiedDays -ne $justified -or [int]$Snapshot.DeductionDays -ne $deductDays){throw 'Compteurs de paie incohérents.'}
 $gross=[decimal](Get-ScoliaSalaryAmount $Snapshot.GrossFC);$rate=$gross/[decimal]$count
 $net=[decimal]::Round(($gross-($rate*[decimal]$deductDays)),0,[MidpointRounding]::AwayFromZero)
 if([string]$Snapshot.DailyRateFC -cne $rate.ToString([Globalization.CultureInfo]::InvariantCulture) -or [decimal]$Snapshot.NetFC -ne $net -or [decimal]$Snapshot.DeductionsFC -ne ($gross-$net)){throw 'Le calcul final ne correspond pas au salaire et aux absences conservés.'}
}
function Get-ScoliaPayrollWeekStart([object]$Date){
 $day=ConvertTo-ScoliaPersonnelDate $Date
 return $day.AddDays(-(([int]$day.DayOfWeek+6)%7)).Date
}
function Get-ScoliaPayrollLateGroups {
 param([object[]]$SourceRows,[object[]]$SourceCalendars,[string]$Month,[string]$Year,[int]$StartMonth)
 $bounds=Get-ScoliaAttendanceYearBounds -Year $Year -StartMonth $StartMonth
 $calendarMonths=@{};foreach($calendar in @($SourceCalendars)){$calendarMonths[[string]$calendar.Month]=$true}
 $weeks=@{};foreach($row in @($SourceRows)){
  $week=(Get-ScoliaPayrollWeekStart $row.Date).ToString('yyyy-MM-dd')
  if(-not $weeks.ContainsKey($week)){$weeks[$week]=New-Object 'System.Collections.Generic.List[object]'}
  $weeks[$week].Add($row)
 }
 $groups=New-Object 'System.Collections.Generic.List[object]'
 foreach($weekKey in @($weeks.Keys | Sort-Object)){
  $weekRows=@($weeks[$weekKey].ToArray() | Sort-Object Date);$weekStart=ConvertTo-ScoliaPersonnelDate $weekKey
  $last=ConvertTo-ScoliaPersonnelDate $weekRows[-1].Date;$complete=$true
  for($check=$weekStart;$check -le $last;$check=$check.AddDays(1)){
   if($check -ge $bounds.Start -and $check -le $bounds.End -and -not $calendarMonths.ContainsKey($check.ToString('yyyy-MM'))){$complete=$false}
  }
  if(-not $complete){continue}
  $lateRows=@($weekRows | Where-Object {$_.IsWorkday -and $_.IsActive -and $_.IsFinal -and $_.SignatureCount -gt 0 -and $_.LateRecorded -and -not ($_.IsAbsent -and $_.IsJustified)})
  for($index=2;$index -lt $lateRows.Count;$index+=3){
   $third=$lateRows[$index];if(([string]$third.Date).Substring(0,7) -cne $Month){continue}
   $dates=@([string]$lateRows[$index-2].Date,[string]$lateRows[$index-1].Date,[string]$third.Date)
   $reason=([string]$third.Reason).Trim()
   $groups.Add([pscustomobject][ordered]@{WeekStart=$weekKey;Dates=$dates;AssignedDate=[string]$third.Date;IsJustified=([bool]$reason);Reason=$reason})
  }
 }
 return $groups.ToArray()
}
function Assert-ScoliaPayrollDailyRow {
 param([object]$Row,[datetime]$Date,[string[]]$WorkDates,[bool]$RequireFinal=$false)
 foreach($field in @('Date','Code','Status','SignatureCount','EntryAt','ExitAt','Reason','LatePenaltyReason','PenaltyWeekStart','ConvertedDates')){if(-not $Row.PSObject.Properties[$field]){throw 'Champ du détail de paie absent.'}}
 foreach($name in @('IsWorkday','IsActive','IsFinal','IsAbsent','IsJustified','Deductible','LateRecorded','IsLate','LatePenalty','LatePenaltyJustified')){if($Row.$name -isnot [bool]){throw 'Statut du détail de paie invalide.'}}
 if([bool]$Row.IsWorkday -ne ($Date.ToString('yyyy-MM-dd') -cin $WorkDates)){throw 'Le détail ne correspond pas aux jours ouvrables.'}
 if([string]$Row.SignatureCount -notmatch '^[012]$'){throw 'Nombre de signatures incohérent dans la paie.'}
 $recorded=$false
 if([int]$Row.SignatureCount -gt 0){
  try{$entry=ConvertTo-ScoliaPersonnelInstant ([string]$Row.EntryAt)}catch{throw 'Heure de signature invalide dans la paie.'}
  if($entry.Date -ne $Date.Date){throw 'La signature ne correspond pas au jour de paie.'}
  $recorded=$entry.TimeOfDay -ge [timespan]::FromHours(8)
 }elseif([string]$Row.EntryAt){throw 'Une entrée ne peut exister sans signature.'}
 if([int]$Row.SignatureCount -eq 2){$exit=ConvertTo-ScoliaPersonnelInstant ([string]$Row.ExitAt);if($exit.Date -ne $Date.Date -or $exit -lt $entry){throw 'La sortie ne correspond pas à la double signature.'}}elseif([string]$Row.ExitAt){throw 'Une sortie ne peut exister sans double signature.'}
 if([bool]$Row.LateRecorded -ne [bool]$recorded){throw 'Le retard ne correspond pas à l''heure de signature.'}
 if($Row.IsWorkday -and $Row.IsActive){
  if($RequireFinal -and -not $Row.IsFinal){throw 'Une paie finalisée contient une journée en cours.'}
  if($Row.IsFinal){
   if([int]$Row.SignatureCount -eq 2){
    $expected=$(if($recorded){'Late'}else{'Present'})
    if($Row.IsAbsent -or [string]$Row.Code -cne $expected){throw 'Une double signature doit compter une présence ou un retard.'}
   }elseif(-not $Row.IsAbsent -or $Row.Code -cnotin @('AbsenceUnjustified','AbsenceJustified')){throw 'Un jour ouvrable terminé sans double signature doit compter une absence.'}
  }
  if($Row.IsAbsent -and ([bool]$Row.IsJustified -ne ([string]$Row.Code -ceq 'AbsenceJustified') -or ($Row.IsJustified -and [string]::IsNullOrWhiteSpace([string]$Row.Reason)))){throw 'Une absence justifiée doit conserver son motif.'}
 }
 $late=$recorded
 if([bool]$Row.IsLate -ne [bool]$late){throw 'Indicateur de retard incohérent.'}
}
function Assert-ScoliaPayrollCalculation {
 param([object]$Snapshot,[object]$Calendar)
 if(-not $Snapshot.PSObject.Properties['RuleVersion']){Assert-ScoliaLegacyPayrollCalculation -Snapshot $Snapshot -Calendar $Calendar;return}
 foreach($field in @('RuleVersion','LateDays','LateAbsenceDays','LateJustifiedAbsenceDays','LateUnjustifiedAbsenceDays','LateDeductibleDays','LateGroups','SourceDays','SourceRows','SourceCalendars','SourceWeeksComplete','UnjustifiedDays','JustifiedDays','DeductionDays','WorkDays','EligibleWorkDays','GrossFC','DailyRateFC','DeductionsFC','NetFC')){if(-not $Snapshot.PSObject.Properties[$field]){throw 'Métadonnée obligatoire de la paie V57 absente.'}}
 if([int]$Snapshot.RuleVersion -ne 2 -or $Snapshot.JustifiedPaid -isnot [bool] -or -not $Snapshot.JustifiedPaid){throw 'Règle de paie V57 invalide.'}
 $window=Get-ScoliaSalaryMonth -Month $Snapshot.Month -Year $Snapshot.Year -StartMonth $Calendar.StartMonth
 $bounds=Get-ScoliaAttendanceYearBounds -Year $Snapshot.Year -StartMonth $Calendar.StartMonth
 $firstSource=Get-ScoliaPayrollWeekStart $window.First;if($firstSource -lt $bounds.Start){$firstSource=$bounds.Start}
 try{$asOf=[datetimeoffset]::ParseExact([string]$Snapshot.AsOf,'o',[Globalization.CultureInfo]::InvariantCulture);$finalized=[datetimeoffset]::ParseExact([string]$Snapshot.FinalizedAt,'o',[Globalization.CultureInfo]::InvariantCulture)}catch{throw 'Horodatage de finalisation invalide.'}
 if($asOf.Date -le $window.Last -or [string]$Snapshot.SourceHash -notmatch '^[a-f0-9]{64}$'){throw 'Sources ou fin du mois de paie invalides.'}
 if($Snapshot.SourceWeeksComplete -isnot [bool] -or -not $Snapshot.SourceWeeksComplete){throw 'Les calendriers des semaines de paie sont incomplets.'}
 $sourceDays=@();for($day=$firstSource;$day -le $window.Last;$day=$day.AddDays(1)){$sourceDays+=$day.ToString('yyyy-MM-dd')}
 if((@($Snapshot.SourceDays) -join '|') -cne ($sourceDays -join '|') -or @($Snapshot.SourceRows).Count -ne $sourceDays.Count){throw 'Les journées sources des retards sont incomplètes.'}
 $calendars=@{};$allWorkDates=@()
 foreach($sourceCalendar in @($Snapshot.SourceCalendars)){
  if($sourceCalendar.JustifiedPaid -isnot [bool] -or $sourceCalendar.Kind -cne 'Calendar' -or $sourceCalendar.ContextId -cne $Snapshot.ContextId -or $sourceCalendar.Year -cne $Snapshot.Year -or [int]$sourceCalendar.StartMonth -ne [int]$Calendar.StartMonth -or $calendars.ContainsKey([string]$sourceCalendar.Month)){throw 'Calendrier source de retard invalide.'}
  Assert-ScoliaSalaryRecordRevision $sourceCalendar
  $checked=New-ScoliaMonthlyCalendar -Month $sourceCalendar.Month -Year $sourceCalendar.Year -ContextId $sourceCalendar.ContextId -WorkDates @($sourceCalendar.WorkDates) -Actor $sourceCalendar.UpdatedBy -StartMonth $sourceCalendar.StartMonth
  if((@($sourceCalendar.WorkDates) -join '|') -cne (@($checked.WorkDates) -join '|')){throw 'Jours du calendrier source incohérents.'}
  $calendars[[string]$sourceCalendar.Month]=$sourceCalendar;$allWorkDates+=@($sourceCalendar.WorkDates)
 }
 foreach($dayKey in $sourceDays){if(-not $calendars.ContainsKey($dayKey.Substring(0,7))){throw 'Un calendrier nécessaire aux retards est absent.'}}
 if(-not $calendars.ContainsKey([string]$Snapshot.Month) -or [int]$calendars[[string]$Snapshot.Month].Revision -ne [int]$Calendar.Revision -or (@($calendars[[string]$Snapshot.Month].WorkDates) -join '|') -cne (@($Calendar.WorkDates) -join '|')){throw 'Le calendrier du mois ne correspond pas à la source conservée.'}
 $sourceMap=@{};$position=0
 foreach($row in @($Snapshot.SourceRows)){
  if([string]$row.Date -cne $sourceDays[$position]){throw 'Ordre des journées sources incohérent.'}
  Assert-ScoliaPayrollDailyRow -Row $row -Date (ConvertTo-ScoliaPersonnelDate $row.Date) -WorkDates $allWorkDates -RequireFinal $true
  $sourceMap[[string]$row.Date]=$row;$position++
 }
 $groups=@(Get-ScoliaPayrollLateGroups -SourceRows @($Snapshot.SourceRows) -SourceCalendars @($Snapshot.SourceCalendars) -Month $Snapshot.Month -Year $Snapshot.Year -StartMonth $Calendar.StartMonth)
 if((Get-ScoliaSalaryRecordHash @($Snapshot.LateGroups)) -cne (Get-ScoliaSalaryRecordHash @($groups))){throw 'La conversion des retards hebdomadaires est incohérente.'}
 $penalties=@{};foreach($group in $groups){$penalties[[string]$group.AssignedDate]=$group}
 $eligible=0;$unjustified=0;$justified=0;$lateCount=0;$deductDays=0;$lateExtra=0;$workCount=@($Calendar.WorkDates).Count
 if($workCount -eq 0 -or @($Snapshot.Rows).Count -ne $window.DayCount){throw 'Le détail quotidien de la paie est incomplet.'}
 $seen=@{}
 foreach($row in @($Snapshot.Rows)){
  $key=[string]$row.Date
  if(-not $sourceMap.ContainsKey($key) -or -not $key.StartsWith([string]$Snapshot.Month+'-',[StringComparison]::Ordinal) -or $seen.ContainsKey($key)){throw 'Le détail quotidien est hors mois ou dupliqué.'};$seen[$key]=$true
  if((Get-ScoliaSalaryRecordHash $row) -cne (Get-ScoliaSalaryRecordHash $sourceMap[$key])){throw 'Le détail du mois diffère de ses journées sources.'}
  if($row.IsWorkday -and $row.IsActive){
   $eligible++;if($row.IsAbsent){if($row.IsJustified){$justified++}else{$unjustified++}}
   if($row.IsFinal -and [int]$row.SignatureCount -gt 0 -and $row.LateRecorded -and -not ($row.IsAbsent -and $row.IsJustified)){$lateCount++}
  }
 }
 foreach($row in @($Snapshot.SourceRows)){
  $key=[string]$row.Date;$penalty=$penalties.ContainsKey($key)
  if([bool]$row.LatePenalty -ne $penalty){throw 'Conversion de retard manquante ou dupliquée.'}
  if($penalty){
   $group=$penalties[$key]
   if([string]$row.PenaltyWeekStart -cne [string]$group.WeekStart -or (@($row.ConvertedDates) -join '|') -cne (@($group.Dates) -join '|') -or [bool]$row.LatePenaltyJustified -ne [bool]$group.IsJustified -or [string]$row.LatePenaltyReason -cne [string]$group.Reason){throw 'Motif ou détail de la conversion hebdomadaire incohérent.'}
  }elseif($row.LatePenaltyJustified -or [string]$row.LatePenaltyReason -or [string]$row.PenaltyWeekStart -or @($row.ConvertedDates).Count){throw 'Une journée sans conversion contient une pénalité.'}
  $shouldDeduct=$row.IsWorkday -and $row.IsActive -and $row.IsFinal -and (($row.IsAbsent -and -not $row.IsJustified) -or ($penalty -and -not $row.LatePenaltyJustified))
  if([bool]$row.Deductible -ne [bool]$shouldDeduct){throw 'Déduction quotidienne incohérente.'}
  if($key.StartsWith([string]$Snapshot.Month+'-',[StringComparison]::Ordinal) -and $shouldDeduct){$deductDays++;if($penalty -and -not $row.LatePenaltyJustified -and -not ($row.IsAbsent -and -not $row.IsJustified)){$lateExtra++}}
 }
 $lateJustified=@($groups | Where-Object {$_.IsJustified}).Count;$lateUnjustified=$groups.Count-$lateJustified
 if($eligible -eq 0 -or [int]$Snapshot.WorkDays -ne $workCount -or [int]$Snapshot.EligibleWorkDays -ne $eligible -or [int]$Snapshot.UnjustifiedDays -ne $unjustified -or [int]$Snapshot.JustifiedDays -ne $justified -or [int]$Snapshot.LateDays -ne $lateCount -or [int]$Snapshot.LateAbsenceDays -ne $groups.Count -or [int]$Snapshot.LateJustifiedAbsenceDays -ne $lateJustified -or [int]$Snapshot.LateUnjustifiedAbsenceDays -ne $lateUnjustified -or [int]$Snapshot.LateDeductibleDays -ne $lateExtra -or [int]$Snapshot.DeductionDays -ne $deductDays -or $deductDays -ne ($unjustified+$lateExtra)){throw 'Compteurs de paie incohérents.'}
 $gross=[decimal](Get-ScoliaSalaryAmount $Snapshot.GrossFC);$rate=$gross/[decimal]$workCount;$net=[decimal]::Round(($gross-($rate*[decimal]$deductDays)),0,[MidpointRounding]::AwayFromZero)
 if([string]$Snapshot.DailyRateFC -cne $rate.ToString([Globalization.CultureInfo]::InvariantCulture) -or [decimal]$Snapshot.NetFC -ne $net -or [decimal]$Snapshot.DeductionsFC -ne ($gross-$net)){throw 'Le calcul final ne correspond pas au salaire, aux absences et aux retards conservés.'}
}
function Get-ScoliaPayrollHashPayload([object]$Snapshot){
 $payload=[ordered]@{Kind=$Snapshot.Kind;Id=$Snapshot.Id;Month=$Snapshot.Month;Year=$Snapshot.Year;ContextId=$Snapshot.ContextId;TeacherId=$Snapshot.TeacherId;TeacherName=$Snapshot.TeacherName;GrossFC=$Snapshot.GrossFC;WorkDates=@($Snapshot.WorkDates);JustifiedPaid=$Snapshot.JustifiedPaid;CalendarRevision=$Snapshot.CalendarRevision;SalaryRecordRevision=$Snapshot.SalaryRecordRevision;PersonnelRevision=$Snapshot.PersonnelRevision;SalaryRevision=$Snapshot.SalaryRevision;WorkDays=$Snapshot.WorkDays;EligibleWorkDays=$Snapshot.EligibleWorkDays;UnjustifiedDays=$Snapshot.UnjustifiedDays;JustifiedDays=$Snapshot.JustifiedDays;DeductionDays=$Snapshot.DeductionDays;DailyRateFC=$Snapshot.DailyRateFC;DeductionsFC=$Snapshot.DeductionsFC;NetFC=$Snapshot.NetFC;Rows=@($Snapshot.Rows);SourceHash=$Snapshot.SourceHash;AsOf=$Snapshot.AsOf;FinalizedAt=$Snapshot.FinalizedAt;FinalizedBy=$Snapshot.FinalizedBy}
 if($Snapshot.PSObject.Properties['RuleVersion']){foreach($name in @('RuleVersion','LateDays','LateAbsenceDays','LateJustifiedAbsenceDays','LateUnjustifiedAbsenceDays','LateDeductibleDays','LateGroups','SourceDays','SourceRows','SourceCalendars','SourceWeeksComplete')){$payload[$name]=$Snapshot.$name}}
 return [pscustomobject]$payload
}
function Assert-ScoliaSalaryDocument([object]$Document){
 if(-not $Document -or $Document.Schema -ne 1 -or -not $Document.Context -or [string]::IsNullOrWhiteSpace([string]$Document.Context.Id) -or [string]$Document.Revision -notmatch '^\d+$' -or [decimal]$Document.Revision -ge [int]::MaxValue){throw 'Document des salaires invalide. Les données existantes sont conservées.'}
 $seen=@{};$calendars=@{};$salaries=@{}
 foreach($record in @($Document.Items)){
  if(-not $record -or [string]$record.ContextId -cne [string]$Document.Context.Id -or [string]$record.Year -cne [string]$Document.Context.Year){throw 'Un enregistrement de paie appartient à une autre base ou année.'}
  $key=[string]$record.Kind+'|'+[string]$record.Id
  if($seen.ContainsKey($key)){throw 'Enregistrement de paie dupliqué.'};$seen[$key]=$true
  switch([string]$record.Kind){
   'Calendar'{
    Assert-ScoliaSalaryRecordRevision $record
    if($record.JustifiedPaid -isnot [bool] -or -not $record.PSObject.Properties['StartMonth'] -or [int]$record.StartMonth -lt 1 -or [int]$record.StartMonth -gt 12){throw 'Règle de paiement ou calendrier scolaire invalide.'}
    $valid=New-ScoliaMonthlyCalendar -Month $record.Month -Year $record.Year -ContextId $record.ContextId -WorkDates @($record.WorkDates) -JustifiedPaid $record.JustifiedPaid -Actor $record.UpdatedBy -StartMonth $record.StartMonth
    if([string]$record.Id -cne [string]$valid.Id -or -not $record.PSObject.Properties['WorkDates'] -or (@($record.WorkDates) -join '|') -cne (@($valid.WorkDates) -join '|')){throw 'Identifiant ou ordre des jours du calendrier invalide.'}
    $calendars[[string]$record.Month]=$record
   }
   'Salary'{
    Assert-ScoliaSalaryRecordRevision $record
    if(-not $record.PSObject.Properties['StartMonth'] -or [int]$record.StartMonth -lt 1 -or [int]$record.StartMonth -gt 12){throw 'Calendrier scolaire du salaire invalide.'}
    $valid=New-ScoliaMonthlySalary -Month $record.Month -Year $record.Year -ContextId $record.ContextId -TeacherId $record.TeacherId -GrossFC $record.GrossFC -Actor $record.UpdatedBy -StartMonth $record.StartMonth
    if([string]$record.Id -cne [string]$valid.Id -or [string]$record.TeacherId -cne [string]$valid.TeacherId){throw 'Identifiant de salaire invalide.'}
    $salaries[[string]$record.Id]=$record
   }
   'Payroll'{
    $teacherKey=ConvertTo-ScoliaSalaryTeacherId $record.TeacherId
    if([string]$record.Id -cne ($record.Month+'|'+$teacherKey) -or $record.State -cne 'Finalisé'){throw 'Identifiant ou état de paie finalisée invalide.'}
    Assert-ScoliaSalaryActor $record.FinalizedBy
    if([string]$record.Hash -notmatch '^[a-f0-9]{64}$' -or [string]$record.Hash -cne (Get-ScoliaSalaryRecordHash (Get-ScoliaPayrollHashPayload $record))){throw 'L''empreinte de la paie finalisée est invalide.'}
    foreach($revision in @($record.PersonnelRevision,$record.SalaryRevision,$record.CalendarRevision,$record.SalaryRecordRevision)){if([string]$revision -notmatch '^\d+$' -or [decimal]$revision -ge [int]::MaxValue){throw 'Révision de source de paie invalide.'}}
    $gross=Get-ScoliaSalaryAmount $record.GrossFC
    if([decimal]$record.NetFC -lt 0 -or [decimal]$record.NetFC -gt [decimal]$gross -or [decimal]$record.DeductionsFC -ne ([decimal]$gross-[decimal]$record.NetFC)){throw 'Montants de la paie finalisée incohérents.'}
   }
   default{throw 'Type d''enregistrement de paie inconnu.'}
  }
 }
 foreach($record in @($Document.Items | Where-Object {$_.Kind -eq 'Salary' -or $_.Kind -eq 'Payroll'})){
  if(-not $calendars.ContainsKey([string]$record.Month)){throw 'Le calendrier du mois doit être enregistré avant son salaire.'}
  if($record.Kind -eq 'Salary' -and [int]$record.StartMonth -ne [int]$calendars[[string]$record.Month].StartMonth){throw 'Le salaire utilise un autre calendrier scolaire que les jours ouvrables.'}
  if([string]$record.Kind -eq 'Payroll'){
   if(-not $salaries.ContainsKey([string]$record.Id)){throw 'La paie finalisée ne possède pas de salaire mensuel.'}
   $calendar=$calendars[[string]$record.Month];$salary=$salaries[[string]$record.Id]
   if([int]$calendar.Revision -ne [int]$record.CalendarRevision -or [int]$salary.Revision -ne [int]$record.SalaryRecordRevision -or [decimal]$salary.GrossFC -ne [decimal]$record.GrossFC -or (-not $record.PSObject.Properties['RuleVersion'] -and [bool]$calendar.JustifiedPaid -ne [bool]$record.JustifiedPaid) -or (@($calendar.WorkDates) -join '|') -cne (@($record.WorkDates) -join '|')){throw 'Les sources d''une paie finalisée ont été modifiées.'}
   Assert-ScoliaPayrollCalculation -Snapshot $record -Calendar $calendar
  }
 }
}
function Assert-ScoliaSalaryChange {
 param([object]$Document,[object]$Record,[int]$ExpectedRevision)
 if([int]$Document.Revision -ne $ExpectedRevision){throw 'Les salaires ont changé dans une autre session. Actualisez avant d''enregistrer.'}
 Assert-ScoliaSalaryDocument $Document
 if([string]$Document.Context.Id -cne [string]$Record.ContextId -or [string]$Document.Context.Year -cne [string]$Record.Year){throw 'Cet enregistrement appartient à une autre base ou année.'}
}
function Set-ScoliaMonthlyCalendar {
 param([object]$Document,[object]$Calendar,[int]$ExpectedRevision)
 Assert-ScoliaSalaryChange -Document $Document -Record $Calendar -ExpectedRevision $ExpectedRevision
 if(@($Document.Items | Where-Object {$_.Kind -eq 'Payroll' -and $_.Month -eq $Calendar.Month}).Count){throw 'Le calendrier est figé car une paie de ce mois est finalisée.'}
 $valid=New-ScoliaMonthlyCalendar -Month $Calendar.Month -Year $Calendar.Year -ContextId $Calendar.ContextId -WorkDates @($Calendar.WorkDates) -JustifiedPaid $Calendar.JustifiedPaid -Actor $Calendar.UpdatedBy -StartMonth $Calendar.StartMonth
 $previous=@($Document.Items | Where-Object {$_.Kind -eq 'Calendar' -and $_.Id -eq $valid.Id})
 if($previous.Count){
  $old=$previous[0];$valid.Revision=[int]$old.Revision+1
  if([int]$old.StartMonth -ne [int]$valid.StartMonth){throw 'Ce mois doit conserver le mois de rentrée du calendrier déjà enregistré.'}
  $valid.Audit=@($old.Audit)+@([pscustomobject]@{At=$valid.UpdatedAt;By=$valid.UpdatedBy;PreviousRevision=$old.Revision;PreviousWorkDates=@($old.WorkDates);PreviousJustifiedPaid=$old.JustifiedPaid})
 }else{$valid.Revision=1}
 $Document.Items=@($Document.Items | Where-Object {-not ($_.Kind -eq 'Calendar' -and $_.Id -eq $valid.Id)})+@($valid)
}
function Set-ScoliaMonthlySalary {
 param([object]$Document,[object]$Salary,[int]$ExpectedRevision)
 Assert-ScoliaSalaryChange -Document $Document -Record $Salary -ExpectedRevision $ExpectedRevision
 if(@($Document.Items | Where-Object {$_.Kind -eq 'Payroll' -and $_.Id -eq $Salary.Id}).Count){throw 'Le salaire est figé car cette paie est finalisée.'}
 $valid=New-ScoliaMonthlySalary -Month $Salary.Month -Year $Salary.Year -ContextId $Salary.ContextId -TeacherId $Salary.TeacherId -GrossFC $Salary.GrossFC -Actor $Salary.UpdatedBy -StartMonth $Salary.StartMonth
 if(@($Document.Items | Where-Object {$_.Kind -eq 'Calendar' -and $_.Month -eq $valid.Month}).Count -ne 1){throw 'Enregistrez d''abord les jours ouvrables de ce mois.'}
 $calendar=@($Document.Items | Where-Object {$_.Kind -eq 'Calendar' -and $_.Month -eq $valid.Month})[0]
 if([int]$valid.StartMonth -ne [int]$calendar.StartMonth){throw 'Le salaire doit conserver le calendrier scolaire enregistré pour ce mois.'}
 $previous=@($Document.Items | Where-Object {$_.Kind -eq 'Salary' -and $_.Id -eq $valid.Id})
 if($previous.Count){
  $old=$previous[0];$valid.Revision=[int]$old.Revision+1
  $valid.Audit=@($old.Audit)+@([pscustomobject]@{At=$valid.UpdatedAt;By=$valid.UpdatedBy;PreviousRevision=$old.Revision;PreviousGrossFC=$old.GrossFC})
 }else{$valid.Revision=1}
 $Document.Items=@($Document.Items | Where-Object {-not ($_.Kind -eq 'Salary' -and $_.Id -eq $valid.Id)})+@($valid)
}
function Get-ScoliaMonthlyTeacherPayroll {
 param([object]$Teacher,[object]$Calendar,[object[]]$Punches=@(),[object[]]$Justifications=@(),[object]$Salary,[datetimeoffset]$AsOf=[datetimeoffset]::Now,[object[]]$PersonnelItems=@(),[object[]]$Calendars=@())
 if(-not $Teacher -or -not $Calendar -or -not $Salary){throw 'L''enseignant, le calendrier et le salaire mensuel sont obligatoires.'}
 $teacherKey=ConvertTo-ScoliaSalaryTeacherId $Teacher.Id
 if($Calendar.Kind -cne 'Calendar' -or $Salary.Kind -cne 'Salary' -or [string]$Salary.TeacherId -cne $teacherKey -or [string]$Salary.Month -cne [string]$Calendar.Month -or [string]$Salary.Year -cne [string]$Calendar.Year -or [string]$Salary.ContextId -cne [string]$Calendar.ContextId){throw 'Le salaire et le calendrier doivent correspondre à cet enseignant, à la même base et au même mois.'}
 if($Calendar.JustifiedPaid -isnot [bool] -or [string]$Calendar.Id -cne [string]$Calendar.Month -or [string]$Salary.Id -cne ($Calendar.Month+'|'+$teacherKey)){throw 'Le calendrier ou l''identifiant du salaire est invalide.'}
 if([int]$Salary.StartMonth -ne [int]$Calendar.StartMonth){throw 'Le salaire et les jours ouvrables doivent conserver le même calendrier scolaire.'}
 $validated=New-ScoliaMonthlyCalendar -Month $Calendar.Month -Year $Calendar.Year -ContextId $Calendar.ContextId -WorkDates @($Calendar.WorkDates) -Actor $Calendar.UpdatedBy -StartMonth $Calendar.StartMonth
 Assert-ScoliaSalaryRecordRevision $Calendar;Assert-ScoliaSalaryRecordRevision $Salary
 $window=Get-ScoliaSalaryMonth -Month $Calendar.Month -Year $Calendar.Year -StartMonth $Calendar.StartMonth
 $bounds=Get-ScoliaAttendanceYearBounds -Year $Calendar.Year -StartMonth $Calendar.StartMonth
 $firstSource=Get-ScoliaPayrollWeekStart $window.First;if($firstSource -lt $bounds.Start){$firstSource=$bounds.Start}
 $sourceDays=@();$sourceMonths=@{}
 for($day=$firstSource;$day -le $window.Last;$day=$day.AddDays(1)){$key=$day.ToString('yyyy-MM-dd');$sourceDays+=$key;$sourceMonths[$day.ToString('yyyy-MM')]=$true}
 $calendarMap=@{};$calendarMap[[string]$Calendar.Month]=$Calendar
 foreach($other in @($Calendars)){
  if(-not $other -or $other.Kind -cne 'Calendar'){continue}
  # Un ancien mois hors des semaines sources peut conserver un autre mois de rentrée.
  if(-not $sourceMonths.ContainsKey([string]$other.Month)){continue}
  if([string]$other.ContextId -cne [string]$Calendar.ContextId -or [string]$other.Year -cne [string]$Calendar.Year -or [int]$other.StartMonth -ne [int]$Calendar.StartMonth){throw 'Un calendrier des retards appartient à une autre base ou année scolaire.'}
  if([string]$other.Month -ceq [string]$Calendar.Month){if((Get-ScoliaSalaryRecordHash $other) -cne (Get-ScoliaSalaryRecordHash $Calendar)){throw 'Le calendrier du mois est fourni deux fois avec des valeurs différentes.'};continue}
  if($calendarMap.ContainsKey([string]$other.Month)){throw 'Un calendrier source est fourni deux fois.'}
  $check=New-ScoliaMonthlyCalendar -Month $other.Month -Year $other.Year -ContextId $other.ContextId -WorkDates @($other.WorkDates) -Actor $other.UpdatedBy -StartMonth $other.StartMonth
  Assert-ScoliaSalaryRecordRevision $other
  if((@($other.WorkDates) -join '|') -cne (@($check.WorkDates) -join '|')){throw 'Les jours ouvrables du calendrier source sont invalides.'}
  $calendarMap[[string]$other.Month]=$other
 }
 $complete=$true
 foreach($sourceMonth in @($sourceMonths.Keys)){if(-not $calendarMap.ContainsKey([string]$sourceMonth)){$complete=$false}}
 $sourceCalendars=@($sourceMonths.Keys | Sort-Object | ForEach-Object {if($calendarMap.ContainsKey($_)){Copy-ScoliaSalaryValue $calendarMap[$_]}})
 $allWorkDates=@($sourceCalendars | ForEach-Object {@($_.WorkDates)})
 $gross=[decimal](Get-ScoliaSalaryAmount $Salary.GrossFC);$workDates=@($validated.WorkDates);$workCount=$workDates.Count
 $allPunches=@(@($Punches)+@($PersonnelItems | Where-Object {$_.Kind -eq 'Punch'}) | Where-Object {(-not $_.PSObject.Properties['Kind'] -or $_.Kind -eq 'Punch') -and $_.TeacherId -eq $teacherKey -and $_.Date -cin $sourceDays})
 $allJustifications=@(@($Justifications)+@($PersonnelItems | Where-Object {$_.Kind -eq 'Justification'}) | Where-Object {(-not $_.PSObject.Properties['Kind'] -or $_.Kind -eq 'Justification') -and $_.TeacherId -eq $teacherKey -and $_.Date -cin $sourceDays})
 $sourceRows=New-Object 'System.Collections.Generic.List[object]'
 foreach($key in $sourceDays){
  $status=Get-ScoliaTeacherDailyStatus -Teacher $Teacher -Date $key -Punches $allPunches -Justifications $allJustifications -WorkDates $allWorkDates -AsOf $AsOf
  $recorded=$false;if([string]$status.EntryAt){$recorded=(ConvertTo-ScoliaPersonnelInstant ([string]$status.EntryAt)).TimeOfDay -ge [timespan]::FromHours(8)}
  $isLate=[bool]$status.IsLate
  $sourceRows.Add([pscustomobject][ordered]@{Date=$key;Code=[string]$status.Code;Status=[string]$status.Status;IsWorkday=[bool]$status.IsWorkday;IsActive=[bool]$status.IsActive;IsFinal=[bool]$status.IsFinal;SignatureCount=[int]$status.SignatureCount;IsAbsent=[bool]$status.IsAbsent;IsJustified=[bool]$status.IsJustified;Deductible=([bool]$status.IsWorkday -and [bool]$status.IsActive -and [bool]$status.IsFinal -and [bool]$status.IsAbsent -and -not [bool]$status.IsJustified);EntryAt=[string]$status.EntryAt;ExitAt=[string]$status.ExitAt;Reason=[string]$status.Reason;LateRecorded=[bool]$recorded;IsLate=[bool]$isLate;LatePenalty=$false;LatePenaltyJustified=$false;LatePenaltyReason='';PenaltyWeekStart='';ConvertedDates=@()})
 }
 $lateGroups=@(Get-ScoliaPayrollLateGroups -SourceRows @($sourceRows.ToArray()) -SourceCalendars $sourceCalendars -Month $Calendar.Month -Year $Calendar.Year -StartMonth $Calendar.StartMonth)
 foreach($group in $lateGroups){
  $row=@($sourceRows.ToArray() | Where-Object {$_.Date -ceq $group.AssignedDate})[0]
  $row.LatePenalty=$true;$row.LatePenaltyJustified=[bool]$group.IsJustified;$row.LatePenaltyReason=[string]$group.Reason;$row.PenaltyWeekStart=[string]$group.WeekStart;$row.ConvertedDates=@($group.Dates);$row.Deductible=($row.IsAbsent -and -not $row.IsJustified) -or -not $group.IsJustified
 }
 $rows=@($sourceRows.ToArray() | Where-Object {$_.Date -like ($Calendar.Month+'-*')})
 $eligibleRows=@($rows | Where-Object {$_.IsWorkday -and $_.IsActive});$eligible=$eligibleRows.Count
 $unjustified=@($eligibleRows | Where-Object {$_.IsFinal -and $_.IsAbsent -and -not $_.IsJustified}).Count
 $justified=@($eligibleRows | Where-Object {$_.IsFinal -and $_.IsAbsent -and $_.IsJustified}).Count
 $pending=@($eligibleRows | Where-Object {-not $_.IsFinal}).Count
 $lateCount=@($eligibleRows | Where-Object {$_.IsFinal -and $_.SignatureCount -gt 0 -and $_.LateRecorded -and -not ($_.IsAbsent -and $_.IsJustified)}).Count
 $lateJustified=@($lateGroups | Where-Object {$_.IsJustified}).Count;$lateUnjustified=$lateGroups.Count-$lateJustified
 $lateExtra=@($eligibleRows | Where-Object {$_.LatePenalty -and -not $_.LatePenaltyJustified -and -not ($_.IsAbsent -and -not $_.IsJustified)}).Count;$deductionDays=$unjustified+$lateExtra
 $available=($workCount -gt 0 -and $eligible -gt 0);$rate=$null;$rawDeduction=$null;$deductions=$null;$netToDate=$null;$netFinal=$null
 if($available){$rate=$gross/[decimal]$workCount;$rawDeduction=$rate*[decimal]$deductionDays;$netToDate=[decimal]::Round(($gross-$rawDeduction),0,[MidpointRounding]::AwayFromZero);$deductions=$gross-$netToDate}
 $canFinalize=$available -and $complete -and $pending -eq 0 -and $AsOf.Date -gt $window.Last.Date
 if($canFinalize){$netFinal=$netToDate}
 $message=$(if($workCount -eq 0){'Aucun jour ouvrable configuré : calcul et finalisation indisponibles.'}elseif($eligible -eq 0){'Aucun jour ouvrable pendant la période d''activité de cet enseignant.'}elseif(-not $complete){'Calcul provisoire : renseignez le calendrier du mois précédent pour terminer la première semaine. Aucune conversion de retard n''est retenue pour une semaine incomplète ; finalisation bloquée.'}elseif(-not $canFinalize){'Net projeté provisoire : les absences non justifiées et les groupes de trois retards terminés sont déduits. Les jours non terminés restent en attente. La paie se finalise après la fin du mois.'}else{'Mois terminé : montant final calculable. La finalisation conserve un état de paie, sans enregistrer de versement.'})
 $message+=' Les absences justifiées restent comptées avec leur motif et sont toujours payées. Trois retards dans une semaine du lundi au dimanche constituent une absence équivalente, affectée au troisième retard.'
 if($eligible -gt 0 -and $eligible -lt $workCount){$message+=' Le salaire mensuel saisi est conservé ; aucune absence n''est déduite hors de la période d''activité.'}
 $source=[pscustomobject][ordered]@{RuleVersion=2;Teacher=(Copy-ScoliaSalaryValue $Teacher);Calendar=(Copy-ScoliaSalaryValue $Calendar);Salary=(Copy-ScoliaSalaryValue $Salary);Calendars=$sourceCalendars;Punches=@($allPunches | Sort-Object Id);Justifications=@($allJustifications | Sort-Object Id)}
 return [pscustomobject]@{RuleVersion=2;Month=[string]$Calendar.Month;Year=[string]$Calendar.Year;ContextId=[string]$Calendar.ContextId;TeacherId=$teacherKey;TeacherName=[string]$Teacher.Nom;GrossFC=$gross;DailyRateFC=$rate;WorkDays=$workCount;EligibleWorkDays=$eligible;UnjustifiedDays=$unjustified;JustifiedDays=$justified;PendingDays=$pending;LateDays=$lateCount;LateAbsenceDays=$lateGroups.Count;LateJustifiedAbsenceDays=$lateJustified;LateUnjustifiedAbsenceDays=$lateUnjustified;LateDeductibleDays=$lateExtra;LateGroups=$lateGroups;SourceDays=$sourceDays;SourceRows=@($sourceRows.ToArray());SourceCalendars=$sourceCalendars;SourceWeeksComplete=$complete;DeductionDays=$deductionDays;UnroundedDeductionsFC=$rawDeduction;DeductionsToDateFC=$deductions;NetToDateFC=$netToDate;ProjectedNetFC=$netToDate;NetFinalFC=$netFinal;CalculationAvailable=$available;CanFinalize=$canFinalize;Message=$message;Rows=$rows;WorkDates=$workDates;JustifiedPaid=$true;CalendarRevision=[int]$Calendar.Revision;SalaryRecordRevision=[int]$Salary.Revision;SourceHash=(Get-ScoliaSalaryRecordHash $source);AsOf=$AsOf.ToString('o')}
}
function New-ScoliaPayrollSnapshot {
 param([object]$Preview,[string]$Actor,[int]$PersonnelRevision,[int]$SalaryRevision)
 Assert-ScoliaSalaryActor $Actor
 if(-not $Preview -or -not $Preview.CanFinalize -or -not $Preview.CalculationAvailable -or $null -eq $Preview.NetFinalFC -or [int]$Preview.PendingDays -ne 0 -or [int]$Preview.RuleVersion -ne 2 -or -not $Preview.SourceWeeksComplete){throw 'La paie ne peut être finalisée qu''après la fin du mois et le contrôle des jours ouvrables et des semaines de retards.'}
 if($PersonnelRevision -lt 0 -or $PersonnelRevision -ge [int]::MaxValue -or $SalaryRevision -lt 0 -or $SalaryRevision -ge [int]::MaxValue){throw 'Révision des sources invalide.'}
 $teacherKey=ConvertTo-ScoliaSalaryTeacherId $Preview.TeacherId
 $snapshot=[pscustomobject][ordered]@{Kind='Payroll';Id=($Preview.Month+'|'+$teacherKey);Month=$Preview.Month;Year=$Preview.Year;ContextId=$Preview.ContextId;TeacherId=$teacherKey;TeacherName=$Preview.TeacherName;State='Finalisé';GrossFC=[long]$Preview.GrossFC;WorkDates=@($Preview.WorkDates);JustifiedPaid=$true;CalendarRevision=$Preview.CalendarRevision;SalaryRecordRevision=$Preview.SalaryRecordRevision;PersonnelRevision=$PersonnelRevision;SalaryRevision=$SalaryRevision;WorkDays=$Preview.WorkDays;EligibleWorkDays=$Preview.EligibleWorkDays;UnjustifiedDays=$Preview.UnjustifiedDays;JustifiedDays=$Preview.JustifiedDays;DeductionDays=$Preview.DeductionDays;DailyRateFC=([decimal]$Preview.DailyRateFC).ToString([Globalization.CultureInfo]::InvariantCulture);DeductionsFC=[long]$Preview.DeductionsToDateFC;NetFC=[long]$Preview.NetFinalFC;Rows=(Copy-ScoliaSalaryValue @($Preview.Rows));SourceHash=$Preview.SourceHash;AsOf=$Preview.AsOf;FinalizedAt=[datetimeoffset]::UtcNow.ToString('o');FinalizedBy=$Actor;Hash=''}
 foreach($name in @('RuleVersion','LateDays','LateAbsenceDays','LateJustifiedAbsenceDays','LateUnjustifiedAbsenceDays','LateDeductibleDays','LateGroups','SourceDays','SourceRows','SourceCalendars','SourceWeeksComplete')){$snapshot | Add-Member NoteProperty $name (Copy-ScoliaSalaryValue $Preview.$name)}
 $snapshot.Hash=Get-ScoliaSalaryRecordHash (Get-ScoliaPayrollHashPayload $snapshot)
 return $snapshot
}
function Set-ScoliaPayrollSnapshot {
 param([object]$Document,[object]$Snapshot,[int]$ExpectedRevision)
 Assert-ScoliaSalaryChange -Document $Document -Record $Snapshot -ExpectedRevision $ExpectedRevision
 if($Snapshot.Kind -cne 'Payroll' -or [int]$Snapshot.SalaryRevision -ne $ExpectedRevision){throw 'La finalisation utilise une révision de salaires périmée.'}
 if(@($Document.Items | Where-Object {$_.Kind -eq 'Payroll' -and $_.Id -eq $Snapshot.Id}).Count){throw 'Cette paie est déjà finalisée. Son état ne peut pas être remplacé.'}
 $candidate=Copy-ScoliaSalaryValue $Document;$candidate.Items=@($candidate.Items)+@((Copy-ScoliaSalaryValue $Snapshot))
 Assert-ScoliaSalaryDocument $candidate
 $Document.Items=$candidate.Items
}
