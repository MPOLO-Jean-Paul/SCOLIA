param([string]$Lanceur,[switch]$SansLancement,[switch]$VerifierSeulement)
$ErrorActionPreference='Stop'
$next=Join-Path $PSScriptRoot 'MettreAJourV57.ps1'
if(-not (Test-Path -LiteralPath $next -PathType Leaf)){throw 'Extrayez le ZIP V57 entièrement puis utilisez METTRE_A_JOUR_V57.cmd.'}
& $next @PSBoundParameters
