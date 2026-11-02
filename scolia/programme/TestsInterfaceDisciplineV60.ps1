# Fenêtres WinForms réelles ; registre simulé, aucun Excel ni enregistrement utilisateur.
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Windows.Forms,System.Drawing
foreach($name in @('ActivityIndicator.cs','ChromeModerne.cs')){Add-Type -Path (Join-Path $PSScriptRoot $name) -ReferencedAssemblies System.Windows.Forms,System.Drawing -IgnoreWarnings}
foreach($spec in @(@('Application.ps1',@('New-Label','New-Button','New-Grid','Set-Grid')),@('ScoliaConfiguration.ps1',@('New-ScoliaDialog','New-ScoliaTable','Add-ScoliaField','Add-ScoliaText','Add-ScoliaButton')))){
 $tokens=$null;$errors=$null;$ast=[Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot $spec[0]),[ref]$tokens,[ref]$errors)
 foreach($name in $spec[1]){$node=$ast.Find({param($n)$n -is [Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -eq $name},$true);. ([scriptblock]::Create($node.Extent.Text))}
}
foreach($name in @('InterfaceResponsive.ps1','Lisibilite.ps1','FonctionnalitesV55.ps1','CentreRechercheV55.ps1','PersonnelV56.ps1','PresencesV55.ps1','EnseignementsV59.ps1','InterfaceEnseignementsV59.ps1','DisciplineV60.ps1','InterfaceDisciplineV60.ps1')){. (Join-Path $PSScriptRoot $name)}
function Check([bool]$Condition,[string]$Message){if(-not $Condition){throw $Message}}
function Assert-ITMPermission{}
function Test-ITMPermission{return $true}
function Test-ScoliaPermission{return $true}
function Set-ITMWindowIcon{}
function Apply-ITMThemeForm($control){if($control -is [Windows.Forms.Form]){[ITMVisualTheme]::PrepareWindow($control)}}
$now=[datetimeoffset]::Now;$first=$(if($now.Month -ge 9){$now.Year}else{$now.Year-1});$year=[string]$first+'-'+[string]($first+1)
$script:testContext=[pscustomobject]@{Id=('a'*64);Year=$year;LocalKey='ui-test'}
function Get-ScoliaFeatureContext{return $script:testContext}
function Read-ScoliaFeatureStore{return $script:testDiscipline}
function Get-ScoliaDisciplineDocuments{return @($script:testDiscipline)}
$root=Join-Path ([IO.Path]::GetTempPath()) ('SCOLIA-discipline-ui-'+[guid]::NewGuid().ToString('N'));[void][IO.Directory]::CreateDirectory($root)
$script:store=$true;$script:storeFolder=$root;$script:book=$true;$script:scolia=$null;$script:busy=$false;$script:main=New-Object Windows.Forms.Form
$learner=[pscustomobject]@{Matricule='ABC001';Nom='Apprenant';PostNom='TEST';Prenom='Une';Promotion='INF1';Annee=$year;Statut='Actif';Tuteur='Parent test';Contact1='0000000000';Contact2='';Naissance='01/01/2010'}
$script:data=[pscustomobject]@{closed=$false;learners=@($learner);promotions=@([pscustomobject]@{Code='INF1';Active=$true;Annee=$year})}
$script:testDiscipline=[pscustomobject]@{Schema=1;Context=$script:testContext;Revision=0;Items=@()}
$request=New-ScoliaDisciplineRequest $learner Temporary 'Premier incident de test' $now.Date.ToString('yyyy-MM-dd') $now.Date.ToString('yyyy-MM-dd') $now.Date.ToString('yyyy-MM-dd')
[void](Add-ScoliaDisciplineDecision $script:testDiscipline $request 0 'Agent test' $now)
$parent=New-ScoliaDisciplineRequest $learner Parents 'Convocation de test' $now.Date.ToString('yyyy-MM-dd') -MeetingAt $now.AddDays(1).ToString('o');[void](Add-ScoliaDisciplineDecision $script:testDiscipline $parent 0 'Agent test' $now)
try{
 $state=Show-ScoliaDiscipline -LayoutOnly;$state.Form.Show();[Windows.Forms.Application]::DoEvents()
 try{
  foreach($width in @(1180,480,320,1180)){
   $state.Form.ClientSize=New-Object Drawing.Size($width,760);Update-ScoliaFeatureDialogLayout $state;[Windows.Forms.Application]::DoEvents()
   Check ($state.Grid.Height -ge 90) 'Registre masqué par les commandes'
   foreach($control in $state.Actions.Controls){if($control.Visible){Check ($control.Right -le $state.Actions.ClientSize.Width -and $control.Bottom -le $state.Actions.ClientSize.Height) 'Commande masquée'}}
  }
  $state.Grid.Rows[0].Selected=$true;$key=[string]$state.Grid.Rows[0].Cells['Clé'].Value;$state.Search.Text='ABC001';[Windows.Forms.Application]::DoEvents()
  Check ((Get-ScoliaFeatureSelectedRow $state).Key -ceq $key) 'Plusieurs décisions du même apprenant perdant leur identité de sélection'
  $editor=Show-ScoliaDisciplineEditor -Manager $state -Learner $learner -LayoutOnly;$editor.Show();[Windows.Forms.Application]::DoEvents()
  try{
   Check ($editor.Tag.PreviousCount -eq 1 -and $editor.Tag.Confirm.Visible -and -not $editor.Tag.Save.Enabled) 'Deuxième sanction sans avertissement ou confirmation'
   $editor.Tag.Reason.Text='Nouveau motif à vérifier';Check (-not $editor.Tag.Save.Enabled) 'Validation possible sans confirmation du renvoi définitif'
   $editor.Tag.Confirm.Checked=$true;Check $editor.Tag.Save.Enabled 'Confirmation non prise en compte'
   $editor.Tag.Type.SelectedIndex=2;Check ($editor.Tag.Meeting.Visible -and -not $editor.Tag.Confirm.Visible) 'Convocation affichant les contrôles de renvoi définitif'
   $editor.Tag.Reason.Clear();Check (-not $editor.Tag.Save.Enabled) 'Motif vide accepté'
  }finally{$editor.Dispose()}
  $script:data.closed=$true;Update-ScoliaDisciplineView $state
  Check (-not $state.NewButton.Enabled -and -not $state.CancelButton.Enabled) 'Année clôturée modifiable'
 }finally{$state.MoreMenu.Dispose();$state.Form.Dispose()}
 'PASS : vrai registre, recherche, sélection par décision, commandes compactes, avertissement de récidive, motif et confirmation obligatoires, convocation et année clôturée.'
}finally{$script:main.Dispose();Remove-Item -LiteralPath $root -Recurse -Force}
