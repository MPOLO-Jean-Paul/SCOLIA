# Windows PowerShell 5.1 -STA. Compile only: no form, Excel, data or registry write.
$ErrorActionPreference='Stop'
# Use the same native parser as Lancer.ps1 before compiling any UI component.
foreach($scriptFile in @(Get-ChildItem -LiteralPath $PSScriptRoot -Filter '*.ps1' -File)){
    $tokens=$null;$parseErrors=$null
    [void][Management.Automation.Language.Parser]::ParseFile($scriptFile.FullName,[ref]$tokens,[ref]$parseErrors)
    if($parseErrors.Count){throw ($scriptFile.Name+': '+(($parseErrors | ForEach-Object {$_.Message}) -join '; '))}
}
Write-Output 'PASS : syntaxe de tous les scripts PowerShell.'
Add-Type -AssemblyName System.Windows.Forms,System.Drawing
foreach($file in @('ActivityIndicator.cs','ChromeModerne.cs','DashboardChart.cs','CartesExamens.cs','NotificationsModernes.cs')){
    Add-Type -Path (Join-Path $PSScriptRoot $file) -ReferencedAssemblies @('System.Windows.Forms','System.Drawing','System.Xml') -IgnoreWarnings -ErrorAction Stop
    Write-Output ('Compilation OK : '+$file)
}
foreach($file in @('SecurityCrypto.cs','WindowsShortcuts.cs','InstancesApplication.cs','ExcelDispatch.cs','CotesMatching.cs','CotesExcel.cs')){
    Add-Type -Path (Join-Path $PSScriptRoot $file) -IgnoreWarnings -ErrorAction Stop
    Write-Output ('Compilation OK : '+$file)
}

Add-Type -Path (Join-Path $PSScriptRoot 'PrisesEnCharge.cs') -ReferencedAssemblies @('System.Xml') -IgnoreWarnings -ErrorAction Stop
Write-Output 'Compilation OK : PrisesEnCharge.cs'
Add-Type -Path (Join-Path $PSScriptRoot 'ScoliaLedger.cs') -ReferencedAssemblies @('System.Xml','System.Core','System.Runtime.Serialization') -IgnoreWarnings -ErrorAction Stop
Write-Output 'Compilation OK : ScoliaLedger.cs'
Add-Type -Path (Join-Path $PSScriptRoot 'ScoliaNetwork.cs') -ReferencedAssemblies @('System','System.Xml','System.Core','System.Runtime.Serialization') -IgnoreWarnings -ErrorAction Stop
Write-Output 'Compilation OK : ScoliaNetwork.cs'
Write-Output 'PASS : les quatorze composants C# sont compilés avec le compilateur Windows utilisé au lancement.'
