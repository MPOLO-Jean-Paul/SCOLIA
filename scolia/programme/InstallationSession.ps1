# Fenêtre et moteur Excel séparés ; suivi écrit après chaque étape, uniquement sur des copies.
function Write-ITMInstallJson([string]$path,$value) {
    $temp=$path+'.'+[guid]::NewGuid().ToString('N')+'.tmp'
    try{
        [IO.File]::WriteAllText($temp,($value | ConvertTo-Json -Depth 8),[Text.UTF8Encoding]::new($true))
        if([IO.File]::Exists($path)){
            # PowerShell 5.1 peut convertir $null en chaîne vide pour un argument string.
            # Un vrai chemin de sauvegarde évite l'erreur « forme du chemin ».
            $previous=$path+'.precedent'
            try{[IO.File]::Replace($temp,$path,$previous,$true)}catch [IO.IOException]{Move-Item -LiteralPath $temp -Destination $path -Force -ErrorAction Stop}
        }else{[IO.File]::Move($temp,$path)}
    }finally{if([IO.File]::Exists($temp)){[IO.File]::Delete($temp)}}
}
function Read-ITMInstallJson([string]$path) {
    try{return Get-Content -LiteralPath $path -Raw -Encoding UTF8 -ErrorAction Stop | ConvertFrom-Json}catch{return $null}
}
function Initialize-ITMInstallWorker([string]$sessionFile) {
    $script:installSessionFolder=[IO.Path]::GetDirectoryName($sessionFile)
    $script:installRequest=Read-ITMInstallJson $sessionFile
    if(-not $script:installRequest){throw 'Le dossier de suivi de cette installation est illisible.'}
    $script:extendedChecks=[bool]$script:installRequest.Extended
    $script:installStep=0;$script:installLogPending=''
    Assert-ITMInstallParent
}
function Assert-ITMInstallParent {
    $owner=Get-Process -Id ([int]$script:installRequest.ParentId) -ErrorAction SilentlyContinue
    if(-not $owner -or $owner.StartTime.ToUniversalTime().Ticks -ne [long]$script:installRequest.ParentStarted){throw 'La fenêtre de suivi a été fermée. Installation interrompue avant publication.'}
}
# Un seul écrivain à la fois, mais les lecteurs peuvent rester ouverts.
# La journalisation est secondaire : un verrou externe ne doit pas arrêter Excel.
function Write-ITMInstallLog([string]$message) {
    $line=(Get-Date -Format 'HH:mm:ss')+'  '+$message+"`r`n"
    $journal=Join-Path $script:installSessionFolder 'journal.txt'
    $bytes=[Text.UTF8Encoding]::new($false).GetBytes($line)
    for($attempt=0;$attempt -lt 4;$attempt++){
        $stream=$null
        try{
            $stream=[IO.File]::Open($journal,[IO.FileMode]::Append,[IO.FileAccess]::Write,([IO.FileShare]::Read -bor [IO.FileShare]::Delete))
            $stream.Write($bytes,0,$bytes.Length)
            return
        }catch [IO.IOException]{
            if($attempt -lt 3){Start-Sleep -Milliseconds (25*($attempt+1))}
        }catch{break}
        finally{if($stream){$stream.Dispose()}}
    }
    # Un fichier indépendant conserve le message même si journal.txt est verrouillé.
    try{
        $part=Join-Path $script:installSessionFolder ('journal-secours-'+[DateTime]::UtcNow.Ticks+'-'+[guid]::NewGuid().ToString('N')+'.txt')
        [IO.File]::WriteAllText($part,$line,[Text.UTF8Encoding]::new($false))
    }catch{
        # Si tout le dossier est inaccessible, conserver le diagnostic dans ce processus.
        $script:installLogPending+=$line
    }
}
function Read-ITMInstallLogFile([string]$path) {
    $stream=$null;$reader=$null
    try{
        $stream=[IO.File]::Open($path,[IO.FileMode]::Open,[IO.FileAccess]::Read,([IO.FileShare]::ReadWrite -bor [IO.FileShare]::Delete))
        $reader=[IO.StreamReader]::new($stream,[Text.Encoding]::UTF8,$true)
        return $reader.ReadToEnd()
    }catch{return $null}
    finally{if($reader){$reader.Dispose()}elseif($stream){$stream.Dispose()}}
}
function Read-ITMInstallLog {
    $journal=Join-Path $script:installSessionFolder 'journal.txt'
    $text=''
    if([IO.File]::Exists($journal)){
        $text=Read-ITMInstallLogFile $journal
        if($null -eq $text){return $null}
    }
    # Les lignes gardent leur heure ; les messages de secours restent visibles et exportés.
    $parts=@(Get-ChildItem -LiteralPath $script:installSessionFolder -Filter 'journal-secours-*.txt' -ErrorAction SilentlyContinue | Sort-Object Name)
    if($parts.Count){
        $text+="`r`n--- Messages conservés pendant un verrouillage du journal ---`r`n"
        foreach($part in $parts){$text+=Read-ITMInstallLogFile $part.FullName}
    }
    if($script:installLogPending){$text+=$script:installLogPending}
    return $text
}
function Copy-ITMInstallLog([string]$destination) {
    try{
        $text=Read-ITMInstallLog
        if($null -ne $text){[IO.File]::WriteAllText($destination,$text,[Text.UTF8Encoding]::new($true))}
    }catch{ } # L'archivage du journal ne modifie jamais le résultat de l'installation.
}
function Write-ITMInstallStatus([string]$message,[int]$step=-1,[string]$folder='') {
    Assert-ITMInstallParent
    if($step -ge 0){$script:installStep=[Math]::Min(100,$step)}
    Write-ITMInstallLog $message
    Write-ITMInstallJson (Join-Path $script:installSessionFolder 'etat.json') ([pscustomobject]@{
        Message=$message;Step=$script:installStep;Updated=[DateTime]::UtcNow.Ticks;Folder=$folder
    })
}
function Write-ITMInstallResult($result) {
    Write-ITMInstallJson (Join-Path $script:installSessionFolder 'resultat.json') $result
}
function Save-ITMInstallExcelOwner($excel,[int[]]$existing) {
    if(-not ('ITMInstallNative' -as [type])){
        Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class ITMInstallNative {
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
}
'@
    }
    $excelId=[uint32]0
    [void][ITMInstallNative]::GetWindowThreadProcessId([IntPtr]$excel.Hwnd,[ref]$excelId)
    $process=Get-Process -Id ([int]$excelId) -ErrorAction Stop
    Write-ITMInstallJson (Join-Path $script:installSessionFolder 'excel.json') ([pscustomobject]@{
        ProcessId=[int]$excelId;Started=$process.StartTime.ToUniversalTime().Ticks;Dedicated=([int]$excelId -notin $existing)
    })
    return ([int]$excelId -notin $existing)
}
function Set-ITMInstallBusy([bool]$busy) {
    $script:busy=$busy;$browse.Enabled=-not $busy;$extended.Enabled=-not $busy
    if($script:newSourceButton){$script:newSourceButton.Enabled=-not $busy}
    $installButton.Enabled=(-not $busy -and [bool]$script:installerLocation.Installable)
    $sourceFolder.Text=$(if($busy){'Arrêter'}else{'Dossier source'})
}
function Start-ITMInstallSession([string]$source,[bool]$extendedChecks,[bool]$newSource=$false) {
    $root=[IO.Path]::GetDirectoryName($PSScriptRoot)
    $installRoot=Join-Path (Join-Path $root 'data') 'installations'
    [void][IO.Directory]::CreateDirectory($installRoot)
    try{$script:installGate=[IO.File]::Open((Join-Path $installRoot 'installation.lock'),[IO.FileMode]::OpenOrCreate,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None)}
    catch [IO.IOException]{throw 'Une autre installation est déjà en cours dans ce dossier. Attendez sa fin avant de recommencer.'}
    catch{throw ('Le dossier data/installations ne permet pas de démarrer : '+$_.Exception.Message)}
    try{
        if(-not $newSource){$source=(Resolve-Path -LiteralPath $source -ErrorAction Stop).Path}else{$source=''}
        $sessionRoot=Join-Path (Join-Path $root 'journaux') 'installations'
        $script:installSessionFolder=Join-Path $sessionRoot ([guid]::NewGuid().ToString('N'))
        [void][IO.Directory]::CreateDirectory($script:installSessionFolder)
        $requestFile=Join-Path $script:installSessionFolder 'demande.json'
        $parent=Get-Process -Id $PID
        Write-ITMInstallJson $requestFile ([pscustomobject]@{Source=$source;Extended=$extendedChecks;NewSource=[bool]$newSource;ParentId=$PID;ParentStarted=$parent.StartTime.ToUniversalTime().Ticks})
        $script:installStarted=[DateTime]::UtcNow;$script:lastInstallUpdate=$script:installStarted
        $script:lastInstallState=$null;$script:installLogPending='';$script:installStopping=$false;$script:finalPath='';$script:diagnosticFolder=''
        $openButton.Text='Voir le fichier corrige'
        $log.Clear();$progress.Value=0;try{$progress.Start()}catch{};$openButton.Enabled=$false
        $status.Text='Démarrage du traitement Excel...'
        $ps=Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
        $arguments='-NoProfile -STA -ExecutionPolicy Bypass -WindowStyle Hidden -File "'+(Join-Path $PSScriptRoot 'Installer.ps1')+'" -Session "'+$requestFile+'"'
        $script:installProcess=Start-Process -FilePath $ps -ArgumentList $arguments -WindowStyle Hidden -PassThru -RedirectStandardError (Join-Path $script:installSessionFolder 'erreur-processus.txt') -RedirectStandardOutput (Join-Path $script:installSessionFolder 'sortie-processus.txt')
        Set-ITMInstallBusy $true
        $script:installTimer.Start()
    }catch{
        if($script:installGate){$script:installGate.Dispose();$script:installGate=$null}
        throw
    }
}
function Update-ITMInstallSession {
    if(-not $script:busy -or $script:installStopping){return}
    $state=Read-ITMInstallJson (Join-Path $script:installSessionFolder 'etat.json')
    if($state){
        $script:lastInstallState=$state
        $script:lastInstallUpdate=[DateTime]::new([long]$state.Updated,[DateTimeKind]::Utc)
        $progress.Value=[Math]::Min(100,[Math]::Max(0,[int]$state.Step))
    }
    $elapsed=[DateTime]::UtcNow-$script:installStarted
    $waiting=[DateTime]::UtcNow-$script:lastInstallUpdate
    $message=$(if($script:lastInstallState){[string]$script:lastInstallState.Message}else{'Démarrage du traitement Excel...'})
    $status.Text=$message+"`r`nTemps écoulé : "+[int]$elapsed.TotalMinutes+' min '+$elapsed.Seconds+' s'+$(if($waiting.TotalSeconds -ge 30){' | Excel travaille : arrêt disponible.'}else{''})
    $text=Read-ITMInstallLog
    if($null -ne $text -and $log.Text -ne $text){$log.Text=$text;$log.SelectionStart=$log.Text.Length;$log.ScrollToCaret()}
    $script:installProcess.Refresh()
    if($script:installProcess.HasExited){
        $result=Read-ITMInstallJson (Join-Path $script:installSessionFolder 'resultat.json')
        if(-not $result){
            $errorFile=Join-Path $script:installSessionFolder 'erreur-processus.txt'
            $detail=$(if([IO.File]::Exists($errorFile)){[IO.File]::ReadAllText($errorFile)}else{''})
            $result=[pscustomobject]@{Success=$false;Final='';Folder='';Error=('Le traitement a quitté sans résultat. '+$detail)}
        }
        Complete-ITMInstallSession $result
        return
    }
    # Un appel COM peut rester suspendu ; le minuteur de la fenêtre continue de tourner.
    if($waiting.TotalMinutes -ge 15 -or $elapsed.TotalMinutes -ge 60){Stop-ITMInstallSession 'Délai dépassé : aucune nouvelle étape depuis 15 minutes ou durée totale de 60 minutes.'}
}
function Test-ITMInstallExcelOwner($owner,$process) {
    return [bool]($owner -and $owner.Dedicated -and $process -and $process.Id -eq [int]$owner.ProcessId -and $process.ProcessName -ieq 'EXCEL' -and $process.StartTime.ToUniversalTime().Ticks -eq [long]$owner.Started)
}
function Stop-ITMInstallExcel {
    $owner=Read-ITMInstallJson (Join-Path $script:installSessionFolder 'excel.json')
    if(-not $owner -or -not $owner.Dedicated){return}
    $process=Get-Process -Id ([int]$owner.ProcessId) -ErrorAction SilentlyContinue
    if(Test-ITMInstallExcelOwner $owner $process){
        try{$process.Kill()}catch{Write-ITMInstallLog ('Fermeture de l''instance Excel dédiée impossible : '+$_.Exception.Message)}
    }
}
function Stop-ITMInstallSession([string]$reason) {
    if(-not $script:busy -or $script:installStopping){return}
    $script:installStopping=$true;$script:installTimer.Stop()
    try{
        $script:installProcess.Refresh()
        if(-not $script:installProcess.HasExited){$script:installProcess.Kill();if(-not $script:installProcess.WaitForExit(2000)){throw 'Le traitement ne confirme pas encore son arrêt.'}}
        Stop-ITMInstallExcel
        $folder=$(if($script:lastInstallState){[string]$script:lastInstallState.Folder}else{''})
        $final=$(if($folder){Join-Path $folder 'SYSTEME_SECRETARIAT_CORRIGE.xlsm'}else{''})
        # Une interruption juste après publication ne transforme pas un résultat complet en échec.
        if($folder -and [IO.File]::Exists($final) -and [IO.File]::Exists((Join-Path $folder 'RESULTAT_INSTALLATION.txt'))){
            $result=[pscustomobject]@{Success=$true;Final=$final;Folder=$folder;Error=''}
            [void](Set-ITMLocationPreference 'Classeur' $final)
            [void](Set-ITMLocationPreference 'ConnexionAutomatique' 'oui')
        }else{
            Write-ITMInstallLog ($reason+' Le fichier source et les sauvegardes sont conservés.')
            $result=[pscustomobject]@{Success=$false;Final='';Folder=$folder;Error=$reason}
            if($folder -and [IO.Directory]::Exists($folder)){
                [IO.File]::WriteAllText((Join-Path $folder 'ERREUR_INSTALLATION.txt'),$reason,[Text.Encoding]::UTF8)
                Copy-ITMInstallLog (Join-Path $folder 'JOURNAL_INSTALLATION.txt')
            }
        }
        Write-ITMInstallResult $result
        Complete-ITMInstallSession $result
    }catch{
        $stopError=$_.Exception.Message
        $script:installProcess.Refresh()
        if(-not $script:installProcess.HasExited){
            $script:installStopping=$false;$script:installTimer.Start()
            [void][ITMMessageBox]::Show($form,$stopError,'Arrêt en attente','OK','Information')
        }else{
            $result=[pscustomobject]@{Success=$false;Final='';Folder='';Error=($reason+' '+$stopError)}
            Complete-ITMInstallSession $result
        }
    }
}
function Complete-ITMInstallSession($result) {
    $script:installTimer.Stop();try{$progress.Stop()}catch{}
    Stop-ITMInstallExcel
    if($script:installGate){$script:installGate.Dispose();$script:installGate=$null}
    if($script:installProcess){$script:installProcess.Dispose();$script:installProcess=$null}
    Set-ITMInstallBusy $false
    if($result.Success -and [IO.File]::Exists([string]$result.Final)){
        $script:finalPath=[string]$result.Final;$openButton.Text='Voir le fichier corrige';$openButton.Enabled=$true;$progress.Value=100
        $status.Text='Installation réussie. Passage à l étape suivante...'
        if($Onboarding){
            # Afficher l'étape suivante avant de fermer l'installateur : aucun écran vide.
            if(Test-Path function:Show-ScoliaTransition){Show-ScoliaTransition 'Installation terminée. Préparation de l étape suivante...'}
            # Nous sommes déjà sur le thread UI du minuteur de suivi : fermer ici est sûr
            # et évite un second Timer capturé dans une fermeture PowerShell. Dans V49,
            # ce Timer local devenait $null au moment du Tick et déclenchait :
            # « Impossible d'appeler une méthode dans une expression Null. »
            # La fermeture immédiate supprime aussi la cassure visuelle entre l'installation
            # et l'étape suivante du parcours unifié.
            if($form -and -not $form.IsDisposed){
                $form.DialogResult=[Windows.Forms.DialogResult]::OK
                $form.Close()
            }
        }else{
            [void][ITMMessageBox]::Show($form,"Installation réussie.`r`n`r`n"+$script:finalPath+"`r`n`r`nVotre fichier source et sa sauvegarde sont conservés.",'Installation terminée','OK','Information')
        }
    }else{
        $script:diagnosticFolder=$script:installSessionFolder;$openButton.Text='Voir le journal';$openButton.Enabled=$true
        $status.Text='Installation interrompue. Votre fichier source reste intact.'
        [void][ITMErrorDialog]::Show($form,([string]$result.Error+"`r`n`r`nJournal : "+$script:installSessionFolder),'Installation interrompue')
    }
}
