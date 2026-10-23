function Get-ITMHistoryDate($entry) {
    $date=[DateTime]::MinValue
    $formats=[string[]]@('dd/MM/yyyy','d/M/yyyy','yyyy-MM-dd','yyyy-MM-dd HH:mm:ss','dd/MM/yyyy HH:mm:ss')
    if([DateTime]::TryParseExact([string]$entry.Date,$formats,[Globalization.CultureInfo]::InvariantCulture,[Globalization.DateTimeStyles]::None,[ref]$date)){return $date}
    return [DateTime]::MaxValue
}
function Sort-ITMPaymentHistory($rows) {
    @($rows | Sort-Object -Property @(@{Expression={Get-ITMHistoryDate $_}},@{Expression={try{[long]$_.ID}catch{[long]::MaxValue}}},'Numero','Source','Row'))
}
function Invoke-ITMJson([string]$name,[object[]]$arguments=@()) {
    if($script:store){
        switch($name){
            'ITMAppCorrectionPaiement' {return Invoke-ITMManagedCorrection $arguments}
            'ITMAppPromotion' {return Invoke-ITMManagedPromotion $arguments}
            'ITMAppAdministration' {return Get-ITMManagedAdministration}
            'ITMAppSauvegarde' {return Backup-ITMStore}
            'ITMAppCloture' {return Invoke-ITMManagedClosure $arguments}
        }
    }
    return Invoke-ITMJsonDirect $name $arguments
}
function Invoke-ITMJsonDirect([string]$name,[object[]]$arguments=@()) {
    if($name -in @('ITMAppCorrectionPaiement','ITMAppPromotion','ITMAppCloture','ITMAppRestaurer')){Assert-ITMPermission 'Administrer'}
    if($name -eq 'ITMAppSauvegarde'){Assert-ITMPermission 'Sauvegarder'}
    $writes=@('ITMAppScoliaSynchroniser','ITMAppScoliaImport','ITMAppScoliaConfiguration','ITMAppDossier','ITMAppPaiement','ITMAppSupprimerApprenant','ITMAppFusionnerApprenants','ITMAppCorrectionPaiement','ITMAppPromotion','ITMAppCloture')
    $script:book=Resolve-ITMLiveWorkbook $script:book
    $macro=Api-Name $name
    if($arguments.Count -notin @(0,1,2,3,4,6,7,8,11,14)){throw 'Nombre de paramètres incorrect.'}
    $reply=$(if($name -in $writes){Invoke-ITMWriteMacro $name $arguments}else{Invoke-ITMExcelRun $script:excel $macro $arguments})
    $value=[string]$reply | ConvertFrom-Json
    if($null -ne $value -and $value.PSObject.Properties['ok'] -and -not $value.ok){throw [string]$value.error}
    return $value
}
function Save-ITMCsv($rows,[string]$filename) {
    Assert-ITMPermission 'Exporter'
    if($null -eq $rows -or @($rows).Count -eq 0){throw 'Aucune ligne à exporter pour ce filtre.'}
    $dialog=New-Object Windows.Forms.SaveFileDialog;$dialog.Filter='Fichier CSV (*.csv)|*.csv';$dialog.FileName=$filename
    $exportFolder=Join-Path ([IO.Path]::GetDirectoryName($PSScriptRoot)) 'exports';[void][IO.Directory]::CreateDirectory($exportFolder);$dialog.InitialDirectory=$exportFolder
    try{if($dialog.ShowDialog($script:main) -eq 'OK'){@($rows) | Export-Csv -LiteralPath $dialog.FileName -Delimiter ';' -Encoding UTF8 -NoTypeInformation;Write-ITMUserAudit 'Export CSV';$script:status.Text='Liste exportée : '+$dialog.FileName}}finally{$dialog.Dispose()}
}
function Show-TextDetail([string]$title,[string]$text) {
    $dialog=New-Object Windows.Forms.Form;Set-ITMWindowIcon $dialog;$dialog.Text=(Get-ITMInstitutionName)+' | '+$title;$dialog.Size=New-Object Drawing.Size(860,620);$dialog.StartPosition='CenterParent'
    $box=New-Object Windows.Forms.TextBox;$box.Dock='Fill';$box.Multiline=$true;$box.ReadOnly=$true;$box.ScrollBars='Both';$box.Font=New-Object Drawing.Font('Segoe UI',10);$box.Text=$text
    $dialog.Controls.Add($box)
    $bar=New-Object Windows.Forms.FlowLayoutPanel;$bar.Dock='Bottom';$bar.AutoSize=$true;$bar.Padding=New-Object Windows.Forms.Padding(8);$bar.FlowDirection='RightToLeft';$dialog.Controls.Add($bar)
    $close=New-Button $bar 'Fermer' 0 0 140;$close.DialogResult='Cancel';$dialog.CancelButton=$close
    $copy=New-Button $bar 'Copier le texte' 0 0 170
    $copy.Add_Click({try{if($box.TextLength){[Windows.Forms.Clipboard]::SetText($box.Text);$copy.Text='Texte copié'}}catch{$copy.Text='Copie indisponible'}})
    $export=New-Button $bar 'Exporter en texte' 0 0 190;$export.Enabled=Test-ITMPermission 'Exporter'
    $export.Add_Click({
        $file=New-Object Windows.Forms.SaveFileDialog;$file.Filter='Rapport texte (*.txt)|*.txt';$file.FileName='SCOLIA_Rapport_'+(Get-Date -Format 'yyyyMMdd_HHmmss')+'.txt'
        try{Assert-ITMPermission 'Exporter';if($file.ShowDialog($dialog) -eq 'OK'){[IO.File]::WriteAllText($file.FileName,$box.Text,[Text.Encoding]::UTF8);$script:status.Text='Rapport exporté : '+$file.FileName}}catch{App-Error $_}finally{$file.Dispose()}
    })
    Enable-ITMDialogResponsive $dialog;Apply-ITMThemeForm $dialog
    try{[void]$dialog.ShowDialog($script:main)}finally{$dialog.Dispose()}
}
function Print-FichePreview([string]$text) {
    Assert-ITMPermission 'Exporter'
    $document=New-Object Drawing.Printing.PrintDocument
    $preview=New-Object Windows.Forms.PrintPreviewDialog;Set-ITMWindowIcon $preview;$preview.Document=$document;$preview.Width=1000;$preview.Height=750
    $font=New-Object Drawing.Font('Segoe UI',10);$script:printLines=@($text -split "`r?`n");$script:printIndex=0;$script:printFont=$font
    $document.Add_BeginPrint({$script:printIndex=0})
    $document.Add_PrintPage({param($sender,$e)
        $y=[single]$e.MarginBounds.Top
        while($script:printIndex -lt $script:printLines.Count){
            $line=[string]$script:printLines[$script:printIndex];if(-not $line){$line=' '}
            $measure=$e.Graphics.MeasureString($line,$script:printFont,$e.MarginBounds.Width)
            $height=[single]([Math]::Max($script:printFont.GetHeight($e.Graphics),$measure.Height)+5)
            if($y+$height -gt $e.MarginBounds.Bottom){break}
            $rect=New-Object Drawing.RectangleF($e.MarginBounds.Left,$y,$e.MarginBounds.Width,$height)
            $e.Graphics.DrawString($line,$script:printFont,[Drawing.Brushes]::Black,$rect)
            $y+=$height;$script:printIndex++
        }
        $e.HasMorePages=$script:printIndex -lt $script:printLines.Count
    })
    try{
        if(-not $document.PrinterSettings.IsValid){throw 'Aucune imprimante disponible pour cet aperçu. Vous pouvez exporter les rapports depuis Exports.'}
        [void]$preview.ShowDialog($script:main)
    }finally{$preview.Dispose();$document.Dispose();$font.Dispose()}
}
function Show-Fiche([string]$mat) {
    $people=$(if(Get-Command Get-ITMLearnerMatches -ErrorAction SilentlyContinue){@(Get-ITMLearnerMatches $mat)}else{@($script:data.learners | Where-Object {(Key $_.Matricule) -eq (Key $mat)})})
    if($people.Count -ne 1){throw 'Dossier absent ou matricule en double.'}
    $a=$people[0];$history=$(if(Get-Command Get-ScoliaPaymentRows -ErrorAction SilentlyContinue){@(Get-ScoliaPaymentRows $mat)}else{Sort-ITMPaymentHistory @($script:data.payments | Where-Object {(Key $_.Matricule) -eq (Key $mat)})})
    $fee=$script:fees[(Promo $a.Promotion)];$paid=Paid $mat
    $details=@(((Get-ITMInstitutionName)+' — FICHE APPRENANT'),('Matricule : '+$mat),('Nom : '+$a.Nom+' '+$a.PostNom+' '+$a.Prenom),('Sexe : '+$a.Sexe),('Naissance : '+$a.Naissance+' à '+$a.Lieu),('Promotion : '+$a.Promotion+' · Année : '+$a.Annee),('Statut : '+$a.Statut),('Adresse : '+$a.Adresse),('Contact parent 1 : '+$a.Contact1),('Contact parent 2 : '+$a.Contact2),('Total annuel versé : '+(Money $paid)))
    if($fee){$details+=('Reste annuel : '+(Money (Get-ITMPECPayingBalance $a)))}
    if(Get-ITMPECRecord $a){$details+=('Régime : '+(Get-ITMPECStatus $a),'Total pris en charge : '+(Money (Get-ITMPECCoverage $a)))}
    $printText=($details+@('','HISTORIQUE DES PAIEMENTS')+@($history | ForEach-Object {$_.Date+' | N° '+$_.Numero+' | '+$_.Mois+' | '+(Money $_.Verse)})) -join "`r`n"
    $script:fichePrintText=$printText;$script:ficheAction=''
    $dialog=New-Object Windows.Forms.Form;Set-ITMWindowIcon $dialog;$dialog.Text=(Get-ITMInstitutionName)+' | Fiche '+$mat;$dialog.Size=New-Object Drawing.Size(1050,780);$dialog.MinimumSize=New-Object Drawing.Size(820,620);$dialog.StartPosition='CenterParent';$dialog.BackColor=[Drawing.Color]::White;$dialog.Font=$script:main.Font
    $footer=New-Object Windows.Forms.FlowLayoutPanel;$footer.Dock='Bottom';$footer.Height=100;$footer.Padding=New-Object Windows.Forms.Padding(14,8,0,0);$dialog.Controls.Add($footer)
    $modify=New-Button $footer 'Modifier ce dossier' 24 15 235 $true;$modify.Enabled=-not $script:data.closed
    $pay=New-Button $footer 'Enregistrer un paiement' 274 15 270;$pay.Enabled=[bool]($a.Eligible -and -not $script:data.closed)
    $print=New-Button $footer 'Aperçu avant impression' 560 15 290
    $modify.Add_Click({$script:ficheAction='Modifier';$script:ficheDialog.Close()});$pay.Add_Click({$script:ficheAction='Paiement';$script:ficheDialog.Close()})
    $print.Add_Click({try{Print-FichePreview $script:fichePrintText}catch{App-Error $_}})
    $body=New-Object Windows.Forms.Panel;$body.Dock='Fill';$body.AutoScroll=$true;$dialog.Controls.Add($body);$body.BringToFront()
    Build-ITMModernFiche $dialog $body $a $mat $fee $paid $history
    $script:ficheDialog=$dialog
    Enable-ITMDialogResponsive $dialog
    try{[void]$dialog.ShowDialog($script:main)}finally{$dialog.Dispose();$script:ficheDialog=$null}
    $script:lastMat=$mat
    if($script:ficheAction -eq 'Modifier'){Show-Dossier $mat}
    if($script:ficheAction -eq 'Paiement'){Choose-Payment $mat;Show-Page 'Paiements'}
}
function Confirm-CorrectionPreview($plan) {
    $dialog=New-Object Windows.Forms.Form;Set-ITMWindowIcon $dialog;$dialog.Text='SCOLIA | Vérification de la correction';$dialog.Size=New-Object Drawing.Size(950,700);$dialog.MinimumSize=New-Object Drawing.Size(800,580);$dialog.StartPosition='CenterParent';$dialog.Font=$script:main.Font;$dialog.BackColor=$script:main.BackColor
    $footer=New-Object Windows.Forms.FlowLayoutPanel;$footer.Dock='Bottom';$footer.Height=64;$footer.Padding=New-Object Windows.Forms.Padding(18,12,0,0);$dialog.Controls.Add($footer)
    $yes=New-Button $footer 'Confirmer la correction' 0 0 310 $true;$yes.DialogResult='Yes'
    $no=New-Button $footer 'Revenir sans modifier' 0 0 310;$no.DialogResult='No';$dialog.CancelButton=$no
    $body=New-Object Windows.Forms.Panel;$body.Dock='Fill';$dialog.Controls.Add($body);$body.BringToFront()
    $summary='Reçu '+$plan.numero+' · '+$plan.matricule+"`r`nAncien montant : "+(Money $plan.ancien)+'     Nouveau montant : '+(Money $plan.nouveau)+"`r`nReçus recalculés : "+$plan.recusRecalcules
    [void](New-Label $body $summary 24 18 860 100 12 $true)
    [void](New-Label $body 'Répartition prévue de tous les reçus de cet apprenant. Vérifiez le tableau avant de confirmer.' 24 128 860 52 11)
    $grid=New-Grid $body 24 192 860 355;$grid.Anchor='Top,Bottom,Left,Right'
    Set-Grid $grid @($plan.repartition) @(@('Date','Date'),@('Numero','N° reçu'),@('Mois','Mois'),@('Montant','Montant (FC)'))
    Enable-ITMDialogResponsive $dialog
    try{$result=$dialog.ShowDialog($script:correctionDialog);return ($result -eq [Windows.Forms.DialogResult]::Yes)}finally{$dialog.Dispose()}
}
function Show-CorrectionPaiement {
    if($script:scolia){Assert-ITMPermission 'CorrigerPaiements';Show-Page 'Comptabilité';throw 'Sélectionnez le reçu dans le journal partagé puis utilisez Corriger / annuler le reçu. Pour un ancien reçu du secrétariat, importez-le dʼabord depuis la comptabilité.'}
    Assert-ITMPermission 'Administrer'
    if($script:payNeedsRefresh){throw 'Actualisez les données avant une correction.'}
    if($null -eq $script:book){throw 'Connectez un classeur.'}
    if($script:paymentGrid.SelectedRows.Count -ne 1){throw 'Sélectionnez une ligne de paiement dans son historique.'}
    $number=[string]$script:paymentGrid.SelectedRows[0].Cells['N° enregistrement'].Value
    if($script:scolia){$protected=Invoke-ITMJsonDirect 'ITMAppScoliaRecuProtege' @($number);if($protected.protected){throw 'Ce reçu provient du journal comptable partagé. Sa correction locale est bloquée pour conserver la cohérence des deux espaces.'}}
    $rows=@($script:data.payments | Where-Object {(Key $_.Numero) -eq (Key $number)})
    $total=[decimal]0;foreach($row in $rows){$total+=[decimal]$row.Verse}
    $dialog=New-Object Windows.Forms.Form;Set-ITMWindowIcon $dialog;$dialog.Text='SCOLIA | Corriger le paiement '+$number;$dialog.Size=New-Object Drawing.Size(730,620);$dialog.StartPosition='CenterParent';$dialog.Font=$script:main.Font;$dialog.BackColor=[Drawing.Color]::White
    [void](New-Label $dialog ('Paiement '+$number+' · montant actuel : '+(Money $total)) 24 20 660 50 13 $true)
    [void](New-Label $dialog 'Nouveau montant total du reçu (FC)' 24 90 650 25 10 $true)
    $amount=New-Object Windows.Forms.NumericUpDown;$amount.SetBounds(24,122,300,32);$amount.Maximum=100000000000;$amount.Minimum=1;$amount.ThousandsSeparator=$true;$amount.Value=[Math]::Max(1,$total);$dialog.Controls.Add($amount)
    [void](New-Label $dialog 'Motif de la correction (obligatoire)' 24 172 650 25 10 $true)
    $reason=New-Object Windows.Forms.TextBox;$reason.SetBounds(24,205,650,80);$reason.Multiline=$true;$reason.MaxLength=250;$dialog.Controls.Add($reason)
    $note=New-Label $dialog 'Les affectations de cet apprenant seront recalculées par date. Les numéros et dates des reçus sont conservés. Une sauvegarde et un historique seront créés.' 24 312 650 100 11
    $apply=New-Button $dialog 'Vérifier puis confirmer' 24 458 300 $true
    $cancel=New-Button $dialog 'Fermer' 340 458 250;$cancel.Add_Click({$script:correctionDialog.Close()})
    $script:correctionDialog=$dialog;$script:correctionAmount=$amount;$script:correctionReason=$reason;$script:correctionNumber=$number;$script:correctionDone=$false
    $apply.Add_Click({
        if($script:busy){return}
        Do-Work {
            $apiArguments=@([string]$script:correctionNumber,[double]$script:correctionAmount.Value,[string]$script:correctionReason.Text,$false)
            $plan=Invoke-ITMJson 'ITMAppCorrectionPaiement' $apiArguments
            if(-not (Confirm-CorrectionPreview $plan)){return}
            $apiArguments[3]=$true;$reply=Invoke-ITMJson 'ITMAppCorrectionPaiement' $apiArguments;Write-ITMUserAudit 'Correction de paiement' $script:correctionNumber
            $script:correctionDone=$true;$script:committedThisAction=$true;$script:payNeedsRefresh=$true;$script:payAmount.Value=0
            [void][ITMMessageBox]::Show($script:correctionDialog,'Correction enregistrée avec son motif.','ITM CD','OK','Information')
            Refresh-Data
        } 'Vérification et correction du paiement...'
        if($script:correctionDone){$script:correctionDialog.Close()}
    })
    $dialog.Add_FormClosing({param($sender,$e)if($script:busy){$e.Cancel=$true}})
    Enable-ITMDialogResponsive $dialog
    try{[void]$dialog.ShowDialog($script:main)}finally{$dialog.Dispose();$script:correctionDialog=$null}
}
function Refresh-Administration {
    if(-not $script:book -or -not $script:journalGrid){return}
    $script:admin=Invoke-ITMJson 'ITMAppAdministration'
    Refresh-ITMStoragePage
    Set-Grid $script:journalGrid @($admin.journal | Sort-Object Date -Descending) @(@('Date','Date'),@('Utilisateur','Utilisateur'),@('Action','Action'),@('Matricule','Matricule'),@('Numero','N°'),@('Motif','Motif'),@('Annee','Année'),@('Row','ID journal'))
    $script:journalGrid.Columns['ID journal'].Visible=$false
    Set-Grid $script:backupGrid @($admin.backups | Sort-Object Date -Descending) @(@('Nom','Nom'),@('Date','Date'),@('Taille','Taille (octets)'),@('Chemin','Chemin'))
    $script:admin.duplicates=@(Get-ITMDuplicateLearners $script:data.learners)
    Set-Grid $script:duplicatesGrid @($admin.duplicates) @(@('Matricule','Matricule'),@('AutreMatricule','Autre matricule'),@('Nom','Nom complet'),@('Observation','Observation'))
    Set-Grid $script:promotionsGrid @($data.promotions) @(@('Code','Code'),@('Designation','Désignation'),@('Annee','Année'),@('Mensuel','Mensualité'),@('Annuel','Total annuel'),@('Active','Active'))
    if(-not $script:nextYear.Text){$currentYear=[string](@($data.promotions | Where-Object {$_.Active}) | Select-Object -First 1).Annee;if($currentYear -match '^(\d{4})'){ $y=[int]$Matches[1]+1;$script:nextYear.Text=[string]$y+'-'+($y+1)}}
    $script:yearState.Text=$(if($script:data.closed){'Année clôturée : consultation disponible. Connectez le nouveau classeur pour saisir.'}else{'Année ouverte. La clôture conserve une archive et prépare un nouveau classeur sans paiements.'})
}
function Show-Promotion([string]$code='') {
    if($script:scolia){Show-ScoliaSettings;return}
    Assert-ITMPermission 'Administrer'
    if($script:payNeedsRefresh){throw 'Actualisez les données avant de modifier les frais.'}
    if(-not $script:book){throw 'Connectez un classeur.'}
    $old=@($script:data.promotions | Where-Object {$_.Code -eq $code});$old=$(if($old.Count -eq 1){$old[0]}else{$null})
    $dialog=New-Object Windows.Forms.Form;Set-ITMWindowIcon $dialog;$dialog.Text='SCOLIA | Promotion et frais';$dialog.Size=New-Object Drawing.Size(660,510);$dialog.StartPosition='CenterParent';$dialog.Font=$script:main.Font
    $fields=@{}
    $spec=@(@('Code','Code de promotion (4 caractères)'),@('Designation','Désignation'),@('Annee','Année scolaire (2026-2027)'),@('Mensuel','Mensualité (FC)'))
    for($i=0;$i -lt $spec.Count;$i++){
        [void](New-Label $dialog $spec[$i][1] 24 (20+$i*75) 580 24 10 $true)
        $box=New-Object Windows.Forms.TextBox;$box.SetBounds(24,(48+$i*75),580,30);$dialog.Controls.Add($box);$fields[$spec[$i][0]]=$box
        if($old){$box.Text=[string]$old.($spec[$i][0])}
    }
    $fields.Code.ReadOnly=[bool]$old;$fields.Code.MaxLength=4;$fields.Code.CharacterCasing='Upper'
    $active=New-Object ITMReadableCheckBox;$active.SetBounds(24,324,580,25);$active.Text='Promotion active';$active.Checked=$(if($old){[bool]$old.Active}else{$true});$dialog.Controls.Add($active)
    [void](New-Label $dialog 'Total annuel = 9 mensualités. Les frais des promotions avec paiements sont conservés.' 24 360 580 40 10)
    $save=New-Button $dialog 'Enregistrer' 24 415 580 $true;$script:promotionFields=$fields;$script:promotionActive=$active;$script:promotionDialog=$dialog;$script:promotionDone=$false
    $save.Add_Click({
        if($script:busy){return}
        Do-Work {
            $f=$script:promotionFields;$monthly=[decimal]0
            if(-not [decimal]::TryParse($f.Mensuel.Text,[Globalization.NumberStyles]::Number,$script:fr,[ref]$monthly)){throw 'Mensualité incorrecte.'}
            $reply=Invoke-ITMJson 'ITMAppPromotion' @($f.Code.Text,$f.Designation.Text,$f.Annee.Text,[double]$monthly,[double]($monthly*9),[bool]$script:promotionActive.Checked)
            Write-ITMUserAudit 'Promotion enregistrée' $f.Code.Text
            $script:promotionDone=$true;$script:committedThisAction=$true;$script:payNeedsRefresh=$true;Refresh-Data
        } 'Enregistrement de la promotion...'
        if($script:promotionDone){$script:promotionDialog.Close()}
    })
    $dialog.Add_FormClosing({param($sender,$e)if($script:busy){$e.Cancel=$true}})
    Enable-ITMDialogResponsive $dialog
    try{[void]$dialog.ShowDialog($script:main)}finally{$dialog.Dispose();$script:promotionDialog=$null}
}

