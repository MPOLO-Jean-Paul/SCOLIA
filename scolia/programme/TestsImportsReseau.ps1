$ErrorActionPreference='Stop'
Add-Type -Path (Join-Path $PSScriptRoot 'ScoliaLedger.cs') -ReferencedAssemblies System.Xml,System.Core,System.Runtime.Serialization
. (Join-Path $PSScriptRoot 'ImportsReseau.ps1')
. (Join-Path $PSScriptRoot 'FinanceSynchronisee.ps1')
$root=Join-Path ([IO.Path]::GetTempPath()) ('scolia-imports-'+[guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($root)
$school=[guid]::NewGuid().ToString('N');$dataset=[guid]::NewGuid().ToString('N')
$profile=[pscustomobject]@{Id=$school;Revision=1;Year='2026-2027';Months=10;StartMonth=9;Classes=@([pscustomobject]@{Code='INF1';Fee=100})}
[IO.File]::WriteAllText((Join-Path $root 'profil-scolia.json'),($profile|ConvertTo-Json -Depth 8))
function Assert($ok,$reason){if(-not $ok){throw $reason}}
function Reject([scriptblock]$fn,$reason){$bad=$false;try{&$fn}catch{$bad=$true};Assert $bad $reason}
function Key($v){return [ScoliaLedger]::Key([string]$v)}
function Promo($v){return Key $v}
function Get-ScoliaSourceDataSet{return $dataset}
function Get-ITMExcelMacroName($b,$module,$method){Assert ($module -eq 'ModuleGestionAvancee') 'Wrong VBA module';return 'journal'}
$script:journalJson='[{"Action":"SCOLIA_TRANSFERT","Numero":"00112233445566778899aabbccddeeff","Avant":"IN001"}]'
function Invoke-ITMExcelRun{return $script:journalJson}
$script:book=[pscustomobject]@{Name='one.xlsm'};$script:store=$null
$script:scolia=$profile;$script:scoliaSession=[pscustomobject]@{AccountId='secretary';Name='Secrétariat';Revision=1}
$learner=[pscustomobject]@{Matricule='INF001';Nom='MPOLO';PostNom='LONO';Prenom='Jean';Promotion='INF1';Annee='2026-2027';Sexe='M';Naissance='01/01/2000';Lieu='Lubumbashi';Contact1='0990000000';Contact2='0990000001';Adresse='Annexe'}
$script:data=[pscustomobject]@{learners=@($learner);payments=@(
 [pscustomobject]@{Numero='A001';Matricule='INF001';Date='12/10/2026';Promotion='INF1';Mois='Septembre';Verse=100},
 [pscustomobject]@{Numero='A001';Matricule='INF001';Date='12/10/2026';Promotion='INF1';Mois='Octobre';Verse=25},
 [pscustomobject]@{Numero='IN001';Matricule='INF001';Date='12/10/2026';Promotion='INF1';Mois='Octobre';Verse=10}
)}
try{
 # A source receipt may map only once to a durable operation ID.
 $validJournal=$script:journalJson
 $script:journalJson='[{"Action":"SCOLIA_TRANSFERT","Numero":"00112233445566778899aabbccddeeff","Avant":"IN001"},{"Action":"SCOLIA_TRANSFERT","Numero":"ffeeddccbbaa99887766554433221100","Avant":"IN001"}]'
 Reject {Get-ScoliaIncomingReceiptMap} 'Conflicting incoming receipt mapping accepted'
 $script:journalJson=$validJournal
 $r=New-Object ScoliaRoster;$r.SchoolId=$school;$r.Year=$profile.Year
 $l=New-Object ScoliaLearner;foreach($n in @('Matricule','Nom','PostNom','Prenom','Promotion','Annee','Sexe','Naissance','Lieu','Contact1','Contact2','Adresse')){$l.$n=$learner.$n};$l.Mensuel=100;$l.Annuel=1000
 foreach($x in @(@('Septembre',100),@('Octobre',25))){$p=New-Object ScoliaPart;$p.Mois=$x[0];$p.Montant=$x[1];$l.Paid.Add($p)};$r.Learners.Add($l)
 # The same receipt number must never silently be split across two learners.
 $originalPayments=@($script:data.payments)
 $script:data.payments=@($originalPayments)+@([pscustomobject]@{Numero='A001';Matricule='INF999';Date='12/10/2026';Promotion='INF1';Mois='Septembre';Verse=5})
 Reject {Add-ScoliaPublishedPayments $r} 'Same receipt reused for two learners accepted'
 $script:data.payments=$originalPayments
 Add-ScoliaPublishedPayments $r
 $r.AppliedPayments.RemoveAll([Predicate[ScoliaEntry]]{param($entry) $entry.Id -eq '00112233445566778899aabbccddeeff'})|Out-Null
 Assert ($r.Payments.Count -eq 1 -and $r.Payments[0].Montant -eq 125) 'Grouping or imported receipt exclusion failed'
 Assert ($r.Payments[0].Parts.Count -eq 2 -and $r.Imported.Contains($r.Payments[0].Id)) 'Monthly allocation or echo protection failed'
 Assert (Test-ScoliaOutgoingReceipt $r.Payments[0]) 'Outgoing receipt not recognised'
 [ScoliaSharing]::Publish($root,$r)
 $cache=Join-Path $root 'local/learners.xml';[ScoliaSharing]::SaveLocalRoster($cache,$r,$school,$profile.Year)
 $copy=[ScoliaSharing]::LoadLocalRoster($cache,$school,$profile.Year)
 Assert ($copy.Learners[0].Contact2 -eq $learner.Contact2 -and $copy.Learners[0].Promotion -eq 'INF1') 'Learner information changed'
 $reader=New-Object ScoliaIncrementalReader($root,$school,$profile.Year);$snap=$reader.Read()
 Assert ($snap.Roster.Payments.Count -eq 1) 'Incremental reader dropped outgoing payments'
 $batch=[ScoliaLedger]::ImportSecretariat($root,$school,$profile.Year)
 Assert ($batch.Imported -eq 1 -and $batch.Errors -eq 0) ('Accounting import failed: '+($batch.Items.Observation -join ';'))
 $again=[ScoliaLedger]::ImportSecretariat($root,$school,$profile.Year)
 Assert ($again.Imported -eq 0 -and $again.Existing -eq 1) 'Repeated import duplicated payment'
 $all=[ScoliaLedger]::Read($root,$school)
 Assert ([ScoliaSharing]::OpeningPaid($r,$l,$all,'Septembre') -eq 0) 'Imported secretary payment counted twice'
 # A new cash payment must start at the remaining October balance.
 $e=New-Object ScoliaEntry;$e.Kind='Paiement';$e.Reference='CAISSE-01';$e.Date='2026-10-12';$e.Matricule=$l.Matricule;$e.Nom=$l.Nom;$e.PostNom=$l.PostNom;$e.Prenom=$l.Prenom;$e.Promotion=$l.Promotion;$e.Annee=$l.Annee;$e.Compte='Comptabilité';$e.ConfigRevision=1;$e.Montant=75
 $cash=[ScoliaLedger]::Commit($root,$school,$e,9,10,100)
 Assert ($cash.Parts.Count -eq 1 -and $cash.Parts[0].Mois -eq 'Octobre' -and $cash.Parts[0].Montant -eq 75) 'Cash allocation double-counted secretary history'
 # Simulate receiving the cash operation in the secretary workbook and republishing its durable ID.
 $r.Imported.Add($cash.Id);$r.AppliedPayments.Add($cash.Copy());$p=New-Object ScoliaPart;$p.Mois='Octobre';$p.Montant=75;$l.Paid.Add($p);[ScoliaSharing]::Publish($root,$r)
 $next=$e.Copy();$next.Id='';$next.Reference='CAISSE-02';$next.Montant=10
 $saved=[ScoliaLedger]::Commit($root,$school,$next,9,10,100)
 Assert ($saved.Parts[0].Mois -eq 'Novembre') 'Return to secretary caused double count'
 # Interrupted batches resume from their immutable ledger IDs.
 $r.Payments[0].Montant=130;$r.Payments[0].Parts[1].Montant=30;[ScoliaSharing]::Publish($root,$r)
 $changed=[ScoliaLedger]::ImportSecretariat($root,$school,$profile.Year)
 Assert ($changed.Errors -eq 1 -and ([ScoliaLedger]::Read($root,$school)).Count -eq 3) 'Changed source silently duplicated or overwrote a payment'
 $gate=[IO.File]::Open((Join-Path $root 'journal.lock'),[IO.FileMode]::OpenOrCreate,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None)
 try{Reject {[ScoliaLedger]::ImportSecretariat($root,$school,$profile.Year)} 'Concurrent writer accepted'}finally{$gate.Dispose()}
 Reject {[ScoliaLedger]::ImportSecretariat($root,([guid]::NewGuid().ToString('N')),$profile.Year)} 'Foreign institution accepted'
 Reject {[ScoliaLedger]::ImportSecretariat((Join-Path $root 'offline'),$school,$profile.Year)} 'Offline share treated as successful empty import'
 $r.Learners.Add($l.Copy());Reject {[ScoliaSharing]::SaveLocalRoster($cache,$r,$school,$profile.Year)} 'Duplicate learner accepted'
 Assert (([ScoliaSharing]::LoadLocalRoster($cache,$school,$profile.Year)).Learners.Count -eq 1) 'Rejected roster damaged previous import'
 'PASS: secretary export, unique receipt owner, durable incoming mapping, full learner details, grouped monthly receipts, both payment directions, no duplicate on retry, no double balance, modified source conflict, locked/offline/foreign share, safe learner cache'
}finally{[IO.Directory]::Delete($root,$true)}
