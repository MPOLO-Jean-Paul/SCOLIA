# Présences de fin de journée : une coche par apprenant et par jour, sans contrôle horaire.
# L'horodatage est conservé uniquement pour l'audit ; il ne crée jamais de retard.
# Les absences automatiques requièrent un calendrier de cours confirmé.
function Get-ScoliaPupilMonthBounds {
    param([datetime]$Month,[string]$Year,[int]$StartMonth=0)
    $first=New-Object datetime($Month.Year,$Month.Month,1)
    $last=$first.AddMonths(1).AddDays(-1)
    $academic=Get-ScoliaAttendanceYearBounds $Year $StartMonth
    if($last -lt $academic.Start -or $first -gt $academic.End){throw 'Le mois choisi ne fait pas partie de l''année scolaire connectée.'}
    return [pscustomobject]@{Start=$first.Date;End=$last.Date;Key=$first.ToString('yyyy-MM',[Globalization.CultureInfo]::InvariantCulture);Academic=$academic}
}
function New-ScoliaPupilCalendar {
    param([datetime]$Month,[string]$Promotion,[string]$Year,[string]$ContextId,[string[]]$WorkDates,[int]$ThresholdAbsentCount=3,[int]$ConsecutiveDays=3,[int]$StartMonth=0)
    $range=Get-ScoliaPupilMonthBounds $Month $Year $StartMonth
    if(-not $Promotion.Trim() -or -not $ContextId.Trim()){throw 'La promotion et le contexte sont obligatoires.'}
    if($ThresholdAbsentCount -lt 1 -or $ThresholdAbsentCount -gt 99 -or $ConsecutiveDays -lt 1 -or $ConsecutiveDays -gt 31){throw 'Les seuils doivent être compris entre 1 et 99 absences, et 1 et 31 jours consécutifs.'}
    $seen=@{};$dates=New-Object 'System.Collections.Generic.List[string]'
    foreach($key in @($WorkDates)){
        $date=[datetime]::ParseExact([string]$key,'yyyy-MM-dd',[Globalization.CultureInfo]::InvariantCulture)
        if($date -lt $range.Start -or $date -gt $range.End -or $date -lt $range.Academic.Start -or $date -gt $range.Academic.End){throw 'Un jour de cours sort du mois ou de l''année scolaire.'}
        if($seen.ContainsKey($key)){throw 'Le calendrier contient un jour de cours en double.'}
        $seen[$key]=$true;$dates.Add($key)
    }
    return [pscustomobject]@{Kind='Calendar';Id=($range.Key+'|'+(Get-ScoliaAttendanceKey $Promotion));Month=$range.Key;Promotion=$Promotion.Trim();Year=$Year;ContextId=$ContextId;StartMonth=$range.Academic.StartMonth;WorkDates=@($dates.ToArray() | Sort-Object);ThresholdAbsentCount=$ThresholdAbsentCount;ConsecutiveDays=$ConsecutiveDays;UpdatedAt=[datetimeoffset]::Now.ToString('o')}
}
function New-ScoliaPupilPromotionScope {
    param([string]$Matricule,[string]$Promotion,[string]$OldPromotion='',[datetime]$EffectiveDate,[string]$Year,[string]$ContextId,[string]$Actor,[int]$StartMonth=0,[datetimeoffset]$Now=[datetimeoffset]::Now)
    $key=Get-ScoliaAttendanceKey $Matricule;$promo=Get-ScoliaAttendanceKey $Promotion;$old=Get-ScoliaAttendanceKey $OldPromotion
    if(-not $key -or -not $promo -or -not $ContextId -or $key.Contains('|') -or $promo.Contains('|') -or $old.Contains('|')){throw 'Le matricule, la promotion et le contexte du suivi sont invalides.'}
    if(-not $Actor.Trim() -or $Actor.Length -gt 200){throw 'La confirmation du suivi requiert une session identifiable.'}
    $bounds=Get-ScoliaAttendanceYearBounds $Year $StartMonth
    if($EffectiveDate.Date -lt $bounds.Start -or $EffectiveDate.Date -gt $bounds.End -or $EffectiveDate.Date -gt $Now.Date){throw 'La date de début du suivi doit appartenir à l''année et ne peut pas être future.'}
    $date=$EffectiveDate.Date.ToString('yyyy-MM-dd',[Globalization.CultureInfo]::InvariantCulture)
    return [pscustomobject]@{Kind='PromotionScope';Id=('scope|'+$key+'|'+$date+'|'+$promo);Matricule=$key;Promotion=$promo;OldPromotion=$old;EffectiveDate=$date;Year=$Year;ContextId=$ContextId;Actor=$Actor.Trim();StartMonth=$bounds.StartMonth;UpdatedAt=$Now.ToString('o',[Globalization.CultureInfo]::InvariantCulture)}
}
function Assert-ScoliaPupilCalendars([object]$Document){
    $seen=@{}
    foreach($calendar in @($Document.Items)){
        if([string]$calendar.ContextId -cne [string]$Document.Context.Id -or [string]$calendar.Year -cne [string]$Document.Context.Year){throw 'Un calendrier d''apprenants appartient à une autre base ou année.'}
        if($calendar.PSObject.Properties['Kind'] -and [string]$calendar.Kind -ceq 'PromotionScope'){
            $effective=[datetime]::ParseExact([string]$calendar.EffectiveDate,'yyyy-MM-dd',[Globalization.CultureInfo]::InvariantCulture)
            $updated=[datetimeoffset]::ParseExact([string]$calendar.UpdatedAt,'o',[Globalization.CultureInfo]::InvariantCulture)
            $valid=New-ScoliaPupilPromotionScope -Matricule $calendar.Matricule -Promotion $calendar.Promotion -OldPromotion $calendar.OldPromotion -EffectiveDate $effective -Year $calendar.Year -ContextId $calendar.ContextId -Actor $calendar.Actor -StartMonth ([int]$calendar.StartMonth) -Now $updated
            if([string]$valid.Id -cne [string]$calendar.Id -or $seen.ContainsKey([string]$valid.Id)){throw 'Une affectation du suivi est invalide ou dupliquée.'}
            $seen[$valid.Id]=$true;continue
        }
        if($calendar.PSObject.Properties['Kind'] -and [string]$calendar.Kind -cne 'Calendar'){throw 'Type de suivi apprenant inconnu.'}
        $month=[datetime]::ParseExact([string]$calendar.Month+'-01','yyyy-MM-dd',[Globalization.CultureInfo]::InvariantCulture)
        $valid=New-ScoliaPupilCalendar -Month $month -Promotion $calendar.Promotion -Year $calendar.Year -ContextId $calendar.ContextId -WorkDates @($calendar.WorkDates) -ThresholdAbsentCount ([int]$calendar.ThresholdAbsentCount) -ConsecutiveDays ([int]$calendar.ConsecutiveDays) -StartMonth ([int]$calendar.StartMonth)
        if([string]$valid.Id -cne [string]$calendar.Id -or $seen.ContainsKey([string]$valid.Id)){throw 'Calendrier mensuel invalide ou dupliqué. Le fichier existant est conservé.'}
        $seen[$valid.Id]=$true
    }
}
function Set-ScoliaPupilCalendar {
    param([object]$Document,[object]$Calendar,[int]$ExpectedRevision)
    if([int]$Document.Revision -ne $ExpectedRevision){throw 'Le calendrier a changé. Actualisez avant de continuer.'}
    Assert-ScoliaPupilCalendars $Document
    if([string]$Document.Context.Id -cne [string]$Calendar.ContextId -or [string]$Document.Context.Year -cne [string]$Calendar.Year){throw 'Le calendrier appartient à un autre dossier scolaire.'}
    $month=[datetime]::ParseExact([string]$Calendar.Month+'-01','yyyy-MM-dd',[Globalization.CultureInfo]::InvariantCulture)
    $valid=New-ScoliaPupilCalendar -Month $month -Promotion $Calendar.Promotion -Year $Calendar.Year -ContextId $Calendar.ContextId -WorkDates @($Calendar.WorkDates) -ThresholdAbsentCount ([int]$Calendar.ThresholdAbsentCount) -ConsecutiveDays ([int]$Calendar.ConsecutiveDays) -StartMonth ([int]$Calendar.StartMonth)
    $Document.Items=@($Document.Items | Where-Object {[string]$_.Id -cne [string]$valid.Id})+@($valid)
}
function Set-ScoliaPupilPromotionScope {
    param([object]$Document,[object]$Scope,[int]$ExpectedRevision)
    if([int]$Document.Revision -ne $ExpectedRevision){throw 'Le suivi des promotions a changé. Actualisez avant de confirmer.'}
    Assert-ScoliaPupilCalendars $Document
    if([string]$Scope.ContextId -cne [string]$Document.Context.Id -or [string]$Scope.Year -cne [string]$Document.Context.Year){throw 'L''affectation appartient à un autre dossier scolaire.'}
    $instant=[datetimeoffset]::ParseExact([string]$Scope.UpdatedAt,'o',[Globalization.CultureInfo]::InvariantCulture)
    $effective=[datetime]::ParseExact([string]$Scope.EffectiveDate,'yyyy-MM-dd',[Globalization.CultureInfo]::InvariantCulture)
    $valid=New-ScoliaPupilPromotionScope -Matricule $Scope.Matricule -Promotion $Scope.Promotion -OldPromotion $Scope.OldPromotion -EffectiveDate $effective -Year $Scope.Year -ContextId $Scope.ContextId -Actor $Scope.Actor -StartMonth ([int]$Scope.StartMonth) -Now $instant
    if([string]$Scope.Id -cne [string]$valid.Id){throw 'Identifiant d''affectation invalide.'}
    $old=@($Document.Items | Where-Object {[string]$_.Id -ceq $valid.Id})
    if($old.Count -gt 0){throw 'Cette affectation a déjà été confirmée à cette date. Son historique est conservé.'}
    $Document.Items=@($Document.Items)+@($valid)
}
function Record-ScoliaPupilPromotionTransition {
    param([string]$Matricule,[string]$OldPromotion,[string]$NewPromotion,[datetime]$EffectiveDate=(Get-Date).Date)
    if((Get-ScoliaAttendanceKey $OldPromotion) -eq (Get-ScoliaAttendanceKey $NewPromotion)){return}
    $context=Get-ScoliaFeatureContext
    $scope=New-ScoliaPupilPromotionScope -Matricule $Matricule -Promotion $NewPromotion -OldPromotion $OldPromotion -EffectiveDate $EffectiveDate -Year $context.Year -ContextId $context.Id -Actor (Get-ScoliaPupilAttendanceActor)
    $store=Read-ScoliaFeatureStore -Name 'suiviapprenants';Assert-ScoliaPupilCalendars $store
    if(@($store.Items | Where-Object {[string]$_.Id -ceq $scope.Id}).Count -gt 0){return}
    $expectedPupilRevision=[int]$store.Revision
    [void](Invoke-ScoliaFeatureUpdate -Name 'suiviapprenants' -Change {param($document) Set-ScoliaPupilPromotionScope -Document $document -Scope $scope -ExpectedRevision $expectedPupilRevision})
}
function Get-ScoliaPupilEnrollmentDate {
    param([object]$Learner,[datetime]$Fallback)
    $raw=([string]$Learner.Inscription).Trim()
    if(-not $raw){return $Fallback.Date}
    $parsed=[datetime]::MinValue
    foreach($format in @('dd/MM/yyyy','d/M/yyyy','yyyy-MM-dd','yyyy-MM-ddTHH:mm:ss')){
        if([datetime]::TryParseExact($raw,$format,[Globalization.CultureInfo]::InvariantCulture,[Globalization.DateTimeStyles]::None,[ref]$parsed)){return $parsed.Date}
    }
    throw ('Date d''inscription invalide pour '+[string]$Learner.Matricule+'. Corrigez son dossier avant le suivi mensuel.')
}
function Get-ScoliaPupilRecordedDays {
    param([object[]]$Sheets,[datetime]$Start,[datetime]$End,[string]$Year,[string]$ContextId)
    $result=@{};$sheetIds=@{}
    $ranks=@{'Absent'=1;'Justifié'=2;'Présent'=3;'Retard'=4}
    foreach($sheet in @($Sheets)){
        if([string]$sheet.Year -cne $Year -or [string]$sheet.ContextId -cne $ContextId){throw 'Les présences contiennent une feuille d''une autre année ou base.'}
        $date=[datetime]::ParseExact([string]$sheet.Date,'yyyy-MM-dd',[Globalization.CultureInfo]::InvariantCulture)
        if($date -lt $Start -or $date -gt $End){continue}
        $expected=[string]$sheet.Date+'|'+(Get-ScoliaAttendanceKey $sheet.Promotion)
        if([string]$sheet.Id -cne $expected -or $sheetIds.ContainsKey($expected)){throw 'Une feuille quotidienne est invalide ou dupliquée.'}
        $sheetIds[$expected]=$true;$seen=@{}
        foreach($entry in @($sheet.Entries)){
            $matricule=Get-ScoliaAttendanceKey $entry.Matricule
            if(-not $matricule -or $seen.ContainsKey($matricule) -or [string]$entry.Status -notin @('Présent','Absent','Retard','Justifié')){throw 'Une feuille de présences contient un matricule dupliqué ou un statut invalide.'}
            $seen[$matricule]=$true
            if([string]$entry.Status -eq 'Justifié' -and -not ([string]$entry.Reason).Trim()){throw 'Une absence justifiée doit avoir un motif.'}
            $signedAt='';$actor=''
            if($entry.PSObject.Properties['SignedAt'] -and [string]$entry.SignedAt){
                $stamp=[datetimeoffset]::ParseExact([string]$entry.SignedAt,'o',[Globalization.CultureInfo]::InvariantCulture)
                if($stamp.Date -ne $date.Date -or [string]$entry.Status -notin @('Présent','Retard') -or -not ([string]$entry.Actor).Trim()){throw 'Une signature d''apprenant est invalide.'}
                $signedAt=[string]$entry.SignedAt;$actor=[string]$entry.Actor
            }
            $key=$matricule+'|'+[string]$sheet.Date
            $record=[pscustomobject]@{Date=[string]$sheet.Date;Matricule=$matricule;Nom=[string]$entry.Nom;Promotion=[string]$sheet.Promotion;Status=[string]$entry.Status;Reason=[string]$entry.Reason;SignedAt=$signedAt;Actor=$actor;ReconciledRecords=0}
            if($result.ContainsKey($key)){
                $old=$result[$key];$extra=[int]$old.ReconciledRecords+1
                if($signedAt -and $old.SignedAt -and ($signedAt -cne $old.SignedAt -or $actor -cne $old.Actor -or [string]$entry.Status -cne $old.Status)){throw ('Deux signatures différentes existent le même jour pour '+$matricule+'. Consultez le diagnostic.')}
                # Les anciennes feuilles de deux promotions ne comptent qu'une journée.
                if(($signedAt -and -not $old.SignedAt) -or (-not $old.SignedAt -and [int]$ranks[$record.Status] -gt [int]$ranks[$old.Status])){$result[$key]=$record}
                $result[$key].ReconciledRecords=$extra
            }else{$result[$key]=$record}
        }
    }
    return $result
}
function Get-ScoliaPupilMonthlySummary {
    param([object[]]$Learners,[object[]]$Sheets,[object[]]$Calendars,[datetime]$Month,[string]$Year,[string]$ContextId,[string]$Promotion='',[datetimeoffset]$Now=[datetimeoffset]::Now)
    $monthKey=$Month.ToString('yyyy-MM',[Globalization.CultureInfo]::InvariantCulture)
    $savedStarts=@($Calendars|Where-Object {[string]$_.Kind -ne 'PromotionScope' -and [string]$_.Month -ceq $monthKey}|ForEach-Object {[int]$_.StartMonth}|Select-Object -Unique)
    if($savedStarts.Count -gt 1){throw 'Les calendriers du mois utilisent des rentrées différentes. Vérifiez le suivi avant de calculer des absences.'}
    $range=Get-ScoliaPupilMonthBounds $Month $Year $(if($savedStarts.Count){$savedStarts[0]}else{0})
    $calendarDoc=[pscustomobject]@{Context=[pscustomobject]@{Id=$ContextId;Year=$Year};Items=@($Calendars)}
    Assert-ScoliaPupilCalendars $calendarDoc
    $calendarsByPromo=@{}
    foreach($calendar in @($Calendars)){if([string]$calendar.Kind -ne 'PromotionScope' -and [string]$calendar.Month -eq $range.Key){$calendarsByPromo[(Get-ScoliaAttendanceKey $calendar.Promotion)]=$calendar}}
    $workSets=@{}
    foreach($key in @($calendarsByPromo.Keys)){$set=@{};foreach($dateKey in @($calendarsByPromo[$key].WorkDates)){$set[[string]$dateKey]=$true};$workSets[$key]=$set}
    $records=Get-ScoliaPupilRecordedDays -Sheets $Sheets -Start $range.Start -End $range.End -Year $Year -ContextId $ContextId
    $history=Get-ScoliaPupilRecordedDays -Sheets $Sheets -Start $range.Academic.Start -End $range.Academic.End -Year $Year -ContextId $ContextId
    $recordLists=@{};$historyLists=@{};$scopeLists=@{}
    foreach($record in @($records.Values)){
        if(-not $recordLists.ContainsKey($record.Matricule)){$recordLists[$record.Matricule]=New-Object 'System.Collections.Generic.List[object]'}
        $recordLists[$record.Matricule].Add($record)
    }
    foreach($record in @($history.Values)){
        if(-not $historyLists.ContainsKey($record.Matricule)){$historyLists[$record.Matricule]=New-Object 'System.Collections.Generic.List[object]'}
        $historyLists[$record.Matricule].Add($record)
    }
    foreach($scope in @($Calendars | Where-Object {[string]$_.Kind -ceq 'PromotionScope'})){
        $key=Get-ScoliaAttendanceKey $scope.Matricule
        if(-not $scopeLists.ContainsKey($key)){$scopeLists[$key]=New-Object 'System.Collections.Generic.List[object]'}
        $scopeLists[$key].Add($scope)
    }
    $roster=@{};$duplicateKeys=@{}
    foreach($learner in @($Learners | Where-Object {[string]$_.Annee -ceq $Year})){
        $key=Get-ScoliaAttendanceKey $learner.Matricule
        if(-not $key){continue}
        if($roster.ContainsKey($key)){$duplicateKeys[$key]=$true}else{$roster[$key]=$learner}
    }
    foreach($key in @($duplicateKeys.Keys)){$roster.Remove($key)}
    # Conserver aussi les dossiers sortis dans l'historique des appels enregistrés.
    foreach($record in @($records.Values)){
        if(-not $roster.ContainsKey($record.Matricule) -and -not $duplicateKeys.ContainsKey($record.Matricule)){$roster[$record.Matricule]=[pscustomobject]@{Matricule=$record.Matricule;Nom=$record.Nom;PostNom='';Prenom='';Promotion=$record.Promotion;Annee=$Year;Statut='Historique';Eligible=$false;Inscription=''}}
    }
    $disciplineDocuments=@();if($script:store -and $script:storeFolder -and (Test-Path function:Get-ScoliaDisciplineDocuments)){$disciplineDocuments=@(Get-ScoliaDisciplineDocuments)}
    $rows=New-Object 'System.Collections.Generic.List[object]'
    foreach($matricule in @($roster.Keys)){
        $learner=$roster[$matricule];$promoKey=Get-ScoliaAttendanceKey $learner.Promotion
        $ownRecords=@(if($recordLists.ContainsKey($matricule)){$recordLists[$matricule].ToArray()})
        $ownHistory=@(if($historyLists.ContainsKey($matricule)){$historyLists[$matricule].ToArray()})
        $scopes=@(if($scopeLists.ContainsKey($matricule)){$scopeLists[$matricule].ToArray() | Sort-Object EffectiveDate,@{Expression={([datetimeoffset]::ParseExact($_.UpdatedAt,'o',[Globalization.CultureInfo]::InvariantCulture)).UtcDateTime}}})
        $otherPromotionHistory=@($ownHistory | Where-Object {(Get-ScoliaAttendanceKey $_.Promotion) -ne $promoKey -or [int]$_.ReconciledRecords -gt 0} | Sort-Object Date)
        $knownCurrentFrom=$null
        if($otherPromotionHistory.Count -gt 0){
            $lastOther=[string]$otherPromotionHistory[-1].Date
            $currentEvidence=@($ownHistory | Where-Object {(Get-ScoliaAttendanceKey $_.Promotion) -eq $promoKey -and [int]$_.ReconciledRecords -eq 0 -and [string]$_.Date -gt $lastOther} | Sort-Object Date)
            if($currentEvidence.Count -gt 0){$knownCurrentFrom=[datetime]::ParseExact($currentEvidence[0].Date,'yyyy-MM-dd',[Globalization.CultureInfo]::InvariantCulture)}
        }
        $administrativeStatus=$(if($learner.PSObject.Properties['DisciplineBaseStatus']){$learner.DisciplineBaseStatus}else{$learner.Statut})
        $active=(Get-ScoliaAttendanceKey $administrativeStatus) -eq 'ACTIF' -and (-not $learner.PSObject.Properties['Eligible'] -or [string]$learner.Eligible -in @('True','1'))
        if(-not $active -and $ownRecords.Count -eq 0){continue}
        if($Promotion -and $promoKey -ne (Get-ScoliaAttendanceKey $Promotion) -and @($ownRecords | Where-Object {(Get-ScoliaAttendanceKey $_.Promotion) -eq (Get-ScoliaAttendanceKey $Promotion)}).Count -eq 0){continue}
        $enrolled=Get-ScoliaPupilEnrollmentDate $learner $range.Academic.Start
        $enrollmentKnown=-not [string]::IsNullOrWhiteSpace([string]$learner.Inscription)
        $calendar=$(if($calendarsByPromo.ContainsKey($promoKey)){$calendarsByPromo[$promoKey]}else{$null})
        $row=[pscustomobject]@{Matricule=$matricule;Nom=([string]$learner.Nom+' '+[string]$learner.PostNom+' '+[string]$learner.Prenom).Trim();Promotion=[string]$learner.Promotion;Month=$range.Key;Presents=0;Absents=0;Retards=0;Justifies=0;TotalAbsences=0;Appels=0;WorkingDays=0;Pending=0;ConsecutiveAbsences=0;MaxConsecutiveAbsences=0;ThresholdAbsentCount=$(if($calendar){[int]$calendar.ThresholdAbsentCount}else{3});ConsecutiveDays=$(if($calendar){[int]$calendar.ConsecutiveDays}else{3});CalendarConfigured=[bool]$calendar;EnrollmentKnown=$enrollmentKnown;Active=$active;ReconciledRecords=0;Days=@();Alert='';AbsenceDates=@();JustifiedDates=@()}
        $days=New-Object 'System.Collections.Generic.List[object]';$streak=0
        for($date=$range.Start;$date -le $range.End;$date=$date.AddDays(1)){
            $dateKey=$date.ToString('yyyy-MM-dd',[Globalization.CultureInfo]::InvariantCulture);$recordKey=$matricule+'|'+$dateKey
            $eligible=$active -and $date -ge $enrolled -and $date -ge $range.Academic.Start -and $date -le $range.Academic.End
            $disciplineStatus=$null;if($disciplineDocuments.Count){$disciplineStatus=Get-ScoliaDisciplineLearnerStatus $disciplineDocuments $matricule $Year $date;if($disciplineStatus.Mode -ne 'None'){$eligible=$false}}
            $dayPromo=$promoKey;$promotionKnown=$true
            if($scopes.Count -gt 0){
                $matchingScopes=@($scopes | Where-Object {[string]$_.EffectiveDate -le $dateKey})
                if($matchingScopes.Count -gt 0){$dayPromo=Get-ScoliaAttendanceKey $matchingScopes[-1].Promotion}
                elseif([string]$scopes[0].OldPromotion){$dayPromo=Get-ScoliaAttendanceKey $scopes[0].OldPromotion}
                else{$promotionKnown=$false}
            }elseif($otherPromotionHistory.Count -gt 0 -and ($null -eq $knownCurrentFrom -or $date -lt $knownCurrentFrom)){$promotionKnown=$false}
            $dayCalendar=$(if($calendarsByPromo.ContainsKey($dayPromo)){$calendarsByPromo[$dayPromo]}else{$null})
            $working=$eligible -and [bool]$dayCalendar -and $workSets[$dayPromo].ContainsKey($dateKey)
            $day=[pscustomobject]@{Date=$dateKey;Status='Non ouvrable';Reason='';SignedAt='';Actor='';Derived=$false;Working=$working;Promotion=$dayPromo;ReconciledRecords=0}
            if($working){$row.WorkingDays++}
            if($date -gt $Now.Date){$day.Status='À venir'}
            elseif($enrollmentKnown -and $date -lt $enrolled){$day.Status='Hors suivi';$day.Reason='Journée antérieure à l''inscription'}
            elseif($records.ContainsKey($recordKey)){
                $record=$records[$recordKey];$day.Status=$record.Status;$day.Reason=$record.Reason;$day.SignedAt=$record.SignedAt;$day.Actor=$record.Actor;$day.Promotion=$record.Promotion;$day.ReconciledRecords=$record.ReconciledRecords;$row.ReconciledRecords+=$record.ReconciledRecords
            }
            elseif($disciplineStatus -and $disciplineStatus.Mode -ne 'None'){$day.Status=$(if($disciplineStatus.Mode -eq 'Temporary'){'Renvoi temporaire'}else{'Renvoi définitif'});$day.Reason=$disciplineStatus.Description}
            elseif(-not $eligible){$day.Status='Hors suivi'}
            elseif($date -eq $Now.Date){$day.Status=$(if(-not $calendar -or ($calendarsByPromo.ContainsKey($promoKey) -and $workSets[$promoKey].ContainsKey($dateKey))){'À signer'}else{'Non ouvrable'})}
            elseif(-not $promotionKnown){$day.Status='Promotion à vérifier';$day.Reason='Date du changement de promotion non confirmée : absence automatique suspendue'}
            elseif($working -and $enrollmentKnown){$day.Status='Absent';$day.Reason='Aucune signature sur un jour de cours confirmé';$day.Derived=$true}
            elseif($working){$day.Status='Inscription à vérifier';$day.Reason='Date d''inscription manquante : absence automatique suspendue'}
            elseif(-not $dayCalendar){$day.Status='Non renseigné'}
            switch($day.Status){
                'Présent'{$row.Presents++;$row.Appels++;$streak=0}
                'Retard'{$row.Retards++;$row.Appels++;$streak=0}
                'Absent'{$row.Absents++;$row.TotalAbsences++;$row.Appels++;$row.AbsenceDates+=@($dateKey);$streak++;if($streak -gt $row.MaxConsecutiveAbsences){$row.MaxConsecutiveAbsences=$streak}}
                'Justifié'{$row.Justifies++;$row.TotalAbsences++;$row.Appels++;$row.JustifiedDates+=@($dateKey);$streak=0}
                'À signer'{$row.Pending++}
                'Non renseigné'{$streak=0}
                'Hors suivi'{$streak=0}
                'Renvoi temporaire'{$streak=0}
                'Renvoi définitif'{$streak=0}
                'Inscription à vérifier'{$streak=0}
                'Promotion à vérifier'{$streak=0}
            }
            $days.Add($day)
        }
        $row.ConsecutiveAbsences=$streak;$row.Days=@($days.ToArray())
        $warnings=New-Object 'System.Collections.Generic.List[string]'
        if($row.Absents -ge $row.ThresholdAbsentCount){$warnings.Add($row.Absents.ToString()+' absences non justifiées')}
        if($row.MaxConsecutiveAbsences -ge $row.ConsecutiveDays){$warnings.Add($row.MaxConsecutiveAbsences.ToString()+' jours de cours consécutifs')}
        $row.Alert=$warnings.ToArray() -join ' · '
        $rows.Add($row)
    }
    return @($rows.ToArray() | Sort-Object Promotion,Nom,Matricule)
}
function Get-ScoliaPupilAbsenceAlerts {
    param([object[]]$Learners,[object[]]$Sheets,[object[]]$Calendars,[datetime]$Month,[string]$Year,[string]$ContextId,[string]$Promotion='',[datetimeoffset]$Now=[datetimeoffset]::Now)
    return @(Get-ScoliaPupilMonthlySummary -Learners $Learners -Sheets $Sheets -Calendars $Calendars -Month $Month -Year $Year -ContextId $ContextId -Promotion $Promotion -Now $Now | Where-Object {$_.Alert})
}
function Set-ScoliaPupilSignature {
    param([object]$Document,[object]$Learner,[datetime]$Date,[string]$Promotion,[int]$ExpectedRevision,[string]$Actor,[datetimeoffset]$Now=[datetimeoffset]::Now)
    if([int]$Document.Revision -ne $ExpectedRevision){throw 'Les présences ont changé. Actualisez avant de signer.'}
    if(Test-Path function:Sync-ScoliaDisciplineSnapshot){[void](Sync-ScoliaDisciplineSnapshot)}
    if(Test-Path function:Assert-ScoliaDisciplineAdmission){Assert-ScoliaDisciplineAdmission ([string]$Learner.Matricule) $Date}
    if($Date.Date -ne $Now.Date){throw 'La présence est renseignée uniquement le jour même ; l''heure sert seulement à l''historique.'}
    if(-not $Actor.Trim() -or $Actor.Length -gt 200){throw 'Une session identifiable est requise pour enregistrer la présence.'}
    $range=Get-ScoliaPupilMonthBounds $Date ([string]$Document.Context.Year)
    if([string]$Learner.Annee -cne [string]$Document.Context.Year -or (Get-ScoliaAttendanceKey $Learner.Statut) -ne 'ACTIF' -or ($Learner.PSObject.Properties['Eligible'] -and [string]$Learner.Eligible -notin @('True','1'))){throw 'Cet apprenant n''est pas admissible à la signature.'}
    if((Get-ScoliaAttendanceKey $Learner.Promotion) -ne (Get-ScoliaAttendanceKey $Promotion) -or (Get-ScoliaPupilEnrollmentDate $Learner $range.Academic.Start) -gt $Date.Date){throw 'La promotion ou la date d''inscription ne permet pas cette signature.'}
    $matricule=Get-ScoliaAttendanceKey $Learner.Matricule
    if(-not $matricule){throw 'Le matricule est obligatoire.'}
    $dateKey=$Date.ToString('yyyy-MM-dd',[Globalization.CultureInfo]::InvariantCulture)
    $records=Get-ScoliaPupilRecordedDays -Sheets @($Document.Items) -Start $Date.Date -End $Date.Date -Year $Document.Context.Year -ContextId $Document.Context.Id
    $existing=$(if($records.ContainsKey($matricule+'|'+$dateKey)){$records[$matricule+'|'+$dateKey]}else{$null})
    if($existing -and ($existing.SignedAt -or $existing.Status -in @('Présent','Retard'))){throw 'La présence de cet apprenant est déjà enregistrée aujourd''hui. Une seconde signature n''est pas autorisée.'}
    if($existing -and (Get-ScoliaAttendanceKey $existing.Promotion) -ne (Get-ScoliaAttendanceKey $Promotion)){throw 'Un statut existe déjà aujourd''hui dans une autre promotion. Ouvrez cet appel pour le vérifier.'}
    $sheetId=$dateKey+'|'+(Get-ScoliaAttendanceKey $Promotion)
    $old=@($Document.Items | Where-Object {[string]$_.Id -ceq $sheetId})
    if($old.Count -gt 1){throw 'Feuille quotidienne dupliquée.'}
    $entries=@(if($old.Count -eq 1){@($old[0].Entries | Where-Object {(Get-ScoliaAttendanceKey $_.Matricule) -ne $matricule})})
    # La présence est indépendante de l'heure : aucun seuil d'entrée ni cumul de retards.
    $entries+=@([pscustomobject]@{Matricule=$matricule;Nom=([string]$Learner.Nom+' '+[string]$Learner.PostNom+' '+[string]$Learner.Prenom).Trim();Status='Présent';Reason='';SignedAt=$Now.ToString('o',[Globalization.CultureInfo]::InvariantCulture);Actor=$Actor.Trim()})
    # New-ScoliaAttendanceSheet valide les anciennes entrées et conserve les signatures.
    $sheet=New-ScoliaAttendanceSheet -Date $Date -Promotion $Promotion -Year $Document.Context.Year -ContextId $Document.Context.Id -Entries $entries -StartMonth $range.Academic.StartMonth
    Set-ScoliaAttendanceSheet -Document $Document -Sheet $sheet -ExpectedRevision $ExpectedRevision
}
function Set-ScoliaPupilDayStatus {
    param([object]$Document,[object]$Learner,[datetime]$Date,[string]$Promotion,[ValidateSet('Absent','Justifié')][string]$Status,[string]$Reason,[int]$ExpectedRevision,[datetimeoffset]$Now=[datetimeoffset]::Now)
    if($Date.Date -gt $Now.Date){throw 'Une absence future ne peut pas être enregistrée.'}
    if([int]$Document.Revision -ne $ExpectedRevision){throw 'Les présences ont changé. Actualisez avant de justifier.'}
    $range=Get-ScoliaPupilMonthBounds $Date ([string]$Document.Context.Year)
    if([string]$Learner.Annee -cne [string]$Document.Context.Year -or (Get-ScoliaPupilEnrollmentDate $Learner $range.Academic.Start) -gt $Date.Date){throw 'Cette journée est antérieure à l''inscription ou hors année scolaire.'}
    if((Get-ScoliaAttendanceKey $Learner.Promotion) -ne (Get-ScoliaAttendanceKey $Promotion)){throw 'La promotion du dossier a changé. Actualisez.'}
    if($Status -eq 'Justifié' -and -not $Reason.Trim()){throw 'Précisez le motif de justification.'}
    if($Reason.Length -gt 500){throw 'Le motif ne peut pas dépasser 500 caractères.'}
    $key=Get-ScoliaAttendanceKey $Learner.Matricule
    $dateKey=$Date.Date.ToString('yyyy-MM-dd',[Globalization.CultureInfo]::InvariantCulture)
    $records=Get-ScoliaPupilRecordedDays -Sheets @($Document.Items) -Start $Date.Date -End $Date.Date -Year $Document.Context.Year -ContextId $Document.Context.Id
    if($records.ContainsKey($key+'|'+$dateKey)){
        $existing=$records[$key+'|'+$dateKey]
        if($existing.SignedAt -or $existing.Status -in @('Présent','Retard')){throw 'Une présence enregistrée ne peut pas être remplacée par une absence.'}
        if((Get-ScoliaAttendanceKey $existing.Promotion) -ne (Get-ScoliaAttendanceKey $Promotion)){throw 'Un statut existe ce jour dans une autre promotion. Vérifiez la feuille d''origine.'}
    }
    $sheetId=$dateKey+'|'+(Get-ScoliaAttendanceKey $Promotion)
    $old=@($Document.Items | Where-Object {[string]$_.Id -ceq $sheetId})
    $entries=@(if($old.Count -eq 1){@($old[0].Entries | Where-Object {(Get-ScoliaAttendanceKey $_.Matricule) -ne $key})})
    $entries+=@([pscustomobject]@{Matricule=$key;Nom=([string]$Learner.Nom+' '+[string]$Learner.PostNom+' '+[string]$Learner.Prenom).Trim();Status=$Status;Reason=$Reason.Trim()})
    $sheet=New-ScoliaAttendanceSheet -Date $Date -Promotion $Promotion -Year $Document.Context.Year -ContextId $Document.Context.Id -Entries $entries -StartMonth $range.Academic.StartMonth
    Set-ScoliaAttendanceSheet -Document $Document -Sheet $sheet -ExpectedRevision $ExpectedRevision
}
function Get-ScoliaPupilAttendanceActor {
    $actor=$(if($script:accountSession -and $script:accountSession.Id){[string]$script:accountSession.Id}else{[Environment]::UserName})
    if(-not $actor){throw 'La session de saisie n''est pas identifiable.'}
    return $actor
}
function Get-ScoliaPupilSelectedRow([object]$State){
    if(-not $State.Grid.CurrentRow){throw 'Sélectionnez un apprenant.'}
    $key=[string]$State.Grid.CurrentRow.Cells['Matricule'].Value
    $matches=@($State.MonthRows | Where-Object {$_.Matricule -ceq $key})
    if($matches.Count -ne 1){throw 'La ligne sélectionnée est invalide. Actualisez.'}
    return $matches[0]
}
function Get-ScoliaPupilSelectedLearner([object]$State,[string]$Matricule){
    $matches=@($script:data.learners | Where-Object {(Get-ScoliaAttendanceKey $_.Matricule) -eq $Matricule -and [string]$_.Annee -ceq $State.Context.Year})
    if($matches.Count -ne 1){throw 'Le dossier a disparu ou son matricule est en double. Actualisez.'}
    return $matches[0]
}
function Update-ScoliaPupilMonthlyDialog([object]$State){
    try{
        Assert-ScoliaAttendanceContext $State
        $range=Get-ScoliaPupilMonthBounds $State.MonthPicker.Value $State.Context.Year
        $presences=Read-ScoliaFeatureStore -Name 'presences';Assert-ScoliaAttendanceDocument $presences
        $calendars=Read-ScoliaFeatureStore -Name 'suiviapprenants';Assert-ScoliaPupilCalendars $calendars
        $promotion=[string]$State.PromotionPicker.Text
        if(-not $promotion){throw 'Choisissez une promotion.'}
        $rows=@(Get-ScoliaPupilMonthlySummary -Learners @($script:data.learners) -Sheets @($presences.Items) -Calendars @($calendars.Items) -Month $State.MonthPicker.Value -Year $State.Context.Year -ContextId $State.Context.Id -Promotion $promotion)
        $calendar=@($calendars.Items | Where-Object {$_.Month -ceq $range.Key -and (Get-ScoliaAttendanceKey $_.Promotion) -eq (Get-ScoliaAttendanceKey $promotion)})
        $State.Loading=$true
        try{
            $oldKey=$(if($State.Grid.CurrentRow){[string]$State.Grid.CurrentRow.Cells['Matricule'].Value}else{''})
            $State.MonthRows=$rows;$State.PresenceRevision=[int]$presences.Revision;$State.CalendarRevision=[int]$calendars.Revision;$State.Calendar=$(if($calendar.Count -eq 1){$calendar[0]}else{$null})
            $selectedDay=$State.DayPicker.Value.Date
            if($selectedDay -lt $range.Start -or $selectedDay -gt $range.End){$selectedDay=$(if((Get-Date).Date -ge $range.Start -and (Get-Date).Date -le $range.End){(Get-Date).Date}elseif((Get-Date).Date -gt $range.End){$range.End}else{$range.Start})}
            # Élargir les bornes avant le changement de mois pour éviter une date hors plage.
            $State.DayPicker.MinDate=$range.Academic.Start;$State.DayPicker.MaxDate=$range.Academic.End
            $State.DayPicker.Value=$selectedDay
            $State.DayPicker.MinDate=$range.Start;$State.DayPicker.MaxDate=$range.End
            $selectedDate=$State.DayPicker.Value.Date.ToString('yyyy-MM-dd',[Globalization.CultureInfo]::InvariantCulture)
            $State.Grid.Rows.Clear()
            $canWrite=(-not $script:data.closed) -and (Test-ITMPermission 'Saisie')
            foreach($row in $rows){
                $day=@($row.Days | Where-Object {$_.Date -ceq $selectedDate})[0]
                $signed=$day.Status -in @('Présent','Retard')
                $time=$(if($day.SignedAt){([datetimeoffset]::ParseExact($day.SignedAt,'o',[Globalization.CultureInfo]::InvariantCulture)).ToString('HH:mm:ss')}elseif($signed){'Appel enregistré'}else{''})
                $index=$State.Grid.Rows.Add($row.Matricule,$row.Nom,$signed,$day.Status,$time,$row.Presents,$row.Retards,$row.Absents,$row.Justifies,$row.Alert)
                $gridRow=$State.Grid.Rows[$index]
                $gridRow.Cells['Signature'].ReadOnly=(-not $canWrite -or $signed -or -not $row.Active -or $State.DayPicker.Value.Date -ne (Get-Date).Date -or $day.Status -notin @('À signer','Absent','Justifié'))
                if($row.Matricule -ceq $oldKey){$State.Grid.CurrentCell=$gridRow.Cells['Nom']}
            }
            # Afficher les anciens retards uniquement lorsqu'un appel historique les contient.
            if($State.Grid.Columns.Contains('Retards')){$State.Grid.Columns['Retards'].Visible=(@($rows | Where-Object {$_.Retards -gt 0}).Count -gt 0)}
            $State.CalendarButton.Enabled=$canWrite;$State.ThresholdButton.Enabled=$canWrite -and [bool]$State.Calendar;$State.JustifyButton.Enabled=$canWrite -and $rows.Count -gt 0
            if($State.PSObject.Properties['ScopeButton']){$State.ScopeButton.Enabled=$canWrite -and $rows.Count -gt 0}
        }finally{$State.Loading=$false}
        $alerts=@($rows | Where-Object {$_.Alert}).Count
        $note=$(if($State.Calendar){'Calendrier confirmé : '+@($State.Calendar.WorkDates).Count+' jours de cours. Les journées passées sans signature comptent comme absences.'}else{'Calendrier non configuré : seules les absences des appels enregistrés sont comptabilisées.'})
        $unknownEnrollment=@($rows | Where-Object {$_.Active -and -not $_.EnrollmentKnown}).Count
        if($unknownEnrollment -gt 0){$note+=' '+$unknownEnrollment+' date(s) d''inscription manquante(s) : vérification requise, absences automatiques suspendues.'}
        $unknownPromotion=@($rows | Where-Object {@($_.Days | Where-Object {$_.Status -eq 'Promotion à vérifier'}).Count -gt 0}).Count
        if($unknownPromotion -gt 0){$note+=' '+$unknownPromotion+' historique(s) de promotion à confirmer avec Début du suivi.'}
        Set-ScoliaAttendanceMessage $State ('Après l''appel en fin de journée, cochez une fois les apprenants présents. Aucun contrôle horaire. '+$range.Start.ToString('dd/MM/yyyy')+' au '+$range.End.ToString('dd/MM/yyyy')+' · '+$rows.Count+' apprenant(s) · '+$alerts+' alerte(s). '+$note)
        return $true
    }catch{
        $message=$_.Exception.Message;$State.MonthRows=@();$State.Calendar=$null;$State.PresenceRevision=-1;$State.CalendarRevision=-1
        $State.Loading=$true
        try{$State.Grid.Rows.Clear();$State.CalendarButton.Enabled=$false;$State.ThresholdButton.Enabled=$false;$State.JustifyButton.Enabled=$false;if($State.PSObject.Properties['ScopeButton']){$State.ScopeButton.Enabled=$false}}finally{$State.Loading=$false}
        Set-ScoliaAttendanceMessage $State $message $true;return $false
    }
}
function Save-ScoliaPupilCheckbox([object]$State,[int]$RowIndex){
    if($State.Loading -or $RowIndex -lt 0){return}
    $gridRow=$State.Grid.Rows[$RowIndex]
    if(-not [bool]$gridRow.Cells['Signature'].Value){[void](Update-ScoliaPupilMonthlyDialog $State);return}
    try{
        Assert-ScoliaAttendanceContext $State $true
        $key=[string]$gridRow.Cells['Matricule'].Value
        $learner=Get-ScoliaPupilSelectedLearner $State $key
        $date=$State.DayPicker.Value.Date
        $now=[datetimeoffset]::Now
        $range=Get-ScoliaPupilMonthBounds $date $State.Context.Year
        $calendarDoc=Read-ScoliaFeatureStore -Name 'suiviapprenants';Assert-ScoliaPupilCalendars $calendarDoc
        $calendar=@($calendarDoc.Items | Where-Object {$_.Month -ceq $range.Key -and (Get-ScoliaAttendanceKey $_.Promotion) -eq (Get-ScoliaAttendanceKey $learner.Promotion)})
        if($calendar.Count -eq 1 -and $date.ToString('yyyy-MM-dd',[Globalization.CultureInfo]::InvariantCulture) -notin @($calendar[0].WorkDates)){throw 'Cette journée n''est pas un jour de cours confirmé. Vérifiez le calendrier.'}
        $actor=Get-ScoliaPupilAttendanceActor
        $expectedPupilRevision=$State.PresenceRevision
        [void](Invoke-ScoliaFeatureUpdate -Name 'presences' -Change {param($document) Set-ScoliaPupilSignature -Document $document -Learner $learner -Date $date -Promotion $learner.Promotion -ExpectedRevision $expectedPupilRevision -Actor $actor -Now $now})
        [void](Update-ScoliaPupilMonthlyDialog $State)
        Set-ScoliaAttendanceMessage $State ('Présence de fin de journée enregistrée · '+$key+' · '+$now.ToString('dd/MM/yyyy')+'. Une seule coche par jour ; l''heure de saisie reste dans l''historique, sans incidence sur la présence.')
    }catch{
        $message=$_.Exception.Message
        [void](Update-ScoliaPupilMonthlyDialog $State)
        Set-ScoliaAttendanceMessage $State $message $true
    }
}
function Save-ScoliaPupilCalendarDialog([object]$State){
    try{
        Assert-ScoliaAttendanceContext $State $true
        if(-not (Update-ScoliaPupilMonthlyDialog $State)){return}
        $selected=@(if($State.Calendar){$State.Calendar.WorkDates})
        $dates=Show-ScoliaMonthCalendar -Month $State.MonthPicker.Value -SelectedDates $selected
        if($null -eq $dates){return}
        $choice=[ITMMessageBox]::Show($State.Form,'Les jours de cours du mois vont être confirmés. Pour ces jours, toute signature manquante dans le passé sera comptabilisée comme absence non justifiée, à partir de l''inscription de chaque apprenant. Confirmer ce calendrier ?','Calendrier des présences','YesNo','Question')
        if($choice -ne 'Yes'){return}
        $threshold=$(if($State.Calendar){[int]$State.Calendar.ThresholdAbsentCount}else{3});$consecutive=$(if($State.Calendar){[int]$State.Calendar.ConsecutiveDays}else{3})
        $calendar=New-ScoliaPupilCalendar -Month $State.MonthPicker.Value -Promotion $State.PromotionPicker.Text -Year $State.Context.Year -ContextId $State.Context.Id -WorkDates @($dates) -ThresholdAbsentCount $threshold -ConsecutiveDays $consecutive
        $expectedPupilRevision=$State.CalendarRevision
        [void](Invoke-ScoliaFeatureUpdate -Name 'suiviapprenants' -Change {param($document) Set-ScoliaPupilCalendar -Document $document -Calendar $calendar -ExpectedRevision $expectedPupilRevision})
        [void](Update-ScoliaPupilMonthlyDialog $State)
    }catch{Set-ScoliaAttendanceMessage $State $_.Exception.Message $true}
}
function Show-ScoliaPupilThresholds([object]$State){
    try{
        Assert-ScoliaAttendanceContext $State $true
        if(-not (Update-ScoliaPupilMonthlyDialog $State)){return}
        if(-not $State.Calendar){throw 'Confirmez d''abord les jours de cours de ce mois.'}
        $form=New-ScoliaDialog 'Seuils des alertes internes' 470 300
        $form.Tag=[pscustomobject]@{Accepted=$false;Count=$null;Streak=$null}
        try{
            [void](New-Label $form 'Absences non justifiées dans le mois' 22 28 320 28)
            $count=New-Object Windows.Forms.NumericUpDown;$count.Location=New-Object Drawing.Point(345,25);$count.Width=80;$count.Minimum=1;$count.Maximum=99;$count.Value=[int]$State.Calendar.ThresholdAbsentCount;$form.Controls.Add($count);$form.Tag.Count=$count
            [void](New-Label $form 'Jours de cours consécutifs sans présence' 22 72 320 30)
            $streak=New-Object Windows.Forms.NumericUpDown;$streak.Location=New-Object Drawing.Point(345,70);$streak.Width=80;$streak.Minimum=1;$streak.Maximum=31;$streak.Value=[int]$State.Calendar.ConsecutiveDays;$form.Controls.Add($streak);$form.Tag.Streak=$streak
            [void](New-Label $form 'Les alertes restent dans SCOLIA. Les absences justifiées sont affichées séparément et ne déclenchent pas ces seuils.' 22 119 405 64 10)
            $accept=New-Button $form 'Enregistrer les seuils' 22 202 230 $true
            $cancel=New-Button $form 'Annuler' 274 202 150
            $accept.Add_Click({param($sender,$e) $dialog=$sender.FindForm();$dialog.Tag.Accepted=$true;$dialog.Close()})
            $cancel.Add_Click({param($sender,$e) $sender.FindForm().Close()})
            Apply-ITMThemeForm $form;[void]$form.ShowDialog($State.Form)
            if(-not $form.Tag.Accepted){return}
            $calendar=New-ScoliaPupilCalendar -Month $State.MonthPicker.Value -Promotion $State.PromotionPicker.Text -Year $State.Context.Year -ContextId $State.Context.Id -WorkDates @($State.Calendar.WorkDates) -ThresholdAbsentCount ([int]$count.Value) -ConsecutiveDays ([int]$streak.Value) -StartMonth ([int]$State.Calendar.StartMonth)
            $expectedPupilRevision=$State.CalendarRevision
            [void](Invoke-ScoliaFeatureUpdate -Name 'suiviapprenants' -Change {param($document) Set-ScoliaPupilCalendar -Document $document -Calendar $calendar -ExpectedRevision $expectedPupilRevision})
            [void](Update-ScoliaPupilMonthlyDialog $State)
        }finally{$form.Dispose()}
    }catch{Set-ScoliaAttendanceMessage $State $_.Exception.Message $true}
}
function Show-ScoliaPupilJustification([object]$State){
    try{
        Assert-ScoliaAttendanceContext $State $true
        $row=Get-ScoliaPupilSelectedRow $State
        $date=$State.DayPicker.Value.Date
        $day=@($row.Days | Where-Object {$_.Date -ceq $date.ToString('yyyy-MM-dd',[Globalization.CultureInfo]::InvariantCulture)})[0]
        if($day.Status -notin @('Absent','Justifié')){throw 'Choisissez une journée passée où cet apprenant est absent ou justifié.'}
        $learner=Get-ScoliaPupilSelectedLearner $State $row.Matricule
        $form=New-ScoliaDialog 'Justifier une absence' 550 320
        $form.Tag=[pscustomobject]@{Accepted=$false;Reason=$null;Message=$null;Status='Justifié'}
        try{
            [void](New-Label $form ($row.Nom+' · '+$date.ToString('dd/MM/yyyy')) 22 22 495 36 11 $true)
            [void](New-Label $form 'Motif de justification (obligatoire)' 22 66 495 25)
            $reason=New-Object Windows.Forms.TextBox;$reason.Multiline=$true;$reason.MaxLength=500;$reason.Location=New-Object Drawing.Point(22,98);$reason.Size=New-Object Drawing.Size(495,85);$reason.Text=[string]$day.Reason;$form.Controls.Add($reason);$form.Tag.Reason=$reason
            $message=New-Label $form '' 22 188 495 32 10;$message.Tag='scolia:feedback:error';$form.Tag.Message=$message
            $accept=New-Button $form 'Enregistrer la justification' 22 235 300 $true
            $cancel=New-Button $form 'Annuler' 347 235 170
            $accept.Add_Click({param($sender,$e) $dialog=$sender.FindForm();if(-not $dialog.Tag.Reason.Text.Trim()){$dialog.Tag.Message.Text='Précisez le motif de justification.';$dialog.Tag.Reason.Focus();return};$dialog.Tag.Accepted=$true;$dialog.Close()})
            $cancel.Add_Click({param($sender,$e) $sender.FindForm().Close()})
            if($day.Status -eq 'Justifié'){
                $form.Height+=45
                $undo=New-Button $form 'Annuler la justification' 22 278 300
                $undo.Add_Click({param($sender,$e) $dialog=$sender.FindForm();if(-not $dialog.Tag.Reason.Text.Trim()){$dialog.Tag.Message.Text='Précisez pourquoi cette justification est annulée.';return};$choice=[ITMMessageBox]::Show($dialog,'Cette journée sera de nouveau comptabilisée comme absence non justifiée. Confirmer ?','Annuler une justification','YesNo','Question');if($choice -eq 'Yes'){$dialog.Tag.Status='Absent';$dialog.Tag.Accepted=$true;$dialog.Close()}})
            }
            Apply-ITMThemeForm $form;[void]$form.ShowDialog($State.Form)
            if(-not $form.Tag.Accepted){return}
            $reasonText=$reason.Text.Trim();$expectedPupilRevision=$State.PresenceRevision;$newStatus=[string]$form.Tag.Status
            if($newStatus -eq 'Absent'){$reasonText='Justification annulée : '+$reasonText}
            [void](Invoke-ScoliaFeatureUpdate -Name 'presences' -Change {param($document) Set-ScoliaPupilDayStatus -Document $document -Learner $learner -Date $date -Promotion $learner.Promotion -Status $newStatus -Reason $reasonText -ExpectedRevision $expectedPupilRevision})
            [void](Update-ScoliaPupilMonthlyDialog $State)
            Set-ScoliaAttendanceMessage $State ('Situation '+$newStatus+' enregistrée · '+$row.Matricule+' · '+$date.ToString('dd/MM/yyyy')+'.')
        }finally{$form.Dispose()}
    }catch{Set-ScoliaAttendanceMessage $State $_.Exception.Message $true}
}
function Show-ScoliaPupilPromotionScope([object]$State){
    try{
        Assert-ScoliaAttendanceContext $State $true
        $row=Get-ScoliaPupilSelectedRow $State
        $learner=Get-ScoliaPupilSelectedLearner $State $row.Matricule
        if((Get-ScoliaAttendanceKey $learner.Statut) -ne 'ACTIF'){throw 'Seul un dossier actif peut recevoir une date de début du suivi.'}
        if(-not $row.EnrollmentKnown){throw 'Renseignez d''abord la date d''inscription dans le dossier de cet apprenant.'}
        $bounds=Get-ScoliaAttendanceYearBounds $State.Context.Year
        $earliest=Get-ScoliaPupilEnrollmentDate $learner $bounds.Start
        if($earliest -lt $bounds.Start){$earliest=$bounds.Start}
        $latest=$(if((Get-Date).Date -lt $bounds.End){(Get-Date).Date}else{$bounds.End})
        if($earliest -gt $latest){throw 'Le suivi de cet apprenant n''a pas encore commencé.'}
        $form=New-ScoliaDialog 'Confirmer le début du suivi' 575 310
        $form.Tag=[pscustomobject]@{Accepted=$false}
        try{
            [void](New-Label $form ($row.Nom+' · promotion '+$learner.Promotion) 22 22 520 36 11 $true)
            [void](New-Label $form 'À partir de quelle date cet apprenant appartient-il à cette promotion ? Les appels enregistrés restent conservés. Les journées précédentes sans appel ne seront pas déduites en absence pour cette promotion.' 22 66 520 84 10)
            $date=New-Object Windows.Forms.DateTimePicker;$date.Format='Short';$date.MinDate=$earliest;$date.MaxDate=$latest;$date.Location=New-Object Drawing.Point(22,163);$date.Width=170
            $selected=$State.DayPicker.Value.Date;if($selected -lt $earliest){$selected=$earliest};if($selected -gt $latest){$selected=$latest};$date.Value=$selected;$date.AccessibleName='Date confirmée du début de la promotion';$form.Controls.Add($date)
            $accept=New-Button $form 'Confirmer cette date' 22 226 300 $true;$cancel=New-Button $form 'Annuler' 347 226 190
            $accept.Add_Click({param($sender,$e) $dialog=$sender.FindForm();$dialog.Tag.Accepted=$true;$dialog.Close()})
            $cancel.Add_Click({param($sender,$e) $sender.FindForm().Close()})
            Apply-ITMThemeForm $form;[void]$form.ShowDialog($State.Form)
            if(-not $form.Tag.Accepted){return}
            $scope=New-ScoliaPupilPromotionScope -Matricule $row.Matricule -Promotion $learner.Promotion -EffectiveDate $date.Value.Date -Year $State.Context.Year -ContextId $State.Context.Id -Actor (Get-ScoliaPupilAttendanceActor)
            $expectedPupilRevision=$State.CalendarRevision
            [void](Invoke-ScoliaFeatureUpdate -Name 'suiviapprenants' -Change {param($document) Set-ScoliaPupilPromotionScope -Document $document -Scope $scope -ExpectedRevision $expectedPupilRevision})
            [void](Update-ScoliaPupilMonthlyDialog $State)
        }finally{$form.Dispose()}
    }catch{Set-ScoliaAttendanceMessage $State $_.Exception.Message $true}
}
function Show-ScoliaPupilAttendanceDetail {
    param([object]$State,[string]$Matricule='')
    try{
        Assert-ScoliaAttendanceContext $State
        $row=$(if($Matricule){@($State.MonthRows | Where-Object {$_.Matricule -ceq $Matricule})[0]}else{Get-ScoliaPupilSelectedRow $State})
        if(-not $row){throw 'L''apprenant est absent de cette vue mensuelle.'}
        $detail=New-ScoliaFeatureDialog -Title ('Suivi de '+$row.Nom) -Subtitle ('Mois '+$row.Month+' · '+$row.Matricule+' · une ligne par jour du premier au dernier jour') -Width 880
        foreach($column in @(@('Date','Jour',100),@('Status','Situation',155),@('SignedAt','Heure de saisie (audit)',155),@('Actor','Saisie par',120),@('Reason','Motif',320))){$c=New-Object Windows.Forms.DataGridViewTextBoxColumn;$c.Name=$column[0];$c.HeaderText=$column[1];$c.Width=$column[2];[void]$detail.Grid.Columns.Add($c)}
        foreach($day in @($row.Days)){
            $date=[datetime]::ParseExact($day.Date,'yyyy-MM-dd',[Globalization.CultureInfo]::InvariantCulture)
            $time=$(if($day.SignedAt){([datetimeoffset]::ParseExact($day.SignedAt,'o',[Globalization.CultureInfo]::InvariantCulture)).ToString('HH:mm:ss zzz')}else{''})
            [void]$detail.Grid.Rows.Add($date.ToString('dd/MM/yyyy'),$day.Status,$time,$day.Actor,$day.Reason)
        }
        $detail.Message.Height=72
        $legacyNote=$(if($row.Retards -gt 0){' · '+$row.Retards+' retard(s) saisi(s) dans les anciens appels'}else{''})
        $detail.Message.Text=([string]::Format('{0} présent(s) · {1} absence(s) non justifiée(s) · {2} justifiée(s){3}. L''heure de saisie sert uniquement à l''audit ; aucun retard automatique. {4}',$row.Presents,$row.Absents,$row.Justifies,$legacyNote,$row.Alert))
        $detail.Message.Tag='scolia:feedback:success'
        $close=New-Button $detail.Toolbar 'Fermer le détail' 0 0 165;$close.AutoSize=$true;$close.Add_Click({param($sender,$e) $sender.FindForm().Close()})
        try{[void]$detail.Form.ShowDialog($State.Form)}finally{$detail.Form.Dispose()}
    }catch{Set-ScoliaAttendanceMessage $State $_.Exception.Message $true}
}
function Export-ScoliaPupilMonthlyAttendance([object]$State){
    try{
        Assert-ScoliaAttendanceContext $State;Assert-ITMPermission 'Exporter'
        if(-not (Update-ScoliaPupilMonthlyDialog $State)){return}
        if(@($State.MonthRows).Count -eq 0){throw 'Aucun apprenant à exporter pour ce mois.'}
        $picker=New-Object Windows.Forms.SaveFileDialog
        try{
            $picker.Filter='Fichier CSV (*.csv)|*.csv';$picker.FileName='Presences_apprenants_'+$State.MonthPicker.Value.ToString('yyyy-MM')+'_'+$State.PromotionPicker.Text+'.csv'
            if($picker.ShowDialog($State.Form) -ne 'OK'){return}
            $rows=New-Object 'System.Collections.Generic.List[object]'
            foreach($row in @($State.MonthRows)){foreach($day in @($row.Days)){$rows.Add([pscustomobject]@{Mois=$row.Month;Matricule=$row.Matricule;Apprenant=$row.Nom;Promotion=$day.Promotion;Jour=$day.Date;Situation=$day.Status;HeureSaisieAudit=$day.SignedAt;SaisiPar=$day.Actor;Motif=$day.Reason;AbsencesNonJustifiees=$row.Absents;AbsencesJustifiees=$row.Justifies;Alerte=$row.Alert;CalendrierConfirme=$row.CalendarConfigured})}}
            $csv=@($rows.ToArray() | ConvertTo-Csv -NoTypeInformation -Delimiter ';') -join "`r`n"
            [IO.File]::WriteAllText($picker.FileName,$csv+"`r`n",(New-Object Text.UTF8Encoding($true)))
            Set-ScoliaAttendanceMessage $State ('Suivi mensuel exporté : '+$picker.FileName)
        }finally{$picker.Dispose()}
    }catch{Set-ScoliaAttendanceMessage $State $_.Exception.Message $true}
}
function Show-ScoliaPupilMonthlyAttendance {
    param([datetime]$Month=(Get-Date),[string]$Promotion='',[Windows.Forms.Form]$Owner,[string]$Matricule='')
    Assert-ITMPermission 'Lire'
    $context=Get-ScoliaFeatureContext;$academic=Get-ScoliaAttendanceYearBounds $context.Year
    $first=New-Object datetime($Month.Year,$Month.Month,1)
    if($first -lt $academic.Start){$first=$academic.Start}
    if($first -gt $academic.End){$first=New-Object datetime($academic.End.Year,$academic.End.Month,1)}
    $state=New-ScoliaFeatureDialog -Title 'Présences de fin de journée' -Subtitle ('Apprenants · une coche par jour, après l''appel · année '+$context.Year) -Width 1120 -Height 730
    foreach($entry in (@{Context=$context;Loading=$true;MonthRows=@();PresenceRevision=0;CalendarRevision=0;Calendar=$null}).GetEnumerator()){$state | Add-Member NoteProperty $entry.Key $entry.Value}
    $state.Form.MinimumSize=New-Object Drawing.Size(600,520);$state.Toolbar.Height=130;$state.Message.Height=110;$state.Grid.AccessibleName='Présences de fin de journée par apprenant dans le mois';$state.Grid.AllowUserToOrderColumns=$false;$state.Grid.ReadOnly=$false;$state.Grid.EditMode='EditOnEnter'
    $monthLabel=New-Label $state.Toolbar 'Mois' 0 0 45 30;$monthLabel.AutoSize=$true
    $monthPicker=New-Object Windows.Forms.DateTimePicker;$monthPicker.Format='Custom';$monthPicker.CustomFormat='MMMM yyyy';$monthPicker.ShowUpDown=$true;$monthPicker.Width=160;$monthPicker.MinDate=$academic.Start;$monthPicker.MaxDate=$academic.End;$monthPicker.Value=$first;$monthPicker.AccessibleName='Mois du suivi';$state.Toolbar.Controls.Add($monthPicker)
    $promo=New-Object Windows.Forms.ComboBox;$promo.DropDownStyle='DropDownList';$promo.Width=165;$promo.AccessibleName='Promotion du suivi mensuel'
    foreach($code in @($script:data.promotions | Where-Object {[string]$_.Annee -ceq $context.Year} | ForEach-Object {$_.Code} | Select-Object -Unique | Sort-Object)){[void]$promo.Items.Add([string]$code)}
    if($Promotion -and $Promotion -in @($promo.Items)){$promo.SelectedItem=$Promotion}elseif($promo.Items.Count -gt 0){$promo.SelectedIndex=0};$state.Toolbar.Controls.Add($promo)
    $dayLabel=New-Label $state.Toolbar 'Jour' 0 0 45 30;$dayLabel.AutoSize=$true
    $dayPicker=New-Object Windows.Forms.DateTimePicker;$dayPicker.Format='Short';$dayPicker.Width=126;$dayPicker.Value=$first;$dayPicker.AccessibleName='Jour affiché';$state.Toolbar.Controls.Add($dayPicker)
    $state | Add-Member NoteProperty MonthPicker $monthPicker;$state | Add-Member NoteProperty PromotionPicker $promo;$state | Add-Member NoteProperty DayPicker $dayPicker
    $calendarButton=New-Button $state.Toolbar 'Jours de cours' 0 0 145;$calendarButton.AutoSize=$true;$state | Add-Member NoteProperty CalendarButton $calendarButton
    $thresholdButton=New-Button $state.Toolbar 'Seuils d''alerte' 0 0 145;$thresholdButton.AutoSize=$true;$state | Add-Member NoteProperty ThresholdButton $thresholdButton
    $scopeButton=New-Button $state.Toolbar 'Début du suivi' 0 0 145;$scopeButton.AutoSize=$true;$state | Add-Member NoteProperty ScopeButton $scopeButton
    $refresh=New-Button $state.Toolbar 'Actualiser' 0 0 110;$refresh.AutoSize=$true
    $detail=New-Button $state.Toolbar 'Détail du mois' 0 0 140;$detail.AutoSize=$true
    $justify=New-Button $state.Toolbar 'Justifier l''absence' 0 0 170;$justify.AutoSize=$true;$state | Add-Member NoteProperty JustifyButton $justify
    $export=New-Button $state.Toolbar 'Exporter CSV' 0 0 125;$export.AutoSize=$true
    foreach($column in @(@('Matricule','Matricule',105),@('Nom','Apprenant',245))){$c=New-Object Windows.Forms.DataGridViewTextBoxColumn;$c.Name=$column[0];$c.HeaderText=$column[1];$c.Width=$column[2];$c.ReadOnly=$true;[void]$state.Grid.Columns.Add($c)}
    $sign=New-Object Windows.Forms.DataGridViewCheckBoxColumn;$sign.Name='Signature';$sign.HeaderText='Présence de fin de journée';$sign.Width=172;$sign.ReadOnly=$false;[void]$state.Grid.Columns.Add($sign)
    foreach($column in @(@('DayStatus','Situation du jour',145),@('Time','Saisie (audit)',115),@('Presents','Présents',80),@('Retards','Retards (historique)',120),@('Absents','Absents',75),@('Justifies','Justifiés',80),@('Alert','Alerte interne',250))){$c=New-Object Windows.Forms.DataGridViewTextBoxColumn;$c.Name=$column[0];$c.HeaderText=$column[1];$c.Width=$column[2];$c.ReadOnly=$true;[void]$state.Grid.Columns.Add($c)}
    $state.Grid.Columns['Time'].Visible=$false;$state.Grid.Columns['Time'].HeaderCell.ToolTipText='Horodatage de saisie conservé dans le détail du mois ; aucune incidence sur la présence.'
    $state.Grid.Columns['Retards'].Visible=$false;$state.Grid.Columns['Retards'].HeaderCell.ToolTipText='Anciens retards saisis manuellement ; aucun retard déduit de l''heure de signature.'
    $state.Grid.Columns['Signature'].HeaderCell.ToolTipText='À cocher après l''appel en fin de journée pour les apprenants présents. Une seule coche par jour, sans contrôle horaire.'
    $state.Grid.Add_CurrentCellDirtyStateChanged({param($sender,$e) if($sender.IsCurrentCellDirty){[void]$sender.CommitEdit([Windows.Forms.DataGridViewDataErrorContexts]::Commit)}})
    $state.Grid.Add_CellValueChanged({param($sender,$e) $s=$sender.FindForm().Tag;if($s -and -not $s.Loading -and $e.RowIndex -ge 0 -and $e.ColumnIndex -eq $sender.Columns['Signature'].Index){Save-ScoliaPupilCheckbox $s $e.RowIndex}})
    $state.Grid.Add_CellDoubleClick({param($sender,$e) if($e.RowIndex -ge 0 -and $e.ColumnIndex -ne $sender.Columns['Signature'].Index){Show-ScoliaPupilAttendanceDetail $sender.FindForm().Tag}})
    $state.Grid.Add_DataError({param($sender,$e) $e.ThrowException=$false;Set-ScoliaAttendanceMessage $sender.FindForm().Tag 'Valeur de présence invalide. Actualisez le suivi.' $true})
    $monthPicker.Add_ValueChanged({param($sender,$e) $s=$sender.FindForm().Tag;if(-not $s.Loading){[void](Update-ScoliaPupilMonthlyDialog $s)}})
    $promo.Add_SelectedIndexChanged({param($sender,$e) $s=$sender.FindForm().Tag;if(-not $s.Loading){[void](Update-ScoliaPupilMonthlyDialog $s)}})
    $dayPicker.Add_ValueChanged({param($sender,$e) $s=$sender.FindForm().Tag;if(-not $s.Loading){[void](Update-ScoliaPupilMonthlyDialog $s)}})
    $calendarButton.Add_Click({param($sender,$e) Save-ScoliaPupilCalendarDialog $sender.FindForm().Tag})
    $thresholdButton.Add_Click({param($sender,$e) Show-ScoliaPupilThresholds $sender.FindForm().Tag})
    $scopeButton.Add_Click({param($sender,$e) Show-ScoliaPupilPromotionScope $sender.FindForm().Tag})
    $refresh.Add_Click({param($sender,$e) [void](Update-ScoliaPupilMonthlyDialog $sender.FindForm().Tag)})
    $detail.Add_Click({param($sender,$e) Show-ScoliaPupilAttendanceDetail $sender.FindForm().Tag})
    $justify.Add_Click({param($sender,$e) Show-ScoliaPupilJustification $sender.FindForm().Tag})
    $export.Add_Click({param($sender,$e) Export-ScoliaPupilMonthlyAttendance $sender.FindForm().Tag})
    $state.Loading=$false
    [void](Update-ScoliaPupilMonthlyDialog $state)
    if((Get-Date).Date -ge $first -and (Get-Date).Date -le $first.AddMonths(1).AddDays(-1)){$state.DayPicker.Value=(Get-Date).Date}
    if($Matricule){foreach($row in $state.Grid.Rows){if([string]$row.Cells['Matricule'].Value -ceq $Matricule){$state.Grid.CurrentCell=$row.Cells['Nom'];break}}}
    if((Test-Path function:Mount-ScoliaWorkspaceForm) -and (Mount-ScoliaWorkspaceForm $state.Form)){return}
    try{if($Owner){[void]$state.Form.ShowDialog($Owner)}elseif($script:main -and -not $script:main.IsDisposed){[void]$state.Form.ShowDialog($script:main)}else{[void]$state.Form.ShowDialog()}}finally{$state.Form.Dispose()}
}
