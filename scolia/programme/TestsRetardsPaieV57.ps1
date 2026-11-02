# Tests de paie V57 : horloges injectées, dossiers en mémoire, aucun Excel ni paiement.
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Windows.Forms
. (Join-Path $PSScriptRoot 'PresencesV55.ps1')
. (Join-Path $PSScriptRoot 'PersonnelV56.ps1')
. (Join-Path $PSScriptRoot 'SalairesV56.ps1')
$script:scolia=$null;$script:latePayrollChecks=0
function Assert-LatePayroll([bool]$Value,[string]$Message){if(-not $Value){throw $Message};$script:latePayrollChecks++}
function Assert-LatePayrollReject([scriptblock]$Action,[string]$Message){$rejected=$false;try{[void](& $Action)}catch{$rejected=$true};Assert-LatePayroll $rejected $Message}
$actor='Direction / test';$year='2023-2024';$context='test-school'
$teacher=New-ScoliaTeacher -Nom 'Enseignant retards' -ActiveFrom '2023-09-01' -Actor $actor
$ended=[datetimeoffset]::Parse('2024-03-01T00:00:00+02:00')
$previousEmpty=New-ScoliaMonthlyCalendar -Month '2024-01' -Year $year -ContextId $context -WorkDates @() -Actor $actor
function New-LatePayrollCalendar([string[]]$Dates,[string]$Month='2024-02'){
 return New-ScoliaMonthlyCalendar -Month $Month -Year $year -ContextId $context -WorkDates $Dates -Actor $actor
}
function New-LatePayrollPunch([string]$Date,[string]$Entry='08:00:00',[bool]$Complete=$true){
 return [pscustomobject]@{Kind='Punch';Id=($teacher.Id+'|'+$Date);TeacherId=$teacher.Id;Date=$Date;EntryAt=($Date+'T'+$Entry+'.0000000+02:00');EntryBy=$actor;ExitAt=$(if($Complete){$Date+'T16:00:00.0000000+02:00'}else{''});ExitBy=$(if($Complete){$actor}else{''})}
}
function New-LatePayrollReason([string]$Date,[string]$Reason='Mission autorisée'){
 return [pscustomobject]@{Kind='Justification';Id=($teacher.Id+'|'+$Date);TeacherId=$teacher.Id;Date=$Date;Reason=$Reason;UpdatedAt='2024-03-01T08:00:00.0000000+02:00';UpdatedBy=$actor;Audit=@()}
}
function Get-LatePayroll([object]$Calendar,[object[]]$Punches=@(),[object[]]$Reasons=@(),[object[]]$OtherCalendars=@($previousEmpty),[datetimeoffset]$Now=$ended){
 $salary=New-ScoliaMonthlySalary -Month $Calendar.Month -Year $Calendar.Year -ContextId $Calendar.ContextId -TeacherId $teacher.Id -GrossFC (@($Calendar.WorkDates).Count*10000) -Actor $actor -StartMonth $Calendar.StartMonth
 return Get-ScoliaMonthlyTeacherPayroll -Teacher $teacher -Calendar $Calendar -Calendars $OtherCalendars -Salary $salary -Punches $Punches -Justifications $Reasons -AsOf $Now
}
$threeDates=@('2024-02-05','2024-02-06','2024-02-07')
$three=New-LatePayrollCalendar $threeDates
$latePunches=@($threeDates | ForEach-Object {New-LatePayrollPunch $_})
$onTime=@($threeDates | ForEach-Object {New-LatePayrollPunch $_ '07:59:59'})
$plain=Get-LatePayroll -Calendar $three -Punches $onTime
Assert-LatePayroll ($plain.LateDays -eq 0 -and $plain.LateAbsenceDays -eq 0 -and $plain.NetFinalFC -eq 30000) 'Une entrée à 07:59:59 ne doit pas être en retard.'
$converted=Get-LatePayroll -Calendar $three -Punches $latePunches
Assert-LatePayroll ($converted.LateDays -eq 3 -and $converted.LateAbsenceDays -eq 1 -and $converted.LateUnjustifiedAbsenceDays -eq 1 -and $converted.LateDeductibleDays -eq 1 -and $converted.UnjustifiedDays -eq 0 -and $converted.DeductionDays -eq 1 -and $converted.NetFinalFC -eq 20000) 'À 08:00:00, trois entrées tardives de la même semaine doivent produire une journée de retenue.'
$third=@($converted.Rows | Where-Object {$_.Date -eq '2024-02-07'})[0]
Assert-LatePayroll ($third.LatePenalty -and $third.PenaltyWeekStart -eq '2024-02-05' -and ($third.ConvertedDates -join '|') -eq ($threeDates -join '|')) 'La conversion doit conserver les trois dates et être attribuée au troisième retard.'
Assert-LatePayroll (-not $third.IsAbsent -and $third.Code -eq 'Late') 'Une équivalence financière ne doit pas effacer la présence physique.'
Assert-LatePayroll (@($converted.Rows | Where-Object {$_.Deductible}).Count -eq 1) 'La retenue doit être matérialisée une seule fois dans le détail du mois.'
$twoOnly=@((New-LatePayrollPunch '2024-02-05'),(New-LatePayrollPunch '2024-02-06'),(New-LatePayrollPunch '2024-02-07' '07:50:00'))
$two=Get-LatePayroll -Calendar $three -Punches $twoOnly
Assert-LatePayroll ($two.LateDays -eq 2 -and $two.LateAbsenceDays -eq 0 -and $two.DeductionDays -eq 0) 'Deux retards ne doivent pas produire une absence équivalente.'
$sixDates=@('2024-02-05','2024-02-06','2024-02-07','2024-02-08','2024-02-09','2024-02-10','2024-02-11')
$sixCalendar=New-LatePayrollCalendar $sixDates
$sixPunches=@($sixDates[0..5] | ForEach-Object {New-LatePayrollPunch $_})+@((New-LatePayrollPunch '2024-02-11' '07:50:00'))
$six=Get-LatePayroll -Calendar $sixCalendar -Punches $sixPunches
Assert-LatePayroll ($six.LateDays -eq 6 -and $six.LateAbsenceDays -eq 2 -and $six.DeductionDays -eq 2 -and $six.NetFinalFC -eq 50000) 'Six retards dans la semaine doivent constituer deux absences équivalentes.'
Assert-LatePayroll (($six.LateGroups.AssignedDate -join '|') -eq '2024-02-07|2024-02-10') 'Les conversions doivent être affectées aux troisième et sixième retards.'
$splitDates=@('2024-02-10','2024-02-11','2024-02-12')
$split=Get-LatePayroll -Calendar (New-LatePayrollCalendar $splitDates) -Punches @($splitDates | ForEach-Object {New-LatePayrollPunch $_})
Assert-LatePayroll ($split.LateDays -eq 3 -and $split.LateAbsenceDays -eq 0 -and $split.NetFinalFC -eq 30000) 'Le compteur doit se réinitialiser le lundi, sans reporter les deux retards du week-end.'
$sundayDates=@('2024-02-09','2024-02-10','2024-02-11')
$sunday=Get-LatePayroll -Calendar (New-LatePayrollCalendar $sundayDates) -Punches @($sundayDates | ForEach-Object {New-LatePayrollPunch $_})
Assert-LatePayroll ($sunday.LateAbsenceDays -eq 1 -and $sunday.LateGroups[0].AssignedDate -eq '2024-02-11') 'Le dimanche ouvrable doit appartenir à la semaine commencée le lundi précédent.'
$today=[datetimeoffset]::Parse('2024-02-07T12:00:00+02:00')
$pendingPunches=@($latePunches[0],$latePunches[1],(New-LatePayrollPunch '2024-02-07' '08:30:00' $false))
$pending=Get-LatePayroll -Calendar $three -Punches $pendingPunches -Now $today
Assert-LatePayroll ($pending.LateAbsenceDays -eq 0 -and $pending.DeductionDays -eq 0 -and $pending.PendingDays -eq 1 -and -not $pending.CanFinalize) 'Une entrée tardive sans sortie aujourd''hui reste en attente et ne déclenche pas encore une retenue.'
$afterClose=Get-LatePayroll -Calendar $three -Punches $pendingPunches
Assert-LatePayroll ($afterClose.UnjustifiedDays -eq 1 -and $afterClose.LateDays -eq 3 -and $afterClose.LateAbsenceDays -eq 1 -and $afterClose.LateDeductibleDays -eq 0 -and $afterClose.DeductionDays -eq 1) 'La troisième entrée tardive incomplète devient une absence ; la même journée ne doit pas être retenue deux fois.'
$mixedPunches=@((New-LatePayrollPunch '2024-02-05' '08:10:00' $false),$latePunches[1],$latePunches[2])
$mixed=Get-LatePayroll -Calendar $three -Punches $mixedPunches
Assert-LatePayroll ($mixed.UnjustifiedDays -eq 1 -and $mixed.LateAbsenceDays -eq 1 -and $mixed.LateDeductibleDays -eq 1 -and $mixed.DeductionDays -eq 2 -and $mixed.NetFinalFC -eq 10000) 'Une absence physique le premier retard et une équivalence au troisième doivent retenir deux dates distinctes.'
$incompletePunches=@($threeDates | ForEach-Object {New-LatePayrollPunch $_ '08:10:00' $false})
$incomplete=Get-LatePayroll -Calendar $three -Punches $incompletePunches
Assert-LatePayroll ($incomplete.UnjustifiedDays -eq 3 -and $incomplete.LateAbsenceDays -eq 1 -and $incomplete.LateDeductibleDays -eq 0 -and $incomplete.DeductionDays -eq 3 -and $incomplete.NetFinalFC -eq 0) 'Trois entrées incomplètes en retard comptent trois absences physiques et une équivalence sans quatrième retenue.'
$excusedIncomplete=Get-LatePayroll -Calendar $three -Punches $mixedPunches -Reasons @((New-LatePayrollReason '2024-02-05'))
Assert-LatePayroll ($excusedIncomplete.JustifiedDays -eq 1 -and $excusedIncomplete.LateDays -eq 2 -and $excusedIncomplete.LateAbsenceDays -eq 0 -and $excusedIncomplete.DeductionDays -eq 0 -and $excusedIncomplete.NetFinalFC -eq 30000) 'Une absence physique justifiée reste comptée et payée, et ne doit pas servir à créer une retenue de retard.'
$reasonThird=New-LatePayrollReason '2024-02-07' 'Formation validée par la direction'
$excusedGroup=Get-LatePayroll -Calendar $three -Punches $latePunches -Reasons @($reasonThird)
Assert-LatePayroll ($excusedGroup.LateAbsenceDays -eq 1 -and $excusedGroup.LateJustifiedAbsenceDays -eq 1 -and $excusedGroup.LateUnjustifiedAbsenceDays -eq 0 -and $excusedGroup.DeductionDays -eq 0 -and $excusedGroup.NetFinalFC -eq 30000) 'Une justification sur le troisième retard conserve l''équivalence et son motif, sans couper le salaire.'
Assert-LatePayroll ($excusedGroup.LateGroups[0].Reason -eq $reasonThird.Reason -and @($excusedGroup.Rows | Where-Object {$_.LatePenalty})[0].LatePenaltyReason -eq $reasonThird.Reason) 'Le motif de la conversion payée doit figurer dans le groupe et le détail quotidien.'
$reasonFirst=Get-LatePayroll -Calendar $three -Punches $latePunches -Reasons @((New-LatePayrollReason '2024-02-05'))
Assert-LatePayroll ($reasonFirst.LateAbsenceDays -eq 1 -and $reasonFirst.DeductionDays -eq 1) 'Un motif porté sur le premier retard ne doit pas justifier automatiquement la conversion attribuée au troisième.'
$oldUnpaid=Copy-ScoliaSalaryValue $three;$oldUnpaid.JustifiedPaid=$false
$oldCalendarBytes=ConvertTo-Json -InputObject $oldUnpaid -Depth 32 -Compress
$absencePaid=Get-LatePayroll -Calendar $oldUnpaid -Punches @($onTime[0],$onTime[1]) -Reasons @((New-LatePayrollReason '2024-02-07'))
Assert-LatePayroll ($absencePaid.JustifiedDays -eq 1 -and $absencePaid.DeductionDays -eq 0 -and $absencePaid.NetFinalFC -eq 30000 -and $absencePaid.JustifiedPaid) 'La nouvelle règle doit payer les absences justifiées même si un ancien calendrier autorisait une retenue.'
Assert-LatePayroll ((ConvertTo-Json -InputObject $oldUnpaid -Depth 32 -Compress) -ceq $oldCalendarBytes) 'La prévisualisation ne doit pas modifier un calendrier historique.'
$forced=New-ScoliaMonthlyCalendar -Month '2024-02' -Year $year -ContextId $context -WorkDates $threeDates -JustifiedPaid $false -Actor $actor
Assert-LatePayroll $forced.JustifiedPaid 'Tout calendrier nouvellement enregistré doit conserver les justifications payées.'
$janCalendar=New-LatePayrollCalendar @('2024-01-30','2024-01-31') '2024-01'
$decCalendar=New-LatePayrollCalendar @() '2023-12'
$febCalendar=New-LatePayrollCalendar @('2024-02-01')
$crossPunches=@((New-LatePayrollPunch '2024-01-30'),(New-LatePayrollPunch '2024-01-31'),(New-LatePayrollPunch '2024-02-01'))
$jan=Get-LatePayroll -Calendar $janCalendar -Punches $crossPunches -OtherCalendars @($decCalendar) -Now $ended
$feb=Get-LatePayroll -Calendar $febCalendar -Punches $crossPunches -OtherCalendars @($janCalendar)
Assert-LatePayroll ($jan.LateAbsenceDays -eq 0 -and $feb.LateAbsenceDays -eq 1 -and $feb.LateGroups[0].AssignedDate -eq '2024-02-01' -and ($feb.LateGroups[0].Dates -join '|') -eq '2024-01-30|2024-01-31|2024-02-01') 'Une semaine traversant deux mois ne doit pas perdre ses retards ; la retenue appartient au mois du troisième retard.'
Assert-LatePayroll ($feb.SourceDays[0] -eq '2024-01-29' -and $feb.SourceDays[-1] -eq '2024-02-29' -and $feb.SourceCalendars.Count -eq 2) 'Les sources doivent inclure le début de semaine du mois précédent et ne jamais figer le mois suivant.'
# Un mois historique qui ne participe pas aux retards ne doit pas bloquer le calcul.
$unrelatedCalendar=New-ScoliaMonthlyCalendar -Month '2023-10' -Year $year -ContextId $context -WorkDates @('2023-10-02') -StartMonth 1 -Actor $actor
$isolationSalary=New-ScoliaMonthlySalary -Month $febCalendar.Month -Year $year -ContextId $context -TeacherId $teacher.Id -GrossFC 10000 -Actor $actor -StartMonth $febCalendar.StartMonth
$baseline=Get-ScoliaMonthlyTeacherPayroll -Teacher $teacher -Calendar $febCalendar -Calendars @($janCalendar) -Salary $isolationSalary -Punches $crossPunches -AsOf $ended
$unrelated=Get-ScoliaMonthlyTeacherPayroll -Teacher $teacher -Calendar $febCalendar -Calendars @($janCalendar,$unrelatedCalendar) -Salary $isolationSalary -Punches $crossPunches -AsOf $ended
Assert-LatePayroll ($unrelated.CanFinalize -and $unrelated.SourceWeeksComplete -and $unrelated.NetFinalFC -eq $baseline.NetFinalFC -and $unrelated.LateAbsenceDays -eq $baseline.LateAbsenceDays -and $unrelated.SourceHash -ceq $baseline.SourceHash -and $unrelated.SourceCalendars.Count -eq 2) 'Un calendrier hors des semaines sources, avec un autre mois de rentrée, ne doit modifier ni le calcul ni son empreinte.'
$wrongSourceCalendar=Copy-ScoliaSalaryValue $janCalendar;$wrongSourceCalendar.StartMonth=1
Assert-LatePayrollReject {Get-LatePayroll -Calendar $febCalendar -Punches $crossPunches -OtherCalendars @($wrongSourceCalendar)} 'Un calendrier réellement utilisé dans la première semaine doit toujours conserver le même mois de rentrée.'
$missing=Get-LatePayroll -Calendar $febCalendar -Punches $crossPunches -OtherCalendars @()
Assert-LatePayroll (-not $missing.SourceWeeksComplete -and -not $missing.CanFinalize -and $missing.LateAbsenceDays -eq 0) 'Un calendrier précédent manquant doit bloquer la finalisation sans inventer une conversion.'
Assert-LatePayrollReject {New-ScoliaPayrollSnapshot -Preview $missing -Actor $actor -PersonnelRevision 0 -SalaryRevision 0} 'Une semaine incomplète ne doit pas devenir un salaire final.'
$notWorking=New-LatePayrollCalendar @('2024-02-05','2024-02-06')
$ignoredDay=Get-LatePayroll -Calendar $notWorking -Punches $latePunches
Assert-LatePayroll ($ignoredDay.LateDays -eq 2 -and $ignoredDay.LateAbsenceDays -eq 0) 'Une entrée enregistrée un jour non ouvrable ne doit pas alimenter la conversion.'
$teacherSaved=$teacher
try{
 $teacher=New-ScoliaTeacher -Nom 'Enseignant tardif' -ActiveFrom '2024-02-06' -Id $teacherSaved.Id -Actor $actor
 $inactiveIgnored=Get-LatePayroll -Calendar $three -Punches @($latePunches[1],$latePunches[2])
 Assert-LatePayroll ($inactiveIgnored.LateDays -eq 2 -and $inactiveIgnored.LateAbsenceDays -eq 0 -and $inactiveIgnored.DeductionDays -eq 0) 'Un jour avant la période d''activité ne doit pas alimenter une conversion.'
}finally{$teacher=$teacherSaved}
$snapshot=New-ScoliaPayrollSnapshot -Preview $converted -Actor $actor -PersonnelRevision 0 -SalaryRevision 0
Assert-ScoliaPayrollCalculation -Snapshot $snapshot -Calendar $three
Assert-LatePayroll ($snapshot.RuleVersion -eq 2 -and $snapshot.LateDays -eq 3 -and $snapshot.SourceRows.Count -eq 32 -and $snapshot.SourceWeeksComplete) 'La finalisation doit conserver la règle V57 et les journées sources.'
$salary=New-ScoliaMonthlySalary -Month $three.Month -Year $year -ContextId $context -TeacherId $teacher.Id -GrossFC 30000 -Actor $actor
$document=[pscustomobject]@{Schema=1;Context=[pscustomobject]@{Id=$context;Year=$year;LocalKey='test-user'};Revision=0;Items=@($three,$salary,$snapshot)}
Assert-ScoliaSalaryDocument $document
Assert-LatePayroll ($document.Items.Count -eq 3) 'Une paie finalisée doit pouvoir être relue avec les nouveaux détails.'
$mutated=Copy-ScoliaSalaryValue $snapshot;$mutated.LateAbsenceDays=2;$mutated.Hash=Get-ScoliaSalaryRecordHash (Get-ScoliaPayrollHashPayload $mutated)
Assert-LatePayrollReject {Assert-ScoliaPayrollCalculation -Snapshot $mutated -Calendar $three} 'Une conversion ajoutée à la main doit être détectée, même avec une empreinte recalculée.'
$mutated=Copy-ScoliaSalaryValue $snapshot;$mutated.SourceRows[0].LateRecorded=$true;$mutated.Hash=Get-ScoliaSalaryRecordHash (Get-ScoliaPayrollHashPayload $mutated)
Assert-LatePayrollReject {Assert-ScoliaPayrollCalculation -Snapshot $mutated -Calendar $three} 'Un retard ajouté à un jour sans entrée doit être refusé.'
$mutated=Copy-ScoliaSalaryValue $snapshot;$mutated.Rows[0].Deductible=$true;$mutated.Hash=Get-ScoliaSalaryRecordHash (Get-ScoliaPayrollHashPayload $mutated)
Assert-LatePayrollReject {Assert-ScoliaPayrollCalculation -Snapshot $mutated -Calendar $three} 'Une retenue ajoutée au détail doit être détectée.'
$emptySnapshot=New-ScoliaPayrollSnapshot -Preview $plain -Actor $actor -PersonnelRevision 0 -SalaryRevision 0
Assert-ScoliaPayrollCalculation -Snapshot $emptySnapshot -Calendar $three
Assert-LatePayroll (@($emptySnapshot.LateGroups).Count -eq 0) 'Une liste vide de conversions doit rester vide après la sérialisation.'
# Compatibilité : une paie V56 déjà finalisée garde sa règle et son empreinte.
$legacy=Copy-ScoliaSalaryValue $emptySnapshot
foreach($name in @('RuleVersion','LateDays','LateAbsenceDays','LateJustifiedAbsenceDays','LateUnjustifiedAbsenceDays','LateDeductibleDays','LateGroups','SourceDays','SourceRows','SourceCalendars','SourceWeeksComplete')){$legacy.PSObject.Properties.Remove($name)}
foreach($row in @($legacy.Rows)){foreach($name in @('LateRecorded','IsLate','LatePenalty','LatePenaltyJustified','LatePenaltyReason','PenaltyWeekStart','ConvertedDates')){$row.PSObject.Properties.Remove($name)}}
$legacy.Hash=Get-ScoliaSalaryRecordHash (Get-ScoliaPayrollHashPayload $legacy)
$legacyJson=ConvertTo-Json -InputObject $legacy -Depth 32 -Compress
Assert-ScoliaPayrollCalculation -Snapshot $legacy -Calendar $three
Assert-LatePayroll ((ConvertTo-Json -InputObject $legacy -Depth 32 -Compress) -ceq $legacyJson) 'La lecture d''une paie V56 ne doit ni recalculer son montant ni ajouter la règle V57.'
Assert-LatePayroll (-not (Get-ScoliaPayrollHashPayload $legacy).PSObject.Properties['RuleVersion']) 'Une paie V56 doit garder exactement la structure de son empreinte d''origine.'
Write-Output ('Tests retards / paie V57 : '+$script:latePayrollChecks+' contrôles réussis, dates injectées et aucun accès aux données réelles.')
