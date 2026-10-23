# Contrôles indépendants, sur fichiers temporaires et objets simulés.
param([int]$TimeoutSeconds=120)
$ErrorActionPreference='Stop'
if($PSVersionTable.PSEdition -ne 'Desktop' -or $PSVersionTable.PSVersion -lt [version]'5.1'){throw 'Utilisez Windows PowerShell 5.1.'}
$logRoot=Join-Path ([IO.Path]::GetDirectoryName($PSScriptRoot)) ('journaux/VerificationV54_'+(Get-Date -Format 'yyyyMMdd_HHmmss'))
[void][IO.Directory]::CreateDirectory($logRoot)
$engine=Join-Path $PSHOME 'powershell.exe'
$tests=@('TestsConnexionDesignV54.ps1','TestsIndicateursV40.ps1','TestsCompilation.ps1','TestsInscriptionPageUnique.ps1','TestsInscriptionDesignV53.ps1','TestsNuancesSombres.ps1','TestsImagesTheme.ps1','TestsConfirmationInscription.ps1','TestsResponsiveContenus.ps1','TestsSoldesEtRechercheV52.ps1','TestsTransitionRuntimeV52.ps1','TestsTransitionsV51.ps1','TestsActiviteFiabilite.ps1','TestsTransactionApprenant.ps1','TestsDoublonsIdentite.ps1','TestsScoliaConfiguration.ps1','TestsScoliaLedger.ps1','TestsScoliaReception.ps1','TestsPrisesEnCharge.ps1','TestsMemoireConnexion.ps1','TestsRevue.ps1','TestsRevueFonctionnelle.ps1','TestsReactivitePages.ps1','TestsRetardsCalendrier.ps1','TestsConnexionObjet.ps1')
$results=New-Object 'System.Collections.Generic.List[object]'
foreach($test in $tests){
    Write-Host ('Vérification : '+$test)
    $start=New-Object Diagnostics.ProcessStartInfo
    $start.FileName=$engine;$start.Arguments='-NoLogo -NoProfile -NonInteractive -STA -ExecutionPolicy Bypass -File "'+(Join-Path $PSScriptRoot $test)+'"'
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
