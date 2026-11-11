# Contrôles de session en lecture seule : aucune opération de caisse ni écriture Excel.
function Test-ITMFinanceSearch($entry,[string]$query) {
    $text=Key ($entry.Reference+' '+$entry.Nom+' '+$entry.PostNom+' '+$entry.Prenom+' '+$entry.Promotion+' '+$entry.Description+' '+$entry.Kind+' '+$entry.OperatorName)
    foreach($word in @((Key $query) -split '\s+' | Where-Object {$_})){
        if(-not $text.Contains($word)){return $false}
    }
    return $true
}
function Get-ITMHealthChecks([hashtable]$state) {
    $rows=New-Object 'System.Collections.Generic.List[object]'
    $rows.Add([pscustomobject]@{Controle='Session';Etat=$(if($state.Session){'OK'}else{'À vérifier'});Conseil=$(if($state.Session){'Compte connecté.'}else{'Reconnectez votre compte avant de travailler.'})})
    if($state.Role -ne 'Comptabilité'){
        $rows.Add([pscustomobject]@{Controle='Données scolaires';Etat=$(if($state.DataReady){'OK'}else{'À vérifier'});Conseil=$(if($state.DataReady){'Données chargées et actualisées.'}else{'Dans Données, connectez le classeur puis actualisez les données.'})})
        if($state.Closed){$rows.Add([pscustomobject]@{Controle='Année scolaire';Etat='Information';Conseil='Année clôturée : les écritures sont bloquées. Ouvrez la nouvelle année pour saisir.'})}
    }
    $rows.Add([pscustomobject]@{Controle='Opération de caisse';Etat=$(if($state.Pending){'À vérifier'}else{'OK'});Conseil=$(if($state.Pending){'Utilisez Comptabilité > Vérifier opération en attente avant toute nouvelle saisie.'}else{'Aucune opération locale en attente de vérification.'})})
    $rows.Add([pscustomobject]@{Controle='Synchronisation';Etat=$(if($state.SyncError){'À vérifier'}elseif($state.SyncReady){'OK'}else{'Information'});Conseil=$(if($state.SyncError){'Vérifiez le réseau et le dossier partagé. Ne ressaisissez pas un paiement déjà envoyé.'}elseif($state.SyncReady){'Dernière lecture du partage réussie.'}else{'La première lecture du partage est en cours ou reste à effectuer.'})})
    if($state.Store){
        $rows.Add([pscustomobject]@{
            Controle='Sauvegarde quotidienne'
            Etat=$(if($state.BackupError){'À vérifier'}elseif(-not $state.BackupEnabled){'Information'}elseif($state.BackupToday){'OK'}else{'À vérifier'})
            Conseil=$(if($state.BackupError){
                'Consultez Diagnostic > Compatibilité et sauvegardes, puis créez une sauvegarde manuelle.'
            }elseif(-not $state.BackupEnabled){
                'Sauvegarde automatique désactivée. Pensez à sauvegarder manuellement.'
            }elseif($state.BackupToday){
                "Une sauvegarde automatique vérifiée existe pour aujourd'hui."
            }else{
                "Aucune sauvegarde automatique vérifiée aujourd'hui. Créez une sauvegarde ou laissez SCOLIA ouvert."
            })
        })
    }elseif($state.Role -ne 'Comptabilité'){
        $rows.Add([pscustomobject]@{Controle='Sauvegarde';Etat='Information';Conseil='Aucun dossier data connecté. Vérifiez vos copies manuelles avant de terminer la journée.'})
    }
    if($state.Issues -gt 0){
        $rows.Add([pscustomobject]@{
            Controle='Qualité des données'
            Etat='À vérifier'
            Conseil=([string]$state.Issues+" observation(s) à examiner dans Diagnostic. Aucun dossier n'est fusionné automatiquement.")
        })
    }
    return $rows.ToArray()
}
function Get-ITMHealthReport {
    Assert-ITMPermission 'Diagnostic'
    $state=@{Session=[bool]$script:scoliaSession;Role=[string]$script:scoliaSession.Role;DataReady=[bool]($script:book -and $script:data -and $script:snapshotValid -and -not $script:payNeedsRefresh);Closed=[bool]$script:data.closed;Pending=$false;SyncError=[bool]$script:scoliaSync.LastError;SyncReady=[bool]$script:scoliaSync.LastSuccess;Store=[bool]$script:store;BackupError=[bool]$script:autoBackupFailure;BackupEnabled=$true;BackupToday=$false;Issues=@($script:data.issues | Where-Object {$_}).Count}
    if($script:scoliaLocalRoot){$state.Pending=[IO.File]::Exists((Join-Path $script:scoliaLocalRoot 'operation-caisse-en-attente.json'))}
    if($state.Store){
        try{
            $state.BackupEnabled=(Get-ITMAutoBackupSettings).Enabled
            $today=Get-Date -Format 'yyyy-MM-dd'
            $state.BackupToday=@(Get-ITMAutomaticBackups | Where-Object {$_.Day -eq $today}).Count -gt 0
        }catch{$state.BackupError=$true}
    }
    $checks=@(Get-ITMHealthChecks $state)
    $lines=New-Object 'System.Collections.Generic.List[string]'
    $lines.Add('SCOLIA — État de mon espace');$lines.Add('Contrôle du '+(Get-Date -Format 'dd/MM/yyyy HH:mm:ss'))
    $lines.Add('Points à vérifier : '+@($checks | Where-Object {$_.Etat -eq 'À vérifier'}).Count)
    foreach($row in $checks){$lines.Add('');$lines.Add('['+$row.Etat+'] '+$row.Controle);$lines.Add($row.Conseil)}
    $lines.Add('');$lines.Add('Ce contrôle décrit la session et les marqueurs de sauvegarde. Il ne remplace pas une vérification des fichiers ou un essai de restauration.')
    return ($lines -join [Environment]::NewLine)
}
function Build-ITMHealthCommands($page) {
    if($script:modernPages -and $script:modernPages.ContainsKey($page)){
        $design=$script:modernPages[$page]
        $button=New-Button $design.Surface 'État de mon espace' 0 0 230
        $button.Add_Click({try{Show-TextDetail 'État de mon espace' (Get-ITMHealthReport)}catch{App-Error $_}})
        $design.Rows.Insert(1,@($button))
        Update-ITMPageDesign $page
    }else{
        $bar=New-Object Windows.Forms.FlowLayoutPanel;$bar.Dock='Bottom';$bar.AutoSize=$true;$bar.Padding=New-Object Windows.Forms.Padding(12);$page.Controls.Add($bar)
        [void](Add-ScoliaButton $bar 'État de mon espace' {try{Show-TextDetail 'État de mon espace' (Get-ITMHealthReport)}catch{App-Error $_}})
    }
}