function Format-ITMAudit([string]$text,[string]$action) {
    if(-not $text){return 'Aucune valeur antérieure.'}
    try{
        $values=@(('{"values":'+$text+'}' | ConvertFrom-Json -ErrorAction Stop).values)
        if($action -eq 'PAIEMENT' -and $values.Count -eq 1){return ('Montant enregistré : '+$text+' FC')}
        if($action -match 'PAIEMENT'){
            return (@($values | ForEach-Object {$v=@($_);$date=$(try{[DateTime]::FromOADate([double]$v[1]).ToString('dd/MM/yyyy')}catch{[string]$v[1]});'Date : '+$date+' | N° '+$v[2]+' | Matricule '+$v[3]+' | '+$v[6]+' | Montant : '+$v[8]+' FC | Reste annuel : '+$v[13]+' FC'}) -join "`r`n")
        }
        $labels=$(if($action -eq 'PROMOTION'){@('Code','Désignation','Section','Niveau','Année','Mensualité','Total annuel','Activation')}else{@('Matricule','Nom','Post-nom','Prénom','Sexe','Naissance','Lieu','Promotion','Parent / tuteur','Contact 1','Contact 2','Inscription','Année','Statut','Adresse')})
        $lines=@();for($i=0;$i -lt [Math]::Min($labels.Count,$values.Count);$i++){$value=$values[$i];if($action -ne 'PROMOTION' -and $i -in @(5,11) -and $null -ne $value){try{$value=[DateTime]::FromOADate([double]$value).ToString('dd/MM/yyyy')}catch{}};$lines+=($labels[$i]+' : '+$value)}
        return $lines -join "`r`n"
    }catch{return $text}
}

