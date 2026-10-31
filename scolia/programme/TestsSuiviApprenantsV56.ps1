$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Windows.Forms
. (Join-Path $PSScriptRoot 'PersonnelV56.ps1')
. (Join-Path $PSScriptRoot 'PresencesV55.ps1')
. (Join-Path $PSScriptRoot 'SuiviApprenantsV56.ps1')
$script:scolia=$null
function Assert-PupilV56([bool]$Condition,[string]$Message){if(-not $Condition){throw $Message}}
function Reject-PupilV56([scriptblock]$Action,[string]$Message){$rejected=$false;try{& $Action}catch{$rejected=$true};Assert-PupilV56 $rejected $Message}
function New-PupilTestSheet([string]$Date,[string]$Promotion,[object[]]$Entries){return [pscustomobject]@{Id=($Date+'|'+$Promotion);Date=$Date;Promotion=$Promotion;Year='2023-2024';ContextId='school';StartMonth=9;Entries=@($Entries)}}
$learner=[pscustomobject]@{Matricule='P01';Nom='ALPHA';PostNom='UN';Prenom='ELISE';Promotion='P1';Annee='2023-2024';Statut='Actif';Eligible=$true;Inscription='02/02/2024'}
$calendar=New-ScoliaPupilCalendar -Month ([datetime]'2024-02-01') -Promotion 'P1' -Year '2023-2024' -ContextId 'school' -WorkDates @('2024-02-01','2024-02-02','2024-02-05','2024-02-06','2024-02-07','2024-02-08','2024-02-09') -ThresholdAbsentCount 3 -ConsecutiveDays 3
$now=[datetimeoffset]::Parse('2024-02-07T12:00:00+02:00',[Globalization.CultureInfo]::InvariantCulture)
$rows=@(Get-ScoliaPupilMonthlySummary -Learners @($learner) -Sheets @() -Calendars @($calendar) -Month ([datetime]'2024-02-01') -Year '2023-2024' -ContextId 'school' -Now $now)
Assert-PupilV56 ($rows.Count -eq 1 -and $rows[0].Days.Count -eq 29) 'Février 2024 doit couvrir exactement les 29 jours du mois.'
Assert-PupilV56 ($rows[0].Absents -eq 3 -and $rows[0].Pending -eq 1) 'Seuls les jours de cours passés sans signature, à partir de l inscription, deviennent absents.'
Assert-PupilV56 ($rows[0].WorkingDays -eq 6) 'Les jours de cours avant inscription doivent être exclus du suivi de cet apprenant.'
Assert-PupilV56 ($rows[0].MaxConsecutiveAbsences -eq 3 -and $rows[0].Alert) 'Les journées de repos ne coupent pas une série de jours de cours absents.'
$script:scolia=[pscustomobject]@{StartMonth=1}
$savedCalendar=@(Get-ScoliaPupilMonthlySummary -Learners @($learner) -Sheets @() -Calendars @($calendar) -Month ([datetime]'2024-02-01') -Year '2023-2024' -ContextId 'school' -Now $now)
Assert-PupilV56 ($savedCalendar[0].Absents -eq 3 -and $savedCalendar[0].Days.Count -eq 29) 'Le calendrier de rentrée conservé doit garder l historique lisible malgré un réglage courant différent.'
$script:scolia=$null
Assert-PupilV56 (@($rows[0].Days | Where-Object {$_.Date -ceq '2024-02-01' -and $_.Status -eq 'Hors suivi'}).Count -eq 1) 'Une absence avant inscription ne doit pas être fabriquée.'
Assert-PupilV56 (@($rows[0].Days | Where-Object {$_.Date -ceq '2024-02-07' -and $_.Status -eq 'À signer'}).Count -eq 1) 'Le jour courant ne devient pas absent avant la fin de la journée.'
Assert-PupilV56 (@($rows[0].Days | Where-Object {$_.Date -ceq '2024-02-08' -and $_.Status -eq 'À venir'}).Count -eq 1) 'Les journées futures ne comptent jamais comme absences.'
$empty=@(Get-ScoliaPupilMonthlySummary -Learners @($learner) -Sheets @() -Calendars @() -Month ([datetime]'2024-02-01') -Year '2023-2024' -ContextId 'school' -Now $now)
Assert-PupilV56 ($empty[0].Absents -eq 0 -and -not $empty[0].CalendarConfigured -and -not $empty[0].Alert) 'Sans calendrier, une feuille absente ne prouve aucune absence.'
$unknownEnrollment=[pscustomobject]@{Matricule='UNKNOWN';Nom='SANS DATE';Promotion='P1';Annee='2023-2024';Statut='Actif';Inscription=''}
$unknown=@(Get-ScoliaPupilMonthlySummary -Learners @($unknownEnrollment) -Sheets @() -Calendars @($calendar) -Month ([datetime]'2024-02-01') -Year '2023-2024' -ContextId 'school' -Now $now)
Assert-PupilV56 ($unknown[0].Absents -eq 0 -and -not $unknown[0].EnrollmentKnown -and @($unknown[0].Days | Where-Object {$_.Status -eq 'Inscription à vérifier'}).Count -gt 0) 'Une inscription sans date doit être vérifiée, sans inventer des absences historiques.'
$justified=New-PupilTestSheet '2024-02-05' 'P1' @([pscustomobject]@{Matricule='P01';Nom='ALPHA';Status='Justifié';Reason='Maladie'})
$explicit=New-PupilTestSheet '2024-02-06' 'P1' @([pscustomobject]@{Matricule='P01';Nom='ALPHA';Status='Absent';Reason=''})
$duplicatePromotion=New-PupilTestSheet '2024-02-06' 'P2' @([pscustomobject]@{Matricule='P01';Nom='ALPHA';Status='Absent';Reason=''})
$summary=@(Get-ScoliaPupilMonthlySummary -Learners @($learner) -Sheets @($justified,$explicit,$duplicatePromotion) -Calendars @($calendar) -Month ([datetime]'2024-02-01') -Year '2023-2024' -ContextId 'school' -Now $now)
Assert-PupilV56 ($summary[0].Absents -eq 1 -and $summary[0].Justifies -eq 1 -and $summary[0].TotalAbsences -eq 2) 'Justifié reste distinct, deux promotions ne doublent pas une journée, et une mutation incertaine ne fabrique pas des absences.'
$reversed=@(Get-ScoliaPupilMonthlySummary -Learners @($learner) -Sheets @($duplicatePromotion,$explicit,$justified) -Calendars @($calendar) -Month ([datetime]'2024-02-01') -Year '2023-2024' -ContextId 'school' -Now $now)
Assert-PupilV56 ($reversed[0].Absents -eq $summary[0].Absents -and $reversed[0].Justifies -eq $summary[0].Justifies -and $reversed[0].MaxConsecutiveAbsences -eq $summary[0].MaxConsecutiveAbsences) 'L ordre des anciennes feuilles de promotions ne doit changer ni les absences ni les alertes.'
Assert-PupilV56 ($summary[0].ReconciledRecords -eq 1) 'Les anciennes journées dupliquées entre promotions doivent être signalées.'
Assert-PupilV56 ($summary[0].MaxConsecutiveAbsences -eq 1 -and -not $summary[0].Alert) 'Une absence justifiée coupe les alertes de jours non justifiés consécutifs.'
$withoutCalendar=@(Get-ScoliaPupilMonthlySummary -Learners @($learner) -Sheets @($justified,$explicit,$duplicatePromotion) -Calendars @() -Month ([datetime]'2024-02-01') -Year '2023-2024' -ContextId 'school' -Now $now)
Assert-PupilV56 ($withoutCalendar[0].Absents -eq 1 -and $withoutCalendar[0].Justifies -eq 1 -and $withoutCalendar[0].Appels -eq 2) 'Sans calendrier, seuls les deux jours explicitement appelés sont comptés.'
$present=New-PupilTestSheet '2024-02-06' 'P2' @([pscustomobject]@{Matricule='P01';Nom='ALPHA';Status='Présent';Reason=''})
$favorable=@(Get-ScoliaPupilMonthlySummary -Learners @($learner) -Sheets @($explicit,$present) -Calendars @() -Month ([datetime]'2024-02-01') -Year '2023-2024' -ContextId 'school' -Now $now)
Assert-PupilV56 ($favorable[0].Presents -eq 1 -and $favorable[0].Absents -eq 0 -and $favorable[0].Appels -eq 1) 'Une présence dans une autre promotion prime sur une ancienne absence contradictoire, sans double compte.'
$beforeEnrollment=New-PupilTestSheet '2024-02-01' 'P1' @([pscustomobject]@{Matricule='P01';Nom='ALPHA';Status='Absent';Reason=''})
$prior=@(Get-ScoliaPupilMonthlySummary -Learners @($learner) -Sheets @($beforeEnrollment) -Calendars @() -Month ([datetime]'2024-02-01') -Year '2023-2024' -ContextId 'school' -Now $now)
Assert-PupilV56 ($prior[0].Absents -eq 0 -and @($prior[0].Days | Where-Object {$_.Date -ceq '2024-02-01' -and $_.Status -eq 'Hors suivi'}).Count -eq 1) 'Même une ancienne feuille contradictoire ne doit pas compter une absence avant inscription.'
# Mutation de promotion : chaque jour utilise sa promotion réelle, sans déduction rétroactive.
$moved=[pscustomobject]@{Matricule='MOVE';Nom='MUTATION';PostNom='';Prenom='';Promotion='P2';Annee='2023-2024';Statut='Actif';Inscription='02/02/2024'}
$oldClass=New-PupilTestSheet '2024-02-02' 'P1' @([pscustomobject]@{Matricule='MOVE';Nom='MUTATION';Status='Présent';Reason=''})
$newCalendar=New-ScoliaPupilCalendar -Month ([datetime]'2024-02-01') -Promotion 'P2' -Year '2023-2024' -ContextId 'school' -WorkDates @('2024-02-02','2024-02-03','2024-02-06','2024-02-07')
$uncertain=@(Get-ScoliaPupilMonthlySummary -Learners @($moved) -Sheets @($oldClass) -Calendars @($calendar,$newCalendar) -Month ([datetime]'2024-02-01') -Year '2023-2024' -ContextId 'school' -Now $now)
Assert-PupilV56 ($uncertain[0].Presents -eq 1 -and $uncertain[0].Absents -eq 0 -and @($uncertain[0].Days | Where-Object {$_.Date -ceq '2024-02-03' -and $_.Status -eq 'Promotion à vérifier'}).Count -eq 1) 'Un changement non daté doit suspendre les absences dérivées de la nouvelle promotion.'
$scope=New-ScoliaPupilPromotionScope -Matricule 'MOVE' -Promotion 'P2' -OldPromotion 'P1' -EffectiveDate ([datetime]'2024-02-06') -Year '2023-2024' -ContextId 'school' -Actor 'admin' -Now $now
$movedSummary=@(Get-ScoliaPupilMonthlySummary -Learners @($moved) -Sheets @($oldClass) -Calendars @($calendar,$newCalendar,$scope) -Month ([datetime]'2024-02-01') -Year '2023-2024' -ContextId 'school' -Now $now)
Assert-PupilV56 ($movedSummary[0].Presents -eq 1 -and $movedSummary[0].Absents -eq 2) 'Avant le transfert confirmé, seule l ancienne promotion doit produire des absences ; après, seule la nouvelle.'
Assert-PupilV56 (@($movedSummary[0].Days | Where-Object {$_.Date -ceq '2024-02-05' -and $_.Promotion -ceq 'P1' -and $_.Status -eq 'Absent'}).Count -eq 1) 'Le jour de cours de l ancienne promotion doit être conservé.'
Assert-PupilV56 (@($movedSummary[0].Days | Where-Object {$_.Date -ceq '2024-02-03' -and $_.Status -eq 'Non ouvrable'}).Count -eq 1) 'Le calendrier de la nouvelle promotion ne doit pas créer une absence avant le transfert.'
Assert-PupilV56 (@($movedSummary[0].Days | Where-Object {$_.Date -ceq '2024-02-06' -and $_.Promotion -ceq 'P2' -and $_.Status -eq 'Absent'}).Count -eq 1) 'Le jour du transfert doit utiliser la nouvelle promotion.'
$moved.Statut='Sorti'
$archive=@(Get-ScoliaPupilMonthlySummary -Learners @($moved) -Sheets @($oldClass) -Calendars @($calendar,$newCalendar,$scope) -Month ([datetime]'2024-02-01') -Year '2023-2024' -ContextId 'school' -Now $now)
Assert-PupilV56 ($archive.Count -eq 1 -and $archive[0].Presents -eq 1 -and $archive[0].Absents -eq 0) 'Un apprenant sorti doit conserver les appels explicites de l historique, sans nouvelles déductions fondées sur son statut courant.'
Reject-PupilV56 {Get-ScoliaPupilMonthlySummary -Learners @($learner) -Sheets @($explicit,$explicit) -Calendars @() -Month ([datetime]'2024-02-01') -Year '2023-2024' -ContextId 'school' -Now $now} 'Une même feuille quotidienne dupliquée doit être refusée.'
Reject-PupilV56 {New-ScoliaPupilCalendar -Month ([datetime]'2024-02-01') -Promotion 'P1' -Year '2023-2024' -ContextId 'school' -WorkDates @('2024-03-01')} 'Un jour extérieur au mois doit être refusé.'
Reject-PupilV56 {New-ScoliaPupilCalendar -Month ([datetime]'2024-02-01') -Promotion 'P1' -Year '2023-2024' -ContextId 'school' -WorkDates @('2024-02-02','2024-02-02')} 'Les jours de cours dupliqués doivent être refusés.'
Reject-PupilV56 {New-ScoliaPupilCalendar -Month ([datetime]'2024-02-01') -Promotion 'P1' -Year '2023-2024' -ContextId 'school' -WorkDates @() -ThresholdAbsentCount 0} 'Un seuil d alerte nul est interdit.'
Reject-PupilV56 {Get-ScoliaPupilMonthBounds ([datetime]'2024-09-01') '2023-2024'} 'Un mois hors de l année scolaire doit être refusé.'
$config=[pscustomobject]@{Context=[pscustomobject]@{Id='school';Year='2023-2024'};Revision=0;Items=@()}
Set-ScoliaPupilCalendar -Document $config -Calendar $calendar -ExpectedRevision 0
Assert-PupilV56 (@($config.Items).Count -eq 1) 'Le calendrier doit être conservé dans son contexte.'
Set-ScoliaPupilPromotionScope -Document $config -Scope $scope -ExpectedRevision 0
Assert-PupilV56 (@($config.Items).Count -eq 2) 'La timeline des promotions doit coexister avec les calendriers.'
Reject-PupilV56 {Set-ScoliaPupilPromotionScope -Document $config -Scope $scope -ExpectedRevision 0} 'Une même affectation ne doit pas être dupliquée.'
$before=$config | ConvertTo-Json -Depth 20 -Compress
Reject-PupilV56 {Set-ScoliaPupilCalendar -Document $config -Calendar $calendar -ExpectedRevision 1} 'Un calendrier de revision périmée doit être refusé.'
Assert-PupilV56 (($config | ConvertTo-Json -Depth 20 -Compress) -ceq $before) 'Une revision périmée ne doit rien changer au calendrier.'
$otherContext=New-ScoliaPupilCalendar -Month ([datetime]'2024-02-01') -Promotion 'P1' -Year '2023-2024' -ContextId 'other' -WorkDates @()
Reject-PupilV56 {Set-ScoliaPupilCalendar -Document $config -Calendar $otherContext -ExpectedRevision 0} 'Un calendrier d une autre base doit être refusé.'
$alertRows=@(Get-ScoliaPupilAbsenceAlerts -Learners @($learner) -Sheets @() -Calendars @($calendar) -Month ([datetime]'2024-02-01') -Year '2023-2024' -ContextId 'school' -Now $now)
Assert-PupilV56 ($alertRows.Count -eq 1 -and $alertRows[0].AbsenceDates.Count -eq 3) 'Les alertes doivent fournir les dates exactes des absences.'
$quiet=New-ScoliaPupilCalendar -Month ([datetime]'2024-02-01') -Promotion 'P1' -Year '2023-2024' -ContextId 'school' -WorkDates @($calendar.WorkDates) -ThresholdAbsentCount 4 -ConsecutiveDays 4
Assert-PupilV56 (@(Get-ScoliaPupilAbsenceAlerts -Learners @($learner) -Sheets @() -Calendars @($quiet) -Month ([datetime]'2024-02-01') -Year '2023-2024' -ContextId 'school' -Now $now).Count -eq 0) 'Les seuils enregistrés doivent commander les alertes.'
# La présence de fin de journée conserve l'heure réelle uniquement pour l'audit.
$actualNow=[datetimeoffset]::Now
$startYear=$(if($actualNow.Month -ge 9){$actualNow.Year}else{$actualNow.Year-1})
$actualYear=([string]$startYear+'-'+[string]($startYear+1))
$signer=[pscustomobject]@{Matricule='NOW01';Nom='PRESENT';PostNom='UN';Prenom='JEAN';Promotion='P1';Annee=$actualYear;Statut='Actif';Eligible=$true;Inscription=$actualNow.Date.AddDays(-7).ToString('dd/MM/yyyy')}
$document=[pscustomobject]@{Context=[pscustomobject]@{Id='school';Year=$actualYear};Revision=0;Items=@()}
Set-ScoliaPupilSignature -Document $document -Learner $signer -Date $actualNow.Date -Promotion 'P1' -ExpectedRevision 0 -Actor 'account-1' -Now $actualNow
Assert-PupilV56 (@($document.Items).Count -eq 1 -and @($document.Items[0].Entries).Count -eq 1) 'Une coche doit enregistrer une seule présence, sans feuille vide supplémentaire.'
$entry=$document.Items[0].Entries[0]
Assert-PupilV56 ([string]$entry.Status -ceq 'Présent' -and [string]$entry.SignedAt -ceq $actualNow.ToString('o',[Globalization.CultureInfo]::InvariantCulture) -and [string]$entry.Actor -ceq 'account-1') 'La signature doit conserver exactement l horodatage et l acteur.'
$before=$document | ConvertTo-Json -Depth 20 -Compress
Reject-PupilV56 {Set-ScoliaPupilSignature -Document $document -Learner $signer -Date $actualNow.Date -Promotion 'P1' -ExpectedRevision 0 -Actor 'account-1' -Now $actualNow.AddSeconds(2)} 'Une deuxième signature doit être refusée sans changer l heure.'
Assert-PupilV56 (($document | ConvertTo-Json -Depth 20 -Compress) -ceq $before) 'Une seconde coche ne doit modifier ni la feuille ni l horodatage.'
$signer.Promotion='P2'
Reject-PupilV56 {Set-ScoliaPupilSignature -Document $document -Learner $signer -Date $actualNow.Date -Promotion 'P2' -ExpectedRevision 0 -Actor 'account-1' -Now $actualNow.AddSeconds(4)} 'Une deuxième promotion ne permet pas une deuxième signature le même jour.'
$signer.Promotion='P1'
Reject-PupilV56 {Set-ScoliaPupilSignature -Document $document -Learner $signer -Date $actualNow.Date.AddDays(1) -Promotion 'P1' -ExpectedRevision 0 -Actor 'account-1' -Now $actualNow} 'Une signature future doit être refusée.'
Reject-PupilV56 {Set-ScoliaPupilSignature -Document $document -Learner $signer -Date $actualNow.Date.AddDays(-1) -Promotion 'P1' -ExpectedRevision 0 -Actor 'account-1' -Now $actualNow} 'Une signature rétroactive doit être refusée.'
Reject-PupilV56 {Set-ScoliaPupilSignature -Document $document -Learner $signer -Date $actualNow.Date -Promotion 'P1' -ExpectedRevision 1 -Actor 'account-1' -Now $actualNow} 'Une signature doit utiliser une revision à jour.'
Reject-PupilV56 {Set-ScoliaPupilDayStatus -Document $document -Learner $signer -Date $actualNow.Date -Promotion 'P1' -Status 'Justifié' -Reason 'Correction' -ExpectedRevision 0 -Now $actualNow} 'Une présence signée ne peut pas être effacée par une absence justifiée.'
Reject-PupilV56 {Set-ScoliaPupilDayStatus -Document $document -Learner $signer -Date $actualNow.Date.AddDays(-1) -Promotion 'P1' -Status 'Justifié' -Reason '' -ExpectedRevision 0 -Now $actualNow} 'Une absence justifiée doit avoir un motif.'
$second=[pscustomobject]@{Matricule='NOW02';Nom='ABSENT';PostNom='DEUX';Prenom='MARIE';Promotion='P1';Annee=$actualYear;Statut='Actif';Eligible=$true;Inscription=$actualNow.Date.AddDays(-7).ToString('dd/MM/yyyy')}
Set-ScoliaPupilDayStatus -Document $document -Learner $second -Date $actualNow.Date -Promotion 'P1' -Status 'Justifié' -Reason 'Maladie' -ExpectedRevision 0 -Now $actualNow
Assert-PupilV56 (@($document.Items).Count -eq 1 -and @($document.Items[0].Entries).Count -eq 2 -and [string]$document.Items[0].Entries[0].SignedAt -ceq [string]$entry.SignedAt) 'Justifier un autre apprenant doit préserver la signature déjà enregistrée.'
# La portée dynamique des callbacks ne doit pas remplacer la révision chargée par celle du stockage.
$script:pupilCallbackContext=[pscustomobject]@{Id='school';Year=$actualYear}
$script:pupilLoadedDocument=[pscustomobject]@{Context=$script:pupilCallbackContext;Revision=0;Items=@()}
$script:pupilCurrentDocument=[pscustomobject]@{Context=$script:pupilCallbackContext;Revision=1;Items=@()}
function Get-ScoliaFeatureContext{return $script:pupilCallbackContext}
function Read-ScoliaFeatureStore([string]$Name){return $script:pupilLoadedDocument}
function Invoke-ScoliaFeatureUpdate([string]$Name,[scriptblock]$Change){$revision=1;[void](& $Change $script:pupilCurrentDocument);return $script:pupilCurrentDocument}
Reject-PupilV56 {Record-ScoliaPupilPromotionTransition -Matricule 'NOW01' -OldPromotion 'P1' -NewPromotion 'P2' -EffectiveDate $actualNow.Date} 'Un callback ne doit pas utiliser la révision interne du stockage à la place de celle chargée.'
Assert-PupilV56 (@($script:pupilCurrentDocument.Items).Count -eq 0) 'Une révision périmée doit bloquer la création d une affectation via le callback réel.'
$source=[IO.File]::ReadAllText((Join-Path $PSScriptRoot 'SuiviApprenantsV56.ps1'))
Assert-PupilV56 ($source -notmatch 'GetNewClosure|Send-MailMessage|WhatsApp|Invoke-Scolia.*Payroll|Set-Scolia.*Teacher') 'Le suivi apprenant ne doit ni envoyer de relance ni modifier les enseignants ou salaires.'
Assert-PupilV56 ($source -match 'FindForm\(\)\.Tag' -and $source -match 'UTF8Encoding\(\$true\)') 'Les événements doivent retrouver leur état et les CSV conserver les accents.'
Write-Output 'PASS : suivi apprenants mensuel V56, calendrier explicite, inscription, années, février bissextile, journée unique, présence de fin de journée, horodatage d audit immuable, justifications, seuils et concurrence.'
