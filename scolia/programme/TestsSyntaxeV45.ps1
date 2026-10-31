$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$targets = @(
    'Application.ps1',
    'ScoliaConfiguration.ps1',
    'ScoliaIntegration.ps1',
    'PrisesEnCharge.ps1',
    'PerformanceInterface.ps1',
    'Personnalisation.ps1',
    'MiseAJourPerformance.ps1'
)
$failures = New-Object 'System.Collections.Generic.List[string]'
foreach($name in $targets){
    $path = Join-Path $root $name
    if(-not (Test-Path -LiteralPath $path -PathType Leaf)){
        $failures.Add('Fichier manquant : ' + $name)
        continue
    }
    $tokens = $null
    $errors = $null
    [void][Management.Automation.Language.Parser]::ParseFile($path,[ref]$tokens,[ref]$errors)
    foreach($err in @($errors)){
        $failures.Add($name + ' | ligne ' + $err.Extent.StartLineNumber + ', colonne ' + $err.Extent.StartColumnNumber + ' | ' + $err.Message)
    }
}
if($failures.Count -gt 0){
    throw ($failures -join [Environment]::NewLine)
}
'PASS V45 : les scripts critiques sont acceptes par le parseur PowerShell de cette machine.'
