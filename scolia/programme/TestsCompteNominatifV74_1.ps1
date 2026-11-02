# Correctif ciblé : disposition de la fenêtre Configuration · Comptes nominatifs.
$ErrorActionPreference='Stop'
$path=Join-Path $PSScriptRoot 'FinanceSynchronisee.ps1'
$tokens=$null;$errors=$null
$ast=[Management.Automation.Language.Parser]::ParseFile($path,[ref]$tokens,[ref]$errors)
if($errors.Count){throw ('Syntaxe FinanceSynchronisee.ps1 : '+(($errors|ForEach-Object {$_.Message}) -join ' | '))}
$node=$ast.Find({param($n)$n -is [Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -eq 'Show-ScoliaAccounts'},$true)
if(-not $node){throw 'Show-ScoliaAccounts introuvable.'}
$text=$node.Extent.Text
function Assert-Layout([bool]$condition,[string]$message){if(-not $condition){throw $message}}
Assert-Layout ($text -match "TableLayoutPanel.*RowCount=2") 'Le formulaire ne sépare plus correctement le corps et les actions.'
Assert-Layout ($text -match "Panel;`$body\.Dock='Fill';`$body\.AutoScroll=`$true") 'Le corps défilant des comptes nominatifs est absent.'
Assert-Layout ($text -match "`$pass\.Height=46.*Drawing\.Size\(180,46\)") 'Le champ Mot de passe n a pas la géométrie attendue.'
Assert-Layout ($text -match "`$confirm\.Height=46.*Drawing\.Size\(180,46\)") 'Le champ Confirmer le mot de passe n a pas la même géométrie.'
Assert-Layout ($text -match "`$root\.Controls\.Add\(`$bar,0,1\)") 'La barre de validation n est pas isolée sous le formulaire.'
Assert-Layout ($text -match "Enable-ITMFluidTable `$t") 'Le réagencement adaptatif du formulaire est absent.'
'PASS : compte nominatif — corps et actions séparés, mot de passe et confirmation alignés, hauteur 46 px logiques, largeur minimale 180 px et réagencement adaptatif.'
