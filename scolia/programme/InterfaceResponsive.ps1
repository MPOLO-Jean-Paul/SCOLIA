. (Join-Path $PSScriptRoot 'TypographieV71.ps1')
if(-not (Test-Path function:Get-ITMTextHeight)){. (Join-Path $PSScriptRoot 'Lisibilite.ps1')}
if(-not $script:pages){$script:pages=@{}}
$script:adaptivePanels=@{}
$script:adaptiveFonts=@{}
$script:fluidBases=@{};$script:fluidRoots=@{};$script:fluidTables=@{};$script:fluidFlowWidths=@{}
$script:pendingFluidTables=New-Object 'System.Collections.Generic.HashSet[object]'
$script:pendingAdaptivePanels=New-Object 'System.Collections.Generic.HashSet[object]'
$script:pendingDossierLayouts=New-Object 'System.Collections.Generic.HashSet[object]'
$script:responsiveLayoutTimer=New-Object Windows.Forms.Timer;$script:responsiveLayoutTimer.Interval=16
$script:responsiveLayoutTimer.Add_Tick({
    $script:responsiveLayoutTimer.Stop()
    $tables=@($script:pendingFluidTables);$panels=@($script:pendingAdaptivePanels);$forms=@($script:pendingDossierLayouts)
    $script:pendingFluidTables.Clear();$script:pendingAdaptivePanels.Clear();$script:pendingDossierLayouts.Clear()
    foreach($table in $tables){if($table -and -not $table.IsDisposed){Update-ITMFluidTable $table}}
    foreach($panel in $panels){if($panel -and -not $panel.IsDisposed){Update-ITMResponsivePanel $panel}}
    foreach($form in $forms){if($form -and -not $form.IsDisposed){Update-ITMDossierLayout $form}}
})
function Start-ITMResponsiveLayoutTimer {if(-not $script:responsiveLayoutTimer.Enabled){$script:responsiveLayoutTimer.Start()}}
function Request-ITMFluidTable($table){if($table -and -not $table.IsDisposed){[void]$script:pendingFluidTables.Add($table);Start-ITMResponsiveLayoutTimer}}
function Request-ITMResponsivePanel($panel){if($panel -and -not $panel.IsDisposed){[void]$script:pendingAdaptivePanels.Add($panel);Start-ITMResponsiveLayoutTimer}}
function Request-ITMDossierLayout($form){if($form -and -not $form.IsDisposed){[void]$script:pendingDossierLayouts.Add($form);Start-ITMResponsiveLayoutTimer}}
function Get-ITMDpiScale($control) {
    $form=$null;if($control -is [Windows.Forms.Control]){$form=$control.FindForm()}
    if($form){return [Math]::Max(1,[Math]::Min(2.5,$form.CurrentAutoScaleDimensions.Height/96.0))}
    return 1
}
function Get-ITMFluidTextSize($control,[double]$base,[double]$reference=1050) {
    if(Test-Path function:Get-ScoliaInterfaceTextSize){return (Get-ScoliaInterfaceTextSize)}
    return [double]10
}
function Get-ITMBaseTextSize($control) {
    if(-not $script:fluidBases.ContainsKey($control)){
        $script:fluidBases[$control]=[pscustomobject]@{Size=$control.Font.SizeInPoints;Style=$control.Font.Style}
        $control.Add_Disposed({param($sender,$e)$script:fluidBases.Remove($sender)})
    }
    return $script:fluidBases[$control].Size
}
function Apply-ITMFluidTypography($root) {
    if($root.IsDisposed){return}
    if($root -isnot [Windows.Forms.DataGridView]){
        if($root -is [Windows.Forms.Label] -or $root -is [Windows.Forms.ButtonBase] -or $root -is [Windows.Forms.TextBoxBase] -or $root -is [Windows.Forms.ComboBox] -or $root -is [Windows.Forms.NumericUpDown] -or $root -is [Windows.Forms.ListBox] -or $root -is [Windows.Forms.TabControl] -or $root.GetType().Name -eq 'ITMDashboardChart'){
            $base=Get-ITMBaseTextSize $root;Set-ITMAdaptiveFont $root $base $script:fluidBases[$root].Style
        }
        foreach($child in $root.Controls){Apply-ITMFluidTypography $child}
    }elseif(Test-Path function:Set-ITMGridAppearance){Set-ITMGridAppearance $root}
}
function Enable-ITMFluidTypography($root) {
    if($script:fluidRoots.ContainsKey($root)){return};$script:fluidRoots[$root]=$false
    Apply-ITMFluidTypography $root
    # Typography is applied at creation and on preference/theme changes.
    # Resizing only changes geometry; it must not restyle every hidden grid.
    $root.Add_Disposed({param($sender,$e)$script:fluidRoots.Remove($sender)})
}
function Enable-ITMFluidTable($table) {
    if($script:fluidTables.ContainsKey($table)){Update-ITMFluidTable $table;return}
    # Le corps défile si nécessaire ; les commandes de validation restent fixes.
    if($table.Parent -is [Windows.Forms.Form]){
        $form=$table.Parent;$body=New-Object Windows.Forms.Panel;$body.Dock='Fill';$body.AutoScroll=$true
        $form.Controls.Add($body);$body.Controls.Add($table);$body.BringToFront();$form.AutoScroll=$false
    }
    $rows=@()
    for($i=0;$i -lt $table.RowCount;$i++){
        $controls=@($table.Controls | Where-Object {$table.GetRow($_) -eq $i} | Sort-Object { $table.GetColumn($_) })
        if($controls.Count){$rows+=,[pscustomobject]@{Controls=$controls;Choice=($controls.Count -eq 2)}}
    }
    $heights=@{};foreach($c in $table.Controls){$heights[$c]=$c.Height}
    $script:fluidTables[$table]=[pscustomobject]@{Rows=$rows;Heights=$heights;Busy=$false}
    $table.AutoSize=$false;$table.Dock='None'
    if($table.Parent -is [Windows.Forms.ScrollableControl]){$table.Parent.AutoScroll=$true}
    $table.Parent.Add_SizeChanged({param($sender,$e)foreach($child in $sender.Controls){if($script:fluidTables.ContainsKey($child)){Request-ITMFluidTable $child}}})
    $table.Add_Disposed({param($sender,$e)$script:fluidTables.Remove($sender)})
    Update-ITMFluidTable $table
}
function Update-ITMFluidTable($table) {
    $state=$script:fluidTables[$table];if(-not $state -or $state.Busy -or $table.IsDisposed){return}
    $state.Busy=$true;$table.SuspendLayout()
    try{
        $parent=$table.Parent;$q=Get-ITMDpiScale $table
        $saved=$parent.AutoScrollPosition;$parent.AutoScrollPosition=New-Object Drawing.Point(0,0)
        $width=[Math]::Max(60,$parent.ClientSize.Width-$parent.Padding.Horizontal-[Windows.Forms.SystemInformation]::VerticalScrollBarWidth-4)
        $contentWidth=[Math]::Max(20,$width-$table.Padding.Horizontal)
        $narrow=$contentWidth/$q -lt 460;$table.ColumnCount=$(if($narrow){1}else{2})
        $table.ColumnStyles.Clear();$table.RowStyles.Clear()
        if($narrow){[void]$table.ColumnStyles.Add((New-Object Windows.Forms.ColumnStyle([Windows.Forms.SizeType]::Percent,100)))}
        else{[void]$table.ColumnStyles.Add((New-Object Windows.Forms.ColumnStyle([Windows.Forms.SizeType]::Percent,35)));[void]$table.ColumnStyles.Add((New-Object Windows.Forms.ColumnStyle([Windows.Forms.SizeType]::Percent,65)))}
        $table.MaximumSize=New-Object Drawing.Size(0,0);$table.MinimumSize=New-Object Drawing.Size(0,0)
        $index=0;$height=0
        # Reserve all cells before repositioning so automatic placement never adds phantom rows.
        $table.RowCount=[int](($state.Rows | ForEach-Object {if($narrow -and $_.Choice){2}else{1}} | Measure-Object -Sum).Sum)
        foreach($row in $state.Rows){
            $rowHeight=0;$column=0
            foreach($c in $row.Controls){
                $base=Get-ITMBaseTextSize $c;Set-ITMAdaptiveFont $c $base $script:fluidBases[$c].Style
                $cw=$(if(-not $narrow -and $row.Choice){[int]($contentWidth*$(if($column -eq 0){.35}else{.65}))}else{$contentWidth})
                $usable=[Math]::Max(20,$cw-$c.Margin.Horizontal)
                $c.AutoSize=$false;$c.MaximumSize=New-Object Drawing.Size(0,0);$c.MinimumSize=New-Object Drawing.Size(0,0);$c.Dock='Fill'
                $h=[Math]::Max($c.Font.Height+12,[int](36*$q))
                if($c -isnot [Windows.Forms.Label]){$h=[Math]::Max($h,$c.PreferredSize.Height)}
                if(($c -is [Windows.Forms.TextBoxBase] -and $c.Multiline) -or $c -is [Windows.Forms.Panel]){$h=[Math]::Max($h,$state.Heights[$c])}
                if($c -is [Windows.Forms.Label] -or $c -is [Windows.Forms.CheckBox]){
                    $h=Get-ITMTextHeight $c ([Math]::Max(20,$usable-$(if($c -is [Windows.Forms.CheckBox]){[int](24*$q)}else{0}))) $h
                }
                $table.SetCellPosition($c,(New-Object Windows.Forms.TableLayoutPanelCellPosition($column,$index)))
                $table.SetColumnSpan($c,$(if(-not $narrow -and -not $row.Choice){2}else{1}))
                $rowHeight=[Math]::Max($rowHeight,$h+$c.Margin.Vertical)
                if($narrow -and $row.Choice){[void]$table.RowStyles.Add((New-Object Windows.Forms.RowStyle([Windows.Forms.SizeType]::Absolute,$rowHeight)));$height+=$rowHeight;$index++;$rowHeight=0}else{$column++}
            }
            if($rowHeight -gt 0){[void]$table.RowStyles.Add((New-Object Windows.Forms.RowStyle([Windows.Forms.SizeType]::Absolute,$rowHeight)));$height+=$rowHeight;$index++}
        }
        $table.SetBounds($parent.Padding.Left,$parent.Padding.Top,$width,($height+$table.Padding.Vertical+4))
        $footerHeight=0;foreach($child in $parent.Controls){if($child.Dock -eq 'Bottom'){$footerHeight+=$child.Height}}
        $parent.AutoScrollMinSize=New-Object Drawing.Size(0,($height+$table.Padding.Vertical+$parent.Padding.Vertical+$footerHeight+8))
        $parent.AutoScrollPosition=New-Object Drawing.Point([Math]::Abs($saved.X),[Math]::Abs($saved.Y))
    }finally{$table.ResumeLayout();$state.Busy=$false}
}

