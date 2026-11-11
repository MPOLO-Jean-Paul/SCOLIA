# Windows PowerShell 5.1 : tests de processus isolés, sans registre ni classeur.
$ErrorActionPreference='Stop'
$testRoot=Join-Path ([IO.Path]::GetTempPath()) ('ITM-Fenetres-'+[guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($testRoot)
$source=Join-Path $testRoot 'Instances.cs'
$child=Join-Path $testRoot 'Enfant.ps1'
$token=[guid]::NewGuid().ToString('N')
$code=[IO.File]::ReadAllText((Join-Path $PSScriptRoot 'InstancesApplication.cs')).Replace('ITMApplicationInstances','ITMTestInstances').Replace('ITMCD','ITMCD-Test-'+$token)
[IO.File]::WriteAllText($source,$code,[Text.Encoding]::UTF8)
$childSource=@'
param([string]$Source,[string]$Result,[string]$Multiple)
$ErrorActionPreference='Stop'
Add-Type -Path $Source
try{[IO.File]::WriteAllText($Result,[string][ITMTestInstances]::Enter($Multiple -eq 'oui'))}finally{[ITMTestInstances]::Leave()}
'@
[IO.File]::WriteAllText($child,$childSource,[Text.Encoding]::UTF8)
function Invoke-ITMTestWindow([string]$multiple){
    $result=Join-Path $testRoot ([guid]::NewGuid().ToString('N')+'.txt')
    $ps=Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
    $args='-NoProfile -ExecutionPolicy Bypass -File "'+$child+'" -Source "'+$source+'" -Result "'+$result+'" -Multiple '+$multiple
    $process=Start-Process -FilePath $ps -ArgumentList $args -WindowStyle Hidden -PassThru
    try{if(-not $process.WaitForExit(15000)){ $process.Kill();throw 'Le test de fenêtre dépasse son délai.' }
        if($process.ExitCode -ne 0 -or -not [IO.File]::Exists($result)){throw 'Le processus de test a échoué.'}
        return [IO.File]::ReadAllText($result)
    }finally{$process.Dispose()}
}
try{
    Add-Type -Path $source
    if(-not [ITMTestInstances]::Enter($false)){throw 'Première fenêtre refusée.'}
    if((Invoke-ITMTestWindow 'non') -ne 'False'){throw 'Le mode unique laisse entrer une deuxième fenêtre.'}
    if((Invoke-ITMTestWindow 'oui') -ne 'True'){throw 'Le mode multiple refuse une deuxième fenêtre.'}
    [ITMTestInstances]::Leave()
    if((Invoke-ITMTestWindow 'non') -ne 'True'){throw 'La fermeture ne libère pas la fenêtre unique.'}
    Write-Output 'PASS : fenêtre unique, plusieurs fenêtres et libération à la fermeture.'
}finally{
    if('ITMTestInstances' -as [type]){[ITMTestInstances]::Leave()}
    if([IO.Directory]::Exists($testRoot)){[IO.Directory]::Delete($testRoot,$true)}
    $sessions=Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) ('ITMCD-Test-'+$token)
    if([IO.Directory]::Exists($sessions)){[IO.Directory]::Delete($sessions,$true)}
}
