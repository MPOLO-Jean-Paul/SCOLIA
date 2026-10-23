function Get-ITMTextHeight($control,[int]$width,[int]$minimum=28) {
    $available=[Math]::Max(1,$width-$control.Padding.Horizontal-8)
    $size=[Windows.Forms.TextRenderer]::MeasureText($control.Text,$control.Font,(New-Object Drawing.Size($available,10000)),([Windows.Forms.TextFormatFlags]::WordBreak -bor [Windows.Forms.TextFormatFlags]::TextBoxControl -bor [Windows.Forms.TextFormatFlags]::NoPrefix))
    return [Math]::Max($minimum,$size.Height+$control.Padding.Vertical+12)
}
$script:gridReadabilityBusy=New-Object 'System.Collections.Generic.HashSet[System.Windows.Forms.DataGridView]'
function Set-ITMGridReadability($grid,[bool]$ResizeRows=$true) {
    if($grid -isnot [Windows.Forms.DataGridView] -or $grid.IsDisposed -or $grid.Disposing){return}
    if(-not $script:gridReadabilityBusy.Add($grid)){return}
    try {
        if(Get-Command Set-ITMGridAppearance -ErrorAction SilentlyContinue){Set-ITMGridAppearance $grid $false}
        # Fit all visible columns to the viewport. Wrapped rows retain all values.
        $grid.AutoSizeColumnsMode='None';$grid.ColumnHeadersHeightSizeMode='AutoSize'
        $grid.ColumnHeadersDefaultCellStyle.WrapMode='True';$grid.DefaultCellStyle.WrapMode='True'
        $grid.ScrollBars='Both';$grid.AutoSizeRowsMode='None';$grid.ShowCellToolTips=$true
        $visible=@($grid.Columns | Where-Object {$_.Visible})
        if(-not $visible.Count){return}
        $available=[Math]::Max(2*$visible.Count,$grid.ClientSize.Width-[Windows.Forms.SystemInformation]::VerticalScrollBarWidth-$grid.RowHeadersWidth-4)
        if(-not $grid.RowHeadersVisible){$available+=$grid.RowHeadersWidth}
        $total=0;$weights=@();$minimums=@();$minimumTotal=0
        $q=1;if(Get-Command Get-ITMDpiScale -ErrorAction SilentlyContinue){$q=Get-ITMDpiScale $grid}
        foreach($col in $visible){
            $weight=1.0
            if($col.HeaderText -match 'Nom complet|Observation|Motif|Adresse'){$weight=2.0}
            elseif($col.HeaderText -match 'Nom|Contact|Date|Période|Reste|Montant|Mensualité|Total'){$weight=1.35}
            $weights+=,$weight;$total+=$weight
            $minimum=72
            if($col.HeaderText -match 'Nom complet|Observation|Motif|Adresse'){$minimum=150}
            elseif($col.HeaderText -match 'Nom|Contact|Montant|Mensualité|Total|Reste'){$minimum=110}
            elseif($col.HeaderText -match 'Date|Période'){$minimum=100}
            if($col -is [Windows.Forms.DataGridViewCheckBoxColumn]){$minimum=[int](44*$q)}
            else{$minimum=[int][Math]::Max($minimum*$q,$grid.Font.Height*5)}
            $minimums+=,$minimum;$minimumTotal+=$minimum
        }
        $remaining=[Math]::Max($available,$minimumTotal);$extra=[Math]::Max(0,$available-$minimumTotal)
        for($i=0;$i -lt $visible.Count;$i++){
            $col=$visible[$i];if($col.DataGridView -ne $grid){continue}
            $col.MinimumWidth=$minimums[$i];$col.AutoSizeMode='None'
            $width=$(if($i -eq $visible.Count-1){$remaining}else{[int]($minimums[$i]+[Math]::Floor($extra*$weights[$i]/$total))})
            $remainingMinimum=0;for($j=$i+1;$j -lt $minimums.Count;$j++){$remainingMinimum+=$minimums[$j]}
            $width=[Math]::Min($remaining-$remainingMinimum,$width);$remaining-=$width
            if($col.Width -ne $width){$col.Width=$width}
        }
        if($ResizeRows -and $grid.Rows.Count){$grid.AutoResizeRows([Windows.Forms.DataGridViewAutoSizeRowsMode]::DisplayedCellsExceptHeaders)}
    } finally { [void]$script:gridReadabilityBusy.Remove($grid) }
}
