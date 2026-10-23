# Local identities for the current Windows profile. Existing security gate stays mandatory.
$script:accountsFile=Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'ITM_CD\comptes.dat'
$script:accounts=$null;$script:accountSession=$null
function Save-ITMAccounts {
    [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($script:accountsFile))
    $lock=$null;$temp='';$previousRevision=[int]$script:accounts.Revision
    try {
        $lock=[IO.File]::Open($script:accountsFile+'.lock',[IO.FileMode]::OpenOrCreate,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None)
        if([IO.File]::Exists($script:accountsFile)){
            $stored=[Security.Cryptography.ProtectedData]::Unprotect([IO.File]::ReadAllBytes($script:accountsFile),$null,[Security.Cryptography.DataProtectionScope]::CurrentUser)
            try{$disk=[Text.Encoding]::UTF8.GetString($stored) | ConvertFrom-Json}finally{[Array]::Clear($stored,0,$stored.Length)}
            if([int]$disk.Revision -ne $previousRevision){throw 'Les comptes ont changé dans une autre session. Fermez puis relancez cette application avant de continuer.'}
        }
        $script:accounts.Revision=$previousRevision+1
        $plain=[Text.Encoding]::UTF8.GetBytes(($script:accounts | ConvertTo-Json -Depth 10 -Compress))
        try{$encrypted=[Security.Cryptography.ProtectedData]::Protect($plain,$null,[Security.Cryptography.DataProtectionScope]::CurrentUser)}finally{[Array]::Clear($plain,0,$plain.Length)}
        $temp=$script:accountsFile+'.'+[Guid]::NewGuid().ToString('N')+'.tmp'
        [IO.File]::WriteAllBytes($temp,$encrypted);[ITMSecurityCrypto]::CommitProtectedFile($temp,$script:accountsFile)
    }catch{$script:accounts.Revision=$previousRevision;throw}
    finally{if($temp -and [IO.File]::Exists($temp)){[IO.File]::Delete($temp)};if($lock){$lock.Dispose()}}
}
function Initialize-ITMAccounts {
    $script:accounts=[pscustomobject]@{Version=1;Revision=0;Users=@();LastKind='Windows';LastUser='';Failures=0;BlockedUntil=''}
    if([IO.File]::Exists($script:accountsFile)){
        try{
            $plain=[Security.Cryptography.ProtectedData]::Unprotect([IO.File]::ReadAllBytes($script:accountsFile),$null,[Security.Cryptography.DataProtectionScope]::CurrentUser)
            try{$saved=[Text.Encoding]::UTF8.GetString($plain) | ConvertFrom-Json}finally{[Array]::Clear($plain,0,$plain.Length)}
            foreach($field in @('Version','Revision','Users','LastKind','LastUser','Failures','BlockedUntil')){if(-not $saved.PSObject.Properties[$field]){throw 'Champ absent.'}}
            if($saved.Version -ne 1 -or [int]$saved.Revision -lt 0 -or $saved.LastKind -notin @('Windows','Application') -or @($saved.Users).Count -gt 50 -or [int]$saved.Failures -lt 0){throw 'Configuration invalide.'}
            if($saved.BlockedUntil){[void][DateTime]::Parse($saved.BlockedUntil)}
            $names=@{}
            foreach($user in @($saved.Users)){
                if([string]$user.Username -notmatch '^[a-zA-Z0-9._-]{3,32}$' -or [string]::IsNullOrWhiteSpace([string]$user.DisplayName)){throw 'Compte invalide.'}
                $key=([string]$user.Username).ToUpperInvariant();if($names.ContainsKey($key)){throw 'Compte en double.'};$names[$key]=$true
                foreach($secret in @($user.Password,$user.Recovery)){if([Convert]::FromBase64String($secret.Salt).Length -ne 32 -or [Convert]::FromBase64String($secret.Hash).Length -ne 32){throw 'Empreinte invalide.'}}
            }
            $migrate=$false;$first=$true
            foreach($u in @($saved.Users)){
                if(-not $u.PSObject.Properties['Role']){Add-Member -InputObject $u -NotePropertyName Role -NotePropertyValue $(if($first){'Administrateur'}else{'Secrétariat'});$migrate=$true}
                if($u.Role -notin @('Administrateur','Secrétariat','Consultation')){throw 'Rôle invalide.'};$first=$false
            }
            if(@($saved.Users).Count -gt 0 -and @($saved.Users | Where-Object {$_.Role -eq 'Administrateur'}).Count -eq 0){throw 'Aucun administrateur.'}
            $script:accounts=$saved;if($migrate){Save-ITMAccounts}
        }catch{throw 'Les comptes locaux sont illisibles. Utilisez leur compte Windows d''origine et conservez comptes.dat pour assistance. Aucun compte n''a été réinitialisé.'}
    }
    Set-ITMWindowsSession $false
}
function Update-ITMAccountButton {
    if($script:main -and $script:main.Visible){Update-WorkspaceState}
    if($script:accountButton){$script:accountButton.Text='Compte';$script:accountButton.AccessibleDescription='Compte connecté : '+$script:accountSession.Name+' ('+$script:accountSession.Kind+' · '+$script:accountSession.Role+')';if($script:commandHelp){$script:commandHelp.SetToolTip($script:accountButton,$script:accountButton.AccessibleDescription+"`r`nGérer les comptes — Ctrl+Alt+U")}}
}
function Set-ITMWindowsSession([bool]$remember=$true) {
    $identity=[Security.Principal.WindowsIdentity]::GetCurrent()
    try{$session=[pscustomobject]@{Kind='Windows';Name=$identity.Name;Id=$identity.User.Value;Role=$(if(@($script:accounts.Users).Count -eq 0){'Administrateur'}else{'Consultation'})}}finally{$identity.Dispose()}
    if($remember -and @($script:accounts.Users).Count -gt 0){Assert-ITMPermission 'Administrer';$session.Role='Administrateur'}
    if($remember){
        $oldKind=$script:accounts.LastKind;$oldUser=$script:accounts.LastUser
        try{$script:accounts.LastKind='Windows';$script:accounts.LastUser='';Save-ITMAccounts}catch{$script:accounts.LastKind=$oldKind;$script:accounts.LastUser=$oldUser;throw}
    }
    $script:accountSession=$session;Update-ITMAccountButton
}
function Find-ITMLocalAccount([string]$username) {return @($script:accounts.Users | Where-Object {$_.Username -ieq $username.Trim()}) | Select-Object -First 1}
function Test-ITMAccountAuthentication([string]$username,[string]$password,[bool]$recovery=$false) {
    if([string]::IsNullOrWhiteSpace($username) -or [string]::IsNullOrWhiteSpace($password)){throw "Renseignez l’identifiant et le mot de passe ou code de récupération."}
    if($script:accounts.BlockedUntil -and [DateTime]::UtcNow -lt [DateTime]::Parse($script:accounts.BlockedUntil).ToUniversalTime()){throw 'Trop de tentatives. Attendez cinq minutes avant de réessayer.'}
    $user=Find-ITMLocalAccount $username
    $valid=$false
    if($user){$valid=Test-ITMSecret $password $(if($recovery){$user.Recovery}else{$user.Password})}
    else{[void][ITMSecurityCrypto]::Derive($password,(New-Object byte[] 32),600000)}
    $oldFailures=$script:accounts.Failures;$oldBlockedUntil=$script:accounts.BlockedUntil
    $script:accounts.Failures=$(if($valid){0}else{[int]$script:accounts.Failures+1})
    if($valid){$script:accounts.BlockedUntil=''}elseif($script:accounts.Failures -ge 5){$script:accounts.Failures=0;$script:accounts.BlockedUntil=[DateTime]::UtcNow.AddMinutes(5).ToString('o')}
    try{Save-ITMAccounts}catch{$script:accounts.Failures=$oldFailures;$script:accounts.BlockedUntil=$oldBlockedUntil;throw}
    return $valid
}
function Connect-ITMLocalAccount([string]$username,[string]$password) {
    if(-not (Test-ITMAccountAuthentication $username $password)){throw 'Identifiant ou mot de passe incorrect.'}
    $user=Find-ITMLocalAccount $username
    $oldKind=$script:accounts.LastKind;$oldUser=$script:accounts.LastUser
    try{$script:accounts.LastKind='Application';$script:accounts.LastUser=$user.Username;Save-ITMAccounts}catch{$script:accounts.LastKind=$oldKind;$script:accounts.LastUser=$oldUser;throw}
    $script:accountSession=[pscustomobject]@{Kind='Application';Name=$user.DisplayName;Id=$user.Username;Role=$user.Role};Update-ITMAccountButton;Write-ITMUserAudit 'Connexion locale'
}
function New-ITMAccountRecovery {return [Convert]::ToBase64String([ITMSecurityCrypto]::Random(24)).Replace('+','-').Replace('/','_').TrimEnd('=')}
function Set-ITMLocalAccount([string]$username,[string]$displayName,[string]$password,[string]$confirmation,[string]$recovery='') {
    $username=$username.Trim();$displayName=$displayName.Trim()
    if($username -notmatch '^[a-zA-Z0-9._-]{3,32}$'){throw 'Identifiant : 3 à 32 lettres, chiffres, points, tirets ou traits de soulignement.'}
    if($password.Length -lt 8 -or $password.Length -gt 128 -or $password -cne $confirmation){throw 'Mot de passe : 8 à 128 caractères et confirmation identique.'}
    $user=Find-ITMLocalAccount $username
    if($recovery){if(-not (Test-ITMAccountAuthentication $username $recovery $true)){throw 'Identifiant ou code de récupération incorrect.'}}
    else{
        if($user){throw 'Cet identifiant existe déjà. Connectez-vous ou choisissez un autre identifiant.'}
        if(@($script:accounts.Users).Count -ge 50){throw 'La limite de 50 comptes locaux est atteinte.'}
        if($displayName.Length -lt 2 -or $displayName.Length -gt 80){throw 'Nom affiché : 2 à 80 caractères.'}
    }
    $code=New-ITMAccountRecovery;$secret=New-ITMSecret $password;$reset=New-ITMSecret $code
    $before=$script:accounts | ConvertTo-Json -Depth 10
    try{
        if($recovery){$user.Password=$secret;$user.Recovery=$reset}
        else{$user=[pscustomobject]@{Username=$username;DisplayName=$displayName;Password=$secret;Recovery=$reset;Role=$(if(@($script:accounts.Users).Count -eq 0){'Administrateur'}else{'Consultation'})};$script:accounts.Users=@($script:accounts.Users)+@($user)}
        $script:accounts.LastKind='Application';$script:accounts.LastUser=$user.Username;Save-ITMAccounts
    }catch{$script:accounts=$before | ConvertFrom-Json;throw}
    $script:accountSession=[pscustomobject]@{Kind='Application';Name=$user.DisplayName;Id=$user.Username;Role=$user.Role};Update-ITMAccountButton
    return $code
}
function Show-ITMAccountRegistration([bool]$recover=$false) {
    $d=New-Object ITMAccountForm($true,$recover,$false);Set-ITMWindowIcon $d
    $d.SetInstitution((Get-ITMInstitutionName));$d.SetBrand($script:schoolLogo,'')
    $user=$d.Username;$name=$d.DisplayName;$password=$d.Password;$confirm=$d.Confirmation
    $message=$d.MessageLabel;$save=$d.Submit
    $save.Add_Click({
        $activityStarted=$false;$accountSaved=$false
        $d.SetBusy($true)
        try{
            Start-ITMActivity 'Création ou récupération du compte...' $save;$activityStarted=$true
            if($recover -and [string]::IsNullOrWhiteSpace($name.Text)){throw 'Saisissez le code de récupération.'}
            $code=Set-ITMLocalAccount $user.Text $name.Text $password.Text $confirm.Text $(if($recover){$name.Text}else{''})
            $accountSaved=$true;$save.Enabled=$false
            Stop-ITMActivity;$activityStarted=$false
            Show-ITMRecoveryCode $code;$d.DialogResult='OK';$d.Close()
        }catch{
            if($accountSaved){
                # Creation is durable: do not offer a second write when only the code window failed.
                $d.ShowSavedRecovery($code)
            }else{$message.Text=$_.Exception.Message}
        }finally{if($activityStarted){Stop-ITMActivity};if($d.DialogResult -ne 'OK' -and -not $accountSaved){$d.SetBusy($false)}}
    })
    Enable-ITMDialogShortcuts $d;Apply-ITMThemeForm $d
    try{return ($d.ShowDialog() -eq 'OK')}finally{$password.Clear();$confirm.Clear();$name.Clear();$d.Dispose()}
}
function Show-ITMAccountWindow([bool]$required=$false) {
    if($script:scolia){return Show-ScoliaLogin}
    $d=New-Object ITMAccountForm($false,$false,$required);Set-ITMWindowIcon $d
    $identity=$(if($script:accountSession){$script:accountSession.Name+' · '+$script:accountSession.Kind+' · '+$script:accountSession.Role}else{'Aucun compte connecté'})
    $d.SetInstitution((Get-ITMInstitutionName));$d.SetBrand($script:schoolLogo,$identity)
    $user=$d.Username;$user.Text=$script:accounts.LastUser;$password=$d.Password
    $message=$d.MessageLabel;$windows=$d.WindowsAction;$login=$d.Submit
    $register=$d.RegisterAction;$recover=$d.RecoverAction;$rolesButton=$d.RolesAction
    $rolesButton.Enabled=(Test-ITMPermission 'Administrer')
    if(-not $required){$d.LogoutAction.Add_Click({$d.DialogResult='Abort';$d.Close()})}
    $rolesButton.Add_Click({try{Show-ITMRoles}catch{$message.Text=$_.Exception.Message}finally{
        $d.SetInstitution((Get-ITMInstitutionName));$d.SetBrand($script:schoolLogo,($script:accountSession.Name+' · '+$script:accountSession.Kind+' · '+$script:accountSession.Role))
        $rolesButton.Enabled=(Test-ITMPermission 'Administrer')
    }})
    $windows.Add_Click({$previousSession=$script:accountSession;$d.SetBusy($true);try{
        if(@($script:accounts.Users).Count -gt 0 -and -not (Test-ITMPermission 'Administrer')){
            if(-not (Test-ITMAccountAuthentication $user.Text $password.Text)){throw 'Renseignez un compte administrateur et son mot de passe.'}
            $admin=Find-ITMLocalAccount $user.Text;if($admin.Role -ne 'Administrateur'){throw 'Un compte administrateur est nécessaire pour retrouver la session Windows.'}
            $script:accountSession=[pscustomobject]@{Kind='Application';Name=$admin.DisplayName;Id=$admin.Username;Role=$admin.Role}
        }
        Set-ITMWindowsSession;Write-ITMUserAudit 'Connexion Windows';Start-ITMActivity 'Accès validé. Ouverture de la session Windows…' $windows;$d.DialogResult='OK';$d.Close()}catch{$script:accountSession=$previousSession;Update-ITMAccountButton;$message.Text=$_.Exception.Message}finally{Stop-ITMActivity;if($d.DialogResult -ne 'OK'){$d.SetBusy($false)}}})
    $login.Add_Click({
        if(-not $login.Enabled){return}
        $d.SetBusy($true)
        try{Connect-ITMLocalAccount $user.Text $password.Text;Start-ITMActivity 'Connexion réussie. Ouverture de votre session…' $login;$d.DialogResult='OK';$d.Close()}
        catch{$failure=$_.Exception.Message;$password.Clear();$message.Text=$failure}finally{Stop-ITMActivity;if($d.DialogResult -ne 'OK'){$d.SetBusy($false);$password.Focus()}}
    })
    $register.Add_Click({try{if(Show-ITMAccountRegistration){$d.DialogResult='OK';$d.Close()}}catch{$message.Text=$_.Exception.Message}})
    $recover.Add_Click({try{if(Show-ITMAccountRegistration $true){$d.DialogResult='OK';$d.Close()}}catch{$message.Text=$_.Exception.Message}})
    Enable-ITMDialogShortcuts $d;Apply-ITMThemeForm $d
    try{$result=$d.ShowDialog();if($result -eq 'Abort'){$script:main.Close()};return ($result -eq 'OK')}finally{$password.Clear();$d.Dispose()}
}
function Start-ITMAccountSession {
    if($script:accounts.LastKind -eq 'Application'){return Show-ITMAccountWindow $true}
    Set-ITMWindowsSession $false;$script:accountSession.Role='Administrateur';Update-ITMAccountButton;return $true
}
