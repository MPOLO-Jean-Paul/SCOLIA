$ErrorActionPreference='Stop'
function Import-V76([string]$File,[string]$Name){
 $tokens=$null;$errors=$null
 $ast=[Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot $File),[ref]$tokens,[ref]$errors)
 if($errors.Count){throw ($errors | Out-String)}
 $node=$ast.Find({param($n)$n -is [Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -eq $Name},$true)
 if(-not $node){throw $Name}
 . ([scriptblock]::Create('function global:'+$Name+$node.Extent.Text.Substring(('function '+$Name).Length)))
}
function Check-V76([bool]$Ok,[string]$Message){if(-not $Ok){throw $Message}}
foreach($n in @('Save-ITMStoreBookIfDirty','Complete-ITMStoreTransaction','Write-ITMAtomicJson')){Import-V76 'StockageClasseurs.ps1' $n}
function Resolve-ITMLiveWorkbook($Book){return $Book}
function Get-ITMExcelProperty($Book,[string]$Name){if($script:propertyFailure){throw 'COM indisponible'};return $Book.$Name}
function Invoke-ITMExcelMethod($Book,[string]$Name){
 if($Name -ne 'Save'){throw $Name};$script:saveCount++
 if($script:saveFailure){throw 'Disque indisponible'}
 [IO.File]::WriteAllText($Book.FullName,'saved-content');$Book.Saved=$true
}
$folder=Join-Path ([IO.Path]::GetTempPath()) ('scolia-v76-'+[guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($folder)
try{
 $script:storeFolder=$folder;$script:store=[pscustomobject]@{Id='test';Segments=@('test.xlsm')}
 $script:storeLock=[IO.File]::Open((Join-Path $folder 'session.lock'),'OpenOrCreate','ReadWrite','None')
 $book=[pscustomobject]@{Name='test.xlsm';FullName=(Join-Path $folder 'test.xlsm');Saved=$true}
 [IO.File]::WriteAllText($book.FullName,'original-content')
 $script:saveCount=0;Save-ITMStoreBookIfDirty $book
 Check-V76 ($script:saveCount -eq 0) 'Classeur déjà sauvegardé réécrit'
 $book.Saved=$false;Save-ITMStoreBookIfDirty $book
 Check-V76 ($script:saveCount -eq 1 -and $book.Saved) 'Classeur modifié non sauvegardé'
 $script:excelBorrowed=$true;Save-ITMStoreBookIfDirty $book
 Check-V76 ($script:saveCount -eq 2) 'Classeur partagé non sauvegardé'
 $script:excelBorrowed=$false;$script:propertyFailure=$true;Save-ITMStoreBookIfDirty $book
 Check-V76 ($script:saveCount -eq 3) 'Échec COM traité comme un succès'
 $script:propertyFailure=$false;[IO.File]::Delete($book.FullName);Save-ITMStoreBookIfDirty $book
 Check-V76 ($script:saveCount -eq 4 -and [IO.File]::Exists($book.FullName)) 'Fichier manquant ignoré'
 $pending=Join-Path $folder 'transaction.json';[IO.File]::WriteAllText($pending,'recovery')
 $book.Saved=$false;$script:saveFailure=$true;$failed=$false
 try{Complete-ITMStoreTransaction @($book)}catch{$failed=$true}
 Check-V76 ($failed -and [IO.File]::Exists($pending)) 'Échec de sauvegarde sans marqueur de récupération'
 Check-V76 (-not [IO.File]::Exists((Join-Path $folder 'registre.json'))) 'Registre validé avant sauvegarde'
 $script:saveFailure=$false;Complete-ITMStoreTransaction @($book,$book)
 Check-V76 (-not [IO.File]::Exists($pending) -and [IO.File]::Exists((Join-Path $folder 'registre.json'))) 'Transaction non terminée'
 $script:storeLock.Dispose();$script:storeLock=$null;$count=$script:saveCount;Save-ITMStoreBookIfDirty $book
 Check-V76 ($script:saveCount -eq $count+1) 'Optimisation active sans verrou exclusif'
 'PASS sauvegarde conditionnelle, repli COM, fichier absent, écriture en échec, journal de récupération et validation'
}finally{if($script:storeLock){$script:storeLock.Dispose();$script:storeLock=$null};[IO.Directory]::Delete($folder,$true)}

Import-V76 'PerformanceInterface.ps1' 'Update-ITMAfterWrite'
function Key($Value){return ([string]$Value).ToUpperInvariant()}
function Invoke-ITMJsonDirect {param($Name,$Arguments);$script:reads++;if($script:readFailure){throw 'Read failed'};return [pscustomobject]@{ok=$true;matricule=$Arguments[0]}}
function Apply-ITMLearnerSlice {param($Slice);$script:applied++;if(-not $Slice.ok){throw 'invalid'}}
function Refresh-Data {param($Defer);$script:fullReads++}
function Refresh-ITMCurrentPage {$script:views++;if($script:viewFailure){throw 'UI failure'}}
function Update-WorkspaceState {$script:states++}
$script:reads=0;$script:fullReads=0;$script:applied=0;$script:views=0;$script:states=0;$script:busy=$true
$script:lastLearnerSlice=[pscustomobject]@{ok=$true;matricule='A1'}
Check-V76 (Update-ITMAfterWrite 'A1' $true) 'Tranche existante refusée'
Check-V76 ($script:reads -eq 0 -and $script:fullReads -eq 0 -and $script:views -eq 0 -and $script:states -eq 0) 'Travail répété dans le lot'
Check-V76 (Update-ITMAfterWrite 'A1' $true) 'Lecture ciblée refusée'
Check-V76 ($script:reads -eq 1) 'Lecture ciblée non unique'
$script:readFailure=$true
Check-V76 (-not (Update-ITMAfterWrite 'A1' $true)) 'Échec non signalé'
Check-V76 ($script:fullReads -eq 1) 'Repli complet manquant'
$script:readFailure=$false;$script:viewFailure=$true;$failed=$false;$before=$script:fullReads
try{[void](Update-ITMAfterWrite 'A1')}catch{$failed=$true}
Check-V76 ($failed -and $script:fullReads -eq $before) 'Erreur visuelle déclenchant une relecture Excel'
$script:viewFailure=$false;$script:busy=$false
[void](Update-ITMAfterWrite 'A1' $true)
Check-V76 ($script:states -eq 1) 'État hors lot non actualisé'
'PASS tranche réutilisée, lecture ciblée unique, repli, séparation données/affichage et lots'

Import-V76 'PerformanceInterface.ps1' 'Update-ITMLearnerIndexForSlice'
function Rebuild-ITMLearnerIndex {$script:rebuilds++;$script:dataRevision++}
function Sync-ScoliaDisciplineSnapshot {$script:discipline++;$script:data.learners[0].Statut='Suspendu';return $true}
$old=[pscustomobject]@{Matricule='A1';Promotion='INF1';Nom='Alpha';PostNom='Beta';Prenom='Gamma';Statut='Actif'}
$new=$old.PSObject.Copy();$script:learnerIndex=@([pscustomobject]@{Entry=$old;Text='A1 ALPHA BETA GAMMA INF1';Promotion='INF1'})
$script:data=[pscustomobject]@{learners=@($new)};$script:rebuilds=0;$script:discipline=0;$script:dataRevision=1
Update-ITMLearnerIndexForSlice 'A1'
Check-V76 ($script:rebuilds -eq 0 -and $script:discipline -eq 1 -and $script:dataRevision -eq 2) 'Index entièrement reconstruit pour un paiement'
Check-V76 ([object]::ReferenceEquals($script:learnerIndex[0].Entry,$new) -and $script:learnerIndex[0].Entry.Statut -eq 'Suspendu') 'Dossier ou discipline obsolète'
$changed=$new.PSObject.Copy();$changed.Nom='Zulu';$script:data.learners=@($changed)
Update-ITMLearnerIndexForSlice 'A1'
Check-V76 ($script:rebuilds -eq 1) 'Nom modifié sans nouveau tri'
$script:data.learners=@($changed,$old);Update-ITMLearnerIndexForSlice 'A1'
Check-V76 ($script:rebuilds -eq 2) 'Ajout non pris en compte'
'PASS index ciblé, changement de nom, ajout et discipline'
