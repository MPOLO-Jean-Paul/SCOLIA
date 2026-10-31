# Windows PowerShell 5.1 -STA. Aucun accès aux données ni à Excel.
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Windows.Forms,System.Drawing
foreach($name in @('ActivityIndicator.cs','ChromeModerne.cs')){Add-Type -Path (Join-Path $PSScriptRoot $name) -ReferencedAssemblies System.Windows.Forms,System.Drawing}
. (Join-Path $PSScriptRoot 'ThemesAffichage.ps1')
function Assert-V54([bool]$ok,[string]$message){if(-not $ok){throw $message}}
$script:displayPreferences=[pscustomobject]@{Theme='Clair';Alternating=$true;TextSize=10}
$d=New-Object ScoliaConnectionForm
try{
 $d.Configure($null,'Établissement scolaire de démonstration')
 [void]$d.Account.Items.Add('Secrétariat');$d.Account.SelectedIndex=0
 $d.Show();[Windows.Forms.Application]::DoEvents()
 foreach($theme in @('Clair','Sombre')){
  $script:displayPreferences.Theme=$theme;Apply-ITMThemeForm $d
  foreach($width in @(460,340,560)){
   $d.ClientSize=New-Object Drawing.Size($width,480);[Windows.Forms.Application]::DoEvents()
   Assert-V54 ($d.Password.Top -ge $d.Account.Parent.Bottom) 'Les champs se chevauchent.'
   Assert-V54 ($d.Submit.Top -ge $d.Password.Bottom) 'Le bouton recouvre le mot de passe.'
   Assert-V54 ($d.Submit.Width -eq $d.Password.Width) 'Les champs et le bouton ne sont pas alignés.'
   $d.SetBusy($true)
   Assert-V54 ($d.BusySpinner.Visible -and $d.Submit.Top -ge $d.BusySpinner.Bottom -and $d.BusySpinner.Right -le $d.Submit.Right) 'Roue coupée en petit écran ou sous ce thème.'
   $d.SetBusy($false)
   Assert-V54 (-not $d.Account.Parent.Parent.HorizontalScroll.Visible) 'Défilement horizontal inattendu.'
  }
 }
 $d.SetBusy($true);Assert-V54 (-not $d.Submit.Enabled -and -not $d.Account.Enabled -and -not $d.Password.Enabled) 'La connexion peut être lancée deux fois.'
 Assert-V54 ($d.BusySpinner.Visible -and $d.Submit.Text -eq 'Connexion en cours…' -and $d.Submit.Top -ge $d.BusySpinner.Bottom) 'Retour immédiat absent ou roue coupée.'
 Assert-V54 (-not $d.UseWaitCursor) 'Un curseur de chargement duplique la progression.'
 $d.Close();Assert-V54 ($d.Visible) 'La fermeture interromprait une connexion en cours.'
 $d.SetBusy($false)
 Assert-V54 (-not $d.BusySpinner.Visible -and $d.Submit.Text -eq 'Se connecter') 'Roue non arrêtée après la vérification.'
 $d.Password.Text='essai';$d.MessageLabel.Text='Mot de passe incorrect. Réessayez.'
 Assert-V54 ($d.MessageLabel.Visible -and $d.Submit.Top -ge $d.MessageLabel.Bottom) 'Le message masque le bouton.'
 $d.Password.Text='correction';Assert-V54 ($d.MessageLabel.Text -eq '') 'Le message reste après correction.'
 $d.RequirePassword($false);Assert-V54 (-not $d.Password.Visible -and $d.Password.Text -eq '') 'Un compte sans protection garde un secret.'
 $d.RequirePassword($true);Assert-V54 ($d.Password.Visible -and $d.Password.InputControl.UseSystemPasswordChar) 'La protection visuelle est perdue.'
 Write-Output 'PASS : alignement, thèmes, messages, verrouillage des actions et protection du mot de passe.'
}finally{$d.Dispose()}
