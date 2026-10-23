$ErrorActionPreference='Stop'
function Import-V66Function($ast,$name){
 $node=$ast.Find({param($n)$n -is [Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -eq $name},$true)
 if(-not $node){throw $name};return [scriptblock]::Create($node.Extent.Text)
}
$tokens=$null;$errors=$null
$catalogue=[Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot 'CommandesClavier.ps1'),[ref]$tokens,[ref]$errors)
$script:commandDefinitions=@{}
foreach($n in $catalogue.FindAll({param($n)$n -is [Management.Automation.Language.AssignmentStatementAst] -and $n.Left.Extent.Text.StartsWith('$script:commandDefinitions')},$true)){. ([scriptblock]::Create($n.Extent.Text))}
function Convert-ITMShortcutKeys($text){return 1}
. (Import-V66Function $catalogue 'Get-ITMCommandInfo')
$style=[Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot 'StyleModerne.ps1'),[ref]$tokens,[ref]$errors)
foreach($name in @('Get-ITMCommandPermission','New-ITMCommand','Update-ITMRibbonState')){. (Import-V66Function $style $name)}
function Register-ITMFrequentCommand($c){}
function Update-ITMFrequentCommands{}
function Update-ITMNavigation{}
function Add-ITMRibbonGroup($name,$commands){$script:commands+=@($commands)}
$script:ribbonFlow=[pscustomobject]@{Controls=@()};$script:exportType=[pscustomobject]@{SelectedIndex=0}
$script:exportTabs=[pscustomobject]@{SelectedIndex=0}
$expected=@('Cocher les cartes','Décocher les cartes','Session actuelle','En-tête des cartes','Aperçu des cartes','Exporter les cartes PDF','Exporter les cartes Word','Imprimer les cartes','Historique des cartes','Dossier des cartes','Grilles remplies')
$script:commands=@();$script:ribbonPage='';$script:currentPage='Scolarité';Update-ITMRibbonState
foreach($label in $expected){if($script:commands.Label -contains $label){throw ('Commande restée dans Scolarité : '+$label)}}
if($script:commands.Label -notcontains 'Cotes et délibération'){throw 'Délibération déplacée hors de Scolarité.'}
$script:commands=@();$script:ribbonPage='';$script:currentPage='Exports';Update-ITMRibbonState
if($script:commands.Label -notcontains 'Exporter PDF' -or $script:commands.Label -notcontains 'Exporter Excel'){throw 'Rapports indisponibles.'}
$script:exportType.SelectedIndex=4;$script:commands=@();$script:ribbonPage='';Update-ITMRibbonState
if($script:commands.Label -notcontains 'Exporter Word'){throw 'Rapport de rentrée Word perdu.'}
$script:exportTabs.SelectedIndex=1;$script:commands=@();$script:ribbonPage='';Update-ITMRibbonState
foreach($label in $expected){if($script:commands.Label -notcontains $label){throw ('Commande absente dans Exports : '+$label)}}
foreach($label in @('Aperçu des cartes','Exporter les cartes PDF','Exporter les cartes Word','Imprimer les cartes')){
 $command=@($script:commands|Where-Object Label -eq $label)[0]
 if($command.Permission -ne 'Exporter'){throw ('Droit dʼexport perdu : '+$label)}
}
'PASS : cartes, impression, historique et documents dans Exports ; délibération dans Scolarité ; rapports PDF/Excel/Word et droits conservés.'
