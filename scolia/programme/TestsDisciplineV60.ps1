# Décisions et persistance isolées, sans Excel ni données scolaires réelles.
$ErrorActionPreference='Stop'
# Use the production dispatcher against the fake workbooks in these isolated tests.
$transportTokens=$null;$transportErrors=$null
$transportAst=[Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot 'PasserelleExcel.ps1'),[ref]$transportTokens,[ref]$transportErrors)
$transportFn=$transportAst.Find({param($n)$n -is [Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -eq 'Invoke-ITMExcelMethod'},$true)
. ([scriptblock]::Create($transportFn.Extent.Text))

foreach($name in @('FonctionnalitesV55.ps1','CentreRechercheV55.ps1','PersonnelV56.ps1','PresencesV55.ps1','EnseignementsV59.ps1','DisciplineV60.ps1','InterfaceDisciplineV60.ps1','SuiviApprenantsV56.ps1')){. (Join-Path $PSScriptRoot $name)}
$tokens=$null;$errors=$null;$ast=[Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot 'StockageClasseurs.ps1'),[ref]$tokens,[ref]$errors)
foreach($name in @('Write-ITMAtomicJson','Get-ITMStoreFiles','Get-ITMPEBackupFiles','Get-ITMV55BackupHash','Get-ITMV55BackupContextId','Assert-ITMBackupManifest','Assert-ITMV55BackupDocument','Get-ITMV55BackupFiles','Backup-ITMStore','Restore-ITMStore')){$node=$ast.Find({param($n)$n -is [Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -eq $name},$true);. ([scriptblock]::Create($node.Extent.Text))}
$script:checks=0
function Check([bool]$condition,[string]$message){if(-not $condition){throw $message};$script:checks++}
function Reject([scriptblock]$action,[string]$message){$failed=$false;try{[void](& $action)}catch{$failed=$true};Check $failed $message}
function Assert-ITMPermission([string]$Name){if(($script:deny -and $Name -eq 'Saisie') -or ($script:denyAdmin -and $Name -eq 'Administrer')){throw 'Accès refusé.'}}
function Test-ScoliaPermission{return -not $script:accounting}
$root=Join-Path ([IO.Path]::GetTempPath()) ('SCOLIA-discipline-'+[guid]::NewGuid().ToString('N'));[void][IO.Directory]::CreateDirectory($root)
try{
 $script:storeFolder=$root;$script:storeRoot=$root;$script:store=[pscustomobject]@{Id=[guid]::NewGuid().ToString('N')};$script:book=[pscustomobject]@{FullName=(Join-Path $root 'test.xlsm')}
 $script:scolia=[pscustomobject]@{Id=[guid]::NewGuid().ToString('N');StartMonth=9};$script:accountSession=[pscustomobject]@{Id='test';Name='Agent test';Role='Secrétariat'}
 $learner=[pscustomobject]@{Matricule='ABC001';Nom='Élise';PostNom='TEST';Prenom='Une';Promotion='INF1';Annee='2026-2027';Naissance='01/01/2010';Tuteur='Parent test';Contact1='0000000000';Contact2='';Statut='Actif';Inscription='01/09/2026'}
 $manual=[pscustomobject]@{Matricule='ABC002';Nom='Manuel';Promotion='INF1';Annee='2026-2027';Statut='Suspendu'}
 $script:data=[pscustomobject]@{closed=$false;learners=@($learner,$manual);promotions=@([pscustomobject]@{Code='INF1';Active=$true;Annee='2026-2027'})};$context=Get-ScoliaFeatureContext;$actor='Agent test';$now=[datetimeoffset]::Parse('2026-10-13T12:00:00+02:00')
 $script:deny=$false;$script:denyAdmin=$false;$script:accounting=$false
 $emptyData=$script:data;$script:data=[pscustomobject]@{learners=@();promotions=@()}
 Check (-not (Sync-ScoliaDisciplineSnapshot)) 'Dossier vierge bloqué avant configuration des promotions'
 $script:data=$emptyData
 $doc=[pscustomobject]@{Schema=1;Context=$context;Revision=0;Items=@()}
 $first=New-ScoliaDisciplineRequest $learner Temporary 'Premier incident vérifié' '2026-10-09' '2026-10-10' '2026-10-12'
 $r=Add-ScoliaDisciplineDecision $doc $first 0 $actor $now
 Check ($r.Type -eq 'Temporary' -and -not $r.Automatic -and $r.TemporaryOrdinal -eq 1) 'Premier renvoi transformé en définitif'
 Check ((Get-ScoliaDisciplineTemporaryCount $doc 'ABC001') -eq 1) 'Premier renvoi non compté'
 Check ((Get-ScoliaDisciplineLearnerStatus @($doc) 'ABC001' $context.Year ([datetime]'2026-10-12')).Status -eq 'Suspendu') 'Dernier jour non inclus'
 Check ((Get-ScoliaDisciplineLearnerStatus @($doc) 'ABC001' $context.Year ([datetime]'2026-10-13')).Mode -eq 'None') 'Suspension prolongée après sa fin'
 $rows=Get-ScoliaDisciplineRows $doc $now
 Check ($rows[0].Days -eq 3 -and $rows[0].ReturnDate -eq '13/10/2026') 'Durée/retour incorrects'
 $before=$doc|ConvertTo-Json -Depth 20 -Compress
 $duplicate=New-ScoliaDisciplineRequest $learner Temporary 'Premier incident vérifié' '2026-10-09' '2026-10-10' '2026-10-12'
 Reject {Add-ScoliaDisciplineDecision $doc $duplicate 0 $actor $now} 'Doublon compté comme deuxième renvoi'
 Reject {Add-ScoliaDisciplineDecision $doc (New-ScoliaDisciplineRequest $learner Temporary 'Incident daté plus tôt' '2026-10-08' '2026-10-08' '2026-10-08') 0 $actor $now} 'Chronologie inversée acceptée'
 Reject {Add-ScoliaDisciplineDecision $doc (New-ScoliaDisciplineRequest $learner Temporary 'Période incorrecte' '2026-10-13' '2026-10-13' '2026-10-12') 0 $actor $now} 'Durée négative acceptée'
 Reject {Add-ScoliaDisciplineDecision $doc (New-ScoliaDisciplineRequest $learner Permanent 'Décision future invalide' '2026-10-14') 0 $actor $now} 'Décision future acceptée'
 Reject {New-ScoliaDisciplineRequest $learner Parents '' '2026-10-13'} 'Motif absent accepté'
 Check (($doc|ConvertTo-Json -Depth 20 -Compress) -ceq $before) 'Refus ayant modifié le registre'
 $parent=New-ScoliaDisciplineRequest $learner Parents 'Comportement à discuter <script>alert(1)</script>' '2026-10-13' -MeetingAt '2026-10-14T10:00:00.0000000+02:00'
 $p=Add-ScoliaDisciplineDecision $doc $parent 0 $actor $now
 Check ((Get-ScoliaDisciplineTemporaryCount $doc 'ABC001') -eq 1) 'Convocation comptée comme renvoi'
 Reject {Update-ScoliaDisciplineDecision $doc $p.Id Absent 'Parent non présent' 0 $actor $now} 'Absence déclarée avant le rendez-vous'
 Update-ScoliaDisciplineDecision $doc $p.Id Received 'Parent reçu au secrétariat en avance' 0 $actor $now
 Check (@($doc.Items|Where-Object {$_.Id -eq $p.Id -and $_.ParentOutcome -eq 'Received' -and $_.Audit.Count -eq 2}).Count -eq 1) 'Réception du parent non journalisée'
 $html=New-ScoliaParentConvocationHtml $p 'École <TEST>'
 Check ($html.Contains('&lt;script&gt;') -and -not $html.Contains('<script>') -and $html.Contains('14/10/2026') -and $html.Contains('&lt;TEST&gt;')) 'Convocation mal échappée ou date absente'
 $second=New-ScoliaDisciplineRequest $learner Temporary 'Deuxième incident vérifié' '2026-10-13' '2026-10-13' '2026-10-13'
 $r2=Add-ScoliaDisciplineDecision $doc $second 0 $actor $now
 Check ($r2.Type -eq 'Permanent' -and $r2.Automatic -and $r2.TemporaryOrdinal -eq 2) 'Deuxième renvoi non transformé automatiquement'
 Check ((Get-ScoliaDisciplineLearnerStatus @($doc) 'ABC001' $context.Year $now.Date).Status -eq 'Sorti') 'Renvoi définitif sans effet sur le statut'
 $permanentCopy=$doc|ConvertTo-Json -Depth 20|ConvertFrom-Json
 Update-ScoliaDisciplineDecision $doc $r.Id Cancel 'Décision initiale attribuée par erreur' 0 $actor $now
 Check ((Get-ScoliaDisciplineLearnerStatus @($doc) 'ABC001' $context.Year $now.Date).Mode -eq 'Permanent') 'Annulation de la première effaçant une autre décision définitive'
 Update-ScoliaDisciplineDecision $doc $r2.Id Cancel 'Deuxième décision corrigée par la direction' 0 $actor $now
 Check ((Get-ScoliaDisciplineLearnerStatus @($doc) 'ABC001' $context.Year $now.Date).Mode -eq 'None' -and $doc.Items.Count -eq 3) 'Annulation détruisant l historique'
 # Deux années : compteur annuel indépendant, renvoi définitif antérieur conservé.
 $nextContext=[pscustomobject]@{Id=$context.Id;Year='2027-2028';LocalKey=$context.LocalKey};$next=[pscustomobject]@{Schema=1;Context=$nextContext;Revision=0;Items=@()}
 $nextLearner=$learner|ConvertTo-Json|ConvertFrom-Json;$nextLearner.Annee='2027-2028';$nextRequest=New-ScoliaDisciplineRequest $nextLearner Temporary 'Incident de nouvelle année' '2027-09-02' '2027-09-02' '2027-09-02';$nextNow=[datetimeoffset]::Parse('2027-09-03T12:00:00+02:00')
 Reject {Add-ScoliaDisciplineDecision $next $nextRequest 0 $actor $nextNow -PriorDocuments @($permanentCopy)} 'Renvoi définitif oublié dans la nouvelle année'
 $nextRecord=Add-ScoliaDisciplineDecision $next $nextRequest 0 $actor $nextNow -PriorDocuments @($doc)
 Check ($nextRecord.Type -eq 'Temporary' -and $nextRecord.TemporaryOrdinal -eq 1) 'Anciennes sanctions annulées contaminant le compteur annuel'
 # Services et vrais fichiers de test.
 [void](Invoke-ScoliaDisciplineUpdate -ExpectedRevision 0 -Change {param($d)[void](Add-ScoliaDisciplineDecision $d $first 0 $actor $now)})
 $path=Get-ScoliaFeatureStorePath discipline;$hash=(Get-FileHash $path).Hash
 Reject {Invoke-ScoliaDisciplineUpdate -ExpectedRevision 1 -Change {param($d)[void](Add-ScoliaDisciplineDecision $d $duplicate 1 $actor $now)}} 'Doublon enregistré sur disque'
 Check ((Get-FileHash $path).Hash -ceq $hash) 'Refus ayant changé le fichier'
 [void](Sync-ScoliaDisciplineSnapshot ([datetime]'2026-10-11'))
 Check ($learner.Statut -eq 'Suspendu' -and $learner.DisciplineBaseStatus -eq 'Actif') 'Suspension temporaire non appliquée'
 [void](Sync-ScoliaDisciplineSnapshot ([datetime]'2026-10-13'))
 Check ($learner.Statut -eq 'Actif' -and $manual.Statut -eq 'Suspendu') 'Fin de suspension altérant un autre statut administratif'
 $calendar=New-ScoliaPupilCalendar -Month ([datetime]'2026-10-01') -Promotion 'INF1' -Year $context.Year -ContextId $context.Id -WorkDates @('2026-10-09','2026-10-10','2026-10-11','2026-10-12','2026-10-13') -Actor $actor
 $summary=@(Get-ScoliaPupilMonthlySummary -Learners @($learner) -Sheets @() -Calendars @($calendar) -Month ([datetime]'2026-10-01') -Year $context.Year -ContextId $context.Id -Now $now)[0]
 Check ($summary.Absents -eq 1 -and @($summary.Days|Where-Object Status -eq 'Renvoi temporaire').Count -eq 3) 'Absences inventées pendant le renvoi ou antérieures effacées'
 $sheet=New-ScoliaAttendanceSheet -Date ([datetime]'2026-10-10') -Promotion 'INF1' -Year $context.Year -ContextId $context.Id -Entries @([pscustomobject]@{Matricule='ABC001';Nom='Élise TEST Une';Status='Présent';Reason=''})
 $historic=@(Get-ScoliaPupilMonthlySummary -Learners @($learner) -Sheets @($sheet) -Calendars @($calendar) -Month ([datetime]'2026-10-01') -Year $context.Year -ContextId $context.Id -Now $now)[0]
 Check ($historic.Presents -eq 1 -and $historic.Absents -eq 1) 'Présence déjà enregistrée effacée par la discipline'
 [void](Sync-ScoliaDisciplineSnapshot ([datetime]'2026-10-11'))
 $pupilDoc=[pscustomobject]@{Context=$context;Revision=0;Items=@()}
 Set-ScoliaPupilSignature -Document $pupilDoc -Learner $learner -Date $now.Date -Promotion 'INF1' -ExpectedRevision 0 -Actor $actor -Now $now
 Check ($learner.Statut -eq 'Actif' -and $pupilDoc.Items.Count -eq 1) 'Retour à la présence bloqué après expiration du renvoi'
 [void](Invoke-ScoliaDisciplineUpdate -ExpectedRevision 1 -Change {param($d)[void](Add-ScoliaDisciplineDecision $d $second 1 $actor $now)})
 [void](Sync-ScoliaDisciplineSnapshot $now.Date);Check ($learner.Statut -eq 'Sorti') 'Récidive persistée mais non appliquée'
 $reenrollAst=[Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot 'ReinscriptionV55.ps1'),[ref]$tokens,[ref]$errors)
 $reenrollNode=$reenrollAst.Find({param($n)$n -is [Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -eq 'Get-ScoliaReenrollmentRecord'},$true);. ([scriptblock]::Create($reenrollNode.Extent.Text))
 Check ((Get-ScoliaReenrollmentRecord $learner).Statut -eq 'Actif') 'Statut disciplinaire recopié à tort dans le statut administratif'

 Reject {Assert-ScoliaDisciplineAdmission 'ABC001' $now.Date} 'Admission autorisée malgré renvoi définitif'
 Reject {Set-ScoliaPupilSignature -Document $pupilDoc -Learner $learner -Date $now.Date -Promotion 'INF1' -ExpectedRevision 0 -Actor $actor -Now $now} 'Signature autorisée après renvoi définitif'
 if(-not ('ITMExamLearner' -as [type])){Add-Type -TypeDefinition 'public class ITMExamLearner {}'}
 $schoolAst=[Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot 'Scolarite.ps1'),[ref]$tokens,[ref]$errors)
 $batchNode=$schoolAst.Find({param($n)$n -is [Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -eq 'Get-ITMExamBatch'},$true);. ([scriptblock]::Create($batchNode.Extent.Text))
 $script:schoolGrid=[pscustomobject]@{Rows=@([pscustomobject]@{Cells=@{Choix=[pscustomobject]@{Value=$true}};Tag=$learner})};$script:schoolGrid|Add-Member ScriptMethod EndEdit {return $true}
 $blocked=$false;try{Get-ITMExamBatch}catch{$blocked=$_.Exception.Message -like '*carte bloquée*'};Check $blocked 'Carte d accès non bloquée par le renvoi définitif'

 $saved=Read-ScoliaDisciplineState;$savedSecond=@($saved.Document.Items|Where-Object Type -eq 'Permanent')[0];$hash=(Get-FileHash $path).Hash
 $script:denyAdmin=$true
 Reject {Invoke-ScoliaDisciplineUpdate -ExpectedRevision 2 -Change {param($d)Update-ScoliaDisciplineDecision $d $savedSecond.Id Cancel 'Tentative sans administrateur' 2 $actor $now}} 'Annulation autorisée en omettant le drapeau administratif'
 Check ((Get-FileHash $path).Hash -ceq $hash) 'Annulation refusée ayant modifié le fichier';$script:denyAdmin=$false
 [void](Invoke-ScoliaDisciplineUpdate -ExpectedRevision 2 -Administrative -Change {param($d)Update-ScoliaDisciplineDecision $d $savedSecond.Id Cancel 'Correction validée par la direction' 2 $actor $now})
 [void](Sync-ScoliaDisciplineSnapshot $now.Date);Check ($learner.Statut -eq 'Actif') 'Annulation ne restaurant pas le statut initial'
 $hash=(Get-FileHash $path).Hash
 Reject {Invoke-ScoliaDisciplineUpdate -ExpectedRevision 3 -Change {param($d)$d.Items=@()}} 'Historique supprimable'
 Reject {Invoke-ScoliaDisciplineUpdate -ExpectedRevision 1 -Change {}} 'Révision périmée acceptée'
 $gate=[IO.File]::Open($path+'.lock','OpenOrCreate','ReadWrite','None');try{Reject {Invoke-ScoliaDisciplineUpdate -ExpectedRevision 3 -Change {}} 'Verrou ignoré'}finally{$gate.Dispose()}
 $script:deny=$true;Reject {Invoke-ScoliaDisciplineUpdate -ExpectedRevision 3 -Change {}} 'Écriture sans droits';$script:deny=$false
 $script:accounting=$true;Reject {Read-ScoliaDisciplineState} 'Discipline accessible au comptable';$script:accounting=$false
 $script:data.closed=$true;Reject {Invoke-ScoliaDisciplineUpdate -ExpectedRevision 3 -Change {}} 'Année clôturée modifiable';$script:data.closed=$false
 Check ((Get-FileHash $path).Hash -ceq $hash) 'Refus modifiant les données'
 # Sauvegarde/restauration du registre, avec identité et octets conservés.
 $script:store=[pscustomobject]@{Version=1;Id=$script:store.Id;Active='classeur-000001.xlsm';Segments=@('classeur-000001.xlsm');LearnerLimit=100;PaymentLimit=100;Closed=$false}
 $bookFile=Join-Path $root 'classeur-000001.xlsm';[IO.File]::WriteAllText($bookFile,'Fixture sans Excel')
 $book=[pscustomobject]@{Name='classeur-000001.xlsm';FullName=$bookFile};$book|Add-Member ScriptMethod SaveCopyAs {param($destination)[IO.File]::Copy($this.FullName,$destination,$false)};$script:storeBooks=@($book)
 $backup=Backup-ITMStore;$restored=Restore-ITMStore $backup.path
 Check ((Get-FileHash (Join-Path ([IO.Path]::GetDirectoryName($restored)) ([IO.Path]::GetFileName($path)))).Hash -ceq $hash) 'Discipline manquante ou modifiée dans la restauration'
 $text=[IO.File]::ReadAllText($path);[IO.File]::WriteAllText($path,$text.Replace('Premier incident vérifié','Premier incident altéré'))
 Reject {Get-ScoliaDisciplineDocuments -Force} 'Motif altéré accepté silencieusement'
 'PASS : '+$script:checks+' contrôles discipline, deuxième renvoi, doublons, durée/retour, parents, annulation, statut, archives, droits, admission, verrou et sauvegarde/restauration.'
}finally{Remove-Item -LiteralPath $root -Recurse -Force}
