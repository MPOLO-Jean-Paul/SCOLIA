# Windows PowerShell 5.1 -STA; isolated animation failures, no Excel/registry writes.
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Windows.Forms,System.Drawing
$tokens=$null;$errors=$null
$ast=[Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot 'PerformanceInterface.ps1'),[ref]$tokens,[ref]$errors)
if($errors.Count){throw 'Invalid activity script.'}
foreach($name in @('Get-ITMActivityAnchor','Start-ITMActivity','Clear-ITMActivityVisual','Stop-ITMActivity')){
    $node=$ast.Find({param($n)$n -is [Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -eq $name},$true)
    . ([scriptblock]::Create($node.Extent.Text))
}
function Assert($value,[string]$message){if(-not $value){throw $message}}
function Get-ITMEffectiveDarkTheme {throw 'Simulated theme failure after clearing button text.'}
$script:main=New-Object Windows.Forms.Form
$button=New-Object Windows.Forms.Button;$button.Text='Enregistrer';$script:main.Controls.Add($button)
$script:activity=$null;$script:activityDepth=0;$script:activitySource=$null;$script:disposed=0
try{
    $script:main.Show()
    Start-ITMActivity 'Test' $button
    Assert ($script:activityDepth -eq 1 -and $null -eq $script:activity -and $button.Text -eq 'Enregistrer') 'Failed setup lost caller depth or button text.'
    Stop-ITMActivity;Assert ($script:activityDepth -eq 0) 'Setup failure left phantom activity.'
    $fake=[pscustomobject]@{}
    $fake | Add-Member ScriptMethod Update {param($text)throw 'Simulated update failure.'}
    $fake | Add-Member ScriptMethod Dispose {$script:disposed++}
    $script:activity=$fake;$script:activityDepth=1
    Start-ITMActivity 'Nested' $button
    Assert ($script:activityDepth -eq 2 -and $script:disposed -eq 1 -and $null -eq $script:activity) 'Nested visual error lost matching depth.'
    Stop-ITMActivity;Assert ($script:activityDepth -eq 1) 'Nested stop released outer call.'
    Stop-ITMActivity;Assert ($script:activityDepth -eq 0) 'Outer stop failed.'
    $fake=[pscustomobject]@{}
    $fake | Add-Member ScriptMethod Update {param($text)}
    $fake | Add-Member ScriptMethod Dispose {$script:disposed++}
    $script:activity=$fake;$script:activityDepth=1
    Start-ITMActivity 'Nested' $button;Stop-ITMActivity
    Assert ($script:activityDepth -eq 1 -and $script:activity -eq $fake) 'Healthy nested stop closed outer visual.'
    Stop-ITMActivity;Stop-ITMActivity
    Assert ($script:activityDepth -eq 0 -and $script:disposed -eq 2) 'Final stop not idempotent.'
    Write-Output 'PASS: setup failure, label restoration, nested update failure, matched stop and final cleanup.'
}finally{$script:main.Dispose()}
