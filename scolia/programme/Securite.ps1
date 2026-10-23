if(-not (Get-Command Import-ITMCompiledSource -ErrorAction SilentlyContinue)){. (Join-Path $PSScriptRoot 'CompilationCache.ps1')}
Add-Type -AssemblyName System.Security
Import-ITMCompiledSource -Path (Join-Path $PSScriptRoot 'SecurityCrypto.cs')
$script:securityFile=Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'ITM_CD\securite.dat'
$script:security=$null;$script:securityLocked=$false;$script:securityTimer=$null
function New-ITMSecret([string]$password) {
    $salt=[ITMSecurityCrypto]::Random(32)
    return [pscustomobject]@{Salt=[Convert]::ToBase64String($salt);Hash=[Convert]::ToBase64String([ITMSecurityCrypto]::Derive($password,$salt,600000))}
}
function Test-ITMSecret([string]$password,$secret) {
    if(-not $secret){return $false}
    return [ITMSecurityCrypto]::Equal([ITMSecurityCrypto]::Derive($password,[Convert]::FromBase64String($secret.Salt),600000),[Convert]::FromBase64String($secret.Hash))
}
function Save-ITMSecurity {
    if($script:scolia){Write-ITMUserAudit 'Événement de sécurité SCOLIA';return}
    $folder=[IO.Path]::GetDirectoryName($script:securityFile);[void][IO.Directory]::CreateDirectory($folder)
    $plain=[Text.Encoding]::UTF8.GetBytes(($script:security | ConvertTo-Json -Depth 8 -Compress))
    $encrypted=[Security.Cryptography.ProtectedData]::Protect($plain,$null,[Security.Cryptography.DataProtectionScope]::CurrentUser)
    [Array]::Clear($plain,0,$plain.Length)
    $tmp=$script:securityFile+'.'+[Guid]::NewGuid().ToString('N')+'.tmp'
    try {
        [IO.File]::WriteAllBytes($tmp,$encrypted)
        [ITMSecurityCrypto]::CommitProtectedFile($tmp,$script:securityFile)
    }catch{throw ('Paramètres non enregistrés. La configuration précédente est conservée. '+$_.Exception.Message)}finally{try{if([IO.File]::Exists($tmp)){[IO.File]::Delete($tmp)}}catch{}}
}
function Add-ITMSecurityEvent([string]$action) {
    $script:security.Events=@(@($script:security.Events)+[pscustomobject]@{Date=(Get-Date -Format 'yyyy-MM-dd HH:mm:ss');Action=$action} | Select-Object -Last 30)
    Save-ITMSecurity
}
function Initialize-ITMSecurity {
    if([IO.File]::Exists($script:securityFile)){
        try{
            $bytes=[Security.Cryptography.ProtectedData]::Unprotect([IO.File]::ReadAllBytes($script:securityFile),$null,[Security.Cryptography.DataProtectionScope]::CurrentUser)
            try{$script:security=[Text.Encoding]::UTF8.GetString($bytes) | ConvertFrom-Json}finally{[Array]::Clear($bytes,0,$bytes.Length)}
            foreach($field in @('Version','Enabled','Password','Recovery','IdleMinutes','Minimize','Failures','BlockedUntil','Events')){if(-not $script:security.PSObject.Properties[$field]){throw 'Paramètre manquant.'}}
            if($script:security.Enabled -isnot [bool] -or $script:security.Minimize -isnot [bool] -or @($script:security.Events).Count -gt 30){throw 'Types de paramètres incorrects.'}
            if($script:security.BlockedUntil){[void][DateTime]::Parse($script:security.BlockedUntil)}
            if($script:security.Version -ne 1 -or $script:security.IdleMinutes -notin @(0,5,15,30) -or [int]$script:security.Failures -lt 0){throw 'Configuration incorrecte.'}
            if($script:security.Enabled){
                foreach($secret in @($script:security.Password,$script:security.Recovery)){
                    if([Convert]::FromBase64String($secret.Salt).Length -ne 32 -or [Convert]::FromBase64String($secret.Hash).Length -ne 32){throw 'Empreinte incorrecte.'}
                }
            }
        }catch{throw 'Les paramètres de sécurité sont illisibles. Accès interrompu. Utilisez le compte Windows qui a activé cette protection et conservez le fichier de configuration pour assistance.'}
    }else{
        $script:security=[pscustomobject]@{Version=1;Enabled=$false;Password=$null;Recovery=$null;IdleMinutes=15;Minimize=$false;Failures=0;BlockedUntil='';Events=@()}
    }
}
function Test-ITMAuthentication([string]$value,[bool]$recovery=$false) {
    if([string]::IsNullOrWhiteSpace($value)){throw 'Renseignez le mot de passe ou le code de récupération.'}
    if($script:security.BlockedUntil -and [DateTime]::UtcNow -lt [DateTime]::Parse($script:security.BlockedUntil).ToUniversalTime()){throw 'Trop de tentatives. Patientez jusqu''à cinq minutes avant de réessayer.'}
    $secret=$(if($recovery){$script:security.Recovery}else{$script:security.Password})
    if(Test-ITMSecret $value $secret){
        $script:security.Failures=0;$script:security.BlockedUntil='';Add-ITMSecurityEvent $(if($recovery){'Code de récupération validé'}else{'Authentification réussie'});return $true
    }
    $script:security.Failures=[int]$script:security.Failures+1
    if($script:security.Failures -ge 5){$script:security.Failures=0;$script:security.BlockedUntil=[DateTime]::UtcNow.AddMinutes(5).ToString('o')}
    Add-ITMSecurityEvent 'Authentification refusée'
    return $false
}
function New-ITMSecurityDialog([string]$title) {
    $d=New-Object Windows.Forms.Form;Set-ITMWindowIcon $d;$d.Text='SCOLIA | '+$title;$d.Size=New-Object Drawing.Size(650,580)
    $d.MinimumSize=New-Object Drawing.Size(520,460);$d.StartPosition='CenterScreen';$d.BackColor=[Drawing.Color]::FromArgb(245,245,245)
    $d.Font=New-Object Drawing.Font('Segoe UI',10)
    $logo=New-Object Windows.Forms.PictureBox;$logo.Image=$script:schoolLogo;$logo.SizeMode='Zoom';$logo.SetBounds(24,20,44,44);$d.Controls.Add($logo)
    [void](New-Label $d 'ITM CD · Accès sécurisé' 82 22 512 42 18 $true)
    [void](New-Label $d $title 24 74 570 52 11)
    return $d
}
function New-ITMPasswordBox($parent,[string]$label,[int]$y) {
    [void](New-Label $parent $label 24 $y 570 24 10 $true)
    $box=New-Object Windows.Forms.TextBox;$box.SetBounds(24,($y+30),570,32);$box.UseSystemPasswordChar=$true;$box.MaxLength=128;$parent.Controls.Add($box);return $box
}
function Show-ITMRecoveryCode([string]$code) {
    $d=New-ITMSecurityDialog 'Conservez ce code hors de cet ordinateur. Il permet de remplacer un mot de passe oublié.'
    $box=New-Object Windows.Forms.TextBox;$box.Multiline=$true;$box.ReadOnly=$true;$box.SetBounds(24,146,570,86);$box.Text=$code;$d.Controls.Add($box)
    [void](New-Label $d 'Ce code sera affiché une seule fois. Un nouveau code remplace le précédent.' 24 250 570 64 11)
    $ok=New-Button $d 'Fermer après conservation du code' 24 348 570 $true;$ok.DialogResult='OK'
    Enable-ITMDialogResponsive $d
    try{[void]$d.ShowDialog()}finally{$box.Clear();$d.Dispose()}
}
function Set-ITMNewPassword([string]$password,[bool]$activate=$true) {
    if($password.Length -lt 4 -or $password.Length -gt 128){throw 'Le mot de passe doit contenir de 4 à 128 caractères.'}
    $newPassword=New-ITMSecret $password
    $code=[Convert]::ToBase64String([ITMSecurityCrypto]::Random(24)).Replace('+','-').Replace('/','_').TrimEnd('=')
    $newRecovery=New-ITMSecret $code
    $old=$script:security | ConvertTo-Json -Depth 8
    try{
        $script:security.Password=$newPassword;$script:security.Recovery=$newRecovery;$script:security.Enabled=$activate
        $script:security.Failures=0;$script:security.BlockedUntil='';Add-ITMSecurityEvent 'Mot de passe défini et code renouvelé'
    }catch{$script:security=$old | ConvertFrom-Json;throw}
    Show-ITMRecoveryCode $code
}
function Show-ITMPasswordChange([bool]$recover=$false) {
    if($script:main.Visible){Assert-ITMPermission 'Administrer'}
    $d=New-ITMSecurityDialog $(if($recover){'Récupération avec votre code personnel'}elseif($script:security.Enabled){'Modifier votre mot de passe'}else{'Activer la protection de votre application'})
    $current=New-ITMPasswordBox $d $(if($recover){'Code de récupération'}else{'Mot de passe actuel (si la protection est active)'}) 132
    $current.Enabled=$recover -or $script:security.Enabled
    $new=New-ITMPasswordBox $d 'Nouveau mot de passe : de 4 à 128 caractères' 211
    $confirm=New-ITMPasswordBox $d 'Confirmer le nouveau mot de passe' 290
    $errorLabel=New-Label $d '' 24 368 570 70 10;$errorLabel.ForeColor=[Drawing.Color]::FromArgb(185,28,28)
    $save=New-Button $d 'Enregistrer et protéger' 24 454 340 $true
    $cancel=New-Button $d 'Annuler' 380 454 214;$cancel.DialogResult='Cancel';$d.CancelButton=$cancel
    $save.Add_Click({
        $save.Enabled=$false;$d.UseWaitCursor=$false
        try{
            Start-ITMActivity 'Vérification et préparation de la protection...' $save
            if($new.Text.Length -lt 4 -or $new.Text -ne $confirm.Text){throw 'Utilisez au moins 4 caractères et une confirmation identique.'}
            if(($script:security.Enabled -or $recover) -and -not (Test-ITMAuthentication $current.Text $recover)){throw 'Mot de passe ou code incorrect.'}
            Set-ITMNewPassword $new.Text
            $d.DialogResult='OK';$d.Close()
        }catch{$errorLabel.Text=$_.Exception.Message}finally{Stop-ITMActivity;$save.Enabled=$true;$d.UseWaitCursor=$false}
    })
    Enable-ITMDialogResponsive $d
    try{return ($d.ShowDialog() -eq 'OK')}finally{$current.Clear();$new.Clear();$confirm.Clear();$d.Dispose()}
}
function Show-ITMLogin([string]$reason='Renseignez votre mot de passe pour accéder à l''accueil.') {
    if($script:scolia){return Show-ScoliaLogin}
    if(-not $script:security.Enabled){return $true}
    $d=New-Object ITMLoginForm;Set-ITMWindowIcon $d
    $d.SetInstitution((Get-ITMInstitutionName));$d.SetBrand($script:schoolLogo,$reason)
    Apply-ITMThemeForm $d
    $password=$d.Password;$show=$d.Reveal;$errorLabel=$d.MessageLabel
    $ok=$d.SignIn;$quit=$d.CancelAction;$recover=$d.Recover
    Set-ITMCommandHelp $ok $null;Set-ITMCommandHelp $quit $null;Enable-ITMDialogShortcuts $d
    $show.Add_CheckedChanged({$password.UseSystemPasswordChar=-not $show.Checked})
    $ok.Add_Click({
        if(-not $ok.Enabled -or [string]::IsNullOrWhiteSpace($password.Text)){return}
        $d.SetBusy($true)
        try{if(Test-ITMAuthentication $password.Text){Start-ITMActivity 'Accès validé. Ouverture de votre session…' $ok;$d.DialogResult='OK';$d.Close()}else{$password.Clear();$errorLabel.Text='Mot de passe incorrect.'}}
        catch{$failure=$_.Exception.Message;$password.Clear();$errorLabel.Text=$failure}finally{Stop-ITMActivity;if($d.DialogResult -ne 'OK'){$d.SetBusy($false);$password.Focus()};$errorLabel.AccessibleDescription=$errorLabel.Text}
    })
    $recover.Add_LinkClicked({try{if(Show-ITMPasswordChange $true){$d.DialogResult='OK';$d.Close()}}catch{$errorLabel.Text=$_.Exception.Message}})
    $d.Add_Shown({$password.Focus()})
    try{return ($d.ShowDialog() -eq 'OK')}finally{$password.Clear();$d.Dispose()}
}
function Lock-ITMSession {
    if($script:securityLocked -or $script:busy -or -not $script:security.Enabled){return}
    $script:securityLocked=$true;$script:securityTimer.Stop()
    $shield=New-Object Windows.Forms.Panel;$shield.BackColor=[Drawing.Color]::White
    $shield.SetBounds(0,0,$script:main.ClientSize.Width,$script:main.ClientSize.Height);$shield.Anchor='Top,Bottom,Left,Right'
    $script:main.Controls.Add($shield);$shield.BringToFront()
    [void](New-Label $shield 'Session verrouillée' 24 30 500 46 20 $true)
    try{
        Add-ITMSecurityEvent 'Session verrouillée'
        if(Show-ITMLogin 'Session verrouillée. Authentifiez-vous pour reprendre votre travail.'){
            $script:main.WindowState='Normal';$script:main.Activate();Update-ITMSecurityPage
        }else{$script:main.Close()}
    }catch{
        [void][ITMErrorDialog]::Show($script:main,'La vérification de sécurité a échoué. L''application va se fermer.','ITM CD');$script:main.Close()
    }finally{$shield.Dispose();$script:securityLocked=$false;if(-not $script:main.IsDisposed){$script:securityTimer.Start()}}
}
function Update-ITMSecurityPage {
    $script:securityState.Text=$(if($script:security.Enabled){'Protection active · Mot de passe requis au démarrage'}else{'Protection désactivée · Accès direct à l''accueil'})
    $script:securityEnable.Text=$(if($script:security.Enabled){'Modifier le mot de passe'}else{'Activer le mot de passe'})
    $script:securityDisable.Enabled=$script:security.Enabled;$script:securityLock.Enabled=$script:security.Enabled
    $script:securityIdle.SelectedItem=[string]$script:security.IdleMinutes;$script:securityMinimize.Checked=[bool]$script:security.Minimize
    Set-Grid $script:securityGrid @($script:security.Events | Sort-Object Date -Descending) @(@('Date','Date'),@('Action','Événement'))
}
function Build-ITMSecurityPage($page) {
    [void](New-Label $page 'Sécurité et accès à l''application' 24 20 920 42 20 $true)
    $script:securityState=New-Label $page '' 24 80 920 54 12 $true
    $script:securityEnable=New-Button $page 'Activer le mot de passe' 24 150 360 $true
    $script:securityDisable=New-Button $page 'Désactiver la protection' 400 150 330
    $script:securityLock=New-Button $page 'Verrouiller maintenant' 24 208 360
    [void](New-Label $page 'Verrouillage après inactivité du PC (minutes ; 0 = désactivé)' 24 280 920 30 10 $true)
    $script:securityIdle=New-Combo $page 24 324 180;Set-Choices $script:securityIdle @('0','5','15','30') '15'
    $script:securityMinimize=New-Object ITMReadableCheckBox;$script:securityMinimize.Text='Verrouiller aussi quand la fenêtre est réduite';$script:securityMinimize.SetBounds(230,324,700,34);$page.Controls.Add($script:securityMinimize)
    $save=New-Button $page 'Enregistrer les préférences' 24 380 360 $true;$script:securitySave=$save
    [void](New-Label $page 'Le code de récupération est renouvelé à chaque changement de mot de passe. Protection locale pour cet utilisateur Windows ; le fichier Excel reste accessible en dehors de l''application.' 24 440 920 86 11)
    $script:securityGrid=New-Grid $page 24 546 920 240
    $securityEnable.Add_Click({try{if(-not $script:busy){[void](Show-ITMPasswordChange);Update-ITMSecurityPage}}catch{App-Error $_}})
    $securityDisable.Add_Click({try{Assert-ITMPermission 'Administrer';if(-not $script:busy -and (Show-ITMLogin 'Confirmez votre mot de passe pour désactiver la protection.')){
        $script:security.Enabled=$false
        try{Add-ITMSecurityEvent 'Protection désactivée'}catch{$script:security.Enabled=$true;throw}
        Update-ITMSecurityPage
    }}catch{App-Error $_}})
    $securityLock.Add_Click({Lock-ITMSession})
    $save.Add_Click({try{Assert-ITMPermission 'Administrer';if(-not $script:busy){
        $minutes=[int]$script:securityIdle.Text;$minimize=[bool]$script:securityMinimize.Checked
        if($script:security.Enabled -and -not (Show-ITMLogin 'Confirmez votre mot de passe pour modifier les préférences.')){Update-ITMSecurityPage;return}
        $oldIdle=$script:security.IdleMinutes;$oldMinimize=$script:security.Minimize
        $script:security.IdleMinutes=$minutes;$script:security.Minimize=$minimize
        try{Add-ITMSecurityEvent 'Préférences modifiées'}catch{$script:security.IdleMinutes=$oldIdle;$script:security.Minimize=$oldMinimize;throw}
        Update-ITMSecurityPage;$script:status.Text='Préférences de sécurité enregistrées.'
    }}catch{App-Error $_}})
    Update-ITMSecurityPage
    $script:securityTimer=New-Object Windows.Forms.Timer;$script:securityTimer.Interval=1500
    $script:securityTimer.Add_Tick({
        if($script:security.Enabled -and -not $script:busy -and -not $script:securityLocked -and (-not [Windows.Forms.Form]::ActiveForm -or [Windows.Forms.Form]::ActiveForm -eq $script:main)){
            if(($script:security.Minimize -and $script:main.WindowState -eq 'Minimized') -or ($script:security.IdleMinutes -gt 0 -and [ITMSecurityCrypto]::IdleMilliseconds() -ge ([int]$script:security.IdleMinutes*60000))){Lock-ITMSession}
        }
    })
    $script:main.Add_Resize({if($script:security -and $script:security.Minimize -and $script:main.WindowState -eq 'Minimized' -and -not $script:busy){Lock-ITMSession}})
}
