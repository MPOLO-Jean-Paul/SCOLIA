$ErrorActionPreference='Stop'
# Tests isolés : copies de test uniquement, aucun classeur et aucun registre de production.
foreach($name in @('ConvertTo-ITMBackupTime','Assert-ITMAutoBackupSettings','Get-ITMAutoBackupSettings','Get-ITMAutoBackupSchedule','Get-ITMAutomaticBackups','Invoke-ITMDailyBackup')){
 $tokens=$null;$errors=$null;$ast=[Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot 'ExploitationProfessionnelle.ps1'),[ref]$tokens,[ref]$errors)
 $node=$ast.Find({param($n)$n -is [Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -eq $name},$true);. ([scriptblock]::Create($node.Extent.Text))
}
function Assert($condition,$message){if(-not $condition){throw $message}}
function Test-ITMPermission {return $true}
function Write-ITMUserAudit {}
function Write-ITMAtomicJson($path,$value){[IO.File]::WriteAllText($path,($value | ConvertTo-Json),[Text.Encoding]::UTF8)}
$script:testNow=[datetime]::Parse('2026-10-13T06:00:00');$script:created=0;$script:corrupt=$false
function Get-Date([string]$Format){if($Format){return $script:testNow.ToString($Format)};return $script:testNow}
function Backup-ITMStore {
 $script:created++;$path=Join-Path (Join-Path $script:storeFolder 'sauvegardes') ([guid]::NewGuid().ToString('N'));[void][IO.Directory]::CreateDirectory($path)
 [IO.File]::WriteAllText((Join-Path $path 'test.txt'),'COPIE');return [pscustomobject]@{path=$path}
}
function Test-ITMBackupIntegrity($path){if($script:corrupt){throw 'Copie de test corrompue.'};return [IO.File]::Exists((Join-Path $path 'test.txt'))}
$root=Join-Path ([IO.Path]::GetTempPath()) ('SCOLIA_backup_test_'+[guid]::NewGuid().ToString('N'));[void][IO.Directory]::CreateDirectory($root)
$script:storeFolder=$root;$script:store=[pscustomobject]@{Id=('a'*32)};$script:book=$true;$script:status=[pscustomobject]@{Text=''}
try{
 $p=Get-ITMAutoBackupSettings;Assert ($p.Keep -eq 1 -and $p.PerDay -eq 1) 'Le réglage initial ne commence pas à 1.'
 foreach($keep in @(1,2,8,365,10000)){Assert-ITMAutoBackupSettings ([pscustomobject]@{Enabled=$true;Keep=$keep;PerDay=3})}
 foreach($keep in @(0,-1,1.5,10001)){$blocked=$false;try{Assert-ITMAutoBackupSettings ([pscustomobject]@{Enabled=$true;Keep=$keep})}catch{$blocked=$true};Assert $blocked 'Rétention invalide acceptée.'}
 Write-ITMAtomicJson (Join-Path $root 'sauvegardes-auto.json') ([pscustomobject]@{Enabled=$true;Keep=7});$p=Get-ITMAutoBackupSettings;Assert ($p.Keep -eq 7 -and $p.PerDay -eq 1) 'Ancien choix non conservé.'
 Write-ITMAtomicJson (Join-Path $root 'sauvegardes-auto.json') ([pscustomobject]@{Enabled=$true;Keep=1;PerDay=3})
 $manual=Join-Path $root 'sauvegardes/manuelle';[void][IO.Directory]::CreateDirectory($manual)
 Invoke-ITMDailyBackup;Invoke-ITMDailyBackup;Assert ($script:created -eq 1) 'Copies immédiates répétées.'
 $script:testNow=$script:testNow.AddHours(8);Invoke-ITMDailyBackup;Assert ($script:created -eq 2) 'Deuxième sauvegarde planifiée absente.'
 $script:testNow=$script:testNow.AddHours(8);Invoke-ITMDailyBackup;Invoke-ITMDailyBackup
 Assert ($script:created -eq 3) 'Quota quotidien dépassé après suppression des anciennes copies.'
 Assert (@(Get-ITMAutomaticBackups).Count -eq 1) 'Le choix de garder une copie nʼest pas respecté.'
 Assert ([IO.Directory]::Exists($manual)) 'Copie manuelle supprimée.'
 $script:testNow=$script:testNow.AddDays(1);$script:corrupt=$true;$blocked=$false;try{Invoke-ITMDailyBackup}catch{$blocked=$true};Assert $blocked 'Copie corrompue publiée.'
 $script:corrupt=$false;$gate=[IO.File]::Open((Join-Path $root 'sauvegardes-auto.lock'),[IO.FileMode]::OpenOrCreate,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None)
 try{$before=$script:created;Invoke-ITMDailyBackup;Assert ($script:created -eq $before) 'Deux sessions ont ignoré le verrou.'}finally{$gate.Dispose()}
 Invoke-ITMDailyBackup;Assert ((Get-ITMAutoBackupSchedule (Get-ITMAutoBackupSettings)).Count -eq 1) 'Le nouveau jour conserve le quota précédent.'
 'PASS : saisie à partir de 1, choix existants conservés, espacement, quota et rétention indépendants, copies manuelles protégées, corruption et verrou.'
}finally{Remove-Item -LiteralPath $root -Recurse -Force}
