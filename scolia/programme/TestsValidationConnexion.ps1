# Test isolé : aucune configuration ni sauvegarde utilisateur n’est modifiée.
$ErrorActionPreference='Stop'
function Assert($condition,[string]$message){if(-not $condition){throw $message}}
$tokens=$null;$errors=$null
$ast=[Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot 'ScoliaConfiguration.ps1'),[ref]$tokens,[ref]$errors)
$connect=$ast.Find({param($n)$n -is [Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -eq 'Connect-ScoliaAccount'},$true)
Invoke-Expression $connect.Extent.Text
function Read-ScoliaConfiguration{return $true}
function Get-ScoliaAccountId($a){return 'secretariat'}
function Get-ScoliaAccountName($a){return 'Compte test'}
function Test-ScoliaAccountActive($a){return $true}
function Get-ScoliaAttemptFile($id){return (Join-Path ([IO.Path]::GetTempPath()) ([guid]::NewGuid().ToString()+'.json'))}
function Test-ITMSecret($password,$secret){$script:secretChecks++;return $password -ceq 'Correct123'}
function Write-ScoliaJson($file,$value){$script:attemptWrites++}
function Write-ITMUserAudit{}
function Show-ScoliaTransition($text){$script:transitions++}
function Close-ScoliaTransition{}
$script:scolia=[pscustomobject]@{Accounts=@([pscustomobject]@{Role='Secrétariat';Locked=$true;Secret='test'});Revision=1;Id='test'}
$script:security=$null;$script:main=$null;$script:secretChecks=0;$script:attemptWrites=0;$script:scoliaSession=$null
foreach($empty in @('','   ')){
 try{Connect-ScoliaAccount 'secretariat' $empty;throw 'VIDE ACCEPTE'}catch{Assert ($_.Exception.Message -eq 'Renseignez le mot de passe.') 'Message du champ requis incorrect'}
}
Assert ($script:secretChecks -eq 0 -and $script:attemptWrites -eq 0 -and $null -eq $script:scoliaSession) 'Le champ vide a déclenché une tentative ou une session'
$show=$ast.Find({param($n)$n -is [Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -eq 'Show-ScoliaLogin'},$true)
$click=$show.Find({param($n)$n -is [Management.Automation.Language.InvokeMemberExpressionAst] -and $n.Expression.Extent.Text -eq '$login' -and $n.Member.Extent.Text -eq 'Add_Click'},$true)
$handler=$click.Arguments[0].ScriptBlock.GetScriptBlock()
$d=[pscustomobject]@{DialogResult='';Busy=$false}
$d|Add-Member ScriptMethod SetBusy {param($value)$this.Busy=$value}
$d|Add-Member ScriptMethod Refresh {}
$d|Add-Member ScriptMethod Close {}
$login=[pscustomobject]@{Enabled=$true};$account=[pscustomobject]@{SelectedIndex=0}
$password=[pscustomobject]@{Text='Wrong'};$password|Add-Member ScriptMethod Clear {$this.Text=''};$password|Add-Member ScriptMethod Focus {}
$message=[pscustomobject]@{Text='';AccessibleDescription=''};$refreshAccounts={}
$script:transitions=0
& $handler
Assert ($script:transitions -eq 1 -and $message.Text -eq 'Mot de passe incorrect.' -and $d.DialogResult -ne 'OK' -and $password.Text -eq '') 'Echec : retour immédiat absent ou erreur absente'
Assert ($null -eq $script:scoliaSession -and -not $d.Busy) 'Une session a été ouverte après échec'
$password.Text='Correct123';& $handler
Assert ($script:transitions -eq 3 -and $d.DialogResult -eq 'OK' -and $null -ne $script:scoliaSession) 'La connexion réussie ne lance pas la session'
$script:scolia.Accounts[0].Locked=$false;$script:scoliaSession=$null
Connect-ScoliaAccount 'secretariat' ''
Assert ($null -ne $script:scoliaSession) 'Compte sans protection bloqué'
Write-Host 'PASS : champ requis sans tentative, vérification immédiate même en cas de mauvais mot de passe, succès avec indicateur, compte non protégé conservé.'
