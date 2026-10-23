# Display preferences belong to the current Windows user, outside school data.
$script:displayGridFonts=@{}
$script:displayFieldsSync=$false;$script:displayChanging=$false
function Get-ITMDefaultDisplayPreferences {
    return [pscustomobject]@{Theme='Clair';WindowMode='Une seule fenêtre';RibbonMode='Onglets et commandes';TextSize=10;Density='Confortable';Tips=$true;Alternating=$true;Maximized=$false}
}
function Read-ITMDisplayPreferences {
    $p=Get-ITMDefaultDisplayPreferences
    try{
        $saved=Get-ItemProperty -LiteralPath 'HKCU:\Software\ITMCD\Secretariat\Affichage' -ErrorAction Stop
        # One registry value commits the full set; legacy values remain readable.
        if($saved.PSObject.Properties['SettingsJson']){$saved=[string]$saved.SettingsJson | ConvertFrom-Json -ErrorAction Stop}
        if($saved.PSObject.Properties['Theme'] -and [string]$saved.Theme -in @('Clair','Sombre','Système')){$p.Theme=[string]$saved.Theme}
        if($saved.PSObject.Properties['WindowMode'] -and [string]$saved.WindowMode -in @('Une seule fenêtre','Plusieurs fenêtres')){$p.WindowMode=[string]$saved.WindowMode}
        if($saved.PSObject.Properties['RibbonMode'] -and [string]$saved.RibbonMode -in @('Onglets et commandes','Onglets seuls')){$p.RibbonMode=[string]$saved.RibbonMode}
        if([int]$saved.TextSize -in @(9,10,11,12,14)){$p.TextSize=[int]$saved.TextSize}
        if([string]$saved.Density -in @('Compact','Confortable','Aéré')){$p.Density=[string]$saved.Density}
        foreach($name in @('Tips','Alternating','Maximized')){if($saved.PSObject.Properties[$name] -and [int]$saved.$name -in @(0,1)){$p.$name=[bool][int]$saved.$name}}
    }catch{}
    return $p
}
$script:displayPreferences=Read-ITMDisplayPreferences
function Save-ITMDisplayPreferences {
    $key='HKCU:\Software\ITMCD\Secretariat\Affichage'
    $json=$script:displayPreferences | ConvertTo-Json -Compress -ErrorAction Stop
    if(-not (Test-Path -LiteralPath $key)){[void](New-Item -Path $key -Force -ErrorAction Stop)}
    # A single value prevents a mixture of old and new options after an interrupted save.
    [void](New-ItemProperty -Path $key -Name SettingsJson -Value $json -PropertyType String -Force -ErrorAction Stop)
}
function Set-ITMGridAppearance($grid,[bool]$ResizeRows=$true) {
    $p=$script:displayPreferences;if(-not $p -or $grid.IsDisposed -or $grid.Disposing){return}
    $effective=[double]$p.TextSize
    # Keep the user's chosen point size; narrow columns wrap instead of resetting it.
    $key=[string]$effective;$boldKey=$key+'|Bold'
    if(-not $script:displayGridFonts.ContainsKey($key)){$script:displayGridFonts[$key]=New-Object Drawing.Font('Segoe UI',[single]$effective)}
    if(-not $script:displayGridFonts.ContainsKey($boldKey)){$script:displayGridFonts[$boldKey]=New-Object Drawing.Font('Segoe UI',[single]$effective,[Drawing.FontStyle]::Bold)}
    $font=$script:displayGridFonts[$key]
    $grid.Font=$font;$grid.DefaultCellStyle.Font=$font
    $grid.RowsDefaultCellStyle.Font=$font;$grid.AlternatingRowsDefaultCellStyle.Font=$font
    $grid.ColumnHeadersDefaultCellStyle.Font=$script:displayGridFonts[$boldKey]
    $padding=switch($p.Density){'Compact'{1};'Aéré'{7};default{4}}
    $spacing=New-Object Windows.Forms.Padding(2,$padding,2,$padding)
    $grid.DefaultCellStyle.Padding=$spacing;$grid.RowsDefaultCellStyle.Padding=$spacing;$grid.AlternatingRowsDefaultCellStyle.Padding=$spacing
    $height=[Math]::Max(24,$font.Height+2*$padding+6)
    $grid.RowTemplate.MinimumHeight=22;$grid.RowTemplate.Height=$height
    $grid.DefaultCellStyle.WrapMode='True';$grid.RowsDefaultCellStyle.WrapMode='True';$grid.AlternatingRowsDefaultCellStyle.WrapMode='True'
    foreach($column in $grid.Columns){$column.DefaultCellStyle.Font=$font;$column.DefaultCellStyle.WrapMode='True';$column.HeaderCell.Style.Font=$script:displayGridFonts[$boldKey]}
    $grid.AutoSizeRowsMode='None';$grid.ColumnHeadersHeightSizeMode='AutoSize'
    $dark=$false;if(Get-Command Get-ITMEffectiveDarkTheme -ErrorAction SilentlyContinue){$dark=Get-ITMEffectiveDarkTheme}
    $base=$(if($dark){[Drawing.Color]::FromArgb(36,42,51)}else{[Drawing.Color]::White})
    $alternate=$(if($dark){[Drawing.Color]::FromArgb(40,47,57)}else{[Drawing.Color]::FromArgb(249,250,252)})
    $grid.AlternatingRowsDefaultCellStyle.BackColor=$(if($p.Alternating){$alternate}else{$base})
    # Recalculate existing visible rows now, without materializing every row in a large list.
    if($ResizeRows -and $grid.Columns.Count -gt 0 -and $grid.Rows.Count -gt 0){$grid.AutoResizeRows([Windows.Forms.DataGridViewAutoSizeRowsMode]::DisplayedCellsExceptHeaders)}
    if($ResizeRows){$grid.PerformLayout();$grid.Invalidate()}
}
function Apply-ITMDisplayTree($control) {
    if($control -is [Windows.Forms.DataGridView]){Set-ITMGridAppearance $control}
    foreach($child in $control.Controls){Apply-ITMDisplayTree $child}
}
function Apply-ITMDisplayPreferences {
    Apply-ITMDisplayTree $script:main
    foreach($form in @([Windows.Forms.Application]::OpenForms)){if($form -ne $script:main){Apply-ITMDisplayTree $form}}
    Apply-ITMThemeToOpenForms
    if($script:commandHelp){$script:commandHelp.Active=[bool]$script:displayPreferences.Tips}
    if($script:ribbonTips){$script:ribbonTips.Active=[bool]$script:displayPreferences.Tips}
    $script:ribbonPeek=$false;Update-ITMNavigation
    if(Get-Command Apply-ITMFluidTypography -ErrorAction SilentlyContinue){Apply-ITMFluidTypography $script:main}
    foreach($table in @($script:fluidTables.Keys)){Update-ITMFluidTable $table}
    foreach($panel in @($script:pages.Values)+@($script:adminPages.Values)){Update-ITMResponsivePanel $panel}
    if(Get-Command Update-ITMWorkspaceLayout -ErrorAction SilentlyContinue){Update-ITMWorkspaceLayout}
    Apply-ITMDisplayTree $script:main
    foreach($form in @([Windows.Forms.Application]::OpenForms)){if($form -ne $script:main){Apply-ITMDisplayTree $form}}
}
function Set-ITMDisplayPreferenceFields {
    $oldSync=$script:displayFieldsSync;$script:displayFieldsSync=$true
    try{
        $script:displayWindowMode.SelectedItem=$script:displayPreferences.WindowMode
        $script:displayNewWindow.Enabled=($script:displayPreferences.WindowMode -eq 'Plusieurs fenêtres')
        $script:displayRibbon.SelectedItem=$script:displayPreferences.RibbonMode
        $script:displayTheme.SelectedItem=$script:displayPreferences.Theme
        $script:displaySize.SelectedItem=[string]$script:displayPreferences.TextSize+' pt'
        $script:displayDensity.SelectedItem=$script:displayPreferences.Density
        $script:displayTips.Checked=$script:displayPreferences.Tips
        $script:displayAlternating.Checked=$script:displayPreferences.Alternating
        $script:displayMaximized.Checked=$script:displayPreferences.Maximized
    }finally{$script:displayFieldsSync=$oldSync}
}
function Get-ITMDisplayPreferenceFields {
    return [pscustomobject]@{Theme=$script:displayTheme.Text;WindowMode=$script:displayWindowMode.Text;RibbonMode=$script:displayRibbon.Text;TextSize=[int]($script:displaySize.Text.Replace(' pt',''));Density=$script:displayDensity.Text;Tips=$script:displayTips.Checked;Alternating=$script:displayAlternating.Checked;Maximized=$script:displayMaximized.Checked}
}
function Set-ITMLiveDisplayPreferences($candidate) {
    if($script:displayChanging -or $script:displayFieldsSync){return}
    if($script:busy){Set-ITMDisplayPreferenceFields;return}
    if($candidate.WindowMode -notin @('Une seule fenêtre','Plusieurs fenêtres')){throw 'Mode de fenêtre invalide.'}
    if($candidate.Theme -notin @('Clair','Sombre','Système') -or $candidate.RibbonMode -notin @('Onglets et commandes','Onglets seuls') -or $candidate.TextSize -notin @(9,10,11,12,14) -or $candidate.Density -notin @('Compact','Confortable','Aéré')){throw 'Préférence d''affichage invalide.'}
    foreach($name in @('Tips','Alternating','Maximized')){if($candidate.$name -isnot [bool]){throw 'Préférence d''affichage invalide.'}}
    if(($candidate | ConvertTo-Json -Compress) -ceq ($script:displayPreferences | ConvertTo-Json -Compress)){return}
    $previous=$script:displayPreferences;$saved=$false;$script:displayChanging=$true
    try{
        $script:displayPreferences=$candidate
        Save-ITMDisplayPreferences;$saved=$true
        Apply-ITMDisplayPreferences
        if($candidate.Maximized -ne $previous.Maximized){
            if($candidate.Maximized){$script:main.WindowState='Maximized'}elseif($script:main.WindowState -eq 'Maximized'){$script:main.WindowState='Normal'}
        }
        Set-ITMDisplayPreferenceFields
        $script:status.Text='Préférence appliquée et mémorisée automatiquement.'
    }catch{
        if(-not $saved){$script:displayPreferences=$previous}
        Set-ITMDisplayPreferenceFields
        if($saved){throw ('Préférence mémorisée, mais affichage interrompu. Relancez l''application pour reprendre ce réglage. '+$_.Exception.Message)}
        throw
    }finally{$script:displayChanging=$false}
}
function Invoke-ITMPreferenceSelection {
    if($script:displayFieldsSync -or $script:displayChanging){return}
    try{Set-ITMLiveDisplayPreferences (Get-ITMDisplayPreferenceFields)}catch{Set-ITMDisplayPreferenceFields;App-Error $_}
}
function Build-ITMDisplayPreferences($page) {
    $page.AutoScroll=$false
    $footer=New-Object Windows.Forms.FlowLayoutPanel;$footer.Dock='Bottom';$footer.AutoSize=$true;$footer.AutoSizeMode='GrowAndShrink';$footer.MinimumSize=New-Object Drawing.Size(0,66);$footer.Padding=New-Object Windows.Forms.Padding(20,12,20,8);$footer.WrapContents=$true
    $page.Controls.Add($footer)
    $scroll=New-Object Windows.Forms.Panel;$scroll.Dock='Fill';$scroll.AutoScroll=$true;$scroll.Padding=New-Object Windows.Forms.Padding(24,12,24,20);$page.Controls.Add($scroll);$scroll.BringToFront()
    $table=New-Object Windows.Forms.TableLayoutPanel;$table.Dock='Top';$table.AutoSize=$true;$table.AutoSizeMode='GrowAndShrink';$table.ColumnCount=2;$table.RowCount=0
    [void]$table.ColumnStyles.Add((New-Object Windows.Forms.ColumnStyle([Windows.Forms.SizeType]::Percent,44)))
    [void]$table.ColumnStyles.Add((New-Object Windows.Forms.ColumnStyle([Windows.Forms.SizeType]::Percent,56)));$scroll.Controls.Add($table)
    $addHeading={param($text,$description)
        $row=$table.RowCount;$table.RowCount++
        [void]$table.RowStyles.Add((New-Object Windows.Forms.RowStyle([Windows.Forms.SizeType]::AutoSize)))
        $label=New-Label $table $text 0 0 850 32 12 $true;$label.AutoSize=$true;$label.Dock='Top';$label.Margin=New-Object Windows.Forms.Padding(0,18,0,8)
        $table.SetCellPosition($label,(New-Object Windows.Forms.TableLayoutPanelCellPosition(0,$row)));$table.SetColumnSpan($label,2)
        if($description){
            $row=$table.RowCount;$table.RowCount++;[void]$table.RowStyles.Add((New-Object Windows.Forms.RowStyle([Windows.Forms.SizeType]::AutoSize)))
            $note=New-Label $table $description 0 0 850 44 10;$note.AutoSize=$true;$note.Dock='Top';$note.Margin=New-Object Windows.Forms.Padding(0,0,0,14);$note.Tag='PreferenceNote'
            $table.SetCellPosition($note,(New-Object Windows.Forms.TableLayoutPanelCellPosition(0,$row)));$table.SetColumnSpan($note,2)
        }
    }
    $addChoice={param($labelText,$choices,$value)
        $row=$table.RowCount;$table.RowCount++;[void]$table.RowStyles.Add((New-Object Windows.Forms.RowStyle([Windows.Forms.SizeType]::AutoSize)))
        $label=New-Label $table $labelText 0 0 370 40 10;$label.AutoSize=$true;$label.Dock='Top';$label.Margin=New-Object Windows.Forms.Padding(0,10,20,10);$label.Tag='PreferenceField'
        $table.SetCellPosition($label,(New-Object Windows.Forms.TableLayoutPanelCellPosition(0,$row)))
        $combo=New-Combo $table 0 0 340;Set-Choices $combo $choices $value;$combo.Dock='Top';$combo.Margin=New-Object Windows.Forms.Padding(0,8,8,10)
        $table.SetCellPosition($combo,(New-Object Windows.Forms.TableLayoutPanelCellPosition(1,$row)));return $combo
    }
    $addCheck={param($text)
        $row=$table.RowCount;$table.RowCount++;[void]$table.RowStyles.Add((New-Object Windows.Forms.RowStyle([Windows.Forms.SizeType]::AutoSize)))
        $check=New-Object ITMReadableCheckBox;$check.Text=$text;$check.AutoSize=$true;$check.Dock='Top';$check.Margin=New-Object Windows.Forms.Padding(0,8,0,10);$table.Controls.Add($check)
        $table.SetCellPosition($check,(New-Object Windows.Forms.TableLayoutPanelCellPosition(0,$row)));$table.SetColumnSpan($check,2);return $check
    }
    & $addHeading 'Personnaliser votre espace de travail' 'Les réglages sont conservés pour votre compte Windows. Chaque choix est appliqué et enregistré immédiatement.'
    & $addHeading 'Apparence' ''
    $script:displayTheme=& $addChoice 'Thème de l''application' @('Clair','Sombre','Système') 'Clair'
    & $addHeading 'Lecture des listes' 'La taille choisie s''applique immédiatement aux listes et aux historiques. Les colonnes se partagent la largeur disponible et le texte passe à la ligne.'
    $script:displaySize=& $addChoice 'Taille du texte' @('9 pt','10 pt','11 pt','12 pt','14 pt') '10 pt'
    $script:displayDensity=& $addChoice 'Espacement des lignes' @('Compact','Confortable','Aéré') 'Confortable'
    $script:displayAlternating=& $addCheck 'Alterner la couleur des lignes'
    & $addHeading 'Commandes et aides' 'En mode Onglets seuls, cliquez sur l''onglet actif pour ouvrir ses commandes. Ctrl+F1 change le mode mémorisé.'
    $script:displayRibbon=& $addChoice 'Affichage du ruban' @('Onglets et commandes','Onglets seuls') 'Onglets et commandes'
    $script:displayTips=& $addCheck 'Afficher la description et le raccourci au survol'
    & $addHeading 'Fenêtre de travail' 'Une seule fenêtre : les prochains lancements reviennent à la fenêtre ouverte. Plusieurs fenêtres : chaque fenêtre peut afficher une tâche différente. Le passage à une seule fenêtre conserve celles déjà ouvertes ; fermez les fenêtres supplémentaires. Un classeur en cours de modification reste protégé contre les écritures simultanées.'
    $script:displayWindowMode=& $addChoice 'Ouverture de l''application' @('Une seule fenêtre','Plusieurs fenêtres') 'Une seule fenêtre'
    $script:displayMaximized=& $addCheck 'Maximiser la fenêtre et conserver ce choix'
    Build-ITMSoundPreferences $table $addHeading $addChoice $addCheck
    Enable-ITMFluidTable $table
    $note=New-Label $footer 'Les changements sont enregistrés automatiquement.' 0 0 430 36 10
    $note.Margin=New-Object Windows.Forms.Padding(0,6,20,6)
    $note.AutoSize=$false
    $footer.Add_SizeChanged({param($sender,$e)
        $w=[Math]::Max(40,$sender.ClientSize.Width-$sender.Padding.Horizontal-12)
        foreach($c in $sender.Controls){$c.Width=[Math]::Min($w,430);$c.Height=Get-ITMTextHeight $c $c.Width 32}
    })
    $script:displayReset=New-Button $footer 'Valeurs par défaut' 0 0 240
    $script:displayNewWindow=New-Button $footer 'Nouvelle fenêtre' 0 0 200
    $script:displayNewWindow.Add_Click({
        try{
            if($script:displayPreferences.WindowMode -ne 'Plusieurs fenêtres'){return}
            $launcher=Join-Path (Join-Path ([IO.Path]::GetDirectoryName($PSScriptRoot)) '00_Demarrage') 'APPLICATION.vbs'
            [void](Start-Process -FilePath (Join-Path $env:SystemRoot 'System32\wscript.exe') -ArgumentList ('"'+$launcher+'"') -ErrorAction Stop)
        }catch{App-Error $_}
    })
    # Initialize before subscribing, then suppress all programmatic synchronization events.
    Set-ITMDisplayPreferenceFields
    foreach($combo in @($script:displayTheme,$script:displaySize,$script:displayDensity,$script:displayRibbon,$script:displayWindowMode)){$combo.Add_SelectionChangeCommitted({Invoke-ITMPreferenceSelection})}
    foreach($check in @($script:displayTips,$script:displayAlternating,$script:displayMaximized)){$check.Add_CheckedChanged({Invoke-ITMPreferenceSelection})}
    $script:displayReset.Add_Click({try{Set-ITMLiveDisplayPreferences (Get-ITMDefaultDisplayPreferences)}catch{App-Error $_}})
    $script:main.Add_Shown({if($script:displayPreferences.Maximized){$script:main.WindowState='Maximized'}})
    $script:main.Add_FormClosed({foreach($font in $script:displayGridFonts.Values){$font.Dispose()};$script:displayGridFonts.Clear()})
}
