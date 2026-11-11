# Windows PowerShell 5.1 -STA. No Excel, school data or registry writes.
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Windows.Forms,System.Drawing
foreach($file in @('ActivityIndicator.cs','ChromeModerne.cs')){Add-Type -Path (Join-Path $PSScriptRoot $file) -ReferencedAssemblies @('System.Windows.Forms','System.Drawing')}
. (Join-Path $PSScriptRoot 'ThemesAffichage.ps1')
function Assert($value,$message){if(-not $value){throw $message}}
$script:displayPreferences=[pscustomobject]@{Theme='Clair';Alternating=$true}
$script:pages=@{};$form=New-Object Windows.Forms.Form;$form.ClientSize=New-Object Drawing.Size(820,600)
$surface=New-Object ITMQuietSurface;$surface.SetBounds(16,16,780,550);$form.Controls.Add($surface)
$input=New-Object Windows.Forms.MaskedTextBox;$input.Mask='00/00/0000';$input.Text='01092006';$input.SetBounds(16,16,220,28);$surface.Controls.Add($input)
$readonly=New-Object Windows.Forms.TextBox;$readonly.ReadOnly=$true;$readonly.Text='Valeur calculée';$readonly.SetBounds(16,54,220,28);$surface.Controls.Add($readonly)
$button=New-Object ITMActionButton;$button.Text='Enregistrer';$button.Primary=$true;$button.BackColor=[Drawing.Color]::FromArgb(0,102,204);$button.ForeColor=[Drawing.Color]::White;$button.SetBounds(16,96,220,36);$surface.Controls.Add($button)
$list=New-Object ITMTopicList;$list.SetBounds(16,160,220,220);[void]$list.Items.Add('Sauvegarde et restauration');$surface.Controls.Add($list)
$beforeInput=$input.Text;$beforeBounds=$button.Bounds
try{
 foreach($name in @('Shared visual components')){
  foreach($mode in @('Clair','Sombre','Clair')){
   $script:displayPreferences.Theme=$mode;Apply-ITMThemeForm $form
   Assert ($input.Text -eq $beforeInput -and $input.Mask -eq '00/00/0000') ($name+': date changed by visual theme')
   Assert ($readonly.ReadOnly -and $readonly.Text -eq 'Valeur calculée') ($name+': calculated field changed')
   Assert ($button.Bounds -eq $beforeBounds -and $button.Enabled) ($name+': command layout or availability changed')
   Assert ($surface.BackColor -ne $form.BackColor -and $readonly.BackColor -ne $input.BackColor) ($name+': visual layers collapsed')
   $bmp=New-Object Drawing.Bitmap(820,600);try{$form.DrawToBitmap($bmp,$form.ClientRectangle)}finally{$bmp.Dispose()}
  }
 }
 Write-Output 'PASS: theme round trips preserve commands, input masks, values, calculated fields and geometry.'
}finally{$form.Dispose()}
