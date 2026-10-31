$ErrorActionPreference='Stop'
if($PSVersionTable.PSVersion.Major -ge 7){Add-Type -Path (Join-Path $PSScriptRoot 'ScoliaLedger.cs')}
else{Add-Type -Path (Join-Path $PSScriptRoot 'ScoliaLedger.cs') -ReferencedAssemblies System.Xml,System.Core,System.Runtime.Serialization}
function Check($ok,$why){if(-not $ok){throw $why}}
$root=Join-Path ([IO.Path]::GetTempPath()) ('async-v78-'+[guid]::NewGuid().ToString('N'));[void][IO.Directory]::CreateDirectory($root)
$local=Join-Path $root 'local';$school=[guid]::NewGuid().ToString('N');$year='2026-2027'
$profile=[pscustomobject]@{Id=$school;Revision=1;Year=$year;Months=10;StartMonth=9;Classes=@([pscustomobject]@{Code='INF1';Fee=100})}
try{
 [IO.File]::WriteAllText((Join-Path $root 'profil-scolia.json'),($profile|ConvertTo-Json -Depth 8))
 $entry=New-Object ScoliaEntry;$entry.Id=[guid]::NewGuid().ToString('N');$entry.Kind='Paiement';$entry.Nom='TEST';$entry.PostNom='DEUX';$entry.Prenom='UN';$entry.Promotion='INF1';$entry.Annee=$year;$entry.Date='2026-10-14';$entry.Reference='TEST-001';$entry.Montant=20;$entry.Compte='Comptabilité';$entry.OperatorId='test';$entry.OperatorName='Test';$entry.ConfigRevision=1
 [void][ScoliaLedger]::Commit($root,$school,$entry,9,10,100)
 $reader=New-Object ScoliaIncrementalReader($root,$school,$year)
 $task=$reader.ReadAndCacheAsync($local,'secretariat');$snapshot=$task.GetAwaiter().GetResult()
 $saved=[ScoliaInboxCache]::Read($local,$school,$year,'secretariat')
 Check ($saved.Snapshot.Entries.Count -eq 1 -and $saved.Snapshot.Entries[0].Montant -eq 20) 'Task completed before durable inbox'
 $snapshot.Entries[0].Montant=999
 $again=$reader.ReadAndCacheAsync($local,'secretariat').GetAwaiter().GetResult()
 Check ($again.Entries[0].Montant -eq 20) 'UI mutation contaminated worker'
 $path=Join-Path $local ('brouillons-recus/'+$school+'-'+$year+'-secretariat.xml')
 $bytes=[IO.File]::ReadAllBytes($path);$gate=[IO.File]::Open($path+'.lock',[IO.FileMode]::OpenOrCreate,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None)
 try{$rejected=$false;try{$reader.ReadAndCacheAsync($local,'secretariat').GetAwaiter().GetResult()}catch{$rejected=$true};Check $rejected 'Lock error swallowed'}finally{$gate.Dispose()}
 Check ([Convert]::ToBase64String($bytes) -ceq [Convert]::ToBase64String([IO.File]::ReadAllBytes($path))) 'Failed cache save altered inbox'
 [IO.File]::WriteAllText($path,'corrupt')
 $rejected=$false;try{$reader.ReadAndCacheAsync($local,'secretariat').GetAwaiter().GetResult()}catch{$rejected=$true};Check $rejected 'Corrupt archive accepted'
 Check ([IO.File]::ReadAllText($path) -ceq 'corrupt') 'Corrupt archive overwritten silently'
 'PASS: asynchronous receive completes after durable save; detached snapshot; lock/corruption errors propagate without data overwrite.'
}finally{Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue}
