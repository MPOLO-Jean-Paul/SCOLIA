# Réinscription dans l'année connectée. Chaque dossier reste une transaction du moteur SCOLIA.
function Get-ScoliaReenrollmentRecord($learner) {
    $record=[ordered]@{}
    foreach($name in @('Matricule','Nom','PostNom','Prenom','Sexe','Naissance','Lieu','Tuteur','Contact1','Contact2','Promotion','Annee','Statut','Adresse','Inscription')){
        $value=([string]$learner.$name).Trim()
        if($name -in @('Matricule','Nom','PostNom','Prenom','Promotion')){$value=$value.ToUpperInvariant()}
        $record[$name]=$value
    }
    if($Learner.PSObject.Properties['DisciplineBaseStatus']){$record['Statut']=[string]$Learner.DisciplineBaseStatus}
    return [pscustomobject]$record
}
function Get-ScoliaReenrollmentFingerprint($learner) {
    return (Get-ScoliaReenrollmentRecord $learner | ConvertTo-Json -Depth 4 -Compress)
}
function Get-ScoliaReenrollmentExpected($entry) {
    if([string]$entry.Decision -notin @('Passage','Maintien','Redoublement','Sortie')){throw 'Décision de réinscription inconnue.'}
    $expected=Get-ScoliaReenrollmentRecord $entry.Before
    $expected.Promotion=([string]$entry.TargetPromotion).Trim().ToUpperInvariant()
    $expected.Annee=[string]$entry.TargetYear
    $expected.Statut=[string]$entry.TargetStatus
    if($entry.Decision -ne 'Passage' -and $expected.Promotion -cne ([string]$entry.Before.Promotion).ToUpperInvariant()){throw 'Seul un passage permet de changer de promotion.'}
    if($entry.Decision -eq 'Passage' -and $expected.Promotion -ceq ([string]$entry.Before.Promotion).ToUpperInvariant()){throw 'Choisissez une nouvelle promotion pour un passage, ou un maintien.'}
    $status=switch([string]$entry.Decision){'Sortie'{'Sorti'};'Maintien'{[string]$entry.Before.Statut};default{'Actif'}}
    if($expected.Statut -cne $status){throw 'Le statut du plan ne correspond pas à la décision.'}
    if(-not $expected.Matricule -or -not $expected.Nom -or -not $expected.PostNom -or -not $expected.Prenom){throw 'Dossier de réinscription incomplet.'}
    return $expected
}
function Assert-ScoliaReenrollmentContext($plan,$snapshot) {
    Assert-ITMPermission 'Administrer';Assert-ITMPermission 'Saisie'
    $context=Get-ScoliaFeatureContext
    if(-not $context.Id -or [string]$context.Year -notmatch '^\d{4}-\d{4}$'){throw 'Connectez les données scolaires avec une seule année scolaire identifiable.'}
    if($plan -and ([string]$plan.ContextId -cne [string]$context.Id -or [string]$plan.Year -cne [string]$context.Year)){throw 'Ce plan appartient à un autre dossier de données ou à une autre année. Aucun dossier modifié.'}
    if(-not $snapshot -or -not $snapshot.PSObject.Properties['learners'] -or -not $snapshot.PSObject.Properties['payments'] -or -not $snapshot.PSObject.Properties['promotions']){throw 'Contrôle des données scolaires impossible.'}
    if($snapshot.closed -or ($script:store -and $script:store.Closed)){throw 'Cette année est clôturée. Connectez le nouveau classeur préparé avant les réinscriptions.'}
    return $context
}
function Assert-ScoliaReenrollmentEntry($entry,$snapshot,[string]$year,[bool]$allowApplied=$false) {
    if([string]$entry.TargetYear -cne $year -or [string]$entry.Before.Annee -cne $year){throw ('Le dossier '+$entry.Matricule+' ne correspond pas à l''année connectée. Utilisez d''abord la préparation de la nouvelle année.')}
    $rows=@($snapshot.learners | Where-Object {(Key $_.Matricule) -eq (Key $entry.Matricule)})
    if($rows.Count -ne 1){throw ('Dossier absent ou matricule en double : '+$entry.Matricule+'.')}
    if((Key $entry.Before.Matricule) -ne (Key $entry.Matricule)){throw 'Matricule incohérent dans le plan.'}
    $expected=Get-ScoliaReenrollmentExpected $entry
    $current=$rows[0]
    $actual=Get-ScoliaReenrollmentFingerprint $current
    if($allowApplied -and $actual -ceq (Get-ScoliaReenrollmentFingerprint $expected)){return [pscustomobject]@{Current=$current;Expected=$expected;AlreadyApplied=$true}}
    if($actual -cne (Get-ScoliaReenrollmentFingerprint $entry.Before)){throw ('Le dossier '+$entry.Matricule+' a changé depuis l''aperçu. Préparez un nouvel aperçu ; aucune modification de ce dossier.')}
    $promos=@($snapshot.promotions | Where-Object {(Promo $_.Code) -eq (Promo $expected.Promotion) -and [string]$_.Annee -ceq $year -and $_.Active})
    if($promos.Count -ne 1 -or [decimal]$promos[0].Mensuel -le 0 -or [decimal]$promos[0].Annuel -lt [decimal]$promos[0].Mensuel){throw ('Promotion active ou frais incompatibles : '+$expected.Promotion+'. Vérifiez Administration > Promotions.')}
    $changesClass=((Promo $expected.Promotion) -ne (Promo $current.Promotion) -or [string]$expected.Annee -cne [string]$current.Annee)
    if($changesClass){
        if(@($snapshot.payments | Where-Object {(Key $_.Matricule) -eq (Key $entry.Matricule)}).Count -gt 0 -or @($snapshot.balances | Where-Object {(Key $_.Matricule) -eq (Key $entry.Matricule)}).Count -gt 0){throw ('Des paiements existent pour '+$entry.Matricule+' : promotion et année conservées. Réinscrivez avant de saisir les paiements de la nouvelle année.')}
        if((Get-Command Get-ITMPECRecord -ErrorAction SilentlyContinue) -and (Get-ITMPECRecord $current)){throw ('Une prise en charge existe pour '+$entry.Matricule+' : promotion et année conservées.')}
    }
    Assert-ITMLearnerIdentity $snapshot.learners ([string]$expected.Matricule) ([string]$expected.Nom) ([string]$expected.PostNom) ([string]$expected.Prenom) ([string]$expected.Promotion)
    return [pscustomobject]@{Current=$current;Expected=$expected;AlreadyApplied=$false}
}
function New-ScoliaReenrollmentPlan($decisions,$snapshot) {
    $context=Assert-ScoliaReenrollmentContext $null $snapshot
    $entries=New-Object 'System.Collections.Generic.List[object]';$seen=@{}
    foreach($decision in @($decisions)){
        if([string]$decision.Decision -eq 'À décider' -or [string]::IsNullOrWhiteSpace([string]$decision.Decision)){continue}
        $mat=Key ([string]$decision.Matricule)
        if(-not $mat -or $seen.ContainsKey($mat)){throw 'Matricule vide ou répété dans les décisions.'};$seen[$mat]=$true
        $learners=@($snapshot.learners | Where-Object {(Key $_.Matricule) -eq $mat})
        if($learners.Count -ne 1){throw ('Dossier absent ou matricule en double : '+$mat+'.')}
        $before=Get-ScoliaReenrollmentRecord $learners[0]
        $target=$(if($decision.Decision -eq 'Passage'){[string]$decision.TargetPromotion}else{$before.Promotion})
        $targetStatus=switch([string]$decision.Decision){'Sortie'{'Sorti'};'Maintien'{$before.Statut};default{'Actif'}}
        $entry=[pscustomobject]@{Matricule=$before.Matricule;Before=$before;Decision=[string]$decision.Decision;TargetPromotion=$target.Trim().ToUpperInvariant();TargetYear=[string]$context.Year;TargetStatus=$targetStatus;State='Préparé';Error='';StartedUtc='';AppliedUtc=''}
        [void](Assert-ScoliaReenrollmentEntry $entry $snapshot ([string]$context.Year))
        $entries.Add($entry)
    }
    if($entries.Count -eq 0){throw 'Choisissez au moins une décision. Les lignes « À décider » restent inchangées.'}
    # Vérifie également les collisions entre les cibles du même lot.
    $projected=@{};foreach($learner in @($snapshot.learners)){$projected[(Key $learner.Matricule)]=Get-ScoliaReenrollmentRecord $learner}
    foreach($entry in $entries){$projected[(Key $entry.Matricule)]=Get-ScoliaReenrollmentExpected $entry}
    foreach($entry in $entries){$expected=$projected[(Key $entry.Matricule)];Assert-ITMLearnerIdentity @($projected.Values) $expected.Matricule $expected.Nom $expected.PostNom $expected.Prenom $expected.Promotion}
    return [pscustomobject]@{Id=[guid]::NewGuid().ToString('N');ContextId=[string]$context.Id;Year=[string]$context.Year;CreatedUtc=[DateTimeOffset]::UtcNow.ToString('o');UpdatedUtc=[DateTimeOffset]::UtcNow.ToString('o');State='Préparé';Entries=$entries.ToArray()}
}
function Save-ScoliaReenrollmentPlan($plan) {
    $snapshot=Invoke-ITMJsonDirect 'ITMAppSnapshot'
    [void](Assert-ScoliaReenrollmentContext $plan $snapshot)
    foreach($entry in @($plan.Entries)){[void](Assert-ScoliaReenrollmentEntry $entry $snapshot ([string]$plan.Year))}
    Invoke-ScoliaFeatureUpdate -Name 'reinscriptions' -Change {param($doc)
        if(@($doc.Items | Where-Object {[string]$_.Id -ceq [string]$plan.Id}).Count){throw 'Ce plan est déjà enregistré. Préparez un nouveau plan pour modifier les choix.'}
        $doc.Items=@($doc.Items)+@($plan)
    } | Out-Null
}
function Set-ScoliaReenrollmentEntryState([string]$planId,[string]$mat,[string]$state,[string]$errorText='') {
    Invoke-ScoliaFeatureUpdate -Name 'reinscriptions' -Change {param($doc)
        $plans=@($doc.Items | Where-Object {[string]$_.Id -ceq $planId});if($plans.Count -ne 1){throw 'Plan de réinscription introuvable.'}
        $entries=@($plans[0].Entries | Where-Object {(Key $_.Matricule) -eq (Key $mat)});if($entries.Count -ne 1){throw 'Décision de réinscription introuvable.'}
        $entry=$entries[0];$entry.State=$state;$entry.Error=$errorText
        if($state -eq 'Écriture en cours'){$entry.StartedUtc=[DateTimeOffset]::UtcNow.ToString('o')}
        if($state -eq 'Appliqué'){$entry.AppliedUtc=[DateTimeOffset]::UtcNow.ToString('o')}
        $plans[0].UpdatedUtc=[DateTimeOffset]::UtcNow.ToString('o')
        $remaining=@($plans[0].Entries | Where-Object {$_.State -ne 'Appliqué'}).Count
        $plans[0].State=$(if($remaining -eq 0){'Appliqué'}elseif(@($plans[0].Entries | Where-Object {$_.State -eq 'Appliqué'}).Count){'Partiel'}else{'Préparé'})
    } | Out-Null
}
function Invoke-ScoliaReenrollment([string]$planId) {
    $document=Read-ScoliaFeatureStore -Name 'reinscriptions'
    $plans=@($document.Items | Where-Object {[string]$_.Id -ceq $planId});if($plans.Count -ne 1){throw 'Enregistrez un aperçu avant de l''appliquer.'}
    $plan=$plans[0];$snapshot=Invoke-ITMJsonDirect 'ITMAppSnapshot'
    [void](Assert-ScoliaReenrollmentContext $plan $snapshot)
    if(@($plan.Entries).Count -eq 0){throw 'Le plan ne contient aucune décision.'}
    # Contrôle complet AVANT la première écriture. Une reprise reconnaît le résultat relu,
    # même si le journal local n'a pas été finalisé après la validation Excel.
    $seen=@{}
    foreach($entry in @($plan.Entries)){
        $key=Key $entry.Matricule;if($seen.ContainsKey($key)){throw 'Matricule répété dans le plan.'};$seen[$key]=$true
        $check=Assert-ScoliaReenrollmentEntry $entry $snapshot ([string]$plan.Year) $true
        if($entry.State -eq 'Appliqué' -and -not $check.AlreadyApplied){throw ('Le dossier '+$entry.Matricule+' ne correspond plus au résultat validé. Aucune réapplication automatique.')}
    }
    $written=0;$confirmed=0
    foreach($entry in @($plan.Entries)){
        $check=Assert-ScoliaReenrollmentEntry $entry $snapshot ([string]$plan.Year) $true
        if($check.AlreadyApplied){Set-ScoliaReenrollmentEntryState $planId $entry.Matricule 'Appliqué';$confirmed++;continue}
        Set-ScoliaReenrollmentEntryState $planId $entry.Matricule 'Écriture en cours'
        try{
            $r=$check.Expected
            if(Get-Command Set-ITMActivityMessage -ErrorAction SilentlyContinue){Set-ITMActivityMessage ('Réinscription '+($written+$confirmed+1)+' / '+@($plan.Entries).Count+' : '+$r.Matricule+'...')}
            $result=Invoke-ITMDossier @([string]$r.Matricule,[string]$r.Nom,[string]$r.PostNom,[string]$r.Prenom,[string]$r.Sexe,[string]$r.Naissance,[string]$r.Lieu,[string]$r.Tuteur,[string]$r.Contact1,[string]$r.Contact2,[string]$r.Promotion,[string]$r.Annee,[string]$r.Statut,[string]$r.Adresse)
            $reply=$(if($result -is [string]){$result | ConvertFrom-ScoliaJson -ErrorAction Stop}else{$result})
            if(-not $reply.ok){throw 'Le moteur n''a pas confirmé l''enregistrement.'}
            $readBack=Invoke-ITMJsonDirect 'ITMAppEtatApprenant' @([string]$r.Matricule)
            if(-not $readBack.ok -or -not $readBack.learner -or (Get-ScoliaReenrollmentFingerprint $readBack.learner) -cne (Get-ScoliaReenrollmentFingerprint $r)){throw 'Le résultat relu ne correspond pas au plan. Arrêt ; vérification obligatoire avant reprise.'}
            $written++;$script:committedThisAction=$true
            Set-ScoliaReenrollmentEntryState $planId $entry.Matricule 'Appliqué'
            # Actualise l'état utilisé par les validations du moteur, sans modifier aucun paiement.
            $snapshot=Invoke-ITMJsonDirect 'ITMAppSnapshot';$script:data=$snapshot;$script:snapshotValid=$true
        }catch{
            $failure=$_.Exception.Message
            try{Set-ScoliaReenrollmentEntryState $planId $entry.Matricule 'À vérifier' $failure}catch{}
            # Le message du lot donne les résultats durables et incertains. Le préfixe
            # générique « enregistrement réussi » serait inexact pour un lot partiel.
            $script:committedThisAction=$false
            throw ('Traitement arrêté pour '+$entry.Matricule+'. '+$written+' dossier(s) enregistré(s), '+$confirmed+' résultat(s) déjà confirmé(s). '+$failure+' Reprenez ce même plan : les résultats seront relus avant toute écriture.')
        }
    }
    return [pscustomobject]@{Written=$written;Confirmed=$confirmed;Total=@($plan.Entries).Count}
}
function Assert-ScoliaReenrollmentDialogContext($state) {
    $current=Get-ScoliaFeatureContext
    if([string]$current.Id -cne [string]$state.Context.Id -or [string]$current.Year -cne [string]$state.Context.Year){throw 'Les données ou l''année ont changé depuis l''ouverture de l''assistant. Fermez-le puis ouvrez-le de nouveau avant de préparer un plan.'}
}
function Update-ScoliaReenrollmentGrid($state,$plan=$null) {
    $grid=$state.Grid;$grid.DataSource=$null;$grid.Rows.Clear();$grid.Columns.Clear();$grid.ReadOnly=$false
    foreach($spec in @(@('Matricule','Matricule',100),@('Identite','Apprenant',210),@('Avant','Promotion actuelle',140),@('Statut','Statut actuel',110))){$column=New-Object Windows.Forms.DataGridViewTextBoxColumn;$column.Name=$spec[0];$column.HeaderText=$spec[1];$column.Width=$spec[2];$column.ReadOnly=$true;[void]$grid.Columns.Add($column)}
    $decision=New-Object Windows.Forms.DataGridViewComboBoxColumn;$decision.Name='Decision';$decision.HeaderText='Décision';$decision.Width=135;$decision.FlatStyle='Flat';foreach($value in @('À décider','Passage','Maintien','Redoublement','Sortie')){[void]$decision.Items.Add($value)};[void]$grid.Columns.Add($decision)
    $promotion=New-Object Windows.Forms.DataGridViewComboBoxColumn;$promotion.Name='TargetPromotion';$promotion.HeaderText='Promotion cible';$promotion.Width=130;$promotion.FlatStyle='Flat';[void]$promotion.Items.Add('')
    $codes=@($state.Snapshot.promotions | Where-Object {$_.Active -and [string]$_.Annee -ceq [string]$state.Year} | ForEach-Object {[string]$_.Code})
    foreach($code in @($state.Snapshot.learners.Promotion)+$codes){if($code -and -not $promotion.Items.Contains([string]$code)){[void]$promotion.Items.Add([string]$code)}};[void]$grid.Columns.Add($promotion)
    foreach($spec in @(@('Result','Statut cible',110),@('Etat','État du plan',130))){$column=New-Object Windows.Forms.DataGridViewTextBoxColumn;$column.Name=$spec[0];$column.HeaderText=$spec[1];$column.Width=$spec[2];$column.ReadOnly=$true;[void]$grid.Columns.Add($column)}
    $entryMap=@{};if($plan){foreach($entry in @($plan.Entries)){$entryMap[(Key $entry.Matricule)]=$entry};$state.PlanId=[string]$plan.Id}else{$state.PlanId=''}
    foreach($learner in @($state.Snapshot.learners | Sort-Object Nom,PostNom,Prenom)){
        $entry=$entryMap[(Key $learner.Matricule)];$before=$(if($entry){$entry.Before}else{$learner})
        $choice=$(if($entry){$entry.Decision}else{'À décider'});$target=$(if($entry){$entry.TargetPromotion}else{[string]$learner.Promotion})
        if($target -and -not $promotion.Items.Contains([string]$target)){[void]$promotion.Items.Add([string]$target)}
        $result=$(if($entry){$entry.TargetStatus}else{'—'});$stage=$(if($entry){$entry.State}else{''})
        $index=$grid.Rows.Add([string]$learner.Matricule,($before.Nom+' '+$before.PostNom+' '+$before.Prenom),[string]$before.Promotion,[string]$before.Statut,[string]$choice,[string]$target,[string]$result,[string]$stage)
        $grid.Rows[$index].Tag=$learner
        $grid.Rows[$index].Cells['TargetPromotion'].ReadOnly=($choice -ne 'Passage')
        if($entry -and $entry.Error){$grid.Rows[$index].ErrorText=[string]$entry.Error}
    }
    $grid.ReadOnly=[bool]$plan;$state.SaveButton.Enabled=-not [bool]$plan;$state.ApplyButton.Enabled=[bool]$plan
    $state.Message.Text=$(if($plan){'Plan '+$plan.Id.Substring(0,8)+' · '+$plan.State+' · '+@($plan.Entries).Count+' décision(s). L''application contrôle de nouveau les dossiers et les paiements.'}else{'Choisissez une décision par apprenant. « À décider » laisse le dossier inchangé. Maintien conserve le statut ; passage et redoublement rendent le dossier actif ; sortie conserve le dossier avec le statut Sorti.'})
}
function Show-ScoliaReenrollment([string]$PlanId='') {
    Assert-ITMPermission 'Administrer';Assert-ITMPermission 'Saisie'
    $context=Assert-ScoliaReenrollmentContext $null $script:data
    $state=New-ScoliaFeatureDialog -Title 'Réinscription des apprenants' -Subtitle ('Année connectée : '+$context.Year+' · Préparez d''abord la nouvelle année dans Administration > Année scolaire. Cet assistant conserve les archives et les paiements.')
    $state | Add-Member -NotePropertyName Context -NotePropertyValue $context
    $state | Add-Member -NotePropertyName Snapshot -NotePropertyValue $script:data
    $state | Add-Member -NotePropertyName Year -NotePropertyValue ([string]$context.Year)
    $state.Message.Height=72
    $state | Add-Member -NotePropertyName PlanId -NotePropertyValue ''
    $plans=New-Combo $state.Toolbar 0 0 250;$plans.AccessibleName='Plans de réinscription enregistrés'
    $state | Add-Member -NotePropertyName Plans -NotePropertyValue $plans
    $state | Add-Member -NotePropertyName PlanMap -NotePropertyValue @{}
    [void]$plans.Items.Add('Nouveau plan');$plans.SelectedIndex=0
    $stored=Read-ScoliaFeatureStore -Name 'reinscriptions'
    foreach($plan in @($stored.Items | Sort-Object CreatedUtc -Descending)){$label=$plan.Id.Substring(0,8)+' · '+$plan.State+' · '+@($plan.Entries).Count+' dossier(s)';$state.PlanMap[$label]=$plan;[void]$plans.Items.Add($label)}
    $save=New-Button $state.Toolbar 'Enregistrer l''aperçu' 0 0 205
    $apply=New-Button $state.Toolbar 'Appliquer le plan' 0 0 190 $true
    $prepare=New-Button $state.Toolbar 'Préparer la nouvelle année' 0 0 245
    $state | Add-Member -NotePropertyName SaveButton -NotePropertyValue $save
    $state | Add-Member -NotePropertyName ApplyButton -NotePropertyValue $apply
    $plans.Add_SelectedIndexChanged({param($sender,$e)$s=$sender.FindForm().Tag;$p=$s.PlanMap[[string]$sender.Text];Update-ScoliaReenrollmentGrid $s $p})
    $state.Grid.Add_DataError({param($sender,$e)$e.ThrowException=$false;$sender.FindForm().Tag.Message.Text='Choisissez une décision et une promotion présentes dans les listes.'})
    $state.Grid.Add_CurrentCellDirtyStateChanged({param($sender,$e)if($sender.IsCurrentCellDirty){[void]$sender.CommitEdit([Windows.Forms.DataGridViewDataErrorContexts]::Commit)}})
    $state.Grid.Add_CellValueChanged({param($sender,$e)
        if($e.RowIndex -lt 0 -or $e.ColumnIndex -lt 0 -or $sender.ReadOnly){return};$row=$sender.Rows[$e.RowIndex];if(-not $row.Tag){return};$column=$sender.Columns[$e.ColumnIndex].Name
        if($column -eq 'Decision'){
            $choice=[string]$row.Cells['Decision'].Value
            if($choice -ne 'Passage'){$row.Cells['TargetPromotion'].Value=[string]$row.Tag.Promotion}
            $row.Cells['TargetPromotion'].ReadOnly=($choice -ne 'Passage')
            $row.Cells['Result'].Value=switch($choice){'Sortie'{'Sorti'};'Maintien'{[string]$row.Tag.Statut};'À décider'{'—'};default{'Actif'}}
        }
    })
    $save.Add_Click({param($sender,$e)
        $s=$sender.FindForm().Tag
        try{
            Assert-ScoliaReenrollmentDialogContext $s
            [void]$s.Grid.EndEdit();$decisions=@($s.Grid.Rows | ForEach-Object {[pscustomobject]@{Matricule=[string]$_.Cells['Matricule'].Value;Decision=[string]$_.Cells['Decision'].Value;TargetPromotion=[string]$_.Cells['TargetPromotion'].Value}})
            $snapshot=Invoke-ITMJsonDirect 'ITMAppSnapshot';$plan=New-ScoliaReenrollmentPlan $decisions $snapshot;Save-ScoliaReenrollmentPlan $plan
            $s.Snapshot=$snapshot
            $label=$plan.Id.Substring(0,8)+' · '+$plan.State+' · '+@($plan.Entries).Count+' dossier(s)';$s.PlanMap[$label]=$plan;[void]$s.Plans.Items.Add($label);$s.Plans.SelectedItem=$label
            $s.Message.Text='Aperçu enregistré : '+@($plan.Entries).Count+' décision(s). Aucun dossier scolaire modifié. Vérifiez les promotions et statuts cibles avant l''application.'
        }catch{App-Error $_}
    })
    $apply.Add_Click({param($sender,$e)
        if($script:busy){return};$s=$sender.FindForm().Tag
        if(-not $s.PlanId){return}
        try{Assert-ScoliaReenrollmentDialogContext $s}catch{App-Error $_;return}
        if([ITMMessageBox]::Show($s.Form,'Appliquer les décisions du plan '+$s.PlanId.Substring(0,8)+' dans l''année '+$s.Year+' ? Les sorties conservent leurs dossiers. Chaque dossier est enregistré séparément ; en cas d''arrêt, la reprise vérifie les résultats déjà enregistrés.','Confirmer les réinscriptions','YesNo','Question') -ne 'Yes'){return}
        $script:scoliaReenrollmentApplyId=[string]$s.PlanId
        Do-Work {
            $failure=$null;$summary=$null
            try{$summary=Invoke-ScoliaReenrollment $script:scoliaReenrollmentApplyId}catch{$failure=$_}
            # Même un arrêt partiel doit afficher les dossiers réellement enregistrés.
            try{Refresh-Data}catch{if(-not $failure){throw}}
            if($failure){throw $failure}
            $script:status.Text='Réinscriptions : '+$summary.Written+' dossier(s) enregistré(s), '+$summary.Confirmed+' résultat(s) déjà conformes.'
        } 'Vérification et enregistrement des réinscriptions...'
        try{$doc=Read-ScoliaFeatureStore -Name 'reinscriptions';$plan=@($doc.Items | Where-Object {$_.Id -ceq $s.PlanId})[0];$s.Snapshot=Invoke-ITMJsonDirect 'ITMAppSnapshot';foreach($key in @($s.PlanMap.Keys)){if($s.PlanMap[$key].Id -ceq $plan.Id){$s.PlanMap[$key]=$plan}};Update-ScoliaReenrollmentGrid $s $plan}catch{App-Error $_}
    })
    $prepare.Add_Click({param($sender,$e)
        $s=$sender.FindForm().Tag
        if($script:adminTabs -and $script:adminPages.ContainsKey('Année scolaire')){$s.Form.Close();Show-Page 'Administration';$script:adminTabs.SelectedTab=$script:adminPages['Année scolaire']}else{$s.Message.Text='Ouvrez Administration > Année scolaire, puis Archivez et préparez la nouvelle année. Relancez ensuite cet assistant dans le nouveau classeur.'}
    })
    Update-ScoliaReenrollmentGrid $state
    if($PlanId){foreach($label in @($state.PlanMap.Keys)){if([string]$state.PlanMap[$label].Id -ceq $PlanId){$plans.SelectedItem=$label;break}}}
    if((Get-Command Mount-ScoliaWorkspaceForm -ErrorAction SilentlyContinue) -and (Mount-ScoliaWorkspaceForm $state.Form)){return}
    try{Apply-ITMThemeForm $state.Form;[void]$state.Form.ShowDialog($script:main)}finally{$state.Form.Dispose()}
}
