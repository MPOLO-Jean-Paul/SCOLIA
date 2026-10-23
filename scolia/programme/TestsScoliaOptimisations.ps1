$ErrorActionPreference='Stop'
Add-Type -Path (Join-Path $PSScriptRoot 'ScoliaLedger.cs') -ReferencedAssemblies System.Xml,System.Core,System.Runtime.Serialization
function Assert($condition,$message){if(-not $condition){throw $message}}
function Reject([scriptblock]$body,$message){$bad=$false;try{& $body}catch{$bad=$true};Assert $bad $message}
$root=Join-Path ([IO.Path]::GetTempPath()) ('scolia100-'+[guid]::NewGuid().ToString('N'));[void][IO.Directory]::CreateDirectory($root);$school=[guid]::NewGuid().ToString('N')
$profile=[pscustomobject]@{Id=$school;Revision=1;Year='2026 - 2027';Months=10;StartMonth=9;Classes=@([pscustomobject]@{Code='INF1';Fee=100})}
[IO.File]::WriteAllText((Join-Path $root 'profil-scolia.json'),($profile|ConvertTo-Json -Depth 8))
function Entry($reference){$e=New-Object ScoliaEntry;$e.Id=[guid]::NewGuid().ToString('N');$e.Kind='Paiement';$e.Nom='Mpolo';$e.PostNom='Lono';$e.Prenom='Jean Paul';$e.Promotion='INF1';$e.Annee=$profile.Year;$e.Date=[datetime]::Today.ToString('yyyy-MM-dd');$e.Reference=$reference;$e.Montant=20;$e.Compte='Comptabilité';$e.OperatorId='operator';$e.OperatorName='Jean Paul';$e.ConfigRevision=1;return $e}
try{
 $first=Entry 'P1';[void][ScoliaLedger]::Commit($root,$school,$first,9,10,100);$reader=New-Object ScoliaIncrementalReader($root,$school,$profile.Year)
 $snapshot=$reader.Read();Assert ($snapshot.Entries.Count -eq 1 -and $reader.LastFilesRead -eq 1) 'Première lecture incorrecte.'
 $snapshot.Entries[0].Montant=900;$snapshot=$reader.Read();Assert ($reader.LastFilesRead -eq 0 -and $snapshot.Entries[0].Montant -eq 20) 'Cache relu ou modifié par le consommateur.'
 $second=Entry 'P2';[void][ScoliaLedger]::Commit($root,$school,$second,9,10,100);$snapshot=$reader.Read();Assert ($snapshot.Entries.Count -eq 2 -and $reader.LastFilesRead -eq 1) 'Lecture incrémentale incorrecte.'
 $draft=Entry 'D1';$draftFolder=Join-Path $root 'local';$file=[ScoliaLedger]::SaveDraft($draftFolder,$school,$draft);Assert ([ScoliaLedger]::Read($root,$school).Count -eq 2) 'Brouillon comptabilisé à tort.'
 $ready=[ScoliaLedger]::ReadDraft($file,$school);$original=[ScoliaLedger]::ReadDraft($file,$school);$saved=[ScoliaLedger]::Commit($root,$school,$ready,9,10,100)
 $replayed=[ScoliaLedger]::Commit($root,$school,$original,9,10,100);Assert ($saved.Id -eq $replayed.Id -and [ScoliaLedger]::Read($root,$school).Count -eq 3) 'Reprise du brouillon dupliquée.'
 $altered=$saved.Copy();$altered.Montant=21;Reject {[ScoliaLedger]::Commit($root,$school,$altered,9,10,100)} 'Montant modifié accepté avec ancienne empreinte.'
 $altered=$saved.Copy();$altered.Reference='AUTRE';Reject {[ScoliaLedger]::Commit($root,$school,$altered,9,10,100)} 'Référence modifiée acceptée avec ancienne empreinte.'
 $same=$saved.Copy();[void][ScoliaLedger]::Commit($root,$school,$same,9,10,100);Assert ([ScoliaLedger]::Read($root,$school).Count -eq 3) 'Reprise inchangée non idempotente.'
 $original.Montant=21;Reject {[ScoliaLedger]::Commit($root,$school,$original,9,10,100)} 'Même référence modifiée acceptée.'
 $roster=New-Object ScoliaRoster;$roster.SchoolId=$school;$roster.Year=$profile.Year;$roster.Imported.Add($first.Id);[ScoliaSharing]::Publish($root,$roster)
 $snapshot=$reader.Read();$snapshot.Roster.Imported.Clear();Assert ($reader.Read().Roster.Imported.Count -eq 1) 'Répertoire en cache mutable.'
 $journal=Join-Path $root ('journal/'+$first.Id+'.xml');$content=[IO.File]::ReadAllText($journal);[IO.File]::WriteAllText($journal,$content.Replace('<Montant>20</Montant>','<Montant>99</Montant>'));[IO.File]::SetLastWriteTimeUtc($journal,[datetime]::UtcNow.AddSeconds(1));Reject {$reader.Read()} 'Journal modifié accepté.';[IO.File]::WriteAllText($journal,$content)
 [void]$reader.Read();[IO.File]::Delete($journal);Reject {$reader.Read()} 'Disparition dʼécriture acceptée comme caisse vide.'
 $text=[IO.File]::ReadAllText($file);[IO.File]::WriteAllText($file,$text.Replace('<Montant>20</Montant>','<Montant>90</Montant>'));Reject {[ScoliaLedger]::ReadDraft($file,$school)} 'Brouillon modifié accepté.'
 # Use the production reconciliation function with a verified cached snapshot.
 $tokens=$null;$errors=$null;$ast=[Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot 'SynchronisationScolia.ps1'),[ref]$tokens,[ref]$errors);$fn=$ast.Find({param($n)$n -is [Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -eq 'Get-ScoliaReconciliation'},$true);. ([scriptblock]::Create($fn.Extent.Text))
 function Test-ScoliaPermission {return $false};$script:scolia=$profile;$script:scoliaEntries=@($first,$second,$saved);$script:scoliaRoster=$roster;$b=Get-ScoliaReconciliation;Assert ($b.Cash -eq 60 -and $b.Received -eq 20 -and $b.Remaining -eq 40) 'Rapprochement incorrect.'
 'PASS cache incrémental, isolation mémoire, intégrité, brouillons non comptabilisés, reprise après commit sans doublon, rejet des modifications et rapprochement.'
}finally{[IO.Directory]::Delete($root,$true)}