function Confirm-ITMReportPreview($preview,[string]$title,[string]$month,[string]$promotion) {
    $dialog=New-Object Windows.Forms.Form;Set-ITMWindowIcon $dialog
    $dialog.Text='SCOLIA | Vérifier le rapport';$dialog.Size=New-Object Drawing.Size(760,650)
    $dialog.StartPosition='CenterParent';$dialog.Font=$script:main.Font;$dialog.BackColor=[Drawing.Color]::White
    $header=New-Object Windows.Forms.Panel;$header.Dock='Top';$header.Height=112;$header.BackColor=[Drawing.Color]::White
    $heading=New-Object ITMReadableLabel;$heading.Dock='Fill';$heading.Padding=New-Object Windows.Forms.Padding(22,16,22,10)
    $heading.Text="Vérifier avant de générer`r`n"+$title;$heading.ForeColor=[Drawing.Color]::FromArgb(24,64,120);$heading.Font=New-Object Drawing.Font('Segoe UI',16,[Drawing.FontStyle]::Bold);$header.Controls.Add($heading)
    $footer=New-Object Windows.Forms.FlowLayoutPanel;$footer.Dock='Bottom';$footer.Height=68;$footer.Padding=New-Object Windows.Forms.Padding(12);$footer.FlowDirection='RightToLeft';$footer.BackColor=[Drawing.Color]::White
    $confirm=New-Button $footer 'Continuer vers la destination' 0 0 270 $true;$confirm.DialogResult='OK'
    $cancel=New-Button $footer 'Revenir aux filtres' 0 0 200;$cancel.DialogResult='Cancel';$dialog.CancelButton=$cancel
    $body=New-Object Windows.Forms.Panel;$body.Dock='Fill';$body.AutoScroll=$true
    $scope=$(if($promotion){$promotion}else{'Toutes les promotions'})
    [void](New-Label $body ($scope+' | '+$(if($month){$month}else{'Année scolaire du classeur'})) 20 16 680 42 11 $true)
    $count=New-Label $body ([string]$preview.rows+' dossier(s) retenu(s)') 20 62 680 36 18 $true
    $count.ForeColor=[Drawing.Color]::FromArgb(24,64,120)
    $note=New-Label $body ([string]$preview.issues+' observation(s) dans le diagnostic. Vérifiez les anomalies avant de communiquer ce rapport.') 20 108 680 64 10
    if([int]$preview.issues -gt 0){$note.ForeColor=[Drawing.Color]::FromArgb(146,64,14)}
    if([int]$preview.rows -eq 0){$note.Text="Aucun dossier retenu. Le fichier contiendra uniquement le diagnostic des anomalies.";$confirm.Text='Exporter le diagnostic'}
    $grid=New-Grid $body 20 180 680 230
    Set-Grid $grid @($preview.groups) @(@('Promotion','Promotion'),@('Dossiers','Dossiers retenus'))
    $dialog.Controls.Add($body);$dialog.Controls.Add($footer);$dialog.Controls.Add($header)
    Enable-ITMDialogResponsive $dialog
    $wait=$script:main.UseWaitCursor;$script:main.UseWaitCursor=$false
    try{return ($dialog.ShowDialog($script:main) -eq 'OK')}finally{$script:main.UseWaitCursor=$wait;$dialog.Dispose()}
}
