# Exécuter avec Windows PowerShell -STA. Aucun accès aux données de l'établissement.
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Windows.Forms,System.Drawing
foreach($file in @('ActivityIndicator.cs','ChromeModerne.cs')){Add-Type -Path (Join-Path $PSScriptRoot $file) -ReferencedAssemblies System.Windows.Forms,System.Drawing -IgnoreWarnings}
foreach($spec in @(@('Application.ps1',@('New-Label','New-Button','New-Grid')),@('ScoliaConfiguration.ps1',@('New-ScoliaDialog')))){
 $tokens=$null;$errors=$null;$ast=[Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot $spec[0]),[ref]$tokens,[ref]$errors)
 foreach($name in $spec[1]){$node=$ast.Find({param($n)$n -is [Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -eq $name},$true);. ([scriptblock]::Create($node.Extent.Text))}
}
function Set-ITMWindowIcon{}
foreach($file in @('InterfaceResponsive.ps1','InterfaceQualiteV65.ps1','FonctionnalitesV55.ps1','PaiementsMultiplesV72.ps1')){. (Join-Path $PSScriptRoot $file)}
function Apply-ITMThemeForm($Form){Set-ScoliaUniformTypography $Form}
function Check($Value,[string]$Message){if(-not $Value){throw $Message}}
function Check-NoOverlap($Controls){
 $seen=@();foreach($c in $Controls){if(-not $c.Visible){continue};foreach($p in $seen){Check (-not $p.Bounds.IntersectsWith($c.Bounds)) ('Chevauchement : '+$p.Text+' / '+$c.Text)};$seen+=$c}
}
$screen=New-ScoliaFeatureDialog 'Gestion des prises en charge' 'Un intitulé long doit rester visible même avec une police agrandie et une fenêtre étroite.' 1000 700
foreach($caption in @('Nouvelle prise en charge','Déclarer payant','Exporter la liste complète')){[void](New-Button $screen.Actions $caption 0 0 180)}
$screen.Message.Text='Résultat du traitement et informations complémentaires à conserver intégralement à l écran.'
try{
 $screen.Form.Show()
 foreach($size in @(10,14)){
  $script:displayPreferences=[pscustomobject]@{TextSize=$size};Set-ScoliaUniformTypography $screen.Form
  foreach($width in @(360,480,800,1280,360)){
   $screen.Form.ClientSize=New-Object Drawing.Size($width,400);Update-ScoliaFeatureDialogLayout $screen;[Windows.Forms.Application]::DoEvents()
   Check ($screen.Grid.Height -ge 150) 'Tableau écrasé par les commandes.'
   Check ($screen.Header.Bottom -le $screen.Body.Top) 'En-tête sur le tableau.'
   Check ($screen.SubtitleLabel.Bottom -le $screen.Header.Height) 'Sous-titre coupé.'
   Check-NoOverlap @($screen.Actions.Controls)
   foreach($button in $screen.Actions.Controls){Check ($button.Right -le $screen.Actions.ClientSize.Width -and $button.Bottom -le $screen.Actions.ClientSize.Height) 'Bouton hors barre.'}
  }
 }
}finally{$screen.Form.Dispose()}
foreach($registration in @($false,$true)){
 $form=New-Object ITMAccountForm($registration,$false,$false)
 try{
  $form.Show()
  foreach($size in @(10,14)){
   $script:displayPreferences=[pscustomobject]@{TextSize=$size};Set-ScoliaUniformTypography $form
   foreach($width in @(360,480,800,360)){
    $form.ClientSize=New-Object Drawing.Size($width,500);$form.PerformLayout();[Windows.Forms.Application]::DoEvents()
    Check-NoOverlap @($form.Controls)
    Check ($form.Submit.Height -ge $form.Submit.Font.Height+12) 'Bouton du compte trop bas.'
    Check ($form.Username.Parent.Bottom -le $form.Password.Parent.Top) 'Champs du compte superposés.'
   }
  }
 }finally{$form.Dispose()}
}
'OK : interfaces communes et comptes, largeurs 360/480/800/1280, texte 10/14, défilement et absence de chevauchement.'
