# Exercise every ribbon page, including the startup frequent-command pass, without Office or Forms.
$ErrorActionPreference='Stop'
function Import-CatalogueFunction($ast,[string]$name){
 $node=$ast.Find({param($n)$n -is [Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -eq $name},$true)
 if(-not $node){throw ('Fonction introuvable : '+$name)};return [scriptblock]::Create($node.Extent.Text)
}
$tokens=$null;$errors=$null;$catalogue=[Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot 'CommandesClavier.ps1'),[ref]$tokens,[ref]$errors);if($errors.Count){throw 'Syntaxe du catalogue incorrecte'}
foreach($node in $catalogue.FindAll({param($n)$n -is [Management.Automation.Language.AssignmentStatementAst] -and $n.Left.Extent.Text.StartsWith('$script:commandDefinitions')},$true)){. ([scriptblock]::Create($node.Extent.Text))}
# Key conversion is unrelated to launch coverage and needs WinForms on Windows.
function Convert-ITMShortcutKeys([string]$shortcut){if(-not $shortcut){throw 'Raccourci manquant'};return 1}
. (Import-CatalogueFunction $catalogue 'Get-ITMCommandInfo')
$style=[Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot 'StyleModerne.ps1'),[ref]$tokens,[ref]$errors);if($errors.Count){throw 'Syntaxe du ruban incorrecte'}
foreach($name in @('Get-ITMCommandPermission','New-ITMCommand','Update-ITMRibbonState')){. (Import-CatalogueFunction $style $name)}
$script:testCommands=@();$script:testPages=@()
function Register-ITMFrequentCommand($command){$script:testCommands+=,$command}
function Add-ITMRibbonGroup($name,$commands){foreach($command in $commands){if(-not $command.Description -or -not $command.Shortcut){throw ('Aide manquante : '+$command.Label)}}}
function Update-ITMFrequentCommands{}
function Update-ITMNavigation{}
$script:ribbonFlow=[pscustomobject]@{Controls=@()};$script:displayPreferences=$null
$pages=@('Tableau de bord','Apprenants','Paiements','Exports','Diagnostic','Administration','Données','Scolarité','Préférences','Sécurité')
foreach($page in $pages){$script:ribbonPage='';$script:currentPage=$page;Update-ITMRibbonState;$script:testPages+=,$page}
$script:exportTabs=[pscustomobject]@{SelectedIndex=1};$script:ribbonPage='';$script:currentPage='Exports';Update-ITMRibbonState
$labels=@($script:testCommands.Label | Sort-Object -Unique)
$schoolCommands=@('Recherche globale','À traiter','Présences','Réinscription','Enseignants','Salaires','Cotes et délibération','Grilles remplies')
foreach($name in $schoolCommands){
 if($labels -notcontains $name){throw ('Commande de scolarité non construite : '+$name)}
 $info=Get-ITMCommandInfo $name
 if(-not $info -or [string]::IsNullOrWhiteSpace([string]$info.Description) -or [string]::IsNullOrWhiteSpace([string]$info.Shortcut)){throw ('Aide manquante : '+$name)}
}
"PASS : construction des $($pages.Count) onglets, $($labels.Count) commandes distinctes, aides et raccourcis présents"
