function Show-ITMHelpWindow([string]$guide) {
    $topics=New-Object 'System.Collections.Generic.List[object]'
    $topics.Add([pscustomobject]@{Title='Bienvenue';Caption='Bienvenue';Body=($guide -split 'SOMMAIRE',2)[0].Trim()})
    $matches=@([regex]::Matches($guide,'(?m)^(\d{2}) — ([^\r\n]+)\r?$') | Where-Object {$_.Groups[2].Value -ceq $_.Groups[2].Value.ToUpperInvariant()})
    $captions=@('Installation','Démarrage','Navigation','Connecter un classeur','Créer un classeur','Tableau de bord','Inscrire un apprenant','Gérer les dossiers','Enregistrer un paiement','Corriger un paiement','Exports et listes','Diagnostic et doublons','Promotions et frais','Capacité des classeurs','Sauvegarde et restauration','Année scolaire et journal','Sécurité','Préférences','Fichiers et déplacement','Incidents et reprise','Raccourcis clavier','Routine quotidienne','Cartes d''examen','Prises en charge','Personnalisation','Son','Cotes et délibération','Décisions automatiques','SCOLIA et comptabilité')
    for($i=0;$i -lt $matches.Count;$i++){
        $start=$matches[$i].Index+$matches[$i].Length;$end=$(if($i+1 -lt $matches.Count){$matches[$i+1].Index}else{$guide.Length})
        $topics.Add([pscustomobject]@{Title=$matches[$i].Value.Trim();Caption=($matches[$i].Groups[1].Value+'  '+$captions[$i]);Body=$guide.Substring($start,$end-$start).Trim()})
    }
    $d=New-Object Windows.Forms.Form;Set-ITMWindowIcon $d;$d.Text='SCOLIA | Centre d''aide';$d.Size=New-Object Drawing.Size(1060,740);$d.MinimumSize=New-Object Drawing.Size(760,520);$d.StartPosition='CenterParent';$d.Font=$script:main.Font;$d.AutoScaleMode='Dpi'
    $header=New-Object Windows.Forms.Panel;$header.Dock='Top';$header.Height=100;$header.Padding=New-Object Windows.Forms.Padding(16);$d.Controls.Add($header)
    $helpHeading=New-Label $header 'Centre d''aide' 18 12 500 30 16 $true
    $helpSearchLabel=New-Label $header 'Rechercher (Ctrl+F)' 18 54 230 30 10
    $search=New-Object Windows.Forms.TextBox;$search.SetBounds(264,54,730,30);$search.Anchor='Top,Left,Right';$search.AccessibleName='Rechercher dans toutes les instructions';$header.Controls.Add($search)
    $footer=New-Object Windows.Forms.Panel;$footer.Dock='Bottom';$footer.Height=64;$d.Controls.Add($footer)
    $close=New-Button $footer 'Fermer' 780 14 230;$close.Anchor='Top,Right';$close.DialogResult='Cancel';$d.CancelButton=$close
    $count=New-Label $footer 'Choisissez une rubrique dans le sommaire.' 18 16 680 34 10;$count.Anchor='Top,Left,Right'
    $split=New-Object Windows.Forms.SplitContainer;$split.Size=New-Object Drawing.Size(1000,550);$split.Dock='Fill';$split.FixedPanel='Panel1';$split.Panel1MinSize=60;$split.Panel2MinSize=80;$split.SplitterWidth=6;$d.Controls.Add($split);$split.BringToFront()
    $list=New-Object ITMTopicList;$list.Dock='Fill';$list.BorderStyle='None';$list.HorizontalScrollbar=$false;$list.IntegralHeight=$false;$list.Font=New-Object Drawing.Font('Segoe UI',10);$split.Panel1.Controls.Add($list)
    $viewer=New-Object Windows.Forms.RichTextBox;$viewer.Dock='Fill';$viewer.ReadOnly=$true;$viewer.BorderStyle='None';$viewer.WordWrap=$true;$viewer.DetectUrls=$false;$viewer.Font=New-Object Drawing.Font('Segoe UI',11);$viewer.AccessibleName='Instructions de la rubrique';$split.Panel2.Padding=New-Object Windows.Forms.Padding(18,12,18,12);$split.Panel2.Controls.Add($viewer)
    $titleFont=New-Object Drawing.Font('Segoe UI',14,[Drawing.FontStyle]::Bold)
    $ownedTitleFont=$titleFont;$ownedBodyFont=$viewer.Font;$ownedListFont=$list.Font
    $update={
        $headingSize=Get-ITMFluidTextSize $d 14;$headingKey=[string]$headingSize+'|Bold'
        if(-not $script:adaptiveFonts.ContainsKey($headingKey)){$script:adaptiveFonts[$headingKey]=New-Object Drawing.Font('Segoe UI',[single]$headingSize,[Drawing.FontStyle]::Bold)}
        $titleFont=$script:adaptiveFonts[$headingKey]
        $selected=[string]$list.SelectedItem;$entry=$topics | Where-Object {$_.Caption -eq $selected} | Select-Object -First 1
        if($entry){$viewer.Text=$entry.Title+"`r`n`r`n"+$entry.Body;$viewer.SelectAll();$viewer.SelectionFont=$viewer.Font;$viewer.SelectionColor=$viewer.ForeColor;$viewer.Select(0,$entry.Title.Length);$viewer.SelectionFont=$titleFont;$viewer.Select(0,0);$viewer.ScrollToCaret();$count.Text=[string]$list.Items.Count+' rubrique(s) · '+$entry.Caption}
        else{$viewer.Text='Aucune rubrique ne correspond. Essayez un autre mot.';$count.Text='Aucun résultat.'}
    }
    $filter={
        $selected=[string]$list.SelectedItem;$query=Key $search.Text
        $list.BeginUpdate()
        try{$list.Items.Clear();foreach($topic in $topics){if(-not $query -or (Key ($topic.Title+' '+$topic.Body)).Contains($query)){[void]$list.Items.Add($topic.Caption)}};if($list.Items.Contains($selected)){$list.SelectedItem=$selected}elseif($list.Items.Count){$list.SelectedIndex=0}}finally{$list.EndUpdate()}
        & $update
    }
    $list.Add_SelectedIndexChanged($update);$search.Add_TextChanged($filter)
    $d.KeyPreview=$true;$d.Add_KeyDown({param($sender,$e)if($e.Control -and $e.KeyCode -eq 'F'){$e.Handled=$true;$e.SuppressKeyPress=$true;$search.Focus();$search.SelectAll()}})
    $helpLayout={
        $q=Get-ITMDpiScale $d;$w=[Math]::Max(40,$d.ClientSize.Width-[int](36*$q));$x=[int](18*$q)
        Apply-ITMFluidTypography $d
        $headingSize=Get-ITMFluidTextSize $d 14;$headingKey=[string]$headingSize+'|Bold'
        if(-not $script:adaptiveFonts.ContainsKey($headingKey)){$script:adaptiveFonts[$headingKey]=New-Object Drawing.Font('Segoe UI',[single]$headingSize,[Drawing.FontStyle]::Bold)}
        $titleFont=$script:adaptiveFonts[$headingKey]
        if($viewer.TextLength){
            $selectionStart=$viewer.SelectionStart;$selectionLength=$viewer.SelectionLength
            $viewer.SelectAll();$viewer.SelectionFont=$viewer.Font
            $firstLine=($viewer.Text -split "`n",2)[0].TrimEnd("`r")
            $viewer.Select(0,$firstLine.Length);$viewer.SelectionFont=$titleFont
            $viewer.Select($selectionStart,$selectionLength)
        }
        $helpHeading.SetBounds($x,[int](10*$q),$w,(Get-ITMTextHeight $helpHeading $w 28))
        $y=$helpHeading.Bottom+[int](4*$q)
        if($w/$q -lt 650){
            $helpSearchLabel.SetBounds($x,$y,$w,(Get-ITMTextHeight $helpSearchLabel $w 24));$y=$helpSearchLabel.Bottom
            $search.SetBounds($x,$y,$w,($search.Font.Height+12))
        }else{
            $lw=[int](180*$q);$helpSearchLabel.SetBounds($x,$y,$lw,(Get-ITMTextHeight $helpSearchLabel $lw 28));$search.SetBounds(($x+$lw),$y,($w-$lw),($search.Font.Height+12))
        }
        $header.Height=[Math]::Max($helpSearchLabel.Bottom,$search.Bottom)+[int](12*$q)
        $bw=[Math]::Min([int](180*$q),[int]($w*.38));$bh=Get-ITMTextHeight $close $bw 32
        $close.SetBounds(($d.ClientSize.Width-$x-$bw),[int](10*$q),$bw,$bh)
        $cw=[Math]::Max(20,$w-$bw-[int](12*$q));$count.SetBounds($x,[int](10*$q),$cw,(Get-ITMTextHeight $count $cw 32))
        $footer.Height=[Math]::Max($close.Bottom,$count.Bottom)+[int](10*$q)
        $narrow=$d.ClientSize.Width/$q -lt 760
        if($narrow){
            $split.Panel1MinSize=0;$split.Panel2MinSize=0;if($split.Orientation -ne 'Horizontal'){$split.SplitterDistance=1;$split.Orientation='Horizontal'}
            $split.SplitterDistance=[Math]::Max(1,[Math]::Min([int]($split.Height*.26),$split.Height-$split.SplitterWidth-1))
        }else{
            $split.Panel1MinSize=0;$split.Panel2MinSize=0;if($split.Orientation -ne 'Vertical'){$split.SplitterDistance=1;$split.Orientation='Vertical'}
            $split.SplitterDistance=[Math]::Max(1,[Math]::Min([int]($split.Width*.30),$split.Width-$split.SplitterWidth-1))
        }
    }
    $area=[Windows.Forms.Screen]::FromControl($script:main).WorkingArea
    $d.MinimumSize=New-Object Drawing.Size([Math]::Min(360,$area.Width-32),[Math]::Min(360,$area.Height-32))
    $d.Size=New-Object Drawing.Size([Math]::Min($d.Width,$area.Width-32),[Math]::Min($d.Height,$area.Height-32))
    $d.Add_Resize($helpLayout);$d.Add_Shown({& $helpLayout;$search.Focus()})
    Enable-ITMDialogShortcuts $d;Apply-ITMThemeForm $d
    & $filter
    try{[void]$d.ShowDialog($script:main)}finally{$d.Dispose();$ownedTitleFont.Dispose();$ownedBodyFont.Dispose();$ownedListFont.Dispose()}
}
