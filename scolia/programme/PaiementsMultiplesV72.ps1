# Paiements multiples V72
# Un lot orchestre plusieurs paiements, mais chaque écriture conserve sa propre
# validation, sa propre référence et sa propre répartition mensuelle.

function Get-ITMBatchLearner([string]$mat) {
    $matches=$(if(Test-Path function:Get-ITMLearnerMatches){@(Get-ITMLearnerMatches $mat)}else{@($script:data.learners | Where-Object {(Key $_.Matricule) -eq (Key $mat)})})
    if($matches.Count -ne 1 -or -not $matches[0].Eligible){throw 'Dossier invalide, absent ou en double : '+$mat}
    return $matches[0]
}

function Get-ITMBatchCandidate([object]$learner,[decimal]$suggested=0) {
    $fee=$script:fees[(Promo $learner.Promotion)]
    if(-not $fee){throw 'Tarif introuvable pour la promotion '+$learner.Promotion+'.'}
    $lateMonths=New-Object 'System.Collections.Generic.List[string]'
    $lateDebt=[decimal]0
    foreach($month in @(Due-Months $learner)){
        $due=[decimal](Get-ITMPECDue $learner $month)
        if($due -gt 0){$lateMonths.Add([string]$month);$lateDebt+=$due}
    }
    $payable=[decimal](Get-ITMPECPayingBalance $learner)
    $lateDebt=[Math]::Min($lateDebt,$payable)
    $firstDue=[decimal]0
    foreach($month in @($script:data.months)){
        $due=[decimal](Get-ITMPECDue $learner ([string]$month))
        if($due -gt 0){$firstDue=[Math]::Min($due,$payable);break}
    }
    if($suggested -gt $payable){$suggested=$payable}
    return [pscustomobject]@{
        Matricule=[string]$learner.Matricule
        Nom=(($learner.Nom+' '+$learner.PostNom+' '+$learner.Prenom).Trim())
        Promotion=[string]$learner.Promotion
        Periode=$(if($lateMonths.Count){$lateMonths -join ', '}else{'À jour'})
        MoisEnRetard=[int]$lateMonths.Count
        DetteExigible=[decimal]$lateDebt
        RestePayant=[decimal]$payable
        PremiereEcheance=[decimal]$firstDue
        MontantSuggere=[decimal]$suggested
    }
}

function Get-ITMBatchCandidates([object[]]$PresetRows=@()) {
    if(-not $script:data -or -not $script:book){throw 'Connectez les données avant de préparer des paiements multiples.'}
    if($script:data.closed){throw 'Cette année scolaire est clôturée.'}
    $preset=@{}
    foreach($row in @($PresetRows)){
        if($null -eq $row){continue}
        $mat=[string]$row.Matricule
        if(-not $mat){continue}
        $value=[decimal]0
        try{if($null -ne $row.DetteValeur){$value=[decimal]$row.DetteValeur}}catch{}
        $preset[(Key $mat)]=$value
    }
    $learners=@($script:data.learners | Where-Object {$_.Eligible})
    if($preset.Count){$learners=@($learners | Where-Object {$preset.ContainsKey((Key $_.Matricule))})}
    $rows=New-Object 'System.Collections.Generic.List[object]'
    foreach($learner in @($learners | Sort-Object Promotion,Nom,PostNom,Prenom,Matricule)){
        $suggested=[decimal]0
        if($preset.ContainsKey((Key $learner.Matricule))){$suggested=[decimal]$preset[(Key $learner.Matricule)]}
        $rows.Add((Get-ITMBatchCandidate $learner $suggested))
    }
    return $rows.ToArray()
}

