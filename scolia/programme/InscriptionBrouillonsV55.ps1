# Brouillons locaux : aucune inscription n'est creee sans Enregistrer le dossier.
$script:registrationDraftState=$null
$script:registrationDraftCommitted=@{}
$script:registrationDraftFields=@('Nom','PostNom','Prenom','Sexe','Naissance','Lieu','Contact1','Contact2','Promotion','Annee','Statut','Adresse')

function Get-ScoliaRegistrationDraftScope($Context) {
    foreach($name in @('Id','Year','LocalKey')){if([string]::IsNullOrWhiteSpace([string]$Context.$name)){throw 'Le contexte du brouillon est incomplet.'}}
    return (@([string]$Context.Id,[string]$Context.Year,[string]$Context.LocalKey) | ConvertTo-Json -Compress)
}
function Test-ScoliaRegistrationDraftContext($First,$Second) {
    if(-not $First -or -not $Second){return $false}
    foreach($name in @('Id','Year','LocalKey')){
        if([string]::IsNullOrWhiteSpace([string]$First.$name) -or -not [string]::Equals([string]$First.$name,[string]$Second.$name,[StringComparison]::Ordinal)){return $false}
    }
    return $true
}
function Assert-ScoliaRegistrationDraftCurrentContext($State) {
    $current=Get-ScoliaFeatureContext
    if(-not (Test-ScoliaRegistrationDraftContext $State.Context $current)){
        throw 'Le classeur, l''année ou le compte a changé. Revenez au contexte du formulaire avant de sauvegarder son brouillon.'
    }
}
function Get-ScoliaRegistrationDraftRegisteredMatricule($Draft,$Learners=$script:data.learners) {
    if(-not $Draft -or -not $Draft.Fields -or -not $Learners){return ''}
    if(-not (Test-ScoliaRegistrationDraftContext $Draft.Context (Get-ScoliaFeatureContext))){return ''}
    foreach($name in @('Nom','PostNom','Prenom','Promotion')){if([string]::IsNullOrWhiteSpace([string]$Draft.Fields.$name)){return ''}}
    $matches=@($Learners | Where-Object {
        $_ -and -not [string]::IsNullOrWhiteSpace([string]$_.Matricule) -and
        (Key $_.Nom) -eq (Key $Draft.Fields.Nom) -and
        (Key $_.PostNom) -eq (Key $Draft.Fields.PostNom) -and
        (Key $_.Prenom) -eq (Key $Draft.Fields.Prenom) -and
        (Promo $_.Promotion) -eq (Promo $Draft.Fields.Promotion)
    })
    return ((@($matches | ForEach-Object {[string]$_.Matricule} | Select-Object -Unique)) -join ', ')
}
function Test-ScoliaRegistrationDraftAlreadyRegistered($Draft,$Learners=$script:data.learners) {
    return (-not [string]::IsNullOrWhiteSpace((Get-ScoliaRegistrationDraftRegisteredMatricule $Draft $Learners)))
}
function Mark-ScoliaRegistrationDraftRegistered($State,$Draft,[string]$Matricule) {
    Assert-ScoliaRegistrationDraftCurrentContext $State
    $capturedId=[string]$Draft.Id;$capturedContext=$State.Context;$capturedMat=$Matricule
    $change={param($document)
        if(-not $document.Context -or
            -not [string]::Equals([string]$document.Context.Id,[string]$capturedContext.Id,[StringComparison]::Ordinal) -or
            -not [string]::Equals([string]$document.Context.Year,[string]$capturedContext.Year,[StringComparison]::Ordinal) -or
            -not [string]::Equals([string]$document.Context.LocalKey,[string]$capturedContext.LocalKey,[StringComparison]::Ordinal)){throw 'Le contexte du magasin local a changé.'}
        foreach($item in @($document.Items)){
            if([string]$item.Id -eq $capturedId -and $item.Context -and
                [string]::Equals([string]$item.Context.Id,[string]$capturedContext.Id,[StringComparison]::Ordinal) -and
                [string]::Equals([string]$item.Context.Year,[string]$capturedContext.Year,[StringComparison]::Ordinal) -and
                [string]::Equals([string]$item.Context.LocalKey,[string]$capturedContext.LocalKey,[StringComparison]::Ordinal)){
                $item | Add-Member -NotePropertyName State -NotePropertyValue 'Saved' -Force
                $item | Add-Member -NotePropertyName SavedMatricule -NotePropertyValue $capturedMat -Force
                $item | Add-Member -NotePropertyName SavedAt -NotePropertyValue ([DateTime]::UtcNow.ToString('o')) -Force
            }
        }
    }.GetNewClosure()
    [void](Invoke-ScoliaFeatureUpdate -Name 'inscriptions' -Local -Change $change)
}
function Get-ScoliaRegistrationDraftSnapshot($State) {
    $fields=[ordered]@{}
    foreach($name in $script:registrationDraftFields){
        if(-not $State.Controls.ContainsKey($name) -or $State.Controls[$name].IsDisposed){throw ('Champ de brouillon indisponible : '+$name)}
        $fields[$name]=[string]$State.Controls[$name].Text
    }
    $content=$false
    foreach($name in @('Nom','PostNom','Prenom','Naissance','Lieu','Contact1','Contact2','Adresse')){
        $value=[string]$fields[$name]
        if(($name -eq 'Naissance' -and $value -match '[0-9]') -or ($name -ne 'Naissance' -and -not [string]::IsNullOrWhiteSpace($value))){$content=$true;break}
    }
    return [pscustomobject]@{Fields=[pscustomobject]$fields;Fingerprint=($fields | ConvertTo-Json -Compress);HasContent=$content}
}
function Set-ScoliaRegistrationDraftStatus($State,[string]$Text,[bool]$Error=$false) {
    if(-not $State -or -not $State.Dialog -or $State.Dialog.IsDisposed){return}
    $State.Dialog.Text=$State.BaseTitle+$(if($Text){' · '+$Text}else{''})
    $State.Dialog.AccessibleDescription='Le brouillon est local. Il ne constitue pas une inscription. '+$Text
    if($Error -and $State.Feedback -and -not $State.Feedback.IsDisposed){
        $State.Feedback.Tag='scolia:feedback:error'
        $State.Feedback.Text=$Text+' Vos champs restent affichés.'
    }
}
function Show-ScoliaRegistrationDraftProblem($State,[string]$Message) {
    $State.LastError=$Message
    Set-ScoliaRegistrationDraftStatus $State ('Brouillon non sauvegardé : '+$Message) $true
}
function Confirm-ScoliaRegistrationDraftResume($Dialog,$Draft) {
    $date=''
    try{$date=([DateTime]::Parse([string]$Draft.UpdatedAt)).ToLocalTime().ToString('dd/MM/yyyy à HH:mm')}catch{}
    $name=([string]$Draft.Fields.Nom+' '+[string]$Draft.Fields.PostNom+' '+[string]$Draft.Fields.Prenom).Trim()
    $message='Un brouillon local est disponible.'
    if($name){$message+=[Environment]::NewLine+$name}
    if($date){$message+=[Environment]::NewLine+'Sauvegardé le '+$date}
    $message+=[Environment]::NewLine+[Environment]::NewLine+'Reprendre ce brouillon ? Il faudra encore enregistrer le dossier pour inscrire cet apprenant.'
    $message+=[Environment]::NewLine+'Si vous choisissez Non, le brouillon reste disponible.'
    return ([ITMMessageBox]::Show($Dialog,$message,'Reprendre une inscription','YesNo','Question') -eq 'Yes')
}
function Restore-ScoliaRegistrationDraft($State,$Draft) {
    Assert-ScoliaRegistrationDraftCurrentContext $State
    if(-not (Test-ScoliaRegistrationDraftContext $Draft.Context $State.Context)){throw 'Ce brouillon appartient à un autre contexte.'}
    if([string]$Draft.State -eq 'Saved'){throw 'Ce dossier est déjà enregistré. Ce brouillon ne peut plus être repris.'}
    $registeredMat=Get-ScoliaRegistrationDraftRegisteredMatricule $Draft
    if($registeredMat){throw ('Cet apprenant est déjà inscrit : '+$registeredMat+'. Ouvrez son dossier existant.')}
    if($script:registrationDraftCommitted.ContainsKey($State.Scope+'|'+[string]$Draft.Fingerprint)){throw 'Ce dossier a déjà été enregistré pendant cette session.'}
    if(-not $Draft.Fields -or [string]::IsNullOrWhiteSpace([string]$Draft.Id)){throw 'Le brouillon est incomplet.'}
    $values=@{}
    foreach($name in $script:registrationDraftFields){
        if($Draft.Fields -is [Collections.IDictionary]){
            if(-not $Draft.Fields.Contains($name)){throw ('Champ manquant dans le brouillon : '+$name)}
            $value=[string]$Draft.Fields[$name]
        }else{
            $property=$Draft.Fields.PSObject.Properties[$name]
            if(-not $property){throw ('Champ manquant dans le brouillon : '+$name)}
            $value=[string]$property.Value
        }
        if($value.Length -gt 10000){throw 'Le brouillon contient une valeur trop longue.'}
        $values[$name]=$value
    }
    $State.Restoring=$true;$missingChoice=$false
    try{
        # La promotion est restauree avant son annee : le lien promotion/annee reste actif.
        foreach($name in $script:registrationDraftFields){
            $control=$State.Controls[$name]
            if($control -is [Windows.Forms.ComboBox]){
                if($control.Items.Contains($values[$name])){$control.SelectedItem=$values[$name]}
                else{$control.SelectedIndex=-1;if($values[$name]){$missingChoice=$true}}
            }else{$control.Text=$values[$name]}
        }
        $script:editTuteur=[string]$Draft.Tuteur
        $State.DraftId=[string]$Draft.Id;$State.HasStored=$true
        $State.LastPersisted=(Get-ScoliaRegistrationDraftSnapshot $State).Fingerprint
        $State.Dirty=$false
        Set-ScoliaRegistrationDraftStatus $State $(if($missingChoice){'Brouillon repris · vérifiez la promotion et les choix disponibles'}else{'Brouillon repris · inscription à enregistrer'})
    }finally{$State.Restoring=$false}
}
function Remove-ScoliaRegistrationDraftItems($State,[string[]]$Ids) {
    if(-not $Ids -or $Ids.Count -eq 0){return}
    Assert-ScoliaRegistrationDraftCurrentContext $State
    $capturedContext=$State.Context;$capturedIds=@($Ids)
    $change={param($document)
        if(-not $document.Context -or
            -not [string]::Equals([string]$document.Context.Id,[string]$capturedContext.Id,[StringComparison]::Ordinal) -or
            -not [string]::Equals([string]$document.Context.Year,[string]$capturedContext.Year,[StringComparison]::Ordinal) -or
            -not [string]::Equals([string]$document.Context.LocalKey,[string]$capturedContext.LocalKey,[StringComparison]::Ordinal)){throw 'Le contexte du magasin local a changé.'}
        $document.Items=@($document.Items | Where-Object {
            -not ([string]$_.Id -in $capturedIds -and $_.Context -and
                [string]::Equals([string]$_.Context.Id,[string]$capturedContext.Id,[StringComparison]::Ordinal) -and
                [string]::Equals([string]$_.Context.Year,[string]$capturedContext.Year,[StringComparison]::Ordinal) -and
                [string]::Equals([string]$_.Context.LocalKey,[string]$capturedContext.LocalKey,[StringComparison]::Ordinal))
        })
    }.GetNewClosure()
    [void](Invoke-ScoliaFeatureUpdate -Name 'inscriptions' -Local -Change $change)
}
function Save-ScoliaRegistrationDraft($State=$script:registrationDraftState,[switch]$Force) {
    if(-not $State -or $State.Dialog.IsDisposed -or $State.Restoring -or $State.Suspended){return $true}
    if(-not [string]::IsNullOrWhiteSpace([string]$script:editMat)){return $true}
    if($script:busy){return $true}
    try{
        Assert-ScoliaRegistrationDraftCurrentContext $State
        if($State.PendingRemoval.Count){
            Remove-ScoliaRegistrationDraftItems $State @($State.PendingRemoval)
            $State.PendingRemoval=@()
        }
        $snapshot=Get-ScoliaRegistrationDraftSnapshot $State
        if($snapshot.Fingerprint -eq $State.CompletedFingerprint){return $true}
        if(-not $snapshot.HasContent){
            if($State.HasStored){Remove-ScoliaRegistrationDraftItems $State @($State.DraftId);$State.HasStored=$false}
            $State.LastPersisted=$snapshot.Fingerprint;$State.Dirty=$false
            Set-ScoliaRegistrationDraftStatus $State ([string]$State.Notice)
            return $true
        }
        if($snapshot.Fingerprint -eq $State.LastPersisted){$State.Dirty=$false;return $true}
        if(-not $Force -and (-not $State.Dirty -or ((Get-Date)-$State.ChangedAt).TotalMilliseconds -lt 900)){return $true}
        $item=[pscustomobject]@{
            Id=$State.DraftId;Kind='Inscription';State='Draft';Context=$State.Context
            UpdatedAt=[DateTime]::UtcNow.ToString('o');Fields=$snapshot.Fields
            Fingerprint=$snapshot.Fingerprint;Tuteur=[string]$script:editTuteur
        }
        $capturedItem=$item;$capturedContext=$State.Context
        $change={param($document)
            if(-not $document.Context -or
                -not [string]::Equals([string]$document.Context.Id,[string]$capturedContext.Id,[StringComparison]::Ordinal) -or
                -not [string]::Equals([string]$document.Context.Year,[string]$capturedContext.Year,[StringComparison]::Ordinal) -or
                -not [string]::Equals([string]$document.Context.LocalKey,[string]$capturedContext.LocalKey,[StringComparison]::Ordinal)){throw 'Le contexte du magasin local a changé.'}
            $document.Items=@($document.Items | Where-Object {
                -not ([string]$_.Id -eq [string]$capturedItem.Id -and $_.Context -and
                    [string]::Equals([string]$_.Context.Id,[string]$capturedContext.Id,[StringComparison]::Ordinal) -and
                    [string]::Equals([string]$_.Context.Year,[string]$capturedContext.Year,[StringComparison]::Ordinal) -and
                    [string]::Equals([string]$_.Context.LocalKey,[string]$capturedContext.LocalKey,[StringComparison]::Ordinal))
            })+@($capturedItem)
        }.GetNewClosure()
        [void](Invoke-ScoliaFeatureUpdate -Name 'inscriptions' -Local -Change $change)
        $State.HasStored=$true;$State.LastPersisted=$snapshot.Fingerprint;$State.LastError='';$State.Dirty=$false;$State.Notice=''
        if($State.Feedback -and -not $State.Feedback.IsDisposed -and $State.Feedback.Text.StartsWith('Brouillon non sauvegardé :')){$State.Feedback.Text=''}
        Set-ScoliaRegistrationDraftStatus $State 'Brouillon enregistré localement'
        return $true
    }catch{Show-ScoliaRegistrationDraftProblem $State ([string]$_.Exception.Message);return $false}
}
function Complete-ScoliaRegistrationDraft([string]$Matricule='') {
    $state=$script:registrationDraftState
    if(-not $state -or -not [string]::IsNullOrWhiteSpace([string]$script:editMat)){return $true}
    $state.Suspended=$true;$commitInContext=$false
    try{
        Assert-ScoliaRegistrationDraftCurrentContext $state
        $snapshot=Get-ScoliaRegistrationDraftSnapshot $state
        # Meme si le rafraichissement Excel echoue ensuite, ce snapshot ne sera plus autosauvegarde.
        $state.CompletedFingerprint=$snapshot.Fingerprint
        $script:registrationDraftCommitted[$state.Scope+'|'+$snapshot.Fingerprint]=$Matricule
        $commitInContext=$true
        $completedId=[string]$state.DraftId
        if($state.HasStored){
            # Le marqueur empeche la reprise si le retrait final rencontre une erreur de disque.
            try{Mark-ScoliaRegistrationDraftRegistered $state ([pscustomobject]@{Id=$completedId}) $Matricule}catch{}
            Remove-ScoliaRegistrationDraftItems $state @($completedId)
        }
        $state.HasStored=$false;$state.LastPersisted='';$state.Dirty=$false;$state.LastError=''
        Set-ScoliaRegistrationDraftStatus $state ''
        return $true
    }catch{
        if($commitInContext){
            if($state.DraftId -notin $state.PendingRemoval){$state.PendingRemoval+=([string]$state.DraftId)}
            $state.HasStored=$false;$state.Dirty=$false
        }
        $problem=$(if($commitInContext){'Le dossier '+$Matricule+' est enregistré, mais le retrait du brouillon sera réessayé. '}else{'Le brouillon reste dans son contexte initial. '})+[string]$_.Exception.Message
        Show-ScoliaRegistrationDraftProblem $state $problem
        return $false
    }finally{
        if($commitInContext){$state.DraftId=[Guid]::NewGuid().ToString('N')}
        $state.Suspended=$false
    }
}
function Enable-ScoliaRegistrationDraft($Dialog) {
    if(-not [string]::IsNullOrWhiteSpace([string]$script:editMat)){return}
    if($script:registrationDraftState -and $script:registrationDraftState.Dialog -eq $Dialog){return}
    $state=@{
        Dialog=$Dialog;Controls=$script:editControls;Feedback=$script:editFeedback;BaseTitle=[string]$Dialog.Text
        Context=$null;Scope='';DraftId=[Guid]::NewGuid().ToString('N');HasStored=$false
        LastPersisted='';CompletedFingerprint='';PendingRemoval=@();Dirty=$false;ChangedAt=Get-Date
        Restoring=$false;Suspended=$false;Timer=$null;LastError='';Notice=''
    }
    $script:registrationDraftState=$state
    $Dialog.Add_Disposed({param($sender,$e)
        $active=$script:registrationDraftState
        if($active -and $active.Dialog -eq $sender){if($active.Timer){$active.Timer.Stop();$active.Timer.Dispose()};$script:registrationDraftState=$null}
    })
    try{
        $context=Get-ScoliaFeatureContext
        $state.Context=[pscustomobject]@{Id=[string]$context.Id;Year=[string]$context.Year;LocalKey=[string]$context.LocalKey}
        $state.Scope=Get-ScoliaRegistrationDraftScope $state.Context
        $state.LastPersisted=(Get-ScoliaRegistrationDraftSnapshot $state).Fingerprint
        $document=Read-ScoliaFeatureStore -Name 'inscriptions' -Local
        # Une suppression interrompue est reprise meme apres fermeture du formulaire.
        $state.PendingRemoval=@($document.Items | Where-Object {
            $_ -and [string]$_.Kind -eq 'Inscription' -and
            (Test-ScoliaRegistrationDraftContext $_.Context $state.Context) -and
            ([string]$_.State -eq 'Saved' -or $script:registrationDraftCommitted.ContainsKey($state.Scope+'|'+[string]$_.Fingerprint))
        } | ForEach-Object {[string]$_.Id})
        $drafts=@($document.Items | Where-Object {
            $_ -and [string]$_.Kind -eq 'Inscription' -and [string]$_.State -ne 'Saved' -and
            (Test-ScoliaRegistrationDraftContext $_.Context $state.Context) -and
            -not $script:registrationDraftCommitted.ContainsKey($state.Scope+'|'+[string]$_.Fingerprint)
        } | Sort-Object UpdatedAt -Descending)
        $available=New-Object Collections.Generic.List[object]
        foreach($draft in $drafts){
            $registeredMat=Get-ScoliaRegistrationDraftRegisteredMatricule $draft
            if($registeredMat){
                $state.Notice='Déjà inscrit · '+$registeredMat
                Set-ScoliaRegistrationDraftStatus $state $state.Notice
                if($state.Feedback -and -not $state.Feedback.IsDisposed){
                    $state.Feedback.Tag='scolia:feedback:success'
                    $state.Feedback.Text='Ce brouillon correspond au dossier '+$registeredMat+'. Retrouvez-le dans Apprenants.'
                }
                # Si le disque reste indisponible, la verification des apprenants sera refaite au prochain lancement.
                try{Mark-ScoliaRegistrationDraftRegistered $state $draft $registeredMat}catch{Show-ScoliaRegistrationDraftProblem $state ('Le dossier '+$registeredMat+' existe déjà. Le brouillon ne sera pas repris. '+[string]$_.Exception.Message)}
            }else{$available.Add($draft)}
        }
        $drafts=@($available.ToArray())
        if($drafts.Count -and (Confirm-ScoliaRegistrationDraftResume $Dialog $drafts[0])){Restore-ScoliaRegistrationDraft $state $drafts[0]}
    }catch{Show-ScoliaRegistrationDraftProblem $state ([string]$_.Exception.Message)}
    if(-not $state.Context -or -not $state.Scope){return}
    foreach($control in $state.Controls.Values){$control.Add_TextChanged({param($sender,$e)
        $active=$script:registrationDraftState
        if($active -and -not $active.Restoring -and -not $active.Suspended -and $active.Controls.Values -contains $sender){$active.Dirty=$true;$active.ChangedAt=Get-Date}
    })}
    $state.Timer=New-Object Windows.Forms.Timer;$state.Timer.Interval=1200
    $state.Timer.Add_Tick({param($sender,$e)
        $active=$script:registrationDraftState
        if($active -and $active.Timer -eq $sender){[void](Save-ScoliaRegistrationDraft $active)}
    })
    $Dialog.Add_FormClosing({param($sender,$e)
        $active=$script:registrationDraftState
        if(-not $e.Cancel -and $active -and $active.Dialog -eq $sender -and -not (Save-ScoliaRegistrationDraft $active -Force)){$e.Cancel=$true}
    })
    $state.Timer.Start()
}
