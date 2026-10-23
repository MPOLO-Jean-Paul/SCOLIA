. (Join-Path $PSScriptRoot 'RubanAdaptatifV64.ps1')
. (Join-Path $PSScriptRoot 'EspaceFonctionnelV67.ps1')
. (Join-Path $PSScriptRoot 'ReactiviteV68.ps1')
if(-not (Get-Command Import-ITMCompiledSource -ErrorAction SilentlyContinue)){. (Join-Path $PSScriptRoot 'CompilationCache.ps1')}
if(-not ('ITMLoginButton' -as [type])){Import-ITMCompiledSource -Path (Join-Path $PSScriptRoot 'ChromeModerne.cs') -ReferencedAssemblies System.Windows.Forms,System.Drawing}
$script:navigationLayoutBusy=$false
$script:ribbonPeek=$false
. (Join-Path $PSScriptRoot 'RaccourcisFrequents.ps1')
function Test-ITMRibbonExpanded {
    if(-not $script:displayPreferences -or -not $script:displayPreferences.PSObject.Properties['RibbonMode']){return $true}
    return ($script:displayPreferences.RibbonMode -ne 'Onglets seuls' -or $script:ribbonPeek)
}
function Toggle-ITMRibbonVisibility {
    if($script:busy){return}
    $previous=$script:displayPreferences.RibbonMode
    $script:displayPreferences.RibbonMode=$(if(Test-ITMRibbonExpanded){'Onglets seuls'}else{'Onglets et commandes'})
    try{Save-ITMDisplayPreferences}catch{$script:displayPreferences.RibbonMode=$previous;throw}
    $script:ribbonPeek=$false
    if($script:displayRibbon){$script:displayRibbon.SelectedItem=$script:displayPreferences.RibbonMode}
    Update-ITMNavigation
}
function Add-ITMRibbonGroup([string]$title,$commands) {
    if($script:frequentBuilding){return}

    $panel=New-Object ITMRibbonGroup
    $q=[Math]::Max(1,[Math]::Min(2.5,$script:main.CurrentAutoScaleDimensions.Height/96.0))
    $cols=[Math]::Max(1,[Math]::Ceiling($commands.Count/3.0));$cell=[int](180*$q);$gap=[int](3*$q)
    $panel.Size=New-Object Drawing.Size(($cols*($cell+$gap)+$gap),([int](120*$q)))
    $panel.Margin=New-Object Windows.Forms.Padding(0,0,([int](6*$q)),0)
    for($i=0;$i -lt $commands.Count;$i++){
        $command=$commands[$i]
        $button=New-Object ITMRibbonCommand
        $button.Text=[string]$command.Label;$button.Symbol=$command.Symbol
        $button.SetBounds((([int][Math]::Floor($i/3)*($cell+$gap))+$gap),((($i%3)*([int](30*$q)+$gap))+$gap),$cell,([int](30*$q)))
        $panel.Controls.Add($button);Set-ITMAdaptiveFont $button 9 ([Drawing.FontStyle]::Regular)
        $help=$command.Description+$(if([string]::IsNullOrWhiteSpace([string]$command.Shortcut)){''}else{"`r`nRaccourci : "+$command.Shortcut})
        Set-ITMCommandHelp $button $command
        if($command.Target){Set-ITMCommandHelp $command.Target $command}
        $button.Tag=$command;$button.AccessibleDescription=$help
        $button.Enabled=(Test-ITMPermission $command.Permission) -and (-not $script:busy) -and ((-not $command.Book) -or [bool]($script:book -and $script:data)) -and ((-not $command.Write) -or (-not $script:data.closed -and -not $script:payNeedsRefresh))
        if($command.Target){$button.Enabled=$button.Enabled -and $command.Target.Enabled}
        $button.Add_Click({param($sender,$e)
            if($script:busy){return}
            $previousSource=$script:activitySource;$script:activitySource=$sender
            try{$command=$sender.Tag;Assert-ITMPermission $command.Permission;if($command.Target){Invoke-ITMRibbonControl $command.Target}else{[void](& $command.Action);Add-ITMCommandUse $command.UsageId}}catch{App-Error $_}finally{$script:activitySource=$previousSource}
        })
    }
    $captionTop=[Math]::Max([int](108*$q),[int](($panel.Controls | Measure-Object Bottom -Maximum).Maximum))+$gap
    $label=New-Label $panel $title $gap $captionTop ($panel.Width-2*$gap) ([int](24*$q)) 9 $false
    $label.TextAlign='MiddleCenter';$label.BackColor=[Drawing.Color]::Transparent;$label.ForeColor=[Drawing.Color]::FromArgb(100,116,139)
    $label.AutoEllipsis=$false
    $captionSize=[Windows.Forms.TextRenderer]::MeasureText($title,$label.Font,(New-Object Drawing.Size($label.Width,10000)),[Windows.Forms.TextFormatFlags]::WordBreak)
    $label.Height=[Math]::Max([int](24*$q),$captionSize.Height+[int](8*$q))
    $panel.Height=$label.Bottom+$gap
    $script:ribbonFlow.Controls.Add($panel)
    if(Get-Command Apply-ITMThemeForm -ErrorAction SilentlyContinue){Apply-ITMThemeForm $panel}
}
function Get-ITMCommandPermission([string]$label) {
    if($label -in @('Encaisser','Nouvelle dépense','Vérifier opération en attente','Brouillons de caisse')){return 'Encaisser'}
    if($label -eq 'Importer les apprenants'){return 'Encaisser'}
    if($label -in @('Importer les paiements','Visualiser lʼimportation','Brouillons reçus') -and $script:scoliaSession -and $script:scoliaSession.Role -ne 'Comptabilité'){return 'Recevoir'}
    if($label -eq 'Transférés en brouillon'){return 'Recevoir'}
    if($label -in @('Paramètres SCOLIA','Configuration SCOLIA','Configurer le réseau')){return 'Administrer'}
    if($label -eq 'Nettoyer les sauvegardes'){return 'Administrer'}
    if($label -in @('Réinscription','Salaires')){return 'Administrer'}
    if($script:scolia -and $label -in @('Cotes et délibération','Présences','Enseignants','À traiter','Gestion des cours','Attribution des cours','Charges des enseignants','Discipline')){return 'Scolarite'}
    if($label -eq 'Exporter le diagnostic'){return 'Diagnostic'}
    if($label -in @('Aperçu des cartes','Imprimer les cartes')){return 'Exporter'}
    if($label -match 'CSV|^Exporter|Dernier rapport|Liste visible|Historique CSV'){return 'Exporter'}
    if($label -in @('Sauvegarder tout')){return 'Sauvegarder'}
    if($label -in @('Inscrire','Modifier','Enregistrer','Paiement','Supprimer l''apprenant')){return 'Saisie'}
    if($label -in @('Corriger le reçu','Restaurer copie','Classeur suivant','Promotions','Clôture annuelle','Préparer','Nouveau classeur','Mot de passe','Désactiver','Sauvegardes automatiques','Journal nominatif','Gérer les rôles','Cotes et délibération')){return 'Administrer'}
    if($label -eq 'Compatibilité et sauvegardes' -or $label -eq 'Exporter le diagnostic'){return 'Diagnostic'}
    return 'Lire'
}
function New-ITMCommand([string]$label,$target=$null,[scriptblock]$action=$null,[bool]$book=$true,[bool]$write=$false,[bool]$primary=$false) {
    $symbol=switch -Regex ($label){
        '^Discipline' {'shield';break}
        '^Gestion des cours' {'grades';break}
        '^Attribution des cours' {'people';break}
        '^Charges des enseignants' {'history';break}
        '^Importer|^Brouillons reçus' {'open';break}
        '^Visualiser l' {'search';break}
        '^Nettoyer les sauvegardes' {'delete';break}
        '^Diagnostic|^Anomalies|^Vérifier la liste' {'shield';break}
        '^Fusionner|^Fusion des doublons|^Fusion de dossiers' {'merge';break}
        '^Doublons|^Rechercher les doublons' {'search';break}
        '^Supprimer l.apprenant|^Effacer un apprenant|^Suppression' {'delete';break}
        '^Inscrire|^Nouvel apprenant|^Ajouter un apprenant' {'person-add';break}
        '^Liste des apprenants|^Apprenants|^Gestion des apprenants' {'people';break}
        '^Présences|^Absences|^Assiduité' {'calendar';break}
        '^À traiter|^Actions à traiter' {'history';break}
        '^Réinscription' {'person-add';break}
        '^Enseignants' {'people';break}
        '^Salaires' {'payment';break}
        '^Notes|^Cotes|^Délibération|^Bulletins' {'grades';break}
        '^Configuration|^Paramètres|^Préférences' {'settings';break}
        '^Connexion|^Se connecter|^Session' {'lock';break}
        '^Erreur|^Journal des erreurs|^Diagnostic technique' {'shield';break}
        '^Sauvegarde|^Sauvegarder' {'save';break}
        '^Restaurer|^Restauration' {'restore';break}
        '^Factures|^Frais|^Recettes|^Dépenses' {'payment';break}
        '^Paiements|^Encaisser|^Reçu' {'payment';break}
        '^Recherche|^Rechercher' {'search';break}
        '^Imprimer|^Impression' {'print';break}
        '^Exporter|^Export' {'export';break}
        '^Brouillons de caisse|^Encaisser' {'payment';break}
        '^Nouvelle dépense' {'delete';break}
        '^Journal CSV' {'export';break}
        '^Reçu /' {'document';break}
        '^Synchronisation|^Configurer le réseau|^Transférés|^Dossier SCOLIA' {'open';break}
        '^Paramètres SCOLIA|^Configuration SCOLIA' {'settings';break}
        '^À propos' {'help';break}
        '^Cotes et délibération' {'document';break}
        '^Grilles remplies' {'open';break}
        '^Valeurs par défaut' {'restore';break}
        '^Compatibilité|^Exporter le diagnostic' {'shield';break}
        '^Toutes promotions|^Promotions' {'people';break}
        '^Journal|^Historique' {'history';break}
        '^Mémoriser' {'save';break}
        '^Dossier sources' {'open';break}
        '^Raccourci Bureau' {'shortcut';break}
        'Excel' {'workbook';break}
        'Word' {'word';break}
        'PDF' {'pdf';break}
        'CSV' {'csv';break}
        'Prise.*charge' {'sponsor';break}
        '^Paiement|^Tous les paiements|Mensualit' {'payment';break}
        '^Compte|Rôles|Utilisateur|^Gérer les rôles' {'account';break}
        '^Thème|Apparence|Affichage' {'theme';break}
        '^Nouveau classeur|^Classeur mémorisé|^Utiliser.*classeur|^Feuilles' {'workbook';break}
        '^Cartes|^Fiche complète|^Dernier dossier' {'card';break}
        '^Imprimer' {'print';break}
        '^Aperçu' {'search';break}
        '^Session actuelle' {'calendar';break}
        '^Historique des cartes' {'restore';break}
        '^Dossier des cartes' {'open';break}
        '^En-tête des cartes' {'edit';break}
        '^Cocher les cartes' {'check';break}
        '^Décocher les cartes' {'close';break}
        '^Ouvrir|Dernier rapport' {'open';break}
        '^Actualiser' {'refresh';break}
        '^Supprimer' {'delete';break}
        '^Fermer|Effacer|Désactiver' {'close';break}
        '^Aide' {'help';break}
        '^Inscrire' {'add';break}
        '^Modifier|Corriger' {'edit';break}
        '^Rechercher|Doublons' {'search';break}
        '^Enregistrer|Sauvegarder' {'save';break}
        'CSV|Exporter' {'export';break}
        '^Restaurer' {'restore';break}
        '^Mot de passe|Verrouiller|Clôture' {'lock';break}
        '^Vérifier|Préparer' {'check';break}
        '^Classeur suivant|Sélectionner' {'next';break}
        '^Gérer|Promotions|Raccourci' {'settings';break}
        default {'document'}
    }
    $info=Get-ITMCommandInfo $label
    # Une nouvelle commande ne doit jamais empêcher SCOLIA de démarrer uniquement
    # parce que son texte d'aide n'a pas encore été catalogué. Les commandes
    # officielles restent définies dans CommandesClavier.ps1 ; ce repli protège le lancement.
    if(-not $info){
        $info=[pscustomobject]@{Description=('Commande SCOLIA : '+$label+'.');Shortcut='';Keys=0}
        try{Write-Verbose ('Aide de commande absente : '+$label)}catch{}
    }
    if(Get-Command Register-ScoliaRibbonSource -ErrorAction SilentlyContinue){Register-ScoliaRibbonSource $target}
    if((Get-Command Get-ScoliaWorkspaceViewNames -ErrorAction SilentlyContinue) -and $label -in (Get-ScoliaWorkspaceViewNames)){$target=$null;$action=New-ScoliaWorkspaceViewAction $label}
    $command=[pscustomobject]@{Permission=(Get-ITMCommandPermission $label);Label=$label;Symbol=$symbol;Target=$target;Action=$action;Book=$book;Write=$write;Primary=$primary;Description=$info.Description;Shortcut=$info.Shortcut;Keys=$info.Keys}
    Register-ITMFrequentCommand $command
    return $command
}
function Initialize-ITMRibbon {
    $script:ribbonTips=New-Object Windows.Forms.ToolTip
    $script:main.Add_FormClosed({$script:ribbonTips.Dispose()})
    $script:ribbon=New-Object ITMRibbonPanel;$script:top.Controls.Add($script:ribbon)
    $script:actions.Visible=$false
    $script:ribbonFlow=New-Object Windows.Forms.FlowLayoutPanel;$script:ribbonFlow.Dock='Fill';$script:ribbonFlow.WrapContents=$false;$script:ribbonFlow.AutoScroll=$true;$script:ribbonFlow.Padding=New-Object Windows.Forms.Padding(8,3,8,2);$script:ribbonFlow.BackColor=[Drawing.Color]::Transparent;$script:ribbon.Controls.Add($script:ribbonFlow)
    $script:ribbonPage=''
    Update-ITMRibbonState
}
# V75: only detached groups live in this cache. Availability is always rechecked.
$script:scoliaRibbonViews=@{}
function Clear-ScoliaRibbonViews {
    foreach($groups in @($script:scoliaRibbonViews.Values)){
        foreach($group in @($groups)){
            if(-not $group.IsDisposed){
                if(Get-Command Remove-ITMThemeRecords -ErrorAction SilentlyContinue){Remove-ITMThemeRecords $group}
                foreach($child in $group.Controls){$script:buttonCommands.Remove($child)}
                $group.Dispose()
            }
        }
    }
    $script:scoliaRibbonViews=@{}
}
function Get-ScoliaRibbonContext {
    return (@($script:connectedWorkbookPath,[bool]$script:book,[bool]$script:scolia,
        $script:scolia.Id,$script:scolia.Revision,$script:scolia.Year,
        $script:scoliaSession.Id,$script:scoliaSession.Role,$script:accountSession.Id,$script:accountSession.Role,
        $script:displayPreferences.Theme,$script:displayPreferences.TextSize,$script:displayPreferences.Density,
        $script:displayPreferences.Tips,$script:main.CurrentAutoScaleDimensions.Height) -join '|')
}
function Update-ITMRibbonState {
    if(-not $script:ribbonFlow){return}
    if(-not $script:frequentBuilding){
        $context=Get-ScoliaRibbonContext
        # An explicit empty ribbonPage remains a force-rebuild signal (exports/settings).
        if($script:scoliaRibbonContext -cne $context -or -not $script:ribbonPage){
            Clear-ScoliaRibbonViews
            $script:scoliaRibbonContext=$context
            $script:ribbonPage=''
        }
        if($script:ribbonPage -and $script:ribbonPage -ne $script:currentPage){
            $script:ribbonFlow.SuspendLayout()
            try{
                $script:scoliaRibbonViews[$script:ribbonPage]=@($script:ribbonFlow.Controls)
                $script:ribbonFlow.Controls.Clear()
                if($script:scoliaRibbonViews.ContainsKey($script:currentPage)){
                    $groups=$script:scoliaRibbonViews[$script:currentPage]
                    $script:scoliaRibbonViews.Remove($script:currentPage)
                    foreach($group in $groups){[void]$script:ribbonFlow.Controls.Add($group)}
                    $script:ribbonPage=$script:currentPage
                    $script:ribbonGeometryRevision++
                }
            }finally{$script:ribbonFlow.ResumeLayout($true)}
        }
    }
    # Rebuild only when the tab changes; busy/selection changes merely refresh availability.
    if($script:ribbonPage -eq $script:currentPage){
        foreach($group in $script:ribbonFlow.Controls){foreach($button in $group.Controls){if($button.Tag -and $button.Tag.PSObject.Properties['Book']){
            $c=$button.Tag;$button.Enabled=(Test-ITMPermission $c.Permission) -and (-not $script:busy) -and ((-not $c.Book) -or [bool]($script:book -and $script:data)) -and ((-not $c.Write) -or (-not $script:data.closed -and -not $script:payNeedsRefresh))
            if($c.Target){$button.Enabled=$button.Enabled -and $c.Target.Enabled}
        }}}
        Update-ITMFrequentCommands;Update-ITMNavigation
        if(Get-Command Update-ScoliaActiveWorkspaceCommand -ErrorAction SilentlyContinue){Update-ScoliaActiveWorkspaceCommand}
        return
    }
    $script:ribbonFlow.SuspendLayout()
    try {
    if(-not $script:frequentBuilding){
    $script:ribbonGeometryRevision++
    $script:ribbonPage=$script:currentPage
    if($script:displayPreferences){$script:ribbonTips.Active=[bool]$script:displayPreferences.Tips}
    while($script:ribbonFlow.Controls.Count){$c=$script:ribbonFlow.Controls[0];$script:ribbonFlow.Controls.Remove($c);if(Get-Command Remove-ITMThemeRecords -ErrorAction SilentlyContinue){Remove-ITMThemeRecords $c};foreach($child in $c.Controls){$script:buttonCommands.Remove($child)};$c.Dispose()}
    }
    if($script:scoliaSession.Role -eq 'Comptabilité'){Add-ITMRibbonGroup 'OUTILS' @((New-ITMCommand 'Actualiser' $null {Request-ScoliaRefresh} $false),(New-ITMCommand 'Aide / guide' $null {Show-ITMUsageGuide} $false))}else{Add-ITMRibbonGroup 'OUTILS' @((New-ITMCommand 'Actualiser' $script:refresh),(New-ITMCommand 'Aide / guide' $null {Show-ITMUsageGuide} $false))}
    switch($script:currentPage){
        'Tableau de bord' {
            if($script:scoliaSession.Role -eq 'Comptabilité'){Add-ITMRibbonGroup 'CAISSE' @((New-ITMCommand 'Encaisser' $null {Show-ScoliaEntry} $false),(New-ITMCommand 'Nouvelle dépense' $null {Show-ScoliaEntry $true} $false));Add-ITMRibbonGroup 'ANALYSE COMPTABLE' @((New-ITMCommand 'Journal CSV' $null {Save-ITMCsv @(Get-ScoliaFinanceRows) 'SCOLIA_Journal.csv'} $false),(New-ITMCommand 'Reçu / imprimer' $null {Show-ScoliaReceipt} $false));break}
            Add-ITMRibbonGroup 'SAISIE' @((New-ITMCommand 'Inscrire' $script:new $null $true $true $true),(New-ITMCommand 'Paiements' $null {Show-Page 'Paiements'}))
            Add-ITMRibbonGroup 'ANALYSE' @((New-ITMCommand 'Rapports' $null {Show-Page 'Exports'}),(New-ITMCommand 'Retards en CSV' $null {Save-ITMCsv $script:lateRows 'Retards_ITM.csv'}))
        }
        'Comptabilité' {
            Add-ITMRibbonGroup 'IMPORTS RÉSEAU' @((New-ITMCommand 'Importer les paiements' $null {Show-ScoliaImports} $false),(New-ITMCommand 'Brouillons reçus' $null {Show-ScoliaImports 'Paiements' $false $true} $false),(New-ITMCommand 'Visualiser lʼimportation' $null {Show-ScoliaImports 'Paiements' $true} $false),(New-ITMCommand 'Importer les apprenants' $null {Show-ScoliaImports 'Apprenants'} $false))
            Add-ITMRibbonGroup 'BROUILLONS' @((New-ITMCommand 'Brouillons de caisse' $null {Show-ScoliaDrafts} $false),(New-ITMCommand 'Synchronisation' $null {Show-ScoliaSync} $false))
            Add-ITMRibbonGroup 'CAISSE' @((New-ITMCommand 'Encaisser' $null {Show-ScoliaEntry} $false),(New-ITMCommand 'Nouvelle dépense' $null {Show-ScoliaEntry $true} $false),(New-ITMCommand 'Vérifier opération en attente' $null {Show-ScoliaPending} $false),(New-ITMCommand 'Reçu / imprimer' $null {Show-ScoliaReceipt} $false))
            Add-ITMRibbonGroup 'JOURNAL ET PARAMÈTRES' @((New-ITMCommand 'Journal CSV' $null {Save-ITMCsv @(Get-ScoliaFinanceRows) 'SCOLIA_Journal.csv'} $false),(New-ITMCommand 'Paramètres SCOLIA' $null {Show-ScoliaSettings} $false),(New-ITMCommand 'À propos de SCOLIA' $null {Show-ScoliaAbout} $false))
        }
        'Apprenants' {
            Add-ITMRibbonGroup 'DOSSIERS' @((New-ITMCommand 'Inscrire' $script:new $null $true $true $true),(New-ITMCommand 'Modifier' $script:edit $null $true $true),(New-ITMCommand 'Fiche complète' $script:fiche),(New-ITMCommand 'Dernier dossier' $script:recent),(New-ITMCommand 'Supprimer l''apprenant' $script:deleteLearner $null $true $true))
            Add-ITMRibbonGroup 'RECHERCHE' @((New-ITMCommand 'Rechercher' $null {$script:search.Focus()}),(New-ITMCommand 'Effacer les filtres' $script:learnerReset))
            Add-ITMRibbonGroup 'LISTES' @((New-ITMCommand 'Liste visible CSV' $null {Export-ITMVisibleLearners}),(New-ITMCommand 'Paiement' $script:payFromList $null $true $true))
        }
        'Paiements' {
            if($script:scolia){Add-ITMRibbonGroup 'IMPORTS RÉSEAU' @((New-ITMCommand 'Importer les paiements' $null {Show-ScoliaImports} $false),(New-ITMCommand 'Brouillons reçus' $null {Show-ScoliaImports 'Paiements' $false $true} $false),(New-ITMCommand 'Visualiser lʼimportation' $null {Show-ScoliaImports 'Paiements' $true} $false))}
            Add-ITMRibbonGroup 'PRISES EN CHARGE' @((New-ITMCommand 'Prises en charge' $script:peManage))
            Add-ITMRibbonGroup 'CAISSE' @((New-ITMCommand 'Sélectionner' $script:selectPay),(New-ITMCommand 'Enregistrer' $script:paySave $null $true $true $true),(New-ITMCommand 'Corriger le reçu' $script:correct $null $true $true),(New-ITMCommand 'Tous les paiements' $script:allPayments))
            Add-ITMRibbonGroup 'HISTORIQUE' @((New-ITMCommand 'Historique CSV' $null {Export-ITMVisiblePayments}),(New-ITMCommand 'Effacer recherche' $script:clearPay))
        }
        'Exports' {
            Add-ITMRibbonGroup 'INTERFACE' @((New-ITMCommand 'Rapports' $null {Set-ScoliaExportView 0}),(New-ITMCommand 'Cartes dʼexamen' $null {Set-ScoliaExportView 1}))
            if($script:exportTabs -and $script:exportTabs.SelectedIndex -eq 1){
            Add-ITMRibbonGroup 'SÉLECTION' @((New-ITMCommand 'Cocher les cartes' $script:schoolAll),(New-ITMCommand 'Décocher les cartes' $script:schoolNone))
            Add-ITMRibbonGroup 'SESSION' @((New-ITMCommand 'Session actuelle' $script:schoolNow),(New-ITMCommand 'En-tête des cartes' $script:schoolHeaderButton))
            Add-ITMRibbonGroup 'CARTES D''EXAMEN' @((New-ITMCommand 'Aperçu des cartes' $script:schoolPreview),(New-ITMCommand 'Exporter les cartes PDF' $script:schoolPdf),(New-ITMCommand 'Exporter les cartes Word' $script:schoolWord),(New-ITMCommand 'Imprimer les cartes' $script:schoolPrint))
            Add-ITMRibbonGroup 'DOCUMENTS PRODUITS' @((New-ITMCommand 'Grilles remplies' $script:schoolGradesFolder $null $false))
            Add-ITMRibbonGroup 'EXPORTS' @((New-ITMCommand 'Historique des cartes' $script:schoolHistory),(New-ITMCommand 'Dossier des cartes' $script:schoolFolder))
                break
            }
            Add-ITMRibbonGroup 'RAPPORTS' @((New-ITMCommand 'Exporter PDF' $script:exportPDF $null $true $false $true),(New-ITMCommand $(if($script:exportType.SelectedIndex -eq 4){'Exporter Word'}else{'Exporter Excel'}) $script:exportExcel),(New-ITMCommand 'Dernier rapport' $script:openReport))
            Add-ITMRibbonGroup 'CONTRÔLE' @((New-ITMCommand 'Vérifier la liste' $null {Do-Work {Show-ITMReportCheck}}),(New-ITMCommand 'Toutes promotions' $null {$script:exportPromo.SelectedItem='Toutes les promotions'}))
        }
        'Diagnostic' {
            Add-ITMRibbonGroup 'COMPATIBILITÉ' @((New-ITMCommand 'Compatibilité et sauvegardes' $script:compatCheck $null $false),(New-ITMCommand 'Exporter le diagnostic' $script:compatExport $null $false))
            Add-ITMRibbonGroup 'QUALITÉ DES DONNÉES' @((New-ITMCommand 'Actualiser' $script:refresh),(New-ITMCommand 'Anomalies CSV' $null {Save-ITMCsv $script:data.issues 'Diagnostic_ITM.csv'}),(New-ITMCommand 'Doublons' $null {Show-Page 'Administration';$script:adminTabs.SelectedTab=$script:adminPages['Doublons']}))
        }
        'Administration' {
            if($script:scoliaSession.Role -eq 'Comptabilité'){Add-ITMRibbonGroup 'ÉTABLISSEMENT ET COMPTE' @((New-ITMCommand 'Paramètres SCOLIA' $null {Show-ScoliaSettings} $false),(New-ITMCommand 'À propos de SCOLIA' $null {Show-ScoliaAbout} $false));break}
            if($script:scolia){Add-ITMRibbonGroup 'ÉTABLISSEMENT' @((New-ITMCommand 'Paramètres SCOLIA' $null {Show-ScoliaSettings} $false),(New-ITMCommand 'À propos de SCOLIA' $null {Show-ScoliaAbout} $false))}
            Add-ITMRibbonGroup 'PROTECTION DES DONNÉES' @((New-ITMCommand 'Sauvegardes automatiques' $script:autoBackupOptions),(New-ITMCommand 'Journal nominatif' $script:namedJournal),(New-ITMCommand 'Gérer les rôles' $null {Show-ITMRoles} $false))
            Add-ITMRibbonGroup 'DONNÉES' @((New-ITMCommand 'Gérer les classeurs' $null {$script:adminTabs.SelectedTab=$script:adminPages['Classeurs et données']}),(New-ITMCommand 'Classeur suivant' $script:storageNext $null $true $true),(New-ITMCommand 'Sauvegarder tout' $script:backupNow),(New-ITMCommand 'Restaurer copie' $script:restore),(New-ITMCommand 'Nettoyer les sauvegardes' $script:dataCleanup))
            Add-ITMRibbonGroup 'PARAMÈTRES' @((New-ITMCommand 'Promotions' $null {$script:adminTabs.SelectedTab=$script:adminPages['Promotions']}),(New-ITMCommand 'Journal' $null {$script:adminTabs.SelectedTab=$script:adminPages['Journal']}),(New-ITMCommand 'Clôture annuelle' $null {$script:adminTabs.SelectedTab=$script:adminPages['Année scolaire']}),(New-ITMCommand 'Raccourci Bureau' $script:createShortcut $null $false))
        }
        'Données' {
            if($script:scolia){Add-ITMRibbonGroup 'SYNCHRONISATION' @((New-ITMCommand 'Synchronisation' $null {Show-ScoliaSync} $false))}
            if($script:scoliaSession.Role -eq 'Comptabilité'){Add-ITMRibbonGroup 'PARTAGE ET JOURNAL' @((New-ITMCommand 'Configurer le réseau' $null {Open-ScoliaNetwork} $false),(New-ITMCommand 'Dossier SCOLIA' $null {Open-ScoliaFolder} $false),(New-ITMCommand 'Journal CSV' $null {Save-ITMCsv @(Get-ScoliaFinanceRows) 'SCOLIA_Journal.csv'} $false),(New-ITMCommand 'Vérifier opération en attente' $null {Show-ScoliaPending} $false));break}
            if($script:scolia){Add-ITMRibbonGroup 'SCOLIA ET TRANSFERTS' @((New-ITMCommand 'Configurer le réseau' $null {Open-ScoliaNetwork} $false),(New-ITMCommand 'Transférés en brouillon' $null {Show-ScoliaInbox} $false),(New-ITMCommand 'Configuration SCOLIA' $null {Do-Work {Apply-ScoliaWorkbookConfiguration}} $true $true))}
            Add-ITMRibbonGroup 'CONNEXION' @((New-ITMCommand 'Ouvrir données' $script:connect $null $false),(New-ITMCommand 'Fermer données' $script:disconnect $null $false),(New-ITMCommand 'Préparer' $script:prepare $null $false),(New-ITMCommand 'Mémoriser' $script:dataRemember $null $false),(New-ITMCommand 'Ouvrir mémorisé' $script:dataReopen $null $false))
            Add-ITMRibbonGroup 'CRÉATION ET FEUILLES' @((New-ITMCommand 'Nouveau classeur' $script:dataGenerate $null $false),(New-ITMCommand 'Actualiser feuilles' $script:dataRefresh $null $false),(New-ITMCommand 'Dossier sources' $null {[void][IO.Directory]::CreateDirectory((Join-Path ([IO.Path]::GetDirectoryName($PSScriptRoot)) 'sources'));Start-Process -FilePath 'explorer.exe' -ArgumentList ('"'+(Join-Path ([IO.Path]::GetDirectoryName($PSScriptRoot)) 'sources')+'"')} $false))
            Add-ITMRibbonGroup 'GESTION' @((New-ITMCommand 'Gérer les classeurs' $null {Show-Page 'Administration';$script:adminTabs.SelectedTab=$script:adminPages['Classeurs et données']}),(New-ITMCommand 'Sauvegarder tout' $script:backupNow),(New-ITMCommand 'Restaurer copie' $script:restore),(New-ITMCommand 'Nettoyer les sauvegardes' $script:dataCleanup))
        }
        'Scolarité' {
            Add-ITMRibbonGroup 'RECHERCHE ET ACTIONS' @((New-ITMCommand 'Recherche globale' $null {Show-ScoliaGlobalSearch} $true $false $true),(New-ITMCommand 'À traiter' $null {Show-ScoliaActionCenter} $true $false))
            Add-ITMRibbonGroup 'PRÉSENCES ET RÉINSCRIPTION' @((New-ITMCommand 'Présences' $null {Show-ScoliaPupilMonthlyAttendance} $true $false),(New-ITMCommand 'Réinscription' $null {Show-ScoliaReenrollment} $true $false))
            if(-not $script:scoliaSession -or $script:scoliaSession.Role -ne 'Comptabilité'){Add-ITMRibbonGroup 'COURS ET ATTRIBUTIONS' @((New-ITMCommand 'Gestion des cours' $null {Show-ScoliaTeachingManager Courses}),(New-ITMCommand 'Attribution des cours' $null {Show-ScoliaTeachingManager Assignments}),(New-ITMCommand 'Charges des enseignants' $null {Show-ScoliaTeachingManager Dashboard}))}
            if(-not $script:scoliaSession -or $script:scoliaSession.Role -ne 'Comptabilité'){Add-ITMRibbonGroup 'DISCIPLINE' @((New-ITMCommand 'Discipline' $null {Show-ScoliaDiscipline}))}
            Add-ITMRibbonGroup 'ENSEIGNANTS ET SALAIRES' @((New-ITMCommand 'Enseignants' $null {Show-ScoliaTeacherAttendance} $true $false),(New-ITMCommand 'Salaires' $null {Show-ScoliaMonthlyPayroll} $true $false))
            Add-ITMRibbonGroup 'COTES' @((New-ITMCommand 'Cotes et délibération' $script:schoolGrades))
        }
        'Préférences' {
            Add-ITMRibbonGroup 'AFFICHAGE' @((New-ITMCommand 'Valeurs par défaut' $script:displayReset $null $false))
        }
        'Sécurité' {
            Add-ITMRibbonGroup 'ACCÈS' @((New-ITMCommand 'Mot de passe' $script:securityEnable $null $false),(New-ITMCommand 'Verrouiller' $script:securityLock $null $false),(New-ITMCommand 'Désactiver' $script:securityDisable $null $false))
            Add-ITMRibbonGroup 'PRÉFÉRENCES' @((New-ITMCommand 'Enregistrer' $script:securitySave $null $false),(New-ITMCommand 'Événements CSV' $null {Save-ITMCsv $script:security.Events 'Securite_ITM.csv'} $false))
        }
    }
    } finally {$script:ribbonFlow.ResumeLayout($true)}
    if(-not $script:frequentBuilding){Update-ITMFrequentCommands;Update-ITMNavigation;if(Get-Command Update-ScoliaActiveWorkspaceCommand -ErrorAction SilentlyContinue){Update-ScoliaActiveWorkspaceCommand}}
}
function Invoke-ITMRibbonControl($control) {
    if(-not $control.Enabled){return}
    # PerformClick skips controls on hidden pages; dispatch the shared Click handler directly.
    $method=$control.GetType().GetMethod('OnClick',[Reflection.BindingFlags]'Instance,NonPublic')
    [void]$method.Invoke($control,[object[]]@([EventArgs]::Empty))
}
function Get-ITMVisibleGridRows($grid) {
    return @(foreach($row in $grid.Rows){if(-not $row.IsNewRow -and $row.Visible){
        $values=[ordered]@{}
        foreach($column in $grid.Columns){if($column.Visible){$values[$column.HeaderText]=[string]$row.Cells[$column.Index].Value}}
        [pscustomobject]$values
    }})
}
function Export-ITMVisibleLearners {
    $rows=Get-ITMVisibleGridRows $script:learnersGrid
    Save-ITMCsv $rows 'Apprenants_ITM.csv'
}
function Export-ITMVisiblePayments {
    $rows=Get-ITMVisibleGridRows $script:paymentGrid
    Save-ITMCsv $rows 'Paiements_ITM.csv'
}
function Show-ITMReportCheck {
    $mode=switch($script:exportType.SelectedIndex){0{3};1{1};2{2};3{4};default{throw 'Choisissez un rapport.'}}
    $month=$script:exportMonth.Text;$promo=$script:exportPromo.Text
    if($mode -eq 3){$month=''}elseif($month -eq 'Tous les mois'){throw 'Choisissez un mois précis pour ce rapport.'}
    if($promo -eq 'Toutes les promotions'){$promo=''}
    $preview=Invoke-ITMJson 'ITMAppVerifierRapport' @($month,$promo,$mode)
    [void](Confirm-ITMReportPreview $preview ([string]$script:exportType.Text) $month $promo)
}
function Update-ITMNavigation {
    if(-not $script:main -or $script:main.IsDisposed -or $script:navigationLayoutBusy){return}
    $signature=@($script:hostPanel.ClientSize.Width,$script:hostPanel.ClientSize.Height,$script:main.CurrentAutoScaleDimensions.Height,$script:ribbonGeometryRevision,$script:ribbonPeek,($script:tabsNames -join '|'),$script:brandTitle.Text,$script:brandNote.Text,$script:accountButton.Text,$script:featureMenuItems.Count,@($script:frequentButtons).Count,$script:pageTitle.Text,$script:fileLabel.Text,$script:displayPreferences.RibbonMode,$script:displayPreferences.TextSize,$script:displayPreferences.Theme) -join '~'
    if($script:scoliaLastNavigationSignature -ceq $signature){return}
    $script:navigationLayoutBusy=$true
    try {
        $script:top.AutoScrollPosition=New-Object Drawing.Point(0,0)
        $width=[Math]::Max(80,$script:hostPanel.ClientSize.Width-[Windows.Forms.SystemInformation]::VerticalScrollBarWidth)
        $q=[Math]::Max(1,[Math]::Min(2.5,$script:main.CurrentAutoScaleDimensions.Height/96.0))
        $logicalWidth=$width/$q;$short=$script:hostPanel.ClientSize.Height/$q -lt 560
        $margin=[Math]::Max(8,[int](16*$q));$gap=[Math]::Max(3,[int]($(if($short){4}else{8})*$q))
        $logoBase=$(if($logicalWidth -lt 840){36}elseif($logicalWidth -lt 1180){42}else{48})
        $logo=[Math]::Max(28,[int]($logoBase*$q))
        $script:brandLogo.SetBounds($margin,[int](10*$q),$logo,$logo)
        $textX=$margin+$logo+[int](14*$q)
        $hasFeatureMenu=[bool]($script:featureMenu -and -not $script:featureMenu.IsDisposed -and $script:featureMenuItems)
        $stackBrand=($logicalWidth -lt 1040)

        # Header command widths are always clamped to the real client width. This
        # prevents negative/oversized bounds on narrow windows and high DPI.
        $availableHeader=[Math]::Max(40,$width-2*$margin)
        $accountWidth=0;$toggleWidth=0
        if($script:accountButton){
            Set-ITMAdaptiveFont $script:accountButton 10 ([Drawing.FontStyle]::Bold)
            $accountMeasure=[Windows.Forms.TextRenderer]::MeasureText($script:accountButton.Text,$script:accountButton.Font).Width+[int](32*$q)
            $accountWidth=[Math]::Min($availableHeader,[Math]::Max([int](72*$q),[Math]::Min($accountMeasure,[int]($availableHeader*.32))))
        }
        $quickCount=@($script:frequentButtons).Count;$quickCell=[int](34*$q);$quickGap=[Math]::Max(2,[int](4*$q))
        $quickWidth=$(if($quickCount){$quickCount*$quickCell+[Math]::Max(0,$quickCount-1)*$quickGap}else{0})
        if($script:ribbonToggle){
            $script:ribbonToggle.Text=$(if(Test-ITMRibbonExpanded){'Masquer les commandes'}else{'Afficher les commandes'})
            if($short){$script:ribbonToggle.Text='Ruban'}
            Set-ITMAdaptiveFont $script:ribbonToggle 10 ([Drawing.FontStyle]::Bold)
            $toggleMeasure=[Windows.Forms.TextRenderer]::MeasureText($script:ribbonToggle.Text,$script:ribbonToggle.Font).Width+[int](34*$q)
            if($stackBrand){
                $reserved=$accountWidth+$gap+$(if($short){$quickWidth+$gap}else{0})
                $toggleWidth=[Math]::Max([int](78*$q),[Math]::Min([Math]::Max([int](78*$q),$toggleMeasure),$availableHeader-$reserved))
            }else{$toggleWidth=[Math]::Min([int]($availableHeader*.34),[Math]::Max([int](98*$q),$toggleMeasure))}
        }

        $rightEdge=$width-$margin
        $accountX=[Math]::Max($margin,$rightEdge-$accountWidth)
        $toggleX=[Math]::Max($margin,$accountX-$gap-$toggleWidth)
        $quickX=[Math]::Max($margin,$toggleX-$gap-$quickWidth)
        $controlsLeft=$(if($quickWidth -gt 0){$quickX}elseif($toggleWidth -gt 0){$toggleX}elseif($accountWidth -gt 0){$accountX}else{$rightEdge})
        $identityAvailable=[Math]::Max(20,$controlsLeft-$textX-$gap)
        $preferredTextWidth=[Math]::Min([int](340*$q),[Math]::Max([int](170*$q),[int]($identityAvailable*.44)))
        $inlineCandidate=[Math]::Max(0,$identityAvailable-$preferredTextWidth-$gap)
        $inlineFeature=$hasFeatureMenu -and -not $stackBrand -and $inlineCandidate -ge [int](360*$q)
        $textWidth=$(if($stackBrand){[Math]::Max(20,$width-$textX-$margin)}elseif($inlineFeature){$preferredTextWidth}else{$identityAvailable})
        $textWidth=[Math]::Max(20,$textWidth)

        $titleFont=$(if($logicalWidth -lt 820){15}elseif($logicalWidth -lt 1180){18}else{20})
        $noteFont=$(if($logicalWidth -lt 820){9}elseif($logicalWidth -lt 1180){10}else{11})
        Set-ITMAdaptiveFont $script:brandTitle $titleFont ([Drawing.FontStyle]::Bold)
        $script:brandTitle.SetBounds($textX,[int](8*$q),$textWidth,(Get-ITMTextHeight $script:brandTitle $textWidth ([int](30*$q))))
        Set-ITMAdaptiveFont $script:brandNote $noteFont ([Drawing.FontStyle]::Regular)
        $script:brandNote.ForeColor=[Drawing.Color]::FromArgb(90,106,134)
        $script:brandNote.Visible=($script:hostPanel.ClientSize.Height/$q -ge 560 -and $logicalWidth -ge 720)
        $script:brandNote.SetBounds($textX,($script:brandTitle.Bottom+[int](2*$q)),$textWidth,(Get-ITMTextHeight $script:brandNote $textWidth ([int](22*$q))))
        $brandBottom=[Math]::Max($script:brandLogo.Bottom,$(if($script:brandNote.Visible){$script:brandNote.Bottom}else{$script:brandTitle.Bottom}))

        if($quickWidth -gt 0){
            $quickY=$(if($stackBrand){$brandBottom+$gap}else{[int](10*$q)})
            $quickStartX=$(if($stackBrand){$(if($short){$margin}else{$textX})}else{$quickX})
            for($i=0;$i -lt $quickCount;$i++){
                $qb=$script:frequentButtons[$i]
                # Hide frequent shortcuts only when even the compact control row cannot fit.
                $qb.Visible=(-not $stackBrand -or -not $short -or ($quickWidth+$toggleWidth+$accountWidth+2*$gap -le $availableHeader))
                if($qb.Visible){$qb.SetBounds(($quickStartX+$i*($quickCell+$quickGap)),$quickY,$quickCell,$quickCell)}
            }
            if(-not ($short -and $stackBrand)){$visibleQuick=@($script:frequentButtons|Where-Object {$_.Visible}).Count;if($visibleQuick){$brandBottom=[Math]::Max($brandBottom,($quickY+$quickCell))}}
        }

        $buttonY=$(if($stackBrand){$brandBottom+$gap}else{[int](10*$q)})
        if($script:accountButton){
            Set-ITMAdaptiveFont $script:accountButton 10 ([Drawing.FontStyle]::Bold)
            $h=Get-ITMTextHeight $script:accountButton $accountWidth ([int](32*$q))
            $script:accountButton.SetBounds($(if($stackBrand){$width-$margin-$accountWidth}else{$accountX}),$buttonY,$accountWidth,$h);$brandBottom=[Math]::Max($brandBottom,$script:accountButton.Bottom)
        }
        if($script:ribbonToggle){
            Set-ITMAdaptiveFont $script:ribbonToggle 10 ([Drawing.FontStyle]::Bold)
            $actualToggleWidth=[Math]::Max(1,[Math]::Min($toggleWidth,$width-2*$margin-$accountWidth-$gap))
            $h=Get-ITMTextHeight $script:ribbonToggle $actualToggleWidth ([int](32*$q))
            $visibleQuickCount=@($script:frequentButtons | Where-Object {$_.Visible}).Count
            $toggleLeft=$(if($stackBrand){$margin+$(if($short -and $visibleQuickCount -gt 0){$quickWidth+$gap}else{0})}else{$accountX-$gap-$actualToggleWidth})
            $script:ribbonToggle.SetBounds([Math]::Max($margin,$toggleLeft),$buttonY,$actualToggleWidth,$h);$brandBottom=[Math]::Max($brandBottom,$script:ribbonToggle.Bottom)
        }

        if($hasFeatureMenu){
            if($inlineFeature){
                $featureX=$textX+$textWidth+$gap;$featureRight=[Math]::Max($featureX,$controlsLeft-$gap);$featureWidth=[Math]::Max(1,$featureRight-$featureX)
                $featureHeight=Update-ScoliaFeatureCommandLayout $featureX $buttonY $featureWidth $q ($featureWidth/$q -lt 760)
                $brandBottom=[Math]::Max($brandBottom,$buttonY+$featureHeight)
            }else{
                # Reduced windows: tools remain inside the institution header and
                # wrap below the identity/account row, never above or over the logo.
                $featureY=$brandBottom+$gap;$featureWidth=[Math]::Max(1,$width-2*$margin)
                $featureHeight=Update-ScoliaFeatureCommandLayout $margin $featureY $featureWidth $q ($featureWidth/$q -lt 980)
                $brandBottom=[Math]::Max($brandBottom,$featureY+$featureHeight)
            }
        }
        $script:brand.Height=[Math]::Max([int](56*$q),$brandBottom+[Math]::Max($gap,[int](8*$q)))
        if($script:brandSeparator){$script:brandSeparator.BackColor=[Drawing.Color]::FromArgb(224,229,237)}
        $tabs=$script:tabs;$tabs.AutoScroll=$false;$tabs.SuspendLayout()
        try {
            $tabs.AutoScrollMinSize=New-Object Drawing.Size(0,0)
            $rows=New-Object 'System.Collections.Generic.List[object]';$row=@();$used=0
            # Navigation adapts to the real logical width. On very small supports the
            # icon becomes the label; names remain exposed through accessibility/help.
            $navIconOnly=($logicalWidth -lt 650)
            $items=@()
            if($navIconOnly){
                $cell=[Math]::Max([int](44*$q),[Math]::Min([int](58*$q),[int]($width/[Math]::Max(1,[Math]::Min($script:tabsNames.Count,7)))))
                foreach($name in $script:tabsNames){
                    $b=$script:nav[$name];$b.Compact=$true;$b.IconOnly=$true
                    Set-ITMAdaptiveFont $b 9 ([Drawing.FontStyle]::Regular)
                    $items+=([pscustomobject]@{Button=$b;Width=$cell;Reserve=0})
                }
            }else{
                # Measure complete labels first, then reduce padding before wrapping.
                foreach($choice in @(@{Font=10;Compact=$false;Reserve=48;Minimum=92},@{Font=9.5;Compact=$true;Reserve=38;Minimum=80},@{Font=9;Compact=$true;Reserve=28;Minimum=68})){
                    $items=@();$totalWidth=0
                    foreach($name in $script:tabsNames){
                        $b=$script:nav[$name];$b.Compact=$choice.Compact;$b.IconOnly=$false
                        Set-ITMAdaptiveFont $b $choice.Font ([Drawing.FontStyle]::Regular)
                        $measured=[Windows.Forms.TextRenderer]::MeasureText($name,$b.Font)
                        $w=[Math]::Max([int]($choice.Minimum*$q),$measured.Width+[int]($choice.Reserve*$q))
                        $items+=([pscustomobject]@{Button=$b;Width=$w;Reserve=[int]($choice.Reserve*$q)})
                        $totalWidth+=$w
                    }
                    if($totalWidth -le $width){break}
                }
            }
            foreach($item in $items){
                $b=$item.Button;$w=[Math]::Min($width,$item.Width)
                if($row.Count -and $used+$w -gt $width){$rows.Add(@($row));$row=@();$used=0}
                if($navIconOnly){$h=[int]($(if($short){34}else{40})*$q)}else{
                    $textWidth=[Math]::Max(1,$w-$item.Reserve)
                    $textHeight=[Windows.Forms.TextRenderer]::MeasureText($b.Text,$b.Font,(New-Object Drawing.Size($textWidth,10000)),[Windows.Forms.TextFormatFlags]::WordBreak).Height
                    $h=[Math]::Max([int]($(if($short){34}else{38})*$q),$textHeight+[int](10*$q))
                }
                $row+=([pscustomobject]@{Button=$b;Width=$w;Height=$h});$used+=$w
            }
            if($row.Count){$rows.Add(@($row))}
            $y=0
            foreach($row in $rows){
                $height=($row | Measure-Object Height -Maximum).Maximum
                $count=[Math]::Max(1,$row.Count)
                $baseWidth=[int][Math]::Floor($width/$count)
                $remainder=$width-($baseWidth*$count)
                $x=0
                for($index=0;$index -lt $row.Count;$index++){
                    $item=$row[$index]
                    $cellWidth=$baseWidth
                    if($remainder -gt 0){$cellWidth++;$remainder--}
                    $item.Button.SetBounds($x,$y,$cellWidth,$height)
                    $x+=$cellWidth
                }
                $y+=$height
            }
            $tabs.SetBounds(0,$script:brand.Height,$width,$y)
        } finally {$tabs.ResumeLayout()}
        $flowWidth=[Math]::Max(8,$width-$script:ribbonFlow.Padding.Horizontal)
        $titleWidth=[Math]::Max(20,$width-2*$margin)
        $titleReserve=(Get-ITMTextHeight $script:pageTitle $titleWidth ([int](26*$q)))+(Get-ITMTextHeight $script:fileLabel $titleWidth ([int](20*$q)))+3*$gap
        $ribbonBudget=[Math]::Max([int](32*$q),$script:hostPanel.ClientSize.Height-$tabs.Bottom-$titleReserve-[int](72*$q)-$script:ribbonFlow.Padding.Vertical)
        Update-ITMFluidRibbon $flowWidth $q $ribbonBudget
        $ribbonHeight=$script:ribbonLayoutHeight+$script:ribbonFlow.Padding.Vertical
        $expanded=Test-ITMRibbonExpanded
        $script:ribbonAutoCompact=$false
        if($script:ribbonToggle){$script:ribbonToggle.Text=$(if($short){'Ruban'}elseif($expanded){'Masquer les commandes'}else{'Afficher les commandes'});$script:ribbonToggle.AccessibleName=$(if($expanded){'Masquer les commandes'}else{'Afficher les commandes'})}
        if($script:inputHelp){$script:inputHelp.SetToolTip($script:ribbonToggle,'Afficher ou masquer les commandes (Ctrl+F1). Le ruban adapte automatiquement la taille et la disposition des commandes.')}
        $script:ribbon.Visible=$expanded
        $height=$(if($expanded){$ribbonHeight}else{0})
        $script:ribbonFlow.AutoScroll=$true
        $script:ribbon.SetBounds(0,$tabs.Bottom,$width,$height)
        $titleWidth=[Math]::Max(20,$width-2*$margin)
        Set-ITMAdaptiveFont $script:pageTitle 12 ([Drawing.FontStyle]::Bold)
        $script:pageTitle.SetBounds($margin,($script:ribbon.Bottom+$gap),$titleWidth,(Get-ITMTextHeight $script:pageTitle $titleWidth ([int](26*$q))))
        $script:fileLabel.SetBounds($margin,($script:pageTitle.Bottom+[int](2*$q)),$titleWidth,(Get-ITMTextHeight $script:fileLabel $titleWidth ([int](20*$q))))
        $fullHeight=$script:fileLabel.Bottom+$gap
        $script:top.AutoScroll=$false
        $script:top.AutoScrollMinSize=New-Object Drawing.Size(0,0)
        $script:top.Height=$fullHeight
        $script:scoliaLastNavigationSignature=$signature
    } finally {$script:navigationLayoutBusy=$false}
}

