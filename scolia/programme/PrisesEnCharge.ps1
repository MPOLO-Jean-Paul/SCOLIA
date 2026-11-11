if(-not (Test-Path function:Import-ITMCompiledSource)){. (Join-Path $PSScriptRoot 'CompilationCache.ps1')}
# Benefits are an independent funding regime, never fictitious receipts.
if(-not ('ITMPE' -as [type])){Import-ITMCompiledSource -Path (Join-Path $PSScriptRoot 'PrisesEnCharge.cs') -ReferencedAssemblies @('System.Xml')}
$script:peLedger=$null;$script:peIndex=@{};$script:peVersion=$false;$script:peError=''
function Load-ITMPEC {
    $script:peIndex=@{};$script:peError='';$script:peVersion=$false;$script:peLedger=$null
    if(-not $script:store){return}
    try{
        if(-not [IO.File]::Exists((Join-Path $script:storeFolder 'prises-en-charge.xml')) -and ([IO.File]::Exists((Join-Path $script:storeFolder 'prises-en-charge.presence')) -or [IO.File]::Exists((Join-Path $script:storeFolder 'prises-en-charge.xml.precedent')))){throw 'Registre des prises en charge manquant. Restaurez une sauvegarde complète avant de continuer.'}
        $script:peLedger=[ITMPE]::Load((Join-Path $script:storeFolder 'prises-en-charge.xml'),[string]$script:store.Id)
        foreach($r in $script:peLedger.Records){
            $peKey=(Key $r.Matricule) + '|' + [ITMPE]::Year($r.Annee)
            $script:peIndex[$peKey]=$r
        }
    }catch{$script:peError=$_.Exception.Message;throw}
    try{$script:peVersion=($script:excel.Run((Api-Name 'ITMAppPriseEnChargeVersion')) -eq 'ITM-PEC-1')}catch{$script:peVersion=$false}
}
function Get-ITMPECRecord($person){
    if($script:peError){throw $script:peError};if(-not $person){return $null}
    if(-not $script:peIndex.Count){return $null};try{$y=[ITMPE]::Year([string]$person.Annee)}catch{return $null};$k=(Key $person.Matricule)+'|'+$y
    if($script:peIndex.ContainsKey($k)){return $script:peIndex[$k]};return $null
}
function Get-ITMPECMonthIndex([string]$month){return [Array]::IndexOf([string[]][ITMPE]::Months,$month)}
function Get-ITMPECCoverage($person,[string]$month=''){
    $record=Get-ITMPECRecord $person;$fee=$script:fees[(Promo $person.Promotion)];if(-not $record -or -not $fee){return [decimal]0}
    if($month){$i=Get-ITMPECMonthIndex $month;if([ITMPE]::Covered($record,$i)){return [Math]::Max([decimal]0,[decimal]$fee.Mensuel-(Paid $person.Matricule $month))};return [decimal]0}
    $sum=[decimal]0;$months=[string[]][ITMPE]::Months;if($script:scolia){$months=[ScoliaLedger]::Schedule($script:scolia.StartMonth,$script:scolia.Months)};foreach($m in $months){$sum+=Get-ITMPECCoverage $person $m};return $sum
}
function Get-ITMPECAnnual($person){if($script:scolia){return [decimal]$script:fees[(Promo $person.Promotion)].Annuel};$fee=$script:fees[(Promo $person.Promotion)];if(Get-ITMPECRecord $person){return [decimal]$fee.Mensuel*10};return [decimal]$fee.Annuel}
function Get-ITMPECDue($person,[string]$month){if(-not $script:scolia -and $month -eq 'Juin' -and -not (Get-ITMPECRecord $person)){return [decimal]0};$fee=$script:fees[(Promo $person.Promotion)];return [Math]::Max([decimal]0,[decimal]$fee.Mensuel-(Paid $person.Matricule $month)-(Get-ITMPECCoverage $person $month))}
function Get-ITMPECPayingBalance($person){return [Math]::Max([decimal]0,(Get-ITMPECAnnual $person)-(Paid $person.Matricule)-(Get-ITMPECCoverage $person))}
function Get-ITMPECStatus($person){
    $r=Get-ITMPECRecord $person;if(-not $r){return 'Payant'};$i=[ITMPE]::MonthIndex((Get-Date),[string]$person.Annee)
    if([ITMPE]::Covered($r,$i)){return 'Pris en charge'}
    if($i -lt 9 -and @($r.Couverture | Select-Object -Skip ([Math]::Max(0,$i+1)) | Where-Object {$_}).Count){return 'Prise en charge à venir'}
    return 'Payant — prise en charge terminée'
}
function Get-ITMPECRows([string]$promotion='Toutes les promotions',[bool]$withPaying=$false){
    $rows=New-Object 'System.Collections.Generic.List[object]'
    foreach($a in @(Learner-Rows '' $promotion)){
        if(-not $a.Eligible){continue};$r=Get-ITMPECRecord $a;if(-not $r -and -not $withPaying){continue}
        $fee=$script:fees[(Promo $a.Promotion)];$item=[ordered]@{Matricule=$a.Matricule;Nom=($a.Nom+' '+$a.PostNom+' '+$a.Prenom);Promotion=$a.Promotion;Annee=$a.Annee;Regime=(Get-ITMPECStatus $a);Garant=$(if($r){$r.Garant}else{''});Mensualite=[decimal]$fee.Mensuel;MoisCouverts=$(if($r){@($r.Couverture | Where-Object {$_}).Count}else{0});TotalCouvert=(Get-ITMPECCoverage $a);RestePayant=(Get-ITMPECPayingBalance $a)}
        foreach($m in [ITMPE]::Months){$item[$m]=Get-ITMPECCoverage $a $m};$rows.Add([pscustomobject]$item)
    };return $rows.ToArray()
}
function Assert-ITMPECWrite {
    Assert-ITMPermission 'Saisie'
    if(-not $script:store -or -not $script:data -or $script:data.closed){throw 'Connectez un dossier data dont l''année scolaire est ouverte.'}
    if(-not $script:peVersion){throw 'Cette fonction nécessite la nouvelle passerelle. Fermez Excel puis installez les corrections avec INSTALLER.vbs et reconnectez le classeur corrigé.'}
    if($script:peError){throw $script:peError}
}
function Save-ITMPECRecord($record,[string]$action,[string]$motif){
    Assert-ITMPECWrite;$path=Join-Path $script:storeFolder 'prises-en-charge.xml'
    $latest=[ITMPE]::Load($path,[string]$script:store.Id)
    if($latest.Revision -ne $script:peLedger.Revision){throw 'La liste a changé. Fermez puis rouvrez Prises en charge avant de recommencer.'}
    [ITMPE]::Apply($latest,$record,[string]$script:accountSession.Name,$action,$motif)
    [ITMPE]::Save($path,$latest,$script:peLedger.Revision);$script:committedThisAction=$true
    Write-ITMAtomicJson (Join-Path $script:storeFolder 'prises-en-charge.presence') ([pscustomobject]@{Dataset=$script:store.Id})
    $script:peLedger=$latest
    $peKey=(Key $record.Matricule) + '|' + [ITMPE]::Year($record.Annee)
    $script:peIndex[$peKey]=$record
    Write-ITMUserAudit $action ($record.Matricule+' / '+$record.Annee+' / '+$motif)
    # La prise en charge vit dans son registre XML : aucun apprenant ni paiement Excel
    # n'a changé. Éviter Refresh-Data ici supprime une relecture complète inutile.
    $script:dataRevision++;$script:snapshotValid=$true
    if($script:viewRevision){foreach($name in @('Tableau de bord','Paiements')){if($script:viewRevision.ContainsKey($name)){$script:viewRevision.Remove($name)}}}
    $script:status.Text='Prise en charge enregistrée : '+$record.Matricule
}
function Show-ITMPECEditor([string]$mat,[bool]$paying=$false){
    $script:committedThisAction=$false;Assert-ITMPECWrite;$matches=$(if(Test-Path function:Get-ITMLearnerMatches){@(Get-ITMLearnerMatches $mat | Where-Object {$_.Eligible})}else{@($script:data.learners | Where-Object {(Key $_.Matricule) -eq (Key $mat) -and $_.Eligible})});if($matches.Count -ne 1){throw 'Sélectionnez un dossier valide et unique.'};$a=$matches[0];$old=Get-ITMPECRecord $a
    if($paying -and -not $old){throw 'Cet apprenant ne possède pas de prise en charge.'}
    $d=New-Object Windows.Forms.Form;Set-ITMWindowIcon $d;$d.Text=$(if($paying){'Déclarer payant'}elseif($old){'Modifier la prise en charge'}else{'Enregistrer une prise en charge'});$d.StartPosition='CenterParent';$d.Size=New-Object Drawing.Size(760,650);$d.MinimumSize=New-Object Drawing.Size(360,380);$d.Font=$script:main.Font
    $table=New-ScoliaTable $d;$table.RowCount=2
    $identity=New-Object ITMReadableLabel;$identity.Text=($a.Nom+' '+$a.PostNom+' '+$a.Prenom+' — '+$a.Promotion+"`r`n"+$a.Matricule+' / '+$a.Annee+' / mensualité : '+(Money $script:fees[(Promo $a.Promotion)].Mensuel));$identity.Dock='Fill';$table.Controls.Add($identity,0,0);$table.SetColumnSpan($identity,2);$identity.Margin=New-Object Windows.Forms.Padding(0,0,0,10)
    $hint=New-Object ITMReadableLabel;$hint.Text='Septembre à juin : dix mensualités au tarif de la promotion. Les mois antérieurs au mois d''effet sont conservés. Un mois déjà payé ne peut pas devenir pris en charge.';$hint.Dock='Fill';$table.Controls.Add($hint,0,1);$table.SetColumnSpan($hint,2);$hint.Margin=New-Object Windows.Forms.Padding(0,0,0,14)
    $fields=@{};$names=@('Garant / organisme (facultatif)','Début de couverture','Fin de couverture','Mois d''effet','Motif obligatoire')
    for($i=0;$i -lt 5;$i++){
        if($i -in @(1,2,3)){$f=New-Object ITMThemeComboBox;$f.DropDownStyle='DropDownList';[void]$f.Items.AddRange([object[]][ITMPE]::Months);if($i -eq 3){[void]$f.Items.Add('Après juin')}}else{$f=New-Object Windows.Forms.TextBox;$f.MaxLength=$(if($i -eq 0){160}else{1000});if($i -eq 4){$f.Multiline=$true;$f.Height=96;$f.ScrollBars='Vertical'}}
        [void](Add-ScoliaField $table $names[$i] $f);$fields[$i]=$f
    }
    $fields[1].SelectedIndex=0;$fields[2].SelectedIndex=9;$effective=[Math]::Max(0,[Math]::Min(10,[ITMPE]::MonthIndex((Get-Date),[string]$a.Annee)))
    $fields[3].SelectedIndex=$(if($old){$effective}else{0})
    if($old){$fields[0].Text=$old.Garant;$indices=@(0..9 | Where-Object {$old.Couverture[$_]});if($indices.Count){$fields[1].SelectedIndex=$indices[0];$fields[2].SelectedIndex=$indices[-1]}}
    if($paying){$fields[0].Enabled=$false;$fields[1].Enabled=$false;$fields[2].Enabled=$false;$hint.Text='Choisissez le mois à partir duquel l''apprenant redevient payant. Les mois précédents conservent leur couverture. Aucun encaissement n''est créé.'}
    $footer=New-Object Windows.Forms.FlowLayoutPanel;$footer.Dock='Bottom';$footer.FlowDirection='RightToLeft';$footer.WrapContents=$true;$footer.AutoSize=$true;$footer.AutoSizeMode='GrowAndShrink';$footer.Padding=New-Object Windows.Forms.Padding(12);$d.Controls.Add($footer)
    $save=New-Button $footer 'Enregistrer la prise en charge' 0 0 280 $true;$cancel=New-Button $footer 'Annuler' 0 0 120;$cancel.DialogResult='Cancel';$d.CancelButton=$cancel
    $save.Tag=[pscustomobject]@{Fields=$fields;Person=$a;Paying=$paying;Original=$old;MinimumEffect=$effective;Dialog=$d}
    $save.Add_Click({param($sender,$e)
        if($script:busy){return};$script:committedThisAction=$false
        try{
            $s=$sender.Tag;$f=$s.Fields;$person=$s.Person;$before=Get-ITMPECRecord $person
            if(-not $f[4].Text.Trim()){throw 'Précisez le motif.'}
            if($before -and $f[3].SelectedIndex -lt $s.MinimumEffect){throw 'Les mois antérieurs sont conservés. Choisissez le mois actuel ou un mois ultérieur.'}
            if($s.Paying){$next=[ITMPE]::Paying($before,$f[3].SelectedIndex)}else{$paid=[decimal[]]@([ITMPE]::Months | ForEach-Object {Paid $person.Matricule $_});$next=[ITMPE]::Plan($before,$person.Matricule,$person.Annee,$person.Promotion,[decimal]$script:fees[(Promo $person.Promotion)].Mensuel,$f[1].SelectedIndex,$f[2].SelectedIndex,$f[3].SelectedIndex,$paid,$f[0].Text,$f[4].Text)}
            $fee=[decimal]$script:fees[(Promo $person.Promotion)].Mensuel;$question='Montant couvert après modification : '+(Money ([ITMPE]::Total($next,$fee)))+"`r`n"+'Les mois non couverts restent payants. Confirmer ?'
            if([ITMMessageBox]::Show($s.Dialog,$question,'Confirmer la prise en charge','YesNo','Question',[Windows.Forms.MessageBoxDefaultButton]::Button2) -ne 'Yes'){return}
            $script:pePending=$next;$script:pePendingAction=$(if($s.Paying){'Retour au statut payant'}elseif($before){'Prise en charge modifiée'}else{'Prise en charge créée'});$script:pePendingMotif=$f[4].Text.Trim()
            Do-Work {Save-ITMPECRecord $script:pePending $script:pePendingAction $script:pePendingMotif} 'Enregistrement de la prise en charge...'
            if($script:committedThisAction){$s.Dialog.DialogResult='OK'}
        }catch{App-Error $_ $script:committedThisAction}
    })
    Enable-ITMFluidTable $table
    Enable-ITMDialogShortcuts $d;Apply-ITMThemeForm $d
    try{[void]$d.ShowDialog($script:main)}finally{$d.Dispose()}
}
function Show-ITMPECList {
    Assert-ITMPermission 'Lire';if(-not $script:data){throw 'Connectez les données.'}
    $screen=New-ScoliaFeatureDialog 'Prises en charge' 'Recherche instantanée, couverture mensuelle et gestion du régime de chaque apprenant.' 1180 770
    $d=$screen.Form;$grid=$screen.Grid;$buttons=$screen.Actions;$note=$screen.Message
    $search=New-Object Windows.Forms.TextBox;$search.CharacterCasing='Upper';$search.AccessibleName='Recherche instantanée par matricule, nom, promotion ou garant'
    $promo=New-Object ITMThemeComboBox;$promo.DropDownStyle='DropDownList'
    Set-Choices $promo (@('Toutes les promotions')+@($script:data.promotions | ForEach-Object {$_.Code} | Select-Object -Unique)) 'Toutes les promotions'
    $filters=New-Object ITMSearchFields($search,$promo);$filters.SearchLabel.Text='Rechercher un apprenant';$filters.KindLabel.Text='Promotion'
    $screen.Toolbar.Controls.Add($filters);$screen|Add-Member NoteProperty SearchFields $filters
    $all=New-Object ITMReadableCheckBox;$all.Text='Inclure les payants';$all.AutoSize=$true;$all.Margin=New-Object Windows.Forms.Padding(0,8,0,8);$screen.Toolbar.Controls.Add($all)
    $note.Text='Saisissez quelques lettres ou un matricule : la liste se filtre sans relire Excel.'
    $add=New-Button $buttons 'Nouvelle prise en charge' 0 0 210;$edit=New-Button $buttons 'Modifier' 0 0 150;$pay=New-Button $buttons 'Déclarer payant' 0 0 170;$detail=New-Button $buttons 'Détail des mois' 0 0 180;$csv=New-Button $buttons 'Exporter la liste' 0 0 190
    $count=New-Object ITMReadableLabel;$count.AutoSize=$true;$count.Margin=New-Object Windows.Forms.Padding(12,10,0,0);$buttons.Controls.Add($count)

    $state=[pscustomobject]@{Grid=$grid;Promo=$promo;All=$all;Search=$search;BaseRows=@();Rows=@();Add=$add;Edit=$edit;Pay=$pay;Note=$note;Count=$count;Apply=$null;Reload=$null}
    foreach($c in @($promo,$all,$search,$add,$edit,$pay,$detail,$csv)){$c.Tag=$state}
    $columns=@(@('Matricule','Matricule'),@('Nom','Nom complet'),@('Promotion','Promotion'),@('Annee','Année scolaire'),@('Regime','Régime'),@('Garant','Garant'),@('Mensualite','Mensualité FC'),@('MoisCouverts','Mois couverts'),@('TotalCouvert','Total couvert FC'),@('RestePayant','Reste payant FC'))
    $apply={param($sender,$e)
        $s=$(if($sender -and $sender.Tag){$sender.Tag}else{$state});$terms=@((Key $s.Search.Text) -split '\s+' | Where-Object {$_})
        if(-not $terms.Count){$rows=@($s.BaseRows)}else{
            $rows=@($s.BaseRows | Where-Object {
                $text=Key ($_.Matricule+' '+$_.Nom+' '+$_.Promotion+' '+$_.Garant+' '+$_.Regime)
                $ok=$true;foreach($term in $terms){if($text.IndexOf($term,[StringComparison]::Ordinal) -lt 0){$ok=$false;break}};$ok
            })
        }
        $s.Rows=$rows;Set-Grid $s.Grid $rows $columns;$s.Count.Text=$rows.Count.ToString()+' résultat(s)'
        $enabled=(Test-ITMPermission 'Saisie') -and $script:peVersion -and -not $script:data.closed;$s.Add.Enabled=$enabled;$s.Edit.Enabled=$enabled;$s.Pay.Enabled=$enabled
        if(-not $script:peVersion){$s.Note.Text='Installez les corrections avec INSTALLER.vbs pour enregistrer les prises en charge. Les données existantes restent consultables.'}
    }
    $reload={param($sender,$e)
        $s=$(if($sender -and $sender.Tag){$sender.Tag}else{$state});$s.BaseRows=@(Get-ITMPECRows ([string]$s.Promo.SelectedItem) $s.All.Checked);& $s.Apply $s.Search $null
    }
    $state.Apply=$apply;$state.Reload=$reload;$csv.Enabled=Test-ITMPermission 'Exporter'
    $timer=New-Object Windows.Forms.Timer;$timer.Interval=45;$timer.Add_Tick({$timer.Stop();try{& $state.Apply $search $null}catch{App-Error $_}})
    $search.Add_TextChanged({$timer.Stop();$timer.Start()})
    $promo.Add_SelectedIndexChanged({try{& $state.Reload $promo $null}catch{App-Error $_}});$all.Add_CheckedChanged({try{& $state.Reload $all $null}catch{App-Error $_}})
    $add.Add_Click({param($sender,$e)try{Show-ITMPECEditor (Selected-Mat $sender.Tag.Grid);& $sender.Tag.Reload $sender $null}catch{App-Error $_}})
    $edit.Add_Click({param($sender,$e)try{$mat=Selected-Mat $sender.Tag.Grid;$a=@($(if(Test-Path function:Get-ITMLearnerMatches){Get-ITMLearnerMatches $mat}else{$script:data.learners|Where-Object {(Key $_.Matricule) -eq (Key $mat)}}))[0];if(-not (Get-ITMPECRecord $a)){throw 'Utilisez Nouvelle prise en charge pour ce dossier.'};Show-ITMPECEditor $mat;& $sender.Tag.Reload $sender $null}catch{App-Error $_}})
    $pay.Add_Click({param($sender,$e)try{Show-ITMPECEditor (Selected-Mat $sender.Tag.Grid) $true;& $sender.Tag.Reload $sender $null}catch{App-Error $_}})
    $detail.Add_Click({param($sender,$e)try{$s=$sender.Tag;$mat=Selected-Mat $s.Grid;$row=@($s.Rows | Where-Object {$_.Matricule -eq $mat})[0];$lines=@($row.Nom+' / '+$row.Promotion,$row.Regime,'Mensualité : '+(Money $row.Mensualite));foreach($m in [ITMPE]::Months){$lines+=($m+' : couvert '+(Money $row.$m))};$lines+=('Total couvert : '+(Money $row.TotalCouvert),'Reste payant : '+(Money $row.RestePayant));foreach($h in $script:peLedger.History){if($h.Matricule -eq $mat -and $h.Annee -eq [ITMPE]::Year($row.Annee)){$lines+=($h.Date+' / '+$h.User+' / '+$h.Action+' / '+$h.Motif)}};Show-TextDetail 'Prise en charge — mois et historique' ($lines -join "`r`n")}catch{App-Error $_}})
    $csv.Add_Click({param($sender,$e)try{Save-ITMCsv $sender.Tag.Rows 'Prises_en_charge.csv'}catch{App-Error $_}})
    $grid.Add_KeyDown({param($sender,$e)if($e.Control -and $e.KeyCode -eq 'F'){$e.Handled=$true;$e.SuppressKeyPress=$true;$search.Focus();$search.SelectAll()}})
    & $reload $promo $null;Enable-ITMDialogShortcuts $d;Apply-ITMThemeForm $d
    $d.Add_Shown({$search.Focus()});$d.Add_FormClosed({$timer.Stop();$timer.Dispose()})
    try{[void]$d.ShowDialog($script:main)}finally{if(-not $d.IsDisposed){$d.Dispose()};if($script:data){try{Refresh-ITMCurrentPage}catch{}}}
}
function Update-ITMPECDashboard($people){
    if(-not $script:dashPECValue){return};$annual=[decimal]0;$period=[decimal]0;$n=0;$month=[string]$script:dashMonth.Text
    foreach($a in $people){$r=Get-ITMPECRecord $a;if(-not $r){continue};$total=Get-ITMPECCoverage $a;$annual+=$total;if($total -gt 0){$n++};if($month -eq 'Tous les mois'){$period+=$total}else{$period+=Get-ITMPECCoverage $a $month}}
    $script:dashPECValue.Text=Money $annual;$script:dashPECHint.Text="$n bénéficiaire(s) sur l'année • Couvert pour la période sélectionnée : $(Money $period) • Dix mois, septembre à juin. Ce montant n'est pas une recette encaissée."
}
