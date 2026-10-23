$script:modernPages=@{}
$script:pendingModernPageLayouts=New-Object 'System.Collections.Generic.HashSet[object]'
$script:modernPageLayoutTimer=New-Object Windows.Forms.Timer
$script:modernPageLayoutTimer.Interval=16
$script:modernPageLayoutTimer.Add_Tick({
    $script:modernPageLayoutTimer.Stop()
    $pagesToLayout=@($script:pendingModernPageLayouts)
    $script:pendingModernPageLayouts.Clear()
    foreach($pendingPage in $pagesToLayout){
        if($pendingPage -and -not $pendingPage.IsDisposed){Update-ITMPageDesign $pendingPage}
    }
})
function Request-ITMPageDesign($page) {
    if(-not $page -or $page.IsDisposed){return}
    [void]$script:pendingModernPageLayouts.Add($page)
    if(-not $script:modernPageLayoutTimer.Enabled){$script:modernPageLayoutTimer.Start()}
}
function Enable-ITMPageDesign($page) {
    $controls=@($page.Controls | Where-Object {$_.Dock -eq 'None'} | Sort-Object Top,Left)
    if(-not $controls.Count){return}
    $rows=New-Object 'System.Collections.Generic.List[object]';$items=@();$top=-1000
    foreach($c in $controls){
        if($c.Top-$top -gt 22 -and $items.Count){$rows.Add(@($items));$items=@()}
        if(-not $items.Count){$top=$c.Top}
        $items+=$c
    };if($items.Count){$rows.Add(@($items))}
    if(($page -eq $script:exportReportsPage -or $page -eq $script:pages['Exports'])){
        $rows.Clear()
        $rows.Add(@($controls | Where-Object {$_.Text -eq 'Documents et rapports'}))
        $rows.Add(@($controls | Where-Object {$_.Text -like '1*Choisir*'}))
        foreach($field in @(@('Promotion',$script:exportPromo),@('Mois',$script:exportMonth),@('Rapport',$script:exportType))){
            $rows.Add(@($controls | Where-Object {$_ -is [Windows.Forms.Label] -and $_.Text -eq $field[0]}));$rows.Add(@($field[1]))
        }
        $rows.Add(@($controls | Where-Object {$_ -is [Windows.Forms.Button] -and $_.Text -like 'Exporter en*'}))
        $rows.Add(@($controls | Where-Object {$_.Text -eq 'Contrôle avant export'}));$rows.Add(@($script:reportRule))
        $rows.Add(@($controls | Where-Object {$_ -is [Windows.Forms.Button] -and $_.Text -eq 'Ouvrir le dernier rapport'}))
    }
    if($page -eq $script:pages['Paiements']){
        $rows.Clear()
        $rows.Add(@($controls | Where-Object {$_.Text -eq 'Rechercher un apprenant'}));$rows.Add(@($script:paySearch));$rows.Add(@($script:payLearnerGrid))
        $rows.Add(@($controls | Where-Object {$_ -is [Windows.Forms.Button] -and $_.Text -in @('Sélectionner cet apprenant','Effacer la recherche','Tous les paiements','Paiements multiples')}))
        $rows.Add(@($script:payCount));$rows.Add(@($script:payIdentity));$rows.Add(@($script:paySituation))
        $rows.Add(@($controls | Where-Object {$_.Text -eq 'Montant du reçu validé à la caisse (FC)'}));$rows.Add(@($script:payAmount))
        $rows.Add(@($controls | Where-Object {$_.Text -eq 'Date du reçu validé à la caisse'}));$rows.Add(@($script:payDate))
        $rows.Add(@($script:payDateHint));$rows.Add(@($script:paySave))
        $rows.Add(@($controls | Where-Object {$_.Text -eq 'Historique des affectations'}))
        $rows.Add(@($script:payFilterMonth)+@($controls | Where-Object {$_ -is [Windows.Forms.Button] -and $_.Text -eq 'Corriger le paiement sélectionné'}))
        $rows.Add(@($script:peManage))
        $rows.Add(@($script:paymentGrid))
    }
    $surface=New-Object ITMQuietSurface;$surface.BackColor=[Drawing.Color]::White;$page.Controls.Add($surface)
    foreach($c in $controls){$surface.Controls.Add($c);$c.Anchor='Top,Left';if($c -is [Windows.Forms.Label] -or $c -is [Windows.Forms.CheckBox]){$c.BackColor=[Drawing.Color]::White}}
    $script:modernPages[$page]=[pscustomobject]@{Surface=$surface;Rows=$rows;Busy=$false}
    foreach($c in $controls){if($c -is [Windows.Forms.Label]){$c.Add_TextChanged({param($sender,$e)if($sender.Parent -and $sender.Parent.Parent){Request-ITMPageDesign $sender.Parent.Parent}})}}
}
function Update-ITMPageDesign($page) {
    $s=$script:modernPages[$page];if(-not $s -or $s.Busy -or $page.ClientSize.Width -lt 240){return}
    $s.Busy=$true;$savedScroll=$page.AutoScrollPosition;$page.AutoScrollPosition=New-Object Drawing.Point(0,0);$page.SuspendLayout()
    try {
        $q=[Math]::Max(1,[Math]::Min(2.5,$script:main.CurrentAutoScaleDimensions.Height/96.0))
        $margin=[int](16*$q);$pad=[int](16*$q);$gap=[int](8*$q)
        $outer=[Math]::Max(60,$page.ClientSize.Width-2*$margin-[Windows.Forms.SystemInformation]::VerticalScrollBarWidth);$width=$outer-2*$pad;$y=$pad
        if($page -eq $script:pages['Paiements'] -and $width -ge 1000*$q){Update-ITMPaymentWorkspace $page $s $outer $width $pad $margin $gap $q;return}
        if(($page -eq $script:exportReportsPage -or $page -eq $script:pages['Exports'])){Update-ITMExportWorkspace $page $s $outer $width $pad $margin $gap $q;return}
        foreach($row in $s.Rows){
            $current=@();$needed=0
            foreach($c in $row){
                if($c.IsDisposed -or ((Test-Path function:Test-ScoliaRibbonSource) -and (Test-ScoliaRibbonSource $c)) -or ($page.Visible -and -not $c.Visible)){continue}
                $min=$(if($c -is [Windows.Forms.CheckBox]){[int](400*$q)}elseif($c -is [Windows.Forms.Button]){[int](170*$q)}else{[int](200*$q)})
                $min=[Math]::Min($width,$min)
                if($current.Count -and $needed+$gap+$min -gt $width){$y=Set-ITMPageRow $current $width $pad $y $gap $q;$current=@();$needed=0}
                if($current.Count){$needed+=$gap};$needed+=$min;$current+=$c
            }
            if($current.Count){$y=Set-ITMPageRow $current $width $pad $y $gap $q}
            $y+=[int](2*$q)
        }
        $s.Surface.SetBounds($margin,$margin,$outer,($y+$pad));$page.AutoScrollMinSize=New-Object Drawing.Size(0,($s.Surface.Bottom+$margin))
    }finally{$page.ResumeLayout();$page.AutoScrollPosition=New-Object Drawing.Point([Math]::Abs($savedScroll.X),[Math]::Abs($savedScroll.Y));$s.Busy=$false}
}
function Set-ITMPageRow($controls,$width,$pad,$y,$gap,$q) {
    if($controls.Count -gt 1 -and $width/$controls.Count -lt 175*$q){
        foreach($c in $controls){$y=Set-ITMPageRow @($c) $width $pad $y $gap $q};return $y
    }
    if(-not $controls.Count){return $y}
    $cw=[int](($width-($controls.Count-1)*$gap)/$controls.Count);$height=0;$i=0
    foreach($c in $controls){
        $h=[int](38*$q)
        if($c -is [Windows.Forms.Label] -or $c -is [Windows.Forms.CheckBox]){
            $base=Get-ITMBaseTextSize $c;$size=$(if($c.Font.Bold -and $base -ge 14){16}else{[Math]::Min(12,[Math]::Max(10,$base))});Set-ITMAdaptiveFont $c $size $c.Font.Style
            $usable=$(if($c -is [Windows.Forms.CheckBox]){[Math]::Max(1,$cw-[int](24*$q))}else{$cw})
            $h=Get-ITMTextHeight $c $usable ([int](28*$q))
        }elseif($c -is [Windows.Forms.Button]){Set-ITMAdaptiveFont $c 10 ([Drawing.FontStyle]::Bold);$h=Get-ITMTextHeight $c $cw ([int](38*$q))}
        elseif($c -is [Windows.Forms.DataGridView]){$h=[int][Math]::Max(150*$q,[Math]::Min(340*$q,$page.ClientSize.Height-180*$q));Set-ITMGridReadability $c $false;if(-not (Test-Path function:Set-ITMGridAppearance)){$c.RowTemplate.Height=[int](30*$q)}}
        if($c -is [Windows.Forms.ComboBox] -or $c -is [Windows.Forms.TextBoxBase] -or $c -is [Windows.Forms.NumericUpDown] -or $c -is [Windows.Forms.DateTimePicker]){Set-ITMAdaptiveFont $c (Get-ITMBaseTextSize $c) $c.Font.Style;$h=[Math]::Max($h,$c.PreferredSize.Height)}
        $c.AutoSize=$false;$c.SetBounds(($pad+$i*($cw+$gap)),$y,$cw,$h);$height=[Math]::Max($height,$h);$i++
    }
    return ($y+$height+[int](8*$q))
}
function Update-ITMPaymentContext($a,$fee) {
    $next='Année entièrement réglée'
    if([decimal]$fee.Mensuel -gt 0){foreach($m in [ITMPE]::Months){if((Get-ITMPECDue $a $m) -gt 0){$next=$m;break}}}else{$next='Tarif à vérifier'}
    $latest=[datetime]::MinValue
    foreach($payment in $script:data.payments){
        $parsed=[datetime]::MinValue
        if((Key $payment.Matricule) -eq (Key $a.Matricule) -and [datetime]::TryParseExact([string]$payment.Date,'dd/MM/yyyy',[Globalization.CultureInfo]::InvariantCulture,[Globalization.DateTimeStyles]::None,[ref]$parsed)){
            if($parsed -gt $latest){$latest=$parsed}
        }
    }
    $last=$(if($latest -gt [datetime]::MinValue){$latest.ToString('dd/MM/yyyy')}else{'Aucun paiement enregistré'})
    $script:paySituation.Text+=''+"`r`nDernier paiement : "+$last+"`r`nPremier mois à régler : "+$next
    Update-ITMResponsivePanel $script:pages['Paiements']
}

