$ErrorActionPreference='Stop'
Add-Type -Path (Join-Path $PSScriptRoot 'ScoliaLedger.cs') -ReferencedAssemblies System.Xml,System.Core,System.Runtime.Serialization
$root=Join-Path ([IO.Path]::GetTempPath()) ('finance-'+[guid]::NewGuid().ToString('N'));[void][IO.Directory]::CreateDirectory($root)
$school=[guid]::NewGuid().ToString('N');$actor=[guid]::NewGuid().ToString('N');$year='2026-2027'
function Assert($ok,$message){if(-not $ok){throw $message}}
function Reject([scriptblock]$action,$message){$failed=$false;try{&$action}catch{$failed=$true};Assert $failed $message}
function NewEntry($reference,$amount,$category='SCOLARITE'){
 $e=New-Object ScoliaEntry;$e.Id=[guid]::NewGuid().ToString('N');$e.SchoolId=$school;$e.Kind='Paiement';$e.Reference=$reference;$e.Montant=$amount;$e.Category=$category;$e.CommonReceipt='1';$e.Date='2026-09-15';$e.Annee=$year;$e.ConfigRevision=1;$e.Compte='Comptabilité';$e.Matricule='INF001';$e.Nom='TEST';$e.PostNom='DOSSIER';$e.Prenom='Un';$e.Promotion='INF1';$e.OperatorId=$actor;$e.OperatorName='Agent test';return $e
}
try{
 $profile=@{Id=$school;Revision=1;Year=$year;StartMonth=9;Months=10;Classes=@(@{Code='INF1';Fee=100})};$profile|ConvertTo-Json -Depth 8|Set-Content (Join-Path $root 'profil-scolia.json')
 $roster=New-Object ScoliaRoster;$roster.SchoolId=$school;$roster.Year=$year;$roster.PublishedUtc=[datetime]::UtcNow.ToString('o');$roster.AppliedFormat='1';$roster.AppliedPayments=New-Object 'Collections.Generic.List[ScoliaEntry]'
 $l=New-Object ScoliaLearner;$l.Matricule='INF001';$l.Nom='TEST';$l.PostNom='DOSSIER';$l.Prenom='Un';$l.Promotion='INF1';$l.Annee=$year;$l.Mensuel=100;$l.Annuel=1000;$roster.Learners.Add($l);[ScoliaSharing]::Publish($root,$roster)
 $a=[ScoliaLedger]::Commit($root,$school,(NewEntry 'RECU-001' 150),9,10,100)
 $originalHash=(Get-FileHash (Join-Path $root ('journal/'+$a.Id+'.xml'))).Hash
 $repeat=NewEntry 'recu-001' 150;$repeat.Compte='Secrétariat';$repeat.OperatorId=[guid]::NewGuid().ToString('N')
 $same=[ScoliaLedger]::Commit($root,$school,$repeat,9,10,100);Assert ($same.Id -eq $a.Id -and ([ScoliaLedger]::Read($root,$school)).Count -eq 1) 'Common receipt duplicated across accounts'
 Reject {[ScoliaLedger]::Commit($root,$school,(NewEntry 'RECU-001' 151),9,10,100)} 'Conflicting reference accepted'
 $catalog=New-Object ScoliaFees;$catalog.SchoolId=$school;$catalog.Year=$year;$fee=New-Object ScoliaFee;$fee.Code='EXAMEN';$fee.Name='Frais des examens';$fee.Amount=50;$catalog.Items.Add($fee);[ScoliaFinance]::SaveFees($root,$school,$catalog,0)
 $exam=[ScoliaLedger]::Commit($root,$school,(NewEntry 'RECU-002' 20 'EXAMEN'),9,10,100);Assert ($exam.Parts.Count -eq 0) 'Additional fee allocated to tuition'
 Reject {[ScoliaLedger]::Commit($root,$school,(NewEntry 'RECU-003' 31 'EXAMEN'),9,10,100)} 'Category overpayment accepted'
 $changed=[ScoliaFinance]::Fees($root,$school,$year);$changed.Items[0].Amount=60;Reject {[ScoliaFinance]::SaveFees($root,$school,$changed,1)} 'Used tariff retroactively changed'
 $roster.Imported.Add($a.Id);$roster.AppliedPayments.Add($a.Copy());foreach($p in $a.Parts){$l.Paid.Add($p)};[ScoliaSharing]::Publish($root,$roster)
 $b=[ScoliaFinance]::Correct($root,$school,$a.Id,$a.Checksum,120,'2026-09-16','Erreur du montant saisi',$actor,'Agent test',1)
 Assert ($b.Revision -eq '1' -and $b.Montant -eq 120 -and $b.Parts[1].Montant -eq 20) 'Correction wrong'
 Assert ((Get-FileHash (Join-Path $root ('journal/'+$a.Id+'.xml'))).Hash -eq $originalHash) 'Original modified'
 Reject {[ScoliaFinance]::Correct($root,$school,$a.Id,$a.Checksum,130,'2026-09-16','Concurrent correction',$actor,'Agent test',1)} 'Stale correction accepted'
 $all=[ScoliaLedger]::Read($root,$school);$corrected=@($all|Where-Object Id -eq $a.Id)[0];Assert ($corrected.Montant -eq 120 -and $corrected.Checksum -eq $b.Checksum) 'Revision read inconsistent'
 $snapshot=New-Object ScoliaSnapshot;$snapshot.Entries=$all;$snapshot.Roster=$roster;$row=@([ScoliaFinance]::Reconcile($snapshot)|Where-Object Id -eq $a.Id)[0];Assert ($row.Status -eq 'Écart' -and $row.Difference -eq -30) 'Unapplied correction hidden'
 # A stale secretary publication must not create a second opening balance.
 $c=[ScoliaLedger]::Commit($root,$school,(NewEntry 'RECU-004' 80),9,10,100);Assert ($c.Parts.Count -eq 1 -and $c.Parts[0].Mois -eq 'Octobre') 'Stale publication double counted correction'
 $roster.AppliedPayments[0]=$b.Copy();$l.Paid.Clear();foreach($p in $b.Parts){$l.Paid.Add($p)};[ScoliaSharing]::Publish($root,$roster)
 $snapshot.Roster=$roster;$snapshot.Entries=[ScoliaLedger]::Read($root,$school);$row=@([ScoliaFinance]::Reconcile($snapshot)|Where-Object Id -eq $a.Id)[0];Assert ($row.Status -eq 'Rapproché') 'Corrected receipt not reconciled'
 $cancel=[ScoliaFinance]::Correct($root,$school,$a.Id,$b.Checksum,0,'2026-09-16','Annulation du reçu erroné',$actor,'Agent test',1)
 Assert ($cancel.Cancelled -eq '1' -and $cancel.Parts.Count -eq 0) 'Cancellation wrong'
 $d=[ScoliaLedger]::Commit($root,$school,(NewEntry 'RECU-005' 100),9,10,100);Assert ($d.Parts[0].Mois -eq 'Septembre') 'Cancelled balance not released'
 Reject {[ScoliaLedger]::Commit($root,$school,(NewEntry 'RECU-001' 150),9,10,100)} 'Cancelled reference reused'
 Assert (([ScoliaFinance]::History($root,$school)).Count -eq 2) 'Correction history incomplete'
 $reader=New-Object ScoliaIncrementalReader($root,$school,$year);$first=$reader.Read();$examCancel=[ScoliaFinance]::Correct($root,$school,$exam.Id,$exam.Checksum,0,'2026-09-15','Annulation frais examen',$actor,'Agent test',1);$next=$reader.Read();Assert (@($next.Entries|Where-Object Id -eq $exam.Id)[0].Cancelled -eq '1') 'Incremental read missed correction'
 [void][ScoliaInboxCache]::Save((Join-Path $root 'local'),$school,$year,'secretariat',$next);$cached=[ScoliaInboxCache]::Read((Join-Path $root 'local'),$school,$year,'secretariat');Assert ($cached.Snapshot.Entries.Count -eq 4) 'Corrected offline cache failed'
 $historyFile=@(Get-ChildItem (Join-Path $root 'corrections') -Filter '*.xml')[0].FullName;$txt=[IO.File]::ReadAllText($historyFile);[IO.File]::WriteAllText($historyFile,$txt.Replace('<AuthorName>Agent test</AuthorName>','<AuthorName>Autre</AuthorName>'));Reject {[ScoliaLedger]::Read($root,$school)} 'Altered correction accepted'
 'PASS common reference across accounts, conflicting receipt, category budgets, tariff protection, immutable originals, optimistic concurrency, reconciliation, correction/cancellation balances, incremental revision detection, offline cache and history integrity'
}finally{[IO.Directory]::Delete($root,$true)}
