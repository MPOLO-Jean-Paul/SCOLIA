# Métier et persistance réels sur données isolées, sans Excel ni interface.
$ErrorActionPreference='Stop'
# Use the production dispatcher against the fake workbooks in these isolated tests.
$transportTokens=$null;$transportErrors=$null
$transportAst=[Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot 'PasserelleExcel.ps1'),[ref]$transportTokens,[ref]$transportErrors)
$transportFn=$transportAst.Find({param($n)$n -is [Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -eq 'Invoke-ITMExcelMethod'},$true)
. ([scriptblock]::Create($transportFn.Extent.Text))

foreach($name in @('FonctionnalitesV55.ps1','CentreRechercheV55.ps1','PersonnelV56.ps1','PresencesV55.ps1','EnseignementsV59.ps1')){. (Join-Path $PSScriptRoot $name)}
$tokens=$null;$errors=$null;$ast=[Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot 'StockageClasseurs.ps1'),[ref]$tokens,[ref]$errors)
foreach($name in @('Write-ITMAtomicJson','Get-ITMV55BackupHash','Get-ITMV55BackupContextId','Assert-ITMV55BackupDocument','Get-ITMV55BackupFiles','Assert-ITMBackupManifest','Get-ITMStoreFiles','Get-ITMPEBackupFiles','Backup-ITMStore','Restore-ITMStore')){$node=$ast.Find({param($n)$n -is [Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -eq $name},$true);. ([scriptblock]::Create($node.Extent.Text))}
$script:checks=0
function Check([bool]$valid,[string]$message){if(-not $valid){throw $message};$script:checks++}
function Reject([scriptblock]$action,[string]$message){$failed=$false;try{[void](& $action)}catch{$failed=$true};Check $failed $message}
function Assert-ITMPermission([string]$permission){if($permission -eq 'Saisie' -and $script:deny){throw 'Accès refusé.'}}
function Test-ScoliaPermission([string]$permission){return -not $script:accounting}
$root=Join-Path ([IO.Path]::GetTempPath()) ('SCOLIA-cours-'+[guid]::NewGuid().ToString('N'));[void][IO.Directory]::CreateDirectory($root)
try{
 $script:storeFolder=$root;$script:store=[pscustomobject]@{Id=[guid]::NewGuid().ToString('N');Segments=@()};$script:book=[pscustomobject]@{FullName=(Join-Path $root 'test.xlsm')}
 $script:scolia=[pscustomobject]@{Id=[guid]::NewGuid().ToString('N');StartMonth=9};$script:accountSession=[pscustomobject]@{Id='secretary';Name='Secrétaire test';Role='Secrétariat'}
 $script:data=[pscustomobject]@{closed=$false;promotions=@([pscustomobject]@{Code='INF1';Active=$true;Annee='2026-2027'},[pscustomobject]@{Code='INF2';Active=$true;Annee='2026-2027'})}
 $context=Get-ScoliaFeatureContext;$actor='Secrétaire test';$script:deny=$false;$script:accounting=$false
 $personnel=[pscustomobject]@{Schema=1;Context=$context;Revision=0;Items=@()}
 $t1=New-ScoliaTeacher -Nom 'Enseignant Un' -ActiveFrom '2026-09-01' -Actor $actor
 $t2=New-ScoliaTeacher -Nom 'Enseignant Deux' -ActiveFrom '2026-09-01' -Actor $actor
 $old=New-ScoliaTeacher -Nom 'Ancien enseignant' -ActiveFrom '2025-09-01' -ActiveTo '2026-08-01' -Actor $actor
 $personnel.Items=@($t1,$t2,$old);Write-ITMAtomicJson (Get-ScoliaFeatureStorePath personnel) $personnel
 $document=[pscustomobject]@{Schema=1;Context=$context;Revision=0;Items=@()};$promos=@('INF1','INF2')
 $c1=New-ScoliaCourse 'INF1' 1 'Anatomie' 30.5 20
 $c2=New-ScoliaCourse 'INF1' 2 'Physiologie' 45 40
 $c3=New-ScoliaCourse 'INF2' 1 'Anatomie' 60 60
 foreach($course in @($c1,$c2,$c3)){Set-ScoliaCourse $document $personnel $course 0 $promos $actor}
 Check (@($document.Items).Count -eq 3) 'Cours par classe non conservés'
 $before=$document|ConvertTo-Json -Depth 20 -Compress
 Reject {Set-ScoliaCourse $document $personnel (New-ScoliaCourse 'INF1' 1 'Autre' 10 20) 0 $promos $actor} 'Numéro dupliqué accepté'
 Reject {Set-ScoliaCourse $document $personnel (New-ScoliaCourse 'INF1' 3 '  anatomie  ' 10 20) 0 $promos $actor} 'Nom du cours dupliqué accepté'
 Reject {Set-ScoliaCourse $document $personnel (New-ScoliaCourse 'INF9' 1 'Autre' 10 20) 0 $promos $actor} 'Promotion inconnue acceptée'
 Reject {New-ScoliaCourse 'INF1' 0 'Cours' 10 20} 'Numéro nul accepté'
 Reject {New-ScoliaCourse 'INF1' 3 'Cours' 0 20} 'Volume nul accepté'
 Reject {New-ScoliaCourse 'INF1' 3 'Cours' 10 -1} 'Cotation négative acceptée'
 Reject {New-ScoliaCourse 'INF1' 3 '' 10 20} 'Nom vide accepté'
 Check (($document|ConvertTo-Json -Depth 20 -Compress) -ceq $before) 'Validation échouée ayant modifié les données'
 foreach($course in @($c1,$c2,$c3)){Set-ScoliaCourseAssignment -Document $document -Personnel $personnel -CourseId $course.Id -TeacherId $t1.Id -ExpectedRevision 0 -Actor $actor}
 $summary=@(Get-ScoliaTeacherTeachingSummary $document $personnel|Where-Object Key -eq $t1.Id)[0]
 Check ($summary.Courses -eq 3 -and $summary.Promotions -eq 2 -and $summary.Hours -eq 135.5) 'Plusieurs cours/classes : total horaire incorrect'
 Check (@(Get-ScoliaTeacherTeachingSummary $document $personnel|Where-Object {$_.Key -eq $t2.Id -and $_.Hours -eq 0 -and $_.Courses -eq 0}).Count -eq 1) 'Enseignant sans cours absent du tableau de bord'
 $before=$document|ConvertTo-Json -Depth 20 -Compress
 Reject {Set-ScoliaCourseAssignment -Document $document -Personnel $personnel -CourseId $c1.Id -TeacherId $t2.Id -ExpectedRevision 0 -Actor $actor} 'Deux enseignants acceptés pour un même cours/classe'
 Check (($document|ConvertTo-Json -Depth 20 -Compress) -ceq $before) 'Doublon modifiant une attribution'
 $assignment=@($document.Items|Where-Object {$_.Kind -eq 'Assignment' -and $_.CourseId -eq $c1.Id})[0]
 Set-ScoliaCourseAssignment -Document $document -Personnel $personnel -CourseId $c1.Id -TeacherId $t2.Id -ExpectedRevision 0 -Actor $actor -Id $assignment.Id
 $assignment=@($document.Items|Where-Object Id -eq $assignment.Id)[0]
 Check ($assignment.Audit.Count -eq 2 -and $assignment.Audit[1].Before.TeacherId -eq $t1.Id) 'Réattribution sans historique'
 Check (@(Get-ScoliaTeacherTeachingSummary $document $personnel|Where-Object {$_.Key -eq $t1.Id -and $_.Hours -eq 105}).Count -eq 1) 'Ancien enseignant conserve une charge retirée'
 Check (@(Get-ScoliaTeacherTeachingSummary $document $personnel|Where-Object {$_.Key -eq $t2.Id -and $_.Hours -eq 30.5}).Count -eq 1) 'Nouvel enseignant ne reçoit pas le volume'
 $archived=New-ScoliaCourse 'INF1' 1 'Anatomie' 30.5 20 -Id $c1.Id -Active $false
 Reject {Set-ScoliaCourse $document $personnel $archived 0 $promos $actor} 'Cours attribué archivé'
 Set-ScoliaCourseAssignment -Document $document -Personnel $personnel -CourseId $c1.Id -TeacherId $t2.Id -ExpectedRevision 0 -Actor $actor -Id $assignment.Id -Active $false
 Set-ScoliaCourse $document $personnel $archived 0 $promos $actor
 Check (@(Get-ScoliaTeachingRows $document $personnel).Count -eq 2 -and @(Get-ScoliaTeachingRows $document $personnel -IncludeArchived).Count -eq 3) 'Archive détruite ou encore active'
 Reject {Set-ScoliaCourseAssignment -Document $document -Personnel $personnel -CourseId $c1.Id -TeacherId $t1.Id -ExpectedRevision 0 -Actor $actor} 'Cours archivé attribué'
 Reject {Set-ScoliaCourseAssignment -Document $document -Personnel $personnel -CourseId $c2.Id -TeacherId $old.Id -ExpectedRevision 0 -Actor $actor} 'Enseignant hors année accepté'
 Reject {Set-ScoliaCourse $document $personnel $c2 1 $promos $actor} 'Révision périmée acceptée'
 # Persistance atomique et isolation : les opérations ci-dessous exécutent le vrai service.
 $course=New-ScoliaCourse 'INF1' 1 'Soins infirmiers' 25 20
 [void](Invoke-ScoliaTeachingUpdate -ExpectedRevision 0 -Change {param($d,$p,$classes)Set-ScoliaCourse $d $p $course 0 $classes $actor})
 $saved=Read-ScoliaTeachingState;$path=Get-ScoliaFeatureStorePath enseignements;$hash=(Get-FileHash $path).Hash
 Check ($saved.Document.Revision -eq 1 -and $saved.Document.Items[0].Hours -eq 25) 'Cours non conservé après relecture'
 Reject {Invoke-ScoliaTeachingUpdate -ExpectedRevision 0 -Change {throw 'Ne doit pas être exécuté'}} 'Concurrence non bloquée'
 Check ((Get-FileHash $path).Hash -ceq $hash) 'Révision périmée écrase le fichier'
 $gate=[IO.File]::Open($path+'.lock','OpenOrCreate','ReadWrite','None')
 try{Reject {Invoke-ScoliaTeachingUpdate -ExpectedRevision 1 -Change {}} 'Verrou ignoré'}finally{$gate.Dispose()}
 $script:deny=$true;Reject {Invoke-ScoliaTeachingUpdate -ExpectedRevision 1 -Change {}} 'Écriture sans droits';$script:deny=$false
 $script:accounting=$true;Reject {Read-ScoliaTeachingState} 'Module visible depuis comptabilité';$script:accounting=$false
 $script:data.closed=$true;Reject {Invoke-ScoliaTeachingUpdate -ExpectedRevision 1 -Change {}} 'Écriture année clôturée';$script:data.closed=$false
 Check ((Get-FileHash $path).Hash -ceq $hash) 'Refus modifiant les fichiers'
 Assert-ITMV55BackupDocument $path $script:store
 Check (@(Get-ITMV55BackupFiles $root $script:store).Count -eq 2) 'Cours absents des sauvegardes avec enseignants'
 $hashes=@{};foreach($f in @(Get-ITMV55BackupFiles $root $script:store)){$hashes[[IO.Path]::GetFileName($f)]=(Get-FileHash $f).Hash};Assert-ITMBackupManifest $script:store ($hashes|ConvertTo-Json|ConvertFrom-Json)
 # A real backup and isolated restore retain the complete course/teacher documents.
 $script:storeRoot=$root;$script:store=[pscustomobject]@{Version=1;Id=$script:store.Id;Active='classeur-000001.xlsm';Segments=@('classeur-000001.xlsm');LearnerLimit=100;PaymentLimit=100;Closed=$false}
 $workbook=Join-Path $root 'classeur-000001.xlsm';[IO.File]::WriteAllText($workbook,'Fixture sans Excel')
 $book=[pscustomobject]@{Name='classeur-000001.xlsm';FullName=$workbook};$book|Add-Member ScriptMethod SaveCopyAs {param($destination)[IO.File]::Copy($this.FullName,$destination,$false)};$script:storeBooks=@($book)
 $backup=Backup-ITMStore;$restorePath=Restore-ITMStore $backup.path;$restoredFolder=[IO.Path]::GetDirectoryName($restorePath)
 Check ((Get-FileHash (Join-Path $restoredFolder ([IO.Path]::GetFileName($path)))).Hash -ceq $hash) 'Sauvegarde/restauration altérant les cours'
 Check ((Get-FileHash (Join-Path $restoredFolder ([IO.Path]::GetFileName((Get-ScoliaFeatureStorePath personnel))))).Hash -ceq (Get-FileHash (Get-ScoliaFeatureStorePath personnel)).Hash) 'Enseignants absents de la restauration'
 $script:data.promotions=@([pscustomobject]@{Code='INF1';Active=$true;Annee='2027-2028'})
 Check ((Read-ScoliaFeatureStore enseignements).Items.Count -eq 0 -and (Get-ScoliaFeatureStorePath enseignements) -cne $path) 'Cours mélangés entre années'
 'PASS : '+$script:checks+' contrôles cours/classes, attribution unique, multi-cours, heures, historique, archives, révisions, droits, verrou, sauvegarde et année.'
}finally{Remove-Item -LiteralPath $root -Recurse -Force}