function Set-ITMAdaptiveFont($control,[double]$size,[Drawing.FontStyle]$style) {
    if($control -is [Windows.Forms.Control] -and -not $script:fluidBases.ContainsKey($control)){
        $script:fluidBases[$control]=[pscustomobject]@{Size=$size;Style=$style}
        $control.Add_Disposed({param($sender,$e)$script:fluidBases.Remove($sender)})
    }
    $size=Get-ITMFluidTextSize $control $size
    $size=Get-ScoliaInterfaceTextSize
    $key=[string]$size+'|'+[string]$style
    if(-not $script:adaptiveFonts.ContainsKey($key)){$script:adaptiveFonts[$key]=New-Object Drawing.Font('Segoe UI',[single]$size,$style)}
    if(-not [object]::ReferenceEquals($control.Font,$script:adaptiveFonts[$key])){$control.Font=$script:adaptiveFonts[$key]}
}
# Compatibility entry point: all workspace surfaces use square native bounds.
function Set-ITMRounded($control,[int]$radius=10) {
    if($control.IsDisposed){return}
    $old=$control.Region;$control.Region=$null;if($old){$old.Dispose()}
}
function Enable-ITMResponsivePanel($panel,[int]$designWidth=1000) {
    if($script:adaptivePanels.ContainsKey($panel)){return}
    $items=@();$bottom=0
    foreach($c in $panel.Controls){
        if($c.Dock -ne 'None'){continue}
        $bounds=$c.Bounds;$bottom=[Math]::Max($bottom,$bounds.Bottom)
        $items+=([pscustomobject]@{Control=$c;Bounds=$bounds;Size=$c.Font.SizeInPoints;Style=$c.Font.Style})
        $c.Anchor='Top,Left'
    }
    $rows=@();$row=@();$top=-1000
    foreach($item in @($items | Sort-Object {$_.Bounds.Top},{$_.Bounds.Left})){
        if($row.Count -and $item.Bounds.Top-$top -gt 22){$rows+=,@($row);$row=@()}
        if(-not $row.Count){$top=$item.Bounds.Top};$row+=$item
    };if($row.Count){$rows+=,@($row)}
    $script:adaptivePanels[$panel]=[pscustomobject]@{Items=$items;Rows=$rows;Width=$designWidth;Bottom=$bottom;Busy=$false}
    $panel.AutoScroll=$true;$panel.AutoScrollMinSize=New-Object Drawing.Size(0,($bottom+24))
    $panel.Add_SizeChanged({param($sender,$e)Request-ITMResponsivePanel $sender})
    $panel.Add_VisibleChanged({param($sender,$e)if($sender.Visible){Request-ITMResponsivePanel $sender}})
    $panel.Add_Disposed({param($sender,$e)$script:adaptivePanels.Remove($sender)})
    Update-ITMResponsivePanel $panel
}
function Update-ITMResponsivePanel($panel) {
    if($panel.IsDisposed -or -not $script:adaptivePanels.ContainsKey($panel)){return}
    $state=$script:adaptivePanels[$panel];if($state.Busy -or $panel.ClientSize.Width -lt 200){return}
    $state.Busy=$true;$panel.SuspendLayout()
    try{
        $savedScroll=$panel.AutoScrollPosition;$panel.AutoScrollPosition=New-Object Drawing.Point(0,0)
        if($panel -eq $script:exportCardsPage){Update-ITMSchoolLayout $panel;return}
        if($panel -eq $script:pages['Apprenants']){Update-ITMLearnerLayout $panel;return}
        if($panel -eq $script:pages['Tableau de bord']){Update-ITMDashboardLayout $panel;return}
        if($script:modernPages -and $script:modernPages.ContainsKey($panel)){Update-ITMPageDesign $panel;return}
        $q=Get-ITMDpiScale $panel;$pad=[int](12*$q);$gap=[int](10*$q)
        $width=[Math]::Max(40,$panel.ClientSize.Width-2*$pad-[Windows.Forms.SystemInformation]::VerticalScrollBarWidth)
        $y=$pad
        foreach($row in $state.Rows){
            $line=@();$need=0
            foreach($item in $row){
                if($item.Control.IsDisposed -or ($panel.Visible -and -not $item.Control.Visible)){continue}
                $minimum=[Math]::Min($width,[int]($(if($item.Control -is [Windows.Forms.PictureBox]){44}elseif($item.Control -is [Windows.Forms.Button]){150}else{170})*$q))
                if($line.Count -and $need+$gap+$minimum -gt $width){$y=Set-ITMFluidRow $line $pad $y $width $gap $q;$line=@();$need=0}
                if($line.Count){$need+=$gap};$need+=$minimum;$line+=$item
            }
            if($line.Count){$y=Set-ITMFluidRow $line $pad $y $width $gap $q}
        }
        $panel.AutoScrollMinSize=New-Object Drawing.Size(0,($y+$pad))
        if($panel -eq $script:pages['Tableau de bord']){Update-ITMDashboardLayout $panel}
    }finally{$panel.ResumeLayout();if($savedScroll){$panel.AutoScrollPosition=New-Object Drawing.Point([Math]::Abs($savedScroll.X),[Math]::Abs($savedScroll.Y))};$state.Busy=$false}
}
function Set-ITMDossierFont($control,[double]$size,[Drawing.FontStyle]$style) {
    # Replier la grille plutot que reduire la taille de lecture choisie.
    $size=Get-ScoliaInterfaceTextSize
    $key=[string]$size+'|'+[string]$style
    if(-not $script:adaptiveFonts.ContainsKey($key)){$script:adaptiveFonts[$key]=New-Object Drawing.Font('Segoe UI',[single]$size,$style)}
    $control.Font=$script:adaptiveFonts[$key]
}
function Get-ITMDossierLabelHeight($control,[int]$width,[int]$minimum) {
    return [Math]::Max($minimum,(Get-ITMTextHeight $control $width 0)-8)
}
function Enable-ITMDossierLayout($dialog,$body,$header,$footer,$labels,$fields,$addressLabel,$address,$save,$cancel) {
    if(Test-Path function:Enable-ITMDialogShortcuts){Enable-ITMDialogShortcuts $dialog}
    [ITMVisualTheme]::Apply($dialog)
    $area=[Windows.Forms.Screen]::FromControl($dialog).WorkingArea
    $q=Get-ITMDpiScale $dialog
    $dialog.MinimumSize=New-Object Drawing.Size([int][Math]::Min(440*$q,$area.Width-32),[int][Math]::Min(420*$q,$area.Height-32))
    $dialog.Size=New-Object Drawing.Size([int][Math]::Min(1040*$q,$area.Width-48),[int][Math]::Min(820*$q,$area.Height-48))
    $body.AutoScroll=$true;$body.AutoScrollMinSize=New-Object Drawing.Size(0,0)
    $body.TabIndex=0;$footer.TabIndex=1;$header.TabStop=$false
    $groups=@();$wrappers=@{}
    $specs=@(
        @{Title="Identité de l'apprenant";Indexes=@(0,1,2,3,4,5)},
        @{Title='Scolarité';Indexes=@(8,9,10)},
        @{Title='Contacts et résidence';Indexes=@(6,7)}
    )
    foreach($spec in $specs){
        $card=New-Object ITMFormSection;$card.TabIndex=$groups.Count;$body.Controls.Add($card)
        $heading=New-Object ITMReadableLabel;$heading.Text=$spec.Title;$heading.AutoSize=$false
        $heading.Font=New-Object Drawing.Font('Segoe UI',12,[Drawing.FontStyle]::Bold)
        $heading.ForeColor=[Drawing.Color]::FromArgb(30,41,59);$heading.Tag='scolia:heading';$card.Controls.Add($heading)
        $tab=0
        foreach($index in $spec.Indexes){
            $label=$labels[$index];$label.AutoSize=$false;$label.Tag='scolia:field-label';$card.Controls.Add($label)
            Set-ITMDossierFont $label 10 ([Drawing.FontStyle]::Regular)
            Set-ITMDossierFont $fields[$index] 11 ([Drawing.FontStyle]::Regular)
            $wrapper=New-Object ITMFieldSurface($fields[$index]);$wrapper.TabIndex=$tab;$wrapper.AccessibleName=$label.Text;$card.Controls.Add($wrapper)
            $wrappers[$index]=$wrapper;$tab++
        }
        $groups+=,[pscustomobject]@{Card=$card;Heading=$heading;Indexes=$spec.Indexes}
    }
    $contactCard=$groups[2].Card;$addressLabel.AutoSize=$false;$addressLabel.Tag='scolia:field-label';$contactCard.Controls.Add($addressLabel)
    $addressSurface=New-Object ITMFieldSurface($address);$addressSurface.TabIndex=2;$contactCard.Controls.Add($addressSurface)
    Set-ITMDossierFont $addressLabel 10 ([Drawing.FontStyle]::Regular)
    Set-ITMDossierFont $address 11 ([Drawing.FontStyle]::Regular)
    $hint=New-Object ITMReadableLabel;$hint.Text='Ctrl+S : enregistrer';$hint.ForeColor=[Drawing.Color]::FromArgb(100,116,139)
    $hint.Font=New-Object Drawing.Font('Segoe UI',9);$hint.TextAlign='MiddleLeft';$footer.Controls.Add($hint)
    $save.TabIndex=1;$cancel.TabIndex=0
    $state=[pscustomobject]@{Body=$body;Header=$header;HeaderLines=@($header.Controls | Where-Object {$_ -is [Windows.Forms.Label]} | Sort-Object Top);Footer=$footer;Labels=$labels;Fields=$fields;Groups=$groups;Wrappers=$wrappers;AddressLabel=$addressLabel;Address=$address;AddressSurface=$addressSurface;Save=$save;Cancel=$cancel;Hint=$hint;Busy=$false}
    $script:adaptivePanels[$dialog]=$state
    $textSize=10
    for($i=0;$i -lt $state.HeaderLines.Count;$i++){
        Set-ITMDossierFont $state.HeaderLines[$i] $(if($i -eq 0){20}else{$textSize}) $(if($i -eq 0){[Drawing.FontStyle]::Bold}else{[Drawing.FontStyle]::Regular})
    }
    Set-ITMDossierFont $cancel 10 ([Drawing.FontStyle]::Regular)
    $dialog.Add_Resize({param($sender,$e)Request-ITMDossierLayout $sender})
    $body.Add_SizeChanged({param($sender,$e)$owner=$sender.FindForm();if($owner){Request-ITMDossierLayout $owner}})
    $dialog.Add_Shown({param($sender,$e)Update-ITMDossierLayout $sender})
    $dialog.Add_Disposed({param($sender,$e)$script:adaptivePanels.Remove($sender)})
    Update-ITMDossierLayout $dialog
}
function Update-ITMDossierLayout($dialog) {
    $s=$script:adaptivePanels[$dialog];if(-not $s -or $s.Busy -or $dialog.IsDisposed -or $dialog.Disposing -or $s.Body.IsDisposed){return}
    $s.Busy=$true;$saved=$s.Body.AutoScrollPosition;$s.Body.SuspendLayout()
    try{
        $s.Body.AutoScrollPosition=New-Object Drawing.Point(0,0)
        $q=Get-ITMDpiScale $dialog;$pad=[int](18*$q);$gap=[int](12*$q);$inner=[int](16*$q)
        $textSize=10
        if($script:displayPreferences -and $script:displayPreferences.PSObject.Properties['TextSize']){$textSize=[Math]::Max(10,[Math]::Min(14,[double]$script:displayPreferences.TextSize))}
        $headerWidth=[Math]::Max(40,$dialog.ClientSize.Width-2*$pad);$headerY=[int](16*$q)
        for($i=0;$i -lt $s.HeaderLines.Count;$i++){
            $line=$s.HeaderLines[$i]
            if([string]::IsNullOrWhiteSpace($line.Text)){$line.Visible=$false;continue}
            $line.Visible=$true
            Set-ITMDossierFont $line $(if($i -eq 0){20}else{$textSize}) $(if($i -eq 0){[Drawing.FontStyle]::Bold}else{[Drawing.FontStyle]::Regular})
            $height=Get-ITMTextHeight $line $headerWidth ([int]($(if($i -eq 0){32}else{24})*$q))
            $line.SetBounds($pad,$headerY,$headerWidth,$height);$headerY+=$height+[int](4*$q)
        }
        $s.Header.Height=$headerY+[int](12*$q)
        $s.Footer.Height=[int](64*$q)
        Set-ITMDossierFont $s.Cancel 10 ([Drawing.FontStyle]::Regular)
        Set-ITMDossierFont $s.Save 10 ([Drawing.FontStyle]::Bold)
        $actionWidth=[Math]::Max(40,$dialog.ClientSize.Width-2*$pad)
        $cancelWidth=[int][Math]::Min(104*$q,$actionWidth*.28)
        $saveWidth=[int][Math]::Min(250*$q,$actionWidth-$cancelWidth-$gap)
        $actionHeight=[Math]::Max((Get-ITMTextHeight $s.Cancel $cancelWidth ([int](42*$q))),(Get-ITMTextHeight $s.Save $saveWidth ([int](42*$q))))
        $s.Footer.Height=$actionHeight+[int](28*$q)
        $s.Cancel.SetBounds($pad,[int](14*$q),$cancelWidth,$actionHeight)
        $s.Save.SetBounds(($dialog.ClientSize.Width-$pad-$saveWidth),[int](14*$q),$saveWidth,$actionHeight)
        $hintWidth=$s.Save.Left-$s.Cancel.Right-2*$gap
        $s.Hint.Visible=$hintWidth -ge 155*$q
        $s.Hint.SetBounds(($s.Cancel.Right+$gap),[int](14*$q),[Math]::Max(1,$hintWidth),$actionHeight)
        $reserve=$(if($s.Body.VerticalScroll.Visible){0}else{[Windows.Forms.SystemInformation]::VerticalScrollBarWidth})
        $outer=[Math]::Max(80,$s.Body.ClientSize.Width-2*$pad-$reserve)
        $paired=$outer/$q -ge 880
        $pairY=0;$pairBottom=0;$groupIndex=0
        $y=[int](18*$q)
        foreach($group in $s.Groups){
            $card=$group.Card;$card.SuspendLayout()
            $cardWidth=$outer;$cardX=$pad;$cardY=$y
            $leftWidth=[int](($outer-$gap)*.38)
            if($paired -and $groupIndex -eq 1){$cardWidth=$leftWidth;$cardY=$pairY}
            if($paired -and $groupIndex -eq 2){$cardWidth=$outer-$leftWidth-$gap;$cardX=$pad+$leftWidth+$gap;$cardY=$pairY}
            $width=[Math]::Max(40,$cardWidth-2*$inner)
            $columns=$(if($width/$q -ge 780){3}elseif($width/$q -ge 470){2}else{1})
            try{
                $cols=[Math]::Min($columns,$group.Indexes.Count);$fieldWidth=[int](($width-($cols-1)*$gap)/$cols)
                Set-ITMDossierFont $group.Heading ([Math]::Max(12,$textSize)) ([Drawing.FontStyle]::Bold)
                $headingHeight=Get-ITMDossierLabelHeight $group.Heading $width ([int](26*$q))
                $group.Heading.SetBounds($inner,[int](16*$q),$width,$headingHeight)
                $rowY=$group.Heading.Bottom+[int](14*$q)
                for($start=0;$start -lt $group.Indexes.Count;$start+=$cols){
                    $labelHeight=[int](22*$q);$fieldHeight=[int](40*$q)
                    for($col=0;$col -lt $cols -and $start+$col -lt $group.Indexes.Count;$col++){
                        $index=$group.Indexes[$start+$col]
                        Set-ITMDossierFont $s.Labels[$index] $textSize ([Drawing.FontStyle]::Regular)
                        Set-ITMDossierFont $s.Fields[$index] ([Math]::Max(11,$textSize)) ([Drawing.FontStyle]::Regular)
                        $labelHeight=[Math]::Max($labelHeight,(Get-ITMDossierLabelHeight $s.Labels[$index] $fieldWidth 0))
                        $fieldHeight=[Math]::Max($fieldHeight,[Math]::Max($s.Fields[$index].PreferredSize.Height,$s.Fields[$index].Font.Height+4)+[int](12*$q))
                    }
                    for($col=0;$col -lt $cols -and $start+$col -lt $group.Indexes.Count;$col++){
                        $index=$group.Indexes[$start+$col];$x=$inner+$col*($fieldWidth+$gap)
                        $s.Labels[$index].SetBounds($x,$rowY,$fieldWidth,$labelHeight)
                        $wrapper=$s.Wrappers[$index];$wrapper.Padding=New-Object Windows.Forms.Padding([int](12*$q),[int](6*$q),[int](28*$q),[int](6*$q))
                        $wrapper.SetBounds($x,($rowY+$labelHeight+[int](5*$q)),$fieldWidth,$fieldHeight)
                    }
                    $rowY+=$labelHeight+$fieldHeight+[int](14*$q)
                }
                if($group -eq $s.Groups[2]){
                    Set-ITMDossierFont $s.AddressLabel $textSize ([Drawing.FontStyle]::Regular)
                    Set-ITMDossierFont $s.Address ([Math]::Max(11,$textSize)) ([Drawing.FontStyle]::Regular)
                    $height=Get-ITMDossierLabelHeight $s.AddressLabel $width ([int](22*$q))
                    $s.AddressLabel.SetBounds($inner,$rowY,$width,$height)
                    $s.AddressSurface.Padding=New-Object Windows.Forms.Padding([int](12*$q),[int](8*$q),[int](12*$q),[int](8*$q))
                    $s.AddressSurface.SetBounds($inner,($rowY+$height+[int](5*$q)),$width,[int](66*$q))
                    $rowY=$s.AddressSurface.Bottom+[int](19*$q)
                }
                $card.SetBounds($cardX,$cardY,$cardWidth,($rowY+[int](2*$q)))
                $y=$card.Bottom+$gap
                if($groupIndex -eq 0){$pairY=$y}
                if($paired -and $groupIndex -eq 1){$pairBottom=$card.Bottom}
                if($paired -and $groupIndex -eq 2){
                    $pairHeight=[Math]::Max($pairBottom,$card.Bottom)-$pairY
                    $s.Groups[1].Card.Height=$pairHeight;$card.Height=$pairHeight;$y=$card.Bottom+$gap
                }
                $groupIndex++
            }finally{$card.ResumeLayout()}
        }
        $s.Body.AutoScroll=$true;$s.Body.AutoScrollMinSize=New-Object Drawing.Size(0,($y+[int](8*$q)))
    }finally{
        $s.Body.ResumeLayout();$s.Body.AutoScrollPosition=New-Object Drawing.Point([Math]::Abs($saved.X),[Math]::Abs($saved.Y));$s.Busy=$false
    }
    if(Test-Path function:Apply-ITMThemeForm){Apply-ITMThemeForm $dialog}
}
function Set-ITMDossierFieldError($control,[string]$message) {
    if($script:editErrors){$script:editErrors.SetError($control,$message)}
    if($control.Parent -and $control.Parent.GetType().Name -eq 'ITMFieldSurface'){$control.Parent.Invalid=-not [string]::IsNullOrWhiteSpace($message)}
}
function Test-ITMDossierFields([string[]]$fields=@('Nom','PostNom','Prenom','Sexe','Naissance','Lieu','Contact1','Contact2','Promotion','Annee','Statut')) {
    $valid=$true;$first=$null;$script:editErrors.Clear()
    foreach($control in $script:editControls.Values){if($control.Parent -and $control.Parent.GetType().Name -eq 'ITMFieldSurface'){$control.Parent.Invalid=$false}}
    foreach($field in @($fields | Where-Object {$_ -ne 'Contact2'})){
        $control=$script:editControls[$field]
        if(-not $control.Text.Trim()){Set-ITMDossierFieldError $control 'Ce champ doit être renseigné.';$valid=$false;if(-not $first){$first=$control}}
    }
    foreach($field in @($fields | Where-Object {$_ -in @('Contact1','Contact2')})){
        $control=$script:editControls[$field]
        if($control.Text -and $control.Text -notmatch '^[0-9]{10}$'){Set-ITMDossierFieldError $control 'Saisissez exactement 10 chiffres.';$valid=$false;if(-not $first){$first=$control}}
    }
    foreach($field in @($fields | Where-Object {$_ -in @('Nom','PostNom','Prenom')})){
        $control=$script:editControls[$field]
        if($control.Text.Trim() -and $control.Text.Trim() -notmatch '^[A-Za-zÀ-ÖØ-öø-ÿ ''\u2019\-]+$'){Set-ITMDossierFieldError $control "Lettres, espaces, apostrophes et traits d'union uniquement.";$valid=$false;if(-not $first){$first=$control}}
    }
    if('Naissance' -in $fields){
        $control=$script:editControls.Naissance;$problem=Get-ITMBirthDateProblem $control
        if($problem){Set-ITMDossierFieldError $control $problem;$valid=$false;if(-not $first){$first=$control}}
    }
    if($first){
        if($script:editFeedback -and -not $script:editFeedback.IsDisposed){$script:editFeedback.Tag='scolia:feedback:error';$script:editFeedback.Text='Vérifiez les champs signalés avant de sauvegarder.'}
        $owner=$first.FindForm()
        if($owner -and $script:adaptivePanels.ContainsKey($owner)){$script:adaptivePanels[$owner].Body.ScrollControlIntoView($first.Parent)}
        $page=$first.Parent.Parent
        if($page -is [Windows.Forms.TabPage]){$page.Parent.SelectedTab=$page}
        $first.Focus()
    }
    return $valid
}
function Enable-ITMDialogResponsive($dialog) {
    $dialog.AutoScaleMode='Dpi'
    if(Test-Path function:Enable-ITMDialogShortcuts){Enable-ITMDialogShortcuts $dialog}
    if("ITMVisualTheme" -as [type]){[ITMVisualTheme]::Apply($dialog)}
    if(Test-Path function:Apply-ITMDisplayTree){Apply-ITMDisplayTree $dialog}
    if(Test-Path function:Apply-ITMThemeForm){Apply-ITMThemeForm $dialog}
    $screenOwner=$(if($script:main -and -not $script:main.IsDisposed){$script:main}else{$dialog})
    $area=[Windows.Forms.Screen]::FromControl($screenOwner).WorkingArea
    $dialog.MinimumSize=New-Object Drawing.Size([Math]::Min(320,$area.Width-32),[Math]::Min(300,$area.Height-32))
    $dialog.Size=New-Object Drawing.Size([Math]::Min($dialog.Width,$area.Width-32),[Math]::Min($dialog.Height,$area.Height-32))
    $body=@($dialog.Controls | Where-Object {$_.Dock -eq 'Fill' -and $_ -is [Windows.Forms.Panel]})
    if($body.Count){foreach($panel in $body){$design=[Math]::Max(500,(@($panel.Controls | Where-Object {$_.Dock -eq 'None'} | ForEach-Object {$_.Right+24}) | Measure-Object -Maximum).Maximum);Enable-ITMResponsivePanel $panel $design}}else{$design=[Math]::Max(500,(@($dialog.Controls | Where-Object {$_.Dock -eq 'None'} | ForEach-Object {$_.Right+24}) | Measure-Object -Maximum).Maximum);Enable-ITMResponsivePanel $dialog $design}
    foreach($footer in @($dialog.Controls | Where-Object {$_ -is [Windows.Forms.FlowLayoutPanel]})){
        $footer.AutoSize=$true;$footer.AutoSizeMode='GrowAndShrink';$footer.WrapContents=$true;$footer.FlowDirection='RightToLeft';$footer.BackColor=[Drawing.Color]::FromArgb(245,245,245)
        foreach($button in $footer.Controls){$button.Margin=New-Object Windows.Forms.Padding(4);$script:fluidFlowWidths[$button]=$button.Width;$button.Add_Disposed({param($sender,$e)$script:fluidFlowWidths.Remove($sender)})}
        $footer.Add_SizeChanged({param($sender,$e)
            $w=[Math]::Max(40,$sender.ClientSize.Width-$sender.Padding.Horizontal-16)
            foreach($c in $sender.Controls){$c.Width=[Math]::Min($w,$script:fluidFlowWidths[$c]);$c.Height=Get-ITMTextHeight $c $c.Width 32}
        })
    }
    Enable-ITMFluidTypography $dialog
    $dialog.Add_Resize({param($sender,$e)foreach($c in $sender.Controls){if($c -is [Windows.Forms.TextBox] -and $c.Dock -eq 'Fill'){Set-ITMAdaptiveFont $c ([Math]::Min(11,[Math]::Max(9,$sender.ClientSize.Width/86))) ([Drawing.FontStyle]::Regular)}}})
    $dialog.Add_Shown({param($sender,$e)foreach($c in $sender.Controls){if($script:adaptivePanels.ContainsKey($c)){Update-ITMResponsivePanel $c}};if($script:adaptivePanels.ContainsKey($sender)){Update-ITMResponsivePanel $sender}})
}

