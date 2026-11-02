$ErrorActionPreference='Stop'
Add-Type -Path (Join-Path $PSScriptRoot 'ScoliaLedger.cs') -ReferencedAssemblies System.Xml,System.Core,System.Runtime.Serialization
. (Join-Path $PSScriptRoot 'ImportsReseau.ps1')
. (Join-Path $PSScriptRoot 'FinanceSynchronisee.ps1')
. (Join-Path $PSScriptRoot 'SynchronisationScolia.ps1')
$root=Join-Path ([IO.Path]::GetTempPath()) ('scolia-drafts-'+[guid]::NewGuid().ToString('N'))
$school=[guid]::NewGuid().ToString('N');$year='2026-2027'
$script:scolia=[pscustomobject]@{Id=$school;Year=$year;Revision=1};$script:scoliaLocalRoot=$root;$script:scoliaSession=[pscustomobject]@{Role='Comptabilité';AccountId='cash-user';Revision=1}
function Assert($ok,$reason){if(-not $ok){throw $reason}}
function Reject([scriptblock]$fn,$reason){$bad=$false;try{&$fn}catch{$bad=$true};Assert $bad $reason}
function Get-ScoliaStateRoot {return (Join-Path $root 'workbook')}
function Assert-ITMPermission {}
function Read-ScoliaConfiguration {return $true}
$r=New-Object ScoliaRoster;$r.SchoolId=$school;$r.Year=$year;$r.PublishedUtc=[datetime]::UtcNow.ToString('o')
$l=New-Object ScoliaLearner;$l.Matricule='INF001';$l.Nom='TEST';$l.PostNom='DOSSIER';$l.Prenom='Alice';$l.Promotion='INF1';$l.Annee=$year;$r.Learners.Add($l)
$payment=New-Object ScoliaEntry;$payment.Id=[guid]::NewGuid().ToString('N');$payment.SchoolId=$school;$payment.Annee=$year;$payment.Compte='Secrétariat';$payment.Kind='Paiement';$payment.Reference='SEC001';$payment.Date='2026-10-12';$payment.Montant=25
$p=New-Object ScoliaPart;$p.Mois='Septembre';$p.Montant=25;$payment.Parts.Add($p);$r.Payments.Add($payment)
$snapshot=New-Object ScoliaSnapshot;$snapshot.Roster=$r;$snapshot.Entries=[ScoliaEntry[]]@()
try{
 # Mixed/secretary legacy cash drafts must remain resumable and be normalized to accounting.
 foreach($legacyRole in @('Mixte','Secrétariat')){
  $draft=New-Object ScoliaEntry;$draft.Kind='Paiement';$draft.OperatorId='cash-user';$draft.Compte=$legacyRole;$draft.Annee=$year
  $script:scoliaSession.Role=$legacyRole;Assert-ScoliaDraftSend $draft
  Assert ($draft.Compte -eq 'Comptabilité') ('Legacy cash draft not normalized: '+$legacyRole)
 }
 $script:scoliaSession.Role='Comptabilité'
 $foreign=New-Object ScoliaEntry;$foreign.Kind='Paiement';$foreign.OperatorId='other-user';$foreign.Compte='Comptabilité';$foreign.Annee=$year
 Reject {Assert-ScoliaDraftSend $foreign} 'Draft from another named account accepted'
 Save-ScoliaAutomaticInbox $snapshot
 $first=Get-ScoliaAutomaticInbox
 Assert ($first.Snapshot.Roster.Payments.Count -eq 1 -and $first.Snapshot.Roster.Learners.Count -eq 1) 'Automatic receipt lost data'
 Assert (@(Get-ScoliaPendingPayments $first.Snapshot $true).Count -eq 1) 'Pending accountant payment missing'
 Assert (@(Get-ScoliaPendingLearners $first.Snapshot).Count -eq 1) 'Pending learner missing'
 Assert (-not [ScoliaInboxCache]::Save($root,$school,$year,'comptabilite',$snapshot)) 'Unchanged snapshot rewritten'
 # Load from disk in a new PowerShell process, independently of in-memory application state.
 $scriptPath=Join-Path $root 'reload.ps1';[IO.File]::WriteAllText($scriptPath,'param($code,$root,$school,$year); if($PSVersionTable.PSVersion.Major -ge 7){Add-Type -Path $code}else{Add-Type -Path $code -ReferencedAssemblies System.Xml,System.Core,System.Runtime.Serialization}; $a=[ScoliaInboxCache]::Read($root,$school,$year,"comptabilite"); if($a.Snapshot.Roster.Payments.Count -ne 1){exit 1}; "REOPEN_OK"')
 $engine=$(if($PSVersionTable.PSEdition -eq 'Desktop'){Join-Path $PSHOME 'powershell.exe'}elseif($env:OS -eq 'Windows_NT'){Join-Path $PSHOME 'pwsh.exe'}else{Join-Path $PSHOME 'pwsh'})
 $result=& $engine -NoProfile -File $scriptPath (Join-Path $PSScriptRoot 'ScoliaLedger.cs') $root $school $year
 Assert ($result -contains 'REOPEN_OK') 'Drafts not available after application restart'
 # A failure to reach a share never touches the previous cached inbox.
 Reject {[ScoliaSharing]::ReadAsync((Join-Path $root 'offline'),$school,$year).GetAwaiter().GetResult()} 'Offline read unexpectedly succeeded'
 Assert ((Get-ScoliaAutomaticInbox).Checksum -eq $first.Checksum) 'Offline read discarded previous drafts'
 $snapshot.Entries=[ScoliaEntry[]]@($payment);Save-ScoliaAutomaticInbox $snapshot
 Assert (@(Get-ScoliaPendingPayments $snapshot $true).Count -eq 0) 'Imported payment remains pending'
 [ScoliaSharing]::SaveLocalRoster((Get-ScoliaImportedRosterPath),$r,$school,$year)
 Assert (@(Get-ScoliaPendingLearners $snapshot).Count -eq 0) 'Imported learner remains pending'
 $l.Contact1='0990000000';Save-ScoliaAutomaticInbox $snapshot
 Assert (@(Get-ScoliaPendingLearners $snapshot).Count -eq 1) 'Changed learner was not staged'
 # Secretary account has an independent inbox and durable imported-state filter.
 $cash=$payment.Copy();$cash.Id=[guid]::NewGuid().ToString('N');$cash.Compte='Comptabilité';$cash.Reference='C001';$snapshot.Entries=[ScoliaEntry[]]@($payment,$cash)
 $script:scoliaSession.Role='Secrétariat';Save-ScoliaAutomaticInbox $snapshot
 Assert (@(Get-ScoliaPendingPayments (Get-ScoliaAutomaticInbox).Snapshot $false).Count -eq 1) 'Secretary drafts echo its own receipts'
 $state=New-Object ScoliaState;$state.SchoolId=$school;$state.Id=$cash.Id;$state.Status='Importé';[ScoliaLedger]::SaveState((Get-ScoliaStateRoot),$state)
 Assert (@(Get-ScoliaPendingPayments $snapshot $false).Count -eq 0) 'Secretary imported payment remains pending'
 Assert ([ScoliaInboxCache]::Read($root,$school,$year,'comptabilite').Snapshot.Entries.Count -eq 1) 'Accounts shared an inbox incorrectly'
 $keep=$snapshot.Entries;$snapshot.Entries=[ScoliaEntry[]]@();Reject {Save-ScoliaAutomaticInbox $snapshot} 'Remote disappearance erased received drafts';$snapshot.Entries=$keep
 Assert ((Get-ScoliaAutomaticInbox).Snapshot.Entries.Count -eq 2) 'Previous drafts not preserved'
 $other=$snapshot.Roster.SchoolId;$snapshot.Roster.SchoolId=[guid]::NewGuid().ToString('N')
 Reject {Save-ScoliaAutomaticInbox $snapshot} 'Foreign school overwrote drafts';$snapshot.Roster.SchoolId=$other
 $cache=Join-Path $root ('brouillons-recus/'+$school+'-'+$year+'-secretariat.xml');$xml=[IO.File]::ReadAllText($cache);[IO.File]::WriteAllText($cache,$xml.Replace('<Montant>25</Montant>','<Montant>26</Montant>'))
 Reject {Get-ScoliaAutomaticInbox} 'Altered drafts accepted'
 'PASS: automatic drafts, mixed-role cash resume, account ownership, offline retention, independent restart, unchanged snapshots, pending filters, learner changes, isolation and integrity'
}finally{if([IO.Directory]::Exists($root)){[IO.Directory]::Delete($root,$true)}}