function New-ITMBatchGrid($parent) {
    $grid=New-Object Windows.Forms.DataGridView
    $grid.Dock='Fill';$grid.ReadOnly=$false;$grid.AllowUserToAddRows=$false;$grid.AllowUserToDeleteRows=$false;$grid.AllowUserToOrderColumns=$true
    $grid.MultiSelect=$false;$grid.SelectionMode='FullRowSelect';$grid.RowHeadersVisible=$false;$grid.AutoGenerateColumns=$false;$grid.BackgroundColor=[Drawing.Color]::White
    $grid.BorderStyle='None';$grid.GridColor=[Drawing.Color]::FromArgb(230,236,245);$grid.EnableHeadersVisualStyles=$false
    $grid.ScrollBars='Both';$grid.AutoSizeColumnsMode='None';$grid.AutoSizeRowsMode='None'
    $grid.ColumnHeadersDefaultCellStyle.BackColor=[Drawing.Color]::FromArgb(231,238,248);$grid.ColumnHeadersDefaultCellStyle.ForeColor=[Drawing.Color]::FromArgb(0,102,204)
    $grid.ColumnHeadersDefaultCellStyle.Font=New-Object Drawing.Font('Segoe UI',10,[Drawing.FontStyle]::Bold);$grid.ColumnHeadersHeight=44
    $grid.DefaultCellStyle.Font=New-Object Drawing.Font('Segoe UI',10);$grid.DefaultCellStyle.Padding=New-Object Windows.Forms.Padding(6,4,6,4);$grid.RowTemplate.Height=38
    $grid.DefaultCellStyle.SelectionBackColor=[Drawing.Color]::FromArgb(217,231,253);$grid.DefaultCellStyle.SelectionForeColor=[Drawing.Color]::FromArgb(0,102,204)
    $grid.AlternatingRowsDefaultCellStyle.BackColor=[Drawing.Color]::FromArgb(247,249,253)
    $check=New-Object Windows.Forms.DataGridViewCheckBoxColumn;$check.Name='Choisir';$check.HeaderText='';$check.Width=52;$check.Frozen=$true;[void]$grid.Columns.Add($check)
    foreach($def in @(
        @('Matricule','Matricule',95),@('Nom','Nom complet',220),@('Promotion','Classe',88),@('Periode','Mois en retard',155),
        @('Dette','Reste exigible',118),@('Reste','Reste payant',118),@('Montant','Montant à encaisser',132),@('Resultat','Résultat',250)
    )){
        $col=New-Object Windows.Forms.DataGridViewTextBoxColumn;$col.Name=$def[0];$col.HeaderText=$def[1];$col.Width=[int]$def[2];$col.SortMode='Automatic'
        $col.ReadOnly=($def[0] -ne 'Montant');if($def[0] -in @('Dette','Reste','Montant')){$col.DefaultCellStyle.Alignment='MiddleRight'}
        if($def[0] -eq 'Resultat'){$col.AutoSizeMode='Fill';$col.MinimumWidth=250}
        [void]$grid.Columns.Add($col)
    }
    $parent.Controls.Add($grid);return $grid
}

