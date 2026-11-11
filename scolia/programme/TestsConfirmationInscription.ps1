# Windows PowerShell 5.1 : vérification statique de l'inscription persistante et des champs de connexion.
$ErrorActionPreference='Stop'
function Assert-Scolia([bool]$ok,[string]$message){if(-not $ok){throw $message}}
$appPath=Join-Path $PSScriptRoot 'Application.ps1'
$tokens=$null;$errors=$null
$ast=[Management.Automation.Language.Parser]::ParseFile($appPath,[ref]$tokens,[ref]$errors)
Assert-Scolia ($errors.Count -eq 0) ('Application.ps1 contient une erreur de syntaxe : '+(($errors | ForEach-Object Message) -join '; '))
$show=$ast.Find({param($n)$n -is [Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -eq 'Show-Dossier'},$true)
Assert-Scolia ($null -ne $show) 'Fonction Show-Dossier introuvable.'
$body=$show.Extent.Text
Assert-Scolia ($body -match 'Inscription enregistrée') 'Message de succès d''inscription absent.'
Assert-Scolia ($body -match 'Modifications enregistrées') 'Message de succès de modification absent.'
Assert-Scolia ($body -match '\$wasNew') 'Le flux ne distingue pas une nouvelle inscription d''une modification.'
Assert-Scolia ($body -match '\$script:editControls\.Nom\.Focus\(\)') 'Le formulaire ne revient pas au champ Nom après l''enregistrement.'
Assert-Scolia ($body -notmatch '\$script:editDialog\.Close\(\)') 'Le formulaire est encore fermé par le chemin de sauvegarde.'
Assert-Scolia ($body -match 'Get-ITMDossierFingerprint') 'Le point de référence anti-fausse-alerte après sauvegarde est absent.'
Assert-Scolia ($body -match 'ITMSuccessToast') 'La confirmation moderne non bloquante est absente.'
Assert-Scolia ($body -match 'Update-ITMAfterWrite \$savedMat \$true') 'L inscription ne diffère pas le rafraîchissement lourd de la vue.'
$cs=Get-Content -LiteralPath (Join-Path $PSScriptRoot 'ChromeModerne.cs') -Raw -Encoding UTF8
Assert-Scolia ($cs -match 'Password=new TextBox\(\);Password\.BorderStyle=BorderStyle\.None;Password\.AutoSize=false') 'Champ de mot de passe de connexion : AutoSize n''est pas désactivé.'
Assert-Scolia ($cs -match 'var input=new TextBox\(\);input\.BorderStyle=BorderStyle\.None;input\.AutoSize=false') 'Champ de mot de passe du compte : AutoSize n''est pas désactivé.'
Assert-Scolia ($cs -match 'int textHeight=Math\.Min\(inputHeight-') 'Hauteur intérieure du champ de connexion non calculée.'
Assert-Scolia ($cs -match 'int textHeight=Math\.Min\(height-') 'Hauteur intérieure des champs de compte non calculée.'
'PASS : script PowerShell analysé ; le formulaire reste ouvert, la confirmation est affichée, la saisie suivante est préparée et les champs de mot de passe ont une hauteur explicite.'
