if(-not (Get-Command Invoke-ITMExcelRunNoArguments -ErrorAction SilentlyContinue)){. (Join-Path $PSScriptRoot 'PasserelleExcel.ps1')}
# Windows PowerShell 5.1. Registered segments form ONE logical school-year database.
$script:storeRoot=[IO.Path]::GetDirectoryName($PSScriptRoot)
$script:store=$null;$script:storeLock=$null;$script:storeBooks=@();$script:storeFolder=''
function Write-ITMAtomicJson([string]$path,$value) {
    if([string]::IsNullOrWhiteSpace($path)){throw 'Chemin de stockage vide.'}
    $directory=[IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($path))
    [void][IO.Directory]::CreateDirectory($directory)
    $temp=$path+'.'+[Guid]::NewGuid().ToString('N')+'.tmp'
    $backup=$path+'.precedent'
    try {
        $json=ConvertTo-Json -InputObject $value -Depth 12 -ErrorAction Stop
        $bytes=[Text.UTF8Encoding]::new($false).GetBytes($json)
        $stream=New-Object IO.FileStream($temp,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None)
        try{$stream.Write($bytes,0,$bytes.Length);$stream.Flush($true)}finally{$stream.Dispose()}
        if([IO.File]::Exists($path)){
            # Supprime uniquement l'ancien fichier de secours avant le remplacement atomique.
            if([IO.File]::Exists($backup)){[IO.File]::Delete($backup)}
            [IO.File]::Replace($temp,$path,$backup,$true)
        }else{[IO.File]::Move($temp,$path)}
    }finally{if([IO.File]::Exists($temp)){[IO.File]::Delete($temp)}}
}
function Get-ITMStoreFiles($index) {
    if($index.Version -ne 1 -or [string]$index.Id -notmatch '^[a-f0-9]{32}$' -or @($index.Segments).Count -lt 1){throw 'Registre data invalide.'}
    $seen=@{};$paths=@()
    foreach($name in @($index.Segments)){
        if([string]$name -notmatch '^classeur-[0-9]{6}\.xlsm$' -or $seen.ContainsKey([string]$name)){throw 'Liste des classeurs invalide.'}
        $seen[[string]$name]=$true;$paths+=Join-Path $script:storeFolder ([string]$name)
    }
    if(-not $seen.ContainsKey([string]$index.Active)){throw 'Classeur actif absent du registre.'}
    if([int]$index.LearnerLimit -lt 100 -or [int]$index.LearnerLimit -gt 100000 -or [int]$index.PaymentLimit -lt 100 -or [int]$index.PaymentLimit -gt 200000){throw 'Seuils data invalides.'}
    return $paths
}
function Invoke-ITMStockMacro($book,[string]$name,[object[]]$arguments=@()) {
    $liveBook=Resolve-ITMLiveWorkbook $book
    $macro=Get-ITMExcelMacroName $liveBook 'ModuleStockageITM' $name
    if($arguments.Count -gt 3){throw 'Paramètres de stockage incorrects.'}
    return Invoke-ITMExcelRun $script:excel $macro $arguments
}
function Get-ITMBookProperty($book,[string]$name) {
    $properties=Get-ITMExcelProperty $book 'CustomDocumentProperties'
    if($null -eq $properties){throw 'Excel n''a pas retourné les propriétés personnalisées du classeur.'}
    $count=[int](Get-ITMExcelProperty $properties 'Count')
    for($i=1;$i -le $count;$i++){
        $property=Get-ITMExcelProperty $properties 'Item' @($i)
        if([string](Get-ITMExcelProperty $property 'Name') -eq $name){return [string](Get-ITMExcelProperty $property 'Value')}
    }
    return ''
}
function Set-ITMBookProperty($book,[string]$name,$value) {
    $script:storeConnectionStep='Enregistrement de l''identité : '+$name
    $properties=Get-ITMExcelProperty $book 'CustomDocumentProperties'
    if($null -eq $properties){throw 'Excel n''a pas retourné les propriétés personnalisées du classeur.'}
    $existing=$null;$count=[int](Get-ITMExcelProperty $properties 'Count')
    for($i=1;$i -le $count;$i++){
        $property=Get-ITMExcelProperty $properties 'Item' @($i)
        if([string](Get-ITMExcelProperty $property 'Name') -eq $name){$existing=$property;break}
    }
    if($null -ne $existing){Set-ITMExcelProperty $existing 'Value' $value}else{
        $type=$(if($value -is [int]){1}else{4})
        # DocumentProperties.Add(Name, LinkToContent, Type, Value, LinkSource).
        # LinkSource must be explicitly omitted rather than lost by the PS COM adapter.
        [void](Invoke-ITMExcelMethod $properties 'Add' @($name,$false,$type,$value,[Reflection.Missing]::Value))
    }
    if((Get-ITMBookProperty $book $name) -cne [string]$value){throw ('Identité non enregistrée dans Excel : '+$name)}
}
function Set-ITMStoreProperties {
    foreach($b in $script:storeBooks){
        Set-ITMBookProperty $b 'ITM_DataSet' ([string]$script:store.Id)
        Set-ITMBookProperty $b 'ITM_SegmentCount' ([int]$script:store.Segments.Count)
    }
}
function Get-ITMPersistedIdentity([string]$path) {
    Add-Type -AssemblyName System.IO.Compression
    $stream=[IO.File]::Open($path,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::ReadWrite)
    $archive=$null;$reader=$null
    try{
        $archive=New-Object IO.Compression.ZipArchive($stream,[IO.Compression.ZipArchiveMode]::Read,$true)
        $entry=$archive.GetEntry('docProps/custom.xml')
        if(-not $entry){return ''}
        $reader=New-Object IO.StreamReader($entry.Open())
        $settings=New-Object Xml.XmlReaderSettings;$settings.DtdProcessing=[Xml.DtdProcessing]::Prohibit;$settings.XmlResolver=$null
        $xmlReader=[Xml.XmlReader]::Create($reader,$settings)
        try{$xml=New-Object Xml.XmlDocument;$xml.XmlResolver=$null;$xml.Load($xmlReader)}finally{$xmlReader.Dispose()}
        foreach($property in $xml.DocumentElement.ChildNodes){if($property -is [Xml.XmlElement] -and $property.GetAttribute('name') -eq 'ITM_DataSet'){return [string]$property.InnerText}}
        return ''
    }finally{if($reader){$reader.Dispose()};if($archive){$archive.Dispose()};$stream.Dispose()}
}
function Get-ITMPreparationSignature {
    return 'ITM-PERSISTENCE-1|'+(Get-FileHash -LiteralPath (Join-Path $PSScriptRoot 'manifest.json') -Algorithm SHA256).Hash
}
function Test-ITMPreparationCurrent($index,[DateTime]$now=[DateTime]::UtcNow) {
    $property=$index.PSObject.Properties['Preparation']
    if(-not $property -or -not $property.Value){return $false}
    $stamp=$property.Value
    if([string]$stamp.DataSet -cne [string]$index.Id -or [string]$stamp.Signature -cne (Get-ITMPreparationSignature)){return $false}
    if([string]$stamp.Segments -cne (@($index.Segments) -join '|')){return $false}
    $verified=[DateTimeOffset]::MinValue
    if(-not [DateTimeOffset]::TryParseExact([string]$stamp.VerifiedUtc,'o',[Globalization.CultureInfo]::InvariantCulture,[Globalization.DateTimeStyles]::None,[ref]$verified)){return $false}
    $age=$now.ToUniversalTime()-$verified.UtcDateTime
    return ($age.TotalSeconds -ge 0 -and $age.TotalHours -lt 24)
}
function Test-ITMPreparationStructure($book) {
    foreach($name in @('PARAMETRES','PROMOTIONS','APPRENANTS','PAIEMENTS')){
        try{$sheet=$book.Worksheets.Item($name)}catch{throw ('Feuille attendue absente : '+$name+' dans '+$book.Name)}
        if($name -in @('APPRENANTS','PAIEMENTS')){
            if($sheet.ListObjects.Count -lt 1){throw ('Tableau absent : '+$name+' dans '+$book.Name)}
            $table=$sheet.ListObjects.Item(1)
            $minimum=$(if($name -eq 'APPRENANTS'){15}else{14})
            if([int]$table.HeaderRowRange.Row -ne 4 -or [int]$table.Range.Columns.Count -lt $minimum){throw ('Structure incompatible : '+$name+' dans '+$book.Name+'. Aucune donnée ne sera réinitialisée.')}
        }
    }
}
function Update-ITMStorePreparation {
    $script:storeConnectionStep='Vérification des identités et de la préparation 24 h'
    $repairs=@();$due=-not (Test-ITMPreparationCurrent $script:store)
    # Check every member before repairing anything. A foreign identity is never overwritten.
    foreach($b in $script:storeBooks){
        $identity=Get-ITMBookProperty $b 'ITM_DataSet'
        $diskIdentity=Get-ITMPersistedIdentity ([string]$b.FullName)
        foreach($found in @($identity,$diskIdentity)){
            if($found -and $found -cne [string]$script:store.Id){throw ('Le classeur '+$b.Name+' appartient à une autre base. Réparation automatique refusée.')}
        }
        if($identity -cne [string]$script:store.Id -or $diskIdentity -cne [string]$script:store.Id -or (Get-ITMBookProperty $b 'ITM_SegmentCount') -ne [string]$script:store.Segments.Count){$repairs+=,$b}
    }
    if(-not $due -and $repairs.Count -eq 0){return}
    if(-not $script:storeImporting){
        $registered=Get-Content -LiteralPath (Join-Path $script:storeFolder 'registre.json') -Raw -Encoding UTF8 | ConvertFrom-Json
        [void](Get-ITMStoreFiles $registered)
        if(-not $registered -or [string]$registered.Id -cne [string]$script:store.Id -or (@($registered.Segments) -join '|') -cne (@($script:store.Segments) -join '|')){throw 'La base ne possède pas un registre valide. Aucune identité ne sera attribuée automatiquement.'}
    }
    foreach($b in $script:storeBooks){Test-ITMPreparationStructure $b}
    $transaction=($repairs.Count -gt 0 -and -not $script:storeImporting)
    if($transaction){Start-ITMStoreTransaction}
    # Leave the recovery journal intact on failure; next startup restores its verified backup.
    foreach($b in $repairs){
        Set-ITMBookProperty $b 'ITM_DataSet' ([string]$script:store.Id)
        Set-ITMBookProperty $b 'ITM_SegmentCount' ([int]$script:store.Segments.Count)
        [void](Invoke-ITMExcelMethod $b 'Save')
        if((Get-ITMPersistedIdentity ([string]$b.FullName)) -cne [string]$script:store.Id){throw ('Excel n''a pas conservé l''identité dans '+$b.Name+'. Sauvegarde de récupération conservée.')}
    }
    $stamp=[pscustomobject]@{DataSet=[string]$script:store.Id;VerifiedUtc=[DateTimeOffset]::UtcNow.ToString('o');Signature=(Get-ITMPreparationSignature);Segments=(@($script:store.Segments) -join '|')}
    $script:store | Add-Member -NotePropertyName Preparation -NotePropertyValue $stamp -Force
    if($transaction){Complete-ITMStoreTransaction}elseif(-not $script:storeImporting){Write-ITMAtomicJson (Join-Path $script:storeFolder 'registre.json') $script:store}
}
function Close-ITMStore {
    # Excel.Quit in Disconnect-Book closes the remaining COM workbooks.
    $script:storeBooks=@();$script:store=$null;$script:storeFolder=''
    if($script:storeLock){$script:storeLock.Dispose();$script:storeLock=$null}
}
function Recover-ITMStore {
    $pending=Join-Path $script:storeFolder 'transaction.json'
    if(-not [IO.File]::Exists($pending)){return}
    $tx=Get-Content -LiteralPath $pending -Raw -Encoding UTF8 | ConvertFrom-Json
    if([string]$tx.Backup -notmatch '^transaction-[a-f0-9]{32}$'){throw 'Journal de récupération invalide.'}
    $dir=Join-Path $script:storeFolder $tx.Backup
    $allFiles=@(Get-ITMStoreFiles $tx.Index)
    $oldFiles=$allFiles
    # Les transactions V37 peuvent ne sauvegarder que le classeur réellement modifié.
    # Les anciens journaux sans Files restent restaurés intégralement.
    if($tx.PSObject.Properties['Files'] -and @($tx.Files).Count){
        $wanted=@($tx.Files | ForEach-Object {[string]$_})
        $oldFiles=@($allFiles | Where-Object {[IO.Path]::GetFileName($_) -in $wanted})
        if($oldFiles.Count -ne $wanted.Count){throw 'Journal de récupération incomplet : un segment ciblé est introuvable.'}
    }
    # Vérifier toutes les copies ciblées avant de toucher un fichier courant. Opération répétable après incident.
    foreach($path in $oldFiles){
        $name=[IO.Path]::GetFileName($path);$copy=Join-Path $dir $name
        if(-not [IO.File]::Exists($copy) -or (Get-FileHash -LiteralPath $copy -Algorithm SHA256).Hash -ne $tx.Hashes.$name){throw 'Sauvegarde de récupération incomplète. Données bloquées pour éviter une restauration partielle.'}
    }
    foreach($path in $oldFiles){[IO.File]::Copy((Join-Path $dir ([IO.Path]::GetFileName($path))),$path,$true)}
    Write-ITMAtomicJson (Join-Path $script:storeFolder 'registre.json') $tx.Index
    [IO.File]::Delete($pending)
    $script:store=$tx.Index
}
function Open-ITMStoreBooks {
    $script:storeBooks=@();$script:book=$null
    foreach($path in @(Get-ITMStoreFiles $script:store)){
        if(-not [IO.File]::Exists($path)){throw ('Classeur data manquant : '+$path)}
        $script:storeConnectionStep='Ouverture du segment : '+[IO.Path]::GetFileName($path)
        $b=$null
        if(Get-Command Find-ScoliaOpenWorkbook -ErrorAction SilentlyContinue){$b=Find-ScoliaOpenWorkbook $script:excel $path}
        if(-not $b){
            $security=$script:excel.AutomationSecurity;$script:excel.AutomationSecurity=1
            try{$b=$script:excel.Workbooks.Open($path,0,$false)}finally{$script:excel.AutomationSecurity=$security}
        }
        if(-not $b){throw ('Excel n''a pas ouvert le classeur : '+$path)}
        if($b.ReadOnly){throw ('Classeur déjà ouvert ou non modifiable : '+$path)}
        $script:storeBooks+=,$b
        $script:storeConnectionStep='Vérification de la passerelle du segment : '+[IO.Path]::GetFileName($path)
        $macro=Get-ITMExcelMacroName $b 'ModuleApplicationITM' 'ITMAppVersion'
        if((Invoke-ITMExcelRunNoArguments $script:excel $macro) -ne 'ITM-DESKTOP-4'){throw 'Un classeur data nécessite une mise à jour de la passerelle.'}
        if($b.Name -eq $script:store.Active){$script:book=$b}
    }
    if(-not $script:book){throw 'Le classeur actif du registre n''a pas été retrouvé dans Excel.'}
    Update-ITMStorePreparation
}
function Initialize-ITMStore([string]$source) {
    $script:storeConnectionStep='Contrôle du dossier et du verrou data'
    $dataRoot=Join-Path $script:storeRoot 'data';[void][IO.Directory]::CreateDirectory($dataRoot)
    $importGuard=[IO.File]::Open((Join-Path $dataRoot 'connexion.lock'),[IO.FileMode]::OpenOrCreate,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None)
    try {
    $folder=[IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($source));$indexPath=Join-Path $folder 'registre.json'
    if(-not [IO.File]::Exists($indexPath) -and -not [IO.File]::Exists((Join-Path $folder 'transaction.json'))){
        $dataRoot=Join-Path $script:storeRoot 'data'
        if([IO.Directory]::Exists($dataRoot)){
            $matches=@(Get-ChildItem -LiteralPath $dataRoot -Directory | ForEach-Object {
                $candidate=Join-Path $_.FullName 'registre.json'
                if([IO.File]::Exists($candidate)){
                    try{$entry=Get-Content -LiteralPath $candidate -Raw -Encoding UTF8 | ConvertFrom-Json
                        if([string]$entry.Origin -eq [IO.Path]::GetFullPath($source)){[pscustomobject]@{Folder=$_.FullName;Index=$entry;Date=$entry.Created}}
                    }catch{throw ('Registre illisible : '+$candidate)}
                }
            } | Sort-Object Date -Descending)
            if($matches.Count){$folder=$matches[0].Folder;$indexPath=Join-Path $folder 'registre.json'}
        }
    }
    if([IO.File]::Exists($indexPath) -or [IO.File]::Exists((Join-Path $folder 'transaction.json'))){
        if($null -ne $script:book){[void](Invoke-ITMExcelMethod $script:book 'Close' @($false,[Reflection.Missing]::Value,[Reflection.Missing]::Value));$script:book=$null}
        $script:storeFolder=$folder
        $script:storeLock=[IO.File]::Open((Join-Path $folder 'session.lock'),[IO.FileMode]::OpenOrCreate,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None)
        # Le journal validé permet de restaurer aussi un registre absent ou illisible.
        Recover-ITMStore
        $script:store=Get-Content -LiteralPath $indexPath -Raw -Encoding UTF8 | ConvertFrom-Json
        [void](Get-ITMStoreFiles $script:store);Open-ITMStoreBooks
    }else{
        $existingGroup=''
        if($null -eq $script:book){throw 'Le classeur source est absent avant l''importation data.'}
        $script:storeConnectionStep='Lecture de l''identité du classeur source'
        $existingGroup=Get-ITMBookProperty $script:book 'ITM_DataSet'
        if($existingGroup){throw 'Ce classeur appartient à un dossier data. Reconnectez un classeur depuis le dossier contenant registre.json et tous ses segments ; une copie isolée ne peut pas être importée.'}
        $id=[Guid]::NewGuid().ToString('N');$folder=Join-Path (Join-Path $script:storeRoot 'data') $id
        [void][IO.Directory]::CreateDirectory($folder);$script:storeFolder=$folder
        $script:storeLock=[IO.File]::Open((Join-Path $folder 'session.lock'),[IO.FileMode]::CreateNew,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None)
        $path=Join-Path $folder 'classeur-000001.xlsm'
        $script:storeConnectionStep='Création de la copie data'
        $script:connectionOperation='Appel : SaveCopyAs'
        [ITMExcelDispatch]::SaveCopyAs($script:book.PSObject.BaseObject,[string]$path)
        $script:storeConnectionStep='Fermeture de la source après copie'
        if(-not $script:excelBorrowed){[void](Invoke-ITMExcelMethod $script:book 'Close' @($false,[Reflection.Missing]::Value,[Reflection.Missing]::Value))}
        $script:book=$null
        $script:store=[pscustomobject]@{Version=1;Id=$id;Created=(Get-Date -Format o);Origin=[IO.Path]::GetFullPath($source);Active='classeur-000001.xlsm';Segments=@('classeur-000001.xlsm');LearnerLimit=10000;PaymentLimit=50000;Closed=$false}
        $script:storeImporting=$true
        $script:storeConnectionStep='Ouverture et contrôle de la copie data'
        try{Open-ITMStoreBooks}finally{$script:storeImporting=$false}
        $script:storeConnectionStep='Sauvegarde initiale des classeurs data'
        foreach($b in $script:storeBooks){[void](Invoke-ITMExcelMethod $b 'Save')}
        # Publishing the index is the final import step; original file stays intact.
        $script:storeConnectionStep='Publication du registre data'
        Write-ITMAtomicJson (Join-Path $folder 'registre.json') $script:store
    }
    if($null -eq $script:book){throw 'Aucun classeur data actif après l''importation.'}
    $script:storeConnectionStep='Mémorisation du classeur actif'
    [void](Save-ConnectionChoice ([string](Get-ITMExcelProperty $script:book 'FullName')))
    $script:storeConnectionStep=''
    }finally{$importGuard.Dispose()}
}

