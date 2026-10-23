# Paies V57 finalisées : les sources de la première semaine restent cohérentes entre mois.
# Fixtures temporaires, sans Excel ni données réelles. Exécution native Windows requise.
$ErrorActionPreference='Stop'
foreach($name in @('FonctionnalitesV55.ps1','PresencesV55.ps1','PersonnelV56.ps1','SalairesV56.ps1','ServicesPersonnelV56.ps1')){. (Join-Path $PSScriptRoot $name)}
$tokens=$null;$errors=$null;$ast=[Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot 'StockageClasseurs.ps1'),[ref]$tokens,[ref]$errors)
if($errors.Count){throw 'Syntaxe du stockage invalide.'}
$node=$ast.Find({param($n)$n -is [Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -eq 'Write-ITMAtomicJson'},$true);. ([scriptblock]::Create($node.Extent.Text))
function Assert-SourcesPaie([bool]$Value,[string]$Message){if(-not $Value){throw $Message};$script:sourcesPaieChecks++}
function Reject-SourcesPaie([scriptblock]$Action,[string]$Message){$failed=$false;try{[void](& $Action)}catch{$failed=$true};Assert-SourcesPaie $failed $Message}
function Assert-ITMPermission([string]$Permission){}
$script:sourcesPaieChecks=0
$root=Join-Path ([IO.Path]::GetTempPath()) ('SCOLIA-sources-paie-tests-'+[guid]::NewGuid().ToString('N'));[void][IO.Directory]::CreateDirectory($root)
$year='2023-2024';$month='2024-02'
$script:storeFolder=$root;$script:store=[pscustomobject]@{Id=[guid]::NewGuid().ToString('N')};$script:book=[pscustomobject]@{FullName=(Join-Path $root 'test.xlsm')}
$script:scolia=[pscustomobject]@{Id=[guid]::NewGuid().ToString('N');StartMonth=9};$script:accountSession=[pscustomobject]@{Id='test';Name='Test';Role='Administrateur'}
$script:data=[pscustomobject]@{closed=$false;promotions=@([pscustomobject]@{Active=$true;Annee=$year})};$context=Get-ScoliaFeatureContext;$actor=Get-ScoliaFeatureActor
try{
 $teacher=New-ScoliaTeacher -Nom 'Semaine entre deux mois' -ActiveFrom '2023-09-01' -Actor $actor
 [void](Invoke-ScoliaFeatureUpdate -Name personnel -Change {param($document)Set-ScoliaTeacher -Document $document -Teacher $teacher -Actor $actor -ExpectedRevision $document.Revision})
 # Les dates passées sont construites directement comme fixtures pour ne pas antidater le service.
 $personnel=Read-ScoliaFeatureStore -Name personnel
 foreach($date in @('2024-01-30','2024-01-31','2024-02-01')){
  $entry=[datetimeoffset]::Parse($date+'T08:00:00+02:00')
  Set-ScoliaTeacherPunch -Document $personnel -TeacherId $teacher.Id -Direction Entry -Now $entry -Actor $actor -ExpectedRevision $personnel.Revision
  Set-ScoliaTeacherPunch -Document $personnel -TeacherId $teacher.Id -Direction Exit -Now $entry.AddHours(8) -Actor $actor -ExpectedRevision $personnel.Revision
 }
 Write-ITMAtomicJson (Get-ScoliaFeatureStorePath -Name personnel) $personnel
 $feb=New-ScoliaMonthlyCalendar -Month $month -Year $year -ContextId $context.Id -WorkDates @('2024-02-01') -Actor $actor
 [void](Invoke-ScoliaFeatureUpdate -Name salaires -Change {param($document)Set-ScoliaMonthlyCalendar -Document $document -Calendar $feb -ExpectedRevision $document.Revision})
 $salary=New-ScoliaMonthlySalary -Month $month -Year $year -ContextId $context.Id -TeacherId $teacher.Id -GrossFC 10000 -Actor $actor
 [void](Invoke-ScoliaFeatureUpdate -Name salaires -Change {param($document)Set-ScoliaMonthlySalary -Document $document -Salary $salary -ExpectedRevision $document.Revision})
 $salaryPath=Get-ScoliaFeatureStorePath -Name salaires;$salaryHash=(Get-FileHash $salaryPath -Algorithm SHA256).Hash
 Reject-SourcesPaie {Invoke-ScoliaFinalizeMonthlyPayroll $month} 'La finalisation accepte une première semaine sans calendrier de janvier.'
 Assert-SourcesPaie ((Get-FileHash $salaryPath -Algorithm SHA256).Hash -ceq $salaryHash) 'Le refus de semaine incomplète altère le document des salaires.'
 $jan=New-ScoliaMonthlyCalendar -Month '2024-01' -Year $year -ContextId $context.Id -WorkDates @('2024-01-30','2024-01-31') -Actor $actor
 [void](Invoke-ScoliaFeatureUpdate -Name salaires -Change {param($document)Set-ScoliaMonthlyCalendar -Document $document -Calendar $jan -ExpectedRevision $document.Revision})
 $final=Invoke-ScoliaFinalizeMonthlyPayroll $month;$pay=@($final.Items|Where-Object {$_.Kind -eq 'Payroll'})[0]
 Assert-SourcesPaie ($pay.RuleVersion -eq 2 -and $pay.SourceWeeksComplete -and $pay.LateAbsenceDays -eq 1 -and $pay.NetFC -eq 0) 'La finalisation ne récupère pas les deux retards de janvier et le troisième de février.'
 Assert-SourcesPaie ($pay.SourceDays[0] -eq '2024-01-29' -and $pay.SourceDays[-1] -eq '2024-02-29') 'La finalisation gèle une fenêtre de sources incorrecte.'
 $personnelPath=Get-ScoliaFeatureStorePath -Name personnel;$personnelHash=(Get-FileHash $personnelPath -Algorithm SHA256).Hash
 Reject-SourcesPaie {Invoke-ScoliaFeatureUpdate -Name personnel -Change {param($document)Set-ScoliaTeacherJustification -Document $document -TeacherId $teacher.Id -Date '2024-01-30' -Reason 'Correction après finalisation' -Actor $actor -ExpectedRevision $document.Revision}} 'Un motif de janvier utilisé par la paie de février reste modifiable.'
 Assert-SourcesPaie ((Get-FileHash $personnelPath -Algorithm SHA256).Hash -ceq $personnelHash) 'Une justification refusée est tout de même enregistrée.'
 $changedJan=New-ScoliaMonthlyCalendar -Month '2024-01' -Year $year -ContextId $context.Id -WorkDates @('2024-01-31') -Actor $actor
 Reject-SourcesPaie {Invoke-ScoliaFeatureUpdate -Name salaires -Change {param($document)Set-ScoliaMonthlyCalendar -Document $document -Calendar $changedJan -ExpectedRevision $document.Revision}} 'La suppression d''un jour source de janvier est acceptée.'
 $changedTeacher=New-ScoliaTeacher -Id $teacher.Id -Nom $teacher.Nom -ActiveFrom '2024-02-01' -Actor $actor
 Reject-SourcesPaie {Invoke-ScoliaFeatureUpdate -Name personnel -Change {param($document)Set-ScoliaTeacher -Document $document -Teacher $changedTeacher -Actor $actor -ExpectedRevision $document.Revision}} 'Une période d''activité modifie les jours de janvier utilisés par la paie de février.'
 $beforePersonnel=Read-ScoliaFeatureStore -Name personnel;$beforeSalaries=Read-ScoliaFeatureStore -Name salaires
 $changedPeriod=Copy-ScoliaSalaryValue $beforePersonnel
 @($changedPeriod.Items|Where-Object {$_.Kind -eq 'Teacher' -and $_.Id -eq $teacher.Id})[0].ActiveFrom='2024-02-01'
 Reject-SourcesPaie {Assert-ScoliaFinalizedStaffUnchanged $beforePersonnel $changedPeriod $beforeSalaries $beforeSalaries} 'Le gel financier ignore l''activité sur les jours sources de janvier.'
 $changedPunch=Copy-ScoliaSalaryValue $beforePersonnel
 @($changedPunch.Items|Where-Object {$_.Kind -eq 'Punch' -and $_.Date -eq '2024-01-31'})[0].EntryAt='2024-01-31T07:59:00.0000000+02:00'
 Reject-SourcesPaie {Assert-ScoliaFinalizedStaffUnchanged $beforePersonnel $changedPunch $beforeSalaries $beforeSalaries} 'Le gel financier ignore une entrée source de janvier.'
 # Le gel ne bloque ni la suite du calendrier scolaire ni le début de janvier hors semaine source.
 [void](Invoke-ScoliaFeatureUpdate -Name personnel -Change {param($document)Set-ScoliaTeacherJustification -Document $document -TeacherId $teacher.Id -Date '2024-03-01' -Reason 'Mission prévue' -Actor $actor -ExpectedRevision $document.Revision})
 [void](Invoke-ScoliaFeatureUpdate -Name personnel -Change {param($document)Set-ScoliaTeacherJustification -Document $document -TeacherId $teacher.Id -Date '2024-01-15' -Reason 'Mission hors semaine source' -Actor $actor -ExpectedRevision $document.Revision})
 $outsideJan=New-ScoliaMonthlyCalendar -Month '2024-01' -Year $year -ContextId $context.Id -WorkDates @('2024-01-15','2024-01-30','2024-01-31') -Actor $actor
 [void](Invoke-ScoliaFeatureUpdate -Name salaires -Change {param($document)Set-ScoliaMonthlyCalendar -Document $document -Calendar $outsideJan -ExpectedRevision $document.Revision})
 $march=New-ScoliaMonthlyCalendar -Month '2024-03' -Year $year -ContextId $context.Id -WorkDates @('2024-03-01') -Actor $actor
 [void](Invoke-ScoliaFeatureUpdate -Name salaires -Change {param($document)Set-ScoliaMonthlyCalendar -Document $document -Calendar $march -ExpectedRevision $document.Revision})
 $afterPersonnel=Read-ScoliaFeatureStore -Name personnel;$afterSalaries=Read-ScoliaFeatureStore -Name salaires
 $afterPay=@($afterSalaries.Items|Where-Object {$_.Kind -eq 'Payroll'})[0]
 Assert-SourcesPaie ($afterPay.Hash -ceq $pay.Hash -and $afterPay.NetFC -eq $pay.NetFC) 'Une modification hors sources réécrit la paie finalisée.'
 Assert-SourcesPaie (@($afterPersonnel.Items|Where-Object {$_.Kind -eq 'Justification' -and $_.Date -in @('2024-03-01','2024-01-15')}).Count -eq 2) 'Le gel bloque des motifs hors période source.'
 Assert-SourcesPaie ((@($afterSalaries.Items|Where-Object {$_.Kind -eq 'Calendar' -and $_.Month -eq '2024-01'})[0].WorkDates -join '|') -eq '2024-01-15|2024-01-30|2024-01-31') 'Le calendrier de janvier est entièrement gelé au lieu de sa seule semaine source.'
 $laterTeacher=New-ScoliaTeacher -Id $teacher.Id -Nom 'Nom corrigé' -ActiveFrom '2023-09-01' -ActiveTo '2024-03-15' -Actor $actor
 [void](Invoke-ScoliaFeatureUpdate -Name personnel -Change {param($document)Set-ScoliaTeacher -Document $document -Teacher $laterTeacher -Actor $actor -ExpectedRevision $document.Revision})
 Assert-SourcesPaie ((@((Read-ScoliaFeatureStore -Name personnel).Items|Where-Object {$_.Kind -eq 'Teacher'})[0].Nom) -eq 'Nom corrigé') 'Un changement sans effet sur les jours sources est bloqué.'
 # Compatibilité : le gel des anciennes paies V56 reste limité au mois d''origine.
 $legacy=Copy-ScoliaSalaryValue $pay
 foreach($name in @('RuleVersion','LateDays','LateAbsenceDays','LateJustifiedAbsenceDays','LateUnjustifiedAbsenceDays','LateDeductibleDays','LateGroups','SourceDays','SourceRows','SourceCalendars','SourceWeeksComplete')){$legacy.PSObject.Properties.Remove($name)}
 foreach($row in @($legacy.Rows)){
  foreach($name in @('LateRecorded','IsLate','LatePenalty','LatePenaltyJustified','LatePenaltyReason','PenaltyWeekStart','ConvertedDates')){$row.PSObject.Properties.Remove($name)}
  if($row.Code -eq 'Late'){$row.Code='Present';$row.Status='Présent'};$row.Deductible=$false
 }
 $legacy.DeductionDays=0;$legacy.DeductionsFC=0;$legacy.NetFC=10000;$legacy.Hash=Get-ScoliaSalaryRecordHash (Get-ScoliaPayrollHashPayload $legacy)
 $legacySalaries=Copy-ScoliaSalaryValue $beforeSalaries;$legacySalaries.Items=@($legacySalaries.Items|Where-Object {$_.Kind -ne 'Payroll'})+@($legacy)
 Assert-ScoliaSalaryDocument $legacySalaries
 Assert-ScoliaFinalizedStaffUnchanged $beforePersonnel $changedPunch $legacySalaries $legacySalaries
 Assert-SourcesPaie (-not $legacy.PSObject.Properties['RuleVersion'] -and $legacy.NetFC -eq 10000) 'Le gel V57 modifie la règle ou le montant d''une ancienne paie.'
 Write-Output ('PASS : '+$script:sourcesPaieChecks+' contrôles des sources de paie, semaines entre mois et compatibilité V56.')
}finally{[IO.Directory]::Delete($root,$true)}
