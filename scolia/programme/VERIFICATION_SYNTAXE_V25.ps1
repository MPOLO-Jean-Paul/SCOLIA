$ErrorActionPreference = 'Stop'
$checker = Join-Path $PSScriptRoot 'VERIFICATION_SYNTAXE_V27.ps1'
if (-not (Test-Path -LiteralPath $checker)) {
    Write-Error 'Le vérificateur V27 est absent du dossier programme.'
    exit 2
}
& $checker
exit $LASTEXITCODE