function Invoke-ScoliaBatchTuitionPayment($learner,[decimal]$amount,[datetime]$date) {
    Assert-ITMPermission 'Encaisser'
    if(-not $script:scolia -or -not $script:scoliaSession){throw 'Session SCOLIA indisponible.'}
    $pendingPath=Join-Path $script:scoliaLocalRoot 'operation-caisse-en-attente.json'
    if([IO.File]::Exists($pendingPath)){throw 'Une écriture attend une vérification. Utilisez Vérifier opération en attente avant de poursuivre le lot.'}
    $identity=New-ScoliaReceiptIdentity
    $entry=New-Object ScoliaEntry;$entry.Id=$identity.Id;$entry.SchoolId=$script:scolia.Id;$entry.Kind='Paiement';$entry.Annee=$script:scolia.Year
    $entry.Reference=$identity.Reference;$entry.Date=$date.ToString('yyyy-MM-dd',[Globalization.CultureInfo]::InvariantCulture);$entry.Montant=$amount
    $entry.Compte=Get-ScoliaPaymentAccount;$entry.OperatorId=$script:scoliaSession.AccountId;$entry.OperatorName=$script:scoliaSession.Name;$entry.ConfigRevision=$script:scoliaSession.Revision
    $entry.Matricule=[string]$learner.Matricule;$entry.Nom=[string]$learner.Nom;$entry.PostNom=[string]$learner.PostNom;$entry.Prenom=[string]$learner.Prenom;$entry.Promotion=[string]$learner.Promotion
    $entry.Sexe=[string]$learner.Sexe;$entry.Naissance=[string]$learner.Naissance;$entry.Lieu=[string]$learner.Lieu;$entry.Contact1=[string]$learner.Contact1;$entry.Contact2=[string]$learner.Contact2;$entry.Adresse=[string]$learner.Adresse
    $entry.CommonReceipt='1';$entry.Category='SCOLARITE';$entry.CategoryName='Scolarité · mensualités'
    [ScoliaLedger]::ValidateDraft($entry,$script:scolia.Id)
    $class=@($script:scolia.Classes | Where-Object {(Promo $_.Code) -eq (Promo $learner.Promotion)})
    if($class.Count -ne 1){throw 'Tarif SCOLIA introuvable ou ambigu pour '+$learner.Promotion+'.'}
    $fee=[decimal]$class[0].Fee
    Write-ScoliaJson $pendingPath ([pscustomobject]@{Id=$entry.Id;SchoolId=$script:scolia.Id;Root=$script:scoliaRoot;Reference=$entry.Reference;Montant=$entry.Montant;Date=$entry.Date})
    $saved=$null
    try{
        $commitTask=[ScoliaLedger]::CommitAsync($script:scoliaRoot,$script:scolia.Id,$entry,[int]$script:scolia.StartMonth,[int]$script:scolia.Months,$fee)
        $saved=Wait-ScoliaNetworkTask $commitTask ('Enregistrement du reçu '+$entry.Reference+'…')
        if([IO.File]::Exists($pendingPath)){[IO.File]::Delete($pendingPath)}
    }catch{
        $cause=$_.Exception.GetBaseException()
        $definite=($cause -is [InvalidOperationException] -or $cause -is [ArgumentException] -or $cause -is [IO.InvalidDataException])
        if($definite -and [IO.File]::Exists($pendingPath)){[IO.File]::Delete($pendingPath)}
        throw $cause
    }
    $script:scoliaLastReceipt=$saved
    $script:scoliaEntries=@($script:scoliaEntries)+@($saved)
    $warning=''
    if((Test-ScoliaPermission 'Recevoir') -and $script:book){
        try{[void](Import-ScoliaReceipt $saved $false)}catch{$warning='Journal enregistré ; import au secrétariat à reprendre : '+$_.Exception.Message}
    }
    Write-ITMUserAudit 'Paiement multiple SCOLIA' $saved.Id
    $script:scoliaLedgerStamp=''
    return [pscustomobject]@{Reference=[string]$saved.Reference;Montant=[decimal]$saved.Montant;Mois=@($saved.Parts).Count;Warning=$warning}
}

function Invoke-ITMBatchPaymentItem($learner,[decimal]$amount,[datetime]$date) {
    if($amount -le 0){throw 'Montant nul ou invalide.'}
    $balance=[decimal](Get-ITMPECPayingBalance $learner)
    if($balance -le 0){throw 'Ce dossier ne présente plus de reste à payer.'}
    if($amount -gt $balance){throw 'Le montant dépasse le reste payant actuel ('+(Money $balance)+').'}
    if($script:scolia){return Invoke-ScoliaBatchTuitionPayment $learner $amount $date}
    $raw=Invoke-ITMPayment ([string]$learner.Matricule) ([double]$amount) ($date.ToString('yyyy-MM-dd',[Globalization.CultureInfo]::InvariantCulture))
    $reply=([string]$raw | ConvertFrom-Json)
    if(-not $reply.ok){throw [string]$reply.error}
    [void](Update-ITMAfterWrite ([string]$learner.Matricule) $true)
    Write-ITMUserAudit 'Paiement multiple enregistré' ([string]$reply.numero)
    return [pscustomobject]@{Reference=[string]$reply.numero;Montant=[decimal]$reply.montant;Mois=[int]$reply.mois;Warning=''}
}

