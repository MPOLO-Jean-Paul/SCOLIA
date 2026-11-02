# Windows PowerShell 5.1 -STA; aucun classeur ouvert et aucune modification du registre.
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Windows.Forms,System.Drawing
foreach($file in @('ActivityIndicator.cs','ChromeModerne.cs')){Add-Type -Path (Join-Path $PSScriptRoot $file) -ReferencedAssemblies System.Windows.Forms,System.Drawing -IgnoreWarnings}
. (Join-Path $PSScriptRoot 'InterfaceResponsive.ps1')
foreach($spec in @(@('Application.ps1','New-Label'),@('ScoliaConfiguration.ps1','New-ScoliaDialog'),@('ScoliaConfiguration.ps1','New-ScoliaTable'),@('ScoliaConfiguration.ps1','Add-ScoliaField'))){
 $tokens=$null;$errors=$null;$ast=[Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot $spec[0]),[ref]$tokens,[ref]$errors)
 if($errors){throw 'Syntaxe invalide'};$name=$spec[1]
 $node=$ast.Find({param($n)$n -is [Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -eq $name},$true)
 . ([scriptblock]::Create($node.Extent.Text))
}
function Set-ITMWindowIcon($form) {}
function Assert($condition,$message){if(-not $condition){throw $message}}
$f=New-ScoliaDialog 'Test de présentation' 820 650;$script:main=$f
$t=New-ScoliaTable $f
$fields=@()
foreach($caption in @('Nom de lʼétablissement','Année scolaire','Mot de passe actuel','Confirmer le mot de passe','Contact principal')){
 $field=New-Object Windows.Forms.TextBox;$field.Text='Exemple';$fields+=,$field
 [void](Add-ScoliaField $t $caption $field)
}
$notes=New-Object Windows.Forms.TextBox;$notes.Multiline=$true;$notes.Height=120
[void](Add-ScoliaField $t 'Observations' $notes)
$footer=New-Object Windows.Forms.FlowLayoutPanel;$footer.Dock='Bottom';$f.Controls.Add($footer)
foreach($text in @('Enregistrer les paramètres de lʼétablissement','Fermer')){$b=New-Object ITMActionButton;$b.Text=$text;$b.Width=280;$b.Height=36;$footer.Controls.Add($b)}
[ITMVisualTheme]::PrepareWindow($f)
try{
 $f.Show();[Windows.Forms.Application]::DoEvents()
 foreach($width in @(320,480,860,320)){
  $f.ClientSize=New-Object Drawing.Size($width,650);[Windows.Forms.Application]::DoEvents();Update-ITMFluidTable $t
  foreach($c in $t.Controls){Assert ($c.Right -le $t.ClientSize.Width) 'Champ hors de la zone de saisie';Assert ($c.Bottom -le $t.ClientSize.Height) 'Dernière ligne cachée'}
  foreach($c in $footer.Controls){Assert ($c.Right -le $footer.ClientSize.Width) 'Commande hors de la barre dʼactions'}
  if($width -lt 650){Assert ($t.ColumnCount -eq 1) 'Les champs ne sont pas empilés';foreach($field in $fields){Assert ($t.GetRow($field) % 2 -eq 1) 'Libellé et champ se superposent'}}
  else{Assert ($t.ColumnCount -eq 2) 'Les deux colonnes ne sont pas rétablies'}
  Assert ($notes.Height -ge 120) 'Observations comprimées';Assert $f.AutoScroll 'Défilement des formulaires indisponible'
 }
 $activity=New-Object ITMActivity('Vérification…',(New-Object Drawing.Rectangle(40,40,440,144)),[IntPtr]::Zero,'',$false)
 try{Assert ($activity.WaitUntilVisible(2000)) 'Indicateur invisible';$activity.Update('Enregistrement…')}finally{$activity.Dispose()}
 'PASS : formulaires 320–860 px, champs distincts, observations conservées, actions accessibles et indicateur actif.'
}finally{$f.Dispose();foreach($font in $script:adaptiveFonts.Values){$font.Dispose()}}
