# Tests PowerShell isolés des sauvegardes V56 : aucun Excel ni données de production.
$ErrorActionPreference='Stop'
# Use the production dispatcher against the fake workbooks in these isolated tests.
$transportTokens=$null;$transportErrors=$null
$transportAst=[Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot 'PasserelleExcel.ps1'),[ref]$transportTokens,[ref]$transportErrors)
$transportFn=$transportAst.Find({param($n)$n -is [Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -eq 'Invoke-ITMExcelMethod'},$true)
. ([scriptblock]::Create($transportFn.Extent.Text))

function Import-BackupV56Function([string]$name){
    $tokens=$null;$errors=$null
    $ast=[Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot 'StockageClasseurs.ps1'),[ref]$tokens,[ref]$errors)
    if($errors.Count){throw 'Syntaxe StockageClasseurs.ps1 invalide.'}
    $node=$ast.Find({param($n)$n -is [Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -eq $name},$true)
    if(-not $node){throw ('Fonction de sauvegarde absente : '+$name)}
    . ([scriptblock]::Create('function global:'+$name+$node.Extent.Text.Substring(('function '+$name).Length)))
}
foreach($name in @('Write-ITMAtomicJson','Get-ITMStoreFiles','Get-ITMPEBackupFiles','Get-ITMV55BackupHash','Get-ITMV55BackupContextId','Assert-ITMBackupManifest','Assert-ITMV55BackupDocument','Get-ITMV55BackupFiles','Backup-ITMStore','Restore-ITMStore')){Import-BackupV56Function $name}
function Assert-BackupV56([bool]$condition,[string]$message){if(-not $condition){throw ('Sauvegardes V56 : '+$message)}}
function Assert-ITMPermission {}
function Get-BackupV56RestoreCount {return @(Get-ChildItem -LiteralPath (Join-Path $script:storeRoot 'data') -Directory -ErrorAction SilentlyContinue).Count}
function Assert-BackupV56Rejected([scriptblock]$action,[string]$message){
    $before=Get-BackupV56RestoreCount;$rejected=$false
    try{[void](& $action)}catch{$rejected=$true}
    Assert-BackupV56 $rejected $message
    Assert-BackupV56 ((Get-BackupV56RestoreCount) -eq $before) 'Une restauration rejetée a créé un dossier data.'
}
function Copy-BackupV56Fixture([string]$source,[string]$name){
    $destination=Join-Path (Join-Path $script:storeFolder 'sauvegardes') $name
    [void][IO.Directory]::CreateDirectory($destination)
    foreach($file in Get-ChildItem -LiteralPath $source -File){[IO.File]::Copy($file.FullName,(Join-Path $destination $file.Name),$false)}
    return $destination
}
function Set-BackupV56Hash([string]$folder,[string]$name){
    $path=Join-Path $folder 'empreintes.json';$manifest=Get-Content -LiteralPath $path -Raw -Encoding UTF8 | ConvertFrom-Json
    $manifest | Add-Member NoteProperty $name (Get-FileHash -LiteralPath (Join-Path $folder $name) -Algorithm SHA256).Hash -Force
    Write-ITMAtomicJson $path $manifest
}
$tempRoot=Join-Path ([IO.Path]::GetTempPath()) ('scolia-tests-sauvegardes-v56-'+[guid]::NewGuid().ToString('N'))
try{
    $script:storeRoot=$tempRoot;$script:storeFolder=Join-Path $tempRoot 'origine';[void][IO.Directory]::CreateDirectory($script:storeFolder)
    $script:scolia=[pscustomobject]@{Id='ecole-test-v56'}
    $script:store=[pscustomobject]@{Version=1;Id=[guid]::NewGuid().ToString('N');Active='classeur-000001.xlsm';Segments=@('classeur-000001.xlsm');LearnerLimit=100;PaymentLimit=100;Closed=$false}
    $workbook=Join-Path $script:storeFolder 'classeur-000001.xlsm';[IO.File]::WriteAllText($workbook,'classeur factice, aucun accès Excel')
    $book=[pscustomobject]@{Name='classeur-000001.xlsm';FullName=$workbook}
    $book | Add-Member ScriptMethod SaveCopyAs {param($path)[IO.File]::Copy($this.FullName,$path,$false)}
    $script:storeBooks=@($book)
    $contextId=Get-ITMV55BackupContextId $script:store;$featureNames=@()
    foreach($spec in @(@('presences','2026-2027'),@('reinscriptions','2025-2026'),@('personnel','2026-2027'),@('salaires','2026-2027'),@('suiviapprenants','2026-2027'))){
        $key=Get-ITMV55BackupHash ($contextId+'|'+$spec[1]);$name='scolia-v55-'+$spec[0]+'-'+$key+'.json';$featureNames+=$name
        # Les sauvegardes conservent les enveloppes et instantanés, sans recalcul métier.
        $calendar=[pscustomobject]@{Month='2026-10';WorkingDates=@('2026-10-01','2026-10-02');ConfiguredBy='test';Revision=2}
        $signatures=@([pscustomobject]@{TeacherId='ens-01';Date='2026-10-01';Entry='2026-10-01T07:04:30+01:00';Exit='2026-10-01T16:00:05+01:00'})
        $items=@([pscustomobject]@{Marker='initial'})
        switch($spec[0]){
            'personnel'{$items=@([pscustomobject]@{Id='teacher:ens-01';Name='Enseignant test';Active=$true},[pscustomobject]@{Id='calendar:2026-10';Calendar=$calendar;Signatures=$signatures})}
            'salaires'{$items=@([pscustomobject]@{Id='salary:2026-10:ens-01';Month='2026-10';AmountFc=[decimal]300000;PayableFc=[decimal]150000;Snapshot=[pscustomobject]@{Teacher=[pscustomobject]@{Id='ens-01';Name='Enseignant test'};Calendar=$calendar;Signatures=$signatures;AbsenceDates=@('2026-10-02');Justifications=@();Formula=[ordered]@{WorkingDays=2;AbsenceDays=1;DailyFc=[decimal]150000}}})}
            'suiviapprenants'{$items=@([pscustomobject]@{Id='followup:2026-10:ACC001';Month='2026-10';Matricule='ACC001';AbsenceDates=@('2026-10-02');Thresholds=[ordered]@{TotalDays=3;ConsecutiveDays=2};Actions=@([pscustomobject]@{Id='action-01';CreatedAt='2026-10-02T14:01:05+01:00';Note='Suivi interne';Closed=$false})})}
        }
        $document=[pscustomobject]@{Schema=1;Context=[pscustomobject]@{Id=$contextId;Year=$spec[1];LocalKey=('a'*64)};Revision=3;Items=$items}
        Write-ITMAtomicJson (Join-Path $script:storeFolder $name) $document
        # Un JSON UTF-8 avec BOM est également recopié tel quel, sans normalisation.
        if($spec[0] -eq 'suiviapprenants'){$json=Get-Content -LiteralPath (Join-Path $script:storeFolder $name) -Raw -Encoding UTF8;[IO.File]::WriteAllText((Join-Path $script:storeFolder $name),$json,[Text.UTF8Encoding]::new($true))}
    }
    $newFeatureNames=@($featureNames|Where-Object {$_ -match '^scolia-v55-(personnel|salaires|suiviapprenants)-'})
    Assert-BackupV56 ($newFeatureNames.Count -eq 3) 'Les trois types de données V56 ne sont pas présents.'
    # Les fichiers de secours et temporaires ne sont pas des données à partager.
    [IO.File]::WriteAllText((Join-Path $script:storeFolder ($newFeatureNames[0]+'.precedent')),'ancienne version privée')
    [IO.File]::WriteAllText((Join-Path $script:storeFolder ($newFeatureNames[1]+'.temp.tmp')),'écriture temporaire')
    # Un brouillon d inscription ne doit jamais entrer dans une sauvegarde partagée.
    $draftName='scolia-v55-inscriptions-'+('b'*64)+'.json';[IO.File]::WriteAllText((Join-Path $script:storeFolder $draftName),'brouillon local privé')
    $originalHashes=@{};foreach($name in @('classeur-000001.xlsm')+$featureNames){$originalHashes[$name]=(Get-FileHash -LiteralPath (Join-Path $script:storeFolder $name) -Algorithm SHA256).Hash}
    $backup=Backup-ITMStore;Assert-BackupV56 $backup.ok 'Sauvegarde complète refusée.'
    $hashes=Get-Content -LiteralPath (Join-Path $backup.path 'empreintes.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    foreach($name in $featureNames){Assert-BackupV56 ([bool]$hashes.PSObject.Properties[$name]) 'Une donnée V56 manque aux empreintes.'}
    Assert-BackupV56 ([bool]$hashes.PSObject.Properties['registre.json']) 'Le registre ne possède pas d empreinte.'
    Assert-BackupV56 (-not [IO.File]::Exists((Join-Path $backup.path $draftName))) 'Un brouillon privé a été copié.'
    Assert-BackupV56 (@(Get-ChildItem -LiteralPath $backup.path -File | Where-Object {$_.Name -like '*.lock' -or $_.Name -like '*.precedent'}).Count -eq 0) 'Les verrous ou fichiers de secours ont été copiés.'
    $restored=Restore-ITMStore $backup.path;$restoredFolder=[IO.Path]::GetDirectoryName($restored)
    Assert-BackupV56 ($restoredFolder -cne $script:storeFolder) 'La restauration a remplacé l origine.'
    $restoredIndex=Get-Content -LiteralPath (Join-Path $restoredFolder 'registre.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    Assert-BackupV56 ($restoredIndex.Id -ceq $script:store.Id) 'L identité de la base restaurée a changé.'
    foreach($name in $featureNames){Assert-BackupV56 ((Get-FileHash -LiteralPath (Join-Path $restoredFolder $name) -Algorithm SHA256).Hash -eq $originalHashes[$name]) 'Une donnée V56 ou son contexte a été réécrit.'}
    $followupName=@($newFeatureNames|Where-Object {$_ -match '^scolia-v55-suiviapprenants-'})[0]
    $restoredBytes=[IO.File]::ReadAllBytes((Join-Path $restoredFolder $followupName))
    Assert-BackupV56 ($restoredBytes.Length -gt 3 -and $restoredBytes[0] -eq 239 -and $restoredBytes[1] -eq 187 -and $restoredBytes[2] -eq 191) 'Le BOM du JSON de suivi a été modifié.'
    $salaryName=@($newFeatureNames|Where-Object {$_ -match '^scolia-v55-salaires-'})[0]
    $salaryDocument=Get-Content -LiteralPath (Join-Path $restoredFolder $salaryName) -Raw -Encoding UTF8|ConvertFrom-Json
    Assert-BackupV56 ($salaryDocument.Items[0].Snapshot.Calendar.WorkingDates.Count -eq 2 -and $salaryDocument.Items[0].Snapshot.Signatures[0].Entry -ceq '2026-10-01T07:04:30+01:00' -and $salaryDocument.Items[0].Snapshot.Formula.WorkingDays -eq 2) 'Un instantané de salaire, calendrier ou pointage a été tronqué.'
    # Une sauvegarde V55 limitée à présences/réinscriptions reste restaurable.
    $v55=Copy-BackupV56Fixture $backup.path 'ancienne-v55'
    $v55Hashes=Get-Content -LiteralPath (Join-Path $v55 'empreintes.json') -Raw -Encoding UTF8|ConvertFrom-Json
    foreach($name in $newFeatureNames){[IO.File]::Delete((Join-Path $v55 $name));$v55Hashes.PSObject.Properties.Remove($name)}
    Write-ITMAtomicJson (Join-Path $v55 'empreintes.json') $v55Hashes
    Assert-BackupV56 ([IO.File]::Exists((Restore-ITMStore $v55))) 'Une sauvegarde V55 valide a été refusée.'
    # Les sauvegardes des versions précédentes ne possèdent ni JSON V55 ni empreinte du registre.
    $legacy=Join-Path (Join-Path $script:storeFolder 'sauvegardes') 'ancienne';[void][IO.Directory]::CreateDirectory($legacy)
    [IO.File]::Copy($workbook,(Join-Path $legacy 'classeur-000001.xlsm'),$false)
    Write-ITMAtomicJson (Join-Path $legacy 'registre.json') $script:store
    Write-ITMAtomicJson (Join-Path $legacy 'empreintes.json') @{'classeur-000001.xlsm'=$originalHashes['classeur-000001.xlsm']}
    Assert-BackupV56 ([IO.File]::Exists((Restore-ITMStore $legacy))) 'Une ancienne sauvegarde valide a été refusée.'
    $tampered=Copy-BackupV56Fixture $backup.path 'empreinte-modifiee'
    [IO.File]::AppendAllText((Join-Path $tampered $featureNames[0]),' ')
    Assert-BackupV56Rejected {Restore-ITMStore $tampered} 'Une donnée V56 modifiée a été restaurée.'
    $invalid=Copy-BackupV56Fixture $backup.path 'json-corrompu'
    [IO.File]::WriteAllText((Join-Path $invalid $featureNames[0]),'{incomplet')
    Set-BackupV56Hash $invalid $featureNames[0]
    Assert-BackupV56Rejected {Restore-ITMStore $invalid} 'Un JSON corrompu avec une empreinte valide a été restauré.'
    foreach($case in @('schema','revision','annee','items')){
        $badStructure=Copy-BackupV56Fixture $backup.path ('structure-'+$case)
        $badDocument=Get-Content -LiteralPath (Join-Path $badStructure $featureNames[0]) -Raw -Encoding UTF8 | ConvertFrom-Json
        switch($case){'schema'{$badDocument.Schema=2};'revision'{$badDocument.Revision=-1};'annee'{$badDocument.Context.Year='2026-2028'};'items'{$badDocument.Items=[pscustomobject]@{Marker='non-liste'}}}
        Write-ITMAtomicJson (Join-Path $badStructure $featureNames[0]) $badDocument;Set-BackupV56Hash $badStructure $featureNames[0]
        Assert-BackupV56Rejected {Restore-ITMStore $badStructure} ('Structure V56 incohérente acceptée : '+$case)
    }
    $changedRegister=Copy-BackupV56Fixture $backup.path 'registre-modifie'
    $changedIndex=Get-Content -LiteralPath (Join-Path $changedRegister 'registre.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    $changedIndex.LearnerLimit=200;Write-ITMAtomicJson (Join-Path $changedRegister 'registre.json') $changedIndex
    Assert-BackupV56Rejected {Restore-ITMStore $changedRegister} 'Un registre modifié sans nouvelle empreinte a été accepté.'
    $foreign=Copy-BackupV56Fixture $backup.path 'autre-contexte'
    $foreignDocument=Get-Content -LiteralPath (Join-Path $foreign $featureNames[0]) -Raw -Encoding UTF8 | ConvertFrom-Json
    $foreignDocument.Context.Id='c'*64;Write-ITMAtomicJson (Join-Path $foreign $featureNames[0]) $foreignDocument;Set-BackupV56Hash $foreign $featureNames[0]
    Assert-BackupV56Rejected {Restore-ITMStore $foreign} 'Un JSON d une autre base a été restauré.'
    $wrongName=Copy-BackupV56Fixture $backup.path 'nom-contexte-incoherent';$renamed='scolia-v55-presences-'+('d'*64)+'.json'
    [IO.File]::Move((Join-Path $wrongName $featureNames[0]),(Join-Path $wrongName $renamed))
    $wrongHashes=Get-Content -LiteralPath (Join-Path $wrongName 'empreintes.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    $wrongHashes.PSObject.Properties.Remove($featureNames[0]);$wrongHashes | Add-Member NoteProperty $renamed $originalHashes[$featureNames[0]]
    Write-ITMAtomicJson (Join-Path $wrongName 'empreintes.json') $wrongHashes
    Assert-BackupV56Rejected {Restore-ITMStore $wrongName} 'Un fichier dont le nom vise une autre année a été restauré.'
    $traversal=Copy-BackupV56Fixture $backup.path 'chemin-interdit'
    $malicious=Get-Content -LiteralPath (Join-Path $traversal 'empreintes.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    $malicious | Add-Member NoteProperty '../autre.json' ('e'*64);Write-ITMAtomicJson (Join-Path $traversal 'empreintes.json') $malicious
    Assert-BackupV56Rejected {Restore-ITMStore $traversal} 'Un chemin hors sauvegarde a été accepté.'
    $unlisted=Copy-BackupV56Fixture $legacy 'v56-non-referencee'
    [IO.File]::Copy((Join-Path $backup.path $featureNames[0]),(Join-Path $unlisted $featureNames[0]),$false)
    Assert-BackupV56Rejected {Restore-ITMStore $unlisted} 'Une donnée V56 non référencée a été acceptée.'
    $registeredDraft=Copy-BackupV56Fixture $backup.path 'brouillon-interdit'
    [IO.File]::WriteAllText((Join-Path $registeredDraft $draftName),'brouillon privé');Set-BackupV56Hash $registeredDraft $draftName
    Assert-BackupV56Rejected {Restore-ITMStore $registeredDraft} 'Un brouillon partagé dans le manifeste a été accepté.'
    foreach($name in $newFeatureNames){
        $type=[regex]::Match($name,'^scolia-v55-([a-z]+)-').Groups[1].Value
        $newUnlisted=Copy-BackupV56Fixture $v55 ('nouveau-non-reference-'+$type)
        [IO.File]::Copy((Join-Path $backup.path $name),(Join-Path $newUnlisted $name),$false)
        Assert-BackupV56Rejected {Restore-ITMStore $newUnlisted} ('Donnée V56 non référencée acceptée : '+$type)
        $newForeign=Copy-BackupV56Fixture $backup.path ('nouveau-autre-contexte-'+$type)
        $newDocument=Get-Content -LiteralPath (Join-Path $newForeign $name) -Raw -Encoding UTF8|ConvertFrom-Json
        $newDocument.Context.Id='f'*64;Write-ITMAtomicJson (Join-Path $newForeign $name) $newDocument;Set-BackupV56Hash $newForeign $name
        Assert-BackupV56Rejected {Restore-ITMStore $newForeign} ('Donnée V56 d une autre base acceptée : '+$type)
    }
    $unknownName='scolia-v55-inconnu-'+('a'*64)+'.json'
    $unknown=Copy-BackupV56Fixture $backup.path 'type-inconnu'
    [IO.File]::WriteAllText((Join-Path $unknown $unknownName),'{}');Set-BackupV56Hash $unknown $unknownName
    Assert-BackupV56Rejected {Restore-ITMStore $unknown} 'Un type de données non autorisé a été restauré.'
    $privateUnlisted=Copy-BackupV56Fixture $backup.path 'brouillon-non-reference'
    [IO.File]::WriteAllText((Join-Path $privateUnlisted $draftName),'brouillon privé')
    Assert-BackupV56Rejected {Restore-ITMStore $privateUnlisted} 'Un brouillon privé non référencé a été accepté.'
    $unknownSource=Join-Path $script:storeFolder $unknownName
    try{
        [IO.File]::WriteAllText($unknownSource,'{}')
        $beforeBackups=@(Get-ChildItem -LiteralPath (Join-Path $script:storeFolder 'sauvegardes') -Directory).Count
        Assert-BackupV56Rejected {Backup-ITMStore} 'Un type source non autorisé a été sauvegardé.'
        Assert-BackupV56 (@(Get-ChildItem -LiteralPath (Join-Path $script:storeFolder 'sauvegardes') -Directory).Count -eq $beforeBackups) 'Une source inconnue a laissé une sauvegarde incomplète.'
    }finally{if([IO.File]::Exists($unknownSource)){[IO.File]::Delete($unknownSource)}}
    $locked=Join-Path $script:storeFolder ($featureNames[0]+'.lock');$gate=[IO.File]::Open($locked,[IO.FileMode]::OpenOrCreate,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None)
    try{Assert-BackupV56Rejected {Backup-ITMStore} 'Une sauvegarde a ignoré un enregistrement en cours.'}finally{$gate.Dispose()}
    $sourceFeature=Join-Path $script:storeFolder $featureNames[0];$sourceBytes=[IO.File]::ReadAllBytes($sourceFeature)
    $backupCount=@(Get-ChildItem -LiteralPath (Join-Path $script:storeFolder 'sauvegardes') -Directory).Count
    try{
        [IO.File]::WriteAllText($sourceFeature,'{illisible')
        Assert-BackupV56Rejected {Backup-ITMStore} 'Une source V56 corrompue a produit une sauvegarde.'
        Assert-BackupV56 (@(Get-ChildItem -LiteralPath (Join-Path $script:storeFolder 'sauvegardes') -Directory).Count -eq $backupCount) 'Une sauvegarde rejetée a laissé un dossier incomplet.'
    }finally{[IO.File]::WriteAllBytes($sourceFeature,$sourceBytes)}
    foreach($name in $originalHashes.Keys){Assert-BackupV56 ((Get-FileHash -LiteralPath (Join-Path $script:storeFolder $name) -Algorithm SHA256).Hash -eq $originalHashes[$name]) 'Une opération a modifié les données de l origine.'}
    'PASS : sauvegarde/restauration V56, cinq stores, calendriers, pointages, instantanés salaires FC, suivi des apprenants, BOM préservé, V55 et anciennes sauvegardes, contexte, intégrité et brouillons exclus.'
}finally{if([IO.Directory]::Exists($tempRoot)){[IO.Directory]::Delete($tempRoot,$true)}}