function Show-ITMBatchPayment([object[]]$PresetRows=@(),[string]$Source='Paiements') {
    Assert-ITMPermission $(if($script:scolia){'Encaisser'}else{'Saisie'})
    $candidates=@(Get-ITMBatchCandidates $PresetRows)
    if(-not $candidates.Count){throw 'Aucun apprenant disponible dans cette sélection.'}
    $d=New-Object Windows.Forms.Form;$d.Text='Paiements multiples';$d.StartPosition='CenterParent';$d.MinimumSize=New-Object Drawing.Size(1040,700);$d.Size=New-Object Drawing.Size(1280,800)
    $d.FormBorderStyle='Sizable';$d.MaximizeBox=$true;$d.MinimizeBox=$false;$d.ShowIcon=$false;$d.AutoScaleMode='Dpi'
    $header=New-Object Windows.Forms.Panel;$header.Dock='Top';$header.Height=134;$header.Padding=New-Object Windows.Forms.Padding(20,14,20,10);$d.Controls.Add($header)
    $title=New-Label $header 'Paiements multiples' 0 0 700 32 16 $true;$title.Dock='Top'
    $subtitle=New-Label $header 'Chaque ligne est enregistrée séparément : montant, reçu, mois affectés et résultat restent propres à chaque apprenant.' 0 0 980 42 10;$subtitle.Dock='Top'
    $filterBar=New-Object Windows.Forms.FlowLayoutPanel;$filterBar.Dock='Bottom';$filterBar.Height=52;$filterBar.WrapContents=$true;$filterBar.AutoScroll=$true;$filterBar.Padding=New-Object Windows.Forms.Padding(0,4,0,0);$header.Controls.Add($filterBar)
    $searchLabel=New-Object ITMReadableLabel;$searchLabel.Text='Recherche';$searchLabel.AutoSize=$true;$searchLabel.Margin=New-Object Windows.Forms.Padding(0,8,8,0);$filterBar.Controls.Add($searchLabel)
    $search=New-Object Windows.Forms.TextBox;$search.Width=340;$search.CharacterCasing='Upper';$search.Margin=New-Object Windows.Forms.Padding(0,4,16,0);$filterBar.Controls.Add($search)
    $summary=New-Object ITMReadableLabel;$summary.AutoSize=$true;$summary.Margin=New-Object Windows.Forms.Padding(0,8,0,0);$summary.MaximumSize=New-Object Drawing.Size(520,0);$filterBar.Controls.Add($summary)
    $body=New-Object Windows.Forms.Panel;$body.Dock='Fill';$body.Padding=New-Object Windows.Forms.Padding(18,8,18,8);$d.Controls.Add($body)
    $grid=New-ITMBatchGrid $body
    $footer=New-Object Windows.Forms.Panel;$footer.Dock='Bottom';$footer.Height=168;$footer.Padding=New-Object Windows.Forms.Padding(18,10,18,14);$d.Controls.Add($footer)
    $tools=New-Object Windows.Forms.FlowLayoutPanel;$tools.Dock='Top';$tools.Height=74;$tools.WrapContents=$true;$tools.AutoScroll=$true;$tools.Padding=New-Object Windows.Forms.Padding(0,0,0,4);$footer.Controls.Add($tools)
    $selectVisible=New-Button $tools 'Sélectionner les visibles' 0 0 190;$selectVisible.Margin=New-Object Windows.Forms.Padding(0,0,8,8)
    $clearSelection=New-Button $tools 'Tout décocher' 0 0 150;$clearSelection.Margin=New-Object Windows.Forms.Padding(0,0,8,8)
    $fillLate=New-Button $tools 'Montant = reste exigible' 0 0 210;$fillLate.Margin=New-Object Windows.Forms.Padding(0,0,8,8)
    $fillOne=New-Button $tools 'Montant = prochaine échéance' 0 0 230;$fillOne.Margin=New-Object Windows.Forms.Padding(0,0,8,8)
    $dateLabel=New-Object ITMReadableLabel;$dateLabel.Text='Date du reçu';$dateLabel.AutoSize=$true;$dateLabel.Margin=New-Object Windows.Forms.Padding(12,8,8,0);$tools.Controls.Add($dateLabel)
    $date=New-Object Windows.Forms.DateTimePicker;$date.Format='Custom';$date.CustomFormat='dd/MM/yyyy';$date.MaxDate=(Get-Date).Date;$date.Value=(Get-Date).Date;$date.Enabled=[bool]($script:scolia -or $script:paymentDatesAvailable);$date.Width=120;$date.Margin=New-Object Windows.Forms.Padding(0,4,0,8);$tools.Controls.Add($date)
    $noticePanel=New-Object Windows.Forms.Panel;$noticePanel.Dock='Top';$noticePanel.Height=34;$noticePanel.Padding=New-Object Windows.Forms.Padding(0,6,0,0);$footer.Controls.Add($noticePanel)
    $notice=New-Object ITMReadableLabel;$notice.Text='Les lignes réussies sont décochées ; une ligne en erreur reste sélectionnée pour correction.';$notice.Dock='Fill';$notice.MaximumSize=New-Object Drawing.Size(0,32);$noticePanel.Controls.Add($notice)
    $actions=New-Object Windows.Forms.FlowLayoutPanel;$actions.Dock='Bottom';$actions.Height=48;$actions.FlowDirection='RightToLeft';$actions.WrapContents=$false;$footer.Controls.Add($actions)
    $close=New-Button $actions 'Fermer' 0 0 130;$close.Margin=New-Object Windows.Forms.Padding(8,0,0,0)
    $save=New-Button $actions 'Enregistrer la sélection' 0 0 220 $true;$save.Margin=New-Object Windows.Forms.Padding(8,0,0,0)

    Enable-ScoliaBatchPaymentLayout $d @{Header=$header;Title=$title;Subtitle=$subtitle;Filters=$filterBar;Search=$search;Summary=$summary;Body=$body;Grid=$grid;Footer=$footer;Tools=$tools;NoticePanel=$noticePanel;Notice=$notice;Actions=$actions}

    $models=@{}
    foreach($item in $candidates){
        $index=$grid.Rows.Add($false,$item.Matricule,$item.Nom,$item.Promotion,$item.Periode,(Money $item.DetteExigible),(Money $item.RestePayant),$(if($item.MontantSuggere -gt 0){$item.MontantSuggere.ToString('0',$script:fr)}else{''}),'')
        $row=$grid.Rows[$index];$row.Tag=$item;$row.HeaderCell.Tag=Key (([string]$item.Matricule)+' '+([string]$item.Nom)+' '+([string]$item.Promotion)+' '+([string]$item.Periode));$models[(Key $item.Matricule)]=$item
    }
    $refreshSummary={
        $count=0;$total=[decimal]0
        foreach($row in $grid.Rows){
            if([bool]$row.Cells['Choisir'].Value){$count++;$value=[decimal]0;if([decimal]::TryParse(([string]$row.Cells['Montant'].Value),[Globalization.NumberStyles]::Number,$script:fr,[ref]$value)){$total+=$value}}
        }
        $summary.Text=$count.ToString()+' sélectionné(s) · total saisi : '+(Money $total)
        $save.Enabled=($count -gt 0 -and -not $script:busy)
    }
    $filter={
        $term=Key $search.Text;$grid.CurrentCell=$null;$grid.ClearSelection()
        foreach($row in $grid.Rows){
            $hay=[string]$row.HeaderCell.Tag
            $row.Visible=(-not $term -or $hay.Contains($term))
        }
        & $refreshSummary
    }
    $filterTimer=New-Object Windows.Forms.Timer;$filterTimer.Interval=45
    $filterTimer.Add_Tick({$filterTimer.Stop();& $filter}.GetNewClosure())
    $search.Add_TextChanged({$filterTimer.Stop();$filterTimer.Start()}.GetNewClosure())
    $d.Add_Disposed({$filterTimer.Stop();$filterTimer.Dispose()}.GetNewClosure())
    $grid.Add_CurrentCellDirtyStateChanged({if($grid.IsCurrentCellDirty){$grid.CommitEdit([Windows.Forms.DataGridViewDataErrorContexts]::Commit)}})
    $grid.Add_CellValueChanged({param($sender,$e)if($e.RowIndex -ge 0 -and $e.ColumnIndex -ge 0){& $refreshSummary}})
    $grid.Add_DataError({param($sender,$e)$e.ThrowException=$false})
    $selectVisible.Add_Click({foreach($row in $grid.Rows){if($row.Visible){$row.Cells['Choisir'].Value=$true}};& $refreshSummary})
    $clearSelection.Add_Click({foreach($row in $grid.Rows){$row.Cells['Choisir'].Value=$false};& $refreshSummary})
    $fillLate.Add_Click({foreach($row in $grid.Rows){if($row.Visible){$row.Cells['Montant'].Value=([decimal]$row.Tag.DetteExigible).ToString('0',$script:fr)}};& $refreshSummary})
    $fillOne.Add_Click({foreach($row in $grid.Rows){if($row.Visible){$row.Cells['Montant'].Value=([decimal]$row.Tag.PremiereEcheance).ToString('0',$script:fr)}};& $refreshSummary})
    $close.Add_Click({if(-not $script:busy){$d.Close()}});$d.CancelButton=$close
    $save.Add_Click({
        if($script:busy){return}
        try{
            $items=New-Object 'System.Collections.Generic.List[object]'
            foreach($row in $grid.Rows){
                if(-not [bool]$row.Cells['Choisir'].Value){continue}
                $typed=[decimal]0
                if(-not [decimal]::TryParse(([string]$row.Cells['Montant'].Value),[Globalization.NumberStyles]::Number,$script:fr,[ref]$typed) -or $typed -le 0 -or [decimal]::Truncate($typed) -ne $typed){throw 'Montant invalide pour '+[string]$row.Cells['Matricule'].Value+'. Saisissez un montant entier positif.'}
                $learner=Get-ITMBatchLearner ([string]$row.Cells['Matricule'].Value);$balance=[decimal](Get-ITMPECPayingBalance $learner)
                if($typed -gt $balance){throw 'Le montant saisi pour '+$learner.Matricule+' dépasse son reste payant actuel ('+(Money $balance)+').'}
                $items.Add([pscustomobject]@{Row=$row;Learner=$learner;Amount=$typed})
            }
            if(-not $items.Count){throw 'Cochez au moins un apprenant.'}
            if($date.Value.Date -gt (Get-Date).Date){throw 'La date du reçu ne peut pas être future.'}
            if(-not $script:scolia){
                $similar=New-Object 'System.Collections.Generic.List[string]'
                foreach($item in $items){
                    $arguments=@([string]$item.Learner.Matricule,[double]$item.Amount);if($script:paymentDatesAvailable){$arguments+=($date.Value.ToString('yyyy-MM-dd',[Globalization.CultureInfo]::InvariantCulture))}
                    $dups=@(Invoke-ITMJson 'ITMAppDoublonsPaiement' $arguments)
                    if($dups.Count){$similar.Add($item.Learner.Matricule+' ('+(@($dups|ForEach-Object {$_.Numero}) -join ', ')+')')}
                }
                if($similar.Count){
                    $text='Des paiements du même montant existent déjà à cette date pour : '+($similar -join '; ')+".`r`n`r`nContinuer malgré ces ressemblances ?"
                    if([ITMMessageBox]::Show($d,$text,'Doublons possibles','YesNo','Warning') -ne 'Yes'){return}
                }
            }
            $total=[decimal](@($items|Measure-Object Amount -Sum).Sum)
            $question='Enregistrer '+$items.Count+' paiement(s) séparés pour un total de '+(Money $total)+' à la date du '+$date.Value.ToString('dd/MM/yyyy')+' ?'
            if([ITMMessageBox]::Show($d,$question,'Confirmer les paiements multiples','YesNo','Question') -ne 'Yes'){return}
            if($script:scolia -and (Get-ScoliaPaymentAccount) -eq 'Secrétariat'){
                Sync-ScoliaRosterForPayment
            }
            $success=0;$failed=0;$warnings=0;$stopForPending=$false
            Do-Work {
                foreach($item in $items){
                    $row=$item.Row;$row.Cells['Resultat'].Value='Traitement...';[Windows.Forms.Application]::DoEvents()
                    try{
                        $result=Invoke-ITMBatchPaymentItem $item.Learner $item.Amount $date.Value.Date
                        $success++;$script:committedThisAction=$true
                        $message='OK · '+$result.Reference+' · '+$result.Mois+' mois'
                        if($result.Warning){$warnings++;$message+=' · '+$result.Warning}
                        $row.Cells['Resultat'].Value=$message;$row.Cells['Choisir'].Value=$false
                    }catch{
                        $failed++;$row.Cells['Resultat'].Value='Erreur · '+$_.Exception.Message
                        if($script:scolia -and [IO.File]::Exists((Join-Path $script:scoliaLocalRoot 'operation-caisse-en-attente.json'))){$stopForPending=$true;break}
                    }
                }
                if($script:scolia -and $script:currentPage -eq 'Comptabilité'){Refresh-ScoliaFinance}
                Refresh-ITMCurrentPage
            } 'Enregistrement des paiements multiples...'
            if($stopForPending){
                $notice.Text="Lot arrêté : vérifiez l'opération réseau en attente avant de reprendre.";[void][ITMMessageBox]::Show($d,"Le lot a été arrêté car une écriture réseau doit être vérifiée. Les paiements déjà marqués OK sont conservés. Utilisez Données > Vérifier opération en attente avant de reprendre les lignes restantes.",'Paiements multiples','OK','Warning')
            }else{
                $text=$success.ToString()+' paiement(s) enregistré(s) · '+$failed.ToString()+' erreur(s)'
                if($warnings){$text+=' · '+$warnings.ToString()+' import(s) à reprendre'}
                $notice.Text=$text
                if($success){[ITMSuccessToast]::Show($d,'Paiements multiples',$text)}
            }
            & $refreshSummary
        }catch{[void][ITMMessageBox]::Show($d,$_.Exception.Message,'Paiements multiples','OK','Warning')}
    })
    if($Source -eq 'Retards'){$subtitle.Text='Sélection issue de « Retards et contacts ». Les restes exigibles sont proposés ligne par ligne et restent modifiables avant validation.'}
    Apply-ITMThemeForm $d;& $refreshSummary
    try{[void]$d.ShowDialog($script:main)}finally{$d.Dispose()}
}

