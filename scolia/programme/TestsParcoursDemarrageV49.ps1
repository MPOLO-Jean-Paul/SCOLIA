$ErrorActionPreference='Stop'
$root=[IO.Path]::GetDirectoryName($PSScriptRoot)
$launchers=@(Get-ChildItem -LiteralPath (Join-Path $root '00_Demarrage') -File -Filter '*.vbs')
if($launchers.Count -ne 1 -or $launchers[0].Name -ne 'APPLICATION.vbs'){throw 'Le dossier de démarrage doit contenir uniquement APPLICATION.vbs.'}
$lancer=[IO.File]::ReadAllText((Join-Path $PSScriptRoot 'Lancer.ps1'))
if($lancer -notmatch "Installer\.ps1'\) -Onboarding" -or $lancer -notmatch 'Get-ITMWorkbookLocation -Purpose Connexion'){throw 'Le parcours unifié de démarrage est incomplet.'}
$installer=[IO.File]::ReadAllText((Join-Path $PSScriptRoot 'Installer.ps1'))
foreach($required in @('New-ITMInstallerBlankSource','Nouvelle source','SCOLIA','NewSource')){if($installer -notmatch [regex]::Escape($required)){throw ('Installateur incomplet : '+$required)}}
$session=[IO.File]::ReadAllText((Join-Path $PSScriptRoot 'InstallationSession.ps1'))
if($session -notmatch 'NewSource=\[bool\]\$newSource' -or $session -notmatch '\$Onboarding'){throw 'Le suivi de l installation ne couvre pas le parcours unifié.'}
'OK - parcours SCOLIA V49 présent.'
