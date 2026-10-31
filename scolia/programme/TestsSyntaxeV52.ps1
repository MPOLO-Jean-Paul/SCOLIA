# Verification native de tous les scripts, sans execution des modules ni acces Excel.
$ErrorActionPreference='Stop'
$failures=New-Object 'System.Collections.Generic.List[string]'
$count=0
Write-Host ('Dossier verifie : '+$PSScriptRoot)
if(Test-Path -LiteralPath (Join-Path $PSScriptRoot 'EtatEspace.ps1') -PathType Leaf){
    Write-Host ('Empreinte EtatEspace.ps1 : '+(Get-FileHash -LiteralPath (Join-Path $PSScriptRoot 'EtatEspace.ps1') -Algorithm SHA256).Hash)
}
foreach($file in @(Get-ChildItem -LiteralPath $PSScriptRoot -Filter '*.ps1' -File | Sort-Object Name)){
    $tokens=$null;$errors=$null
    [void][Management.Automation.Language.Parser]::ParseFile($file.FullName,[ref]$tokens,[ref]$errors)
    $count++
    foreach($error in $errors){
        $failures.Add($file.Name+' : ligne '+$error.Extent.StartLineNumber+', colonne '+$error.Extent.StartColumnNumber+' : '+$error.Message)
    }
}
if($failures.Count){
    foreach($failure in $failures){Write-Host $failure -ForegroundColor Red}
    exit 1
}
Write-Host ('OK : syntaxe de '+$count+' scripts verifiee par le parseur PowerShell.') -ForegroundColor Green
exit 0
