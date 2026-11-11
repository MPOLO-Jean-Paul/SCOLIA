# Vrais contrôles WinForms isolés ; aucune ouverture Excel ou données métier.
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Windows.Forms,System.Drawing
foreach($name in @('ActivityIndicator.cs','ChromeModerne.cs')){Add-Type -Path (Join-Path $PSScriptRoot $name) -ReferencedAssemblies System.Windows.Forms,System.Drawing}
foreach($pair in @(@('Application.ps1',@('New-Label','New-Button','New-Grid')),@('ScoliaConfiguration.ps1',@('New-ScoliaDialog')))){
 $tokens=$null;$errors=$null;$ast=[Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot $pair[0]),[ref]$tokens,[ref]$errors)
 if($errors.Count){throw ('Erreur de syntaxe : '+$pair[0])}
 foreach($name in $pair[1]){$node=$ast.Find({param($n)$n -is [Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -eq $name},$true);. ([scriptblock]::Create($node.Extent.Text))}
}
. (Join-Path $PSScriptRoot 'ThemesAffichage.ps1')
. (Join-Path $PSScriptRoot 'FonctionnalitesV55.ps1')
function Set-ITMWindowIcon($form){}
function Set-ITMGridReadability($grid){}
function Test-ITMPermission($name){return $name -notin @($script:deniedFeaturePermissions)}
function Test-ScoliaPermission($name){return $true}
function Assert-InterfaceV55([bool]$ok,[string]$message){if(-not $ok){throw $message}}
$script:displayPreferences=[pscustomobject]@{Theme='Clair'}
$state=New-ScoliaFeatureDialog -Title 'Présences et absences' -Subtitle 'Appel quotidien'
try{
 $state.Form.Show();[Windows.Forms.Application]::DoEvents()
 foreach($theme in @('Clair','Sombre')){
  $script:displayPreferences.Theme=$theme;Apply-ITMThemeForm $state.Form
  foreach($width in @(980,500)){
   $state.Form.Size=New-Object Drawing.Size($width,440);[Windows.Forms.Application]::DoEvents()
   Assert-InterfaceV55 (-not $state.Header.Bounds.IntersectsWith($state.Toolbar.Bounds)) 'En-tête et actions se recouvrent.'
   Assert-InterfaceV55 (-not $state.Toolbar.Bounds.IntersectsWith($state.Body.Bounds)) 'Les actions recouvrent la grille.'
   Assert-InterfaceV55 (-not $state.Message.Bounds.IntersectsWith($state.Body.Bounds)) 'Le message masque la grille.'
   Assert-InterfaceV55 ($state.Grid.Height -gt 70) 'La grille est inaccessible dans une petite fenêtre.'
  }
 }
}finally{$state.Form.Dispose()}
$script:main=New-Object Windows.Forms.Form;$script:main.Size=New-Object Drawing.Size(900,600)
$hostPanel=New-Object Windows.Forms.Panel;$hostPanel.Dock='Fill';$script:main.Controls.Add($hostPanel)
$script:brand=New-Object Windows.Forms.Panel;$script:brand.Dock='Top';$script:brand.Height=120;$hostPanel.Controls.Add($script:brand)
$status=New-Object Windows.Forms.StatusStrip;$status.Dock='Bottom';$script:main.Controls.Add($status)
$script:busy=$false;$script:data=[pscustomobject]@{};$script:book=[pscustomobject]@{};$script:store=[pscustomobject]@{};$script:scolia=$null
try{
 Initialize-ScoliaFeatureCommands;$script:main.Show();[Windows.Forms.Application]::DoEvents()
 Assert-InterfaceV55 ($script:featureMenu.Parent -eq $script:brand) 'Les nouveaux onglets doivent rester dans le bandeau de l institution.'
 Assert-InterfaceV55 ($null -eq $script:main.MainMenuStrip) 'Un MenuStrip séparé ne doit plus apparaître au-dessus du logo.'
 foreach($width in @(980,760,520,400,320,180)){
  $height=Update-ScoliaFeatureCommandLayout 0 8 $width 1 ($width -lt 650);[Windows.Forms.Application]::DoEvents()
  Assert-InterfaceV55 ($height -ge 30 -and $height -le 220) 'La barre des nouveaux onglets a une hauteur invalide.'
  foreach($button in $script:featureMenuItems.Values){Assert-InterfaceV55 ($button.Left -ge 0 -and $button.Right -le $script:featureMenu.ClientSize.Width -and $button.Bottom -le $script:featureMenu.ClientSize.Height -and $button.Width -gt 0 -and $button.Height -gt 0) 'Un nouvel onglet déborde de sa zone responsive.';Assert-InterfaceV55 (-not [string]::IsNullOrWhiteSpace($button.AccessibleName)) 'Un onglet a perdu son nom accessible.'}
 }
 Assert-InterfaceV55 ($hostPanel.Bottom -le $status.Top) 'La zone principale recouvre la barre d état.'
 Assert-InterfaceV55 ($script:featureMenuItems.Count -eq 2 -and $script:featureToolItems.Count -eq 5) 'Le menu permanent perd des outils scolaires.'
 Assert-InterfaceV55 ($script:featureMenuItems['Outils scolaires'].Visible) 'Les outils scolaires ne restent pas visibles hors du ruban.'
 $script:data=$null;Update-ScoliaFeatureCommands
 Assert-InterfaceV55 (@($script:featureToolItems.Values|Where-Object {$_.Enabled}).Count -eq 0) 'Un outil scolaire reste disponible sans données connectées.'
 $script:data=[pscustomobject]@{};$script:deniedFeaturePermissions=@('Administrer');Update-ScoliaFeatureCommands
 Assert-InterfaceV55 (-not $script:featureToolItems['Salaires'].Enabled -and -not $script:featureToolItems['Réinscription'].Enabled -and $script:featureToolItems['Enseignants'].Enabled) 'Le menu permanent ne respecte pas les permissions.'
 $script:deniedFeaturePermissions=@();Update-ScoliaFeatureCommands
 $script:testFeatureCommand=''
 function Invoke-ScoliaFeatureCommand([string]$Name){$script:testFeatureCommand=$Name}
 $script:featureToolItems['Enseignants'].PerformClick()
 Assert-InterfaceV55 ($script:testFeatureCommand -ceq 'Enseignants') 'Le menu ne dispatch pas sa commande.'
 $alternate=New-Object Windows.Forms.KeyEventArgs([Windows.Forms.Keys]'Control,Alt,K')
 & $script:featureCommandKeyHandler $script:main $alternate
 Assert-InterfaceV55 (-not $alternate.Handled) 'La recherche intercepte le raccourci des prises en charge.'
 $script:busy=$true;Update-ScoliaFeatureCommands
 Assert-InterfaceV55 (@($script:featureMenuItems.Values|Where-Object {$_.Enabled}).Count -eq 0 -and @($script:featureToolItems.Values|Where-Object {$_.Enabled}).Count -eq 0) 'Les commandes restent actives pendant une écriture.'
 Write-Output 'PASS : bandeau intégré, onglets responsives, fenêtre réduite, thèmes et verrouillage pendant une écriture.'
}finally{$script:main.Dispose()}
