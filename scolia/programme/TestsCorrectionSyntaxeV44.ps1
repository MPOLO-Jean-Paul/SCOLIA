$ErrorActionPreference='Stop'
$root=$PSScriptRoot
$targets=@(
    'Application.ps1',
    'ScoliaConfiguration.ps1',
    'ScoliaIntegration.ps1',
    'PrisesEnCharge.ps1',
    'PerformanceInterface.ps1',
    'Personnalisation.ps1',
    'Lancer.ps1'
)
$failures=New-Object 'System.Collections.Generic.List[string]'
foreach($name in $targets){
    $path=Join-Path $root $name
    $tokens=$null
    $errors=$null
    [void][Management.Automation.Language.Parser]::ParseFile($path,[ref]$tokens,[ref]$errors)
    foreach($err in @($errors)){
        $failures.Add(($name+' : ligne '+$err.Extent.StartLineNumber+', colonne '+$err.Extent.StartColumnNumber+' : '+$err.Message))
    }
}
$app=Get-Content -LiteralPath (Join-Path $root 'Application.ps1') -Raw -Encoding UTF8
if($app -match '\$script:navIcons\[\$name\+''\|''\+\$active\]'){$failures.Add('Application.ps1 : ancienne concaténation d''index navIcons encore présente.')}
if($app -match '\$script:navIcons\[\$key\+''\|''\+\$active\]'){$failures.Add('Application.ps1 : ancienne concaténation d''index Show-Page encore présente.')}
if($failures.Count -gt 0){throw ($failures -join [Environment]::NewLine)}
'PASS V44 : scripts critiques analysés par le parseur PowerShell et expressions d''index à risque supprimées.'
