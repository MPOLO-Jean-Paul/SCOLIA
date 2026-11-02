$ErrorActionPreference='Stop'
# Isolated native PowerShell test. The registry cmdlets below are mocked.
foreach($spec in @(@('PreferencesAffichage.ps1','Get-ITMDefaultDisplayPreferences'),@('PreferencesAffichage.ps1','Read-ITMDisplayPreferences'),@('PreferencesAffichage.ps1','Save-ITMDisplayPreferences'),@('StyleModerne.ps1','Test-ITMRibbonExpanded'),@('StyleModerne.ps1','Toggle-ITMRibbonVisibility'))){
    $tokens=$null;$errors=$null;$ast=[Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot $spec[0]),[ref]$tokens,[ref]$errors)
    if($errors.Count){throw ('Syntaxe invalide : '+$spec[0])}
    $name=$spec[1];$definition=$ast.Find({param($node)$node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq $name},$true)
    if(-not $definition){throw ('Fonction absente : '+$name)}
    . ([scriptblock]::Create($definition.Extent.Text))
}
$script:fakeSaved=[pscustomobject]@{Theme='Clair'};$script:written=@{};$script:failSave=$false;$script:failWrite=$false
function Get-ItemProperty {param($LiteralPath,$ErrorAction);return $script:fakeSaved}
function Test-Path {param($LiteralPath);return $false} # Missing test key: never access HKCU.
function New-Item {param($Path,[switch]$Force,$ErrorAction);if($script:failSave){throw 'Échec simulé de mémorisation.'}}
function New-ItemProperty {param($Path,$Name,$Value,$PropertyType,[switch]$Force,$ErrorAction);if($script:failWrite){throw 'Échec simulé de la valeur.'};$script:written[$Name]=[pscustomobject]@{Value=$Value;Type=$PropertyType}}
function Update-ITMNavigation {}
$script:displayPreferences=Read-ITMDisplayPreferences
if($script:displayPreferences.WindowMode -ne 'Une seule fenêtre'){throw 'Le mode unique par défaut est absent.'}
$script:fakeSaved=[pscustomobject]@{SettingsJson='{"WindowMode":"Plusieurs fenêtres"}'}
if((Read-ITMDisplayPreferences).WindowMode -ne 'Plusieurs fenêtres'){throw 'Le mode plusieurs fenêtres n''est pas mémorisé.'}
$script:fakeSaved=[pscustomobject]@{Theme='Clair'}
$script:ribbonPeek=$false;$script:busy=$false;$script:displayRibbon=$null
if($script:displayPreferences.RibbonMode -ne 'Onglets et commandes' -or -not (Test-ITMRibbonExpanded)){throw 'Ancienne préférence incompatible.'}
Toggle-ITMRibbonVisibility
if($script:displayPreferences.RibbonMode -ne 'Onglets seuls' -or (Test-ITMRibbonExpanded)){throw 'Ruban non masqué.'}
if(($script:written['SettingsJson'].Value | ConvertFrom-Json).RibbonMode -ne 'Onglets seuls' -or $script:written['SettingsJson'].Type -ne 'String'){throw 'Choix non mémorisé comme texte.'}
$script:ribbonPeek=$true
if(-not (Test-ITMRibbonExpanded)){throw 'Ouverture temporaire impossible.'}
Toggle-ITMRibbonVisibility
if($script:ribbonPeek -or (Test-ITMRibbonExpanded)){throw 'Fermeture du ruban temporaire impossible.'}
Toggle-ITMRibbonVisibility
if(-not (Test-ITMRibbonExpanded) -or $script:displayPreferences.RibbonMode -ne 'Onglets et commandes'){throw 'Restauration du ruban impossible.'}
$script:failSave=$true;$failed=$false
try{Toggle-ITMRibbonVisibility}catch{$failed=$true}
if(-not $failed -or $script:displayPreferences.RibbonMode -ne 'Onglets et commandes'){throw 'Préférence non restaurée après échec.'}
$script:failSave=$false
$persisted=$script:written['SettingsJson'].Value
$script:fakeSaved=[pscustomobject]@{SettingsJson=$persisted;Theme='Sombre';RibbonMode='Onglets seuls'}
$loaded=Read-ITMDisplayPreferences
if($loaded.Theme -ne 'Clair' -or $loaded.RibbonMode -ne 'Onglets et commandes'){throw 'Lecture du jeu complet incorrecte.'}
$script:failWrite=$true;$failed=$false
try{Toggle-ITMRibbonVisibility}catch{$failed=$true}
if(-not $failed -or $script:written['SettingsJson'].Value -cne $persisted -or $script:displayPreferences.RibbonMode -ne 'Onglets et commandes'){throw 'État partiel après échec écriture.'}
$script:failWrite=$false;$script:busy=$true
Toggle-ITMRibbonVisibility
if($script:displayPreferences.RibbonMode -ne 'Onglets et commandes'){throw 'Modification pendant un traitement.'}
Write-Output 'Modes, mémorisation, ouverture temporaire, reprise après échec et verrou de traitement validés.'
