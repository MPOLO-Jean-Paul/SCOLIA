$ErrorActionPreference='Stop'
# Logic-only test with in-memory persistence and fast test secrets; no real account files.
$tokens=$null;$errors=$null;$ast=[Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot 'Comptes.ps1'),[ref]$tokens,[ref]$errors)
if($errors.Count){throw 'Syntaxe des comptes invalide.'}
foreach($name in @('Find-ITMLocalAccount','Test-ITMAccountAuthentication','Set-ITMLocalAccount','Connect-ITMLocalAccount')){
    $definition=$ast.Find({param($node)$node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq $name},$true)
    if(-not $definition){throw ('Fonction absente : '+$name)}
    . ([scriptblock]::Create($definition.Extent.Text))
}
function New-ITMSecret($value){$sha=[Security.Cryptography.SHA256]::Create();try{return [pscustomobject]@{Salt='test-only';Hash=[Convert]::ToBase64String($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($value)))}}finally{$sha.Dispose()}}
function Test-ITMSecret($value,$secret){return ((New-ITMSecret $value).Hash -ceq $secret.Hash)}
function Save-ITMAccounts {if($script:failSave){throw 'Échec simulé de sauvegarde.'};$script:saveCount++}
function Update-ITMAccountButton {}
function Write-ITMUserAudit {}
function New-ITMAccountRecovery {$script:resetCounter++;return ('reset-code-'+$script:resetCounter)}
$script:accounts=[pscustomobject]@{Version=1;Revision=0;Users=@();LastKind='Windows';LastUser='';Failures=0;BlockedUntil=''}
$script:saveCount=0;$script:resetCounter=0;$script:failSave=$false
$code=Set-ITMLocalAccount 'secretaire' 'Jean Paul' 'TestSecret123' 'TestSecret123'
if(@($script:accounts.Users).Count -ne 1 -or $script:accountSession.Id -ne 'secretaire'){throw 'Compte non créé.'}
$failed=$false
try{[void](Set-ITMLocalAccount 'SECRETAIRE' 'Autre' 'TestSecret123' 'TestSecret123')}catch{$failed=$true}
if(-not $failed -or @($script:accounts.Users).Count -ne 1){throw 'Doublon accepté.'}
if(Test-ITMAccountAuthentication 'secretaire' 'incorrect'){throw 'Mot de passe incorrect accepté.'}
Connect-ITMLocalAccount 'SECRETAIRE' 'TestSecret123'
if($script:accounts.LastKind -ne 'Application' -or $script:accounts.Failures -ne 0){throw 'Connexion incorrecte.'}
$newCode=Set-ITMLocalAccount 'secretaire' '' 'NouveauSecret123' 'NouveauSecret123' $code
if($newCode -eq $code -or (Test-ITMAccountAuthentication 'secretaire' 'TestSecret123')){throw 'Récupération incorrecte.'}
Connect-ITMLocalAccount 'secretaire' 'NouveauSecret123'
$before=@($script:accounts.Users).Count;$script:failSave=$true;$failed=$false
try{[void](Set-ITMLocalAccount 'second' 'Deuxième' 'TestSecret123' 'TestSecret123')}catch{$failed=$true}
if(-not $failed -or @($script:accounts.Users).Count -ne $before){throw 'Compte conservé malgré échec de sauvegarde.'}
$failed=$false;$oldFailures=$script:accounts.Failures;$oldBlockedUntil=$script:accounts.BlockedUntil
try{[void](Test-ITMAccountAuthentication 'secretaire' 'incorrect')}catch{$failed=$true}
if(-not $failed -or $script:accounts.Failures -ne $oldFailures -or $script:accounts.BlockedUntil -cne $oldBlockedUntil){throw 'Compteur modifié malgré échec de sauvegarde.'}
$script:failSave=$false
for($i=0;$i -lt 5;$i++){if(Test-ITMAccountAuthentication 'secretaire' 'incorrect'){throw 'Échec accepté.'}}
$blocked=$false
try{[void](Test-ITMAccountAuthentication 'secretaire' 'NouveauSecret123')}catch{$blocked=$true}
if(-not $blocked -or -not $script:accounts.BlockedUntil){throw 'Blocage absent.'}
Write-Output 'Création, unicité, connexion, récupération, annulation après échec et blocage validés en mémoire.'
