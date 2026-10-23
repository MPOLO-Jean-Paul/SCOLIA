# Tests WinForms isoles ; le magasin de brouillons est simule en memoire.
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Windows.Forms,System.Drawing
. (Join-Path $PSScriptRoot 'InscriptionBrouillonsV55.ps1')
function Assert-DraftV55([bool]$Ok,[string]$Message){if(-not $Ok){throw $Message}}
$script:mockContext=[pscustomobject]@{Id='ecole-a';Year='2026-2027';LocalKey='utilisateur-a'}
$script:mockDocument=[pscustomobject]@{Schema=1;Context=$script:mockContext;Revision=0;Items=@()}
$script:mockWriteFailure=$false;$script:mockRemoveFailure=$false;$script:mockResume=$false;$script:mockProblems=@()
$script:mockPrompts=0;$script:busy=$false;$script:editMat='';$script:editTuteur=''
$script:data=[pscustomobject]@{learners=@()}
function Key($Value){return ([string]$Value).Trim().ToUpperInvariant()}
function Promo($Value){return (Key $Value).Replace(' ','').Replace('-','')}
function Get-ScoliaFeatureContext{return $script:mockContext}
function Read-ScoliaFeatureStore{param([string]$Name,[switch]$Local)
    Assert-DraftV55 ($Name -eq 'inscriptions' -and $Local) 'Le brouillon est sorti du magasin local.'
    return ($script:mockDocument | ConvertTo-Json -Depth 20 | ConvertFrom-Json)
}
function Invoke-ScoliaFeatureUpdate{param([string]$Name,[switch]$Local,[scriptblock]$Change)
    Assert-DraftV55 ($Name -eq 'inscriptions' -and $Local) 'Ecriture hors magasin local.'
    if($script:mockWriteFailure){throw 'Disque indisponible'}
    $copy=$script:mockDocument | ConvertTo-Json -Depth 20 | ConvertFrom-Json
    & $Change $copy
    if($script:mockRemoveFailure -and @($copy.Items).Count -lt @($script:mockDocument.Items).Count){throw 'Retrait impossible'}
    $copy.Revision=[int]$copy.Revision+1;$script:mockDocument=$copy
    return $copy
}
function Confirm-ScoliaRegistrationDraftResume($Dialog,$Draft){$script:mockPrompts++;return $script:mockResume}
function Show-ScoliaRegistrationDraftProblem($State,[string]$Message){$State.LastError=$Message;$script:mockProblems+=,$Message}
function Get-ITMDossierFingerprint{return (@($script:editControls.Keys | Sort-Object | ForEach-Object {$_+'='+$script:editControls[$_].Text}) | ConvertTo-Json -Compress)}
function New-DraftTestForm {
    $dialog=New-Object Windows.Forms.Form;$dialog.Text='SCOLIA | Nouvelle inscription'
    $script:editDialog=$dialog;$script:editControls=@{};$script:editFeedback=New-Object Windows.Forms.Label;$dialog.Controls.Add($script:editFeedback)
    foreach($name in $script:registrationDraftFields){
        if($name -in @('Sexe','Promotion','Annee','Statut')){
            $control=New-Object Windows.Forms.ComboBox;$control.DropDownStyle='DropDownList'
            $choices=switch($name){'Sexe'{@('M','F')};'Promotion'{@('INF1','INF2')};'Annee'{@('2026-2027')};'Statut'{@('Actif','Suspendu','Sorti')}}
            foreach($choice in $choices){[void]$control.Items.Add($choice)}
            if($name -in @('Annee','Statut')){$control.SelectedIndex=0}
        }elseif($name -eq 'Naissance'){
            $control=New-Object Windows.Forms.MaskedTextBox;$control.Mask='00\/00\/0000';$control.TextMaskFormat='IncludeLiterals'
        }else{$control=New-Object Windows.Forms.TextBox}
        $dialog.Controls.Add($control);$script:editControls[$name]=$control
    }
    $script:editDone=$false;$script:editOriginal=Get-ITMDossierFingerprint
    return ,$dialog
}
function Set-DraftTestValues {
    foreach($pair in @(@('Nom','NSEYA'),@('PostNom','KABONGO'),@('Prenom','JEAN'),@('Sexe','M'),@('Naissance','01/09/2006'),@('Lieu','Kinshasa'),@('Contact1','0812345678'),@('Contact2','0898765432'),@('Promotion','INF1'),@('Annee','2026-2027'),@('Statut','Actif'),@('Adresse','Avenue des écoles 12'))){$script:editControls[$pair[0]].Text=$pair[1]}
}
$dialog=$null
try{
    $dialog=New-DraftTestForm;Enable-ScoliaRegistrationDraft $dialog
    $state=$script:registrationDraftState
    Assert-DraftV55 ($null -ne $state.Timer) 'Autosauvegarde absente.'
    Assert-DraftV55 (Save-ScoliaRegistrationDraft $state -Force) 'Formulaire vide refuse.'
    Assert-DraftV55 (@($script:mockDocument.Items).Count -eq 0) 'Un formulaire vide est sauvegarde.'
    Set-DraftTestValues
    $fingerprint=Get-ITMDossierFingerprint
    $script:mockWriteFailure=$true
    Assert-DraftV55 (-not (Save-ScoliaRegistrationDraft $state -Force)) 'Un echec de disque a ete masque.'
    Assert-DraftV55 ((Get-ITMDossierFingerprint) -eq $fingerprint -and $script:mockProblems.Count -gt 0) 'Echec non signale ou champs perdus.'
    $script:mockWriteFailure=$false
    Assert-DraftV55 (Save-ScoliaRegistrationDraft $state -Force) 'La nouvelle tentative a echoue.'
    $draft=$script:mockDocument.Items[0]
    Assert-DraftV55 (@($draft.Fields.PSObject.Properties).Count -eq 12) 'Mapping incomplet des douze champs.'
    foreach($name in $script:registrationDraftFields){Assert-DraftV55 ([string]$draft.Fields.$name -eq [string]$script:editControls[$name].Text) ('Champ perdu : '+$name)}
    Assert-DraftV55 ($draft.Fields.Contact1 -eq '0812345678') 'Le zero initial du contact est perdu.'
    Assert-DraftV55 (-not $script:editDone -and -not $script:editMat) 'Le brouillon a finalise implicitement un dossier.'
    $malformed=$draft | ConvertTo-Json -Depth 20 | ConvertFrom-Json
    $malformed.Fields.PSObject.Properties.Remove('Contact1');$malformedRejected=$false
    try{Restore-ScoliaRegistrationDraft $state $malformed}catch{$malformedRejected=$true}
    Assert-DraftV55 ($malformedRejected -and (Get-ITMDossierFingerprint) -eq $fingerprint) 'Un brouillon incomplet a efface des champs.'
    # Un changement de base/annee/utilisateur pendant la saisie ne peut jamais ecrire ailleurs.
    foreach($property in @('Id','Year','LocalKey')){
        $before=$script:mockDocument | ConvertTo-Json -Depth 20 -Compress
        $originalContext=$script:mockContext
        $script:mockContext=[pscustomobject]@{Id=$originalContext.Id;Year=$originalContext.Year;LocalKey=$originalContext.LocalKey}
        $script:mockContext.$property=[string]$script:mockContext.$property+'-change'
        Assert-DraftV55 (-not (Save-ScoliaRegistrationDraft $state -Force)) ('Changement accepte : '+$property)
        $removeRejected=$false
        try{Remove-ScoliaRegistrationDraftItems $state @($draft.Id)}catch{$removeRejected=$true}
        Assert-DraftV55 $removeRejected ('Suppression dans un autre contexte : '+$property)
        Assert-DraftV55 (-not (Complete-ScoliaRegistrationDraft 'ACC-OTHER')) ('Finalisation dans un autre contexte : '+$property)
        Assert-DraftV55 ($state.DraftId -eq $draft.Id -and $state.PendingRemoval.Count -eq 0) 'La finalisation refusee a programme une suppression differee.'
        Assert-DraftV55 (($script:mockDocument | ConvertTo-Json -Depth 20 -Compress) -eq $before) ('Magasin change apres changement de contexte : '+$property)
        Assert-DraftV55 ((Get-ITMDossierFingerprint) -eq $fingerprint) 'Champs perdus au changement de contexte.'
        $script:mockContext=$originalContext
    }
    $timer=$state.Timer;$dialog.Dispose();$dialog=$null
    Assert-DraftV55 ($null -eq $script:registrationDraftState -and -not $timer.Enabled) 'Le timer survit au formulaire.'
    # Refuser la reprise ne pre-remplit ni ne detruit le brouillon precedent.
    $dialog=New-DraftTestForm;$script:mockResume=$false;Enable-ScoliaRegistrationDraft $dialog
    Assert-DraftV55 (-not $script:editControls.Nom.Text -and @($script:mockDocument.Items).Count -eq 1) 'Le refus a rempli ou detruit un brouillon.'
    $script:editControls.Nom.Text='NOUVEAU BROUILLON'
    Assert-DraftV55 (Save-ScoliaRegistrationDraft -Force) 'Le nouveau brouillon a echoue.'
    Assert-DraftV55 (@($script:mockDocument.Items).Count -eq 2 -and @($script:mockDocument.Items | Where-Object {$_.Id -eq $draft.Id}).Count -eq 1) 'Le nouveau brouillon a ecrase celui refuse.'
    Assert-DraftV55 (Complete-ScoliaRegistrationDraft 'ACC998') 'Le brouillon nouveau ne peut pas etre nettoye.'
    Assert-DraftV55 (@($script:mockDocument.Items).Count -eq 1 -and $script:mockDocument.Items[0].Id -eq $draft.Id) 'Le nettoyage a supprime un autre brouillon.'
    $dialog.Dispose();$dialog=$null
    # Les trois composantes du contexte doivent correspondre avant la proposition.
    foreach($property in @('Id','Year','LocalKey')){
        $previous=[string]$script:mockContext.$property;$script:mockContext.$property=$previous+'-autre'
        $prompts=$script:mockPrompts;$dialog=New-DraftTestForm;Enable-ScoliaRegistrationDraft $dialog
        Assert-DraftV55 ($script:mockPrompts -eq $prompts -and -not $script:editControls.Nom.Text) ('Fuite de contexte : '+$property)
        $dialog.Dispose();$dialog=$null;$script:mockContext.$property=$previous
    }
    $dialog=New-DraftTestForm;$script:mockResume=$true;Enable-ScoliaRegistrationDraft $dialog
    foreach($name in $script:registrationDraftFields){Assert-DraftV55 ([string]$script:editControls[$name].Text -eq [string]$draft.Fields.$name) ('Reprise incomplete : '+$name)}
    Assert-DraftV55 (-not $script:editDone) 'La reprise a valide le dossier.'
    Assert-DraftV55 (Complete-ScoliaRegistrationDraft 'ACC999') 'Le nettoyage apres inscription a echoue.'
    Assert-DraftV55 (@($script:mockDocument.Items).Count -eq 0) 'Le brouillon a survecu a une inscription reussie.'
    # Simulation : la sauvegarde metier a reussi puis le rafraichissement echoue.
    try{throw 'Actualisation impossible'}catch{}
    [void](Save-ScoliaRegistrationDraft -Force)
    Assert-DraftV55 (@($script:mockDocument.Items).Count -eq 0) 'Un echec de rafraichissement a recree le brouillon inscrit.'
    # Le prochain apprenant reste autosauvegarde apres la reinitialisation.
    foreach($name in @('Nom','PostNom','Prenom','Naissance','Lieu','Contact1','Contact2','Adresse')){$script:editControls[$name].Clear()}
    [void](Save-ScoliaRegistrationDraft -Force)
    $script:editControls.Nom.Text='AUTRE'
    Assert-DraftV55 (Save-ScoliaRegistrationDraft -Force) 'Le prochain apprenant ne peut plus etre sauvegarde.'
    $script:mockRemoveFailure=$true
    Assert-DraftV55 (-not (Complete-ScoliaRegistrationDraft 'ACC1000')) 'Le retrait impossible a ete masque.'
    Assert-DraftV55 ($script:mockDocument.Items[0].State -eq 'Saved' -and $script:mockDocument.Items[0].SavedMatricule -eq 'ACC1000') 'Marqueur anti-duplication absent.'
    $dialog.Dispose();$dialog=$null
    $prompts=$script:mockPrompts;$dialog=New-DraftTestForm;Enable-ScoliaRegistrationDraft $dialog
    Assert-DraftV55 ($script:mockPrompts -eq $prompts) 'Un brouillon deja inscrit est propose a nouveau.'
    $script:mockRemoveFailure=$false
    Assert-DraftV55 (Save-ScoliaRegistrationDraft -Force) 'Le retrait differe ne peut pas reprendre.'
    Assert-DraftV55 (@($script:mockDocument.Items).Count -eq 0) 'Le marqueur en attente a survecu au retrait reussi.'
    $dialog.Dispose();$dialog=$null
    # Redemarrage : meme si le marqueur et le retrait avaient tous deux echoue,
    # l'identite deja inscrite dans les donnees rechargees interdit la reprise.
    $script:data.learners=@();$dialog=New-DraftTestForm;Enable-ScoliaRegistrationDraft $dialog;Set-DraftTestValues
    Assert-DraftV55 (Save-ScoliaRegistrationDraft -Force) 'Preparation du test de redemarrage impossible.'
    $restartDraft=$script:mockDocument.Items[0]
    $script:mockWriteFailure=$true
    Assert-DraftV55 (-not (Complete-ScoliaRegistrationDraft 'ACC2000')) 'La double erreur de disque a ete masquee.'
    Assert-DraftV55 ($script:mockDocument.Items[0].State -eq 'Draft') 'Le magasin en erreur a ete modifie.'
    $dialog.Dispose();$dialog=$null;$script:registrationDraftCommitted=@{}
    $registered=$restartDraft.Fields | ConvertTo-Json -Depth 5 | ConvertFrom-Json
    $registered | Add-Member -NotePropertyName Matricule -NotePropertyValue 'ACC2000'
    $script:data.learners=@($registered)
    Assert-DraftV55 (Test-ScoliaRegistrationDraftAlreadyRegistered $restartDraft) 'Inscription existante ignoree apres redemarrage.'
    $script:mockWriteFailure=$false;$prompts=$script:mockPrompts
    $dialog=New-DraftTestForm;Enable-ScoliaRegistrationDraft $dialog
    Assert-DraftV55 ($script:mockPrompts -eq $prompts -and -not $script:editControls.Nom.Text) 'Le redemarrage propose une seconde inscription.'
    Assert-DraftV55 ($script:mockDocument.Items[0].State -eq 'Saved' -and $dialog.Text.Contains('ACC2000')) 'Le dossier existant est mal signale.'
    $dialog.Dispose();$dialog=$null;$script:data.learners=@()
    # Une modification d'un dossier existant n'installe aucun timer de brouillon.
    $script:editMat='ACC101';$dialog=New-DraftTestForm;Enable-ScoliaRegistrationDraft $dialog
    Assert-DraftV55 ($null -eq $script:registrationDraftState) 'Un dossier existant est autosauvegarde comme inscription.'
    Write-Output 'PASS V55 : mapping complet, sauvegarde locale, reprise volontaire, isolation, erreurs, succes et anti-duplication.'
}finally{if($dialog){$dialog.Dispose()};$script:editMat=''}
