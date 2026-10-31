. (Join-Path $PSScriptRoot 'JsonCompatV65.ps1')
# Domaine du personnel V56. Les écritures sont appliquées par le service atomique commun.
function ConvertTo-ScoliaPersonnelDate([object]$Value) {
    if($Value -is [datetime]){return ([datetime]$Value).Date}
    if($Value -is [datetimeoffset]){return ([datetimeoffset]$Value).Date}
    $text=[string]$Value;$parsed=[datetime]::MinValue
    if($text -notmatch '^\d{4}-\d{2}-\d{2}$' -or -not [datetime]::TryParseExact($text,'yyyy-MM-dd',[Globalization.CultureInfo]::InvariantCulture,[Globalization.DateTimeStyles]::None,[ref]$parsed)){throw 'Date invalide : utilisez le format année-mois-jour.'}
    return $parsed.Date
}
function ConvertTo-ScoliaPersonnelId([string]$Value) {
    $parsed=[guid]::Empty
    if(-not [guid]::TryParse($Value,[ref]$parsed) -or $parsed -eq [guid]::Empty){throw 'Identifiant enseignant invalide.'}
    return $parsed.ToString('D').ToLowerInvariant()
}
function ConvertTo-ScoliaPersonnelInstant([string]$Value) {
    $parsed=[datetimeoffset]::MinValue
    if($Value -notmatch '^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(\.\d{1,7})?(Z|[+-]\d{2}:\d{2})$' -or -not [datetimeoffset]::TryParse($Value,[Globalization.CultureInfo]::InvariantCulture,[Globalization.DateTimeStyles]::None,[ref]$parsed)){throw 'Heure de signature invalide. Le décalage horaire doit être conservé.'}
    return $parsed
}
function Assert-ScoliaPersonnelActor([string]$Actor) {
    if([string]::IsNullOrWhiteSpace($Actor) -or $Actor.Trim().Length -gt 160){throw 'Utilisateur de saisie manquant ou invalide.'}
}
function Assert-ScoliaPersonnelRevision([object]$Document,[int]$ExpectedRevision) {
    if(-not $Document -or [string]$Document.Revision -notmatch '^\d+$' -or [int]$Document.Revision -ne $ExpectedRevision){throw 'Les informations du personnel ont changé. Actualisez avant de signer ou de modifier.'}
}
function Test-ScoliaTeacherActive([object]$Teacher,[object]$Date) {
    $day=ConvertTo-ScoliaPersonnelDate $Date;$start=ConvertTo-ScoliaPersonnelDate $Teacher.ActiveFrom
    $end=$(if([string]::IsNullOrWhiteSpace([string]$Teacher.ActiveTo)){[datetime]::MaxValue.Date}else{ConvertTo-ScoliaPersonnelDate $Teacher.ActiveTo})
    return ($day -ge $start -and $day -le $end)
}
function Assert-ScoliaTeacherRecord([object]$Teacher) {
    if(-not $Teacher -or $Teacher.Kind -cne 'Teacher'){throw 'Fiche enseignant invalide.'}
    $id=ConvertTo-ScoliaPersonnelId ([string]$Teacher.Id)
    if([string]$Teacher.Id -cne $id){throw 'Identifiant enseignant non normalisé.'}
    if([string]::IsNullOrWhiteSpace([string]$Teacher.Nom) -or ([string]$Teacher.Nom).Trim().Length -gt 160){throw 'Le nom de l''enseignant est obligatoire et limité à 160 caractères.'}
    $start=ConvertTo-ScoliaPersonnelDate $Teacher.ActiveFrom
    if([string]$Teacher.ActiveFrom -cne $start.ToString('yyyy-MM-dd')){throw 'Date de début enseignant non normalisée.'}
    if(-not [string]::IsNullOrWhiteSpace([string]$Teacher.ActiveTo)){
        $end=ConvertTo-ScoliaPersonnelDate $Teacher.ActiveTo
        if([string]$Teacher.ActiveTo -cne $end.ToString('yyyy-MM-dd') -or $end -lt $start){throw 'La date de fin doit suivre la date de début.'}
    }
}
function Assert-ScoliaTeacherPunchRecord([object]$Punch,[object]$Teacher) {
    if(-not $Punch -or $Punch.Kind -cne 'Punch'){throw 'Pointage enseignant invalide.'}
    $id=ConvertTo-ScoliaPersonnelId ([string]$Punch.TeacherId);$day=ConvertTo-ScoliaPersonnelDate $Punch.Date
    if($id -cne [string]$Teacher.Id -or [string]$Punch.TeacherId -cne $id -or [string]$Punch.Date -cne $day.ToString('yyyy-MM-dd') -or [string]$Punch.Id -cne ($id+'|'+$day.ToString('yyyy-MM-dd'))){throw 'La signature ne correspond pas à cet enseignant ou à ce jour.'}
    if(-not (Test-ScoliaTeacherActive $Teacher $day)){throw 'Une signature se trouve hors de la période d''activité de l''enseignant.'}
    $entry=ConvertTo-ScoliaPersonnelInstant ([string]$Punch.EntryAt)
    if($entry.Date -ne $day){throw 'Le jour de signature ne correspond pas à son heure d''entrée.'}
    Assert-ScoliaPersonnelActor ([string]$Punch.EntryBy)
    if(-not [string]::IsNullOrWhiteSpace([string]$Punch.ExitAt)){
        $exit=ConvertTo-ScoliaPersonnelInstant ([string]$Punch.ExitAt)
        if($exit.Date -ne $day -or $exit -lt $entry){throw 'La sortie doit suivre l''entrée au cours du même jour.'}
        Assert-ScoliaPersonnelActor ([string]$Punch.ExitBy)
    }elseif(-not [string]::IsNullOrWhiteSpace([string]$Punch.ExitBy)){throw 'Utilisateur de sortie renseigné sans signature de sortie.'}
}
function Assert-ScoliaTeacherJustificationRecord([object]$Justification,[object]$Teacher) {
    if(-not $Justification -or $Justification.Kind -cne 'Justification'){throw 'Justification enseignant invalide.'}
    $id=ConvertTo-ScoliaPersonnelId ([string]$Justification.TeacherId);$day=ConvertTo-ScoliaPersonnelDate $Justification.Date
    if([string]$Justification.TeacherId -cne $id -or $id -cne [string]$Teacher.Id -or [string]$Justification.Date -cne $day.ToString('yyyy-MM-dd') -or [string]$Justification.Id -cne ($id+'|'+$day.ToString('yyyy-MM-dd'))){throw 'Justification affectée à un enseignant ou à un jour invalide.'}
    if(-not (Test-ScoliaTeacherActive $Teacher $day)){throw 'Justification hors de la période d''activité de l''enseignant.'}
    if([string]::IsNullOrWhiteSpace([string]$Justification.Reason) -or ([string]$Justification.Reason).Trim().Length -gt 1000){throw 'Un motif de justification de 1 à 1000 caractères est obligatoire.'}
    [void](ConvertTo-ScoliaPersonnelInstant ([string]$Justification.UpdatedAt));Assert-ScoliaPersonnelActor ([string]$Justification.UpdatedBy)
    if($Justification.PSObject.Properties['Revoked']){
        if($Justification.Revoked -isnot [bool]){throw 'État de justification invalide.'}
        if($Justification.Revoked){
            if([string]::IsNullOrWhiteSpace([string]$Justification.RevocationReason) -or ([string]$Justification.RevocationReason).Length -gt 1000){throw 'Le motif d''annulation de justification est invalide.'}
            [void](ConvertTo-ScoliaPersonnelInstant ([string]$Justification.RevokedAt));Assert-ScoliaPersonnelActor ([string]$Justification.RevokedBy)
        }
    }
}
function Assert-ScoliaPersonnelDocument([object]$Document) {
    if(-not $Document -or $Document.Schema -ne 1 -or -not $Document.PSObject.Properties['Items'] -or [string]$Document.Revision -notmatch '^\d+$'){throw 'Fichier du personnel invalide. Les informations existantes sont conservées.'}
    $teachers=@{};$keys=@{}
    foreach($item in @($Document.Items)){
        if(-not $item -or $item.Kind -cnotin @('Teacher','Punch','Justification')){throw 'Type de donnée du personnel inconnu.'}
        $key=[string]$item.Kind+'|'+[string]$item.Id
        if($keys.ContainsKey($key)){throw 'Une fiche, une signature ou une justification est enregistrée en double.'};$keys[$key]=$true
        if($item.Kind -ceq 'Teacher'){Assert-ScoliaTeacherRecord $item;$teachers[[string]$item.Id]=$item}
    }
    foreach($item in @($Document.Items)){
        if($item.Kind -ceq 'Teacher'){continue}
        $teacher=$teachers[[string]$item.TeacherId];if(-not $teacher){throw 'Une signature ou une justification fait référence à un enseignant absent du registre.'}
        if($item.Kind -ceq 'Punch'){Assert-ScoliaTeacherPunchRecord $item $teacher}else{Assert-ScoliaTeacherJustificationRecord $item $teacher}
    }
}
function New-ScoliaTeacher {
    param([string]$Nom,[object]$ActiveFrom,[object]$ActiveTo='',[string]$Id='',[datetimeoffset]$Now=[datetimeoffset]::Now,[string]$Actor)
    Assert-ScoliaPersonnelActor $Actor
    if([string]::IsNullOrWhiteSpace($Nom)){throw 'Le nom de l''enseignant est obligatoire.'}
    $teacherId=$(if([string]::IsNullOrWhiteSpace($Id)){[guid]::NewGuid().ToString('D')}else{ConvertTo-ScoliaPersonnelId $Id})
    $start=ConvertTo-ScoliaPersonnelDate $ActiveFrom;$end=$(if([string]::IsNullOrWhiteSpace([string]$ActiveTo)){''}else{(ConvertTo-ScoliaPersonnelDate $ActiveTo).ToString('yyyy-MM-dd')})
    $record=[pscustomobject]@{Kind='Teacher';Id=$teacherId;Nom=$Nom.Trim();ActiveFrom=$start.ToString('yyyy-MM-dd');ActiveTo=$end;CreatedAt=$Now.ToString('o');CreatedBy=$Actor.Trim();UpdatedAt=$Now.ToString('o');UpdatedBy=$Actor.Trim();Audit=@()}
    Assert-ScoliaTeacherRecord $record
    return $record
}
function Set-ScoliaTeacher {
    param([object]$Document,[object]$Teacher,[int]$ExpectedRevision,[datetimeoffset]$Now=[datetimeoffset]::Now,[string]$Actor)
    Assert-ScoliaPersonnelActor $Actor;Assert-ScoliaPersonnelRevision $Document $ExpectedRevision;Assert-ScoliaPersonnelDocument $Document;Assert-ScoliaTeacherRecord $Teacher
    $existing=@($Document.Items|Where-Object {$_.Kind -ceq 'Teacher' -and $_.Id -ceq $Teacher.Id})
    foreach($record in @($Document.Items|Where-Object {$_.Kind -cne 'Teacher' -and $_.TeacherId -ceq $Teacher.Id})){
        if(-not (Test-ScoliaTeacherActive $Teacher $record.Date)){throw 'La période proposée exclut des signatures ou justifications historiques. Ces informations doivent rester consultables.'}
    }
    $copy=New-ScoliaTeacher -Nom $Teacher.Nom -ActiveFrom $Teacher.ActiveFrom -ActiveTo $Teacher.ActiveTo -Id $Teacher.Id -Now $Now -Actor $Actor
    if($existing.Count -eq 1){
        $before=$existing[0];$copy.CreatedAt=$before.CreatedAt;$copy.CreatedBy=$before.CreatedBy
        $event=[pscustomobject]@{At=$Now.ToString('o');By=$Actor.Trim();Action='Update';Before=[pscustomobject]@{Nom=$before.Nom;ActiveFrom=$before.ActiveFrom;ActiveTo=$before.ActiveTo};After=[pscustomobject]@{Nom=$copy.Nom;ActiveFrom=$copy.ActiveFrom;ActiveTo=$copy.ActiveTo}}
        $copy.Audit=@($before.Audit)+@($event)
    }else{$copy.Audit=@([pscustomobject]@{At=$Now.ToString('o');By=$Actor.Trim();Action='Create'})}
    $Document.Items=@($Document.Items|Where-Object {-not ($_.Kind -ceq 'Teacher' -and $_.Id -ceq $copy.Id)})+@($copy)
}
function Get-ScoliaTeacherRoster {
    param([object]$Document,[object]$Date)
    Assert-ScoliaPersonnelDocument $Document;$day=ConvertTo-ScoliaPersonnelDate $Date
    return @($Document.Items|Where-Object {$_.Kind -ceq 'Teacher' -and (Test-ScoliaTeacherActive $_ $day)}|Sort-Object Nom,Id)
}
function Set-ScoliaTeacherPunch {
    param([object]$Document,[string]$TeacherId,[ValidateSet('Entry','Exit')][string]$Direction,[datetimeoffset]$Now=[datetimeoffset]::Now,[string]$Actor,[int]$ExpectedRevision)
    Assert-ScoliaPersonnelActor $Actor;Assert-ScoliaPersonnelRevision $Document $ExpectedRevision;Assert-ScoliaPersonnelDocument $Document
    if($Direction -notin @('Entry','Exit')){throw 'Choisissez la signature d''entrée ou de sortie.'}
    $teacherId=ConvertTo-ScoliaPersonnelId $TeacherId;$teacher=@($Document.Items|Where-Object {$_.Kind -ceq 'Teacher' -and $_.Id -ceq $teacherId})
    if($teacher.Count -ne 1){throw 'Enseignant introuvable. Actualisez le registre.'}
    $date=$Now.Date.ToString('yyyy-MM-dd');if(-not (Test-ScoliaTeacherActive $teacher[0] $date)){throw 'Cet enseignant n''est pas actif aujourd''hui.'}
    $existing=@($Document.Items|Where-Object {$_.Kind -ceq 'Punch' -and $_.TeacherId -ceq $teacherId -and $_.Date -ceq $date})
    if($Direction -eq 'Entry'){
        if($existing.Count -gt 0){throw 'L''entrée est déjà signée. Son heure ne peut pas être remplacée.'}
        $record=[pscustomobject]@{Kind='Punch';Id=($teacherId+'|'+$date);TeacherId=$teacherId;Date=$date;EntryAt=$Now.ToString('o');EntryBy=$Actor.Trim();ExitAt='';ExitBy=''}
        $Document.Items=@($Document.Items)+@($record)
    }else{
        if($existing.Count -ne 1){throw 'Signez d''abord l''entrée de ce jour. Une entrée de la veille ne peut pas être utilisée.'}
        $record=$existing[0];if(-not [string]::IsNullOrWhiteSpace([string]$record.ExitAt)){throw 'La sortie est déjà signée. Son heure ne peut pas être remplacée.'}
        $entry=ConvertTo-ScoliaPersonnelInstant ([string]$record.EntryAt)
        if($Now -lt $entry){throw 'L''heure actuelle précède l''entrée enregistrée. Vérifiez l''horloge de l''ordinateur.'}
        $record.ExitAt=$Now.ToString('o');$record.ExitBy=$Actor.Trim()
    }
}
function Set-ScoliaTeacherJustification {
    param([object]$Document,[string]$TeacherId,[object]$Date,[string]$Reason,[datetimeoffset]$Now=[datetimeoffset]::Now,[string]$Actor,[int]$ExpectedRevision)
    Assert-ScoliaPersonnelActor $Actor;Assert-ScoliaPersonnelRevision $Document $ExpectedRevision;Assert-ScoliaPersonnelDocument $Document
    $teacherId=ConvertTo-ScoliaPersonnelId $TeacherId;$teacher=@($Document.Items|Where-Object {$_.Kind -ceq 'Teacher' -and $_.Id -ceq $teacherId});$day=ConvertTo-ScoliaPersonnelDate $Date
    if($teacher.Count -ne 1){throw 'Enseignant introuvable.'};if([string]::IsNullOrWhiteSpace($Reason)){throw 'Renseignez le motif de justification.'}
    $dateKey=$day.ToString('yyyy-MM-dd');$record=[pscustomobject]@{Kind='Justification';Id=($teacherId+'|'+$dateKey);TeacherId=$teacherId;Date=$dateKey;Reason=$Reason.Trim();UpdatedAt=$Now.ToString('o');UpdatedBy=$Actor.Trim();Revoked=$false;RevokedAt='';RevokedBy='';RevocationReason='';Audit=@()}
    Assert-ScoliaTeacherJustificationRecord $record $teacher[0]
    $old=@($Document.Items|Where-Object {$_.Kind -ceq 'Justification' -and $_.Id -ceq $record.Id})
    $priorAudit=@();if($old.Count -eq 1){$priorAudit=@($old[0].Audit)}
    $record.Audit=$priorAudit+@([pscustomobject]@{At=$Now.ToString('o');By=$Actor.Trim();Action='Set';Before=$(if($old.Count -eq 1){[string]$old[0].Reason}else{''});After=$record.Reason})
    $Document.Items=@($Document.Items|Where-Object {-not ($_.Kind -ceq 'Justification' -and $_.Id -ceq $record.Id)})+@($record)
}
function Revoke-ScoliaTeacherJustification {
    param([object]$Document,[string]$TeacherId,[object]$Date,[string]$Reason,[datetimeoffset]$Now=[datetimeoffset]::Now,[string]$Actor,[int]$ExpectedRevision)
    Assert-ScoliaPersonnelActor $Actor;Assert-ScoliaPersonnelRevision $Document $ExpectedRevision;Assert-ScoliaPersonnelDocument $Document
    if([string]::IsNullOrWhiteSpace($Reason) -or $Reason.Trim().Length -gt 1000){throw 'Renseignez le motif d''annulation de la justification (1000 caractères maximum).'}
    $id=ConvertTo-ScoliaPersonnelId $TeacherId;$day=(ConvertTo-ScoliaPersonnelDate $Date).ToString('yyyy-MM-dd')
    $records=@($Document.Items|Where-Object {$_.Kind -ceq 'Justification' -and $_.TeacherId -ceq $id -and $_.Date -ceq $day})
    if($records.Count -ne 1){throw 'Aucune justification à annuler pour ce jour.'};$record=$records[0]
    if($record.PSObject.Properties['Revoked'] -and $record.Revoked){throw 'Cette justification est déjà annulée.'}
    foreach($property in @('Revoked','RevokedAt','RevokedBy','RevocationReason')){if(-not $record.PSObject.Properties[$property]){$record|Add-Member NoteProperty $property $null}}
    $record.Revoked=$true;$record.RevokedAt=$Now.ToString('o');$record.RevokedBy=$Actor.Trim();$record.RevocationReason=$Reason.Trim();$record.UpdatedAt=$Now.ToString('o');$record.UpdatedBy=$Actor.Trim()
    $record.Audit=@($record.Audit)+@([pscustomobject]@{At=$Now.ToString('o');By=$Actor.Trim();Action='Revoke';Before=$record.Reason;After='';Reason=$Reason.Trim()})
}
function Get-ScoliaTeacherDailyStatus {
    param([object]$Teacher,[object]$Date,[object[]]$Punches=@(),[object[]]$Justifications=@(),[object[]]$WorkDates=@(),[datetimeoffset]$AsOf=[datetimeoffset]::Now)
    Assert-ScoliaTeacherRecord $Teacher;$day=ConvertTo-ScoliaPersonnelDate $Date;$key=$day.ToString('yyyy-MM-dd');$isActive=Test-ScoliaTeacherActive $Teacher $day
    $workSet=@{};foreach($workDate in @($WorkDates)){$workSet[(ConvertTo-ScoliaPersonnelDate $workDate).ToString('yyyy-MM-dd')]=$true};$isWorkday=$workSet.ContainsKey($key)
    $matchingPunches=@($Punches|Where-Object {$_.TeacherId -ceq $Teacher.Id -and $_.Date -ceq $key});$matchingJustifications=@($Justifications|Where-Object {$_.TeacherId -ceq $Teacher.Id -and $_.Date -ceq $key})
    if($matchingPunches.Count -gt 1 -or $matchingJustifications.Count -gt 1){throw 'Présences ou justifications en double. Le calcul est interrompu.'}
    $entry='';$exit='';$signatures=0;$reason=''
    if($matchingPunches.Count -eq 1){Assert-ScoliaTeacherPunchRecord $matchingPunches[0] $Teacher;$entry=[string]$matchingPunches[0].EntryAt;$exit=[string]$matchingPunches[0].ExitAt;$signatures=1;if($exit){$signatures=2}}
    if($matchingJustifications.Count -eq 1){Assert-ScoliaTeacherJustificationRecord $matchingJustifications[0] $Teacher;if(-not ($matchingJustifications[0].PSObject.Properties['Revoked'] -and $matchingJustifications[0].Revoked)){$reason=[string]$matchingJustifications[0].Reason}}
    $lateRecorded=($entry -and (ConvertTo-ScoliaPersonnelInstant $entry).TimeOfDay -ge [timespan]::FromHours(8) -and $day -le $AsOf.Date)
    $latePending=(-not $entry -and $isActive -and $isWorkday -and $day -eq $AsOf.Date -and $AsOf.TimeOfDay -ge [timespan]::FromHours(8))
    $isLate=[bool]($lateRecorded -or $latePending)
    $final=($day -lt $AsOf.Date);$code='';$status='';$absent=$false;$justified=$false
    if(-not $isActive){$code='Inactive';$status='Hors période d''activité';$final=$true}
    elseif(-not $isWorkday){$code='NonWorking';$status='Jour non ouvrable';$final=$true}
    elseif($day -gt $AsOf.Date){$code='Future';$status='À venir';$final=$false}
    elseif($signatures -eq 2){$code=$(if($lateRecorded){'Late'}else{'Present'});$status=$(if($lateRecorded){'Présent en retard'}else{'Présent'});$final=$true}
    elseif($reason){$code='AbsenceJustified';$status='Absence justifiée';$absent=$true;$justified=$true}
    elseif($day -lt $AsOf.Date){$code='AbsenceUnjustified';$status='Absent non justifié';$absent=$true}
    elseif($signatures -eq 1){$code='InProgress';$status=$(if($lateRecorded){'Retard · entrée signée, sortie attendue'}else{'Entrée signée, sortie attendue'});$final=$false}
    else{$code='Awaiting';$status=$(if($latePending){'Retard · entrée à signer'}else{'À signer'});$final=$false}
    return [pscustomobject]@{TeacherId=$Teacher.Id;Date=$key;Code=$code;Status=$status;IsActive=$isActive;IsWorkday=$isWorkday;IsFinal=$final;IsLate=$isLate;LateRecorded=[bool]$lateRecorded;LatePending=[bool]$latePending;SignatureCount=$signatures;IsAbsent=$absent;IsJustified=$justified;Deductible=($absent -and -not $justified);EntryAt=$entry;ExitAt=$exit;Reason=$reason}
}