function Set-ITMFluidRow($items,$pad,$y,$width,$gap,$q) {
    $total=($items | ForEach-Object {$_.Bounds.Width} | Measure-Object -Sum).Sum
    $usable=$width-($items.Count-1)*$gap;$x=$pad;$height=0
    $minimumTotal=0;foreach($item in $items){$minimumTotal+=[Math]::Min($width,[int]($(if($item.Control -is [Windows.Forms.PictureBox]){44}elseif($item.Control -is [Windows.Forms.Button]){150}else{170})*$q))}
    $extra=[Math]::Max(0,$usable-$minimumTotal)
    foreach($item in $items){
        $c=$item.Control;if($c.IsDisposed){continue}
        $minimum=[Math]::Min($width,[int]($(if($item.Control -is [Windows.Forms.PictureBox]){44}elseif($item.Control -is [Windows.Forms.Button]){150}else{170})*$q))
        $cw=[Math]::Max(20,[int]($minimum+$extra*$item.Bounds.Width/[Math]::Max(1,$total)))
        Set-ITMAdaptiveFont $c $item.Size $item.Style
        $h=[Math]::Max($c.PreferredSize.Height,[Math]::Max($c.Font.Height+12,[int](32*$q)))
        if($c -is [Windows.Forms.Label] -or $c -is [Windows.Forms.CheckBox] -or $c -is [Windows.Forms.Button]){
            $h=Get-ITMTextHeight $c $cw $h
            if($c -is [Windows.Forms.CheckBox]){$h=Get-ITMTextHeight $c ([Math]::Max(20,$cw-[int](24*$q))) $h}
        }elseif($c -is [Windows.Forms.PictureBox]){$h=[int]([Math]::Min(64,$item.Bounds.Height)*$q)}elseif($c -is [Windows.Forms.DataGridView]){
            $h=[Math]::Max([int](150*$q),[Math]::Min([int](340*$q),$c.Parent.ClientSize.Height-$y-$pad));Set-ITMGridReadability $c $false
        }elseif($c -is [Windows.Forms.TextBox] -and $c.Multiline){$h=[Math]::Max([int](80*$q),[int]($item.Bounds.Height*[Math]::Min(1.2,$width/700.0)))}
        if($c -is [Windows.Forms.Panel] -or $c -is [Windows.Forms.GroupBox]){$h=[Math]::Max($h,$item.Bounds.Height)}
        $c.AutoSize=$false;$c.SetBounds($x,$y,$cw,$h);$height=[Math]::Max($height,$h);$x+=$cw+$gap
    }
    return ($y+$height+$gap)
}

