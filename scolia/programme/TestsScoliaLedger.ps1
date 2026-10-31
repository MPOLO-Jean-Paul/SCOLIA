$ErrorActionPreference='Stop'
Add-Type -Path (Join-Path $PSScriptRoot 'ScoliaLedger.cs') -ReferencedAssemblies System.Xml,System.Core,System.Runtime.Serialization
$root=Join-Path ([IO.Path]::GetTempPath()) ('scolia-test-'+[guid]::NewGuid().ToString('N'));[void][IO.Directory]::CreateDirectory($root)
$school=[guid]::NewGuid().ToString('N');$profile=[pscustomobject]@{Id=$school;Revision=1;Year='2026 - 2027';Months=10;StartMonth=9;Classes=@([pscustomobject]@{Code='INF1';Fee=100})};[IO.File]::WriteAllText((Join-Path $root 'profil-scolia.json'),($profile|ConvertTo-Json -Depth 8),[Text.UTF8Encoding]::new($true))
function Entry([string]$ref,[decimal]$amount){$e=New-Object ScoliaEntry;$e.Kind='Paiement';$e.Reference=$ref;$e.Nom='Émile';$e.PostNom='MPOLO';$e.Prenom='Jean Paul';$e.Promotion='INF1';$e.Annee='2026 - 2027';$e.Date='2026-09-30';$e.Montant=$amount;$e.ConfigRevision=1;return $e}
function MustReject([scriptblock]$work,[string]$reason){$rejected=$false;try{&$work}catch{$rejected=$true};if(-not $rejected){throw ('Accepté à tort : '+$reason)}}
try{
 $roster=New-Object ScoliaRoster;$roster.SchoolId=$school;$roster.Year=$profile.Year;$l=New-Object ScoliaLearner;$l.Nom='EMILE';$l.PostNom='Mpolo';$l.Prenom='Jean Paul';$l.Promotion='INF1';$l.Annee='2026-2027';$l.Mensuel=100;$l.Annuel=1000;$p=New-Object ScoliaPart;$p.Mois='Septembre';$p.Montant=100;$l.Paid.Add($p);$roster.Learners.Add($l);[ScoliaSharing]::Publish($root,$roster)
 $e=Entry 'CAISSE-001' 150;$saved=[ScoliaLedger]::Commit($root,$school,$e,9,10,100)
 if($saved.Parts.Count -ne 2 -or $saved.Parts[0].Mois -ne 'Octobre' -or $saved.Parts[0].Montant -ne 100 -or $saved.Parts[1].Montant -ne 50){throw 'Mensualité antérieure payée non prise en compte'}
 $roster.Imported.Add($saved.Id);foreach($part in $saved.Parts){$l.Paid.Add($part)};[ScoliaSharing]::Publish($root,$roster)
 $next=[ScoliaLedger]::Commit($root,$school,(Entry 'CAISSE-002' 50),9,10,100);if($next.Parts[0].Mois -ne 'Novembre' -or $next.Parts[0].Montant -ne 50){throw 'Double comptabilisation après retour du secrétariat'}
 MustReject {[void][ScoliaLedger]::Commit($root,$school,(Entry 'CAISSE-002' 30),9,10,100)} 'référence de caisse dupliquée'
 MustReject {[void][ScoliaLedger]::Commit($root,$school,(Entry 'CAISSE-003' 900),9,10,100)} 'dépassement du reste annuel'
 MustReject {[void][ScoliaLedger]::Commit($root,$school,(Entry 'CAISSE-004' 10),9,10,200)} 'tarif différent de la configuration'
 $gate=[IO.File]::Open((Join-Path $root 'journal.lock'),[IO.FileMode]::OpenOrCreate,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None);try{MustReject {[void][ScoliaLedger]::Commit($root,$school,(Entry 'CAISSE-005' 10),9,10,100)} 'écriture concurrente'}finally{$gate.Dispose()}
 $expense=Entry '' 10;$expense.Kind='Depense';$expense.Description='Achat de fournitures';MustReject {[void][ScoliaLedger]::Commit($root,$school,$expense,9,10,0)} 'dépense sans pièce de caisse'
 $copy=[ScoliaLedger]::Read($root,$school);if($copy.Count -ne 2){throw 'Une écriture refusée a été conservée'}
 $state=New-Object ScoliaState;$state.Id=$saved.Id;$state.SchoolId=$school;$state.Status='Importé';[ScoliaLedger]::SaveState($root,$state);$state.Status='Brouillon';MustReject {[ScoliaLedger]::SaveState($root,$state)} 'retour dʼun import vers brouillon'
 $profile.Revision=2;[IO.File]::WriteAllText((Join-Path $root 'profil-scolia.json'),($profile|ConvertTo-Json -Depth 8));MustReject {[void][ScoliaLedger]::Commit($root,$school,(Entry 'CAISSE-006' 10),9,10,100)} 'session périmée'
 $file=Join-Path $root ('journal/'+$saved.Id+'.xml');$text=[IO.File]::ReadAllText($file);[IO.File]::WriteAllText($file,($text.Replace('<Montant>150</Montant>','<Montant>151</Montant>')));MustReject {[ScoliaLedger]::Read($root,$school)} 'journal altéré'
 'PASS calendrier, soldes antérieurs, reprise après import, références uniques, surpaiement, tarif, concurrence, état durable, session périmée, intégrité'
}finally{[IO.Directory]::Delete($root,$true)}
