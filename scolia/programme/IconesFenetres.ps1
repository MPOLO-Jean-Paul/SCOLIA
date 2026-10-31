if(-not (Test-Path function:Import-ITMCompiledSource)){. (Join-Path $PSScriptRoot 'CompilationCache.ps1')}
Add-Type -AssemblyName System.Drawing
if(-not ('ITMErrorDialog' -as [type])){Import-ITMCompiledSource -Path (Join-Path $PSScriptRoot 'NotificationsModernes.cs') -ReferencedAssemblies System.Windows.Forms,System.Drawing}
[ITMErrorDialog]::BrandIconPath=Join-Path $PSScriptRoot 'Assets/SCOLIA.ico'
[ITMErrorDialog]::BrandImagePath=Join-Path $PSScriptRoot 'Assets/SCOLIA.png'
[ITMErrorDialog]::SoundPath=Join-Path $PSScriptRoot 'Assets/NotificationDouce.wav' 
$script:windowIconPath=Join-Path $PSScriptRoot 'Assets/SCOLIA.ico'
# L’identité des fenêtres appartient à SCOLIA ; les logos institutionnels restent dans les documents.
function Set-ITMWindowIcon([Windows.Forms.Form]$window) {
    if(-not $window -or $window.IsDisposed){return}
    $icon=New-Object Drawing.Icon($script:windowIconPath)
    try {
        $window.Icon=$icon;$window.ShowIcon=$true
        if("ITMWindowsShortcuts" -as [type]){try{[ITMWindowsShortcuts]::SetWindowIconResource($window.Handle,$script:windowIconPath)}catch{Write-Verbose $_.Exception.Message}}
        $window.Add_Shown({param($sender,$e)
            if(Test-Path function:Apply-ITMThemeForm){Apply-ITMThemeForm $sender}
        })
        # Each dialog owns its icon until that dialog is disposed.
        $window.Add_Disposed({param($sender,$e)$icon.Dispose()}.GetNewClosure())
    } catch {$icon.Dispose();throw}
}
