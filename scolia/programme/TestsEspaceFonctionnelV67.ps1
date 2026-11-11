$ErrorActionPreference='Stop'
$tokens=$null;$errors=$null
$ast=[Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot 'EspaceFonctionnelV67.ps1'),[ref]$tokens,[ref]$errors)
foreach($name in @('Get-ScoliaWorkspaceViewNames','Get-ScoliaWorkspaceViewPermission','Get-ScoliaWorkspaceIdentity','Invoke-ScoliaWorkspaceView','Ensure-ScoliaDefaultWorkspaceView')){
 $n=$ast.Find({param($n)$n -is [Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -eq $name},$true);. ([scriptblock]::Create($n.Extent.Text))
}
function Get-ITMCommandPermission($name){return 'Lire'}
function Assert-ITMPermission($name){$script:permission=$name;if($script:deny){throw 'Accès refusé'}}
function Update-ScoliaActiveWorkspaceCommand{}
function Show-Page($name){$script:currentPage=$name}
function Reset-ScoliaInlineViews{$script:scoliaInlineViews=@{};$script:scoliaInlineIdentity='';$script:resets++}
function Show-ScoliaGlobalSearch{$script:opened++;$script:requested=$script:scoliaInlineRequest.Name}
function Show-ScoliaMonthlyPayroll{$script:opened++;$script:requested=$script:scoliaInlineRequest.Name}
$script:scoliaInlineViews=@{};$script:scoliaInlineIdentity='';$script:scoliaInlineRequest=$null;$script:opened=0;$script:resets=0
$script:book=[pscustomobject]@{FullName='test.xlsm'};$script:data=[pscustomobject]@{};$script:scolia=[pscustomobject]@{Id='school';Year='2026-2027'};$script:accountSession=[pscustomobject]@{Id='one';Role='Administrateur'};$script:currentPage='Scolarité'
Ensure-ScoliaDefaultWorkspaceView
if($script:opened -ne 1 -or $script:requested -ne 'Recherche globale' -or $script:scoliaInlineRequest){throw 'Premier écran ou nettoyage du contexte incorrect.'}
$fake=[pscustomobject]@{IsDisposed=$false;Visible=$false};$fake|Add-Member ScriptMethod BringToFront {}
$script:scoliaInlineViews['Recherche globale']=$fake
Invoke-ScoliaWorkspaceView 'Recherche globale'
if($script:opened -ne 1 -or -not $fake.Visible){throw 'Interface reconstruite au lieu de conserver les filtres.'}
$before=$script:resets;$script:accountSession.Id='two';Ensure-ScoliaDefaultWorkspaceView
if($script:resets -ne $before+1 -or $script:opened -ne 2){throw 'Ancienne interface conservée après changement de compte.'}
Invoke-ScoliaWorkspaceView 'Salaires';if($script:permission -ne 'Administrer'){throw 'Droit de gestion des salaires perdu.'}
$script:deny=$true;try{Invoke-ScoliaWorkspaceView 'Salaires';throw 'AUTORISÉ'}catch{if($_.Exception.Message -eq 'AUTORISÉ'){throw}}
$script:deny=$false;$script:busy=$true;$count=$script:opened;Invoke-ScoliaWorkspaceView 'Recherche globale';if($script:opened -ne $count){throw 'Navigation permise pendant une opération.'}
'PASS : premier écran, filtres conservés, contexte de compte invalidé, droits et verrouillage des opérations.'
