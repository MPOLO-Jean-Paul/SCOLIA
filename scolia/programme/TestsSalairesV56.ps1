# Tests métier de paie isolés. Dates injectées ; aucun Excel ni fichier de production.
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Windows.Forms
. (Join-Path $PSScriptRoot 'PresencesV55.ps1')
. (Join-Path $PSScriptRoot 'PersonnelV56.ps1')
. (Join-Path $PSScriptRoot 'SalairesV56.ps1')
$script:scolia=$null;$script:salaryChecks=0
function Assert-SalaryTest([bool]$Value,[string]$Message){if(-not $Value){throw $Message};$script:salaryChecks++}
function Assert-SalaryReject([scriptblock]$Action,[string]$Message){$rejected=$false;try{[void](& $Action)}catch{$rejected=$true};Assert-SalaryTest $rejected $Message}
function New-SalaryDocument {return [pscustomobject]@{Schema=1;Context=[pscustomobject]@{Id='test-school';Year='2023-2024';LocalKey='test-user'};Revision=0;Items=@()}}
function New-SalaryTestPunch([object]$Teacher,[string]$Date,[bool]$Exit=$true){
 return [pscustomobject]@{Kind='Punch';Id=($Teacher.Id+'|'+$Date);TeacherId=$Teacher.Id;Date=$Date;EntryAt=($Date+'T07:50:00.0000000+02:00');EntryBy='test-agent';ExitAt=$(if($Exit){$Date+'T16:00:00.0000000+02:00'}else{''});ExitBy=$(if($Exit){'test-agent'}else{''})}
}
function New-SalaryTestJustification([object]$Teacher,[string]$Date){return [pscustomobject]@{Kind='Justification';Id=($Teacher.Id+'|'+$Date);TeacherId=$Teacher.Id;Date=$Date;Reason='Congé autorisé';UpdatedAt='2024-03-01T07:50:00.0000000+02:00';UpdatedBy='test-agent';Audit=@()}}
$actor='Direction / test';$month='2024-02';$year='2023-2024';$context='test-school'
$asOf=[datetimeoffset]::Parse('2024-03-01T00:00:00+02:00')
$salarySupportCalendar=New-ScoliaMonthlyCalendar -Month '2024-01' -Year $year -ContextId $context -WorkDates @() -Actor $actor
$teacher=New-ScoliaTeacher -Nom 'Enseignante A' -ActiveFrom '2023-09-01' -Actor $actor
$other=New-ScoliaTeacher -Nom 'Enseignant B' -ActiveFrom '2023-09-01' -Actor $actor
$monthWindow=Get-ScoliaSalaryMonth -Month $month -Year $year
Assert-SalaryTest ($monthWindow.DayCount -eq 29 -and $monthWindow.First.Day -eq 1 -and $monthWindow.Last.Day -eq 29) 'Le calendrier doit couvrir exactement février bissextile.'
Assert-SalaryTest ((Get-ScoliaSalaryMonth -Month '2023-02' -Year '2022-2023').DayCount -eq 28) 'Février ordinaire doit contenir exactement 28 jours.'
Assert-SalaryTest ((Get-ScoliaSalaryMonth -Month '2023-09' -Year $year).DayCount -eq 30) 'Les mois de 30 jours doivent conserver leurs bornes.'
Assert-SalaryTest ((Get-ScoliaSalaryMonth -Month '2024-01' -Year $year).DayCount -eq 31) 'Les mois de 31 jours doivent conserver leurs bornes.'
Assert-SalaryReject {Get-ScoliaSalaryMonth -Month '2024-2' -Year $year} 'Un mois non canonique doit être refusé.'
Assert-SalaryReject {Get-ScoliaSalaryMonth -Month '2024-13' -Year $year} 'Un mois impossible doit être refusé.'
Assert-SalaryReject {Get-ScoliaSalaryMonth -Month '2024-09' -Year $year} 'Un mois hors année doit être refusé.'
Assert-SalaryReject {Get-ScoliaSalaryMonth -Month '2024-02' -Year '2023-2025'} 'Une année scolaire incohérente doit être refusée.'
Assert-SalaryReject {New-ScoliaMonthlyCalendar -Month $month -Year $year -ContextId $context -WorkDates @('2024-02-30') -Actor $actor} 'Une date impossible doit être refusée.'
Assert-SalaryReject {New-ScoliaMonthlyCalendar -Month $month -Year $year -ContextId $context -WorkDates @('2024-03-01') -Actor $actor} 'Une date hors mois doit être refusée.'
Assert-SalaryReject {New-ScoliaMonthlyCalendar -Month $month -Year $year -ContextId $context -WorkDates @('2024-02-01','2024-02-01') -Actor $actor} 'Un jour dupliqué doit être refusé.'
Assert-SalaryReject {New-ScoliaMonthlyCalendar -Month $month -Year $year -ContextId $context -WorkDates @('2024-2-1') -Actor $actor} 'Les jours doivent être canoniques.'
Assert-SalaryReject {New-ScoliaMonthlySalary -Month $month -Year $year -ContextId $context -TeacherId $teacher.Id -GrossFC 0 -Actor $actor} 'Un salaire nul doit être refusé.'
Assert-SalaryReject {New-ScoliaMonthlySalary -Month $month -Year $year -ContextId $context -TeacherId $teacher.Id -GrossFC -1 -Actor $actor} 'Un salaire négatif doit être refusé.'
Assert-SalaryReject {New-ScoliaMonthlySalary -Month $month -Year $year -ContextId $context -TeacherId $teacher.Id -GrossFC 100.5 -Actor $actor} 'Le montant saisi en FC doit être entier.'
Assert-SalaryReject {New-ScoliaMonthlySalary -Month $month -Year $year -ContextId $context -TeacherId $teacher.Id -GrossFC $true -Actor $actor} 'Un booléen ne doit pas devenir un salaire.'
Assert-SalaryReject {New-ScoliaMonthlySalary -Month $month -Year $year -ContextId $context -TeacherId 'a01' -GrossFC 100 -Actor $actor} 'Un identifiant enseignant invalide doit être refusé.'
Assert-SalaryReject {New-ScoliaMonthlySalary -Month $month -Year $year -ContextId $context -TeacherId $teacher.Id -GrossFC 100 -Actor ''} 'L''auteur de la saisie doit être conservé.'
$calendar=New-ScoliaMonthlyCalendar -Month $month -Year $year -ContextId $context -WorkDates @('2024-02-29','2024-02-02','2024-02-01') -Actor $actor
Assert-SalaryTest ((@($calendar.WorkDates) -join '|') -ceq '2024-02-01|2024-02-02|2024-02-29') 'Seuls les jours explicitement cochés doivent être enregistrés et triés.'
Assert-SalaryTest $calendar.JustifiedPaid 'Les justifications doivent être rémunérées par défaut.'
$salary=New-ScoliaMonthlySalary -Month $month -Year $year -ContextId $context -TeacherId $teacher.Id -GrossFC 100 -Actor $actor
$document=New-SalaryDocument
Assert-SalaryReject {Set-ScoliaMonthlySalary -Document $document -Salary $salary -ExpectedRevision 0} 'Le calendrier mensuel doit précéder le salaire.'
Set-ScoliaMonthlyCalendar -Document $document -Calendar $calendar -ExpectedRevision 0
$document.Revision=1;Set-ScoliaMonthlySalary -Document $document -Salary $salary -ExpectedRevision 1;$document.Revision=2
$calendar=@($document.Items | Where-Object {$_.Kind -eq 'Calendar'})[0];$salary=@($document.Items | Where-Object {$_.Kind -eq 'Salary'})[0]
Assert-SalaryTest ($calendar.Revision -eq 1 -and $salary.Revision -eq 1) 'Chaque source de calcul doit posséder une révision.'
Assert-ScoliaSalaryDocument $document
$before=$document | ConvertTo-Json -Depth 32 -Compress
Assert-SalaryReject {Set-ScoliaMonthlySalary -Document $document -Salary $salary -ExpectedRevision 1} 'Une révision périmée doit empêcher l''écriture.'
Assert-SalaryTest (($document | ConvertTo-Json -Depth 32 -Compress) -ceq $before) 'Un conflit ne doit pas modifier le stockage.'
$wrong=Copy-ScoliaSalaryValue $salary;$wrong.Month='2024-01'
Assert-SalaryReject {Get-ScoliaMonthlyTeacherPayroll -Calendars @($salarySupportCalendar) -Teacher $teacher -Calendar $calendar -Salary $wrong -AsOf $asOf} 'Un salaire d''un autre mois doit être refusé.'
$wrong=Copy-ScoliaSalaryValue $salary;$wrong.ContextId='other-school'
Assert-SalaryReject {Get-ScoliaMonthlyTeacherPayroll -Calendars @($salarySupportCalendar) -Teacher $teacher -Calendar $calendar -Salary $wrong -AsOf $asOf} 'Une autre base doit être refusée.'
$p1=New-SalaryTestPunch $teacher '2024-02-01';$p2=New-SalaryTestPunch $teacher '2024-02-02' $false;$p3=New-SalaryTestPunch $teacher '2024-02-29'
$preview=Get-ScoliaMonthlyTeacherPayroll -Calendars @($salarySupportCalendar) -Teacher $teacher -Calendar $calendar -Salary $salary -Punches @($p1,$p2,$p3) -AsOf $asOf
Assert-SalaryTest ($preview.WorkDays -eq 3 -and $preview.EligibleWorkDays -eq 3 -and $preview.Rows.Count -eq 29) 'Le bilan doit couvrir le mois entier et compter seulement les jours choisis.'
Assert-SalaryTest ($preview.UnjustifiedDays -eq 1 -and $preview.DeductionDays -eq 1) 'Une seule signature dans un jour terminé doit compter une absence déductible.'
Assert-SalaryTest ($preview.NetFinalFC -eq 67 -and $preview.DeductionsToDateFC -eq 33 -and $preview.DailyRateFC -gt 33 -and $preview.DailyRateFC -lt 34) 'Le taux doit rester précis et seul le total net doit être arrondi en FC.'
$twoAbsences=Get-ScoliaMonthlyTeacherPayroll -Calendars @($salarySupportCalendar) -Teacher $teacher -Calendar $calendar -Salary $salary -Punches @($p1,$p2) -AsOf $asOf
Assert-SalaryTest ($twoAbsences.NetFinalFC -eq 33 -and $twoAbsences.DeductionsToDateFC -eq 67) 'Arrondir le taux quotidien avant deux déductions donnerait un faux net de 34 FC ; seul le net final doit être arrondi.'
$allAbsences=Get-ScoliaMonthlyTeacherPayroll -Calendars @($salarySupportCalendar) -Teacher $teacher -Calendar $calendar -Salary $salary -AsOf $asOf
Assert-SalaryTest ($allAbsences.UnjustifiedDays -eq 3 -and $allAbsences.NetFinalFC -eq 0 -and $allAbsences.DeductionsToDateFC -eq 100) 'L''absence à tous les jours ouvrables doit retenir exactement le brut, sans montant négatif.'
$halfCalendar=New-ScoliaMonthlyCalendar -Month $month -Year $year -ContextId $context -WorkDates @('2024-02-01','2024-02-02') -Actor $actor
$halfSalary=New-ScoliaMonthlySalary -Month $month -Year $year -ContextId $context -TeacherId $teacher.Id -GrossFC 101 -Actor $actor
$half=Get-ScoliaMonthlyTeacherPayroll -Calendars @($salarySupportCalendar) -Teacher $teacher -Calendar $halfCalendar -Salary $halfSalary -Punches @($p1) -AsOf $asOf
Assert-SalaryTest ($half.NetFinalFC -eq 51 -and $half.DeductionsToDateFC -eq 50) 'Un net exact de 50,5 FC doit être arrondi à 51 FC et la retenue doit équilibrer le brut.'
Assert-SalaryTest ($preview.CanFinalize -and $preview.PendingDays -eq 0) 'Un mois terminé doit être finalisable.'
Assert-SalaryTest (@($preview.Rows | Where-Object {$_.Code -eq 'NonWorking'}).Count -eq 26) 'Un jour non ouvrable ne doit produire aucune absence.'
$today=[datetimeoffset]::Parse('2024-02-02T12:00:00+02:00')
$partial=Get-ScoliaMonthlyTeacherPayroll -Calendars @($salarySupportCalendar) -Teacher $teacher -Calendar $calendar -Salary $salary -Punches @($p1,$p2) -AsOf $today
Assert-SalaryTest (-not $partial.CanFinalize -and $null -eq $partial.NetFinalFC -and $partial.PendingDays -eq 2 -and $partial.UnjustifiedDays -eq 0 -and $partial.NetToDateFC -eq 100) 'Une entrée aujourd''hui et une journée future doivent rester en attente sans déduction.'
Assert-SalaryReject {New-ScoliaPayrollSnapshot -Preview $partial -Actor $actor -PersonnelRevision 5 -SalaryRevision 2} 'Un montant provisoire ne doit pas devenir une paie finalisée.'
$lastDay=Get-ScoliaMonthlyTeacherPayroll -Calendars @($salarySupportCalendar) -Teacher $teacher -Calendar $calendar -Salary $salary -Punches @($p1,$p2,$p3) -AsOf ([datetimeoffset]::Parse('2024-02-29T20:00:00+02:00'))
Assert-SalaryTest (-not $lastDay.CanFinalize -and $null -eq $lastDay.NetFinalFC) 'La finalisation doit attendre la fin du dernier jour du mois.'
$justified=New-SalaryTestJustification $teacher '2024-02-02'
$paid=Get-ScoliaMonthlyTeacherPayroll -Calendars @($salarySupportCalendar) -Teacher $teacher -Calendar $calendar -Salary $salary -Punches @($p1,$p2,$p3) -Justifications @($justified) -AsOf $asOf
Assert-SalaryTest ($paid.JustifiedDays -eq 1 -and $paid.UnjustifiedDays -eq 0 -and $paid.NetFinalFC -eq 100) 'Une justification doit conserver l''absence sans déduction par défaut.'
$unpaidCalendar=Copy-ScoliaSalaryValue $calendar;$unpaidCalendar.JustifiedPaid=$false
$unpaid=Get-ScoliaMonthlyTeacherPayroll -Calendars @($salarySupportCalendar) -Teacher $teacher -Calendar $unpaidCalendar -Salary $salary -Punches @($p1,$p2,$p3) -Justifications @($justified) -AsOf $asOf
Assert-SalaryTest ($unpaid.DeductionDays -eq 0 -and $unpaid.NetFinalFC -eq 100 -and $unpaid.JustifiedPaid) 'Une ancienne règle non rémunérée ne doit plus couper le salaire des absences justifiées.'
$zero=New-ScoliaMonthlyCalendar -Month $month -Year $year -ContextId $context -WorkDates @() -Actor $actor
$zeroPreview=Get-ScoliaMonthlyTeacherPayroll -Calendars @($salarySupportCalendar) -Teacher $teacher -Calendar $zero -Salary $salary -AsOf $asOf
Assert-SalaryTest (-not $zeroPreview.CalculationAvailable -and $null -eq $zeroPreview.DailyRateFC -and $null -eq $zeroPreview.NetToDateFC -and -not $zeroPreview.CanFinalize) 'Un mois sans jour ouvrable ne doit jamais diviser par zéro ou produire un montant payable.'
$newTeacher=New-ScoliaTeacher -Nom 'Arrivée fin de mois' -ActiveFrom '2024-02-29' -Id $teacher.Id -Actor $actor
$midMonth=Get-ScoliaMonthlyTeacherPayroll -Calendars @($salarySupportCalendar) -Teacher $newTeacher -Calendar $calendar -Salary $salary -Punches @($p3) -AsOf $asOf
Assert-SalaryTest ($midMonth.EligibleWorkDays -eq 1 -and $midMonth.WorkDays -eq 3 -and $midMonth.UnjustifiedDays -eq 0 -and $midMonth.NetFinalFC -eq 100) 'Les jours avant l''entrée dans le personnel ne doivent pas compter en absence ; le brut saisi reste celui du mois.'
$inactiveTeacher=New-ScoliaTeacher -Nom 'Arrivée mois suivant' -ActiveFrom '2024-03-01' -Id $teacher.Id -Actor $actor
$inactive=Get-ScoliaMonthlyTeacherPayroll -Calendars @($salarySupportCalendar) -Teacher $inactiveTeacher -Calendar $calendar -Salary $salary -AsOf $asOf
Assert-SalaryTest ($inactive.EligibleWorkDays -eq 0 -and -not $inactive.CanFinalize -and -not $inactive.CalculationAvailable) 'Un enseignant sans jour admissible ne doit pas obtenir de paie finalisée automatiquement.'
$foreign=New-SalaryTestPunch $other '2024-02-02'
$filtered=Get-ScoliaMonthlyTeacherPayroll -Calendars @($salarySupportCalendar) -Teacher $teacher -Calendar $calendar -Salary $salary -PersonnelItems @($teacher,$other,$p1,$p2,$p3,$foreign) -AsOf $asOf
Assert-SalaryTest ($filtered.NetFinalFC -eq 67) 'Le registre complet doit filtrer le personnel et les signatures des autres enseignants.'
Assert-SalaryReject {Get-ScoliaMonthlyTeacherPayroll -Calendars @($salarySupportCalendar) -Teacher $teacher -Calendar $calendar -Salary $salary -Punches @($p1,$p1) -AsOf $asOf} 'Des signatures dupliquées doivent arrêter le calcul.'
$changed=New-ScoliaMonthlyCalendar -Month $month -Year $year -ContextId $context -WorkDates @('2024-02-01','2024-02-02','2024-02-03') -JustifiedPaid $false -Actor $actor
$diff=Get-ScoliaCalendarDifference -Previous $calendar -Proposed $changed
Assert-SalaryTest ($diff.Added.Count -eq 1 -and $diff.Added[0] -eq '2024-02-03' -and $diff.Removed.Count -eq 1 -and $diff.Removed[0] -eq '2024-02-29' -and -not $diff.PaymentRuleChanged -and $changed.JustifiedPaid) 'Une modification de calendrier doit fournir les dates ajoutées et supprimées ; les justifications restent toujours payées.'
$snapshot=New-ScoliaPayrollSnapshot -Preview $preview -Actor $actor -PersonnelRevision 9 -SalaryRevision 2
Set-ScoliaPayrollSnapshot -Document $document -Snapshot $snapshot -ExpectedRevision 2;$document.Revision=3
Assert-ScoliaSalaryDocument $document
$stored=@($document.Items | Where-Object {$_.Kind -eq 'Payroll'})[0]
Assert-SalaryTest ($stored.State -eq 'Finalisé' -and $stored.NetFC -eq 67 -and $stored.PersonnelRevision -eq 9 -and $stored.FinalizedBy -eq $actor -and $stored.Rows.Count -eq 29) 'La finalisation doit conserver les sources, auteurs, montants et le détail quotidien.'
$before=$document | ConvertTo-Json -Depth 32 -Compress
Assert-SalaryReject {Set-ScoliaPayrollSnapshot -Document $document -Snapshot $snapshot -ExpectedRevision 3} 'Une paie finalisée ne doit jamais être remplacée.'
Assert-SalaryReject {Set-ScoliaMonthlyCalendar -Document $document -Calendar $changed -ExpectedRevision 3} 'Une finalisation doit figer le calendrier du mois.'
Assert-SalaryReject {Set-ScoliaMonthlySalary -Document $document -Salary $salary -ExpectedRevision 3} 'Une finalisation doit figer le salaire de cet enseignant et de ce mois.'
Assert-SalaryTest (($document | ConvertTo-Json -Depth 32 -Compress) -ceq $before) 'Les modifications refusées ne doivent pas altérer la paie finalisée.'
$changedAmount=Copy-ScoliaSalaryValue $document;@($changedAmount.Items | Where-Object {$_.Kind -eq 'Payroll'})[0].NetFC=68
Assert-SalaryReject {Assert-ScoliaSalaryDocument $changedAmount} 'Une paie finalisée altérée doit être signalée.'
$changedAmount=Copy-ScoliaSalaryValue $document;$badSnapshot=@($changedAmount.Items | Where-Object {$_.Kind -eq 'Payroll'})[0];$badSnapshot.NetFC=68;$badSnapshot.DeductionsFC=32;$badSnapshot.Hash=Get-ScoliaSalaryRecordHash (Get-ScoliaPayrollHashPayload $badSnapshot)
Assert-SalaryReject {Assert-ScoliaSalaryDocument $changedAmount} 'Une somme incohérente doit être refusée même avec une empreinte recalculée.'
$wrongContext=Copy-ScoliaSalaryValue $document;@($wrongContext.Items | Where-Object {$_.Kind -eq 'Calendar'})[0].ContextId='other-school'
Assert-SalaryReject {Assert-ScoliaSalaryDocument $wrongContext} 'Les données d''une autre base doivent être refusées.'
$duplicate=Copy-ScoliaSalaryValue $document;$duplicate.Items=@($duplicate.Items)+@($duplicate.Items[0])
Assert-SalaryReject {Assert-ScoliaSalaryDocument $duplicate} 'Les calendriers dupliqués doivent être refusés.'
$updatedDocument=New-SalaryDocument;Set-ScoliaMonthlyCalendar -Document $updatedDocument -Calendar $calendar -ExpectedRevision 0;Set-ScoliaMonthlySalary -Document $updatedDocument -Salary $salary -ExpectedRevision 0
Set-ScoliaMonthlyCalendar -Document $updatedDocument -Calendar $changed -ExpectedRevision 0
$record=@($updatedDocument.Items | Where-Object {$_.Kind -eq 'Calendar'})[0]
Assert-SalaryTest ($record.Revision -eq 2 -and $record.Audit.Count -eq 1 -and $record.Audit[0].PreviousWorkDates.Count -eq 3) 'La modification des jours doit être auditée avant une finalisation.'
$newSalary=New-ScoliaMonthlySalary -Month $month -Year $year -ContextId $context -TeacherId $teacher.Id -GrossFC 200 -Actor $actor
Set-ScoliaMonthlySalary -Document $updatedDocument -Salary $newSalary -ExpectedRevision 0
$record=@($updatedDocument.Items | Where-Object {$_.Kind -eq 'Salary'})[0]
Assert-SalaryTest ($record.Revision -eq 2 -and $record.Audit[0].PreviousGrossFC -eq 100) 'La modification de salaire doit conserver son ancien montant et l''auteur.'
# Le résultat doit conserver le même arrondi final dans les cultures française et anglaise.
$savedCulture=[Threading.Thread]::CurrentThread.CurrentCulture
try{
 foreach($culture in @('fr-FR','en-US')){
  [Threading.Thread]::CurrentThread.CurrentCulture=[Globalization.CultureInfo]::GetCultureInfo($culture)
  $same=Get-ScoliaMonthlyTeacherPayroll -Calendars @($salarySupportCalendar) -Teacher $teacher -Calendar $calendar -Salary $salary -Punches @($p1,$p2,$p3) -AsOf $asOf
  Assert-SalaryTest ($same.NetFinalFC -eq 67) 'La langue du poste ne doit pas modifier le salaire.'
 }
}finally{[Threading.Thread]::CurrentThread.CurrentCulture=$savedCulture}
Write-Output ('Tests salaires V56 : '+$script:salaryChecks+' contrôles réussis, dates injectées, aucun versement ni accès aux données de production.')
