# Isolated ranking and settings tests. Registry access is mocked.
$ErrorActionPreference='Stop'
foreach($name in @('Read-ITMCommandCounts','Get-ITMTopCommands')){
    $tokens=$null;$errors=$null;$ast=[Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot 'RaccourcisFrequents.ps1'),[ref]$tokens,[ref]$errors)
    if($errors.Count){throw 'Syntaxe invalide des raccourcis fréquents.'}
    $node=$ast.Find({param($n)$n -is [Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -eq $name},$true)
    . ([scriptblock]::Create($node.Extent.Text))
}
$script:saved='{"a":7,"b":12,"c":9}'
function Get-ItemProperty {param($LiteralPath,$ErrorAction);return [pscustomobject]@{CountsJson=$script:saved}}
function Test-ITMPermission {param($permission);return $permission -ne 'Interdit'}
$script:frequentCatalog=@{}
foreach($pair in @(@('a','Inscrire'),@('b','Paiements'),@('c','Exporter PDF'),@('d','Actualiser'))){$script:frequentCatalog[$pair[0]]=[pscustomobject]@{UsageId=$pair[0];Label=$pair[1];Permission='Autorisé'}}
$script:frequentCounts=Read-ITMCommandCounts
if(((Get-ITMTopCommands | ForEach-Object {$_.UsageId}) -join ',') -ne 'b,c,a'){throw 'Classement initial incorrect.'}
$script:frequentCounts['d']=20
if(((Get-ITMTopCommands | ForEach-Object {$_.UsageId}) -join ',') -ne 'd,b,c'){throw 'Une commande plus utilisée ne remplace pas le troisième raccourci.'}
$script:frequentCatalog['d'].Permission='Interdit'
if(((Get-ITMTopCommands | ForEach-Object {$_.UsageId}) -join ',') -ne 'b,c,a'){throw 'Une commande interdite apparaît dans les raccourcis.'}
$script:frequentCounts=@{}
if(((Get-ITMTopCommands | ForEach-Object {$_.Label}) -join ',') -ne 'Inscrire,Paiements,Exporter PDF'){throw 'Les raccourcis de démarrage sont incorrects.'}
$script:saved='incomplet'
if((Read-ITMCommandCounts).Count -ne 0){throw 'Un historique incomplet bloque le démarrage.'}
Write-Output 'PASS : trois raccourcis, classement évolutif, droits, valeurs initiales et lecture de l''historique.'
