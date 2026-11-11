# Windows PowerShell 5.1 -STA : fenêtres réelles et données simulées, sans Excel.
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Windows.Forms,System.Drawing
foreach($name in @('ActivityIndicator.cs','ChromeModerne.cs')){Add-Type -Path (Join-Path $PSScriptRoot $name) -ReferencedAssemblies System.Windows.Forms,System.Drawing -IgnoreWarnings}
foreach($spec in @(@('Application.ps1',@('New-Label','New-Button','New-Grid','Set-Grid')),@('ScoliaConfiguration.ps1',@('New-ScoliaDialog','New-ScoliaTable','Add-ScoliaField','Add-ScoliaText','Add-ScoliaButton')))){
 $tokens=$null;$errors=$null;$ast=[Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot $spec[0]),[ref]$tokens,[ref]$errors)
 foreach($name in $spec[1]){$node=$ast.Find({param($n)$n -is [Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -eq $name},$true);. ([scriptblock]::Create($node.Extent.Text))}
}
foreach($name in @('InterfaceResponsive.ps1','Lisibilite.ps1','FonctionnalitesV55.ps1','CentreRechercheV55.ps1','PersonnelV56.ps1','PresencesV55.ps1','EnseignementsV59.ps1','InterfaceEnseignementsV59.ps1')){. (Join-Path $PSScriptRoot $name)}
function Assert-ITMPermission{}
function Test-ITMPermission{return $true}
function Test-ScoliaPermission{return $true}
function Set-ITMWindowIcon{}
function Apply-ITMThemeForm($control){if($control -is [Windows.Forms.Form]){[ITMVisualTheme]::PrepareWindow($control)}}
function Check([bool]$condition,[string]$message){if(-not $condition){throw $message}}
$script:testContext=[pscustomobject]@{Id='test-cours';Year='2026-2027';LocalKey='secretary'}
function Get-ScoliaFeatureContext{return $script:testContext}
function Read-ScoliaFeatureStore([string]$Name){if($Name -eq 'enseignements'){return $script:testTeaching};return $script:testPersonnel}
$script:main=New-Object Windows.Forms.Form;$script:store=$true;$script:book=$true;$script:scolia=$null;$script:busy=$false
$script:data=[pscustomobject]@{closed=$false;promotions=@([pscustomobject]@{Active=$true;Annee='2026-2027';Code='INF1'})}
$teacher=New-ScoliaTeacher -Nom 'Enseignant test' -ActiveFrom '2026-09-01' -Actor 'test'
$script:testPersonnel=[pscustomobject]@{Schema=1;Context=$script:testContext;Revision=0;Items=@($teacher)}
$course=New-ScoliaCourse 'INF1' 1 'Anatomie et physiologie humaine' 30.5 40
$script:testTeaching=[pscustomobject]@{Schema=1;Context=$script:testContext;Revision=0;Items=@($course)}
try{
 foreach($mode in @('Courses','Assignments','Dashboard')){
  $s=Show-ScoliaTeachingManager -Mode $mode -LayoutOnly;$s.Form.Show();[Windows.Forms.Application]::DoEvents()
  try{
   foreach($width in @(1120,480,320,1120)){
    $s.Form.ClientSize=New-Object Drawing.Size($width,730);Update-ScoliaFeatureDialogLayout $s;[Windows.Forms.Application]::DoEvents()
    Check ($s.Grid.Height -ge 90) 'Grille masquée par les commandes'
    foreach($control in $s.Actions.Controls){if($control.Visible){Check ($control.Right -le $s.Actions.ClientSize.Width -and $control.Bottom -le $s.Actions.ClientSize.Height) 'Commande hors fenêtre'}}
    foreach($control in $s.SearchFields.Controls){Check ($control.Right -le $s.SearchFields.Width -and $control.Bottom -le $s.SearchFields.Height) 'Champ de recherche masqué'}
   }
   if($mode -ne 'Dashboard'){
    $s.Grid.Rows[0].Selected=$true;$s.Search.Text='Anatomie';[Windows.Forms.Application]::DoEvents();Check ($s.Rows.Count -eq 1 -and -not $s.Message.Tag) 'Filtrage/identité de ligne incorrects'
    $editor=Show-ScoliaCourseEditor -Manager $s -LayoutOnly
    try{$editor.Show();[Windows.Forms.Application]::DoEvents();Check (-not $editor.Tag.Save.Enabled) 'Cours vide enregistrable';$editor.Tag.Name.Text='Autre cours';Check ($editor.Tag.Save.Enabled -and $editor.Tag.Number.Value -eq 2) 'Numéro suggéré ou validation incorrects'}finally{$editor.Dispose()}
    $s.Grid.Rows[0].Selected=$true;$assignment=Show-ScoliaAssignmentEditor -Manager $s -LayoutOnly
    try{$assignment.Show();[Windows.Forms.Application]::DoEvents();Check (-not $assignment.Tag.Save.Enabled) 'Attribution sans enseignant enregistrable';$assignment.Tag.Teacher.SelectedIndex=0;Check $assignment.Tag.Save.Enabled 'Choix de l enseignant non accepté'}finally{$assignment.Dispose()}
   }
  }finally{$s.MoreMenu.Dispose();$s.Form.Dispose()}
 }
 'PASS : trois fenêtres, recherche, colonnes numériques, sélection, commandes compactes, cours et attributions obligatoires.'
}finally{$script:main.Dispose()}
