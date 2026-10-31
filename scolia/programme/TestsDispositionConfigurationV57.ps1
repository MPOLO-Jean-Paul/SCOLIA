# Native Windows layout regression: actual controls, no configuration writes or Excel.
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Windows.Forms,System.Drawing
foreach($file in @('ActivityIndicator.cs','ChromeModerne.cs')){Add-Type -Path (Join-Path $PSScriptRoot $file) -ReferencedAssemblies System.Windows.Forms,System.Drawing}
Add-Type -Path (Join-Path $PSScriptRoot 'ScoliaLedger.cs') -ReferencedAssemblies System.Xml,System.Core,System.Runtime.Serialization
function Import-ConfigurationLayoutFunction([string]$name){
 $tokens=$null;$errors=$null;$ast=[Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot 'ScoliaConfiguration.ps1'),[ref]$tokens,[ref]$errors)
 if($errors.Count){throw ('Syntaxe invalide : '+(($errors|ForEach-Object {$_.Message}) -join ' | '))}
 $node=$ast.Find({param($n)$n -is [Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -eq $name},$true)
 if(-not $node){throw ('Fonction manquante : '+$name)}
 . ([scriptblock]::Create('function global:'+ $name + $node.Extent.Text.Substring(('function '+$name).Length)))
}
foreach($name in @('New-ScoliaDialog','New-ScoliaCardContent','New-ScoliaSetupFields','Add-ScoliaSetupField','Add-ScoliaSetupText','Add-ScoliaSetupNote','ConvertFrom-ScoliaClassText','Test-ScoliaDataRootCandidate','Show-ScoliaSetup')){Import-ConfigurationLayoutFunction $name}
function Set-ITMWindowIcon($form){}
function Close-ScoliaTransition {}
function Get-ITMInstitutionName {return 'École de test'}
function Promo($text){return ([string]$text).Trim().ToUpperInvariant()}
. (Join-Path $PSScriptRoot 'ThemesAffichage.ps1')
function Assert-ConfigurationLayout([bool]$Condition,[string]$Message){if(-not $Condition){throw $Message}}
function Get-ConfigurationControlRectangle($form,$control){
 $position=$form.PointToClient($control.PointToScreen((New-Object Drawing.Point(0,0))))
 return (New-Object Drawing.Rectangle($position,$control.Size))
}
function Assert-ConfigurationSiblings($parent){
 # The password component owns its internal reveal overlay; verify its outer native bounds only.
 if($parent -is [ScoliaPasswordBox]){return}
 $children=@($parent.Controls|Where-Object {$_.Visible})
 for($i=0;$i -lt $children.Count;$i++){
  for($j=$i+1;$j -lt $children.Count;$j++){
   Assert-ConfigurationLayout (-not $children[$i].Bounds.IntersectsWith($children[$j].Bounds)) ('Chevauchement dans '+$parent.GetType().Name+' : '+$children[$i].GetType().Name+' / '+$children[$j].GetType().Name)
  }
  # Scrollable viewports may legitimately expose content below the visible area.
  if($parent -isnot [Windows.Forms.ScrollableControl] -or -not $parent.AutoScroll){
   Assert-ConfigurationLayout ($children[$i].Right -le $parent.ClientSize.Width+1 -and $children[$i].Bottom -le $parent.ClientSize.Height+1) ('Contenu coupé dans '+$parent.GetType().Name+' : '+$children[$i].GetType().Name)
  }
  Assert-ConfigurationSiblings $children[$i]
 }
}
$script:displayPreferences=[pscustomobject]@{Theme='Clair'}
$state=Show-ScoliaSetup -LayoutOnly
$uncreatedRoot=Join-Path ([IO.Path]::GetTempPath()) ('scolia-layout-'+[guid]::NewGuid().ToString('N'))
try{
 $state.Form.Show();[Windows.Forms.Application]::DoEvents()
 $q=[Math]::Max(1.0,$state.Form.CurrentAutoScaleDimensions.Height/96.0)
 $state.Root.Text=$uncreatedRoot
 $state.QuickCode.Text='INF1';$state.QuickFee.Value=25000;$state.QuickAdd.PerformClick();[Windows.Forms.Application]::DoEvents()
 Assert-ConfigurationLayout ($state.Classes.Text -eq 'INF1;25000') 'L ajout rapide ne fonctionne plus après construction de la fenêtre.'
 Assert-ConfigurationLayout ($state.ClassInfo.Text -match '1 classe valide' -and $state.ClassInfo.Text -notmatch 'pas reconnu') 'La validation dynamique des classes ne retrouve plus ses fonctions après GetNewClosure.'
 $state.Name.Text='École de test';$state.Year.Text='2026 - 2027'
 foreach($theme in @('Clair','Sombre')){
  $script:displayPreferences.Theme=$theme;Apply-ITMThemeForm $state.Form
  foreach($size in @(@(1120,722),@(960,642),@(600,440),@(410,480))){
   $state.Form.MinimumSize=New-Object Drawing.Size(1,1)
   $state.Form.ClientSize=New-Object Drawing.Size([int]($size[0]*$q),[int]($size[1]*$q));& $state.ApplyLayout;[Windows.Forms.Application]::DoEvents()
   foreach($mode in 0..3){
    $state.Organisation.SelectedIndex=$mode
    $state.Next.PerformClick();[Windows.Forms.Application]::DoEvents()
    Assert-ConfigurationLayout ($state.PageTwo.Visible -and $state.Finish.Visible -and $state.Form.AcceptButton -eq $state.Finish) 'La navigation vers les comptes ne fonctionne plus.'
    $visibleCards=@($state.Accounts.Values|Where-Object {$_.Card.Visible})
    Assert-ConfigurationLayout ($visibleCards.Count -eq $(if($mode -eq 0){2}else{1})) 'Le nombre de cartes ne correspond pas au mode de comptes.'
    Assert-ConfigurationLayout ($state.Organisation.Height -ge $state.Organisation.PreferredSize.Height) 'Le sélecteur Organisation est coupé.'
    foreach($card in $visibleCards){
     $card.Locked.Checked=$true
     Assert-ConfigurationLayout ($card.Password.Enabled -and $card.Confirm.Enabled) 'La protection du compte ne rend pas les deux champs accessibles.'
     Assert-ConfigurationLayout ($card.Password.Height -ge $card.Password.MinimumSize.Height -and $card.Confirm.Height -ge $card.Confirm.MinimumSize.Height) 'Un champ mot de passe est comprimé.'
    }
    Assert-ConfigurationSiblings $state.Form
    $footerRect=Get-ConfigurationControlRectangle $state.Form $state.Footer
    Assert-ConfigurationLayout ($state.Form.ClientRectangle.Contains($footerRect)) 'Le pied de page déborde de la fenêtre.'
    foreach($button in @($state.Back,$state.Cancel,$state.Finish)){
     Assert-ConfigurationLayout ($footerRect.Contains((Get-ConfigurationControlRectangle $state.Form $button))) 'Un bouton est hors du pied de page.'
    }
    if($size[0] -eq 1120 -and $size[1] -eq 722){Assert-ConfigurationLayout (-not $state.PageTwoBody.VerticalScroll.Visible) 'Les comptes nécessitent un défilement à la taille normale.'}
    $state.Back.PerformClick();[Windows.Forms.Application]::DoEvents()
    Assert-ConfigurationLayout ($state.PageOne.Visible -and $state.Next.Visible -and $state.Form.AcceptButton -eq $state.Next) 'Le retour vers l établissement ne fonctionne plus.'
    Assert-ConfigurationSiblings $state.Form
    if($size[0] -eq 1120 -and $size[1] -eq 722){Assert-ConfigurationLayout (-not $state.PageOneBody.VerticalScroll.Visible) 'L établissement nécessite un défilement à la taille normale.'}
   }
  }
 }
 $state.Feedback.Text='Le dossier de données sélectionné est inaccessible. Choisissez un emplacement disponible, puis recommencez.'
 [Windows.Forms.Application]::DoEvents()
 Assert-ConfigurationLayout ($state.Feedback.Visible -and $state.Feedback.Height -ge $state.Feedback.PreferredSize.Height) 'Un message de validation disparaît ou est tronqué.'
 Assert-ConfigurationLayout ($state.Classes.Text -eq 'INF1;25000' -and $state.Name.Text -eq 'École de test' -and $state.Year.Text -eq '2026 - 2027') 'Le redimensionnement ou les changements de comptes ont modifié les valeurs saisies.'
 Assert-ConfigurationLayout (-not [IO.Directory]::Exists($uncreatedRoot)) 'Le test de disposition a écrit dans un dossier métier.'
 'PASS disposition configuration : deux étapes, quatre modes, thèmes clair/sombre, tailles normales/compactes, clics réels, champs non comprimés et messages visibles au DPI Windows courant.'
}finally{$state.Busy.Stop();$state.Form.Dispose()}
