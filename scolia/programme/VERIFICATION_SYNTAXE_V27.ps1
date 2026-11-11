$ErrorActionPreference = 'Stop'
$extensions = @('.ps1', '.psm1', '.psd1')
$files = @(Get-ChildItem -LiteralPath $PSScriptRoot -Recurse -File | Where-Object { $extensions -contains $_.Extension.ToLowerInvariant() } | Sort-Object FullName)
$failed = $false
Write-Host ('Contrôle syntaxique PowerShell : {0} fichier(s)' -f $files.Count) -ForegroundColor Cyan
foreach ($file in $files) {
    $tokens = $null
    $parseErrors = $null
    try {
        [void][System.Management.Automation.Language.Parser]::ParseFile($file.FullName, [ref]$tokens, [ref]$parseErrors)
    }
    catch {
        $failed = $true
        Write-Host ('ERREUR DE LECTURE : {0} : {1}' -f $file.FullName, $_.Exception.Message) -ForegroundColor Red
        continue
    }
    if ($parseErrors -and $parseErrors.Count -gt 0) {
        $failed = $true
        Write-Host ('ERREUR : {0}' -f $file.FullName) -ForegroundColor Red
        foreach ($e in $parseErrors) {
            Write-Host ('  Ligne {0}, colonne {1} : {2}' -f $e.Extent.StartLineNumber, $e.Extent.StartColumnNumber, $e.Message) -ForegroundColor Red
        }
    }
}
if ($failed) {
    Write-Host 'ÉCHEC : corrigez les erreurs affichées avant de lancer SCOLIA.' -ForegroundColor Red
    exit 1
}
Write-Host 'SUCCÈS : tous les fichiers PowerShell analysés sont syntaxiquement valides.' -ForegroundColor Green
exit 0
