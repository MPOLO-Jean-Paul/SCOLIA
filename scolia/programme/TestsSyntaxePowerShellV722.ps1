$ErrorActionPreference='Stop'
$files=Get-ChildItem -LiteralPath $PSScriptRoot -Filter '*.ps1' -File
$allErrors=New-Object 'System.Collections.Generic.List[string]'
foreach($file in $files){
    $tokens=$null
    $errors=$null
    [void][System.Management.Automation.Language.Parser]::ParseFile($file.FullName,[ref]$tokens,[ref]$errors)
    if($errors -and $errors.Count -gt 0){
        foreach($err in $errors){
            $line=$err.Extent.StartLineNumber
            $col=$err.Extent.StartColumnNumber
            $allErrors.Add(($file.Name+': ligne '+$line+', colonne '+$col+' : '+$err.Message))
        }
    }
}
if($allErrors.Count -gt 0){
    Write-Host 'ECHEC - Erreurs de syntaxe PowerShell detectees :' -ForegroundColor Red
    foreach($item in $allErrors){Write-Host (' - '+$item) -ForegroundColor Red}
    exit 1
}
Write-Host ('OK - '+$files.Count+' scripts PowerShell analyses sans erreur de syntaxe.') -ForegroundColor Green
