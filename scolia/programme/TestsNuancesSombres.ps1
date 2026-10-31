# Run on Windows with Windows PowerShell 5.1 -STA. No workbook is opened.
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Windows.Forms,System.Drawing
foreach($file in @('ActivityIndicator.cs','ChromeModerne.cs','DashboardChart.cs')){Add-Type -Path (Join-Path $PSScriptRoot $file) -ReferencedAssemblies @('System.Windows.Forms','System.Drawing')}
. (Join-Path $PSScriptRoot 'ThemesAffichage.ps1')
$script:displayPreferences=[pscustomobject]@{Theme='Sombre';Alternating=$true}
function Assert($ok,$message){if(-not $ok){throw $message}}
$form=New-Object Windows.Forms.Form
$form.Size=New-Object Drawing.Size(900,700)
$ribbon=New-Object ITMRibbonPanel;$ribbon.SetBounds(0,0,850,120);$form.Controls.Add($ribbon)
$group=New-Object ITMRibbonGroup;$group.SetBounds(10,5,230,100);$ribbon.Controls.Add($group)
$command=New-Object ITMRibbonCommand;$command.Text='Exporter';$command.SetBounds(5,5,180,32);$group.Controls.Add($command)
$surface=New-Object ITMQuietSurface;$surface.SetBounds(10,140,800,450);$form.Controls.Add($surface)
$field=New-Object Windows.Forms.TextBox;$field.SetBounds(10,10,300,28);$surface.Controls.Add($field)
$label=New-Object ITMReadableLabel;$label.Text='Nom';$label.SetBounds(330,10,100,28);$surface.Controls.Add($label)
$grid=New-Object Windows.Forms.DataGridView;$grid.SetBounds(10,60,700,300);$surface.Controls.Add($grid)
$grid.AllowUserToAddRows=$false;[void]$grid.Columns.Add('Nom','Nom');[void]$grid.Rows.Add('Exemple')
$chart=New-Object ITMDashboardChart;$chart.SetBounds(10,370,250,60);$surface.Controls.Add($chart)
$controls=@($form,$ribbon,$group,$command,$surface,$field,$label,$grid,$chart)
$before=@{};foreach($c in $controls){$before[$c]=[pscustomobject]@{Bounds=$c.Bounds;Padding=$c.Padding;Font=$c.Font;Back=$c.BackColor;Fore=$c.ForeColor}}
try{
    Apply-ITMThemeForm $form
    Assert ($form.BackColor -eq $script:darkPalette.Window) 'Window surface'
    Assert ($ribbon.BackColor -eq $script:darkPalette.Ribbon) 'Ribbon surface'
    Assert ($surface.BackColor -eq $script:darkPalette.Surface) 'Content surface'
    Assert ($field.BackColor -eq $script:darkPalette.Input) 'Input surface'
    Assert ($group.BackColor -eq $ribbon.BackColor -and $command.BackColor -eq $group.BackColor) 'Quiet ribbon commands'
    Assert ($chart.BackColor -eq $surface.BackColor) 'Chart surface inheritance'
    Assert ($label.BackColor -eq $surface.BackColor) 'Label surface inheritance'
    Assert ($grid.ColumnHeadersDefaultCellStyle.BackColor -eq $script:darkPalette.ColumnHeader) 'Grid headers'
    Assert ($grid.AlternatingRowsDefaultCellStyle.BackColor -eq $script:darkPalette.Alternate) 'Alternate rows'
    Assert ($grid.DefaultCellStyle.SelectionBackColor -eq $script:darkPalette.Selection) 'Grid selection'
    foreach($c in $controls){Assert ($c.Bounds -eq $before[$c].Bounds -and $c.Padding -eq $before[$c].Padding -and $c.Font -eq $before[$c].Font) ('Geometry or font changed: '+$c.GetType().Name)}
    $script:displayPreferences.Theme='Clair';Apply-ITMThemeForm $form
    Assert ($form.BackColor -eq $script:lightPalette.Window -and $surface.BackColor -eq $script:lightPalette.Surface) 'Light semantic surfaces'
    Assert ($group.BackColor -eq $ribbon.BackColor -and $label.BackColor -eq $surface.BackColor) 'Light surface inheritance'
    foreach($c in $controls){Assert ($c.Bounds -eq $before[$c].Bounds -and $c.Font -eq $before[$c].Font) 'Theme round trip changed layout'}
    $script:displayPreferences.Theme='Sombre';Apply-ITMThemeForm $form
    $bitmap=New-Object Drawing.Bitmap(900,700)
    try{$form.DrawToBitmap($bitmap,$form.ClientRectangle)}finally{$bitmap.Dispose()}
    Write-Host 'PASS: surfaces, inheritance, geometry, light restore and rendering.'
}finally{$form.Dispose()}
