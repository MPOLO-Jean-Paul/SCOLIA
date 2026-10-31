$ErrorActionPreference='Stop'
function Import-TestFunction([string]$file,[string]$name){
 $tokens=$null;$errors=$null;$ast=[Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot $file),[ref]$tokens,[ref]$errors)
 $node=$ast.Find({param($n) $n -is [Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -eq $name},$true)
 if(-not $node){throw $name};. ([scriptblock]::Create('function global:'+ $name + $node.Extent.Text.Substring(('function '+$name).Length)))
}
foreach($n in @('Show-Page','Update-WorkspaceState')){Import-TestFunction 'Application.ps1' $n}
foreach($n in @('Test-ITMPageNeedsRefresh','Refresh-ITMCurrentPage')){Import-TestFunction 'PerformanceInterface.ps1' $n}
function Update-ITMRibbonState {}
function Update-ScoliaFeatureCommands {}
function Update-ITMNavigation {}
function Update-PaymentButton {}
function Apply-ITMRoleControls {}
function Update-ITMCardSelection {$script:scans++}
function Refresh-Learners {$script:refreshes++}
function Refresh-Dashboard {$script:refreshes++;$script:dashboardDate=(Get-Date).ToString('yyyyMMdd')}
function Refresh-ITMSchoolCards {$script:refreshes++}
function Do-Work($action,$message){$script:cycles++; & $action}
$script:pages=@{};$script:nav=@{};$script:navIcons=@{}
foreach($name in @('Apprenants','Exports','Tableau de bord')){
 $panel=[pscustomobject]@{Visible=$false;Enabled=$true;IsDisposed=$false};$panel|Add-Member ScriptMethod BringToFront {}
 $script:pages[$name]=$panel;$script:nav[$name]=[pscustomobject]@{Active=$false;ForeColor=$null;Image=$null;AccessibleDescription='';Enabled=$true;IsDisposed=$false}
}
$script:pageTitle=[pscustomobject]@{Text=''};$script:book=$true;$script:data=[pscustomobject]@{closed=$false};$script:schoolGrid=$true
$script:dataRevision=1;$script:viewRevision=@{};$script:cycles=0;$script:refreshes=0;$script:scans=0
# Initialisation du catalogue peut déjà avoir défini currentPage avant le premier affichage.
$script:currentPage='Apprenants'
Show-Page 'Apprenants'
if(-not $script:pages['Apprenants'].Visible){throw 'Première page masquée après initialisation du catalogue'}
Show-Page 'Exports';Show-Page 'Apprenants'
if($cycles -ne 0 -or $refreshes -ne 2){throw 'Page à jour inutilement actualisée'}
$script:dataRevision++;Show-Page 'Apprenants';if($refreshes -ne 3){throw 'Modification non actualisée'}
Update-WorkspaceState;if($scans -ne 0){throw 'Liste cachée reparcourue'}
Show-Page 'Exports';Update-WorkspaceState;if($scans -ne 1){throw 'Sélection visible non actualisée'}
Show-Page 'Tableau de bord';$count=$refreshes;Show-Page 'Tableau de bord';if($refreshes -ne $count){throw 'Tableau de bord actualisé sans changement'}
$script:dashboardDate='19000101';Show-Page 'Tableau de bord';if($refreshes -ne $count+1){throw 'Retards non recalculés au changement de date'}
'PASS navigation en cache, invalidation après modification, liste cachée évitée, changement de date des retards'
