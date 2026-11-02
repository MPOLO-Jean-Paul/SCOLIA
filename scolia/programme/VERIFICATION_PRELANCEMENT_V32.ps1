# SCOLIA V32 — preflight local des scripts, sans modification des classeurs Excel.
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$issues = New-Object System.Collections.Generic.List[string]
$files = @(Get-ChildItem -LiteralPath $root -Filter '*.ps1' -File -Recurse | Sort-Object FullName)
foreach ($file in $files) {
    $tokens = $null; $errors = $null
    [void][System.Management.Automation.Language.Parser]::ParseFile($file.FullName, [ref]$tokens, [ref]$errors)
    foreach ($errorItem in $errors) {
        $issues.Add(('{0}:{1}:{2} {3}' -f $file.Name, $errorItem.Extent.StartLineNumber, $errorItem.Extent.StartColumnNumber, $errorItem.Message))
    }
}
$vbs = Join-Path (Join-Path (Split-Path $root -Parent) '00_Demarrage') 'INSTALLER_CORRECTIONS.vbs'
if (Test-Path -LiteralPath $vbs) {
    $bytes = [IO.File]::ReadAllBytes($vbs)
    if ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF) {
        $issues.Add('INSTALLER_CORRECTIONS.vbs contient un BOM UTF-8 ; Windows Script Host peut le refuser.')
    }
    foreach ($b in $bytes) { if ($b -gt 0x7F) { $issues.Add('INSTALLER_CORRECTIONS.vbs doit rester ASCII pur pour la compatibilité VBScript.'); break } }
}
$storage = Get-Content -LiteralPath (Join-Path $root 'StockageClasseurs.ps1') -Raw
foreach ($required in @('Invoke-ITMJsonDirect ''ITMAppSnapshot''','Complete-ITMStoreTransaction','Undo-ITMStoreTransaction')) {
    if ($storage.IndexOf($required, [StringComparison]::Ordinal) -lt 0) { $issues.Add("Contrôle de persistance manquant : $required") }
}
if ($issues.Count) {
    Write-Host 'ECHEC DU CONTROLE SCOLIA' -ForegroundColor Red
    $issues | ForEach-Object { Write-Host (' - ' + $_) }
    exit 1
}
Write-Host ('PASS : {0} scripts PowerShell analysés récursivement ; lanceur VBScript ASCII sans BOM ; marqueurs transactionnels présents.' -f $files.Count) -ForegroundColor Green
Write-Host 'Ce contrôle syntaxique doit être exécuté sur Windows PowerShell 5.1 avant installation.'