function Set-ITMWorkspaceControl($control,$x,$y,$width,$minimum,$q) {
    if(-not $control){return 0}
    $base=Get-ITMBaseTextSize $control
    if($control -is [Windows.Forms.Label] -and $control.Font.Bold -and $base -ge 14){$base=16}
    Set-ITMAdaptiveFont $control $base $control.Font.Style
    $height=[Math]::Max($control.Font.Height+12,[int]($minimum*$q))
    if($control -is [Windows.Forms.Label] -or $control -is [Windows.Forms.Button]){$height=Get-ITMTextHeight $control $width $height}
    if($control -isnot [Windows.Forms.Label] -and $control -isnot [Windows.Forms.Button]){$height=[Math]::Max($height,$control.PreferredSize.Height)}
    $control.AutoSize=$false
    $control.Visible=$true
    $control.SetBounds($x,$y,[Math]::Max(1,$width),$height)
    return $height
}
function Update-ITMPaymentWorkspace($page,$s,$outer,$width,$pad,$margin,$gap,$q) {
    $controls=@($s.Surface.Controls)
    $left=[int]($width*0.52);$right=$width-$left-[int](24*$q);$rx=$pad+$left+[int](24*$q)
    $y=$pad
    $searchLabel=@($controls | Where-Object {$_.Text -eq 'Rechercher un apprenant'})[0]
    $y+=(Set-ITMWorkspaceControl $searchLabel $pad $y $left 26 $q)+$gap
    $y+=(Set-ITMWorkspaceControl $script:paySearch $pad $y $left 32 $q)+$gap
    $script:payLearnerGrid.SetBounds($pad,$y,$left,[int](240*$q));$y=$script:payLearnerGrid.Bottom+$gap
    $buttons=@($controls | Where-Object {$_ -is [Windows.Forms.Button] -and $_.Text -in @('Sélectionner cet apprenant','Effacer la recherche','Tous les paiements','Paiements multiples')})
    $y=Set-ITMPageRow $buttons $left $pad $y $gap $q
    $y+=(Set-ITMWorkspaceControl $script:payCount $pad $y $left 24 $q)+$gap
    $rightY=$pad
    $rightY+=(Set-ITMWorkspaceControl $script:payIdentity $rx $rightY $right 42 $q)+$gap
    $rightY+=(Set-ITMWorkspaceControl $script:paySituation $rx $rightY $right 80 $q)+$gap
    foreach($field in @(@('Montant du reçu validé à la caisse (FC)',$script:payAmount),@('Date du reçu validé à la caisse',$script:payDate))){
        $label=@($controls | Where-Object {$_.Text -eq $field[0]})[0]
        $rightY+=(Set-ITMWorkspaceControl $label $rx $rightY $right 26 $q)+[int](4*$q)
        $rightY+=(Set-ITMWorkspaceControl $field[1] $rx $rightY $right 32 $q)+$gap
    }
    $rightY+=(Set-ITMWorkspaceControl $script:payDateHint $rx $rightY $right 26 $q)+$gap
    $rightY+=(Set-ITMWorkspaceControl $script:paySave $rx $rightY $right 40 $q)+$gap
    $rightY+=(Set-ITMWorkspaceControl $script:peManage $rx $rightY $right 40 $q)+$gap
    $y=[Math]::Max($y,$rightY)+[int](12*$q)
    $historyLabel=@($controls | Where-Object {$_.Text -eq 'Historique des affectations'})[0]
    $y+=(Set-ITMWorkspaceControl $historyLabel $pad $y $width 28 $q)+$gap
    $correct=@($controls | Where-Object {$_.Text -eq 'Corriger le paiement sélectionné'})[0]
    $y=Set-ITMPageRow @($script:payFilterMonth,$correct) $width $pad $y $gap $q
    $height=[Math]::Max([int](230*$q),$page.ClientSize.Height-$y-2*$margin-$pad)
    $script:paymentGrid.SetBounds($pad,$y,$width,$height)
    Set-ITMGridReadability $script:payLearnerGrid $false;Set-ITMGridReadability $script:paymentGrid $false
    $s.Surface.SetBounds($margin,$margin,$outer,($script:paymentGrid.Bottom+$pad))
    $page.AutoScrollMinSize=New-Object Drawing.Size(0,($s.Surface.Bottom+$margin))
}
function Update-ITMExportWorkspace($page,$s,$outer,$width,$pad,$margin,$gap,$q) {
    $controls=@($s.Surface.Controls | Where-Object {-not ((Test-Path function:Test-ScoliaRibbonSource) -and (Test-ScoliaRibbonSource $_))});$y=$pad
    foreach($pattern in @('Documents et rapports','1*Choisir*')){
        $label=@($controls | Where-Object {$_ -is [Windows.Forms.Label] -and $_.Text -like $pattern})[0]
        $y+=(Set-ITMWorkspaceControl $label $pad $y $width 28 $q)+$gap
    }
    foreach($field in @(@('Promotion',$script:exportPromo),@('Mois',$script:exportMonth),@('Rapport',$script:exportType))){
        $label=@($controls | Where-Object {$_ -is [Windows.Forms.Label] -and $_.Text -eq $field[0]})[0]
        $labelWidth=[int](140*$q);$controlWidth=$width-$labelWidth-$gap
        if($width -lt 460*$q){
            $y+=(Set-ITMWorkspaceControl $label $pad $y $width 26 $q)+[int](4*$q)
            $y+=(Set-ITMWorkspaceControl $field[1] $pad $y $width 32 $q)+[int](12*$q)
        }else{
            $h=Set-ITMWorkspaceControl $label $pad $y $labelWidth 32 $q
            $h=[Math]::Max($h,(Set-ITMWorkspaceControl $field[1] ($pad+$labelWidth+$gap) $y $controlWidth 32 $q))
            $y+=$h+[int](12*$q)
        }
    }
    $buttons=@($controls | Where-Object {$_ -is [Windows.Forms.Button] -and $_.Text -like 'Exporter en*'})
    $y=Set-ITMPageRow $buttons $width $pad ($y+$gap) $gap $q
    $label=@($controls | Where-Object {$_.Text -eq 'Contrôle avant export'})[0]
    $y+=(Set-ITMWorkspaceControl $label $pad ($y+$gap) $width 28 $q)+$gap
    $y+=(Set-ITMWorkspaceControl $script:reportRule $pad $y $width 54 $q)+$gap
    $open=@($controls | Where-Object {$_.Text -eq 'Ouvrir le dernier rapport'})[0]
    $y+=(Set-ITMWorkspaceControl $open $pad $y ([Math]::Min($width,[int](300*$q))) 38 $q)+$pad
    $s.Surface.SetBounds($margin,$margin,$outer,[Math]::Max($y,$page.ClientSize.Height-2*$margin))
    $page.AutoScrollMinSize=New-Object Drawing.Size(0,($y+2*$margin))
}
