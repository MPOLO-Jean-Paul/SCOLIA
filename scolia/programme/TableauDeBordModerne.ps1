if(-not (Get-Command Import-ITMCompiledSource -ErrorAction SilentlyContinue)){. (Join-Path $PSScriptRoot 'CompilationCache.ps1')}
Import-ITMCompiledSource -Path (Join-Path $PSScriptRoot 'DashboardChart.cs') -ReferencedAssemblies System.Windows.Forms,System.Drawing
function Build-ITMDashboard($p) {
    $script:dashHeading=New-Label $p 'Pilotage du secrétariat' 0 0 400 32 14 $true
    $script:dashSubtitle=New-Label $p 'Dossiers, paiements et régularisations · Synthèse de la sélection' 0 0 500 44 10
    $script:dashPromoLabel=New-Label $p 'Promotion' 0 0 100 24 10 $true
    $script:dashMonthLabel=New-Label $p 'Période' 0 0 100 24 10 $true
    $script:dashPromo=New-Combo $p 0 0 240;$script:dashMonth=New-Combo $p 0 0 220
    $script:cardValues=@();$script:dashboardCards=@()
    $titles=@('Dossiers valides','Recettes validées','Reste exigible','Dossiers à jour')
    $hints=@('Selon la promotion choisie','Affectations de la période','Somme restant à régulariser','Part des dossiers sans reste')
    $colors=@([Drawing.Color]::FromArgb(0,102,204),[Drawing.Color]::FromArgb(27,126,84),[Drawing.Color]::FromArgb(171,94,16),[Drawing.Color]::FromArgb(0,102,204))
    for($i=0;$i -lt 4;$i++){
        $card=New-Object ITMDashboardSurface;$card.BackColor=[Drawing.Color]::White;$p.Controls.Add($card)
        $title=New-Label $card $titles[$i] 16 18 180 24 10 $true
        $value=New-Label $card '—' 16 50 180 36 20 $true;$value.ForeColor=$colors[$i]
        $hint=New-Label $card $hints[$i] 16 94 180 40 9
        foreach($c in @($title,$value,$hint)){$c.BackColor=[Drawing.Color]::White}
        $card.Accent=$colors[$i]
        $glyph=New-Object Windows.Forms.PictureBox;$glyph.SizeMode='Zoom';$glyph.BackColor=[Drawing.Color]::White
        $iconName=@('Apprenants','Paiements','Diagnostic','Sécurité')[$i]
        # Every dashboard PictureBox owns its bitmap; it never borrows the navigation cache.
        $glyph.Image=New-NavIcon $iconName (Get-ITMEffectiveDarkTheme)
        $glyph.Add_Disposed({param($sender,$e)
            $ownedImage=$sender.Image;$sender.Image=$null
            if($ownedImage){$ownedImage.Dispose()}
        })
        $card.Controls.Add($glyph)
        $card.Tag=[pscustomobject]@{Title=$title;Value=$value;Hint=$hint;Glyph=$glyph;IconName=$iconName}
        $script:dashboardCards+=$card;$script:cardValues+=$value
    }
    $script:dashPECSurface=New-Object ITMDashboardSurface;$script:dashPECSurface.Accent=[Drawing.Color]::FromArgb(86,99,165);$p.Controls.Add($script:dashPECSurface)
    $script:dashPECTitle=New-Label $script:dashPECSurface 'Prises en charge — total couvert sur dix mois' 16 12 600 26 11 $true
    $script:dashPECValue=New-Label $script:dashPECSurface '—' 16 43 600 38 20 $true
    $script:dashPECHint=New-Label $script:dashPECSurface 'Montant distinct des recettes encaissées.' 16 86 600 46 10
    $script:dashNote=New-Label $p 'Connectez un classeur pour afficher vos indicateurs.' 0 0 500 44 10
    $script:dashUpdated=New-Label $p 'Aucune actualisation' 0 0 500 24 9
    $script:dashChartCards=@();$script:dashCharts=@()
    foreach($title in @('Recettes affectées par mois','Restes exigibles par promotion')){
        $card=New-Object ITMDashboardSurface;$card.BackColor=[Drawing.Color]::White;$p.Controls.Add($card)
        $label=New-Label $card $title 16 14 380 24 11 $true;$label.BackColor=[Drawing.Color]::White
        $caption=New-Label $card '' 16 43 380 38 9;$caption.BackColor=[Drawing.Color]::White
        $chart=New-Object ITMDashboardChart;$card.Controls.Add($chart)
        $card.Tag=[pscustomobject]@{Title=$label;Caption=$caption;Chart=$chart}
        $script:dashChartCards+=$card;$script:dashCharts+=$chart
    }
    $script:dashCharts[1].Accent=[Drawing.Color]::FromArgb(171,94,16)
    $script:dashChartCards[0].Tag.Caption.Text='Année scolaire complète • promotion choisie • FC'
    $script:dashChartCards[1].Tag.Caption.Text='Période et promotion choisies • FC'
    $script:dashListTitle=New-Label $p 'Dossiers à régulariser' 0 0 500 30 13 $true
    $script:dashLateLabel=New-Label $p 'Mois en retard' 0 0 120 24 10 $true
    $script:dashLate=New-Combo $p 0 0 220;Set-Choices $script:dashLate @('Tous','1 mois','2 mois','3 mois et plus') 'Tous'
    $script:exportLateButton=New-Button $p 'Exporter les retards et contacts' 0 0 350
    $script:exportLateButton.Add_Click({try{if(-not $script:busy){Save-ITMCsv $script:lateRows 'Retards_et_contacts.csv'}}catch{App-Error $_}})
    $script:batchLateButton=New-Button $p 'Régler une sélection' 0 0 260 $true
    $script:batchLateButton.Add_Click({try{if(-not $script:busy){Show-ITMBatchPayment -PresetRows $script:lateRows -Source 'Retards'}}catch{App-Error $_}})
    $script:dashGrid=New-Grid $p 0 0 900 300;$script:dashGrid.Anchor='Top,Left'
    $script:dashViews=New-Object ITMModernTabs;$script:dashViews.Multiline=$true;$script:dashViews.Font=New-Object Drawing.Font('Segoe UI',10);$p.Controls.Add($script:dashViews)
    $script:dashAnalysis=New-Object Windows.Forms.TabPage;$script:dashAnalysis.Text='Analyse';$script:dashAnalysis.BackColor=$p.BackColor;$script:dashAnalysis.AutoScroll=$true
    $script:dashFollowup=New-Object Windows.Forms.TabPage;$script:dashFollowup.Text='Retards et contacts';$script:dashFollowup.BackColor=$p.BackColor;$script:dashFollowup.AutoScroll=$true
    [void]$script:dashViews.TabPages.Add($script:dashAnalysis);[void]$script:dashViews.TabPages.Add($script:dashFollowup)
    foreach($c in @($script:dashNote,$script:dashUpdated)+@($script:dashChartCards)){$script:dashAnalysis.Controls.Add($c)}
    foreach($c in @($script:dashListTitle,$script:dashLateLabel,$script:dashLate,$script:exportLateButton,$script:batchLateButton,$script:dashGrid)){$script:dashFollowup.Controls.Add($c)}
    $script:dashViews.Add_SelectedIndexChanged({Update-ITMDashboardLayout $script:pages['Tableau de bord']})
    $script:dashLate.Add_SelectedIndexChanged({Refresh-Dashboard})
    $script:dashPromo.Add_SelectedIndexChanged({Refresh-Dashboard});$script:dashMonth.Add_SelectedIndexChanged({Refresh-Dashboard})
}
function Update-ITMDashboardIcons([bool]$dark) {
    foreach($card in @($script:dashboardCards)){
        if(-not $card -or $card.IsDisposed){continue}
        $glyph=$card.Tag.Glyph
        if(-not $glyph -or $glyph.IsDisposed){continue}
        # Construct before swapping; a generation failure leaves the old image usable.
        $next=New-NavIcon ([string]$card.Tag.IconName) $dark
        $old=$glyph.Image
        try{$glyph.Image=$next}catch{$next.Dispose();throw}
        if($old){$old.Dispose()}
        $glyph.Invalidate()
    }
}
function Update-ITMDashboardVisuals($people,$debtByPromotion) {
    $months=@($script:data.months)
    $amounts=New-Object 'System.Collections.Generic.List[double]'
    foreach($m in $months){$sum=[decimal]0;foreach($a in $people){$sum+=Paid $a.Matricule $m};$amounts.Add([double]$sum)}
    $script:dashCharts[0].SetData([string[]]$months,[double[]]$amounts.ToArray())
    $groups=@($debtByPromotion.GetEnumerator() | Sort-Object Value -Descending)
    $names=New-Object 'System.Collections.Generic.List[string]';$values=New-Object 'System.Collections.Generic.List[double]'
    foreach($group in @($groups | Select-Object -First 8)){$names.Add([string]$group.Key);$values.Add([double]$group.Value)}
    if($groups.Count -gt 8){$other=[double]0;foreach($group in @($groups | Select-Object -Skip 8)){$other+=[double]$group.Value};$names.Add('Autres');$values.Add($other)}
    $script:dashCharts[1].SetData([string[]]$names.ToArray(),[double[]]$values.ToArray())
    $script:dashUpdated.Text='Actualisé à '+(Get-Date -Format 'HH:mm')+' • Tous les mois : reste des mois échus à ce jour.'
    Update-ITMDashboardLayout $script:pages['Tableau de bord']
}
$script:dashboardLayoutBusy=$false
function Update-ITMDashboardLayout($p) {
    if($script:dashboardCards.Count -ne 4 -or $p.ClientSize.Width -lt 240 -or $script:dashboardLayoutBusy){return}
    $script:dashboardLayoutBusy=$true;$p.SuspendLayout()
    $scrolls=@{};foreach($view in @($p,$script:dashAnalysis,$script:dashFollowup)){$scrolls[$view]=$view.AutoScrollPosition;$view.AutoScrollPosition=New-Object Drawing.Point(0,0)}
    try {
        $q=[Math]::Max(1,[Math]::Min(2.5,$script:main.CurrentAutoScaleDimensions.Height/96.0))
        $margin=[int](16*$q);$gap=[int](12*$q);$w=[Math]::Max(60,$p.ClientSize.Width-2*$margin-[Windows.Forms.SystemInformation]::VerticalScrollBarWidth);$y=$margin
        Set-ITMAdaptiveFont $script:dashHeading 16 ([Drawing.FontStyle]::Bold)
        $h=Get-ITMTextHeight $script:dashHeading $w ([int](32*$q));$script:dashHeading.SetBounds($margin,$y,$w,$h);$y+=$h
        $h=Get-ITMTextHeight $script:dashSubtitle $w ([int](26*$q));$script:dashSubtitle.SetBounds($margin,$y,$w,$h);$y+=$h+$gap
        $stackFilters=$w/$q -lt 520;$filterWidth=$(if($stackFilters){$w}else{[int](($w-$gap)/2)})
        $labelH=[Math]::Max((Get-ITMTextHeight $script:dashPromoLabel $filterWidth 24),(Get-ITMTextHeight $script:dashMonthLabel $filterWidth 24))
        $fieldH=[Math]::Max($script:dashPromo.PreferredSize.Height,[int](32*$q))
        $script:dashPromoLabel.SetBounds($margin,$y,$filterWidth,$labelH)
        $script:dashPromo.SetBounds($margin,($y+$labelH),$filterWidth,$fieldH)
        $mx=$(if($stackFilters){$margin}else{$margin+$filterWidth+$gap});$my=$(if($stackFilters){$script:dashPromo.Bottom+$gap}else{$y})
        $script:dashMonthLabel.SetBounds($mx,$my,$filterWidth,$labelH);$script:dashMonth.SetBounds($mx,($my+$labelH),$filterWidth,$fieldH)
        $y=$script:dashMonth.Bottom+$gap
        $cols=$(if($w -ge 900*$q){4}elseif($w -ge 470*$q){2}else{1});$cw=[int](($w-($cols-1)*$gap)/$cols);$ch=[int](126*$q)
        foreach($card in $script:dashboardCards){
            $tag=$card.Tag;Set-ITMAdaptiveFont $tag.Title 10 ([Drawing.FontStyle]::Regular);Set-ITMAdaptiveFont $tag.Hint 9 ([Drawing.FontStyle]::Regular)
            $titleH=Get-ITMTextHeight $tag.Title ($cw-[int](70*$q)) ([int](24*$q))
            $hintH=Get-ITMTextHeight $tag.Hint ($cw-[int](32*$q)) ([int](24*$q))
            $tag.Title.SetBounds([int](16*$q),[int](16*$q),($cw-[int](70*$q)),$titleH)
            $tag.Glyph.SetBounds(($cw-[int](42*$q)),[int](16*$q),[int](24*$q),[int](24*$q))
            $fontSize=26;Set-ITMAdaptiveFont $tag.Value $fontSize ([Drawing.FontStyle]::Bold)
            $valueWidth=$cw-[int](32*$q)
            $valueH=Get-ITMTextHeight $tag.Value $valueWidth ([int](40*$q))
            $tag.Value.SetBounds([int](16*$q),($tag.Title.Bottom+[int](4*$q)),$valueWidth,$valueH)
            $tag.Hint.SetBounds([int](16*$q),($tag.Value.Bottom+[int](2*$q)),$valueWidth,$hintH)
            $ch=[Math]::Max($ch,$tag.Hint.Bottom+[int](14*$q))
        }
        for($i=0;$i -lt 4;$i++){$script:dashboardCards[$i].SetBounds(($margin+($i%$cols)*($cw+$gap)),($y+[int][Math]::Floor($i/$cols)*($ch+$gap)),$cw,$ch)}
        $y+=[int][Math]::Ceiling(4/$cols)*($ch+$gap)
        $sy=[int](12*$q);$sw=$w-[int](32*$q)
        foreach($c in @($script:dashPECTitle,$script:dashPECValue,$script:dashPECHint)){$sh=Get-ITMTextHeight $c $sw ([int](28*$q));$c.SetBounds([int](16*$q),$sy,$sw,$sh);$sy+=$sh+[int](4*$q)}
        $script:dashPECSurface.SetBounds($margin,$y,$w,($sy+[int](12*$q)));$y=$script:dashPECSurface.Bottom+$gap
        $viewHeight=[Math]::Max([int](350*$q),$p.ClientSize.Height-$y-$margin)
        $script:dashViews.SetBounds($margin,$y,$w,$viewHeight)
        $p.AutoScrollMinSize=New-Object Drawing.Size(0,($script:dashViews.Bottom+$margin))
        # Analysis and follow-up are separate scrollable views; no information is removed.
        $page=$script:dashAnalysis;$inner=[Math]::Max(60,$page.ClientSize.Width-2*$gap-[Windows.Forms.SystemInformation]::VerticalScrollBarWidth);$cy=$gap
        $h=Get-ITMTextHeight $script:dashNote $inner ([int](28*$q));$script:dashNote.SetBounds($gap,$cy,$inner,$h);$cy+=$h+[int](4*$q)
        $h=Get-ITMTextHeight $script:dashUpdated $inner ([int](24*$q));$script:dashUpdated.SetBounds($gap,$cy,$inner,$h);$cy+=$h+$gap
        $chartCols=$(if($inner -ge 840*$q){2}else{1});$chartWidth=[int](($inner-($chartCols-1)*$gap)/$chartCols)
        $chartHeight=[int](350*$q)
        for($i=0;$i -lt 2;$i++){
            $card=$script:dashChartCards[$i];$tag=$card.Tag
            $titleH=Get-ITMTextHeight $tag.Title ($chartWidth-[int](32*$q)) ([int](28*$q))
            $tag.Title.SetBounds([int](16*$q),[int](14*$q),($chartWidth-[int](32*$q)),$titleH)
            $captionH=Get-ITMTextHeight $tag.Caption ($chartWidth-[int](32*$q)) ([int](24*$q))
            $tag.Caption.SetBounds([int](16*$q),$tag.Title.Bottom,($chartWidth-[int](32*$q)),$captionH)
            $needed=$tag.Chart.RequiredHeight([Math]::Max(20,$chartWidth-[int](32*$q)))
            $chartHeight=[Math]::Max($chartHeight,$tag.Caption.Bottom+[Math]::Max([int](264*$q),$needed)+[int](24*$q))
        }
        for($i=0;$i -lt 2;$i++){
            $card=$script:dashChartCards[$i];$tag=$card.Tag
            $card.SetBounds(($gap+($i%$chartCols)*($chartWidth+$gap)),($cy+[int][Math]::Floor($i/$chartCols)*($chartHeight+$gap)),$chartWidth,$chartHeight)
            $tag.Chart.SetBounds([int](16*$q),($tag.Caption.Bottom+[int](8*$q)),($chartWidth-[int](32*$q)),($chartHeight-$tag.Caption.Bottom-[int](24*$q)))
        }
        $page.AutoScrollMinSize=New-Object Drawing.Size(0,($cy+[int][Math]::Ceiling(2/$chartCols)*($chartHeight+$gap)))
        $page=$script:dashFollowup;$inner=[Math]::Max(60,$page.ClientSize.Width-2*$gap-[Windows.Forms.SystemInformation]::VerticalScrollBarWidth);$cy=$gap
        $h=Get-ITMTextHeight $script:dashListTitle $inner ([int](30*$q));$script:dashListTitle.SetBounds($gap,$cy,$inner,$h);$cy+=$h+$gap
        $lh=Get-ITMTextHeight $script:dashLateLabel $inner ([int](24*$q));$script:dashLateLabel.SetBounds($gap,$cy,$inner,$lh);$cy+=$lh+[int](4*$q)
        $stack=$inner -lt 820*$q;$fw=$(if($stack){$inner}else{[int](210*$q)})
        $script:dashLate.SetBounds($gap,$cy,$fw,[int](32*$q))
        if($stack){
            $by=$script:dashLate.Bottom+$gap
            $bh=Get-ITMTextHeight $script:batchLateButton $inner ([int](38*$q));$script:batchLateButton.SetBounds($gap,$by,$inner,$bh)
            $ey=$script:batchLateButton.Bottom+$gap;$eh=Get-ITMTextHeight $script:exportLateButton $inner ([int](38*$q));$script:exportLateButton.SetBounds($gap,$ey,$inner,$eh)
            $cy=$script:exportLateButton.Bottom+$gap
        }else{
            $buttonsWidth=$inner-$fw-2*$gap;$bw=[int](($buttonsWidth-$gap)/2);$bx=$gap+$fw+$gap
            $bh=Get-ITMTextHeight $script:batchLateButton $bw ([int](38*$q));$eh=Get-ITMTextHeight $script:exportLateButton $bw ([int](38*$q));$buttonH=[Math]::Max($bh,$eh)
            $script:batchLateButton.SetBounds($bx,$cy,$bw,$buttonH);$script:exportLateButton.SetBounds(($bx+$bw+$gap),$cy,$bw,$buttonH)
            $cy=[Math]::Max($script:batchLateButton.Bottom,$script:exportLateButton.Bottom)+$gap
        }
        $script:dashGrid.SetBounds($gap,$cy,$inner,[Math]::Max([int](220*$q),$page.ClientSize.Height-$cy-$gap));Set-ITMGridReadability $script:dashGrid $false
        $page.AutoScrollMinSize=New-Object Drawing.Size(0,($script:dashGrid.Bottom+$gap))
    } finally {$p.ResumeLayout();foreach($view in @($p,$script:dashAnalysis,$script:dashFollowup)){$old=$scrolls[$view];$view.AutoScrollPosition=New-Object Drawing.Point([Math]::Abs($old.X),[Math]::Abs($old.Y))};$script:dashboardLayoutBusy=$false}
}