function Start-ITMStoreTransaction([object[]]$books=@()) {
    $saveWatch=[Diagnostics.Stopwatch]::StartNew()
    try {
    if(-not $script:store){return}
    $pending=Join-Path $script:storeFolder 'transaction.json'
    if([IO.File]::Exists($pending)){throw 'Une récupération est nécessaire avant une autre opération.'}
    $sourceTargets=$(if(@($books).Count){@($books)}else{@($script:storeBooks)})
    $targets=@()
    foreach($candidate in @($sourceTargets | Where-Object {$_})){$targets+=,(Resolve-ITMLiveWorkbook $candidate)}
    $targets=@($targets | Group-Object Name | ForEach-Object {$_.Group[0]})
    if(-not $targets.Count){throw 'Aucun classeur à protéger avant l enregistrement.'}
    foreach($b in $targets){if([string]$b.Name -notin @($script:store.Segments)){throw 'Transaction refusée : classeur hors du dossier data.'}}
    $name='transaction-'+[Guid]::NewGuid().ToString('N');$dir=Join-Path $script:storeFolder $name
    [void][IO.Directory]::CreateDirectory($dir);$hashes=@{}
    foreach($b in $targets){$path=Join-Path $dir $b.Name;[void](Invoke-ITMExcelMethod $b 'SaveCopyAs' @([string]$path));$hashes[$b.Name]=(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash}
    Write-ITMAtomicJson $pending ([pscustomobject]@{Backup=$name;Index=$script:store;Hashes=$hashes;Files=@($targets | ForEach-Object {$_.Name});Date=(Get-Date -Format o)})
    } finally {
        $saveWatch.Stop()
        if(Get-Command Add-ScoliaPerformanceSample -ErrorAction SilentlyContinue){Add-ScoliaPerformanceSample 'Copie de recuperation' $saveWatch.Elapsed.TotalMilliseconds}
    }
}
function Save-ITMStoreBookIfDirty($book) {
    $saveWatch=[Diagnostics.Stopwatch]::StartNew()
    try {
    $skip=$false
    if(-not $script:excelBorrowed -and $script:storeLock -and $script:storeLock.CanWrite){
        try{
            $saved=Get-ITMExcelProperty $book 'Saved'
            $path=[string](Get-ITMExcelProperty $book 'FullName')
            $expected=Join-Path $script:storeFolder ([string](Get-ITMExcelProperty $book 'Name'))
            $skip=($saved -is [bool] -and $saved -and [IO.File]::Exists($path) -and [IO.Path]::GetFullPath($path).Equals([IO.Path]::GetFullPath($expected),[StringComparison]::OrdinalIgnoreCase))
        }catch{$skip=$false}
    }
    if(-not $skip){[void](Invoke-ITMExcelMethod $book 'Save')}
    } finally {
        $saveWatch.Stop()
        if(Get-Command Add-ScoliaPerformanceSample -ErrorAction SilentlyContinue){Add-ScoliaPerformanceSample $(if($skip){'Classeur deja sauvegarde'}else{'Sauvegarde classeur'}) $saveWatch.Elapsed.TotalMilliseconds}
    }
}
function Complete-ITMStoreTransaction([object[]]$books=@()) {
    $saveWatch=[Diagnostics.Stopwatch]::StartNew()
    try {
    $sourceTargets=$(if(@($books).Count){@($books)}else{@($script:storeBooks)})
    $targets=@()
    foreach($candidate in @($sourceTargets | Where-Object {$_})){$targets+=,(Resolve-ITMLiveWorkbook $candidate)}
    foreach($b in @($targets | Group-Object Name | ForEach-Object {$_.Group[0]})){Save-ITMStoreBookIfDirty $b}
    Write-ITMAtomicJson (Join-Path $script:storeFolder 'registre.json') $script:store
    [IO.File]::Delete((Join-Path $script:storeFolder 'transaction.json'))
    } finally {
        $saveWatch.Stop()
        if(Get-Command Add-ScoliaPerformanceSample -ErrorAction SilentlyContinue){Add-ScoliaPerformanceSample 'Validation transaction' $saveWatch.Elapsed.TotalMilliseconds}
    }
}
function Undo-ITMStoreTransaction {
    $open=@(foreach($b in $script:excel.Workbooks){if([string]$b.Path -eq $script:storeFolder){$b}})
    foreach($b in $open){[void](Invoke-ITMExcelMethod $b 'Close' @($false,[Reflection.Missing]::Value,[Reflection.Missing]::Value))}
    $script:storeBooks=@();$script:book=$null
    Recover-ITMStore;Open-ITMStoreBooks
}
function Ensure-ITMCapacity([string]$kind,[int]$reserve=1) {
    if(-not $script:store){return}
    if($script:store.Closed -or $script:data.closed){throw 'Année clôturée : consultation uniquement.'}
    $cap=[string](Invoke-ITMStockMacro $script:book 'ITMAppCapacite') | ConvertFrom-Json
    $used=$(if($kind -eq 'learners'){[int]$cap.learners}else{[int]$cap.payments})
    $limit=$(if($kind -eq 'learners'){[int]$script:store.LearnerLimit}else{[int]$script:store.PaymentLimit})
    if($used+$reserve -gt $limit){New-ITMSegment $true}
}
function New-ITMSegment([bool]$automatic=$false) {
    Assert-ITMPermission $(if($automatic){'Saisie'}else{'Administrer'})
    if(-not $script:store){throw 'Connectez un classeur préparé avec cette édition pour gérer le dossier data.'}
    if($script:store.Closed -or $script:data.closed){throw 'Année clôturée : création de segment bloquée.'}
    $numbers=@($script:store.Segments | ForEach-Object {[int]($_ -replace '\D','')})
    $number=1+($numbers | Measure-Object -Maximum).Maximum
    do{$name='classeur-{0:D6}.xlsm' -f [int]$number;$number++}while([IO.File]::Exists((Join-Path $script:storeFolder $name)))
    $path=Join-Path $script:storeFolder $name
    Start-ITMStoreTransaction
    try {
        $script:excel.AutomationSecurity=1
        try{$reply=[string](Invoke-ITMStockMacro $script:book 'ITMAppSegment' @($path)) | ConvertFrom-Json}finally{$script:excel.AutomationSecurity=3}
        if(-not $reply.ok){throw $reply.error}
        $b=$script:excel.Workbooks.Item($name)
        $cap=[string](Invoke-ITMStockMacro $b 'ITMAppCapacite') | ConvertFrom-Json
        if($cap.learners -ne 0 -or $cap.payments -ne 0){throw 'Le nouveau classeur ne possède pas des tableaux vides.'}
        $script:storeBooks+=,$b;$script:store.Segments=@($script:store.Segments)+$name;$script:store.Active=$name;$script:book=$b
        Set-ITMStoreProperties;Complete-ITMStoreTransaction
        [void](Save-ConnectionChoice $path)
    }catch{Undo-ITMStoreTransaction;throw}
}
function Get-ITMLearnerBook([string]$mat) {
    $learners=$(if(Get-Command Get-ITMLearnerMatches -ErrorAction SilentlyContinue){@(Get-ITMLearnerMatches $mat)}else{@($script:data.learners | Where-Object {(Key $_.Matricule) -eq (Key $mat)})})
    if($learners.Count -ne 1){throw 'Dossier absent ou matricule en double.'}
    if(-not $script:store){return (Resolve-ITMLiveWorkbook $script:book)}
    $source=[string]$learners[0].Source
    $books=@()
    for($i=0;$i -lt @($script:storeBooks).Count;$i++){
        if($i -lt @($script:store.Segments).Count -and [string]$script:store.Segments[$i] -eq $source){$books+=,$script:storeBooks[$i]}
    }
    if($books.Count -ne 1){throw 'Classeur du dossier introuvable.'}
    return (Resolve-ITMLiveWorkbook $books[0])
}
function Get-ITMIdentityPart([string]$value) {
    return (Key ($value -replace '[\s\u00a0\u202f]+',' ')).Trim()
}
function Assert-ITMLearnerIdentity($learners,[string]$mat,[string]$nom,[string]$post,[string]$prenom,[string]$promotion) {
    $n=Get-ITMIdentityPart $nom;$p=Get-ITMIdentityPart $post;$first=Get-ITMIdentityPart $prenom;$promo=Promo $promotion
    if(-not $n -or -not $p -or -not $first -or -not $promo){throw 'Identité ou promotion incomplète : enregistrement bloqué.'}
    $useIndex=$false
    try{$useIndex=[bool]((Get-Command Get-ITMLearnersByIdentity -ErrorAction SilentlyContinue) -and $script:learnerIndexSource -and [object]::ReferenceEquals($script:learnerIndexSource,$learners))}catch{$useIndex=$false}
    $candidates=$(if($useIndex){@(Get-ITMLearnersByIdentity $nom $post $prenom $promotion)}else{@($learners)})
    foreach($learner in $candidates){
        if($mat -and (Key $learner.Matricule) -eq (Key $mat)){continue}
        if((Get-ITMIdentityPart $learner.Nom) -eq $n -and (Get-ITMIdentityPart $learner.PostNom) -eq $p -and (Get-ITMIdentityPart $learner.Prenom) -eq $first -and (Promo $learner.Promotion) -eq $promo){
            throw ('Cet apprenant existe déjà dans cette promotion. Matricule : '+$learner.Matricule+' ; promotion : '+$learner.Promotion+'. Retrouvez et modifiez son dossier existant. Aucun nouvel enregistrement effectué.')
        }
    }
}
function Invoke-ITMDossier([object[]]$a) {
    Assert-ITMPermission 'Saisie'
    if($a.Count -ne 14){throw 'Paramètres du dossier incomplets.'}
    # Le snapshot déjà vérifié à la connexion est autoritatif tant que SCOLIA garde
    # le verrou exclusif du dossier data. Éviter une relecture Excel complète ici
    # accélère fortement les inscriptions successives.
    if($script:excelBorrowed){Refresh-Data $true}
    $snapshot=$script:data
    if(-not $script:snapshotValid -or -not $snapshot -or -not $snapshot.PSObject.Properties['learners']){$snapshot=Invoke-ITMJsonDirect 'ITMAppSnapshot'}
    if(-not $snapshot -or -not $snapshot.PSObject.Properties['learners']){throw 'Contrôle des apprenants impossible : enregistrement bloqué.'}
    if([string]$a[0] -and (Get-Command Assert-ScoliaDisciplineAdmission -ErrorAction SilentlyContinue)){
        $old=$(if((Get-Command Get-ITMLearnerMatches -ErrorAction SilentlyContinue) -and $script:learnerIndexSource -and [object]::ReferenceEquals($script:learnerIndexSource,$snapshot.learners)){@(Get-ITMLearnerMatches ([string]$a[0]))}else{@($snapshot.learners|Where-Object {(Key $_.Matricule) -eq (Key $a[0])})})
        if($old.Count -eq 1){
            if((Key $a[12]) -eq 'ACTIF' -and ([string]$a[11] -cne [string]$old[0].Annee -or (Promo $a[10]) -cne (Promo $old[0].Promotion))){Assert-ScoliaDisciplineAdmission ([string]$a[0])}
        }
    }
    Assert-ITMLearnerIdentity $snapshot.learners ([string]$a[0]) ([string]$a[1]) ([string]$a[2]) ([string]$a[3]) ([string]$a[10])
    $previousPromotion='';if([string]$a[0]){$previous=$(if((Get-Command Get-ITMLearnerMatches -ErrorAction SilentlyContinue) -and $script:learnerIndexSource -and [object]::ReferenceEquals($script:learnerIndexSource,$snapshot.learners)){@(Get-ITMLearnerMatches ([string]$a[0]))}else{@($snapshot.learners|Where-Object {(Key $_.Matricule) -eq (Key $a[0])})});if($previous.Count -eq 1){$previousPromotion=[string]$previous[0].Promotion}}
    Reset-ITMCompletedSaveLock
    $active=$script:book
    $transactionStarted=$false
    if([string]$a[0] -eq ''){Ensure-ITMCapacity 'learners';$active=$script:book}else{$script:book=Get-ITMLearnerBook ([string]$a[0])}
    try {
        # A multi-classeur data store needs an explicit durable transaction for learner writes.
        # The VBA macro can report success while a later reconnect loads the last committed files;
        # therefore do not report success until the transaction is committed and the saved record
        # can be read back from the authoritative snapshot.
        if($script:store){Start-ITMStoreTransaction @($script:book);$transactionStarted=$true}
        $raw=[string](Invoke-ITMWriteMacro 'ITMAppDossier' $a)
        $reply=$raw | ConvertFrom-Json -ErrorAction Stop
        if(-not $reply -or -not $reply.ok){
            $saveError = 'Excel n a pas confirme l enregistrement du dossier.'
            if($reply -and $reply.error){$saveError = [string]$reply.error}
            throw $saveError
        }
        # Vérification ciblée du dossier écrit : on ne sérialise plus toute la base
        # pour confirmer une seule inscription. La tranche sert aussi à actualiser
        # immédiatement l'interface sans second aller-retour Excel.
        $verify=Invoke-ITMJsonDirect 'ITMAppEtatApprenant' @([string]$reply.matricule)
        if(-not $verify -or -not $verify.ok -or -not $verify.learner){throw 'Contrôle de persistance échoué : le dossier enregistré est absent dans la base relue.'}
        $script:lastLearnerSlice=$verify
        if($script:store){
            Complete-ITMStoreTransaction @($script:book)
            $transactionStarted=$false
        }
        if($previousPromotion -and (Promo $previousPromotion) -ne (Promo $verify.learner.Promotion) -and (Get-Command Record-ScoliaPupilPromotionTransition -ErrorAction SilentlyContinue)){
            try{Record-ScoliaPupilPromotionTransition -Matricule ([string]$reply.matricule) -OldPromotion $previousPromotion -NewPromotion ([string]$verify.learner.Promotion) -EffectiveDate (Get-Date).Date}catch{if($script:status){$script:status.Text='Dossier enregistré ; confirmez le début du suivi dans la nouvelle promotion.'};try{Write-ITMUserAudit 'Suivi de promotion à confirmer' ([string]$reply.matricule)}catch{}}
        }
        return $raw
    } catch {
        if($transactionStarted){try{Undo-ITMStoreTransaction}catch{throw ('Échec de sauvegarde du dossier et de restauration transactionnelle : '+$_.Exception.Message)}}
        throw
    } finally {$script:book=$active}
}
function Assert-ITMLearnerDeletion($snapshot,[string]$mat,[string]$motif) {
    if([string]::IsNullOrWhiteSpace($mat)){throw 'Sélectionnez un apprenant.'}
    if([string]::IsNullOrWhiteSpace($motif) -or $motif.Trim().Length -lt 5){throw 'Indiquez un motif de suppression (au moins 5 caractères).'}
    if(-not $snapshot -or -not $snapshot.PSObject.Properties['learners'] -or -not $snapshot.PSObject.Properties['payments']){throw 'Contrôle des dossiers et paiements impossible : suppression bloquée.'}
    $rows=@($snapshot.learners | Where-Object {(Key $_.Matricule) -eq (Key $mat)})
    if($rows.Count -ne 1){throw 'Dossier absent ou matricule en double : suppression bloquée.'}
    # Paiements existants sont conservés comme historique comptable et la suppression est journalisée avec son motif.
    return $rows[0]
}
function Invoke-ITMLearnerDeletion([string]$mat,[string]$motif) {
    Assert-ITMPermission 'Saisie'
    $snapshot=Invoke-ITMJsonDirect 'ITMAppSnapshot'
    $learner=Assert-ITMLearnerDeletion $snapshot $mat $motif
    $active=$script:book
    try{
        if($script:store){
            $books=@($script:storeBooks | Where-Object {$_.Name -eq $learner.Source})
            if($books.Count -ne 1){throw 'Classeur de l''apprenant introuvable.'}
            $script:book=$books[0]
        }
        return Invoke-ITMJsonDirect 'ITMAppSupprimerApprenant' @([string]$learner.Matricule,$motif.Trim())
    }finally{$script:book=$active}
}
function Assert-ITMLearnerMerge($snapshot,[string]$keepMat,[string]$mergeMat,[string]$motif) {
    if([string]::IsNullOrWhiteSpace($keepMat) -or [string]::IsNullOrWhiteSpace($mergeMat)){throw 'Sélectionnez les deux dossiers à fusionner.'}
    if((Key $keepMat) -eq (Key $mergeMat)){throw 'Les deux matricules doivent être différents.'}
    if([string]::IsNullOrWhiteSpace($motif) -or $motif.Trim().Length -lt 5){throw 'Indiquez le motif de fusion (au moins 5 caractères).'}
    if(-not $snapshot -or -not $snapshot.PSObject.Properties['learners'] -or -not $snapshot.PSObject.Properties['payments']){throw 'Lecture des dossiers et paiements impossible : fusion bloquée.'}
    $keep=@($snapshot.learners | Where-Object {(Key $_.Matricule) -eq (Key $keepMat)})
    $merge=@($snapshot.learners | Where-Object {(Key $_.Matricule) -eq (Key $mergeMat)})
    if($keep.Count -ne 1 -or $merge.Count -ne 1){throw 'Un des dossiers est absent ou son matricule n''est pas unique.'}
    foreach($field in @('Nom','PostNom','Prenom')){
        if((Get-ITMIdentityPart ([string]$keep[0].$field)) -ne (Get-ITMIdentityPart ([string]$merge[0].$field))){throw 'Fusion refusée : les noms ne correspondent pas exactement.'}
    }
    if((Promo $keep[0].Promotion) -ne (Promo $merge[0].Promotion)){throw 'Fusion refusée : les promotions sont différentes.'}
    if(-not $keep[0].Source -or -not $merge[0].Source){throw 'Source du dossier inconnue : fusion bloquée.'}
    return [pscustomobject]@{Keep=$keep[0];Merge=$merge[0]}
}
function Invoke-ITMLearnerMerge([string]$keepMat,[string]$mergeMat,[string]$motif) {
    Assert-ITMPermission 'Saisie'
    $snapshot=Invoke-ITMJsonDirect 'ITMAppSnapshot'
    $pair=Assert-ITMLearnerMerge $snapshot $keepMat $mergeMat $motif
    $active=$script:book
    try{
        if($script:store){
            $books=@($script:storeBooks | Where-Object {$_.Name -eq $pair.Merge.Source})
            if($books.Count -ne 1){throw 'Classeur du dossier à fusionner introuvable.'}
            $script:book=$books[0]
        }
        return Invoke-ITMJsonDirect 'ITMAppFusionnerApprenants' @($keepMat.Trim(),$mergeMat.Trim(),$motif.Trim())
    }finally{$script:book=$active}
}
function Invoke-ITMPayment([string]$mat,[double]$amount,[string]$date) {
    if($script:scolia){throw 'Les paiements SCOLIA sʼenregistrent dans Comptabilité > Encaisser et sont reçus automatiquement au secrétariat.'}
    Assert-ITMPermission 'Saisie'
    if($script:peError){throw $script:peError}
    $persons=$(if(Get-Command Get-ITMLearnerMatches -ErrorAction SilentlyContinue){@(Get-ITMLearnerMatches $mat)}else{@($script:data.learners | Where-Object {(Key $_.Matricule) -eq (Key $mat)})});if($persons.Count -ne 1){throw 'Dossier absent ou matricule en double.'};$person=$persons[0]
    if((Get-ITMPECRecord $person) -and -not $script:peVersion){throw 'Mettez à jour les corrections avant d''enregistrer un paiement pour un bénéficiaire.'}
    Reset-ITMCompletedSaveLock
    Ensure-ITMCapacity 'payments' 10
    $raw=$(if($script:paymentDatesAvailable){Invoke-ITMWriteMacro 'ITMAppPaiement' @($mat,$amount,$date)}else{Invoke-ITMWriteMacro 'ITMAppPaiement' @($mat,$amount)})
    $reply=[string]$raw | ConvertFrom-Json -ErrorAction Stop
    if($reply -and $reply.ok){
        try{$script:lastLearnerSlice=Invoke-ITMJsonDirect 'ITMAppEtatApprenant' @($mat)}catch{$script:lastLearnerSlice=$null}
    }
    return [string]$raw
}
function New-ITMConsolidation([string]$mat='') {
    $folder=Join-Path $script:storeFolder 'temp';[void][IO.Directory]::CreateDirectory($folder)
    $path=Join-Path $folder ([Guid]::NewGuid().ToString('N')+'.xlsm')
    $script:excel.AutomationSecurity=1
    try{$reply=[string](Invoke-ITMStockMacro $script:book 'ITMAppConsolider' @($path,$mat)) | ConvertFrom-Json}finally{$script:excel.AutomationSecurity=3}
    if(-not $reply.ok){throw $reply.error}
    $pec=Join-Path $script:storeFolder 'prises-en-charge.xml';if([IO.File]::Exists($pec)){[IO.File]::Copy($pec,(Join-Path $folder 'prises-en-charge.xml'),$true)}
    $stage=$script:excel.Workbooks.Item([IO.Path]::GetFileName($path))
    if([IO.File]::Exists($pec)){[void]$stage.CustomDocumentProperties.Add('ITM_PECDataSet',$false,4,[string]$script:store.Id);$stage.Save()}
    return $stage
}
function Invoke-ITMManagedCorrection([object[]]$parameters) {
    Assert-ITMPermission 'Administrer'
    $receipt=[string]$parameters[0];$rows=@($script:data.payments | Where-Object {(Key $_.Numero) -eq (Key $receipt)})
    $mats=@($rows | ForEach-Object {Key $_.Matricule} | Select-Object -Unique)
    if($mats.Count -ne 1){throw 'Reçu introuvable ou utilisé pour plusieurs apprenants.'}
    $mat=$mats[0];$stage=$null;$stagePath='';$active=$script:book;$tx=$false
    try {
        $stage=New-ITMConsolidation $mat;$stagePath=[string]$stage.FullName
        $logStart=1
        try{$logStart=[int]$stage.Worksheets.Item('JOURNAL_ITM').Cells.Item(1048576,1).End(-4162).Row}catch{}
        $script:book=$stage
        $reply=Invoke-ITMJsonDirect 'ITMAppCorrectionPaiement' $parameters
        $script:book=$active
        if([bool]$parameters[3]){
            $src=$stage.Worksheets.Item('PAIEMENTS');$count=[Math]::Max(0,[int]$src.Cells.Item($src.Rows.Count,1).End(-4162).Row-4)
            Ensure-ITMCapacity 'payments' $count;$active=$script:book
            Start-ITMStoreTransaction;$tx=$true
            # Remove only this learner's old rows from each segment, preserving every other row.
            foreach($b in $script:storeBooks){
                $w=$b.Worksheets.Item('PAIEMENTS');$capacity=[string](Invoke-ITMStockMacro $b 'ITMAppCapacite') | ConvertFrom-Json;$last=4+[int]$capacity.payments
                for($r=$last;$r -ge 5;$r--){if((Key ($w.Cells.Item($r,4).Value2)) -eq $mat){[void]$w.Rows.Item($r).Delete()}}
                $capacity=[string](Invoke-ITMStockMacro $b 'ITMAppCapacite') | ConvertFrom-Json;$last=[Math]::Max(5,4+[int]$capacity.payments);[void]$w.ListObjects.Item(1).Resize($w.Range('A4:N'+$last))
            }
            $src=$stage.Worksheets.Item('PAIEMENTS');$last=[int]$src.Cells.Item($src.Rows.Count,1).End(-4162).Row
            # The corrected history was reserved as a whole before this transaction.
            for($r=5;$r -le $last;$r++){
                $cap=[string](Invoke-ITMStockMacro $script:book 'ITMAppCapacite') | ConvertFrom-Json
                if([int]$cap.payments+1 -gt [int]$script:store.PaymentLimit){throw 'Historique corrigé trop grand pour le classeur actif. Créez un classeur suivant dans Administration puis relancez la correction.'}
                $w=$script:book.Worksheets.Item('PAIEMENTS');$dest=[Math]::Max(5,5+[int]$cap.payments)
                $w.Range('A'+$dest+':N'+$dest).Value2=$src.Range('A'+$r+':N'+$r).Value2
                [void]$w.ListObjects.Item(1).Resize($w.Range('A4:N'+$dest));$w.Range('B'+$dest).NumberFormat='dd/mm/yyyy'
            }
            # Stage was a copy of active: transfer its full audit log, including reallocation entries.
            $log=$stage.Worksheets.Item('JOURNAL_ITM');$destLog=$null
            try{$destLog=$script:book.Worksheets.Item('JOURNAL_ITM')}catch{$destLog=$script:book.Worksheets.Add();$destLog.Name='JOURNAL_ITM'}
            $logEnd=[int]$log.Cells.Item(1048576,1).End(-4162).Row
            $logDest=[Math]::Max(2,[int]$destLog.Cells.Item(1048576,1).End(-4162).Row+1)
            $destLog.Range('A1:I1').Value2=$log.Range('A1:I1').Value2
            if($logEnd -gt $logStart){$destLog.Range('A'+$logDest+':I'+($logDest+$logEnd-$logStart-1)).Value2=$log.Range('A'+($logStart+1)+':I'+$logEnd).Value2}
            Complete-ITMStoreTransaction;$tx=$false;$script:committedThisAction=$true
        }
        return $reply
    }catch{if($tx){$script:book=$active;Undo-ITMStoreTransaction;$active=$script:book};throw}finally{
        $script:book=$active
        if($stage){[void]$stage.Close($false)}
        if($stagePath -and [IO.File]::Exists($stagePath)){[IO.File]::Delete($stagePath)}
    }
}
function Sync-ITMPromotions {
    $src=$script:book.Worksheets.Item('PROMOTIONS');$last=[Math]::Max(5,[int]$src.Cells.Item($src.Rows.Count,1).End(-4162).Row)
    foreach($b in $script:storeBooks){if($b.Name -ne $script:book.Name){
        $w=$b.Worksheets.Item('PROMOTIONS');[void]$w.Range('A5:H'+[Math]::Max($last,[int]$w.Cells.Item($w.Rows.Count,1).End(-4162).Row)).ClearContents()
        $w.Range('A5:H'+$last).Value2=$src.Range('A5:H'+$last).Value2
        if($w.ListObjects.Count -eq 1){[void]$w.ListObjects.Item(1).Resize($w.Range('A4:H'+$last))}
    }}
}
function Invoke-ITMManagedPromotion([object[]]$a) {
    Assert-ITMPermission 'Administrer'
    $code=Promo ([string]$a[0]);$old=@($script:data.promotions | Where-Object {(Promo $_.Code) -eq $code})
    $hasPayments=@($script:data.payments | Where-Object {(Promo $_.Promotion) -eq $code}).Count -gt 0
    if($old.Count -eq 1 -and $hasPayments -and ([decimal]$old[0].Mensuel -ne [decimal]$a[3] -or [decimal]$old[0].Annuel -ne [decimal]$a[4] -or [string]$old[0].Annee -ne [string]$a[2] -or -not [bool]$a[5])){throw 'Cette promotion possède des paiements : frais, année et activation conservés.'}
    if($old.Count -eq 1 -and [string]$old[0].Annee -ne [string]$a[2] -and @($script:data.learners | Where-Object {(Promo $_.Promotion) -eq $code}).Count){throw 'Des dossiers existent : utilisez la clôture annuelle.'}
    Start-ITMStoreTransaction
    try{$reply=Invoke-ITMJsonDirect 'ITMAppPromotion' $a;Sync-ITMPromotions;Complete-ITMStoreTransaction;$script:committedThisAction=$true;return $reply}catch{Undo-ITMStoreTransaction;throw}
}
function Get-ITMPEBackupFiles([string]$folder,$hashes=$null) {
    $paths=@();foreach($name in @('prises-en-charge.xml','prises-en-charge.xml.precedent','prises-en-charge.presence')){
        $path=Join-Path $folder $name
        if($hashes -and $hashes.PSObject.Properties[$name]){if(-not [IO.File]::Exists($path)){throw ('Sauvegarde de prise en charge manquante : '+$name)};$paths+=$path}
        elseif(-not $hashes -and [IO.File]::Exists($path)){$paths+=$path}
        elseif($hashes -and [IO.File]::Exists($path)){throw ('Prise en charge non référencée dans la sauvegarde : '+$name)}
    };return $paths
}
function Get-ITMV55BackupHash([string]$value) {
    $algorithm=[Security.Cryptography.SHA256]::Create()
    try{return ([BitConverter]::ToString($algorithm.ComputeHash([Text.Encoding]::UTF8.GetBytes($value)))).Replace('-','').ToLowerInvariant()}finally{$algorithm.Dispose()}
}
function Get-ITMV55BackupContextId($index) {
    $school=$(if($script:scolia){[string]$script:scolia.Id}else{'local'})
    return Get-ITMV55BackupHash ($school+'|'+[string]$index.Id)
}
function Assert-ITMBackupManifest($index,$hashes) {
    if(-not $hashes -or $hashes -isnot [pscustomobject]){throw 'Liste des empreintes de sauvegarde invalide.'}
    $allowed=@('registre.json','prises-en-charge.xml','prises-en-charge.xml.precedent','prises-en-charge.presence')+@($index.Segments)
    foreach($property in $hashes.PSObject.Properties){
        $name=[string]$property.Name
        if($allowed -cnotcontains $name -and $name -cnotmatch '^scolia-v55-(presences|reinscriptions|personnel|salaires|suiviapprenants|enseignements|discipline)-[a-f0-9]{64}\.json$'){throw ('Fichier non autorisé dans la sauvegarde : '+$name)}
        if([string]$property.Value -notmatch '^[a-fA-F0-9]{64}$'){throw ('Empreinte de sauvegarde invalide : '+$name)}
    }
    foreach($name in @($index.Segments)){if(-not $hashes.PSObject.Properties[[string]$name]){throw ('Classeur sans empreinte dans la sauvegarde : '+$name)}}
}
function Assert-ITMV55BackupDocument([string]$path,$index) {
    $name=[IO.Path]::GetFileName($path)
    if($name -cmatch '^scolia-v55-(presences|reinscriptions|personnel|salaires|suiviapprenants|enseignements|discipline)-([a-f0-9]{64})\.json$'){$fileKey=[string]$Matches[2]}else{throw ('Nom de données de suivi invalide : '+$name)}
    if(-not [IO.File]::Exists($path) -or ((Get-Item -LiteralPath $path -ErrorAction Stop).Attributes -band [IO.FileAttributes]::ReparsePoint)){throw ('Données de suivi manquantes ou lien non autorisé : '+$name)}
    try{$document=[IO.File]::ReadAllText($path,[Text.Encoding]::UTF8) | ConvertFrom-Json -ErrorAction Stop}catch{throw ('Données de suivi illisibles : '+$name)}
    if(-not $document -or $document.Schema -ne 1 -or $null -eq $document.Items -or $document.Items -isnot [Array] -or [string]$document.Revision -notmatch '^\d+$' -or [decimal]$document.Revision -ge [int]::MaxValue){throw ('Structure de données de suivi invalide : '+$name)}
    $contextId=[string]$document.Context.Id;$year=[string]$document.Context.Year
    if($contextId -cnotmatch '^[a-f0-9]{64}$' -or $contextId -cne (Get-ITMV55BackupContextId $index)){throw ('Données de suivi d''une autre base : '+$name)}
    if($year -notmatch '^\d{4}-\d{4}$' -or [int]$year.Substring(5,4) -ne [int]$year.Substring(0,4)+1){throw ('Année de données de suivi invalide : '+$name)}
    if($fileKey -cne (Get-ITMV55BackupHash ($contextId+'|'+$year))){throw ('Nom et contexte des données de suivi incohérents : '+$name)}
}
function Get-ITMV55BackupFiles([string]$folder,$index,$hashes=$null) {
    $names=@()
    if($hashes){$names=@($hashes.PSObject.Properties | Where-Object {$_.Name -clike 'scolia-v55-*'} | ForEach-Object {[string]$_.Name})}
    # Les JSON partagés ont une liste fermée ; verrous, fichiers de secours et brouillons sont exclus.
    foreach($file in @(Get-ChildItem -LiteralPath $folder -File -ErrorAction Stop | Where-Object {$_.Name -like 'scolia-v55-*.json'})){
        if($file.Name -cmatch '^scolia-v55-inscriptions-[a-f0-9]{64}\.json$'){
            if($hashes){throw ('Brouillon privé non autorisé dans une sauvegarde partagée : '+$file.Name)}
            continue
        }
        if($file.Name -cnotmatch '^scolia-v55-(presences|reinscriptions|personnel|salaires|suiviapprenants|enseignements|discipline)-[a-f0-9]{64}\.json$'){throw ('Nom de données de suivi invalide : '+$file.Name)}
        if($hashes -and $names -cnotcontains $file.Name){throw ('Données de suivi non référencées dans la sauvegarde : '+$file.Name)}
        if(-not $hashes){$names+=$file.Name}
    }
    $paths=@()
    foreach($name in @($names | Sort-Object)){
        if($name -cnotmatch '^scolia-v55-(presences|reinscriptions|personnel|salaires|suiviapprenants|enseignements|discipline)-[a-f0-9]{64}\.json$'){throw ('Nom de données de suivi invalide : '+$name)}
        $path=Join-Path $folder $name;Assert-ITMV55BackupDocument $path $index;$paths+=$path
    }
    return $paths
}
function Backup-ITMStore {
    Assert-ITMPermission 'Sauvegarder'
    if(-not $script:store){return Invoke-ITMJsonDirect 'ITMAppSauvegarde'}
    [void](Get-ITMStoreFiles $script:store)
    $featureFiles=@();$gates=@();$folder=''
    try{
        try{$gates+=[IO.File]::Open((Join-Path $script:storeFolder 'sauvegardes-entretien.lock'),[IO.FileMode]::OpenOrCreate,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None)}catch{throw 'Un nettoyage ou une autre sauvegarde est en cours. Réessayez après sa fin.'}
        $featureFiles=@(Get-ITMV55BackupFiles $script:storeFolder $script:store)
        foreach($file in $featureFiles){
            try{$gates+=[IO.File]::Open($file+'.lock',[IO.FileMode]::OpenOrCreate,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None)}catch{throw 'Enregistrement des données de suivi en cours dans une autre session. Réessayez la sauvegarde après actualisation.'}
        }
        $root=Join-Path $script:storeFolder 'sauvegardes';[void][IO.Directory]::CreateDirectory($root)
        $folder=Join-Path $root ((Get-Date -Format 'yyyyMMdd-HHmmss')+'-'+[Guid]::NewGuid().ToString('N').Substring(0,8));[void][IO.Directory]::CreateDirectory($folder)
        $hashes=@{}
        foreach($b in $script:storeBooks){$path=Join-Path $folder $b.Name;[void](Invoke-ITMExcelMethod $b 'SaveCopyAs' @([string]$path));$hashes[$b.Name]=(Get-FileHash -LiteralPath $path -Algorithm SHA256 -ErrorAction Stop).Hash}
        foreach($file in (@(Get-ITMPEBackupFiles $script:storeFolder)+$featureFiles)){
            $name=[IO.Path]::GetFileName($file);$copy=Join-Path $folder $name;[IO.File]::Copy($file,$copy,$false)
            if($featureFiles -contains $file){Assert-ITMV55BackupDocument $copy $script:store}
            $hashes[$name]=(Get-FileHash -LiteralPath $copy -Algorithm SHA256 -ErrorAction Stop).Hash
        }
        Write-ITMAtomicJson (Join-Path $folder 'registre.json') $script:store
        $hashes['registre.json']=(Get-FileHash -LiteralPath (Join-Path $folder 'registre.json') -Algorithm SHA256 -ErrorAction Stop).Hash
        $checked=$hashes | ConvertTo-Json | ConvertFrom-Json;Assert-ITMBackupManifest $script:store $checked
        Write-ITMAtomicJson (Join-Path $folder 'empreintes.json') $hashes
        return [pscustomobject]@{ok=$true;path=$folder}
    }catch{
        if($folder -and [IO.Directory]::Exists($folder)){[IO.Directory]::Delete($folder,$true)}
        throw
    }finally{foreach($gate in $gates){$gate.Dispose()}}
}
function Restore-ITMStore([string]$source) {
    Assert-ITMPermission 'Administrer'
    $maintenanceGate=[IO.File]::Open((Join-Path $script:storeFolder 'sauvegardes-entretien.lock'),[IO.FileMode]::OpenOrCreate,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None)
    try{
    $source=[IO.Path]::GetFullPath($source);$backupRoot=[IO.Path]::GetFullPath((Join-Path $script:storeFolder 'sauvegardes'))+[IO.Path]::DirectorySeparatorChar
    if(-not $source.StartsWith($backupRoot,[StringComparison]::OrdinalIgnoreCase)){throw 'Choisissez une sauvegarde complète de ce dossier data.'}
    try{$index=Get-Content -LiteralPath (Join-Path $source 'registre.json') -Raw -Encoding UTF8 -ErrorAction Stop | ConvertFrom-Json -ErrorAction Stop}catch{throw 'Registre de sauvegarde illisible.'}
    if(-not $script:store -or [string]$index.Id -cne [string]$script:store.Id){throw 'Sauvegarde d''un autre dossier de données : restauration bloquée.'}
    try{$hashes=Get-Content -LiteralPath (Join-Path $source 'empreintes.json') -Raw -Encoding UTF8 -ErrorAction Stop | ConvertFrom-Json -ErrorAction Stop}catch{throw 'Empreintes de sauvegarde illisibles.'}
    $originalFolder=$script:storeFolder;$script:storeFolder=$source
    try{$files=@(Get-ITMStoreFiles $index)}finally{$script:storeFolder=$originalFolder}
    Assert-ITMBackupManifest $index $hashes
    $files+=@(Get-ITMPEBackupFiles $source $hashes)
    $files+=@(Get-ITMV55BackupFiles $source $index $hashes)
    if($hashes.PSObject.Properties['registre.json'] -and (Get-FileHash -LiteralPath (Join-Path $source 'registre.json') -Algorithm SHA256 -ErrorAction Stop).Hash -ne $hashes.'registre.json'){throw 'Registre de sauvegarde endommagé : restauration bloquée.'}
    foreach($path in $files){
        $name=[IO.Path]::GetFileName($path)
        if(-not [IO.File]::Exists($path) -or ((Get-Item -LiteralPath $path -ErrorAction Stop).Attributes -band [IO.FileAttributes]::ReparsePoint) -or (Get-FileHash -LiteralPath $path -Algorithm SHA256 -ErrorAction Stop).Hash -ne $hashes.$name){throw 'Sauvegarde endommagée : restauration bloquée.'}
    }
    # Restauration isolée : le registre conserve son Id et les JSON leur contexte et leurs octets.
    $newFolder=Join-Path (Join-Path $script:storeRoot 'data') ([Guid]::NewGuid().ToString('N'));[void][IO.Directory]::CreateDirectory($newFolder)
    try{
        foreach($path in $files){
            $name=[IO.Path]::GetFileName($path);$copy=Join-Path $newFolder $name;[IO.File]::Copy($path,$copy,$false)
            if((Get-FileHash -LiteralPath $copy -Algorithm SHA256 -ErrorAction Stop).Hash -ne $hashes.$name){throw 'Copie de restauration incomplète.'}
        }
        Write-ITMAtomicJson (Join-Path $newFolder 'registre.json') $index
        return Join-Path $newFolder $index.Active
    }catch{if([IO.Directory]::Exists($newFolder)){[IO.Directory]::Delete($newFolder,$true)};throw}
    }finally{$maintenanceGate.Dispose()}
}
function Refresh-ITMStoragePage {
    if(-not $script:storageGrid){return}
    if(-not $script:store){$script:storageNote.Text='Connectez un classeur préparé avec cette édition. Le logiciel crée ensuite son dossier data.';$script:storageGrid.DataSource=$null;return}
    $rows=@(foreach($b in $script:storeBooks){
        $cap=[string](Invoke-ITMStockMacro $b 'ITMAppCapacite') | ConvertFrom-Json
        [pscustomobject]@{Nom=$b.Name;Etat=$(if($b.Name -eq $script:store.Active){'Actif'}else{'Historique consulté'});Apprenants=$cap.learners;Paiements=$cap.payments;Taille=[Math]::Round((Get-Item -LiteralPath $b.FullName).Length/1048576,2)}
    })
    Set-Grid $script:storageGrid $rows @(@('Nom','Classeur'),@('Etat','État'),@('Apprenants','Apprenants'),@('Paiements','Lignes paiement'),@('Taille','Mo'))
    $script:storageNote.Text='Basculement automatique · '+$script:store.LearnerLimit+' apprenants ou '+$script:store.PaymentLimit+' lignes paiement par classeur.'+"`r`n"+$script:storeFolder
    $script:storageLearnerLimit.Value=[decimal]$script:store.LearnerLimit;$script:storagePaymentLimit.Value=[decimal]$script:store.PaymentLimit
}
function Get-ITMManagedAdministration {
    $reply=Invoke-ITMJsonDirect 'ITMAppAdministration';$journal=@();$id=0
    foreach($b in $script:storeBooks){
        $macro=Get-ITMExcelMacroName $b 'ModuleGestionAvancee' 'ITMJournalLire'
        $entries=[string](Invoke-ITMExcelRunNoArguments $script:excel $macro) | ConvertFrom-Json
        foreach($entry in @($entries)){if($null -ne $entry){
            $id++
            $entry | Add-Member -NotePropertyName Source -NotePropertyValue $b.Name -Force
            $entry | Add-Member -NotePropertyName Row -NotePropertyValue $id -Force
            $journal+=,$entry
        }}
    }
    $backups=@();$root=Join-Path $script:storeFolder 'sauvegardes'
    if([IO.Directory]::Exists($root)){
        $backups=@(Get-ChildItem -LiteralPath $root -Directory | Where-Object {[IO.File]::Exists((Join-Path $_.FullName 'empreintes.json'))} | ForEach-Object {[pscustomobject]@{Nom=$_.Name;Date=$_.LastWriteTime.ToString('yyyy-MM-dd HH:mm:ss');Chemin=$_.FullName;Taille=0}})
    }
    $reply.journal=$journal;$reply.backups=$backups;return $reply
}
function Invoke-ITMManagedClosure([object[]]$parameters) {
    Assert-ITMPermission 'Administrer'
    $stage=$null;$path='';$active=$script:book;$tx=$false
    try {
        $backup=Backup-ITMStore
        $stage=New-ITMConsolidation;$path=[string]$stage.FullName
        Start-ITMStoreTransaction;$tx=$true;$script:book=$stage
        $script:excel.AutomationSecurity=1
        try{$reply=Invoke-ITMJsonDirect 'ITMAppCloture' $parameters}finally{$script:excel.AutomationSecurity=3}
        $script:book=$active
        foreach($b in $script:storeBooks){[void](Invoke-ITMStockMacro $b 'ITMSetProperty' @($b,'ITM_AnneeCloturee',$true))}
        $script:store.Closed=$true;Complete-ITMStoreTransaction;$tx=$false;$script:committedThisAction=$true
        $reply.archive=$backup.path;return $reply
    }catch{if($tx){$script:book=$active;Undo-ITMStoreTransaction;$active=$script:book};throw}finally{
        $script:book=$active;if($stage){[void]$stage.Close($false)}
        if($path -and [IO.File]::Exists($path)){[IO.File]::Delete($path)}
    }
}
function Build-ITMStoragePage($page) {
    [void](New-Label $page 'Classeurs et dossier de données' 24 20 940 38 18 $true)
    $script:storageNote=New-Label $page 'Connectez un classeur préparé avec cette édition.' 24 70 940 70 11
    $script:storageNext=New-Button $page 'Créer le classeur suivant' 24 160 290 $true
    $open=New-Button $page 'Ouvrir le dossier data' 330 160 260
    $check=New-Button $page 'Contrôler les classeurs' 606 160 310
    $script:storageNext.Add_Click({Do-Work {New-ITMSegment;Refresh-Data;$script:status.Text='Classeur suivant créé. Toutes les données restent consultables.'} 'Création du classeur suivant...'})
    $open.Add_Click({try{if($script:storeFolder){Start-Process -FilePath 'explorer.exe' -ArgumentList ('"'+$script:storeFolder+'"')}else{throw 'Aucun dossier data connecté.'}}catch{App-Error $_}})
    $check.Add_Click({Do-Work {
        if(-not $script:store){throw 'Aucun dossier data connecté.'}
        [void](Get-ITMStoreFiles $script:store)
        foreach($b in $script:storeBooks){
            $macro=Get-ITMExcelMacroName $b 'ModuleFiabilite' 'ITMVerifierInstallation'
            if(-not (Invoke-ITMExcelRunNoArguments $script:excel $macro)){throw ('Structure invalide : '+$b.Name)}
        }
        Refresh-Data;$script:status.Text='Tous les classeurs sont présents. Structures et données actualisées ; consultez Diagnostic pour les anomalies.'
    } 'Contrôle des classeurs...'})
    [void](New-Label $page 'Seuil apprenants par classeur' 24 222 360 24 10 $true)
    [void](New-Label $page 'Seuil lignes de paiement par classeur' 400 222 470 24 10 $true)
    $script:storageLearnerLimit=New-Object Windows.Forms.NumericUpDown;$script:storageLearnerLimit.SetBounds(24,252,260,32);$script:storageLearnerLimit.Minimum=100;$script:storageLearnerLimit.Maximum=100000;$script:storageLearnerLimit.Increment=100;$script:storageLearnerLimit.Value=10000;$page.Controls.Add($script:storageLearnerLimit)
    $script:storagePaymentLimit=New-Object Windows.Forms.NumericUpDown;$script:storagePaymentLimit.SetBounds(400,252,260,32);$script:storagePaymentLimit.Minimum=100;$script:storagePaymentLimit.Maximum=200000;$script:storagePaymentLimit.Increment=100;$script:storagePaymentLimit.Value=50000;$page.Controls.Add($script:storagePaymentLimit)
    $script:storageSave=New-Button $page 'Enregistrer les seuils' 682 246 275
    $script:storageSave.Add_Click({Do-Work {
        Assert-ITMPermission 'Administrer'
        if(-not $script:store){throw 'Aucun dossier data connecté.'}
        $oldLearners=$script:store.LearnerLimit;$oldPayments=$script:store.PaymentLimit
        try{$script:store.LearnerLimit=[int]$script:storageLearnerLimit.Value;$script:store.PaymentLimit=[int]$script:storagePaymentLimit.Value;Write-ITMAtomicJson (Join-Path $script:storeFolder 'registre.json') $script:store}catch{$script:store.LearnerLimit=$oldLearners;$script:store.PaymentLimit=$oldPayments;throw}
        Refresh-ITMStoragePage;$script:status.Text='Seuils enregistrés. Le prochain enregistrement basculera si nécessaire.'
    }})
    $script:storageCleanup=New-Button $page 'Nettoyer les sauvegardes' 24 310 290
    $script:storageCleanup.Destructive=$true;$script:storageCleanup.Add_Click({try{Show-ITMBackupCleanup}catch{App-Error $_}})
    $script:storageGrid=New-Grid $page 24 370 940 290
}

function Get-ITMDuplicateLearners($learners) {
    $seen=@{}
    foreach($learner in @($learners)){
        $parts=@((Get-ITMIdentityPart $learner.Nom),(Get-ITMIdentityPart $learner.PostNom),(Get-ITMIdentityPart $learner.Prenom),(Promo $learner.Promotion))
        if(@($parts | Where-Object {[string]::IsNullOrWhiteSpace($_)}).Count){continue}
        $key=$parts -join '|'
        if($seen.ContainsKey($key)){
            [pscustomobject]@{Matricule=$learner.Matricule;AutreMatricule=$seen[$key];Nom=($learner.Nom+' '+$learner.PostNom+' '+$learner.Prenom);Observation=('Identité et promotion identiques : '+$learner.Promotion+'. Vérifiez les deux dossiers.')}
        }else{$seen[$key]=$learner.Matricule}
    }
}