# Presentation only: no reads or writes to learner/payment records.
function Set-ScoliaMeasuredFlow($Flow,[int]$Width,[double]$Scale){
 $Flow.AutoSize=$false;$Flow.AutoScroll=$false;$Flow.WrapContents=$true
 $Flow.MaximumSize=New-Object Drawing.Size(0,0);$Flow.Width=[Math]::Max(40,$Width)
 Update-ScoliaFlowSpacing $Flow
 $height=$Flow.GetPreferredSize((New-Object Drawing.Size($Flow.Width,0))).Height
 $Flow.Height=[Math]::Max([int](40*$Scale),$height)
 $Flow.PerformLayout()
 $bottom=$Flow.Padding.Top
 foreach($child in $Flow.Controls){if($child.Visible){$bottom=[Math]::Max($bottom,$child.Bottom+$child.Margin.Bottom)}}
 $Flow.Height=[Math]::Max($Flow.Height,$bottom+$Flow.Padding.Bottom)
}
function Update-ScoliaBatchPaymentLayout($Form){
 $s=$Form.Tag
 if(-not $s -or -not $s.PSObject.Properties['BatchViewport'] -or $s.LayoutBusy -or $Form.IsDisposed){return}
 $s.LayoutBusy=$true;$s.Canvas.SuspendLayout();$saved=$s.BatchViewport.AutoScrollPosition
 try{
  $s.BatchViewport.AutoScrollPosition=New-Object Drawing.Point(0,0)
  $q=Get-ITMDpiScale $Form;$pad=[int](18*$q);$gap=[int](10*$q)
  $width=[Math]::Max(80,$s.BatchViewport.ClientSize.Width-[Windows.Forms.SystemInformation]::VerticalScrollBarWidth)
  $inner=[Math]::Max(40,$width-2*$pad)
  $s.Header.SetBounds(0,0,$width,1);$y=$pad
  foreach($label in @($s.Title,$s.Subtitle)){
   $label.AutoSize=$false;$h=Get-ITMTextHeight $label $inner ([int](26*$q))
   $label.SetBounds($pad,$y,$inner,$h);$y+=$h+[int](4*$q)
  }
  $s.Search.Width=[Math]::Min([int](340*$q),[Math]::Max(40,$inner-$s.Search.Margin.Horizontal))
  Set-ScoliaMeasuredFlow $s.Filters $inner $q
  $s.Filters.Location=New-Object Drawing.Point($pad,($y+$gap))
  $s.Header.Height=$s.Filters.Bottom+$gap
  Set-ScoliaMeasuredFlow $s.Tools $inner $q
  $s.Tools.Location=New-Object Drawing.Point($pad,$gap)
  $s.Notice.MaximumSize=New-Object Drawing.Size(0,0);$s.Notice.AutoSize=$false
  $noticeHeight=Get-ITMTextHeight $s.Notice $inner ([int](28*$q))
  $s.NoticePanel.SetBounds($pad,($s.Tools.Bottom+$gap),$inner,($noticeHeight+$s.NoticePanel.Padding.Vertical))
  Set-ScoliaMeasuredFlow $s.Actions $inner $q
  $s.Actions.Location=New-Object Drawing.Point($pad,($s.NoticePanel.Bottom+$gap))
  $s.Footer.Size=New-Object Drawing.Size($width,($s.Actions.Bottom+$pad))
  $gridHeight=[Math]::Max([int](260*$q),$s.BatchViewport.ClientSize.Height-$s.Header.Height-$s.Footer.Height)
  $s.Body.SetBounds(0,$s.Header.Bottom,$width,$gridHeight)
  $s.Footer.Location=New-Object Drawing.Point(0,$s.Body.Bottom)
  $s.Canvas.Size=New-Object Drawing.Size($width,$s.Footer.Bottom)
  $s.BatchViewport.AutoScrollMinSize=New-Object Drawing.Size(0,$s.Canvas.Height)
 }finally{$s.Canvas.ResumeLayout($true);$s.BatchViewport.AutoScrollPosition=New-Object Drawing.Point([Math]::Abs($saved.X),[Math]::Abs($saved.Y));$s.LayoutBusy=$false}
}
function Enable-ScoliaBatchPaymentLayout($Form,$Parts){
 $viewport=New-Object Windows.Forms.Panel;$viewport.Dock='Fill';$viewport.AutoScroll=$true
 $canvas=New-Object Windows.Forms.Panel;$canvas.Location=New-Object Drawing.Point(0,0)
 foreach($part in @($Parts.Header,$Parts.Body,$Parts.Footer)){$part.Dock='None';$part.Anchor='Top,Left';$canvas.Controls.Add($part)}
 foreach($part in @($Parts.Title,$Parts.Subtitle,$Parts.Filters,$Parts.Tools,$Parts.NoticePanel,$Parts.Actions)){$part.Dock='None';$part.Anchor='Top,Left'}
 $viewport.Controls.Add($canvas);$Form.Controls.Add($viewport)
 $Parts.BatchViewport=$viewport;$Parts.Canvas=$canvas;$Parts.LayoutBusy=$false;$Form.Tag=[pscustomobject]$Parts
 $Form.MinimumSize=New-Object Drawing.Size(360,380)
 $Form.Add_SizeChanged({param($sender,$e)Update-ScoliaBatchPaymentLayout $sender})
 $Form.Add_Shown({param($sender,$e)Update-ScoliaBatchPaymentLayout $sender})
 foreach($label in @($Parts.Title,$Parts.Subtitle,$Parts.Notice,$Parts.Summary)){
  $label.Add_TextChanged({param($sender,$e)$owner=$sender.FindForm();if($owner){Update-ScoliaBatchPaymentLayout $owner}})
  $label.Add_FontChanged({param($sender,$e)$owner=$sender.FindForm();if($owner){Update-ScoliaBatchPaymentLayout $owner}})
 }
 Update-ScoliaBatchPaymentLayout $Form
}
