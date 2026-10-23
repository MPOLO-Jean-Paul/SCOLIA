if(-not (Get-Command Write-ITMInstallJson -ErrorAction SilentlyContinue)){. (Join-Path $PSScriptRoot 'InstallationSession.ps1')}
# Contrôles non destructifs sur le suivi, le verrou et lʼidentification du processus Excel.
function Test-ITMInstallSession {
    $testRoot=Join-Path ([IO.Path]::GetTempPath()) ('ITM Suivi é '+[guid]::NewGuid().ToString('N'))
    [void][IO.Directory]::CreateDirectory($testRoot)
    try{
        $state=Join-Path $testRoot 'etat.json'
        Write-ITMInstallJson $state ([pscustomobject]@{Message='Étape 1';Step=25;Updated=1})
        Write-ITMInstallJson $state ([pscustomobject]@{Message='Étape 2';Step=80;Updated=2})
        $read=Read-ITMInstallJson $state
        if($read.Message -ne 'Étape 2' -or $read.Step -ne 80){throw 'Le suivi ne relit pas la dernière étape.'}
        $backup=Read-ITMInstallJson ($state+'.precedent')
        if(-not $backup -or $backup.Step -ne 25){throw 'Le remplacement ne conserve pas le précédent état valide.'}
        Write-ITMInstallJson $state ([pscustomobject]@{Message='Étape 3';Step=90;Updated=3})
        $read=Read-ITMInstallJson $state;$backup=Read-ITMInstallJson ($state+'.precedent')
        if($read.Step -ne 90 -or $backup.Step -ne 80){throw 'Les remplacements successifs du suivi ont échoué.'}
        [IO.File]::WriteAllText($state,'incomplet')
        if($null -ne (Read-ITMInstallJson $state)){throw 'Le suivi accepte un état JSON incomplet.'}
        $lockPath=Join-Path $testRoot 'installation.lock'
        $lock=[IO.File]::Open($lockPath,[IO.FileMode]::OpenOrCreate,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None)
        try{
            $blocked=$false;$other=$null
            try{$other=[IO.File]::Open($lockPath,[IO.FileMode]::OpenOrCreate,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None)}catch [IO.IOException]{$blocked=$true}
            finally{if($other){$other.Dispose()}}
            if(-not $blocked){throw 'Deux installations pourraient partager le verrou.'}
        }finally{$lock.Dispose()}
        $reopened=[IO.File]::Open($lockPath,[IO.FileMode]::OpenOrCreate,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None);$reopened.Dispose()
        $started=[DateTime]::UtcNow
        $process=[pscustomobject]@{Id=1234;ProcessName='EXCEL';StartTime=$started}
        $owner=[pscustomobject]@{ProcessId=1234;Started=$started.Ticks;Dedicated=$true}
        if(-not (Test-ITMInstallExcelOwner $owner $process)){throw 'Instance dédiée non reconnue.'}
        $owner.Dedicated=$false
        if(Test-ITMInstallExcelOwner $owner $process){throw 'Une instance déjà ouverte serait ciblée.'}
        $owner.Dedicated=$true;$owner.Started=$started.AddSeconds(-1).Ticks
        if(Test-ITMInstallExcelOwner $owner $process){throw 'Un numéro de processus réutilisé serait ciblé.'}
        $owner.Started=$started.Ticks;$process.ProcessName='powershell'
        if(Test-ITMInstallExcelOwner $owner $process){throw 'Un processus non Excel serait ciblé.'}
        $oldFolder=$script:installSessionFolder;$oldPending=$script:installLogPending
        try{
            $script:installSessionFolder=$testRoot;$script:installLogPending=''
            Write-ITMInstallLog 'Premier message'
            $journal=Join-Path $testRoot 'journal.txt'
            $reader=[IO.File]::Open($journal,[IO.FileMode]::Open,[IO.FileAccess]::Read,([IO.FileShare]::ReadWrite -bor [IO.FileShare]::Delete))
            try{Write-ITMInstallLog 'Écriture pendant la lecture'}finally{$reader.Dispose()}
            if((Read-ITMInstallLog) -notlike '*Écriture pendant la lecture*'){throw 'La lecture bloque encore l''écriture du journal.'}
            $locked=[IO.File]::Open($journal,[IO.FileMode]::Open,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None)
            try{Write-ITMInstallLog 'Message sous verrou exclusif'}finally{$locked.Dispose()}
            $text=Read-ITMInstallLog
            if($text -notlike '*Message sous verrou exclusif*'){throw 'Le message de secours est perdu.'}
            $archive=Join-Path $testRoot 'archive.txt';Copy-ITMInstallLog $archive
            if([IO.File]::ReadAllText($archive) -notlike '*Message sous verrou exclusif*'){throw 'Le journal exporté oublie les messages de secours.'}
            Write-ITMInstallLog 'Reprise après le verrou'
            if((Read-ITMInstallLog) -notlike '*Reprise après le verrou*'){throw 'Le journal ne reprend pas après déverrouillage.'}
        }finally{$script:installSessionFolder=$oldFolder;$script:installLogPending=$oldPending}
        return 'Suivi verifie : ecriture/lecture, fichier incomplet, verrou exclusif et ciblage de la seule instance Excel dediee. Aucun processus arrete par le test.'
    }finally{if([IO.Directory]::Exists($testRoot)){[IO.Directory]::Delete($testRoot,$true)}}
}
if($MyInvocation.InvocationName -ne '.') {Write-Output (Test-ITMInstallSession)}
