$ErrorActionPreference='Stop'
foreach($spec in @(@('Application.ps1','Save-ConnectionChoice'),@('Application.ps1','Read-ConnectionChoice'),@('LocalisationClasseurs.ps1','Get-ITMLocationPreference'),@('LocalisationClasseurs.ps1','Set-ITMLocationPreference'),@('GestionDonnees.ps1','Remember-ITMCurrentWorkbook'))){
    $tokens=$null;$errors=$null;$ast=[Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot $spec[0]),[ref]$tokens,[ref]$errors)
    if($errors.Count){throw 'Syntaxe invalide.'};$name=$spec[1]
    $definition=$ast.Find({param($n)$n -is [Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -eq $name},$true)
    if(-not $definition){throw $name};. ([scriptblock]::Create($definition.Extent.Text))
}
# Fully mock all registry access. Existing keys must NEVER be recreated.
$script:connectionKey='TEST';$script:locationKey='TEST';$script:fake=@{Classeur='C:\ancien.xlsm';ConnexionAutomatique='non';DossierSource='C:\sources'}
function Test-Path {param($LiteralPath);return $true}
function New-Item {param($Path,[switch]$Force,$ErrorAction);throw 'Une clé existante a été recréée.'}
function Get-ItemProperty {param($LiteralPath,$Name,$ErrorAction);return [pscustomobject]$script:fake}
function New-ItemProperty {param($LiteralPath,$Name,$Value,$PropertyType,[switch]$Force,$ErrorAction);$script:fake[$Name]=$Value}
function Get-ITMWorkbookLocation {param($Purpose,$Explicit);return [pscustomobject]@{Path=$script:fake.Classeur}}
$script:book=[pscustomobject]@{FullName='C:\data\classeur-000002.xlsm'};$script:dataAuto=[pscustomobject]@{Checked=$false}
Remember-ITMCurrentWorkbook
if($script:fake.Classeur -ne $script:book.FullName -or $script:fake.ConnexionAutomatique -ne 'oui' -or $script:fake.DossierSource -ne 'C:\sources'){throw 'Une autre préférence a été effacée.'}
# Simulate reopening: reconstruct memory solely from persisted settings.
$script:rememberedPath='';$Classeur='';$path=Read-ConnectionChoice
if($path -ne $script:book.FullName){throw 'Reconnexion perdue au prochain lancement.'}
if(-not (Set-ITMLocationPreference 'DernierClasseurCree' 'C:\nouveau.xlsm')){throw 'Enregistrement refusé.'}
if((Read-ConnectionChoice) -ne $script:book.FullName){throw 'La création a effacé le classeur mémorisé.'}
$script:fake.ConnexionAutomatique='non'
if(Read-ConnectionChoice){throw 'Choix de non-reconnexion ignoré.'}
Write-Output 'Mémorisation, reconnexion, réglages indépendants et désactivation volontaire validés sans accès au registre réel.'