# Limit this pass to native wrapping toolbars; custom ribbons retain their own layout.
function Update-ScoliaFlowSpacing($Panel){
 if(-not $Panel.WrapContents -or $Panel.FlowDirection -notin @('LeftToRight','RightToLeft')){return}
 $width=[Math]::Max(24,$Panel.ClientSize.Width-$Panel.Padding.Horizontal)
 $q=1;if(Test-Path function:Get-ITMDpiScale){$q=Get-ITMDpiScale $Panel}
 $Panel.SuspendLayout()
 try{
  foreach($control in @($Panel.Controls)){
   if($control.IsDisposed -or $control.Dock -ne 'None'){continue}
   $available=[Math]::Max(20,$width-$control.Margin.Horizontal)
   if($control -is [Windows.Forms.ButtonBase]){
    $natural=[Windows.Forms.TextRenderer]::MeasureText($control.Text,$control.Font).Width+$control.Padding.Horizontal+[int](32*$q)
    if($control.Image){$natural+=$control.Image.Width+[int](8*$q)}
    $control.AutoSize=$false;$control.Width=[Math]::Min($available,[Math]::Max([int](104*$q),$natural))
    $textWidth=$control.Width
    if($control.Image){$textWidth-=$control.Image.Width+[int](8*$q)}
    if($control -is [Windows.Forms.CheckBox] -or $control -is [Windows.Forms.RadioButton]){$textWidth-=[int](24*$q)}
    $control.Height=Get-ITMTextHeight $control ([Math]::Max(20,$textWidth)) ([int](38*$q))
   }elseif($control -is [Windows.Forms.Label]){
    $control.AutoSize=$true;$control.MaximumSize=New-Object Drawing.Size($available,0)
   }elseif($control -is [Windows.Forms.TextBoxBase] -or $control -is [Windows.Forms.ComboBox] -or $control -is [Windows.Forms.DateTimePicker] -or $control -is [Windows.Forms.NumericUpDown]){
    $control.MaximumSize=New-Object Drawing.Size($available,0)
   }
  }
 }finally{$Panel.ResumeLayout($true)}
}
