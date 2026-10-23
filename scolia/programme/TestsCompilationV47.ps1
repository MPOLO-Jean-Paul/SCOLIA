$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Windows.Forms,System.Drawing
$files=@(
  @{Name='NotificationsModernes.cs';Refs=@('System.Windows.Forms','System.Drawing')},
  @{Name='ActivityIndicator.cs';Refs=@('System.Windows.Forms','System.Drawing')},
  @{Name='ChromeModerne.cs';Refs=@('System.Windows.Forms','System.Drawing')}
)
foreach($item in $files){
  $path=Join-Path $PSScriptRoot $item.Name
  Add-Type -Path $path -ReferencedAssemblies $item.Refs -IgnoreWarnings -ErrorAction Stop
  Write-Host ('OK : '+$item.Name)
}
$cache=Get-Content -LiteralPath (Join-Path $PSScriptRoot 'CompilationCache.ps1') -Raw -Encoding UTF8
if($cache -notmatch 'IgnoreWarnings=\$true'){throw 'CompilationCache.ps1 ne protege pas encore les avertissements de compilation.'}
Write-Host 'OK : CompilationCache ignore les avertissements sans masquer les vraies erreurs.'