function Set-ITMRibbonRowLayout($items,[double]$q,[int]$gap) {
    $height=1;$rows=1;$captionHeight=0
    foreach($item in $items){
        $height=[Math]::Max($height,$item.CellHeight)
        $rows=[Math]::Max($rows,$item.Rows)
        $captionHeight=[Math]::Max($captionHeight,[Math]::Max($item.CaptionHeight,$item.Caption.Font.Height+8))
    }
    $captionY=$gap+$rows*($height+$gap)
    for($n=0;$n -lt $items.Count;$n++){
        $item=$items[$n]
        for($col=0;$col -lt $item.Columns;$col++){
            for($row=0;$row -lt $item.Rows;$row++){
                $i=$row*$item.Columns+$col
                if($i -lt $item.Buttons.Count){$item.Buttons[$i].SetBounds(($gap+$col*($item.CellWidth+$gap)),($gap+$row*($height+$gap)),$item.CellWidth,$height)}
            }
        }
        $item.Caption.SetBounds($gap,$captionY,($item.Width-2*$gap),$captionHeight)
        $item.Group.Size=New-Object Drawing.Size($item.Width,($captionY+$captionHeight+$gap))
        $item.Group.ShowSeparator=($n -lt $items.Count-1)
    }
}

function Update-ITMFluidRibbon([int]$available,[double]$q,[int]$maxHeight=400) {
    $groups=@($script:ribbonFlow.Controls|Where-Object {@($_.Controls|Where-Object {$_ -is [Windows.Forms.Button]}).Count})
    $counts=@();$textWidths=@();$textHeights=@();$sources=@()
    foreach($group in $groups){
        $buttons=@($group.Controls|Where-Object {$_ -is [Windows.Forms.Button]});$caption=@($group.Controls|Where-Object {$_ -is [Windows.Forms.Label]})[0]
        $natural=[int](125*$q)
        foreach($button in $buttons){Set-ITMAdaptiveFont $button 9 ([Drawing.FontStyle]::Regular);$natural=[Math]::Max($natural,[Windows.Forms.TextRenderer]::MeasureText($button.Text,$button.Font).Width+[int](40*$q))}
        $natural=[Math]::Min([int](210*$q),$natural);$height=[int](28*$q)
        foreach($button in $buttons){$height=[Math]::Max($height,(Get-ITMTextHeight $button ([Math]::Max(1,$natural-[int](36*$q))) ([int](28*$q))))}
        $counts+=,$buttons.Count;$textWidths+=,$natural;$textHeights+=,$height
        $sources+=,[pscustomobject]@{Group=$group;Buttons=$buttons;Caption=$caption}
    }
    $planner=$(if(Get-Command Get-ScoliaCachedRibbonLayout -ErrorAction SilentlyContinue){'Get-ScoliaCachedRibbonLayout'}else{'Get-ScoliaRibbonLayout'})
    $plan=& $planner ([int[]]$counts) $available $maxHeight $q ([int[]]$textWidths) ([int[]]$textHeights)
    $script:ribbonLayoutHeight=$plan.Height
    if($plan.Width -gt $available){$script:ribbonLayoutHeight+=[Windows.Forms.SystemInformation]::HorizontalScrollBarHeight}
    $script:ribbonFlow.SuspendLayout()
    try{
        $script:ribbonFlow.WrapContents=$false
        $row=@();$lastBand=-1
        foreach($geometry in $plan.Items){
            if($row.Count -and $geometry.Band -ne $lastBand){Set-ITMRibbonRowLayout $row $q $plan.Gap;$row=@()}
            $source=$sources[$geometry.Index];$group=$source.Group;$caption=$source.Caption
            $group.Visible=$true;$group.Margin=New-Object Windows.Forms.Padding(0,0,$plan.Margin,0)
            foreach($button in $source.Buttons){$button.IconOnly=$plan.IconOnly;$button.Visible=$true;$button.AccessibleName=$button.Text;Set-ITMAdaptiveFont $button $(if($plan.IconOnly){8.5}else{9}) ([Drawing.FontStyle]::Regular)}
            $caption.AutoEllipsis=$true;$caption.AccessibleName=$caption.Text;Set-ITMAdaptiveFont $caption 8 ([Drawing.FontStyle]::Regular)
            if($script:ribbonTips){$script:ribbonTips.SetToolTip($caption,$caption.Text)}
            $row+=,[pscustomobject]@{Group=$group;Buttons=$source.Buttons;Caption=$caption;Columns=$geometry.Columns;Rows=$geometry.Rows;Width=$geometry.Width;CellWidth=$geometry.CellWidth;CellHeight=$geometry.CellHeight;CaptionHeight=$geometry.CaptionHeight}
            $lastBand=$geometry.Band
        }
        if($row.Count){Set-ITMRibbonRowLayout $row $q $plan.Gap}
        $actualHeight=0;foreach($group in $groups){$actualHeight=[Math]::Max($actualHeight,$group.Height)}
        $script:ribbonLayoutHeight=$actualHeight
        if($plan.Width -gt $available){$script:ribbonLayoutHeight+=[Windows.Forms.SystemInformation]::HorizontalScrollBarHeight}
    }finally{$script:ribbonFlow.ResumeLayout()}
}
