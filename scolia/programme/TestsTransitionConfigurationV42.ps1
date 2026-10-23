$ErrorActionPreference='Stop'
$tokens=$null;$errors=$null
$configFile=Join-Path $PSScriptRoot 'ScoliaConfiguration.ps1'
$appFile=Join-Path $PSScriptRoot 'Application.ps1'
[void][Management.Automation.Language.Parser]::ParseFile($configFile,[ref]$tokens,[ref]$errors)
if($errors.Count){throw ('ScoliaConfiguration.ps1 : '+(($errors|ForEach-Object {$_.Message}) -join ' | '))}
$tokens=$null;$errors=$null
[void][Management.Automation.Language.Parser]::ParseFile($appFile,[ref]$tokens,[ref]$errors)
if($errors.Count){throw ('Application.ps1 : '+(($errors|ForEach-Object {$_.Message}) -join ' | '))}
$app=[IO.File]::ReadAllText($appFile)
$setup=$app.IndexOf('Show-ScoliaSetup')
$login=$app.IndexOf('if(-not (Show-ScoliaLogin)){return}')
$main=$app.IndexOf('$script:main=New-Object Windows.Forms.Form')
if($setup -lt 0 -or $login -lt 0 -or $main -lt 0 -or -not ($setup -lt $login -and $login -lt $main)){throw 'Le flux de pré-connexion nʼest pas dans lʼordre attendu : configuration > connexion > construction de lʼespace principal.'}
$config=[IO.File]::ReadAllText($configFile)
foreach($marker in @('Configurer SCOLIA','Étape 1 sur 2','Terminer la configuration','Configuration terminée · ouverture de la connexion…')){if($config.IndexOf($marker) -lt 0){throw ('Élément V42 manquant : '+$marker)}}
'OK - configuration, connexion immédiate et ordre de démarrage V42 vérifiés.'
