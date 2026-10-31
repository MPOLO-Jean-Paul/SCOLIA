$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Windows.Forms
. (Join-Path $PSScriptRoot 'PresencesV55.ps1')
$script:scolia=$null
$testStartYear=(Get-Date).Year-2
$testYear=([string]$testStartYear+'-'+[string]($testStartYear+1))
$oldYear=([string]($testStartYear-1)+'-'+[string]$testStartYear)
function Assert-AttendanceTest([bool]$Condition,[string]$Message){if(-not $Condition){throw $Message}}
function Assert-AttendanceReject([scriptblock]$Action,[string]$Message){$rejected=$false;try{& $Action}catch{$rejected=$true};Assert-AttendanceTest $rejected $Message}
$learners=@(
    [pscustomobject]@{Matricule=' a01 ';Nom='Alpha';PostNom='';Prenom='Un';Promotion='TLM1';Annee=$testYear;Statut='Actif';Eligible=$true},
    [pscustomobject]@{Matricule='A02';Nom='Beta';PostNom='';Prenom='Deux';Promotion='TLM1';Annee=$testYear;Statut='Actif';Eligible=$true},
    [pscustomobject]@{Matricule='DUP';Nom='Double';PostNom='';Prenom='';Promotion='TLM1';Annee=$testYear;Statut='Actif';Eligible=$true},
    [pscustomobject]@{Matricule='dup';Nom='Double autre';PostNom='';Prenom='';Promotion='TLM2';Annee=$testYear;Statut='Actif';Eligible=$true},
    [pscustomobject]@{Matricule='OUT';Nom='Sorti';PostNom='';Prenom='';Promotion='TLM1';Annee=$testYear;Statut='Sorti';Eligible=$true},
    [pscustomobject]@{Matricule='BAD';Nom='Ineligible';PostNom='';Prenom='';Promotion='TLM1';Annee=$testYear;Statut='Actif';Eligible=$false},
    [pscustomobject]@{Matricule='OLD';Nom='Ancien';PostNom='';Prenom='';Promotion='TLM1';Annee=$oldYear;Statut='Actif';Eligible=$true}
)
$roster=@(Get-ScoliaAttendanceRoster -Learners $learners -Promotion 'tlm1' -Year $testYear)
Assert-AttendanceTest ($roster.Count -eq 2) 'La liste doit exclure doublons, sorties, ineligibles et autre année.'
Assert-AttendanceTest ($roster[0].Matricule -eq 'A01') 'Les matricules doivent être normalisés.'
Assert-AttendanceTest (@($roster | Where-Object {$_.Status}).Count -eq 0) 'Une feuille vide ne doit jamais précocher Présent.'
$bounds=Get-ScoliaAttendanceYearBounds $testYear
$today=$bounds.End
$script:scolia=[pscustomobject]@{StartMonth=1}
$calendarBounds=Get-ScoliaAttendanceYearBounds $testYear
Assert-AttendanceTest ($calendarBounds.Start.Month -eq 1 -and $calendarBounds.End.Month -eq 12) 'Le mois scolaire configuré doit être pris en compte.'
$script:scolia=$null
Assert-AttendanceReject {New-ScoliaAttendanceSheet -Date $today -Promotion 'TLM1' -Year $testYear -ContextId 'school1' -Entries $roster} 'Un statut vide doit empêcher la sauvegarde.'
$roster[0].Status='Présent';$roster[1].Status='Justifié'
Assert-AttendanceReject {New-ScoliaAttendanceSheet -Date $today -Promotion 'TLM1' -Year $testYear -ContextId 'school1' -Entries $roster} 'Justifié doit demander un motif.'
$roster[1].Reason='Certificat médical'
Assert-AttendanceReject {New-ScoliaAttendanceSheet -Date $bounds.Start.AddDays(-1) -Promotion 'TLM1' -Year $testYear -ContextId 'school1' -Entries $roster} 'Une date avant le début de l année doit être refusée.'
Assert-AttendanceReject {New-ScoliaAttendanceSheet -Date $bounds.End.AddDays(1) -Promotion 'TLM1' -Year $testYear -ContextId 'school1' -Entries $roster} 'Une date après la fin de l année doit être refusée.'
Assert-AttendanceReject {Get-ScoliaAttendanceYearBounds '2026-2028'} 'Une année non consécutive doit être refusée.'
$day1=New-ScoliaAttendanceSheet -Date $today.AddDays(-1) -Promotion 'TLM1' -Year $testYear -ContextId 'school1' -Entries $roster
Assert-AttendanceTest ($day1.Entries.Count -eq 2 -and $day1.Entries[1].Reason -eq 'Certificat médical') 'Les informations doivent être conservées.'
Assert-AttendanceReject {New-ScoliaAttendanceSheet -Date (Get-Date).Date.AddDays(1) -Promotion 'TLM1' -Year $testYear -ContextId 'school1' -Entries $roster} 'La saisie future doit être refusée.'
Assert-AttendanceReject {New-ScoliaAttendanceSheet -Date $today -Promotion 'TLM1' -Year $testYear -ContextId 'school1' -Entries @($roster[0],$roster[0])} 'Les doublons doivent être refusés avant écriture.'
$document=[pscustomobject]@{Schema=1;Context=[pscustomobject]@{Id='school1';Year=$testYear;LocalKey='test'};Revision=0;Items=@()}
Set-ScoliaAttendanceSheet -Document $document -Sheet $day1 -ExpectedRevision 0
Assert-AttendanceTest (@($document.Items).Count -eq 1) 'La première feuille doit être insérée.'
$document.Revision=1
$original=$document | ConvertTo-Json -Depth 20 -Compress
Assert-AttendanceReject {Set-ScoliaAttendanceSheet -Document $document -Sheet $day1 -ExpectedRevision 0} 'Une revision périmée doit bloquer une écriture concurrente.'
Assert-AttendanceTest (($document | ConvertTo-Json -Depth 20 -Compress) -ceq $original) 'Une revision périmée ne doit pas modifier le document.'
$roster[0].Status='Absent';$roster[1].Status='Retard';$roster[1].Reason='Transport'
$day2=New-ScoliaAttendanceSheet -Date $today -Promotion 'TLM1' -Year $testYear -ContextId 'school1' -Entries $roster
Set-ScoliaAttendanceSheet -Document $document -Sheet $day2 -ExpectedRevision 1
Set-ScoliaAttendanceSheet -Document $document -Sheet $day2 -ExpectedRevision 1
Assert-AttendanceTest (@($document.Items).Count -eq 2) 'Réenregistrer une feuille doit la remplacer sans la doubler.'
Assert-ScoliaAttendanceDocument $document
$otherYear=New-ScoliaAttendanceSheet -Date $today.AddYears(-1) -Promotion 'TLM1' -Year $oldYear -ContextId 'school1' -Entries $roster
Assert-AttendanceReject {Set-ScoliaAttendanceSheet -Document $document -Sheet $otherYear -ExpectedRevision 1} 'Une feuille hors année ne doit pas être écrite.'
$corruptDocument=[pscustomobject]@{Schema=1;Context=$document.Context;Revision=1;Items=@($day1,$day1)}
Assert-AttendanceReject {Assert-ScoliaAttendanceDocument $corruptDocument} 'Un fichier avec des feuilles dupliquées doit être signalé.'
$summary=@(Get-ScoliaAttendanceSummary -Sheets @($document.Items) -Start $today.AddDays(-5) -End $today)
Assert-AttendanceTest ($summary.Count -eq 2) 'Le bilan doit regrouper les mêmes apprenants.'
$alpha=@($summary | Where-Object {$_.Matricule -eq 'A01'})[0]
$beta=@($summary | Where-Object {$_.Matricule -eq 'A02'})[0]
Assert-AttendanceTest ($alpha.Presents -eq 1 -and $alpha.Absents -eq 1 -and $alpha.Appels -eq 2) 'Les jours sans appel ne doivent pas être comptabilisés en absences.'
Assert-AttendanceTest ($beta.Justifies -eq 1 -and $beta.Retards -eq 1 -and $beta.Appels -eq 2) 'Retard et justification doivent rester des catégories séparées.'
$onlyToday=@(Get-ScoliaAttendanceSummary -Sheets @($document.Items) -Start $today -End $today -Promotion 'TLM1')
Assert-AttendanceTest ($onlyToday[0].Appels -eq 1) 'Les bornes de période doivent être inclusives.'
Assert-AttendanceTest (@(Get-ScoliaAttendanceSummary -Sheets @($document.Items) -Start $today -End $today -Promotion 'TLM2').Count -eq 0) 'Le filtre de promotion doit être respecté.'
Assert-AttendanceReject {Get-ScoliaAttendanceSummary -Sheets @($day1,$day1) -Start $today.AddDays(-1) -End $today} 'Des feuilles dupliquées doivent être signalées, pas comptées deux fois.'
Assert-AttendanceReject {Get-ScoliaAttendanceSummary -Sheets @($document.Items) -Start $today -End $today.AddDays(-1)} 'Une période inversée doit être refusée.'
Write-Output 'OK : présences V55, statut explicite, motifs, filtres, historique, bilan et concurrence.'

# Un changement de calendrier courant ne reinterpreté pas les feuilles déjà conservées.
$script:scolia=[pscustomobject]@{StartMonth=1}
Assert-ScoliaAttendanceDocument $document
$script:scolia=$null
Write-Output 'PASS : calendrier de rentrée conservé avec les feuilles.'
