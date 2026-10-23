# Tests de stockage réel isolé ; aucun classeur ni profil de production.
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'FonctionnalitesV55.ps1')
$tokens=$null;$errors=$null
$ast=[Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot 'StockageClasseurs.ps1'),[ref]$tokens,[ref]$errors)
if($errors.Count){throw 'Le stockage existant contient une erreur de syntaxe.'}
$node=$ast.Find({param($n)$n -is [Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -eq 'Write-ITMAtomicJson'},$true)
. ([scriptblock]::Create($node.Extent.Text))
function Assert-CoreV55([bool]$condition,[string]$message){if(-not $condition){throw $message}}
function Reject-CoreV55([scriptblock]$action,[string]$message){$rejected=$false;try{& $action|Out-Null}catch{$rejected=$true};Assert-CoreV55 $rejected $message}
function Assert-ITMPermission([string]$permission){if($script:denied){throw 'Droit refusé.'}}
$root=Join-Path ([IO.Path]::GetTempPath()) ('SCOLIA-V55-tests-'+[guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($root)
$script:storeFolder=$root;$script:store=[pscustomobject]@{Id='a'*32};$script:book=[pscustomobject]@{FullName=(Join-Path $root 'test.xlsm')}
$script:scolia=[pscustomobject]@{Id='b'*32};$script:accountSession=[pscustomobject]@{Id='tester';Role='Secrétariat'}
$script:data=[pscustomobject]@{closed=$false;promotions=@([pscustomobject]@{Active=$true;Annee='2026-2027'})}
try{
 $empty=Read-ScoliaFeatureStore -Name presences;Assert-CoreV55 ($empty.Revision -eq 0 -and $empty.Items.Count -eq 0) 'Le stockage neuf n est pas vide.'
 $written=Invoke-ScoliaFeatureUpdate -Name presences -Change {param($document)$document.Items=@([pscustomobject]@{Id='feuille';Date='2026-10-11';Entries=@()})}
 Assert-CoreV55 ($written.Revision -eq 1) 'La révision n est pas incrémentée.'
 $path=Get-ScoliaFeatureStorePath -Name presences;$hash=(Get-FileHash $path -Algorithm SHA256).Hash
 Reject-CoreV55 {Invoke-ScoliaFeatureUpdate -Name presences -Change {param($document)$document.Items=@();throw 'interruption'}} 'Une mutation interrompue est enregistrée.'
 Assert-CoreV55 ((Get-FileHash $path -Algorithm SHA256).Hash -eq $hash) 'Une erreur modifie le fichier existant.'
 $gate=[IO.File]::Open($path+'.lock',[IO.FileMode]::OpenOrCreate,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None)
 try{Reject-CoreV55 {Invoke-ScoliaFeatureUpdate -Name presences -Change {param($document)$document.Items=@()}} 'Le verrou concurrent est ignoré.'}finally{$gate.Dispose()}
 $script:denied=$true;Reject-CoreV55 {Invoke-ScoliaFeatureUpdate -Name presences -Change {param($document)$document.Items=@()}} 'Les droits ne sont pas contrôlés.';$script:denied=$false
 $script:data.closed=$true;Reject-CoreV55 {Invoke-ScoliaFeatureUpdate -Name presences -Change {param($document)$document.Items=@()}} 'L année clôturée est modifiable.';$script:data.closed=$false
 $script:data.promotions[0].Annee='2027-2028';$other=Read-ScoliaFeatureStore -Name presences
 Assert-CoreV55 ($other.Items.Count -eq 0 -and (Get-ScoliaFeatureStorePath -Name presences) -ne $path) 'Deux années partagent le même fichier.'
 $script:data.promotions[0].Annee='2026-2027'
 [IO.File]::WriteAllText($path,'{json invalide',[Text.Encoding]::UTF8)
 Reject-CoreV55 {Read-ScoliaFeatureStore -Name presences} 'Un fichier corrompu est réinitialisé silencieusement.'
 Assert-CoreV55 ([IO.File]::ReadAllText($path) -eq '{json invalide') 'Le fichier corrompu est écrasé.'
 $script:data.promotions+=([pscustomobject]@{Active=$true;Annee='2027-2028'})
 Reject-CoreV55 {Get-ScoliaFeatureContext} 'Une base de deux années actives est acceptée.'
 Write-Output 'PASS : stockage atomique, verrou, erreurs, droits, clôture, isolation annuelle et fichier corrompu.'
}finally{[IO.Directory]::Delete($root,$true)}
