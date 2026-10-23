$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Windows.Forms
. (Join-Path $PSScriptRoot 'PersonnelV56.ps1')
. (Join-Path $PSScriptRoot 'PresencesV55.ps1')
. (Join-Path $PSScriptRoot 'SuiviApprenantsV56.ps1')
$script:scolia=$null
function Assert-PupilEndV57([bool]$Condition,[string]$Message){if(-not $Condition){throw $Message}}
function Reject-PupilEndV57([scriptblock]$Action,[string]$Message){$rejected=$false;try{& $Action}catch{$rejected=$true};Assert-PupilEndV57 $rejected $Message}
$day=[datetime]'2024-02-07';$month=[datetime]'2024-02-01';$year='2023-2024'
$document=[pscustomobject]@{Context=[pscustomobject]@{Id='school';Year=$year};Revision=0;Items=@()}
$learners=New-Object 'System.Collections.Generic.List[object]'
$times=@('07:59:59','08:00:00','18:00:00','23:59:00')
for($i=0;$i -lt $times.Count;$i++){
    $learner=[pscustomobject]@{Matricule=('END'+[string]$i);Nom=('ELEVE '+[string]$i);PostNom='';Prenom='';Promotion='P1';Annee=$year;Statut='Actif';Eligible=$true;Inscription='07/02/2024'}
    $learners.Add($learner)
    $instant=[datetimeoffset]::ParseExact(('2024-02-07T'+$times[$i]+'+02:00'),'yyyy-MM-ddTHH:mm:sszzz',[Globalization.CultureInfo]::InvariantCulture)
    Set-ScoliaPupilSignature -Document $document -Learner $learner -Date $day -Promotion 'P1' -ExpectedRevision 0 -Actor 'secretaire-1' -Now $instant
    $entry=@($document.Items[0].Entries | Where-Object {$_.Matricule -ceq $learner.Matricule})[0]
    Assert-PupilEndV57 ([string]$entry.Status -ceq 'Présent' -and [string]$entry.SignedAt -ceq $instant.ToString('o',[Globalization.CultureInfo]::InvariantCulture)) ('La saisie à '+$times[$i]+' doit enregistrer Présent et conserver son heure uniquement pour l audit.')
}
$unsigned=[pscustomobject]@{Matricule='UNSIGNED';Nom='NON SIGNE';PostNom='';Prenom='';Promotion='P1';Annee=$year;Statut='Actif';Eligible=$true;Inscription='07/02/2024'}
$learners.Add($unsigned)
$calendar=New-ScoliaPupilCalendar -Month $month -Promotion 'P1' -Year $year -ContextId 'school' -WorkDates @('2024-02-07','2024-02-08') -ThresholdAbsentCount 3 -ConsecutiveDays 3
# L'heure ne participe pas au calcul : à 07:00, 08:00 et 23:59 le statut d'une présence du jour reste identique.
foreach($clock in @('07:00:00','08:00:00','18:00:00','23:59:59')){
    $now=[datetimeoffset]::ParseExact(('2024-02-07T'+$clock+'+02:00'),'yyyy-MM-ddTHH:mm:sszzz',[Globalization.CultureInfo]::InvariantCulture)
    $summary=@(Get-ScoliaPupilMonthlySummary -Learners $learners.ToArray() -Sheets @($document.Items) -Calendars @($calendar) -Month $month -Year $year -ContextId 'school' -Now $now)
    $signed=@($summary | Where-Object {$_.Matricule -like 'END*'})
    Assert-PupilEndV57 ($signed.Count -eq 4 -and @($signed | Where-Object {$_.Presents -ne 1 -or $_.Retards -ne 0 -or $_.Absents -ne 0}).Count -eq 0) ('Une signature d apprenant ne doit créer aucun retard ni absence à '+$clock+'.')
    foreach($row in $signed){Assert-PupilEndV57 (@($row.Days | Where-Object {$_.Date -ceq '2024-02-07' -and $_.Status -ceq 'Présent'}).Count -eq 1) 'Les quatre heures de saisie doivent produire une seule journée présente.'}
    $waiting=@($summary | Where-Object {$_.Matricule -ceq 'UNSIGNED'})[0]
    Assert-PupilEndV57 ($waiting.Absents -eq 0 -and $waiting.Pending -eq 1 -and @($waiting.Days | Where-Object {$_.Date -ceq '2024-02-07' -and $_.Status -eq 'À signer'}).Count -eq 1) 'Un apprenant non signé aujourd hui doit rester à signer jusque la fin du jour, sans absence automatique anticipée.'
}
$nextMidnight=[datetimeoffset]::ParseExact('2024-02-08T00:00:00+02:00','yyyy-MM-ddTHH:mm:sszzz',[Globalization.CultureInfo]::InvariantCulture)
$nextSummary=@(Get-ScoliaPupilMonthlySummary -Learners $learners.ToArray() -Sheets @($document.Items) -Calendars @($calendar) -Month $month -Year $year -ContextId 'school' -Now $nextMidnight)
$waiting=@($nextSummary | Where-Object {$_.Matricule -ceq 'UNSIGNED'})[0]
Assert-PupilEndV57 ($waiting.Absents -eq 1 -and $waiting.Pending -eq 1 -and $waiting.AbsenceDates.Count -eq 1 -and $waiting.AbsenceDates[0] -ceq '2024-02-07') 'À minuit le jour de cours terminé sans présence devient une absence ; le nouveau jour reste à signer.'
Assert-PupilEndV57 (@($nextSummary | Where-Object {$_.Matricule -like 'END*' -and ($_.Presents -ne 1 -or $_.Absents -ne 0 -or $_.Retards -ne 0)}).Count -eq 0) 'Une présence saisie à toute heure doit rester Présent le lendemain.'
$before=$document | ConvertTo-Json -Depth 20 -Compress
$late=[datetimeoffset]::ParseExact('2024-02-07T23:59:59+02:00','yyyy-MM-ddTHH:mm:sszzz',[Globalization.CultureInfo]::InvariantCulture)
Reject-PupilEndV57 {Set-ScoliaPupilSignature -Document $document -Learner $learners[0] -Date $day -Promotion 'P1' -ExpectedRevision 0 -Actor 'secretaire-1' -Now $late} 'Une deuxième coche le même jour doit être rejetée, même à une autre heure.'
$learners[0].Promotion='P2'
Reject-PupilEndV57 {Set-ScoliaPupilSignature -Document $document -Learner $learners[0] -Date $day -Promotion 'P2' -ExpectedRevision 0 -Actor 'secretaire-1' -Now $late} 'Un changement de promotion ne doit pas permettre une seconde coche le même jour.'
$learners[0].Promotion='P1'
Reject-PupilEndV57 {Set-ScoliaPupilSignature -Document $document -Learner $unsigned -Date $day.AddDays(1) -Promotion 'P1' -ExpectedRevision 0 -Actor 'secretaire-1' -Now $late} 'Une présence future doit être rejetée.'
Reject-PupilEndV57 {Set-ScoliaPupilSignature -Document $document -Learner $unsigned -Date $day -Promotion 'P1' -ExpectedRevision 0 -Actor 'secretaire-1' -Now $nextMidnight} 'La saisie rétroactive ne doit pas modifier le jour terminé.'
Assert-PupilEndV57 (($document | ConvertTo-Json -Depth 20 -Compress) -ceq $before) 'Les opérations refusées doivent laisser les signatures, heures d audit et feuilles intactes.'
$withoutCalendar=@(Get-ScoliaPupilMonthlySummary -Learners @($unsigned) -Sheets @() -Calendars @() -Month $month -Year $year -ContextId 'school' -Now $nextMidnight)
Assert-PupilEndV57 ($withoutCalendar[0].Absents -eq 0) 'Sans calendrier confirmé, le passage à minuit ne doit pas inventer un jour de cours ou une absence.'
# Les anciens retards manuels restent consultables ; aucune règle horaire ne les transforme en absence.
$legacy=[pscustomobject]@{Id='2024-02-07|P1';Date='2024-02-07';Promotion='P1';Year=$year;ContextId='school';StartMonth=9;Entries=@([pscustomobject]@{Matricule='UNSIGNED';Nom='NON SIGNE';Status='Retard';Reason='Ancien appel manuel'})}
$legacySummary=@(Get-ScoliaPupilMonthlySummary -Learners @($unsigned) -Sheets @($legacy) -Calendars @($calendar) -Month $month -Year $year -ContextId 'school' -Now $nextMidnight)
Assert-PupilEndV57 ($legacySummary[0].Retards -eq 1 -and $legacySummary[0].Absents -eq 0 -and $legacySummary[0].Appels -eq 1) 'L historique scolaire doit conserver les retards manuels sans les convertir en absence enseignante.'
$source=[IO.File]::ReadAllText((Join-Path $PSScriptRoot 'SuiviApprenantsV56.ps1'))
Assert-PupilEndV57 ($source -match "Présence de fin de journée" -and $source -match "Heure de saisie \(audit\)" -and $source -match 'Columns\[''Time''\]\.Visible=\$false') 'La page doit expliquer la fin de journée et conserver l heure uniquement dans le détail d audit.'
Assert-PupilEndV57 ($source -notmatch 'TimeOfDay|TotalMinutes|ArrivalDeadline|WeeklyLate|08:00|08h') 'Le suivi apprenant ne doit contenir aucune règle horaire ou conversion hebdomadaire des retards.'
Write-Output 'PASS : présence de fin de journée V57, quatre heures sans effet, une seule coche, audit immuable, journée ouverte, absence après minuit, calendrier confirmé et historique manuel conservé.'
