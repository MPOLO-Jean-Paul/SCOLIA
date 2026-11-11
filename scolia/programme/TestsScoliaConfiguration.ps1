$ErrorActionPreference='Stop'
function Import-ScoliaTestFunction([string]$file,[string]$name){$tokens=$null;$errors=$null;$ast=[Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot $file),[ref]$tokens,[ref]$errors);$node=$ast.Find({param($n)$n -is [Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -eq $name},$true);if(-not $node){throw $name};. ([scriptblock]::Create('function global:'+ $name + $node.Extent.Text.Substring(('function '+$name).Length)))}
foreach($n in @('Test-ScoliaAccountActive','Test-ScoliaAccountAdmin','Get-ScoliaAccountName','Get-ScoliaAccountId','Get-ScoliaAccountLabel','Write-ScoliaJson','Test-ScoliaProfile','Read-ScoliaConfiguration','Set-ScoliaLocation','Get-ScoliaAttemptFile','Get-ScoliaAttemptUntil','Connect-ScoliaAccount','Test-ScoliaPermission')){Import-ScoliaTestFunction 'ScoliaConfiguration.ps1' $n}
function Promo($s){return ([string]$s).Trim().ToUpperInvariant()}
Add-Type -Path (Join-Path $PSScriptRoot 'SecurityCrypto.cs')
foreach($n in @('New-ITMSecret','Test-ITMSecret')){Import-ScoliaTestFunction 'Securite.ps1' $n}
function Write-ITMUserAudit {}
$root=Join-Path ([IO.Path]::GetTempPath()) ('scolia-config-'+[guid]::NewGuid().ToString('N'));[void][IO.Directory]::CreateDirectory($root);$script:scoliaPointer=Join-Path $root 'poste.json'
$zero=[Convert]::ToBase64String((New-Object byte[] 32));$profile=[pscustomobject]@{Version=1;Revision=1;Id=[guid]::NewGuid().ToString('N');School='École de test';Year='2026 - 2027';Months=10;StartMonth=9;Classes=@([pscustomobject]@{Code='INF1';Fee=50000});Accounts=@([pscustomobject]@{Role='Secrétariat';Locked=$false;Secret=$null},[pscustomobject]@{Role='Comptabilité';Locked=$true;Secret=(New-ITMSecret 'Test-secret-96')})}
try{
 foreach($revision in @($null,0,-1,'1.5','invalid',2147483648)){$bad=$profile|ConvertTo-Json -Depth 12|ConvertFrom-Json;$bad.Revision=$revision;$rejected=$false;try{Test-ScoliaProfile $bad}catch{$rejected=$true};if(-not $rejected){throw ('Révision invalide acceptée : '+$revision)}}
 Test-ScoliaProfile $profile;Write-ScoliaJson (Join-Path $root 'profil-scolia.json') $profile;Set-ScoliaLocation $root $profile
 Connect-ScoliaAccount 'Secrétariat' '';if(-not (Test-ScoliaPermission 'Recevoir') -or -not (Test-ScoliaPermission 'Encaisser')){throw 'Droits secrétariat incorrects'}
 $failed=$false;try{Connect-ScoliaAccount 'Comptabilité' 'incorrect'}catch{$failed=$true};if(-not $failed -or $script:scoliaSession.Role -ne 'Secrétariat'){throw 'Mot de passe incorrect accepté ou session existante modifiée'}
 for($i=0;$i -lt 4;$i++){try{Connect-ScoliaAccount 'Comptabilité' 'incorrect'}catch{}}
 Connect-ScoliaAccount 'Secrétariat' '';if($script:scoliaSession.Role -ne 'Secrétariat'){throw 'Compte libre bloqué par les tentatives dʼun autre compte'}
 $failed=$false;try{Connect-ScoliaAccount 'Comptabilité' 'Test-secret-96'}catch{$failed=$true};if(-not $failed){throw 'Blocage après cinq tentatives absent'}
 $attemptPath=Get-ScoliaAttemptFile 'Comptabilité';Write-ScoliaJson $attemptPath ([pscustomobject]@{Count=0;Until=[datetime]::UtcNow.AddMinutes(-1).ToString('o')})
 Connect-ScoliaAccount 'Comptabilité' 'Test-secret-96';if(-not (Test-ScoliaPermission 'Encaisser') -or (Test-ScoliaPermission 'Saisie') -or (Test-ScoliaPermission 'Recevoir')){throw 'Droits comptabilité incorrects'}
 $profile.Accounts=@([pscustomobject]@{Role='Mixte';Locked=$false;Secret=$null});$profile.Revision++;Write-ScoliaJson (Join-Path $root 'profil-scolia.json') $profile;Connect-ScoliaAccount 'Mixte' '';foreach($p in @('Encaisser','Recevoir','Saisie','Administrer')){if(-not (Test-ScoliaPermission $p)){throw ('Droit mixte absent : '+$p)}}
 $bad=$profile|ConvertTo-Json -Depth 12|ConvertFrom-Json;$bad.Classes=@($bad.Classes)+@($bad.Classes);$failed=$false;try{Test-ScoliaProfile $bad}catch{$failed=$true};if(-not $failed){throw 'Classe dupliquée acceptée'}
 $personalId=[guid]::NewGuid().ToString('N');$profile.Accounts[0]|Add-Member Name 'Jean Paul' -Force;$profile.Accounts[0]|Add-Member Id $personalId -Force;Write-ScoliaJson (Join-Path $root 'profil-scolia.json') $profile;Connect-ScoliaAccount 'Mixte' '';if($script:scoliaSession.Name -cne 'Jean Paul' -or $script:scoliaSession.AccountId -cne $personalId -or (Get-ScoliaAccountLabel $profile.Accounts[0]) -cne 'Jean Paul · Mixte'){throw 'Compte nominatif perdu à la connexion'}
 $bad=$profile|ConvertTo-Json -Depth 12|ConvertFrom-Json;$bad.Accounts[0].Name=' ';$failed=$false;try{Test-ScoliaProfile $bad}catch{$failed=$true};if(-not $failed){throw 'Nom de compte vide accepté'}
 $bad=$profile|ConvertTo-Json -Depth 12|ConvertFrom-Json;$bad.Accounts=@([pscustomobject]@{Id=$personalId;Name='A';Role='Secrétariat';Locked=$false},[pscustomobject]@{Id=$personalId;Name='B';Role='Comptabilité';Locked=$false});$failed=$false;try{Test-ScoliaProfile $bad}catch{$failed=$true};if(-not $failed){throw 'Identités de comptes dupliquées acceptées'}
 # Several named accounts may share a role while keeping independent rights and lockout counters.
 $second=[pscustomobject]@{Id=[guid]::NewGuid().ToString('N');Name='Agent secondaire';Role='Mixte';Locked=$true;Secret=(New-ITMSecret 'Second-test-96');Active=$true;IsAdmin=$false;CanCorrect=$false;CanManageFees=$true}
 $profile.Accounts=@($profile.Accounts)+@($second);Test-ScoliaProfile $profile;Write-ScoliaJson (Join-Path $root 'profil-scolia.json') $profile
 $rejected=$false;try{Connect-ScoliaAccount 'Mixte' ''}catch{$rejected=$true};if(-not $rejected){throw 'Connexion ambiguë par rôle acceptée'}
 Connect-ScoliaAccount $second.Id 'Second-test-96'
 if($script:scoliaSession.AccountId -cne $second.Id -or (Test-ScoliaPermission 'GererComptes') -or (Test-ScoliaPermission 'CorrigerPaiements') -or -not (Test-ScoliaPermission 'GererFrais')){throw 'Droits nominatifs mélangés'}
 if((Get-ScoliaAttemptFile $second.Id) -ceq (Get-ScoliaAttemptFile $personalId)){throw 'Blocages de connexion non individuels'}
 $second.Active=$false;Write-ScoliaJson (Join-Path $root 'profil-scolia.json') $profile;$rejected=$false;try{Connect-ScoliaAccount $second.Id 'Second-test-96'}catch{$rejected=$true};if(-not $rejected){throw 'Compte désactivé connecté'}
 Connect-ScoliaAccount $personalId '';if(-not (Test-ScoliaPermission 'GererComptes')){throw 'Administrateur historique perdu'}
 $bad=$profile|ConvertTo-Json -Depth 12|ConvertFrom-Json;$bad.Accounts[0]|Add-Member -NotePropertyName IsAdmin -NotePropertyValue $false -Force;$rejected=$false;try{Test-ScoliaProfile $bad}catch{$rejected=$true};if(-not $rejected){throw 'Dernier administrateur supprimé'}
 $pointer=[IO.File]::ReadAllText($script:scoliaPointer)|ConvertFrom-Json;$pointer.Id=[guid]::NewGuid().ToString('N');Write-ScoliaJson $script:scoliaPointer $pointer;$failed=$false;try{[void](Read-ScoliaConfiguration)}catch{$failed=$true};if(-not $failed){throw 'Configuration dʼun autre établissement acceptée'}
 'PASS comptes nominatifs multiples, droits individuels, désactivation, administrateur conservé, comptes libres/protégés, mot de passe refusé, espaces séparés/mixte, persistance de configuration, classes dupliquées, autre institution'
}finally{[IO.Directory]::Delete($root,$true)}
