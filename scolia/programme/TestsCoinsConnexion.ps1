# Windows PowerShell 5.1 -STA. No Excel, credentials or registry writes.
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Windows.Forms,System.Drawing
Add-Type -Path (Join-Path $PSScriptRoot 'ChromeModerne.cs') -ReferencedAssemblies @('System.Windows.Forms','System.Drawing')
. (Join-Path $PSScriptRoot 'ThemesAffichage.ps1')
function Assert($value,$message){if(-not $value){throw $message}}
foreach($kind in @('Login','Account','Registration')){
 $form=$(if($kind -eq 'Login'){New-Object ITMLoginForm}else{New-Object ITMAccountForm(($kind -eq 'Registration'),$false,$false)})
 try{
  foreach($mode in @('Clair','Sombre','Clair')){
   $script:displayPreferences=[pscustomobject]@{Theme=$mode;Alternating=$true};Apply-ITMThemeForm $form
   foreach($field in @($form.Controls | Where-Object {$_ -is [ITMPasswordSurface]})){foreach($input in @($field.Controls | Where-Object {$_ -is [Windows.Forms.TextBox]})){Assert (-not $input.AutoSize -and $input.Height -ge $input.PreferredHeight) ($kind+' '+$mode+': password text height is clipped')}}
   foreach($field in @($form.Controls | Where-Object {$_ -is [ITMPasswordSurface]})){
    $image=New-Object Drawing.Bitmap($field.Width,$field.Height)
    try{
     $field.DrawToBitmap($image,(New-Object Drawing.Rectangle(0,0,$field.Width,$field.Height)))
     foreach($point in @(@(0,0),@(($field.Width-1),0),@(0,($field.Height-1)),@(($field.Width-1),($field.Height-1)))){
      Assert ($image.GetPixel($point[0],$point[1]).ToArgb() -eq $form.BackColor.ToArgb()) ($kind+' '+$mode+': incorrect corner background')
     }
     foreach($input in @($field.Controls | Where-Object {$_ -is [Windows.Forms.TextBox]})){Assert ($input.BackColor -eq $field.BackColor) 'Input fill differs from its rounded field'}
    }finally{$image.Dispose()}
   }
  }
  Write-Output ('PASS: '+$kind+' corners and field colors across light/dark theme round trip.')
 }finally{$form.Dispose()}
}
