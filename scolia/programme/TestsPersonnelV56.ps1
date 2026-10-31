$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'PersonnelV56.ps1')
$script:checks=0
function Assert-Personnel([bool]$Condition,[string]$Message){if(-not $Condition){throw $Message};$script:checks++}
function Assert-PersonnelReject([scriptblock]$Action,[string]$Message){$rejected=$false;try{[void](& $Action)}catch{$rejected=$true};Assert-Personnel $rejected $Message}
function New-PersonnelTestDocument {
    return [pscustomobject]@{Schema=1;Context=[pscustomobject]@{Id='test-school';Year='2026-2027';LocalKey='test-local'};Revision=0;Items=@()}
}
function Copy-PersonnelTestDocument([object]$Value){return ($Value|ConvertTo-Json -Depth 30|ConvertFrom-ScoliaJson)}
$actor='Directeur / test'
$entry=[datetimeoffset]::Parse('2026-10-12T07:35:10+02:00')
$exit=[datetimeoffset]::Parse('2026-10-12T15:40:20+02:00')
$nextDay=[datetimeoffset]::Parse('2026-10-13T00:00:00+02:00')
$teacher=New-ScoliaTeacher -Nom '  Enseignante A  ' -ActiveFrom '2026-09-01' -Now $entry -Actor $actor
Assert-Personnel ($teacher.Nom -ceq 'Enseignante A') 'Le nom doit être lisible, sans espaces de bord.'
Assert-Personnel ($teacher.Id -match '^[0-9a-f]{8}-') 'Les enseignants doivent posséder un identifiant stable.'
Assert-PersonnelReject {New-ScoliaTeacher -Nom '' -ActiveFrom '2026-09-01' -Now $entry -Actor $actor} 'Un enseignant sans nom doit être refusé.'
Assert-PersonnelReject {New-ScoliaTeacher -Nom 'A' -ActiveFrom '2026-02-30' -Now $entry -Actor $actor} 'Une date impossible doit être refusée.'
Assert-PersonnelReject {New-ScoliaTeacher -Nom 'A' -ActiveFrom '2026-10-12' -ActiveTo '2026-10-11' -Now $entry -Actor $actor} 'Une période inversée doit être refusée.'
Assert-PersonnelReject {New-ScoliaTeacher -Nom 'A' -ActiveFrom '2026-09-01' -Id ([guid]::Empty.ToString()) -Now $entry -Actor $actor} 'Un identifiant vide doit être refusé.'
Assert-PersonnelReject {New-ScoliaTeacher -Nom 'A' -ActiveFrom '2026-09-01' -Now $entry -Actor ''} 'Une fiche sans identité de saisie doit être refusée.'
$document=New-PersonnelTestDocument
Set-ScoliaTeacher -Document $document -Teacher $teacher -ExpectedRevision 0 -Now $entry -Actor $actor
Assert-Personnel (@($document.Items).Count -eq 1) 'La création doit conserver une seule fiche enseignant.'
$document.Revision=1
$before=$document|ConvertTo-Json -Depth 30 -Compress
Assert-PersonnelReject {Set-ScoliaTeacherPunch -Document $document -TeacherId $teacher.Id -Direction Entry -Now $entry -Actor $actor -ExpectedRevision 0} 'Une révision périmée doit bloquer la signature.'
Assert-Personnel (($document|ConvertTo-Json -Depth 30 -Compress) -ceq $before) 'Le conflit de révision doit laisser le document intact.'
Assert-PersonnelReject {Set-ScoliaTeacherPunch -Document $document -TeacherId $teacher.Id -Direction Exit -Now $exit -Actor $actor -ExpectedRevision 1} 'La sortie sans entrée du même jour doit être refusée.'
Set-ScoliaTeacherPunch -Document $document -TeacherId $teacher.Id -Direction Entry -Now $entry -Actor $actor -ExpectedRevision 1
$punch=@($document.Items|Where-Object {$_.Kind -eq 'Punch'})[0]
Assert-Personnel ($punch.Date -ceq '2026-10-12' -and $punch.EntryAt -ceq $entry.ToString('o')) 'Cocher l''entrée doit conserver exactement l''heure et le décalage de l''horloge.'
Assert-Personnel ($punch.EntryBy -ceq $actor) 'La signature doit conserver son auteur.'
$document.Revision=2
$before=$document|ConvertTo-Json -Depth 30 -Compress
Assert-PersonnelReject {Set-ScoliaTeacherPunch -Document $document -TeacherId $teacher.Id -Direction Entry -Now $entry.AddMinutes(15) -Actor $actor -ExpectedRevision 2} 'Une seconde entrée doit être refusée.'
Assert-Personnel (($document|ConvertTo-Json -Depth 30 -Compress) -ceq $before) 'Une seconde entrée ne doit pas changer la première heure.'
Assert-PersonnelReject {Set-ScoliaTeacherPunch -Document $document -TeacherId $teacher.Id -Direction Exit -Now $entry.AddMinutes(-1) -Actor $actor -ExpectedRevision 2} 'Une horloge antérieure à l''entrée doit être détectée.'
Assert-PersonnelReject {Set-ScoliaTeacherPunch -Document $document -TeacherId $teacher.Id -Direction Exit -Now $nextDay -Actor $actor -ExpectedRevision 2} 'Une signature de la veille ne doit pas permettre de signer la sortie du nouveau jour.'
$workDates=@('2026-10-12','2026-10-13','2026-10-14')
$status=Get-ScoliaTeacherDailyStatus -Teacher $teacher -Date '2026-10-12' -Punches @($punch) -WorkDates $workDates -AsOf $entry.AddHours(2)
Assert-Personnel ($status.Code -ceq 'InProgress' -and -not $status.IsAbsent -and -not $status.Deductible -and -not $status.IsFinal) 'Une entrée seule aujourd''hui doit attendre la sortie sans déduction anticipée.'
$status=Get-ScoliaTeacherDailyStatus -Teacher $teacher -Date '2026-10-12' -Punches @($punch) -WorkDates $workDates -AsOf $nextDay
Assert-Personnel ($status.Code -ceq 'AbsenceUnjustified' -and $status.SignatureCount -eq 1 -and $status.IsFinal -and $status.Deductible) 'Au changement de jour, une seule signature doit compter comme absence non justifiée.'
$status=Get-ScoliaTeacherDailyStatus -Teacher $teacher -Date '2026-10-13' -WorkDates $workDates -AsOf $nextDay
Assert-Personnel ($status.Code -ceq 'Awaiting' -and -not $status.IsAbsent) 'Le jour qui commence doit rester à signer.'
$status=Get-ScoliaTeacherDailyStatus -Teacher $teacher -Date '2026-10-14' -WorkDates $workDates -AsOf $nextDay
Assert-Personnel ($status.Code -ceq 'Future' -and -not $status.IsAbsent -and -not $status.IsFinal) 'Les jours futurs ne doivent pas compter comme absences.'
$status=Get-ScoliaTeacherDailyStatus -Teacher $teacher -Date '2026-10-11' -WorkDates $workDates -AsOf $nextDay
Assert-Personnel ($status.Code -ceq 'NonWorking' -and -not $status.IsAbsent) 'Un jour non ouvrable doit être exclu des absences.'
Set-ScoliaTeacherPunch -Document $document -TeacherId $teacher.Id -Direction Exit -Now $exit -Actor $actor -ExpectedRevision 2
$document.Revision=3
$punch=@($document.Items|Where-Object {$_.Kind -eq 'Punch'})[0]
Assert-Personnel ($punch.EntryAt -ceq $entry.ToString('o') -and $punch.ExitAt -ceq $exit.ToString('o')) 'La sortie doit préserver l''entrée et conserver son heure exacte.'
$status=Get-ScoliaTeacherDailyStatus -Teacher $teacher -Date '2026-10-12' -Punches @($punch) -WorkDates $workDates -AsOf $exit
Assert-Personnel ($status.Code -cin @('Present','Late') -and $status.SignatureCount -eq 2 -and -not $status.Deductible) 'Deux signatures doivent valider la présence.'
Assert-PersonnelReject {Set-ScoliaTeacherPunch -Document $document -TeacherId $teacher.Id -Direction Exit -Now $exit.AddMinutes(1) -Actor $actor -ExpectedRevision 3} 'Une seconde sortie doit être refusée.'
$midnight=[datetimeoffset]::Parse('2026-10-13T00:05:00+02:00')
Set-ScoliaTeacherPunch -Document $document -TeacherId $teacher.Id -Direction Entry -Now $midnight -Actor $actor -ExpectedRevision 3
$document.Revision=4
$midnightPunch=@($document.Items|Where-Object {$_.Kind -eq 'Punch' -and $_.Date -eq '2026-10-13'})
Assert-Personnel ($midnightPunch.Count -eq 1 -and $midnightPunch[0].EntryAt -ceq $midnight.ToString('o')) 'Le jour local doit être utilisé, même si le jour UTC est encore la veille.'
$single=Copy-PersonnelTestDocument $document
Set-ScoliaTeacherJustification -Document $single -TeacherId $teacher.Id -Date '2026-10-13' -Reason 'Certificat médical contrôlé' -Now $nextDay.AddDays(1) -Actor $actor -ExpectedRevision 4
$justification=@($single.Items|Where-Object {$_.Kind -eq 'Justification'})[0]
$status=Get-ScoliaTeacherDailyStatus -Teacher $teacher -Date '2026-10-13' -Punches @($midnightPunch) -Justifications @($justification) -WorkDates $workDates -AsOf $nextDay.AddDays(1)
Assert-Personnel ($status.Code -ceq 'AbsenceJustified' -and $status.IsAbsent -and $status.IsJustified -and -not $status.Deductible) 'Une absence justifiée doit être comptabilisée sans retenue par défaut.'
Assert-Personnel ($midnightPunch[0].EntryAt -ceq $midnight.ToString('o') -and -not $midnightPunch[0].ExitAt) 'La justification ne doit fabriquer ni entrée ni sortie.'
Assert-PersonnelReject {Set-ScoliaTeacherJustification -Document $single -TeacherId $teacher.Id -Date '2026-10-13' -Reason ' ' -Now $nextDay.AddDays(1) -Actor $actor -ExpectedRevision 4} 'Une absence justifiée exige un motif.'
Set-ScoliaTeacherJustification -Document $single -TeacherId $teacher.Id -Date '2026-10-13' -Reason 'Congé validé par la direction' -Now $nextDay.AddDays(1) -Actor $actor -ExpectedRevision 4
$justification=@($single.Items|Where-Object {$_.Kind -eq 'Justification'})[0]
Assert-Personnel (@($justification.Audit).Count -eq 2 -and $justification.Audit[1].Before -ceq 'Certificat médical contrôlé') 'La modification du motif doit conserver le motif précédent et son auteur.'
Revoke-ScoliaTeacherJustification -Document $single -TeacherId $teacher.Id -Date '2026-10-13' -Reason 'Motif attribué par erreur à cet enseignant' -Now $nextDay.AddDays(1) -Actor $actor -ExpectedRevision 4
$justification=@($single.Items|Where-Object {$_.Kind -eq 'Justification'})[0]
$status=Get-ScoliaTeacherDailyStatus -Teacher $teacher -Date '2026-10-13' -Punches @($midnightPunch) -Justifications @($justification) -WorkDates $workDates -AsOf $nextDay.AddDays(1)
Assert-Personnel ($status.Code -ceq 'AbsenceUnjustified' -and $status.Deductible -and $justification.Revoked -and @($justification.Audit).Count -eq 3) 'Annuler une justification doit rétablir la règle d''absence sans supprimer son audit.'
Assert-Personnel ($justification.Reason -ceq 'Congé validé par la direction' -and $justification.RevocationReason -ceq 'Motif attribué par erreur à cet enseignant') 'Le motif initial et le motif d''annulation doivent rester consultables.'
Assert-PersonnelReject {Revoke-ScoliaTeacherJustification -Document $single -TeacherId $teacher.Id -Date '2026-10-13' -Reason 'Nouvelle annulation' -Now $nextDay.AddDays(1) -Actor $actor -ExpectedRevision 4} 'Une justification déjà annulée doit être reconnue.'
Set-ScoliaTeacherJustification -Document $single -TeacherId $teacher.Id -Date '2026-10-13' -Reason 'Justification corrigée après contrôle' -Now $nextDay.AddDays(1) -Actor $actor -ExpectedRevision 4
$justification=@($single.Items|Where-Object {$_.Kind -eq 'Justification'})[0]
Assert-Personnel (-not $justification.Revoked -and @($justification.Audit).Count -eq 4) 'Une nouvelle justification doit pouvoir être validée avec son historique complet.'
Set-ScoliaTeacherJustification -Document $single -TeacherId $teacher.Id -Date '2026-10-14' -Reason 'Congé anticipé autorisé' -Now $nextDay -Actor $actor -ExpectedRevision 4
$futureJustification=@($single.Items|Where-Object {$_.Kind -eq 'Justification' -and $_.Date -eq '2026-10-14'})
$status=Get-ScoliaTeacherDailyStatus -Teacher $teacher -Date '2026-10-14' -Justifications $futureJustification -WorkDates $workDates -AsOf $nextDay
Assert-Personnel ($status.Code -ceq 'Future' -and -not $status.IsAbsent -and -not $status.Deductible) 'Un motif anticipé ne doit pas transformer un jour futur en absence.'
$changed=New-ScoliaTeacher -Nom 'Enseignante A corrigée' -ActiveFrom '2026-09-01' -ActiveTo '2026-10-13' -Id $teacher.Id -Now $nextDay -Actor $actor
Set-ScoliaTeacher -Document $document -Teacher $changed -ExpectedRevision 4 -Now $nextDay -Actor $actor
$document.Revision=5
$changed=@($document.Items|Where-Object {$_.Kind -eq 'Teacher'})[0]
Assert-Personnel (@(Get-ScoliaTeacherRoster -Document $document -Date '2026-10-13').Count -eq 1 -and @(Get-ScoliaTeacherRoster -Document $document -Date '2026-10-14').Count -eq 0) 'La date de fin doit être inclusive et l''historique conservé.'
Assert-Personnel ($changed.CreatedAt -ceq $teacher.CreatedAt -and @($changed.Audit).Count -eq 2) 'La correction doit conserver la création et être auditée.'
Assert-PersonnelReject {Set-ScoliaTeacherPunch -Document $document -TeacherId $teacher.Id -Direction Entry -Now $nextDay.AddDays(1) -Actor $actor -ExpectedRevision 5} 'Un enseignant sorti du personnel ne doit plus signer.'
$invalidRange=New-ScoliaTeacher -Nom 'Enseignante A' -ActiveFrom '2026-10-13' -Id $teacher.Id -Now $nextDay -Actor $actor
Assert-PersonnelReject {Set-ScoliaTeacher -Document $document -Teacher $invalidRange -ExpectedRevision 5 -Now $nextDay -Actor $actor} 'Une modification de période ne doit pas effacer le sens des signatures passées.'
$status=Get-ScoliaTeacherDailyStatus -Teacher $changed -Date '2026-10-14' -WorkDates $workDates -AsOf $nextDay.AddDays(2)
Assert-Personnel ($status.Code -ceq 'Inactive' -and -not $status.IsAbsent -and -not $status.Deductible) 'Les jours après le départ de l''enseignant ne doivent pas produire de retenue.'
$duplicate=Copy-PersonnelTestDocument $document;$duplicate.Items=@($duplicate.Items)+@($duplicate.Items|Where-Object {$_.Kind -eq 'Punch'}|Select-Object -First 1)
Assert-PersonnelReject {Assert-ScoliaPersonnelDocument $duplicate} 'Des signatures dupliquées doivent interrompre le calcul.'
$orphan=Copy-PersonnelTestDocument $document;$orphan.Items=@($orphan.Items|Where-Object {$_.Kind -ne 'Teacher'})
Assert-PersonnelReject {Assert-ScoliaPersonnelDocument $orphan} 'Une signature sans fiche enseignant doit être refusée.'
Assert-PersonnelReject {Get-ScoliaTeacherDailyStatus -Teacher $teacher -Date '2026-10-12' -Punches @($punch,$punch) -WorkDates $workDates -AsOf $nextDay} 'Le calcul journalier doit également refuser les doublons.'
Write-Output ('Tests personnel V56 : '+$script:checks+' contrôles réussis. Aucun accès aux données de production.')
