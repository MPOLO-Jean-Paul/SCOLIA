$ErrorActionPreference='Stop'
$root=$PSScriptRoot
$badQuoteChars=@([char]0x2018,[char]0x2019,[char]0x201C,[char]0x201D)
$failures=New-Object System.Collections.Generic.List[string]
$files=Get-ChildItem -LiteralPath $root -Filter '*.ps1' -File | Sort-Object Name
foreach($file in $files){
    $raw=[IO.File]::ReadAllText($file.FullName)
    foreach($ch in $badQuoteChars){
        if($raw.IndexOf($ch) -ge 0){
            $failures.Add($file.Name+' : guillemet typographique PowerShell detecte (U+'+([int]$ch).ToString('X4')+').')
        }
    }
    $tokens=$null;$errors=$null
    [void][Management.Automation.Language.Parser]::ParseFile($file.FullName,[ref]$tokens,[ref]$errors)
    foreach($err in @($errors)){
        $failures.Add($file.Name+' : ligne '+$err.Extent.StartLineNumber+', colonne '+$err.Extent.StartColumnNumber+' : '+$err.Message)
    }
}
if($failures.Count -gt 0){throw ($failures -join [Environment]::NewLine)}
'PASS V46 : tous les scripts PowerShell sont analyses sans erreur et sans guillemets typographiques ambigus.'
