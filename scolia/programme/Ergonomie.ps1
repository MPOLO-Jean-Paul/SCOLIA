# Reusable input helpers; business validation and saving remain in the existing APIs.
$script:inputHelp=New-Object Windows.Forms.ToolTip
$script:inputHelp.AutoPopDelay=16000;$script:inputHelp.InitialDelay=450;$script:inputHelp.ReshowDelay=150
function New-ITMDateField {
    $box=New-Object Windows.Forms.MaskedTextBox
    $box.BorderStyle='FixedSingle'
    $box.Mask='00\/00\/0000';$box.PromptChar='_';$box.HidePromptOnLeave=$false
    $box.SkipLiterals=$true;$box.ResetOnSpace=$true;$box.ResetOnPrompt=$true
    $box.TextMaskFormat=[Windows.Forms.MaskFormat]::IncludeLiterals
    $box.CutCopyMaskFormat=[Windows.Forms.MaskFormat]::IncludeLiterals
    $box.AccessibleName='Date de naissance';$box.AccessibleDescription='Jour, mois, année : huit chiffres. Les barres obliques sont ajoutées automatiquement.'
    $box.Add_Enter({param($sender,$e)
        if($sender.MaskCompleted){$sender.SelectAll()}else{
            $position=$sender.MaskedTextProvider.FindUnassignedEditPositionFrom(0,$true)
            if($position -ge 0){$sender.SelectionStart=$position;$sender.SelectionLength=0}
        }
    })
    return $box
}
function Get-ITMBirthDateProblem($control) {
    if($control -is [Windows.Forms.MaskedTextBox] -and -not $control.MaskCompleted){return 'Saisissez les huit chiffres : jour, mois, année. Les / sont automatiques.'}
    $date=[DateTime]::MinValue
    if(-not [DateTime]::TryParseExact([string]$control.Text,'dd/MM/yyyy',[Globalization.CultureInfo]::InvariantCulture,[Globalization.DateTimeStyles]::None,[ref]$date)){return 'Cette date n''existe pas. Exemple : 01/09/2006.'}
    if($date.Year -lt 1900 -or $date.Date -gt (Get-Date).Date){return 'La naissance doit être comprise entre 1900 et aujourd''hui.'}
    return ''
}
function Get-ITMDossierFingerprint {
    return (@($script:editControls.Keys | Sort-Object | ForEach-Object {[string]$_+'='+[string]$script:editControls[$_].Text}) | ConvertTo-Json -Compress)
}
function Enable-ITMRegistrationForm($dialog) {
    $state=$script:adaptivePanels[$dialog]
    $dialog.AcceptButton=$state.Save
    $dialog.KeyPreview=$true
    $dialog.Add_KeyDown({param($sender,$e)
        if(-not $e.Handled -and $e.Control -and $e.KeyCode -eq 'S' -and -not $script:busy){$e.Handled=$true;$e.SuppressKeyPress=$true;$script:editSave.PerformClick()}
    })
    foreach($field in $script:editControls.Keys){
        $c=$script:editControls[$field]
        $c.AccessibleName=switch($field){'PostNom'{'Post-nom'};'Naissance'{'Date de naissance'};'Contact1'{'Contact parent 1'};'Contact2'{'Contact parent 2 facultatif'};'Annee'{'Année scolaire'};default{$field}}
        $script:inputHelp.SetToolTip($c, $(switch($field){
            'Naissance'{'Tapez 01092006 : la date devient 01/09/2006. Vous pouvez aussi coller une date avec ses /.'}
            'Contact1'{'Dix chiffres, sans espaces. Le zéro initial est conservé.'}
            'Contact2'{'Deuxième contact : dix chiffres ou laissez vide.'}
            'Annee'{'L''année scolaire suit la promotion sélectionnée.'}
            'Statut'{'Actif : inscrit ; Suspendu : scolarité interrompue ; Sorti : a quitté l''établissement.'}
            'Adresse'{'Facultatif : quartier, avenue et numéro si disponibles.'}
            default{'Tabulation : champ suivant. Ctrl+S : enregistrer le dossier.'}
        }))
        $c.AccessibleDescription=$(if($field -in @('Contact2','Adresse')){'Champ facultatif. '}else{'Champ obligatoire. '})+$script:inputHelp.GetToolTip($c)
        $c.Add_TextChanged({param($sender,$e)
            Set-ITMDossierFieldError $sender ''
            if(-not $script:busy -and $script:editFeedback -and -not $script:editFeedback.IsDisposed){$script:editFeedback.Text=''}
        })
    }
    $script:editControls.Naissance.Add_Leave({param($sender,$e)if($script:editErrors){Set-ITMDossierFieldError $sender (Get-ITMBirthDateProblem $sender)}})
    $script:editControls.Adresse.AcceptsReturn=$true
    $script:editDone=$false;$script:editOriginal=Get-ITMDossierFingerprint
    $dialog.Add_FormClosing({param($sender,$e)
        if($script:busy){$e.Cancel=$true;return}
        if(-not $script:editDone -and (Get-ITMDossierFingerprint) -ne $script:editOriginal){
            if([ITMMessageBox]::Show($sender,'Ce dossier contient des modifications non enregistrées. Fermer sans les enregistrer ?','Dossier non enregistré','YesNo','Question') -ne 'Yes'){$e.Cancel=$true}
        }
    })
    Update-ITMDossierLayout $dialog
}
function Show-ITMUsageGuide {
    try {
        $path=Join-Path ([IO.Path]::GetDirectoryName($PSScriptRoot)) 'documentation\GUIDE_UTILISATION_COMPLET.txt'
        if(-not (Test-Path -LiteralPath $path -PathType Leaf)){throw 'Le guide est absent. Copiez également le dossier documentation de cette édition.'}
        $text=Get-Content -LiteralPath $path -Raw -Encoding UTF8 -ErrorAction Stop
        Show-ITMHelpWindow $text
    }catch{App-Error $_}
}
function Test-ITMDateInput {
    $box=New-ITMDateField
    try {
        foreach($dateText in @('01012006','01/01/2006')){
            $box.Clear();$box.Text=$dateText
            if(-not $box.MaskCompleted -or $box.Text -ne '01/01/2006' -or (Get-ITMBirthDateProblem $box)){throw 'Le contrôle de date ne formate pas correctement les huit chiffres.'}
        }
        $box.Clear();$box.Text='31022006'
        if(-not (Get-ITMBirthDateProblem $box)){throw 'Une date impossible a été acceptée.'}
        $box.Clear();$box.Text='0101'
        if(-not (Get-ITMBirthDateProblem $box)){throw 'Une date incomplète a été acceptée.'}
        return $true
    }finally{$box.Dispose()}
}
