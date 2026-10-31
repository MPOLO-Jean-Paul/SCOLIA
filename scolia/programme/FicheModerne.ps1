function New-ITMFicheCard($parent,[string]$title,$pairs) {
    $card=New-Object ITMSectionPanel;$card.BackColor=[Drawing.Color]::White;$parent.Controls.Add($card)
    $heading=New-Label $card $title 18 16 600 32 12 $true;$heading.BackColor=[Drawing.Color]::White;$heading.ForeColor=[Drawing.Color]::FromArgb(29,67,130)
    $rows=@()
    foreach($pair in $pairs){
        $label=New-Label $card ([string]$pair[0]) 18 60 180 30 10 $true;$label.BackColor=[Drawing.Color]::White
        $value=[string]$pair[1];if(-not $value.Trim()){$value='Non renseigné'}
        $text=New-Label $card $value 210 60 380 30 11;$text.BackColor=[Drawing.Color]::White
        $rows+=([pscustomobject]@{Label=$label;Value=$text})
    }
    return [pscustomobject]@{Card=$card;Title=$heading;Rows=$rows}
}
function Build-ITMModernFiche($dialog,$body,$a,[string]$mat,$fee,[decimal]$paid,$history) {
    $dialog.AutoScaleMode='Dpi'
    $header=New-Object Windows.Forms.Panel;$header.Dock='Top';$header.Height=114;$header.BackColor=[Drawing.Color]::White;$dialog.Controls.Add($header)
    $name=New-Label $header ($a.Nom+' '+$a.PostNom+' '+$a.Prenom) 24 14 900 48 20 $true;$name.ForeColor=[Drawing.Color]::FromArgb(29,67,130);$name.AutoEllipsis=$false
    $meta=New-Label $header ('Matricule '+$mat+'   ·   '+$a.Promotion+'   ·   '+$a.Annee+'   ·   '+$a.Statut) 24 70 900 30 10;$meta.ForeColor=[Drawing.Color]::FromArgb(100,116,139)
    $tabs=New-Object ITMModernTabs;$tabs.Dock='Fill';$tabs.Padding=New-Object Drawing.Point(18,10);$tabs.Multiline=$true;$tabs.Font=New-Object Drawing.Font('Segoe UI',10,[Drawing.FontStyle]::Bold);$body.Controls.Add($tabs)
    $pages=@{}
    foreach($title in @('Dossier','Situation financière','Historique')){
        $page=New-Object Windows.Forms.TabPage;$page.Text=$title;$page.BackColor=$dialog.BackColor;$page.AutoScroll=$true;[void]$tabs.TabPages.Add($page);$pages[$title]=$page
    }
    $cards=@()
    $cards+=New-ITMFicheCard $pages['Dossier'] 'Identité' @(@('Nom',$a.Nom),@('Post-nom',$a.PostNom),@('Prénom',$a.Prenom),@('Sexe',$a.Sexe),@('Naissance',$a.Naissance),@('Lieu de naissance',$a.Lieu))
    $cards+=New-ITMFicheCard $pages['Dossier'] 'Contacts et résidence' @(@('Contact parent 1',$a.Contact1),@('Contact parent 2',$a.Contact2),@('Tuteur',$a.Tuteur),@('Adresse',$a.Adresse))
    $cards+=New-ITMFicheCard $pages['Dossier'] 'Scolarité' @(@('Matricule',$mat),@('Promotion',$a.Promotion),@('Année scolaire',$a.Annee),@('Statut',$a.Statut))
    $financial=$pages['Situation financière'];$metrics=@()
    $annual=$(if($fee){(Get-ITMPECAnnual $a)}else{0});$rest=Get-ITMPECPayingBalance $a
    foreach($metric in @(@('Montant annuel',$(if($fee){Money $annual}else{'Non défini'})),@('Total versé',(Money $paid)),@('Reste annuel',$(if($fee){Money $rest}else{'Non calculable'})))){
        $card=New-Object ITMQuietSurface;$card.BackColor=[Drawing.Color]::White;$financial.Controls.Add($card)
        $label=New-Label $card $metric[0] 18 14 500 26 10 $true;$label.BackColor=[Drawing.Color]::White
        $value=New-Label $card $metric[1] 18 48 500 42 16 $true;$value.BackColor=[Drawing.Color]::White;$value.ForeColor=[Drawing.Color]::FromArgb(29,67,130)
        $metrics+=([pscustomobject]@{Card=$card;Label=$label;Value=$value})
    }
    $note=New-Label $financial $(if($fee){'Mensualité : '+(Money $fee.Mensuel)+' · Affectations enregistrées ; les restes des mois futurs ne sont pas des retards.'}else{'Tarif de la promotion indisponible. Consultez le diagnostic avant toute interprétation.'}) 20 140 900 54 11
    $monthlyGrid=New-Grid $financial 20 216 900 250
    $record=Get-ITMPECRecord $a;if($record){$note.Text+=' · '+(Get-ITMPECStatus $a)+' · Total couvert : '+(Money (Get-ITMPECCoverage $a))};$months=$(if($record){[ITMPE]::Months}else{@($script:data.months | Where-Object {$_ -ne 'Juin'})});$monthly=@();foreach($month in @($months)){
        $amount=Paid $mat ([string]$month)
        $monthly+=([pscustomobject]@{Mois=$month;Du=$(if($fee){Money $fee.Mensuel}else{'—'});Verse=(Money $amount);Couvert=(Money (Get-ITMPECCoverage $a $month));Reste=$(if($fee){Money (Get-ITMPECDue $a $month)}else{'—'})})
    }
    Set-Grid $monthlyGrid $monthly @(@('Mois','Mois'),@('Du','Mensualité (FC)'),@('Verse','Cumul affecté (FC)'),@('Couvert','Pris en charge (FC)'),@('Reste','Reste du mois (FC)'))
    $historyNote=New-Label $pages['Historique'] 'Chaque ligne représente une affectation à un mois. Plusieurs lignes peuvent appartenir au même reçu.' 20 18 900 58 11
    $historyGrid=New-Grid $pages['Historique'] 20 100 900 340
    Set-Grid $historyGrid $history @(@('Date','Date'),@('Numero','N° enregistrement'),@('Mois','Mois'),@('Verse','Montant affecté (FC)'),@('Reste','Reste du mois'),@('Statut','Statut'))
    $dialog.Tag=[pscustomobject]@{Busy=$false;Header=$header;Name=$name;Meta=$meta;Pages=$pages;Cards=$cards;Metrics=$metrics;Note=$note;Monthly=$monthlyGrid;HistoryNote=$historyNote;History=$historyGrid}
    $tabs.Tag=$dialog;$tabs.Add_SelectedIndexChanged({param($sender,$e)Update-ITMModernFiche $sender.Tag})
    $dialog.Add_Resize({param($sender,$e)Update-ITMModernFiche $sender})
    $dialog.Add_Shown({param($sender,$e)Update-ITMModernFiche $sender})
    Update-ITMModernFiche $dialog
}
function Update-ITMModernFiche($dialog) {
    $s=$dialog.Tag;if(-not $s -or $dialog.IsDisposed -or $s.Busy){return};$s.Busy=$true
    $q=[Math]::Max(1,[Math]::Min(2.5,$dialog.CurrentAutoScaleDimensions.Height/96));$margin=[int](16*$q);$gap=[int](12*$q)
    Apply-ITMFluidTypography $dialog
    $hw=[Math]::Max(40,$dialog.ClientSize.Width-2*$margin)
    $s.Name.SetBounds($margin,[int](14*$q),$hw,(Get-ITMTextHeight $s.Name $hw 32))
    $s.Meta.SetBounds($margin,$s.Name.Bottom,$hw,(Get-ITMTextHeight $s.Meta $hw 26))
    $s.Header.Height=$s.Meta.Bottom+[int](12*$q)
    $scrolls=@{};foreach($page in $s.Pages.Values){$scrolls[$page]=$page.AutoScrollPosition;$page.SuspendLayout();$page.AutoScrollPosition=New-Object Drawing.Point(0,0)}
    try{
        $page=$s.Pages['Dossier'];$width=[Math]::Max(60,$page.ClientSize.Width-2*$margin-[Windows.Forms.SystemInformation]::VerticalScrollBarWidth);$cols=$(if($width -ge 880*$q){2}else{1});$cardWidth=[int](($width-($cols-1)*$gap)/$cols)
        $y=$margin;$rowHeight=0
        for($i=0;$i -lt $s.Cards.Count;$i++){
            $group=$s.Cards[$i];$pad=[int](18*$q);$inner=$cardWidth-2*$pad;$labelWidth=[int]($inner*0.36);$valueWidth=$inner-$labelWidth-[int](12*$q)
            $group.Title.SetBounds($pad,[int](16*$q),$inner,(Get-ITMTextHeight $group.Title $inner 28));$rowY=$group.Title.Bottom+[int](12*$q);$group.Card.HeaderHeight=$group.Title.Bottom+[int](6*$q)
            foreach($row in $group.Rows){
                if($inner/$q -lt 450){
                    $lh=Get-ITMTextHeight $row.Label $inner 24;$vh=Get-ITMTextHeight $row.Value $inner 26
                    $row.Label.SetBounds($pad,$rowY,$inner,$lh);$row.Value.SetBounds($pad,($rowY+$lh),$inner,$vh);$rowY=$row.Value.Bottom+[int](12*$q)
                }else{
                    $height=[Math]::Max((Get-ITMTextHeight $row.Label $labelWidth 28),(Get-ITMTextHeight $row.Value $valueWidth 28))
                    $row.Label.SetBounds($pad,$rowY,$labelWidth,$height);$row.Value.SetBounds(($pad+$labelWidth+[int](12*$q)),$rowY,$valueWidth,$height);$rowY+=$height+[int](12*$q)
                }
            }
            $height=$rowY+[int](12*$q);$group.Card.SetBounds(($margin+($i%$cols)*($cardWidth+$gap)),$y,$cardWidth,$height)
            $rowHeight=[Math]::Max($rowHeight,$height)
            if(($i%$cols) -eq $cols-1 -or $i -eq $s.Cards.Count-1){$y+=$rowHeight+$gap;$rowHeight=0}
        }
        $page.AutoScrollMinSize=New-Object Drawing.Size(0,($y+$margin))
        $page=$s.Pages['Situation financière'];$width=[Math]::Max(60,$page.ClientSize.Width-2*$margin-[Windows.Forms.SystemInformation]::VerticalScrollBarWidth);$cols=$(if($width -ge 840*$q){3}else{1});$cardWidth=[int](($width-($cols-1)*$gap)/$cols)
        $mh=0
        foreach($metric in $s.Metrics){
            Set-ITMAdaptiveFont $metric.Value 18 ([Drawing.FontStyle]::Bold)
            $iw=[Math]::Max(20,$cardWidth-[int](36*$q))
            $metric.Label.SetBounds([int](18*$q),[int](14*$q),$iw,(Get-ITMTextHeight $metric.Label $iw 24))
            $metric.Value.SetBounds([int](18*$q),$metric.Label.Bottom,$iw,(Get-ITMTextHeight $metric.Value $iw 36));$mh=[Math]::Max($mh,$metric.Value.Bottom+[int](14*$q))
        }
        for($i=0;$i -lt 3;$i++){$s.Metrics[$i].Card.SetBounds(($margin+($i%$cols)*($cardWidth+$gap)),($margin+[int][Math]::Floor($i/$cols)*($mh+$gap)),$cardWidth,$mh)}
        $y=$margin+[int][Math]::Ceiling(3/$cols)*($mh+$gap)
        $s.Note.SetBounds($margin,$y,$width,(Get-ITMTextHeight $s.Note $width 36))
        $s.Monthly.SetBounds($margin,($s.Note.Bottom+$gap),$width,[int][Math]::Max(180*$q,$page.ClientSize.Height-$s.Note.Bottom-$gap-$margin));Set-ITMGridReadability $s.Monthly $false
        $page.AutoScrollMinSize=New-Object Drawing.Size(0,($s.Monthly.Bottom+$margin))
        $page=$s.Pages['Historique'];$width=[Math]::Max(60,$page.ClientSize.Width-2*$margin-[Windows.Forms.SystemInformation]::VerticalScrollBarWidth)
        $s.HistoryNote.SetBounds($margin,$margin,$width,(Get-ITMTextHeight $s.HistoryNote $width 36));$s.History.SetBounds($margin,($s.HistoryNote.Bottom+$gap),$width,[int][Math]::Max(180*$q,$page.ClientSize.Height-$s.HistoryNote.Bottom-$gap-$margin));Set-ITMGridReadability $s.History $false
        $page.AutoScrollMinSize=New-Object Drawing.Size(0,($s.History.Bottom+$margin))
    }finally{foreach($page in $s.Pages.Values){$page.ResumeLayout();$old=$scrolls[$page];$page.AutoScrollPosition=New-Object Drawing.Point([Math]::Abs($old.X),[Math]::Abs($old.Y))};$s.Busy=$false}
}
