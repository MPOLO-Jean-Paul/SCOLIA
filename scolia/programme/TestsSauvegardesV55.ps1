# Tests PowerShell isolés des sauvegardes V55 : aucun Excel ni données de production.
$ErrorActionPreference='Stop'
# Use the production dispatcher against the fake workbooks in these isolated tests.
$transportTokens=$null;$transportErrors=$null
$transportAst=[Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot 'PasserelleExcel.ps1'),[ref]$transportTokens,[ref]$transportErrors)
$transportFn=$transportAst.Find({param($n)$n -is [Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -eq 'Invoke-ITMExcelMethod'},$true)
. ([scriptblock]::Create($transportFn.Extent.Text))

function Import-BackupV55Function([string]$name){
    $tokens=$null;$errors=$null
    $ast=[Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot 'StockageClasseurs.ps1'),[ref]$tokens,[ref]$errors)
    if($errors.Count){throw 'Syntaxe StockageClasseurs.ps1 invalide.'}
    $node=$ast.Find({param($n)$n -is [Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -eq $name},$true)
    if(-not $node){throw ('Fonction de sauvegarde absente : '+$name)}
    . ([scriptblock]::Create('function global:'+$name+$node.Extent.Text.Substring(('function '+$name).Length)))
}
foreach($name in @('Write-ITMAtomicJson','Get-ITMStoreFiles','Get-ITMPEBackupFiles','Get-ITMV55BackupHash','Get-ITMV55BackupContextId','Assert-ITMBackupManifest','Assert-ITMV55BackupDocument','Get-ITMV55BackupFiles','Backup-ITMStore','Restore-ITMStore')){Import-BackupV55Function $name}
function Assert-BackupV55([bool]$condition,[string]$message){if(-not $condition){throw ('Sauvegardes V55 : '+$message)}}
function Assert-ITMPermission {}
function Get-BackupV55RestoreCount {return @(Get-ChildItem -LiteralPath (Join-Path $script:storeRoot 'data') -Directory -ErrorAction SilentlyContinue).Count}
function Assert-BackupV55Rejected([scriptblock]$action,[string]$message){
    $before=Get-BackupV55RestoreCount;$rejected=$false
    try{[void](& $action)}catch{$rejected=$true}
    Assert-BackupV55 $rejected $message
    Assert-BackupV55 ((Get-BackupV55RestoreCount) -eq $before) 'Une restauration rejetée a créé un dossier data.'
}
function Copy-BackupV55Fixture([string]$source,[string]$name){
    $destination=Join-Path (Join-Path $script:storeFolder 'sauvegardes') $name
    [void][IO.Directory]::CreateDirectory($destination)
    foreach($file in Get-ChildItem -LiteralPath $source -File){[IO.File]::Copy($file.FullName,(Join-Path $destination $file.Name),$false)}
    return $destination
}
function Set-BackupV55Hash([string]$folder,[string]$name){
    $path=Join-Path $folder 'empreintes.json';$manifest=Get-Content -LiteralPath $path -Raw -Encoding UTF8 | ConvertFrom-Json
    $manifest | Add-Member NoteProperty $name (Get-FileHash -LiteralPath (Join-Path $folder $name) -Algorithm SHA256).Hash -Force
    Write-ITMAtomicJson $path $manifest
}
$tempRoot=Join-Path ([IO.Path]::GetTempPath()) ('scolia-tests-sauvegardes-v55-'+[guid]::NewGuid().ToString('N'))
try{
    $script:storeRoot=$tempRoot;$script:storeFolder=Join-Path $tempRoot 'origine';[void][IO.Directory]::CreateDirectory($script:storeFolder)
    $script:scolia=[pscustomobject]@{Id='ecole-test-v55'}
    $script:store=[pscustomobject]@{Version=1;Id=[guid]::NewGuid().ToString('N');Active='classeur-000001.xlsm';Segments=@('classeur-000001.xlsm');LearnerLimit=100;PaymentLimit=100;Closed=$false}
    $workbook=Join-Path $script:storeFolder 'classeur-000001.xlsm';[IO.File]::WriteAllText($workbook,'classeur factice, aucun accès Excel')
    $book=[pscustomobject]@{Name='classeur-000001.xlsm';FullName=$workbook}
    $book | Add-Member ScriptMethod SaveCopyAs {param($path)[IO.File]::Copy($this.FullName,$path,$false)}
    $script:storeBooks=@($book)
    $contextId=Get-ITMV55BackupContextId $script:store;$featureNames=@()
    foreach($spec in @(@('presences','2026-2027'),@('reinscriptions','2025-2026'))){
        $key=Get-ITMV55BackupHash ($contextId+'|'+$spec[1]);$name='scolia-v55-'+$spec[0]+'-'+$key+'.json';$featureNames+=$name
        $document=[pscustomobject]@{Schema=1;Context=[pscustomobject]@{Id=$contextId;Year=$spec[1];LocalKey=('a'*64)};Revision=3;Items=@([pscustomobject]@{Marker='initial'})}
        Write-ITMAtomicJson (Join-Path $script:storeFolder $name) $document
    }
    # Un brouillon d inscription ne doit jamais entrer dans une sauvegarde partagée.
    $draftName='scolia-v55-inscriptions-'+('b'*64)+'.json';[IO.File]::WriteAllText((Join-Path $script:storeFolder $draftName),'brouillon local privé')
    $originalHashes=@{};foreach($name in @('classeur-000001.xlsm')+$featureNames){$originalHashes[$name]=(Get-FileHash -LiteralPath (Join-Path $script:storeFolder $name) -Algorithm SHA256).Hash}
    $backup=Backup-ITMStore;Assert-BackupV55 $backup.ok 'Sauvegarde complète refusée.'
    $hashes=Get-Content -LiteralPath (Join-Path $backup.path 'empreintes.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    foreach($name in $featureNames){Assert-BackupV55 ([bool]$hashes.PSObject.Properties[$name]) 'Une donnée V55 manque aux empreintes.'}
    Assert-BackupV55 ([bool]$hashes.PSObject.Properties['registre.json']) 'Le registre ne possède pas d empreinte.'
    Assert-BackupV55 (-not [IO.File]::Exists((Join-Path $backup.path $draftName))) 'Un brouillon privé a été copié.'
    Assert-BackupV55 (@(Get-ChildItem -LiteralPath $backup.path -File | Where-Object {$_.Name -like '*.lock' -or $_.Name -like '*.precedent'}).Count -eq 0) 'Les verrous ou fichiers de secours ont été copiés.'
    $restored=Restore-ITMStore $backup.path;$restoredFolder=[IO.Path]::GetDirectoryName($restored)
    Assert-BackupV55 ($restoredFolder -cne $script:storeFolder) 'La restauration a remplacé l origine.'
    $restoredIndex=Get-Content -LiteralPath (Join-Path $restoredFolder 'registre.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    Assert-BackupV55 ($restoredIndex.Id -ceq $script:store.Id) 'L identité de la base restaurée a changé.'
    foreach($name in $featureNames){Assert-BackupV55 ((Get-FileHash -LiteralPath (Join-Path $restoredFolder $name) -Algorithm SHA256).Hash -eq $originalHashes[$name]) 'Une donnée V55 ou son contexte a été réécrit.'}
    # Les sauvegardes des versions précédentes ne possèdent ni JSON V55 ni empreinte du registre.
    $legacy=Join-Path (Join-Path $script:storeFolder 'sauvegardes') 'ancienne';[void][IO.Directory]::CreateDirectory($legacy)
    [IO.File]::Copy($workbook,(Join-Path $legacy 'classeur-000001.xlsm'),$false)
    Write-ITMAtomicJson (Join-Path $legacy 'registre.json') $script:store
    Write-ITMAtomicJson (Join-Path $legacy 'empreintes.json') @{'classeur-000001.xlsm'=$originalHashes['classeur-000001.xlsm']}
    Assert-BackupV55 ([IO.File]::Exists((Restore-ITMStore $legacy))) 'Une ancienne sauvegarde valide a été refusée.'
    $tampered=Copy-BackupV55Fixture $backup.path 'empreinte-modifiee'
    [IO.File]::AppendAllText((Join-Path $tampered $featureNames[0]),' ')
    Assert-BackupV55Rejected {Restore-ITMStore $tampered} 'Une donnée V55 modifiée a été restaurée.'
    $invalid=Copy-BackupV55Fixture $backup.path 'json-corrompu'
    [IO.File]::WriteAllText((Join-Path $invalid $featureNames[0]),'{incomplet')
    Set-BackupV55Hash $invalid $featureNames[0]
    Assert-BackupV55Rejected {Restore-ITMStore $invalid} 'Un JSON corrompu avec une empreinte valide a été restauré.'
    foreach($case in @('schema','revision','annee','items')){
        $badStructure=Copy-BackupV55Fixture $backup.path ('structure-'+$case)
        $badDocument=Get-Content -LiteralPath (Join-Path $badStructure $featureNames[0]) -Raw -Encoding UTF8 | ConvertFrom-Json
        switch($case){'schema'{$badDocument.Schema=2};'revision'{$badDocument.Revision=-1};'annee'{$badDocument.Context.Year='2026-2028'};'items'{$badDocument.Items=[pscustomobject]@{Marker='non-liste'}}}
        Write-ITMAtomicJson (Join-Path $badStructure $featureNames[0]) $badDocument;Set-BackupV55Hash $badStructure $featureNames[0]
        Assert-BackupV55Rejected {Restore-ITMStore $badStructure} ('Structure V55 incohérente acceptée : '+$case)
    }
    $changedRegister=Copy-BackupV55Fixture $backup.path 'registre-modifie'
    $changedIndex=Get-Content -LiteralPath (Join-Path $changedRegister 'registre.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    $changedIndex.LearnerLimit=200;Write-ITMAtomicJson (Join-Path $changedRegister 'registre.json') $changedIndex
    Assert-BackupV55Rejected {Restore-ITMStore $changedRegister} 'Un registre modifié sans nouvelle empreinte a été accepté.'
    $foreign=Copy-BackupV55Fixture $backup.path 'autre-contexte'
    $foreignDocument=Get-Content -LiteralPath (Join-Path $foreign $featureNames[0]) -Raw -Encoding UTF8 | ConvertFrom-Json
    $foreignDocument.Context.Id='c'*64;Write-ITMAtomicJson (Join-Path $foreign $featureNames[0]) $foreignDocument;Set-BackupV55Hash $foreign $featureNames[0]
    Assert-BackupV55Rejected {Restore-ITMStore $foreign} 'Un JSON d une autre base a été restauré.'
    $wrongName=Copy-BackupV55Fixture $backup.path 'nom-contexte-incoherent';$renamed='scolia-v55-presences-'+('d'*64)+'.json'
    [IO.File]::Move((Join-Path $wrongName $featureNames[0]),(Join-Path $wrongName $renamed))
    $wrongHashes=Get-Content -LiteralPath (Join-Path $wrongName 'empreintes.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    $wrongHashes.PSObject.Properties.Remove($featureNames[0]);$wrongHashes | Add-Member NoteProperty $renamed $originalHashes[$featureNames[0]]
    Write-ITMAtomicJson (Join-Path $wrongName 'empreintes.json') $wrongHashes
    Assert-BackupV55Rejected {Restore-ITMStore $wrongName} 'Un fichier dont le nom vise une autre année a été restauré.'
    $traversal=Copy-BackupV55Fixture $backup.path 'chemin-interdit'
    $malicious=Get-Content -LiteralPath (Join-Path $traversal 'empreintes.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    $malicious | Add-Member NoteProperty '../autre.json' ('e'*64);Write-ITMAtomicJson (Join-Path $traversal 'empreintes.json') $malicious
    Assert-BackupV55Rejected {Restore-ITMStore $traversal} 'Un chemin hors sauvegarde a été accepté.'
    $unlisted=Copy-BackupV55Fixture $legacy 'v55-non-referencee'
    [IO.File]::Copy((Join-Path $backup.path $featureNames[0]),(Join-Path $unlisted $featureNames[0]),$false)
    Assert-BackupV55Rejected {Restore-ITMStore $unlisted} 'Une donnée V55 non référencée a été acceptée.'
    $registeredDraft=Copy-BackupV55Fixture $backup.path 'brouillon-interdit'
    [IO.File]::WriteAllText((Join-Path $registeredDraft $draftName),'brouillon privé');Set-BackupV55Hash $registeredDraft $draftName
    Assert-BackupV55Rejected {Restore-ITMStore $registeredDraft} 'Un brouillon partagé dans le manifeste a été accepté.'
    $locked=Join-Path $script:storeFolder ($featureNames[0]+'.lock');$gate=[IO.File]::Open($locked,[IO.FileMode]::OpenOrCreate,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None)
    try{Assert-BackupV55Rejected {Backup-ITMStore} 'Une sauvegarde a ignoré un enregistrement en cours.'}finally{$gate.Dispose()}
    $sourceFeature=Join-Path $script:storeFolder $featureNames[0];$sourceBytes=[IO.File]::ReadAllBytes($sourceFeature)
    $backupCount=@(Get-ChildItem -LiteralPath (Join-Path $script:storeFolder 'sauvegardes') -Directory).Count
    try{
        [IO.File]::WriteAllText($sourceFeature,'{illisible')
        Assert-BackupV55Rejected {Backup-ITMStore} 'Une source V55 corrompue a produit une sauvegarde.'
        Assert-BackupV55 (@(Get-ChildItem -LiteralPath (Join-Path $script:storeFolder 'sauvegardes') -Directory).Count -eq $backupCount) 'Une sauvegarde rejetée a laissé un dossier incomplet.'
    }finally{[IO.File]::WriteAllBytes($sourceFeature,$sourceBytes)}
    foreach($name in $originalHashes.Keys){Assert-BackupV55 ((Get-FileHash -LiteralPath (Join-Path $script:storeFolder $name) -Algorithm SHA256).Hash -eq $originalHashes[$name]) 'Une opération a modifié les données de l origine.'}
    'PASS : sauvegarde/restauration V55, deux années, contexte conservé, anciennes sauvegardes, empreintes, JSON corrompu, chemins interdits et brouillons privés.'
}finally{if([IO.Directory]::Exists($tempRoot)){[IO.Directory]::Delete($tempRoot,$true)}}
