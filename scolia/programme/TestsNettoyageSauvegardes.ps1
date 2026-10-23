$ErrorActionPreference='Stop'
# Fichiers de test isolés : aucun Excel et aucune donnée de production.
. (Join-Path $PSScriptRoot 'NettoyageSauvegardes.ps1')
foreach($spec in @(@('StockageClasseurs.ps1','Get-ITMStoreFiles'),@('StockageClasseurs.ps1','Get-ITMPEBackupFiles'),@('StockageClasseurs.ps1','Get-ITMV55BackupFiles'),@('StockageClasseurs.ps1','Assert-ITMBackupManifest'),@('ExploitationProfessionnelle.ps1','Test-ITMBackupIntegrity'))){
 $tokens=$null;$errors=$null;$ast=[Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot $spec[0]),[ref]$tokens,[ref]$errors);$name=$spec[1]
 $node=$ast.Find({param($n)$n -is [Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -eq $name},$true);. ([scriptblock]::Create($node.Extent.Text))
}
function Assert($value,$message){if(-not $value){throw $message}}
$script:allowed=$true
function Assert-ITMPermission($permission){if(-not $script:allowed){throw 'Accès refusé.'}}
function Write-ITMUserAudit {}
function Write-TestJson($path,$value){[IO.File]::WriteAllText($path,($value | ConvertTo-Json -Depth 8),[Text.Encoding]::UTF8)}
function New-TestBackup([string]$Name,[string]$Id=('a'*32)){
 $folder=Join-Path (Join-Path $script:storeFolder 'sauvegardes') $Name;[void][IO.Directory]::CreateDirectory($folder)
 $index=[pscustomobject]@{Version=1;Id=$Id;Active='classeur-000001.xlsm';Segments=@('classeur-000001.xlsm');LearnerLimit=100;PaymentLimit=100}
 $file=Join-Path $folder 'classeur-000001.xlsm';[IO.File]::WriteAllText($file,('DONNEES_'+$Name)*20)
 Write-TestJson (Join-Path $folder 'registre.json') $index
 $hashes=@{'classeur-000001.xlsm'=(Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash;'registre.json'=(Get-FileHash -LiteralPath (Join-Path $folder 'registre.json') -Algorithm SHA256).Hash}
 Write-TestJson (Join-Path $folder 'empreintes.json') $hashes
 return $folder
}
function New-TestCase([string]$Name){$script:storeFolder=Join-Path $root $Name;[void][IO.Directory]::CreateDirectory($script:storeFolder);$script:store=[pscustomobject]@{Id=('a'*32)}}
$root=Join-Path ([IO.Path]::GetTempPath()) ('SCOLIA_cleanup_test_'+[guid]::NewGuid().ToString('N'));[void][IO.Directory]::CreateDirectory($root)
try{
 New-TestCase 'normal';$first=New-TestBackup '20261010-080000-first';$middle=New-TestBackup '20261011-080000-middle';$last=New-TestBackup '20261012-080000-last';$foreign=New-TestBackup '20261013-090000-other' ('b'*32)
 $incomplete=Join-Path $script:storeFolder 'sauvegardes/incomplet';[void][IO.Directory]::CreateDirectory($incomplete);[IO.File]::WriteAllText((Join-Path $incomplete 'garder.txt'),'GARDER')
 $active=Join-Path $script:storeFolder 'classeur-actif.xlsm';[IO.File]::WriteAllText($active,'BASE ACTIVE')
 $settings=Join-Path $script:storeFolder 'sauvegardes-auto.json';[IO.File]::WriteAllText($settings,'PARAMETRES')
 $plan=Get-ITMBackupCleanupPlan;Assert ($plan.Keep.Folder -eq $last -and $plan.Delete.Count -eq 2 -and $plan.Excluded -eq 2) 'Plan incorrect.'
 $bytes=$plan.Bytes;$lastHash=(Get-FileHash -LiteralPath (Join-Path $last 'classeur-000001.xlsm')).Hash
 $result=Invoke-ITMBackupCleanup $plan
 Assert ($result.Deleted -eq 2 -and $result.FreedBytes -eq $bytes -and $result.Failures.Count -eq 0) 'Nettoyage ou comptage incorrect.'
 Assert (-not [IO.Directory]::Exists($first) -and -not [IO.Directory]::Exists($middle)) 'Anciennes copies conservées.'
 Assert ((Get-FileHash -LiteralPath (Join-Path $last 'classeur-000001.xlsm')).Hash -eq $lastHash) 'Dernière copie modifiée.'
 Assert ([IO.Directory]::Exists($foreign) -and [IO.Directory]::Exists($incomplete) -and [IO.File]::ReadAllText($active) -eq 'BASE ACTIVE' -and [IO.File]::ReadAllText($settings) -eq 'PARAMETRES') 'Fichier hors du périmètre modifié.'
 $single=Get-ITMBackupCleanupPlan;Assert ($single.Delete.Count -eq 0) 'Une copie unique serait supprimée.'
 New-TestCase 'corrupt';$old=New-TestBackup '20261010-080000-old';$bad=New-TestBackup '20261012-080000-new';[IO.File]::AppendAllText((Join-Path $bad 'classeur-000001.xlsm'),'CORRUPT')
 $blocked=$false;try{Get-ITMBackupCleanupPlan}catch{$blocked=$true};Assert ($blocked -and [IO.Directory]::Exists($old)) 'Dernière copie corrompue acceptée.'
 New-TestCase 'changed';$old=New-TestBackup '20261010-080000-old';$last=New-TestBackup '20261012-080000-new';$plan=Get-ITMBackupCleanupPlan;$newest=New-TestBackup '20261013-080000-newer'
 $blocked=$false;try{Invoke-ITMBackupCleanup $plan}catch{$blocked=$true};Assert ($blocked -and [IO.Directory]::Exists($old) -and [IO.Directory]::Exists($newest)) 'Plan périmé exécuté.'
 $plan=Get-ITMBackupCleanupPlan;$gate=[IO.File]::Open((Join-Path $script:storeFolder 'sauvegardes-entretien.lock'),[IO.FileMode]::OpenOrCreate,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None)
 try{$blocked=$false;try{Invoke-ITMBackupCleanup $plan}catch{$blocked=$true};Assert ($blocked -and [IO.Directory]::Exists($old)) 'Verrou ignoré.'}finally{$gate.Dispose()}
 New-TestCase 'link';$old=New-TestBackup '20261010-080000-old';$last=New-TestBackup '20261012-080000-new'
 $outside=Join-Path $root 'exterieur';[void][IO.Directory]::CreateDirectory($outside);$protected=Join-Path $outside 'garder.txt';[IO.File]::WriteAllText($protected,'NE PAS SUPPRIMER')
 $linkMade=$false;try{[void](New-Item -ItemType SymbolicLink -Path (Join-Path $old 'lien') -Target $outside -ErrorAction Stop);$linkMade=$true}catch{}
 if($linkMade){$plan=Get-ITMBackupCleanupPlan;Assert ($plan.Delete.Count -eq 0 -and $plan.Excluded -eq 1) 'Sauvegarde contenant un lien autorisée';[void](Invoke-ITMBackupCleanup $plan);Assert ([IO.File]::ReadAllText($protected) -eq 'NE PAS SUPPRIMER') 'Lien suivi pendant la suppression.'}
 $script:allowed=$false;$blocked=$false;try{Get-ITMBackupCleanupPlan}catch{$blocked=$true};Assert $blocked 'Utilisateur non administrateur autorisé.'
 'PASS : nettoyage réel de copies de test, dernière copie intacte, espace compté, autre base et fichiers actifs protégés, corruption, changement de liste, verrous, liens et autorisations.'
}finally{Remove-Item -LiteralPath $root -Recurse -Force}
