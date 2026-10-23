# Appel quotidien et bilan : les absences ne sont jamais deduites d'une case vide.
function Get-ScoliaAttendanceKey([object]$value) {
    return ([string]$value).Trim().ToUpperInvariant()
}
function Get-ScoliaAttendanceRoster {
    param([object[]]$Learners,[string]$Promotion,[string]$Year)
    $duplicates=@{}
    foreach($learner in @($Learners)){
        $key=Get-ScoliaAttendanceKey $learner.Matricule
        if($key){if(-not $duplicates.ContainsKey($key)){$duplicates[$key]=0};$duplicates[$key]++}
    }
    $result=New-Object 'System.Collections.Generic.List[object]'
    foreach($learner in @($Learners)){
        $key=Get-ScoliaAttendanceKey $learner.Matricule
        if(-not $key -or $duplicates[$key] -ne 1){continue}
        if((Get-ScoliaAttendanceKey $learner.Promotion) -ne (Get-ScoliaAttendanceKey $Promotion)){continue}
        if([string]$learner.Annee -ne $Year){continue}
        if((Get-ScoliaAttendanceKey $learner.Statut) -ne 'ACTIF'){continue}
        if($learner.PSObject.Properties['Eligible'] -and [string]$learner.Eligible -notin @('True','1')){continue}
        $result.Add([pscustomobject]@{Matricule=$key;Nom=([string]$learner.Nom+' '+[string]$learner.PostNom+' '+[string]$learner.Prenom).Trim();Status='';Reason=''})
    }
    return @($result.ToArray() | Sort-Object Nom,Matricule)
}
function Get-ScoliaAttendanceYearBounds([string]$Year,[int]$StartMonth=0) {
    if($Year -notmatch '^(\d{4})-(\d{4})$'){throw 'Année scolaire invalide. Utilisez AAAA-AAAA.'}
    $first=[int]$Matches[1];$last=[int]$Matches[2]
    if($first -lt 1753 -or $first -gt 9998 -or $last -ne $first+1){throw 'L''année scolaire doit contenir deux années consécutives.'}
    $month=9
    if($StartMonth -ne 0){if($StartMonth -lt 1 -or $StartMonth -gt 12){throw 'Calendrier enregistré invalide.'};$month=$StartMonth}
    elseif($script:scolia -and $script:scolia.PSObject.Properties['StartMonth']){
        $month=[int]$script:scolia.StartMonth
        if($month -lt 1 -or $month -gt 12){throw 'Le mois de début de l''année scolaire est invalide.'}
    }
    $start=New-Object datetime($first,$month,1)
    return [pscustomobject]@{Start=$start.Date;End=$start.AddYears(1).AddDays(-1).Date;StartMonth=$month}
}
function New-ScoliaAttendanceSheet {
    param([datetime]$Date,[string]$Promotion,[string]$Year,[string]$ContextId,[object[]]$Entries,[int]$StartMonth=0)
    if($Date.Date -gt (Get-Date).Date){throw 'Les présences ne peuvent pas être saisies pour une date future.'}
    $bounds=Get-ScoliaAttendanceYearBounds $Year $StartMonth
    if($Date.Date -lt $bounds.Start -or $Date.Date -gt $bounds.End){throw ('La date doit appartenir à l''année scolaire : du '+$bounds.Start.ToString('dd/MM/yyyy')+' au '+$bounds.End.ToString('dd/MM/yyyy')+'.')}
    if(-not $Promotion.Trim() -or -not $Year.Trim() -or -not $ContextId.Trim()){throw 'La promotion, l''année et le contexte sont obligatoires.'}
    $valid=@('Présent','Absent','Retard','Justifié');$seen=@{};$rows=New-Object 'System.Collections.Generic.List[object]'
    foreach($entry in @($Entries)){
        $key=Get-ScoliaAttendanceKey $entry.Matricule
        if(-not $key -or $seen.ContainsKey($key)){throw 'La feuille contient un matricule vide ou un apprenant en double.'}
        $seen[$key]=$true
        if([string]$entry.Status -notin $valid){throw ('Choisissez un statut pour '+$key+'. Les cases vides ne signifient pas Présent.')}
        $reason=([string]$entry.Reason).Trim()
        if([string]$entry.Status -eq 'Justifié' -and -not $reason){throw ('Précisez le motif de justification pour '+$key+'.')}
        if($reason.Length -gt 500){throw 'Un motif ne peut pas dépasser 500 caractères.'}
        $row=[pscustomobject]@{Matricule=$key;Nom=[string]$entry.Nom;Status=[string]$entry.Status;Reason=$reason}
        if($entry.PSObject.Properties['SignedAt'] -and $entry.SignedAt){
            $instant=ConvertTo-ScoliaPersonnelInstant ([string]$entry.SignedAt)
            if($instant.Date -ne $Date.Date -or [string]$entry.Status -notin @('Présent','Retard') -or [string]::IsNullOrWhiteSpace([string]$entry.Actor)){throw 'Signature apprenant incohérente : date, statut ou utilisateur invalide.'}
            $row|Add-Member NoteProperty SignedAt ([string]$entry.SignedAt);$row|Add-Member NoteProperty Actor ([string]$entry.Actor)
        }
        $rows.Add($row)
    }
    if($rows.Count -eq 0){throw 'La feuille ne contient aucun apprenant admissible.'}
    $dateKey=$Date.Date.ToString('yyyy-MM-dd',[Globalization.CultureInfo]::InvariantCulture)
    $promoKey=Get-ScoliaAttendanceKey $Promotion
    return [pscustomobject]@{Id=($dateKey+'|'+$promoKey);Date=$dateKey;Promotion=$Promotion.Trim();Year=$Year;ContextId=$ContextId;StartMonth=$bounds.StartMonth;Entries=@($rows.ToArray());UpdatedAt=[datetime]::UtcNow.ToString('o')}
}
function Assert-ScoliaAttendanceDocument([object]$Document) {
    $seen=@{}
    foreach($sheet in @($Document.Items)){
        if([string]$sheet.ContextId -cne [string]$Document.Context.Id -or [string]$sheet.Year -cne [string]$Document.Context.Year){throw 'Une feuille enregistrée appartient à une autre base ou année.'}
        $date=[datetime]::ParseExact([string]$sheet.Date,'yyyy-MM-dd',[Globalization.CultureInfo]::InvariantCulture)
        $valid=New-ScoliaAttendanceSheet -Date $date -Promotion $sheet.Promotion -Year $sheet.Year -ContextId $sheet.ContextId -Entries @($sheet.Entries) -StartMonth $(if($sheet.PSObject.Properties['StartMonth']){[int]$sheet.StartMonth}else{0})
        if([string]$valid.Id -cne [string]$sheet.Id -or $seen.ContainsKey([string]$valid.Id)){throw 'Identifiant de feuille invalide ou feuille dupliquée. Le fichier existant est conservé.'}
        $seen[[string]$valid.Id]=$true
    }
}
function Set-ScoliaAttendanceSheet {
    param([object]$Document,[object]$Sheet,[int]$ExpectedRevision)
    if(-not $Document.Context -or [string]$Document.Context.Id -cne [string]$Sheet.ContextId -or [string]$Document.Context.Year -cne [string]$Sheet.Year){throw 'Cette feuille appartient à une autre base ou année scolaire.'}
    if([int]$Document.Revision -ne $ExpectedRevision){throw 'Les présences ont changé dans une autre session. Rechargez la feuille avant de l''enregistrer.'}
    $date=[datetime]::ParseExact([string]$Sheet.Date,'yyyy-MM-dd',[Globalization.CultureInfo]::InvariantCulture)
    $validated=New-ScoliaAttendanceSheet -Date $date -Promotion $Sheet.Promotion -Year $Sheet.Year -ContextId $Sheet.ContextId -Entries @($Sheet.Entries) -StartMonth $(if($Sheet.PSObject.Properties['StartMonth']){[int]$Sheet.StartMonth}else{0})
    Assert-ScoliaAttendanceDocument $Document
    $items=@($Document.Items)
    if(@($items | Where-Object {[string]$_.Id -eq $validated.Id}).Count -gt 1){throw 'Plusieurs feuilles portent cette date et cette promotion. Corrigez le stockage avant de continuer.'}
    foreach($oldEntry in @($items|Where-Object {$_.Id -ceq $validated.Id}|ForEach-Object {$_.Entries}|Where-Object {$_.PSObject.Properties['SignedAt'] -and $_.SignedAt})){
        $next=@($validated.Entries|Where-Object {$_.Matricule -ceq $oldEntry.Matricule})
        if($next.Count -ne 1 -or $next[0].SignedAt -cne $oldEntry.SignedAt -or $next[0].Actor -cne $oldEntry.Actor -or $next[0].Status -cne $oldEntry.Status){throw 'Une présence déjà signée est conservée. Actualisez la feuille sans remplacer son horodatage.'}
    }
    $Document.Items=@($items | Where-Object {[string]$_.Id -ne $validated.Id})+@($validated)
}
function Get-ScoliaAttendanceSummary {
    param([object[]]$Sheets,[datetime]$Start,[datetime]$End,[string]$Promotion='')
    if($Start.Date -gt $End.Date){throw 'La date de début doit précéder la date de fin.'}
    $rows=@{};$keys=@{}
    foreach($sheet in @($Sheets)){
        $date=[datetime]::ParseExact([string]$sheet.Date,'yyyy-MM-dd',[Globalization.CultureInfo]::InvariantCulture)
        if($date.Date -lt $Start.Date -or $date.Date -gt $End.Date){continue}
        if($Promotion -and (Get-ScoliaAttendanceKey $sheet.Promotion) -ne (Get-ScoliaAttendanceKey $Promotion)){continue}
        $sheetId=[string]$sheet.Id
        if($keys.ContainsKey($sheetId)){throw 'Le bilan contient une feuille dupliquée.'};$keys[$sheetId]=$true
        $seen=@{}
        foreach($entry in @($sheet.Entries)){
            $key=Get-ScoliaAttendanceKey $entry.Matricule
            if(-not $key -or $seen.ContainsKey($key)){throw 'Le bilan contient un apprenant dupliqué dans une feuille.'};$seen[$key]=$true
            $group=$key+'|'+(Get-ScoliaAttendanceKey $sheet.Promotion)
            if(-not $rows.ContainsKey($group)){$rows[$group]=[pscustomobject]@{Matricule=$key;Nom=[string]$entry.Nom;Promotion=[string]$sheet.Promotion;Presents=0;Absents=0;Retards=0;Justifies=0;Appels=0}}
            $row=$rows[$group]
            switch([string]$entry.Status){
                'Présent'{$row.Presents++}
                'Absent'{$row.Absents++}
                'Retard'{$row.Retards++}
                'Justifié'{$row.Justifies++}
                default{throw 'Le bilan contient un statut invalide.'}
            }
            $row.Appels++
        }
    }
    return @($rows.Values | Sort-Object Promotion,Nom,Matricule)
}
function Assert-ScoliaAttendanceContext([object]$state,[bool]$Write=$false) {
    Assert-ITMPermission 'Lire'
    $current=Get-ScoliaFeatureContext
    if([string]$current.Id -cne [string]$state.Context.Id -or [string]$current.Year -cne [string]$state.Context.Year -or [string]$current.LocalKey -cne [string]$state.Context.LocalKey){throw 'Le dossier ou l''année a changé. Fermez cette fenêtre et rouvrez les présences.'}
    if($Write){Assert-ITMPermission 'Saisie';if($script:data.closed){throw 'Cette année est clôturée : consultation et export uniquement.'}}
}
function Set-ScoliaAttendanceMessage([object]$state,[string]$Text,[bool]$Error=$false) {
    $state.Message.Text=$Text
    $state.Message.Tag=$(if($Error){'scolia:feedback:error'}else{'scolia:feedback:success'})
    if(Get-Command Apply-ITMThemeForm -ErrorAction SilentlyContinue){Apply-ITMThemeForm $state.Form}
}
function Get-ScoliaAttendanceGridEntries([object]$state) {
    [void]$state.Grid.EndEdit()
    return @($state.Grid.Rows | ForEach-Object {
        $entry=[pscustomobject]@{Matricule=[string]$_.Cells['Matricule'].Value;Nom=[string]$_.Cells['Nom'].Value;Status=[string]$_.Cells['Status'].Value;Reason=[string]$_.Cells['Reason'].Value}
        if($_.Tag -and $_.Tag.PSObject.Properties['SignedAt'] -and $_.Tag.SignedAt){$entry|Add-Member NoteProperty SignedAt ([string]$_.Tag.SignedAt);$entry|Add-Member NoteProperty Actor ([string]$_.Tag.Actor)}
        $entry
    })
}
function Save-ScoliaAttendanceDialog([object]$state) {
    try{
        Assert-ScoliaAttendanceContext $state $true
        $sheet=New-ScoliaAttendanceSheet -Date $state.LoadedDate -Promotion $state.LoadedPromotion -Year $state.Context.Year -ContextId $state.Context.Id -Entries (Get-ScoliaAttendanceGridEntries $state)
        $state.PendingSheet=$sheet
        $state.PendingRevision=[int]$state.Revision
        $updated=Invoke-ScoliaFeatureUpdate -Name 'presences' -Change {param($document) Set-ScoliaAttendanceSheet -Document $document -Sheet $state.PendingSheet -ExpectedRevision $state.PendingRevision}
        $state.Revision=[int]$updated.Revision;$state.Dirty=$false
        Set-ScoliaAttendanceMessage $state ('Appel enregistré pour le '+$state.LoadedDate.ToString('dd/MM/yyyy')+' : '+$sheet.Entries.Count+' apprenant(s).')
        return $true
    }catch{Set-ScoliaAttendanceMessage $state $_.Exception.Message $true;return $false}
}
function Confirm-ScoliaAttendanceLeave([object]$state) {
    [void]$state.Grid.EndEdit()
    if(-not $state.Dirty){return $true}
    $choice=[ITMMessageBox]::Show($state.Form,'La feuille a été modifiée. Oui : enregistrer ; Non : abandonner les modifications ; Annuler : continuer la saisie.','Présences non enregistrées','YesNoCancel','Warning')
    if($choice -eq 'Cancel'){return $false}
    if($choice -eq 'Yes'){return (Save-ScoliaAttendanceDialog $state)}
    return $true
}
function Load-ScoliaAttendanceDialog([object]$state) {
    Assert-ScoliaAttendanceContext $state
    if($state.DatePicker.Value.Date -gt (Get-Date).Date){throw 'Choisissez une date passée ou la date du jour.'}
    $promotion=[string]$state.PromotionPicker.Text
    if(-not $promotion){throw 'Choisissez une promotion.'}
    $store=Read-ScoliaFeatureStore -Name 'presences'
    Assert-ScoliaAttendanceDocument $store
    $key=$state.DatePicker.Value.Date.ToString('yyyy-MM-dd',[Globalization.CultureInfo]::InvariantCulture)+'|'+(Get-ScoliaAttendanceKey $promotion)
    $sheets=@($store.Items | Where-Object {[string]$_.Id -eq $key})
    if($sheets.Count -gt 1){throw 'Plusieurs feuilles portent cette date et cette promotion.'}
    $entries=@(if($sheets.Count -eq 1){@($sheets[0].Entries)}else{Get-ScoliaAttendanceRoster -Learners @($script:data.learners) -Promotion $promotion -Year $state.Context.Year})
    $state.Loading=$true
    try{
        $state.Grid.Rows.Clear()
        foreach($entry in @($entries)){$index=$state.Grid.Rows.Add([string]$entry.Matricule,[string]$entry.Nom,[string]$entry.Status,[string]$entry.Reason);$state.Grid.Rows[$index].Tag=$entry;if($entry.PSObject.Properties['SignedAt'] -and $entry.SignedAt){$state.Grid.Rows[$index].Cells['Status'].ReadOnly=$true}}
        $state.Revision=[int]$store.Revision;$state.Dirty=$false;$state.LoadedDate=$state.DatePicker.Value.Date;$state.LoadedPromotion=$promotion
    }finally{$state.Loading=$false}
    $canWrite=(-not $script:data.closed) -and (Test-ITMPermission 'Saisie')
    $state.Grid.ReadOnly=-not $canWrite;$state.SaveButton.Enabled=$canWrite -and $entries.Count -gt 0;$state.AllPresentButton.Enabled=$state.SaveButton.Enabled
    $prefix=$(if($script:data.closed){'Année clôturée : lecture uniquement. '}else{''})
    $message=$(if($sheets.Count -eq 1){'Feuille enregistrée chargée ; la liste conserve les apprenants de cet appel.'}else{'Nouvel appel : choisissez les statuts ou Tous présents. Doublons et dossiers non actifs exclus.'})
    Set-ScoliaAttendanceMessage $state ($prefix+$entries.Count+' apprenant(s). '+$message)
}
function Show-ScoliaAttendanceSummary {
    param([Windows.Forms.Form]$Owner)
    Assert-ITMPermission 'Lire'
    $context=Get-ScoliaFeatureContext
    $state=New-ScoliaFeatureDialog -Title 'Bilan des présences' -Subtitle ('Année '+$context.Year+' · seuls les appels enregistrés sont comptabilisés')
    $state | Add-Member NoteProperty Context $context
    $state.Message.Height=68;$state.Grid.AccessibleName='Bilan des appels enregistrés'
    $state | Add-Member NoteProperty SummaryRows @()
    $state | Add-Member NoteProperty Loading $false
    $fromLabel=New-Label $state.Toolbar 'Du' 0 0 25 30;$fromLabel.AutoSize=$true
    $begin=New-Object Windows.Forms.DateTimePicker;$begin.Format='Short';$begin.Width=125;$begin.AccessibleName='Début de la période';$begin.Value=(Get-Date).Date.AddDays(-30);$state.Toolbar.Controls.Add($begin)
    $toLabel=New-Label $state.Toolbar 'au' 0 0 25 30;$toLabel.AutoSize=$true
    $end=New-Object Windows.Forms.DateTimePicker;$end.Format='Short';$end.Width=125;$end.AccessibleName='Fin de la période';$end.Value=(Get-Date).Date;$state.Toolbar.Controls.Add($end)
    $promo=New-Object Windows.Forms.ComboBox;$promo.DropDownStyle='DropDownList';$promo.Width=180;[void]$promo.Items.Add('Toutes les promotions')
    foreach($code in @($script:data.promotions | ForEach-Object {$_.Code} | Select-Object -Unique | Sort-Object)){[void]$promo.Items.Add([string]$code)};$promo.SelectedIndex=0;$state.Toolbar.Controls.Add($promo)
    $state | Add-Member NoteProperty StartPicker $begin;$state | Add-Member NoteProperty EndPicker $end;$state | Add-Member NoteProperty PromotionPicker $promo
    foreach($column in @(@('Matricule','Matricule',110),@('Nom','Apprenant',240),@('Promotion','Promotion',100),@('Presents','Présents',80),@('Absents','Absents',80),@('Retards','Retards',80),@('Justifies','Justifiés',80),@('Appels','Appels',80))){
        $c=New-Object Windows.Forms.DataGridViewTextBoxColumn;$c.Name=$column[0];$c.HeaderText=$column[1];$c.Width=$column[2];[void]$state.Grid.Columns.Add($c)
    }
    $refresh=New-Button $state.Toolbar 'Actualiser le bilan' 0 0 165 $true;$refresh.AutoSize=$true
    $export=New-Button $state.Toolbar 'Exporter CSV' 0 0 130;$export.AutoSize=$true
    $refresh.Add_Click({param($sender,$e) Update-ScoliaAttendanceSummaryDialog $sender.FindForm().Tag})
    $export.Add_Click({param($sender,$e)
        $s=$sender.FindForm().Tag
        try{
            Assert-ScoliaAttendanceContext $s;Assert-ITMPermission 'Exporter'
            if(-not (Update-ScoliaAttendanceSummaryDialog $s)){return}
            if(@($s.SummaryRows).Count -eq 0){throw 'Aucun appel enregistré sur cette période.'}
            $picker=New-Object Windows.Forms.SaveFileDialog
            try{
                $picker.Filter='Fichier CSV (*.csv)|*.csv';$picker.FileName='Presences_'+$s.Context.Year+'_'+$s.StartPicker.Value.ToString('yyyyMMdd')+'_'+$s.EndPicker.Value.ToString('yyyyMMdd')+'.csv'
                if($picker.ShowDialog($s.Form) -eq 'OK'){
                    $csv=@($s.SummaryRows | Select-Object Matricule,Nom,Promotion,Presents,Absents,Retards,Justifies,Appels | ConvertTo-Csv -NoTypeInformation -Delimiter ';') -join "`r`n"
                    [IO.File]::WriteAllText($picker.FileName,$csv+"`r`n",(New-Object Text.UTF8Encoding($true)))
                    Set-ScoliaAttendanceMessage $s ('Bilan exporté : '+$picker.FileName)
                }
            }finally{$picker.Dispose()}
        }catch{Set-ScoliaAttendanceMessage $s $_.Exception.Message $true}
    })
    [void](Update-ScoliaAttendanceSummaryDialog $state)
    try{if($Owner){[void]$state.Form.ShowDialog($Owner)}else{[void]$state.Form.ShowDialog()}}finally{$state.Form.Dispose()}
}
function Update-ScoliaAttendanceSummaryDialog([object]$state) {
    try{
        Assert-ScoliaAttendanceContext $state
        $document=Read-ScoliaFeatureStore -Name 'presences'
        Assert-ScoliaAttendanceDocument $document
        $promotion=$(if($state.PromotionPicker.SelectedIndex -gt 0){[string]$state.PromotionPicker.Text}else{''})
        $rows=@(Get-ScoliaAttendanceSummary -Sheets @($document.Items) -Start $state.StartPicker.Value.Date -End $state.EndPicker.Value.Date -Promotion $promotion)
        $state.Grid.Rows.Clear();foreach($row in $rows){[void]$state.Grid.Rows.Add($row.Matricule,$row.Nom,$row.Promotion,$row.Presents,$row.Absents,$row.Retards,$row.Justifies,$row.Appels)}
        $state.SummaryRows=$rows
        $calls=($rows | Measure-Object -Property Appels -Sum).Sum
        Set-ScoliaAttendanceMessage $state ([string]::Format('{0} apprenant(s), {1} statut(s) enregistrés. Les journées sans appel ne sont pas des absences.',$rows.Count,[int]$calls))
        return $true
    }catch{$state.SummaryRows=@();Set-ScoliaAttendanceMessage $state $_.Exception.Message $true;return $false}
}
function Show-ScoliaAttendance {
    Assert-ITMPermission 'Lire'
    if(-not $script:data){throw 'Connectez un dossier scolaire avant de faire l''appel.'}
    $context=Get-ScoliaFeatureContext
    $bounds=Get-ScoliaAttendanceYearBounds $context.Year
    if((Get-Date).Date -lt $bounds.Start){throw 'Cette année scolaire n''a pas encore commencé. Aucun appel futur ne peut être saisi.'}
    $latest=$(if((Get-Date).Date -lt $bounds.End){(Get-Date).Date}else{$bounds.End})
    $state=New-ScoliaFeatureDialog -Title 'Présences et absences' -Subtitle ('Appel quotidien · année '+$context.Year+' · aucun statut par défaut')
    foreach($entry in (@{Context=$context;Loading=$true;Dirty=$false;Revision=0;LoadedDate=(Get-Date).Date;LoadedPromotion='';PendingSheet=$null;PendingRevision=0}).GetEnumerator()){$state | Add-Member NoteProperty $entry.Key $entry.Value}
    $date=New-Object Windows.Forms.DateTimePicker;$date.Format='Short';$date.Width=130;$date.MaxDate=$latest;$date.MinDate=$bounds.Start;$date.Value=$latest;$date.AccessibleName='Date de l appel';$state.Toolbar.Controls.Add($date)
    $promo=New-Object Windows.Forms.ComboBox;$promo.DropDownStyle='DropDownList';$promo.Width=170;$promo.AccessibleName='Promotion de l appel'
    foreach($code in @($script:data.promotions | ForEach-Object {$_.Code} | Select-Object -Unique | Sort-Object)){[void]$promo.Items.Add([string]$code)}
    if($promo.Items.Count -gt 0){$promo.SelectedIndex=0};$state.Toolbar.Controls.Add($promo)
    $state | Add-Member NoteProperty DatePicker $date;$state | Add-Member NoteProperty PromotionPicker $promo
    $all=New-Button $state.Toolbar 'Tous présents' 0 0 135;$all.AutoSize=$true;$state | Add-Member NoteProperty AllPresentButton $all
    $save=New-Button $state.Toolbar 'Enregistrer l''appel' 0 0 170 $true;$save.AutoSize=$true;$state | Add-Member NoteProperty SaveButton $save
    $reload=New-Button $state.Toolbar 'Recharger' 0 0 105;$reload.AutoSize=$true
    $summary=New-Button $state.Toolbar 'Bilan / export' 0 0 125;$summary.AutoSize=$true
    foreach($column in @(@('Matricule','Matricule',115),@('Nom','Apprenant',270))){$c=New-Object Windows.Forms.DataGridViewTextBoxColumn;$c.Name=$column[0];$c.HeaderText=$column[1];$c.Width=$column[2];$c.ReadOnly=$true;[void]$state.Grid.Columns.Add($c)}
    $status=New-Object Windows.Forms.DataGridViewComboBoxColumn;$status.Name='Status';$status.HeaderText='Statut *';$status.Width=140;[void]$status.Items.Add('');[void]$status.Items.AddRange([object[]]@('Présent','Absent','Retard','Justifié'));[void]$state.Grid.Columns.Add($status)
    $reason=New-Object Windows.Forms.DataGridViewTextBoxColumn;$reason.Name='Reason';$reason.HeaderText='Motif (obligatoire si justifié)';$reason.AutoSizeMode='Fill';$reason.MinimumWidth=180;[void]$state.Grid.Columns.Add($reason)
    $state.Message.Height=68;$state.Grid.AccessibleName='Feuille d appel';$state.Grid.EditMode='EditOnEnter';$state.Grid.SelectionMode='CellSelect';$state.Grid.AllowUserToOrderColumns=$false
    $state.Grid.Add_DataError({param($sender,$e) $e.ThrowException=$false;Set-ScoliaAttendanceMessage $sender.FindForm().Tag 'Statut invalide dans la feuille. Choisissez une valeur de la liste.' $true})
    $state.Grid.Add_CurrentCellDirtyStateChanged({param($sender,$e) if($sender.IsCurrentCellDirty){[void]$sender.CommitEdit([Windows.Forms.DataGridViewDataErrorContexts]::Commit)}})
    $state.Grid.Add_CellValueChanged({param($sender,$e) $s=$sender.FindForm().Tag;if($s -and -not $s.Loading -and $e.RowIndex -ge 0){$s.Dirty=$true;Set-ScoliaAttendanceMessage $s 'Modifications non enregistrées. Enregistrez cet appel avant de changer de feuille.'}})
    $date.Add_ValueChanged({param($sender,$e) Switch-ScoliaAttendanceDialog $sender.FindForm().Tag})
    $promo.Add_SelectedIndexChanged({param($sender,$e) Switch-ScoliaAttendanceDialog $sender.FindForm().Tag})
    $all.Add_Click({param($sender,$e) $s=$sender.FindForm().Tag;try{Assert-ScoliaAttendanceContext $s $true;foreach($row in $s.Grid.Rows){if($row.Tag -and $row.Tag.PSObject.Properties['SignedAt'] -and $row.Tag.SignedAt){continue};$row.Cells['Status'].Value='Présent';$row.Cells['Reason'].Value=''};$s.Dirty=$true;Set-ScoliaAttendanceMessage $s 'Tous les apprenants sont marqués présents. Corrigez les exceptions puis enregistrez.'}catch{Set-ScoliaAttendanceMessage $s $_.Exception.Message $true}})
    $save.Add_Click({param($sender,$e) [void](Save-ScoliaAttendanceDialog $sender.FindForm().Tag)})
    $reload.Add_Click({param($sender,$e) $s=$sender.FindForm().Tag;if(Confirm-ScoliaAttendanceLeave $s){try{Load-ScoliaAttendanceDialog $s}catch{Set-ScoliaAttendanceMessage $s $_.Exception.Message $true}}})
    $summary.Add_Click({param($sender,$e) $s=$sender.FindForm().Tag;try{Show-ScoliaAttendanceSummary -Owner $s.Form}catch{Set-ScoliaAttendanceMessage $s $_.Exception.Message $true}})
    $state.Form.Add_FormClosing({param($sender,$e) if(-not (Confirm-ScoliaAttendanceLeave $sender.Tag)){$e.Cancel=$true}})
    $state.Loading=$false
    try{Load-ScoliaAttendanceDialog $state}catch{$save.Enabled=$false;$all.Enabled=$false;Set-ScoliaAttendanceMessage $state $_.Exception.Message $true}
    try{if($script:main -and -not $script:main.IsDisposed){[void]$state.Form.ShowDialog($script:main)}else{[void]$state.Form.ShowDialog()}}finally{$state.Form.Dispose()}
}
function Switch-ScoliaAttendanceDialog([object]$state) {
    if($state.Loading){return}
    if(Confirm-ScoliaAttendanceLeave $state){try{Load-ScoliaAttendanceDialog $state;return}catch{Set-ScoliaAttendanceMessage $state $_.Exception.Message $true}}
    $state.Loading=$true
    try{$state.DatePicker.Value=$state.LoadedDate;$state.PromotionPicker.Text=$state.LoadedPromotion}finally{$state.Loading=$false}
}
