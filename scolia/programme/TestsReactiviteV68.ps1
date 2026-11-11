$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'ReactiviteV68.ps1')
# Le clic survient après la fin de la fonction qui a construit le bouton.
function Build-ScopedActionV68 {
 function Invoke-ScoliaWorkspaceView([string]$Name){$global:scopedViewV68=$Name}
 return (New-ScoliaWorkspaceViewAction 'Discipline')
}
$action=Build-ScopedActionV68
& $action
if($global:scopedViewV68 -ne 'Discipline'){throw 'Action hors portée non exécutée.'}
Remove-Variable scopedViewV68 -Scope Global
$t=$null;$e=$null
$workspaceAst=[Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot 'EspaceFonctionnelV67.ps1'),[ref]$t,[ref]$e)
$n=$workspaceAst.Find({param($n)$n -is [Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -eq 'Get-ScoliaWorkspaceViewNames'},$true);. ([scriptblock]::Create($n.Extent.Text))
$styleAst=[Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot 'StyleModerne.ps1'),[ref]$t,[ref]$e)
$n=$styleAst.Find({param($n)$n -is [Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -eq 'New-ITMCommand'},$true);. ([scriptblock]::Create($n.Extent.Text))
function Get-ITMCommandInfo($label){return [pscustomobject]@{Description=$label;Shortcut='Ctrl+D';Keys=0}}
function Get-ITMCommandPermission($label){return 'Lire'}
function Register-ITMFrequentCommand($command){}
function Build-RealScopedCommandV68 {
 function Invoke-ScoliaWorkspaceView([string]$Name){$global:scopedViewV68=$Name}
 return (New-ITMCommand 'Discipline')
}
$command=Build-RealScopedCommandV68;& $command.Action
if($global:scopedViewV68 -ne 'Discipline'){throw 'Le bouton réel ne retrouve pas son action.'}
Remove-Variable scopedViewV68 -Scope Global
$script:plans=0
function Get-ScoliaRibbonLayout($counts,$width,$height,$scale,$textWidths,$textHeights){$script:plans++;return [pscustomobject]@{Width=$width;Height=$height;Scale=$scale}}
for($i=0;$i -lt 30;$i++){[void](Get-ScoliaCachedRibbonLayout @(5,4) 1000 150 1 @(150,150) @(28,28))}
if($script:plans -ne 1){throw 'Plan recalculé malgré une géométrie identique.'}
[void](Get-ScoliaCachedRibbonLayout @(5,4) 800 150 1 @(150,150) @(28,28))
[void](Get-ScoliaCachedRibbonLayout @(5,4) 800 150 2 @(150,150) @(28,28))
if($script:plans -ne 3){throw 'Cache conservé après changement de taille ou DPI.'}
for($i=0;$i -lt 70;$i++){[void](Get-ScoliaCachedRibbonLayout @(5,4) (810+$i) 150 1 @(150,150) @(28,28))}
if($script:scoliaRibbonPlanCache.Count -gt 32){throw 'Cache de géométrie non borné.'}
$t=$null;$e=$null;$ast=[Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot 'CentreRechercheV55.ps1'),[ref]$t,[ref]$e)
foreach($name in @('ConvertTo-ScoliaSearchKey','Test-ScoliaSearchTokens','Update-ScoliaSearchFeature')){$n=$ast.Find({param($n)$n -is [Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -eq $name},$true);. ([scriptblock]::Create($n.Extent.Text))}
$script:indexBuilds=0;$script:stamp='one'
function Get-ScoliaSearchIndexStamp{return $script:stamp}
function Get-ScoliaCachedSearchRows {param([switch]$Force);$script:indexBuilds++;return [pscustomobject]@{SearchKey='ECOLE';Action='Learner'}}
function Set-Grid {}
$state=[pscustomobject]@{Search=[pscustomobject]@{Text=''};Kind=[pscustomobject]@{SelectedItem='Tous'};Message=[pscustomobject]@{Tag=$null;Text=''};Rows=@();SourceRows=@();SourceStamp='';Grid=[pscustomobject]@{Columns=@{'Clé'=[pscustomobject]@{Visible=$true}};Enabled=$true};OpenButton=[pscustomobject]@{Enabled=$false}}
Update-ScoliaSearchFeature $state -Reload
$state.Search.Text='e';Update-ScoliaSearchFeature $state
if($script:indexBuilds -ne 0){throw 'Index construit avant une requête utilisable.'}
$state.Search.Text='ecole';Update-ScoliaSearchFeature $state;Update-ScoliaSearchFeature $state
if($script:indexBuilds -ne 1 -or $state.Rows.Count -ne 1){throw 'Recherche ou réutilisation de lʼindex incorrecte.'}
$script:stamp='two';Update-ScoliaSearchFeature $state
if($script:indexBuilds -ne 2){throw 'Changement de données ignoré.'}
'PASS : action exécutée après sa création, cache borné et invalidé, recherche différée et données actualisées.'
