if(-not (Test-Path function:Import-ITMCompiledSource)){. (Join-Path $PSScriptRoot 'CompilationCache.ps1')}
Import-ITMCompiledSource -Path (Join-Path $PSScriptRoot 'WindowsShortcuts.cs')
try{[ITMWindowsShortcuts]::IdentifyProcess()}catch{$script:shortcutIdentityWarning=$_.Exception.Message}
function New-ITMWindowsShortcut([string]$path) {
    $scriptPath=Join-Path $PSScriptRoot 'Lancer.ps1';$iconPath=$script:windowIconPath;if(-not $iconPath){$iconPath=Join-Path $PSScriptRoot 'Assets/SCOLIA.ico'}
    $powershell=Join-Path $env:SystemRoot 'System32/WindowsPowerShell/v1.0/powershell.exe'
    foreach($file in @($scriptPath,$iconPath,$powershell)){if(-not (Test-Path -LiteralPath $file -PathType Leaf)){throw ('Fichier du raccourci manquant : '+$file)}}
    $shell=$null;$link=$null
    try{
        $shell=New-Object -ComObject WScript.Shell;$link=$shell.CreateShortcut($path)
        $logs=Join-Path ([IO.Path]::GetDirectoryName($PSScriptRoot)) 'journaux';[void][IO.Directory]::CreateDirectory($logs)
        $journal=Join-Path $logs 'ITM_CD_Raccourci.txt'
        $link.TargetPath=$powershell
        $link.Arguments='-NoLogo -NoProfile -STA -ExecutionPolicy Bypass -WindowStyle Hidden -File "'+$scriptPath+'" -Mode Application -Journal "'+$journal+'"'
        $link.WorkingDirectory=[IO.Path]::GetDirectoryName($PSScriptRoot);$link.IconLocation=$iconPath+',0';$link.WindowStyle=7
        $link.Description='SCOLIA · Gestion scolaire et comptable';$link.Save()
        [ITMWindowsShortcuts]::IdentifyShortcut($path)
    }finally{
        if($link){[void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($link)}
        if($shell){[void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($shell)}
    }
}
function Create-ITMDesktopShortcut {
    if($script:busy){return}
    $desktop=[Environment]::GetFolderPath('DesktopDirectory');$programs=[Environment]::GetFolderPath('Programs')
    if(-not $desktop -or -not $programs){throw 'Windows ne fournit pas le dossier du Bureau ou du menu Démarrer.'}
    $shortcut=Join-Path $desktop 'SCOLIA - Secrétariat.lnk'
    New-ITMWindowsShortcut $shortcut
    $start=Join-Path $programs 'SCOLIA - Secrétariat.lnk'
    try{
        New-ITMWindowsShortcut $start
    }catch{
        $script:status.Text='Raccourci créé sur le Bureau. Le menu Démarrer n''a pas pu être mis à jour.'
        [void][ITMMessageBox]::Show($script:main,'Le raccourci du Bureau est créé. Le raccourci du menu Démarrer a échoué : '+$_.Exception.Message,'SCOLIA','OK','Warning');return
    }
    $script:status.Text='Raccourcis SCOLIA créés sur le Bureau et dans le menu Démarrer.'
    [void][ITMMessageBox]::Show($script:main,'Raccourcis SCOLIA créés avec le logo SCOLIA.'+"`r`n`r`n"+'Pour la barre des tâches : clic droit sur le raccourci SCOLIA, puis Épingler à la barre des tâches. Vous pouvez aussi chercher SCOLIA dans le menu Démarrer.'+"`r`n`r`n"+'Gardez ce dossier du programme à son emplacement. Si vous le déplacez, recréez les raccourcis depuis la nouvelle copie.','SCOLIA','OK','Information')
}

function Update-ITMInstitutionShortcutIcons {
    if(-not $script:windowIconPath -or -not [IO.File]::Exists($script:windowIconPath)){return}
    $paths=New-Object 'System.Collections.Generic.List[string]'
    foreach($folder in @([Environment]::GetFolderPath('DesktopDirectory'),[Environment]::GetFolderPath('Programs'))){if($folder){$path=Join-Path $folder 'SCOLIA - Secrétariat.lnk';if([IO.File]::Exists($path)){$paths.Add($path)}}}
    $pinned=Join-Path ([Environment]::GetFolderPath('ApplicationData')) 'Microsoft/Internet Explorer/Quick Launch/User Pinned/TaskBar'
    if([IO.Directory]::Exists($pinned)){foreach($file in @(Get-ChildItem -LiteralPath $pinned -Filter '*.lnk' -File)){$paths.Add($file.FullName)}}
    $shell=$null;$link=$null
    try{
        $shell=New-Object -ComObject WScript.Shell
        $launcher=Join-Path $PSScriptRoot 'Lancer.ps1';$vbs=[IO.Path]::GetFullPath((Join-Path ([IO.Path]::GetDirectoryName($PSScriptRoot)) '00_Demarrage/APPLICATION.vbs'))
        foreach($path in @($paths | Select-Object -Unique)){
            try{
                $link=$shell.CreateShortcut($path)
                # Change only shortcuts pointing to this installation, never another school's copy.
                if(([string]$link.Arguments).IndexOf($launcher,[StringComparison]::OrdinalIgnoreCase) -ge 0 -or [string]$link.TargetPath -ieq $vbs){
                    $link.IconLocation=$script:windowIconPath+',0';$link.Save();[ITMWindowsShortcuts]::NotifyIconChange($path)
                }
            }catch{Write-Verbose ('Icône du raccourci non actualisée : '+$_.Exception.Message)}finally{if($link){[void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($link);$link=$null}}
        }
    }catch{Write-Verbose ('Actualisation des raccourcis indisponible : '+$_.Exception.Message)}finally{if($shell){[void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($shell)}}
}
