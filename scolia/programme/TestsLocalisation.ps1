if(-not ('Windows.Forms.OpenFileDialog' -as [type])){Add-Type -AssemblyName System.Windows.Forms}
# Vérifie la détection sur des fichiers fictifs temporaires, sans Excel ni registre Windows.
. (Join-Path $PSScriptRoot 'LocalisationClasseurs.ps1')
function Test-ITMWorkbookLocation {
    $testRoot=Join-Path ([IO.Path]::GetTempPath()) ('ITM_Localisation_'+[guid]::NewGuid().ToString('N'))
    try{
        [void][IO.Directory]::CreateDirectory($testRoot)
        $source=Join-Path $testRoot 'source.xlsm';[IO.File]::WriteAllText($source,'TEST')
        $first=Get-ITMWorkbookLocation -Root $testRoot -Connection $source -Source ''
        if($first.Path -ne $source -or -not $first.Installable){throw 'Détection du choix mémorisé incorrecte.'}
        $id='aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa';$folder=Join-Path (Join-Path $testRoot 'data') $id
        [void][IO.Directory]::CreateDirectory($folder)
        foreach($name in @('classeur-000001.xlsm','classeur-000002.xlsm')){[IO.File]::WriteAllText((Join-Path $folder $name),'TEST')}
        $index=@{Version=1;Id=$id;Active='classeur-000002.xlsm';Segments=@('classeur-000001.xlsm','classeur-000002.xlsm');Closed=$false}
        $index | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $folder 'registre.json') -Encoding UTF8
        $old=Join-Path $folder 'classeur-000001.xlsm'
        $active=Get-ITMWorkbookLocation -Root $testRoot -Connection $old -Source ''
        if($active.Path -ne (Join-Path $folder 'classeur-000002.xlsm') -or -not $active.Managed -or $active.Installable){throw 'Le segment actif ne suit pas la rotation.'}
        $movedOld=Join-Path (Join-Path (Join-Path $testRoot 'ancien_dossier') $id) 'classeur-000001.xlsm'
        $moved=Get-ITMWorkbookLocation -Root $testRoot -Connection $movedOld -Source ''
        if($moved.Path -ne $active.Path){throw 'Base déplacée non retrouvée.'}
        $single=Get-ITMWorkbookLocation -Root $testRoot -Connection '' -Source ''
        if($single.Path -ne $active.Path){throw 'Base unique non détectée.'}
        $oldFolder=[IO.Path]::GetDirectoryName($movedOld)
        [void][IO.Directory]::CreateDirectory($oldFolder)
        $previous=@{Version=1;Id=$id;Active='classeur-000001.xlsm';Segments=@('classeur-000001.xlsm');Closed=$false}
        $previous | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $oldFolder 'registre.json') -Encoding UTF8
        [IO.File]::WriteAllText($movedOld,'TEST')
        $preferLocal=Get-ITMWorkbookLocation -Root $testRoot -Connection $movedOld -Source ''
        if($preferLocal.Path -ne $active.Path){throw 'La copie locale ne prend pas le relais du choix mémorisé.'}
        $explicitOld=Get-ITMWorkbookLocation -Root $testRoot -Explicit $movedOld -Connection '' -Source ''
        if($explicitOld.Path -ne $movedOld){throw 'Un choix explicite a été remplacé par une autre copie.'}
        $secondFile=Join-Path $folder 'classeur-000002.xlsm'
        [IO.File]::Delete($secondFile)
        $blocked=$false
        try{Test-ITMWorkbookSet $old}catch{$blocked=$true}
        if(-not $blocked){throw 'La vérification initiale accepte un segment manquant.'}
        [IO.File]::WriteAllText($secondFile,'TEST')
        @{Index=$index} | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $folder 'transaction.json') -Encoding UTF8
        [IO.File]::WriteAllText((Join-Path $folder 'registre.json'),'{illisible')
        $pending=Get-ITMWorkbookLocation -Root $testRoot -Connection '' -Source ''
        if(-not $pending.Managed -or $pending.Path -ne $active.Path){throw 'La détection ne retrouve pas la récupération en attente.'}
        Test-ITMWorkbookSet $old
        [IO.File]::Delete((Join-Path $folder 'registre.json'))
        if(-not (Read-ITMLocationIndex $folder)){throw 'Le registre absent empêche de détecter le journal de récupération.'}
        [IO.File]::Delete((Join-Path $folder 'transaction.json'))
        $index | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $folder 'registre.json') -Encoding UTF8
        $id2='bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb';$folder2=Join-Path (Join-Path $testRoot 'data') $id2
        [void][IO.Directory]::CreateDirectory($folder2)
        $index.Id=$id2;$index | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $folder2 'registre.json') -Encoding UTF8
        $ambiguous=Get-ITMWorkbookLocation -Root $testRoot -Connection '' -Source ''
        if($ambiguous.Path -or -not $ambiguous.Ambiguous){throw 'Plusieurs bases choisies sans instruction.'}
        $missing=Join-Path $testRoot 'absent.xlsm'
        $unavailable=Get-ITMWorkbookLocation -Purpose Installation -Root $testRoot -Connection $missing -Source $source
        if($unavailable.Path -ne $missing -or $unavailable.Exists){throw 'Une copie ancienne remplace un choix indisponible.'}
        Remove-Item -LiteralPath (Join-Path $testRoot 'data') -Recurse -Force
        $installFolder=Join-Path (Join-Path (Join-Path $testRoot 'data') 'installations') 'Installation_ITM_20261007_100000_001'
        [void][IO.Directory]::CreateDirectory($installFolder)
        $ready=Join-Path $installFolder 'SYSTEME_SECRETARIAT_CORRIGE.xlsm';[IO.File]::WriteAllText($ready,'TEST')
        $incomplete=Get-ITMWorkbookLocation -Root $testRoot -Connection '' -Source ''
        if($incomplete.Path){throw 'Installation incomplète choisie.'}
        [IO.File]::WriteAllText((Join-Path $installFolder 'RESULTAT_INSTALLATION.txt'),'TEST')
        $complete=Get-ITMWorkbookLocation -Root $testRoot -Connection '' -Source ''
        if($complete.Path -ne $ready){throw 'Installation réussie non détectée.'}
        $dialog=New-Object Windows.Forms.OpenFileDialog
        try{
            Set-ITMWorkbookDialogLocation $dialog $complete
            if($dialog.InitialDirectory -ne $installFolder -or $dialog.FileName -ne 'SYSTEME_SECRETARIAT_CORRIGE.xlsm'){throw 'Le sélecteur ne cible pas le dossier détecté.'}
        }finally{$dialog.Dispose()}
        return 'Détection : choix mémorisé, rotation, déplacement, base unique, ambiguïté, support absent, installation complète et dossier du sélecteur vérifiés.'
    }finally{if([IO.Directory]::Exists($testRoot)){Remove-Item -LiteralPath $testRoot -Recurse -Force}}
}
if($MyInvocation.InvocationName -ne '.') {Write-Output (Test-ITMWorkbookLocation)}
