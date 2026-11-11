. (Join-Path $PSScriptRoot 'TypographieV71.ps1')
. (Join-Path $PSScriptRoot 'InterfaceQualiteV65.ps1')
# Shared native-window theme. No workbook or authentication data is touched.
$script:themeStripRenderers=@{};$script:themeColors=@{};$script:themeForms=@{};$script:themeLastDark=$null
function Get-ITMThemeMode {
    if($script:displayPreferences -and $script:displayPreferences.PSObject.Properties['Theme']){return [string]$script:displayPreferences.Theme}
    try{
        $saved=Get-ItemProperty -LiteralPath 'HKCU:\Software\ITMCD\Secretariat\Affichage' -ErrorAction Stop
        if($saved.PSObject.Properties['SettingsJson']){$saved=[string]$saved.SettingsJson | ConvertFrom-Json -ErrorAction Stop}
        $mode=[string]$saved.Theme;if($mode -in @('Clair','Sombre','Système')){return $mode}
    }catch{}
    return 'Clair'
}
function Get-ITMEffectiveDarkTheme {
    switch(Get-ITMThemeMode){
        'Sombre'{return $true}
        'Système'{try{return ([int](Get-ItemProperty -LiteralPath 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize' -Name AppsUseLightTheme -ErrorAction Stop).AppsUseLightTheme -eq 0)}catch{return $false}}
        default{return $false}
    }
}
# Semantic surfaces keep the existing geometry while separating visual layers.
$script:darkPalette=@{
    Window=[Drawing.Color]::FromArgb(27,31,38);Workspace=[Drawing.Color]::FromArgb(31,36,44)
    Header=[Drawing.Color]::FromArgb(32,38,47);Navigation=[Drawing.Color]::FromArgb(30,35,43)
    Ribbon=[Drawing.Color]::FromArgb(35,41,50);Surface=[Drawing.Color]::FromArgb(39,45,54)
    Input=[Drawing.Color]::FromArgb(32,38,47);ReadOnly=[Drawing.Color]::FromArgb(43,49,58)
    Table=[Drawing.Color]::FromArgb(36,42,51);Alternate=[Drawing.Color]::FromArgb(40,47,57)
    ColumnHeader=[Drawing.Color]::FromArgb(46,54,65);Selection=[Drawing.Color]::FromArgb(52,73,101)
    Border=[Drawing.Color]::FromArgb(98,111,129);Divider=[Drawing.Color]::FromArgb(69,79,94)
    Accent=[Drawing.Color]::FromArgb(166,198,237);Ink=[Drawing.Color]::FromArgb(237,240,244)
    Muted=[Drawing.Color]::FromArgb(189,199,211);Primary=[Drawing.Color]::FromArgb(47,82,127)
}

$script:lightPalette=@{
    Window=[Drawing.Color]::FromArgb(246,247,249);Workspace=[Drawing.Color]::FromArgb(242,244,247)
    Header=[Drawing.Color]::White;Navigation=[Drawing.Color]::FromArgb(247,248,250)
    Ribbon=[Drawing.Color]::FromArgb(249,250,251);Surface=[Drawing.Color]::White
    Input=[Drawing.Color]::White;ReadOnly=[Drawing.Color]::FromArgb(244,246,248)
    Table=[Drawing.Color]::White;Alternate=[Drawing.Color]::FromArgb(249,250,252)
    ColumnHeader=[Drawing.Color]::FromArgb(244,246,248);Selection=[Drawing.Color]::FromArgb(229,238,248)
    Border=[Drawing.Color]::FromArgb(206,214,224);Divider=[Drawing.Color]::FromArgb(224,228,234)
    Accent=[Drawing.Color]::FromArgb(38,91,155);Ink=[Drawing.Color]::FromArgb(31,41,55)
    Muted=[Drawing.Color]::FromArgb(91,105,122);Primary=[Drawing.Color]::FromArgb(38,91,155)
}
function Get-ITMLightSurface($control) {
    $p=$script:lightPalette;$kind=$control.GetType().Name
    if($control.Parent -and $control.Parent.GetType().Name -eq 'ITMFieldSurface'){return $control.Parent.BackColor}
    if($kind -eq 'ITMFieldSurface'){
        if(-not $control.Editor.Enabled -or ($control.Editor -is [Windows.Forms.TextBoxBase] -and $control.Editor.ReadOnly)){return $p.ReadOnly}
        return $p.Input
    }
    if($control -is [Windows.Forms.TextBoxBase] -and $control.Parent -and $control.Parent.GetType().Name -eq 'ITMPasswordSurface'){return $control.Parent.BackColor}
    if($control -is [Windows.Forms.Form]){return $p.Window}
    if($kind -in @('ITMDashboardSurface','ITMQuietSurface','ITMSectionPanel','ITMFormSection','ITMPasswordSurface','ITMPasswordReveal')){return $p.Surface}
    if($kind -eq 'ITMRibbonPanel'){return $p.Ribbon}
    if($kind -eq 'ITMWorkspaceTab'){return $p.Navigation}
    if($control -is [Windows.Forms.TextBoxBase] -and $control.ReadOnly){return $p.ReadOnly}
    if($control -is [Windows.Forms.TextBoxBase] -or $control -is [Windows.Forms.ComboBox] -or $control -is [Windows.Forms.UpDownBase] -or $control -is [Windows.Forms.ListBox] -or $control -is [Windows.Forms.DataGridView]){return $p.Input}
    if($control -is [Windows.Forms.TabPage] -or $control -is [Windows.Forms.TabControl]){return $p.Workspace}
    if($script:pages -and $control -in $script:pages.Values){return $p.Workspace}
    if($control -is [Windows.Forms.Panel] -and ($control.Dock -eq 'Top' -or $control.Dock -eq 'Bottom')){return $p.Header}
    if($control.Parent){return $control.Parent.BackColor};return $p.Workspace
}

function Get-ITMDarkSurface($control) {
    $p=$script:darkPalette;$kind=$control.GetType().Name
    if($control.Parent -and $control.Parent.GetType().Name -eq 'ITMFieldSurface'){return $control.Parent.BackColor}
    if($kind -eq 'ITMFieldSurface'){
        if(-not $control.Editor.Enabled -or ($control.Editor -is [Windows.Forms.TextBoxBase] -and $control.Editor.ReadOnly)){return $p.ReadOnly}
        return $p.Input
    }
    if($control -is [Windows.Forms.TextBoxBase] -and $control.Parent -and $control.Parent.GetType().Name -eq 'ITMPasswordSurface'){return $control.Parent.BackColor}
    if($control -is [Windows.Forms.Form]){return $p.Window}
    if($kind -in @('ITMDashboardSurface','ITMQuietSurface','ITMSectionPanel','ITMFormSection')){return $p.Surface}
    if($kind -in @('ITMPasswordSurface','ITMPasswordReveal')){return $p.Input}
    if($kind -eq 'ITMRibbonPanel'){return $p.Ribbon}
    if($kind -eq 'ITMWorkspaceTab'){return $p.Navigation}
    if($kind -in @('ITMRibbonGroup','ITMRibbonCommand','ITMDashboardChart')){if($control.Parent){return $control.Parent.BackColor};return $p.Surface}
    if($control -is [Windows.Forms.DataGridView]){return $p.Table}
    if($control -is [Windows.Forms.ToolStrip]){return $p.Header}
    if($control -is [Windows.Forms.TabPage] -or $control -is [Windows.Forms.TabControl]){return $p.Workspace}
    if($script:pages -and $control -in $script:pages.Values){return $p.Workspace}
    if($control -is [Windows.Forms.TextBoxBase] -or $control -is [Windows.Forms.ComboBox] -or $control -is [Windows.Forms.NumericUpDown] -or $control -is [Windows.Forms.ListBox]){return $p.Input}
    if($control -is [Windows.Forms.Panel] -or $control -is [Windows.Forms.TableLayoutPanel]){
        if($control.Dock -eq [Windows.Forms.DockStyle]::Top -or $control.Dock -eq [Windows.Forms.DockStyle]::Bottom){return $p.Header}
        if($control.Parent){return $control.Parent.BackColor}
    }
    return $p.Workspace
}
function Set-ITMThemeMenuItems($items,[bool]$dark,$back,$fore) {
    foreach($item in $items){
        $item.BackColor=$back;$item.ForeColor=$fore
        if($item -is [Windows.Forms.ToolStripDropDownItem]){
            $item.DropDown.BackColor=$back;$item.DropDown.ForeColor=$fore
            if(-not $script:themeStripRenderers.ContainsKey($item.DropDown)){
                $script:themeStripRenderers[$item.DropDown]=$item.DropDown.Renderer
                $item.DropDown.Add_Disposed({param($sender,$e)$script:themeStripRenderers.Remove($sender)})
            }
            $item.DropDown.Renderer=$(if($dark){New-Object ITMThemeStripRenderer}else{$script:themeStripRenderers[$item.DropDown]})
            Set-ITMThemeMenuItems $item.DropDownItems $dark $back $fore
        }
    }
}
function Set-ITMThemeTree($control,[bool]$dark) {
    if($control.IsDisposed){return}
    if(-not $script:themeColors.ContainsKey($control)){$script:themeColors[$control]=[pscustomobject]@{Back=$control.BackColor;Fore=$control.ForeColor}}
    $original=$script:themeColors[$control]
    $p=$script:darkPalette;$bg=$p.Table;$ink=$p.Ink
    if(-not $dark){
        $control.BackColor=Get-ITMLightSurface $control;$control.ForeColor=$original.Fore
        if($control -is [Windows.Forms.Label]){
            $c=$original.Fore
            if($c.R -ge 70 -and $c.G -ge 70 -and $c.B -ge 70 -and [Math]::Abs([int]$c.R-[int]$c.B) -le 60){$control.ForeColor=$script:lightPalette.Muted}
            elseif($c.B -gt $c.R*1.3){$control.ForeColor=$script:lightPalette.Accent}
            if($control.Font.Bold -and $control.Font.SizeInPoints -ge 12){$control.ForeColor=$script:lightPalette.Ink}
        }
        if($control -is [Windows.Forms.TextBoxBase] -or $control -is [Windows.Forms.ComboBox] -or $control -is [Windows.Forms.CheckBox] -or $control -is [Windows.Forms.RadioButton]){$control.ForeColor=$script:lightPalette.Ink}
        if($control -is [Windows.Forms.Button] -and $control.GetType().Name -notin @('ITMWorkspaceTab','ITMRibbonCommand')){
            $primary=($original.Back.B -gt $original.Back.R*1.4 -and $original.Back.B -gt 90 -and $original.Fore.GetBrightness() -gt .7)
            if($control.PSObject.Properties['Primary']){$primary=[bool]$control.Primary}
            $control.BackColor=$(if($primary){$script:lightPalette.Primary}else{$script:lightPalette.Surface});$control.ForeColor=$(if($primary){[Drawing.Color]::White}else{$script:lightPalette.Ink})
        }
    }
    else{
        $control.BackColor=Get-ITMDarkSurface $control;$control.ForeColor=$ink
        if($control -is [Windows.Forms.Label] -or $control -is [Windows.Forms.CheckBox] -or $control -is [Windows.Forms.RadioButton] -or $control -is [Windows.Forms.PictureBox]){
            if($control.Parent){$control.BackColor=$control.Parent.BackColor}
            $c=$original.Fore
            if($c.R -gt 130 -and $c.R -gt $c.G*1.3){$control.ForeColor=[Drawing.Color]::FromArgb(255,190,160)}
            elseif($c.G -gt $c.R*1.3 -and $c.G -gt 90){$control.ForeColor=[Drawing.Color]::FromArgb(120,220,168)}
            elseif($c.B -gt $c.R*1.3){$control.ForeColor=[Drawing.Color]::FromArgb(173,211,255)}
        }
        if($control -is [Windows.Forms.TextBoxBase] -and $control.ReadOnly -and (-not $control.Parent -or $control.Parent.GetType().Name -ne 'ITMPasswordSurface')){$control.BackColor=$p.ReadOnly}
        if($control -is [Windows.Forms.Button] -and $control.GetType().Name -notin @('ITMWorkspaceTab','ITMRibbonCommand')){
            $primary=($original.Back.B -gt $original.Back.R*1.4 -and $original.Back.B -gt 90 -and $original.Fore.GetBrightness() -gt .7)
            if($control.PSObject.Properties['Primary']){$primary=[bool]$control.Primary}
            $control.BackColor=$(if($primary){$p.Primary}else{$p.Surface});$control.ForeColor=$p.Ink
        }
        if($control -is [Windows.Forms.Label]){
            if($control.Font.Bold -and $control.Font.SizeInPoints -ge 12){$control.ForeColor=$p.Ink}
            elseif($original.Fore.R -ge 80 -and $original.Fore.G -ge 80 -and $original.Fore.B -ge 80 -and [Math]::Abs([int]$original.Fore.R-[int]$original.Fore.B) -le 60){$control.ForeColor=$p.Muted}
        }
        # Native Windows date editors retain their system-drawn edit surface.
        if($control -is [Windows.Forms.DateTimePicker]){$control.BackColor=[Drawing.Color]::White;$control.ForeColor=[Drawing.Color]::Black}
    }
    if($control -is [Windows.Forms.RichTextBox]){
        # Explicitly formatted help text keeps its old color unless the full text is updated.
        # Preserve the reading position, selection, and heading fonts.
        $start=$control.SelectionStart;$length=$control.SelectionLength
        $control.SelectAll();$control.SelectionColor=$control.ForeColor
        $control.Select($start,$length)
    }
    if($control -is [Windows.Forms.ComboBox]){
        # Flat native border respects BackColor; owner drawing keeps closed/dropdown text coherent.
        $control.FlatStyle=$(if($dark -or ($control.Parent -and $control.Parent.GetType().Name -eq 'ITMFieldSurface')){'Flat'}else{'Standard'})
    }
    if($control -is [Windows.Forms.ToolStrip]){
        if(-not $script:themeStripRenderers.ContainsKey($control)){
            $script:themeStripRenderers[$control]=$control.Renderer
            $control.Add_Disposed({param($sender,$e)$script:themeStripRenderers.Remove($sender)})
        }
        $control.Renderer=$(if($dark){New-Object ITMThemeStripRenderer}else{$script:themeStripRenderers[$control]})
        Set-ITMThemeMenuItems $control.Items $dark $control.BackColor $control.ForeColor
    }
    if($control -is [Windows.Forms.LinkLabel]){
        $link=$(if($dark){[Drawing.Color]::FromArgb(173,211,255)}else{[Drawing.Color]::FromArgb(29,67,130)})
        $control.LinkColor=$link;$control.ActiveLinkColor=$link;$control.VisitedLinkColor=$link;$control.DisabledLinkColor=$(if($dark){$p.Muted}else{[Drawing.Color]::Gray})
    }
    if($control -is [Windows.Forms.DataGridView]){
        $control.EnableHeadersVisualStyles=$false
        $base=$(if($dark){$bg}else{$script:lightPalette.Table});$text=$(if($dark){$ink}else{$script:lightPalette.Ink})
        $control.BackgroundColor=$base;$control.DefaultCellStyle.BackColor=$base;$control.DefaultCellStyle.ForeColor=$text
        $control.ColumnHeadersDefaultCellStyle.BackColor=$(if($dark){$p.ColumnHeader}else{$script:lightPalette.ColumnHeader})
        $control.ColumnHeadersDefaultCellStyle.ForeColor=$text
        $control.DefaultCellStyle.SelectionBackColor=$(if($dark){$p.Selection}else{$script:lightPalette.Selection})
        $control.DefaultCellStyle.SelectionForeColor=$text
        $control.ColumnHeadersDefaultCellStyle.SelectionBackColor=$control.ColumnHeadersDefaultCellStyle.BackColor
        $control.ColumnHeadersDefaultCellStyle.SelectionForeColor=$text
        $control.RowHeadersDefaultCellStyle.BackColor=$control.ColumnHeadersDefaultCellStyle.BackColor
        $control.RowHeadersDefaultCellStyle.ForeColor=$text
        $control.RowsDefaultCellStyle.BackColor=$base;$control.RowsDefaultCellStyle.ForeColor=$text
        $control.RowsDefaultCellStyle.SelectionBackColor=$control.DefaultCellStyle.SelectionBackColor
        $control.RowsDefaultCellStyle.SelectionForeColor=$text
        $control.GridColor=$(if($dark){$p.Divider}else{$script:lightPalette.Divider})
        $alternate=$true;if($script:displayPreferences){$alternate=[bool]$script:displayPreferences.Alternating}
        $control.AlternatingRowsDefaultCellStyle.BackColor=$(if(-not $alternate){$base}elseif($dark){$p.Alternate}else{$script:lightPalette.Alternate})
        $control.AlternatingRowsDefaultCellStyle.ForeColor=$text
        $control.AlternatingRowsDefaultCellStyle.SelectionBackColor=$control.DefaultCellStyle.SelectionBackColor
        $control.AlternatingRowsDefaultCellStyle.SelectionForeColor=$text
    }
    switch([string]$control.Tag){
        'scolia:heading' {$control.ForeColor=$(if($dark){$script:darkPalette.Ink}else{$script:lightPalette.Ink})}
        'scolia:field-label' {$control.ForeColor=$(if($dark){$script:darkPalette.Muted}else{$script:lightPalette.Ink})}
        'scolia:feedback:success' {$control.ForeColor=$(if($dark){[Drawing.Color]::FromArgb(151,211,174)}else{[Drawing.Color]::FromArgb(31,112,62)})}
        'scolia:feedback:error' {$control.ForeColor=$(if($dark){[Drawing.Color]::FromArgb(241,143,144)}else{[Drawing.Color]::FromArgb(173,54,61)})}
    }
    if($dark -and $control -isnot [Windows.Forms.DateTimePicker]){
        # Validate the final semantic color rather than assuming every tint is readable.
        $control.ForeColor=[ITMReadableColors]::Ink($control.ForeColor,$control.BackColor,$control.Enabled)
    }
    foreach($child in $control.Controls){Set-ITMThemeTree $child $dark}
    $control.Invalidate()
}
function Remove-ITMThemeRecords($control) {
    foreach($child in $control.Controls){Remove-ITMThemeRecords $child}
    $script:themeColors.Remove($control)
}
function Apply-ITMThemeForm($form) {
    if(-not $form -or $form.IsDisposed){return}
    $dark=Get-ITMEffectiveDarkTheme
    Set-ITMThemeTree $form $dark
    if(Test-Path function:Set-ScoliaUniformTypography){Set-ScoliaUniformTypography $form}
    if($form -is [Windows.Forms.Form] -and (Test-Path function:Enable-ScoliaProfessionalWindow)){Enable-ScoliaProfessionalWindow $form}
    if($form -is [Windows.Forms.Form] -and ("ITMVisualTheme" -as [type])){[ITMVisualTheme]::PrepareWindow($form)}
    if($form -is [Windows.Forms.Form] -and -not $script:themeForms.ContainsKey($form)){
        $script:themeForms[$form]=$true
        $form.Add_Disposed({param($sender,$e)
            foreach($control in @($script:themeColors.Keys)){if($control.IsDisposed){$script:themeColors.Remove($control)}}
            $script:themeForms.Remove($sender)
        })
    }
}
function Apply-ITMThemeToOpenForms {
    if(Test-Path function:Clear-ScoliaRibbonViews){Clear-ScoliaRibbonViews}
    $dark=Get-ITMEffectiveDarkTheme
    if($script:main){Apply-ITMThemeForm $script:main}
    foreach($form in @([Windows.Forms.Application]::OpenForms)){if($form -ne $script:main){Apply-ITMThemeForm $form}}
    if($script:navIcons -and $script:nav){
        foreach($name in @($script:nav.Keys)){
            $nextInactive=New-NavIcon $name $dark
            try{$nextActive=New-NavIcon $name $dark}catch{$nextInactive.Dispose();throw}
            $inactiveKey=$name+'__False';$activeKey=$name+'__True'
            $oldInactive=$script:navIcons[$inactiveKey];$oldActive=$script:navIcons[$activeKey]
            # Detach the old displayed bitmap before disposing either cached image.
            try{$script:nav[$name].Image=$(if($script:currentPage -eq $name){$nextActive}else{$nextInactive})}
            catch{$nextInactive.Dispose();$nextActive.Dispose();throw}
            $script:navIcons[$inactiveKey]=$nextInactive;$script:navIcons[$activeKey]=$nextActive
            if($oldInactive){$oldInactive.Dispose()}
            if($oldActive -and -not [object]::ReferenceEquals($oldActive,$oldInactive)){$oldActive.Dispose()}
        }
    }
    if(Test-Path function:Update-ITMDashboardIcons){Update-ITMDashboardIcons $dark}
    $script:themeLastDark=$dark
}
function Start-ITMThemeMonitor {
    $script:themeTimer=New-Object Windows.Forms.Timer;$script:themeTimer.Interval=5000
    $script:themeTimer.Add_Tick({if($script:busy){return};$dark=Get-ITMEffectiveDarkTheme;if($null -eq $script:themeLastDark -or $dark -ne $script:themeLastDark){Apply-ITMThemeToOpenForms}})
    $script:themeTimer.Start()
    $script:main.Add_FormClosed({$script:themeTimer.Stop();$script:themeTimer.Dispose()})
}

# Les fenêtres auxiliaires qui utilisent leur propre constructeur reçoivent
# elles aussi les couleurs et mesures communes, une seule fois après ouverture.
if($script:scoliaThemeIdleHandler){[Windows.Forms.Application]::remove_Idle($script:scoliaThemeIdleHandler)}
$script:scoliaThemeIdleBusy=$false
$script:scoliaThemeIdleHandler=[System.EventHandler]{
 param($sender,$e)
 if($script:scoliaThemeIdleBusy){return};$script:scoliaThemeIdleBusy=$true
 try{
  foreach($window in @([Windows.Forms.Application]::OpenForms)){
   if(-not $window.IsDisposed -and -not $window.InvokeRequired -and -not $script:themeForms.ContainsKey($window)){
    try{Apply-ITMThemeForm $window}catch{Write-Verbose ('Thème de fenêtre : '+$_.Exception.Message)}
   }
  }
 }finally{$script:scoliaThemeIdleBusy=$false}
}
# V80 : le contrôle « fenêtre déjà thématisée ? » est fait en C# à chaque période
# d'inactivité ; PowerShell n'est appelé que pour une nouvelle fenêtre. L'ancien
# gestionnaire reste le repli si le noyau d'accélération n'est pas disponible.
if($script:scoliaThemeWatcher){try{$script:scoliaThemeWatcher.Detach()}catch{};$script:scoliaThemeWatcher=$null}
if('ITMFormWatcher' -as [type]){
 $script:scoliaThemeWatcher=[ITMFormWatcher]::new($script:themeForms)
 $script:scoliaThemeWatcher.add_FormNeedsTheme([EventHandler]{
  param($sender,$e)
  if($script:scoliaThemeIdleBusy){return};$script:scoliaThemeIdleBusy=$true
  try{
   if(-not $sender.IsDisposed -and -not $sender.InvokeRequired -and -not $script:themeForms.ContainsKey($sender)){
    try{Apply-ITMThemeForm $sender}catch{Write-Verbose ('Thème de fenêtre : '+$_.Exception.Message)}
   }
  }finally{$script:scoliaThemeIdleBusy=$false}
 })
 $script:scoliaThemeWatcher.Attach()
}else{
 [Windows.Forms.Application]::add_Idle($script:scoliaThemeIdleHandler)
}
