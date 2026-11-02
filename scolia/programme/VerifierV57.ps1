# Contrôles indépendants, sur fichiers temporaires et objets simulés.
param([int]$TimeoutSeconds=120)
$ErrorActionPreference='Stop'
if($PSVersionTable.PSEdition -ne 'Desktop' -or $PSVersionTable.PSVersion -lt [version]'5.1'){throw 'Utilisez Windows PowerShell 5.1.'}
$logRoot=Join-Path ([IO.Path]::GetDirectoryName($PSScriptRoot)) ('journaux/VerificationV57_'+(Get-Date -Format 'yyyyMMdd_HHmmss'))
[void][IO.Directory]::CreateDirectory($logRoot)
$engine=Join-Path $PSHOME 'powershell.exe'
$tests=@(Get-ChildItem -LiteralPath $PSScriptRoot -Filter 'Tests*.ps1' -File | Sort-Object Name | ForEach-Object {$_.Name})
if(-not $tests.Count){throw 'Aucun test SCOLIA trouvé dans le dossier programme.'}
if(@($tests | Group-Object | Where-Object {$_.Count -gt 1}).Count){throw 'La liste de tests contient des doublons.'}
Write-Host ('Couverture complète : '+$tests.Count+' test(s) détecté(s).')
$results=New-Object 'System.Collections.Generic.List[object]'
foreach($test in $tests){
    Write-Host ('Vérification : '+$test)
    $testPath=Join-Path $PSScriptRoot $test
    if(-not [IO.File]::Exists($testPath)){$results.Add([pscustomobject]@{Test=$test;Resultat='Échec : fichier de test absent'});continue}
    $start=New-Object Diagnostics.ProcessStartInfo
    $start.FileName=$engine;$start.Arguments='-NoLogo -NoProfile -NonInteractive -STA -ExecutionPolicy Bypass -File "'+$testPath+'"'
    $start.UseShellExecute=$false;$start.CreateNoWindow=$true;$start.RedirectStandardOutput=$true;$start.RedirectStandardError=$true
    $process=New-Object Diagnostics.Process;$process.StartInfo=$start
    try{
        [void]$process.Start();$stdout=$process.StandardOutput.ReadToEndAsync();$stderr=$process.StandardError.ReadToEndAsync()
        if(-not $process.WaitForExit($TimeoutSeconds*1000)){
            $process.Kill();$process.WaitForExit();$status='Délai dépassé'
        }else{$status=$(if($process.ExitCode -eq 0){'OK'}else{'Échec'})}
        [IO.File]::WriteAllText((Join-Path $logRoot ($test+'.txt')),($stdout.Result+[Environment]::NewLine+$stderr.Result),[Text.Encoding]::UTF8)
        $results.Add([pscustomobject]@{Test=$test;Resultat=$status})
    }catch{$results.Add([pscustomobject]@{Test=$test;Resultat=('Échec : '+$_.Exception.Message)})}finally{$process.Dispose()}
}
$results | Export-Csv -LiteralPath (Join-Path $logRoot 'RESULTATS.csv') -Delimiter ';' -NoTypeInformation -Encoding UTF8
$results | Format-Table -AutoSize
Write-Host ('Rapports : '+$logRoot)
if(@($results | Where-Object {$_.Resultat -ne 'OK'}).Count){exit 1}
exit 0
