param([Parameter(Mandatory=$true)]$Excel,[Parameter(Mandatory=$true)]$Reference,[scriptblock]$Progress={param($message)})
# Integration tests run ONLY on temporary copies during installation.
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'StockageClasseurs.ps1')
. (Join-Path $PSScriptRoot 'MiseAJourPerformance.ps1')
. (Join-Path $PSScriptRoot 'FonctionsGestion.ps1')
. (Join-Path $PSScriptRoot 'ExploitationProfessionnelle.ps1')
$script:accountSession=[pscustomobject]@{Kind='Test';Id='test';Name='Test stockage';Role='Administrateur'}
function Key($value){return ([string]$value).Trim().ToUpperInvariant()}
function Promo($value){return Key $value}
function Api-Name([string]$name){return "'"+$script:book.Name.Replace("'","''")+"'!ModuleApplicationITM."+$name}
function Save-ConnectionChoice([string]$path){return $true} # Never change the user's connection preference.
function Assert-Stock([bool]$condition,[string]$message){if(-not $condition){throw ('Test stockage : '+$message)}}
$testRoot=Join-Path ([IO.Path]::GetTempPath()) ('ITM-tests-'+[Guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($testRoot)
$script:storeRoot=$testRoot;$script:excel=$Excel
try {
    $script:storeFolder=Join-Path $testRoot 'base';[void][IO.Directory]::CreateDirectory($script:storeFolder)
    & $Progress 'Tests approfondis : creation des copies temporaires...'
    $path=Join-Path $script:storeFolder 'classeur-000001.xlsm'
    $Excel.AutomationSecurity=1
    try{$reply=[string](Invoke-ITMStockMacro $Reference 'ITMAppSegment' @($path)) | ConvertFrom-Json}finally{$Excel.AutomationSecurity=3}
    Assert-Stock ([bool]$reply.ok) 'création de copie temporaire'
    $script:book=$Excel.Workbooks.Item('classeur-000001.xlsm');$script:storeBooks=@($script:book)
    $script:store=[pscustomobject]@{Version=1;Id=[Guid]::NewGuid().ToString('N');Created=(Get-Date -Format o);Origin='TEST';Active='classeur-000001.xlsm';Segments=@('classeur-000001.xlsm');LearnerLimit=100;PaymentLimit=100;Closed=$false}
    Set-ITMStoreProperties
    try{$script:book.CustomDocumentProperties.Item('ITM_AnneeCloturee').Delete()}catch{}
    $w=$script:book.Worksheets.Item('PROMOTIONS');$w.Range('A5:H1000').ClearContents();$w.Range('A5:H5').Value2=[object[]]@('TST1','TEST','','','2026-2027',10000,90000,'ACTIVE')
    $learners=[Array]::CreateInstance([object],100,15);$payments=[Array]::CreateInstance([object],100,14)
    for($i=0;$i -lt 100;$i++){
        $mat='{0:D6}' -f ($i+1)
        $values=@($mat,'TEST','DOSSIER','ALPHA','M',([DateTime]::new(2000,1,1).AddDays($i).ToOADate()),'LUBUMBASHI','TST1','PARENT','0990000000','',(Get-Date).ToOADate(),'2026-2027','Actif','ADRESSE')
        for($j=0;$j -lt 15;$j++){$learners[$i,$j]=$values[$j]}
        $values=@(($i+1),(Get-Date).Date.ToOADate(),('S{0:D4}' -f ($i+1)),$mat,'TEST DOSSIER ALPHA','TST1','Septembre',10000,1000,9000,'LITIGE',1000,90000,89000)
        for($j=0;$j -lt 14;$j++){$payments[$i,$j]=$values[$j]}
    }
    $w=$script:book.Worksheets.Item('APPRENANTS');$w.Range('A5:O104').NumberFormat='@';$w.Range('A5:O104').Value2=$learners
    $w.Range('F5:F104').NumberFormat='dd/mm/yyyy';$w.Range('L5:L104').NumberFormat='dd/mm/yyyy';$w.ListObjects.Item(1).Resize($w.Range('A4:O104'))
    $w=$script:book.Worksheets.Item('PAIEMENTS');$w.Range('C5:G104').NumberFormat='@';$w.Range('A5:N104').Value2=$payments;$w.Range('B5:B104').NumberFormat='dd/mm/yyyy';$w.ListObjects.Item(1).Resize($w.Range('A4:N104'))
    $script:book.Save();Write-ITMAtomicJson (Join-Path $script:storeFolder 'registre.json') $script:store
    $script:data=Invoke-ITMJsonDirect 'ITMAppSnapshot'
    & $Progress 'Tests approfondis : verrou termine et lectures par blocs...'
    Assert-Stock ((@($script:data.learners)[0].Row) -eq 5) 'numero de ligne preserve dans la lecture par blocs'
    Assert-Stock ((@($script:data.learners)[0].Naissance) -eq '01/01/2000') 'date de naissance preservee'
    # Simulate an abandoned lock with NO edited cells: Excel still reports Saved=True.
    $macro="'"+$script:book.Name.Replace("'","''")+"'!ModuleFiabilite.ITMDebutSaisie"
    [void]$Excel.Run($macro,$script:book.Worksheets.Item('APPRENANTS'))
    # No cells were edited in this fixture; ignore the global calculation-mode dirty flag.
    $script:book.Saved=$true
    & $Progress 'Tests approfondis : basculement des inscriptions...'
    $result=[string](Invoke-ITMDossier @('','TEST','NOUVEAU','BETA','F','01/01/2006','LUBUMBASHI','PARENT','0990000000','','TST1','2026-2027','Actif','ADRESSE')) | ConvertFrom-Json
    Assert-Stock ([bool]$result.ok -and $script:store.Segments.Count -eq 2) 'basculement automatique des inscriptions'
    $script:data=Invoke-ITMJsonDirect 'ITMAppSnapshot'
    Assert-Stock ($script:data.learners.Count -eq 101) 'lecture commune des 101 dossiers'
    $result=[string](Invoke-ITMPayment '000001' 9000 (Get-Date -Format 'yyyy-MM-dd')) | ConvertFrom-Json
    Assert-Stock ([bool]$result.ok) 'paiement dans un autre segment'
    $script:data=Invoke-ITMJsonDirect 'ITMAppSnapshot'
    $sum=@($script:data.balances | Where-Object {$_.Matricule -eq '000001' -and $_.Mois -eq 'SEPTEMBRE'})
    Assert-Stock ($sum.Count -eq 1 -and [decimal]$sum[0].Verse -eq 10000) 'solde cumulé après basculement'
    & $Progress 'Tests approfondis : correction entre plusieurs classeurs...'
    $preview=Invoke-ITMManagedCorrection @('S0001',2000,'TEST CORRECTION',$false)
    Assert-Stock ([bool]$preview.ok) 'aperçu de correction inter-classeurs'
    $corrected=Invoke-ITMManagedCorrection @('S0001',2000,'TEST CORRECTION',$true)
    Assert-Stock ([bool]$corrected.ok) 'application de correction inter-classeurs'
    $script:data=Invoke-ITMJsonDirect 'ITMAppSnapshot'
    $total=($script:data.balances | Where-Object {$_.Matricule -eq '000001'} | Measure-Object Verse -Sum).Sum
    Assert-Stock ($total -eq 11000) 'historique recalculé sans double comptage'
    & $Progress 'Tests approfondis : capacite des paiements et export global...'
    Ensure-ITMCapacity 'payments' 100
    Assert-Stock ($script:store.Segments.Count -eq 3) 'basculement de capacité des paiements'
    $preview=Invoke-ITMJsonDirect 'ITMAppVerifierRapport' @('','',3)
    Assert-Stock ([int]$preview.rows -eq 101) 'export global des trois segments'
    & $Progress 'Tests approfondis : annulation et recuperation...'
    Start-ITMStoreTransaction
    $script:book.Worksheets.Item('APPRENANTS').Cells.Item(5,1).Value2='ERREUR';$script:book.Save()
    Undo-ITMStoreTransaction
    $script:data=Invoke-ITMJsonDirect 'ITMAppSnapshot'
    Assert-Stock ($script:data.learners.Count -eq 101) 'annulation et récupération complète'
    & $Progress 'Tests approfondis : recuperation du registre illisible ou absent...'
    foreach($damage in @('illisible','absent')){
        Start-ITMStoreTransaction
        $script:book.Worksheets.Item('APPRENANTS').Cells.Item(5,1).Value2='ERREUR';$script:book.Save()
        foreach($b in $script:storeBooks){$b.Close($false)}
        $script:storeBooks=@();$script:book=$null
        if($script:storeLock){$script:storeLock.Dispose();$script:storeLock=$null}
        $indexPath=Join-Path $script:storeFolder 'registre.json'
        if($damage -eq 'illisible'){[IO.File]::WriteAllText($indexPath,'{illisible')}else{[IO.File]::Delete($indexPath)}
        Initialize-ITMStore $path
        $script:data=Invoke-ITMJsonDirect 'ITMAppSnapshot'
        Assert-Stock ($script:data.learners.Count -eq 101) ('reconnexion apres registre '+$damage)
        Assert-Stock (@($script:data.learners | Where-Object {$_.Matricule -eq '000001'}).Count -eq 1) ('identite restauree apres registre '+$damage)
        $total=($script:data.balances | Where-Object {$_.Matricule -eq '000001'} | Measure-Object Verse -Sum).Sum
        Assert-Stock ($total -eq 11000) ('paiements preserves apres registre '+$damage)
    }
    & $Progress 'Tests approfondis : sauvegarde et restauration...'
    $backup=Backup-ITMStore;$restored=Restore-ITMStore $backup.path
    Assert-Stock ([IO.File]::Exists($restored)) 'sauvegarde vérifiée et restauration vers un autre dossier'
    & $Progress 'Tests approfondis : reouverture de la base restauree...'
    $previous=$script:storeFolder
    foreach($b in $script:storeBooks){$b.Close($false)}
    $script:storeFolder=[IO.Path]::GetDirectoryName($restored);$script:store=Get-Content -LiteralPath (Join-Path $script:storeFolder 'registre.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    Open-ITMStoreBooks;$script:data=Invoke-ITMJsonDirect 'ITMAppSnapshot'
    Assert-Stock ($script:data.learners.Count -eq 101) 'réouverture de la base restaurée'
    & $Progress 'Tests approfondis : persistance, 24 heures et identite manquante...'
    $before=[string](Invoke-ITMStockMacro $script:book 'ITMAppCapacite')
    foreach($b in $script:storeBooks){
        Assert-Stock ((Get-ITMPersistedIdentity $b.FullName) -eq [string]$script:store.Id) ('identite sauvegardee sur disque : '+$b.Name)
        $b.Close($false)
    }
    Open-ITMStoreBooks
    Assert-Stock (([string](Invoke-ITMStockMacro $script:book 'ITMAppCapacite')) -eq $before) 'fermeture et reouverture sans preparation ni perte de lignes'
    $first=$script:storeBooks[0]
    $first.CustomDocumentProperties.Item('ITM_DataSet').Delete();$first.Save()
    foreach($b in $script:storeBooks){$b.Close($false)}
    Open-ITMStoreBooks
    Assert-Stock ((Get-ITMPersistedIdentity $script:storeBooks[0].FullName) -eq [string]$script:store.Id) 'identite absente reparee automatiquement et conservee sur disque'
    $script:data=Invoke-ITMJsonDirect 'ITMAppSnapshot'
    Assert-Stock ($script:data.learners.Count -eq 101) 'les 101 dossiers subsistent apres reparation'
    Assert-Stock (([decimal](($script:data.balances | Where-Object {$_.Matricule -eq '000001'} | Measure-Object Verse -Sum).Sum)) -eq 11000) 'paiements preserves apres reparation'
    Assert-Stock (@(Get-ChildItem -LiteralPath $script:storeFolder -Directory -Filter 'transaction-*').Count -gt 0) 'sauvegarde avant reparation conservee'
    $script:store.Preparation.VerifiedUtc=[DateTimeOffset]::UtcNow.AddHours(-25).ToString('o')
    Write-ITMAtomicJson (Join-Path $script:storeFolder 'registre.json') $script:store
    foreach($b in $script:storeBooks){$b.Close($false)}
    Open-ITMStoreBooks
    Assert-Stock (Test-ITMPreparationCurrent $script:store) 'renouvellement automatique apres 24 heures'
    $indexOnDisk=Get-Content -LiteralPath (Join-Path $script:storeFolder 'registre.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    Assert-Stock (Test-ITMPreparationCurrent $indexOnDisk) 'validation renouvelee persistee dans le registre'
    $goodId=[string]$script:store.Id
    Set-ITMBookProperty $script:storeBooks[-1] 'ITM_DataSet' 'bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb';$script:storeBooks[-1].Save()
    foreach($b in $script:storeBooks){$b.Close($false)}
    $blocked=$false
    try{Open-ITMStoreBooks}catch{$blocked=$true}
    Assert-Stock $blocked 'identite etrangere refusee'
    Assert-Stock ((Get-ITMPersistedIdentity $script:storeBooks[-1].FullName) -eq 'bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb') 'identite etrangere jamais ecrasee'
    Set-ITMBookProperty $script:storeBooks[-1] 'ITM_DataSet' $goodId;$script:storeBooks[-1].Save()
    return $true
} finally {
    $toClose=@(foreach($b in $Excel.Workbooks){if(([string]$b.FullName).StartsWith($testRoot,[StringComparison]::OrdinalIgnoreCase)){$b}})
    foreach($b in $toClose){try{$b.Close($false)}catch{}}
    Close-ITMStore
    if([IO.Directory]::Exists($testRoot)){[IO.Directory]::Delete($testRoot,$true)}
}
