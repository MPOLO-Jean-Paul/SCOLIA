# Windows PowerShell 5.1 -STA : vraies fenêtres, aucune connexion Excel.
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Windows.Forms,System.Drawing
foreach($name in @('ActivityIndicator.cs','ChromeModerne.cs')){Add-Type -Path (Join-Path $PSScriptRoot $name) -ReferencedAssemblies System.Windows.Forms,System.Drawing -IgnoreWarnings}
. (Join-Path $PSScriptRoot 'InterfaceResponsive.ps1')
foreach($spec in @(@('Application.ps1','New-Label'),@('Application.ps1','New-Button'),@('Application.ps1','New-Grid'),@('ScoliaConfiguration.ps1','New-ScoliaDialog'),@('ScoliaConfiguration.ps1','New-ScoliaTable'),@('ScoliaConfiguration.ps1','Add-ScoliaField'),@('ScoliaConfiguration.ps1','Add-ScoliaButton'),@('FonctionnalitesV55.ps1','New-ScoliaFeatureDialog'),@('FonctionnalitesV55.ps1','Update-ScoliaFeatureDialogLayout'),@('ExploitationProfessionnelle.ps1','Assert-ITMAutoBackupSettings'),@('ExploitationProfessionnelle.ps1','Get-ITMAutoBackupSettings'),@('ExploitationProfessionnelle.ps1','Show-ITMAutoBackupOptions'))){
 $tokens=$null;$errors=$null;$ast=[Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot $spec[0]),[ref]$tokens,[ref]$errors);$name=$spec[1]
 $node=$ast.Find({param($n)$n -is [Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -eq $name},$true);. ([scriptblock]::Create($node.Extent.Text))
}
. (Join-Path $PSScriptRoot 'CentreRechercheV55.ps1')
function Assert-ITMPermission {}
function Set-ITMWindowIcon($Form){$Form.Icon=New-Object Drawing.Icon((Join-Path $PSScriptRoot 'Assets/ITM_CD.ico'))}
function Enable-ITMDialogShortcuts {}
function Apply-ITMThemeForm($Form){[ITMVisualTheme]::PrepareWindow($Form)}
function Check($condition,$message){if(-not $condition){throw $message}}
$script:main=New-Object Windows.Forms.Form;$script:main.Font=New-Object Drawing.Font('Segoe UI',10);$script:busy=$false
$root=Join-Path ([IO.Path]::GetTempPath()) ('SCOLIA_design_test_'+[guid]::NewGuid().ToString('N'));[void][IO.Directory]::CreateDirectory($root);$script:storeFolder=$root;$script:store=[pscustomobject]@{Id=('a'*32)}
try{
 $s=Show-ScoliaGlobalSearch -LayoutOnly;$s.Form.Show();[Windows.Forms.Application]::DoEvents()
 try{
  foreach($width in @(320,480,1060,320)){
   $s.Form.ClientSize=New-Object Drawing.Size($width,680);Update-ScoliaFeatureDialogLayout $s;[Windows.Forms.Application]::DoEvents()
   foreach($c in $s.SearchFields.Controls){Check ($c.Right -le $s.SearchFields.ClientSize.Width -and $c.Bottom -le $s.SearchFields.ClientSize.Height) 'Filtre caché'}
   Check ($s.Grid.Height -gt 80) 'Résultats masqués par les filtres';foreach($c in $s.Actions.Controls){Check ($c.Right -le $s.Actions.ClientSize.Width) 'Commande cachée'}
  }
 }finally{$s.Timer.Stop();$script:scoliaFeatureSearchTimers.Remove($s.Timer);$s.Timer.Dispose();$s.Form.Dispose()}
 $b=Show-ITMAutoBackupOptions -LayoutOnly;$b.Form.Show();[Windows.Forms.Application]::DoEvents()
 try{
  Check ($b.Keep.Minimum -eq 1 -and $b.PerDay.Minimum -eq 1 -and $b.Keep.Value -eq 1 -and $b.PerDay.Value -eq 1) 'Valeurs initiales incorrectes'
  foreach($width in @(360,700,360)){$b.Form.ClientSize=New-Object Drawing.Size($width,600);Update-ITMFluidTable $b.Table;[Windows.Forms.Application]::DoEvents();foreach($c in $b.Table.Controls){Check ($c.Right -le $b.Table.ClientSize.Width -and $c.Bottom -le $b.Table.ClientSize.Height) 'Réglage caché'}}
 }finally{$b.Form.Dispose()}
 'PASS : vraies fenêtres de recherche et sauvegarde, champs visibles, commandes accessibles et valeurs à partir de 1.'
}finally{$script:main.Dispose();Remove-Item -LiteralPath $root -Recurse -Force}
