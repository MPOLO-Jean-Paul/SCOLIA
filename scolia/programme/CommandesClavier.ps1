$script:commandDefinitions=@{}
$script:commandDefinitions['Nettoyer les sauvegardes']=[pscustomobject]@{Shortcut='Ctrl+Shift+Delete';Description='Affiche les anciennes copies de la base, vérifie la dernière sauvegarde et permet de supprimer les autres après confirmation.'}
$script:commandDefinitions['Ouvrir le dernier créé']=[pscustomobject]@{Shortcut='Ctrl+Alt+O';Description='Rouvre le dernier classeur généré ou reprend sa préparation sans recréer de fichier.'}
$script:commandDefinitions['Masquer les commandes']=[pscustomobject]@{Shortcut='Ctrl+F1';Description='Affiche ou masque les groupes de commandes. Les onglets restent accessibles et le choix est mémorisé.'}
$script:commandDefinitions['Compte']=[pscustomobject]@{Shortcut='Ctrl+Alt+U';Description='Sélectionne un compte Secrétariat, Comptabilité ou Mixte et demande son mot de passe uniquement si protégé.'}
$script:commandDefinitions['Utiliser ma session Windows']=[pscustomobject]@{Shortcut='Ctrl+W';Description='Utilise l''identité de la session Windows déjà ouverte.'}
$script:commandDefinitions['Connexion au compte']=[pscustomobject]@{Shortcut='Ctrl+Enter';Description='Vérifie l''identifiant et le mot de passe du compte local.'}
$script:commandDefinitions['Créer mon compte']=[pscustomobject]@{Shortcut='Ctrl+N';Description='Inscrit un compte local au profil Windows actuel.'}
$script:commandDefinitions['Récupérer mon compte']=[pscustomobject]@{Shortcut='Ctrl+R';Description='Remplace le mot de passe avec le code personnel du compte.'}
$script:commandDefinitions['Réinitialiser le compte']=[pscustomobject]@{Shortcut='Ctrl+Enter';Description='Valide le nouveau mot de passe après vérification du code.'}
$script:commandDefinitions['Déconnecter le compte']=[pscustomobject]@{Shortcut='Ctrl+Shift+Q';Description='Ferme l''application et termine la session du compte.'}
$script:commandDefinitions['Supprimer l''apprenant']=[pscustomobject]@{Shortcut='Ctrl+Shift+D';Description='Supprime le dossier sélectionné après confirmation, motif et sauvegarde ; conserve son historique de paiement.'}
$script:buttonCommands=@{}
$script:commandHelp=New-Object Windows.Forms.ToolTip
$script:commandHelp.InitialDelay=450;$script:commandHelp.ReshowDelay=150;$script:commandHelp.AutoPopDelay=12000
$script:commandDefinitions['Ouvrir données']=[pscustomobject]@{Shortcut='Ctrl+O';Description='Choisit et connecte un classeur Excel existant.'}
$script:commandDefinitions['Préparer']=[pscustomobject]@{Shortcut='Ctrl+Shift+I';Description='Ouvre l''installateur pour préparer ou mettre à jour le classeur.'}
$script:commandDefinitions['Actualiser']=[pscustomobject]@{Shortcut='F5';Description='Relit les données et met à jour les listes et indicateurs.'}
$script:commandDefinitions['Fermer données']=[pscustomobject]@{Shortcut='Ctrl+Shift+W';Description='Ferme le classeur et conserve son choix de reconnexion automatique.'}
$script:commandDefinitions['Aide / guide']=[pscustomobject]@{Shortcut='F1';Description='Affiche le guide d''utilisation du logiciel.'}
$script:commandDefinitions['Inscrire']=[pscustomobject]@{Shortcut='Ctrl+N';Description='Ouvre le formulaire d''inscription d''un apprenant.'}
$script:commandDefinitions['Paiements']=[pscustomobject]@{Shortcut='Ctrl+P';Description='Ouvre l''onglet de suivi des paiements.'}
$script:commandDefinitions['Rapports']=[pscustomobject]@{Shortcut='Ctrl+R';Description='Ouvre les rapports et leurs filtres d''export.'}
$script:commandDefinitions['Retards en CSV']=[pscustomobject]@{Shortcut='Ctrl+Shift+E';Description='Exporte les apprenants en retard de paiement affichés.'}
$script:commandDefinitions['Modifier']=[pscustomobject]@{Shortcut='Ctrl+E';Description='Ouvre le dossier sélectionné pour le modifier.'}
$script:commandDefinitions['Fiche complète']=[pscustomobject]@{Shortcut='Ctrl+D';Description='Affiche l''identité, la situation et l''historique du dossier sélectionné.'}
$script:commandDefinitions['Dernier dossier']=[pscustomobject]@{Shortcut='Ctrl+L';Description='Rouvre le dernier dossier consulté pendant cette session.'}
$script:commandDefinitions['Rechercher']=[pscustomobject]@{Shortcut='Ctrl+F';Description='Place le curseur dans la recherche des apprenants.'}
$script:commandDefinitions['Effacer les filtres']=[pscustomobject]@{Shortcut='Ctrl+Shift+F';Description='Efface la recherche et rétablit toutes les promotions.'}
$script:commandDefinitions['Liste visible CSV']=[pscustomobject]@{Shortcut='Ctrl+Shift+E';Description='Exporte en CSV les apprenants de la liste visible.'}
$script:commandDefinitions['Paiement']=[pscustomobject]@{Shortcut='Ctrl+P';Description='Sélectionne cet apprenant dans le formulaire de paiement.'}
$script:commandDefinitions['Sélectionner']=[pscustomobject]@{Shortcut='Ctrl+D';Description='Charge l''apprenant sélectionné pour enregistrer son paiement.'}
$script:commandDefinitions['Enregistrer']=[pscustomobject]@{Shortcut='Ctrl+S';Description='Enregistre les informations saisies après validation.'}
$script:commandDefinitions['Corriger le reçu']=[pscustomobject]@{Shortcut='Ctrl+E';Description='Ouvre la correction du paiement sélectionné avec contrôle avant confirmation.'}
$script:commandDefinitions['Tous les paiements']=[pscustomobject]@{Shortcut='Ctrl+L';Description='Quitte la sélection d''un apprenant et affiche l''ensemble des paiements.'}
$script:commandDefinitions['Historique CSV']=[pscustomobject]@{Shortcut='Ctrl+Shift+E';Description='Exporte l''historique des paiements actuellement affiché.'}
$script:commandDefinitions['Effacer recherche']=[pscustomobject]@{Shortcut='Ctrl+Shift+F';Description='Efface la recherche des paiements et la sélection de l''apprenant.'}
$script:commandDefinitions['Exporter PDF']=[pscustomobject]@{Shortcut='Ctrl+P';Description='Vérifie puis génère le rapport choisi au format PDF.'}
$script:commandDefinitions['Exporter Word']=[pscustomobject]@{Shortcut='Ctrl+E';Description='Prépare le rapport de rentrée et génère un document Word modifiable.'}
$script:commandDefinitions['Exporter Excel']=[pscustomobject]@{Shortcut='Ctrl+E';Description='Vérifie puis génère le rapport choisi au format Excel.'}
$script:commandDefinitions['Dernier rapport']=[pscustomobject]@{Shortcut='Ctrl+L';Description='Ouvre le dernier rapport généré pendant cette session.'}
$script:commandDefinitions['Vérifier la liste']=[pscustomobject]@{Shortcut='Ctrl+D';Description='Contrôle le nombre de lignes et les montants du rapport avant export.'}
$script:commandDefinitions['Toutes promotions']=[pscustomobject]@{Shortcut='Ctrl+T';Description='Sélectionne toutes les promotions pour le rapport.'}
$script:commandDefinitions['Anomalies CSV']=[pscustomobject]@{Shortcut='Ctrl+Shift+E';Description='Exporte la liste des anomalies de données à corriger.'}
$script:commandDefinitions['Doublons']=[pscustomobject]@{Shortcut='Ctrl+D';Description='Ouvre le contrôle des dossiers potentiellement en double.'}
$script:commandDefinitions['Gérer les classeurs']=[pscustomobject]@{Shortcut='Ctrl+G';Description='Affiche les classeurs de données, leurs capacités et les seuils de basculement.'}
$script:commandDefinitions['Classeur suivant']=[pscustomobject]@{Shortcut='Ctrl+Shift+N';Description='Crée le segment de stockage suivant dans le même dossier data.'}
$script:commandDefinitions['Sauvegarder tout']=[pscustomobject]@{Shortcut='Ctrl+B';Description='Sauvegarde l''ensemble des classeurs de données.'}
$script:commandDefinitions['Restaurer copie']=[pscustomobject]@{Shortcut='Ctrl+R';Description='Restaure la sauvegarde sélectionnée dans un nouveau dossier de données.'}
$script:commandDefinitions['Promotions']=[pscustomobject]@{Shortcut='Ctrl+P';Description='Affiche la gestion des promotions et des frais scolaires.'}
$script:commandDefinitions['Journal']=[pscustomobject]@{Shortcut='Ctrl+J';Description='Affiche l''historique des opérations enregistrées.'}
$script:commandDefinitions['Clôture annuelle']=[pscustomobject]@{Shortcut='Ctrl+Shift+Y';Description='Ouvre les contrôles d''archivage et de préparation de la nouvelle année.'}
$script:commandDefinitions['Raccourci Bureau']=[pscustomobject]@{Shortcut='Ctrl+Shift+D';Description='Crée le raccourci de l''application sur le Bureau.'}
$script:commandDefinitions['Mot de passe']=[pscustomobject]@{Shortcut='Ctrl+M';Description='Ouvre la configuration de la protection par mot de passe.'}
$script:commandDefinitions['Verrouiller']=[pscustomobject]@{Shortcut='Ctrl+L';Description='Verrouille la session et demande le mot de passe pour reprendre.'}
$script:commandDefinitions['Désactiver']=[pscustomobject]@{Shortcut='Ctrl+Shift+D';Description='Désactive la protection après les vérifications requises.'}
$script:commandDefinitions['Événements CSV']=[pscustomobject]@{Shortcut='Ctrl+Shift+E';Description='Exporte les événements de sécurité en CSV.'}
$script:commandDefinitions['Valeurs par défaut']=[pscustomobject]@{Shortcut='Ctrl+Shift+R';Description='Rétablit les réglages initiaux d''affichage et de thème.'}
$script:commandDefinitions['Classeur existant']=[pscustomobject]@{Shortcut='Ctrl+O';Description='Choisit et connecte un classeur Excel existant.'}
$script:commandDefinitions['Mémoriser']=[pscustomobject]@{Shortcut='Ctrl+M';Description='Mémorise le classeur actuel et active sa reconnexion au démarrage.'}
$script:commandDefinitions['Ouvrir mémorisé']=[pscustomobject]@{Shortcut='Ctrl+L';Description='Reconnecte le classeur mémorisé pour ce compte Windows.'}
$script:commandDefinitions['Nouveau classeur']=[pscustomobject]@{Shortcut='Ctrl+N';Description='Génère un classeur vide dans sources, puis le connecte ou ouvre sa préparation.'}
$script:commandDefinitions['Actualiser feuilles']=[pscustomobject]@{Shortcut='Ctrl+Shift+F5';Description='Relit la liste des feuilles et des colonnes des classeurs connectés.'}
$script:commandDefinitions['Dossier sources']=[pscustomobject]@{Shortcut='Ctrl+D';Description='Ouvre le dossier contenant les classeurs sources générés.'}
$script:commandDefinitions['Nouvelle promotion']=[pscustomobject]@{Shortcut='Ctrl+N';Description='Ouvre la création d''une promotion et de ses frais.'}
$script:commandDefinitions['Modifier la promotion']=[pscustomobject]@{Shortcut='Ctrl+E';Description='Modifie la promotion sélectionnée et ses tarifs.'}
$script:commandDefinitions['Exporter le journal']=[pscustomobject]@{Shortcut='Ctrl+Shift+E';Description='Exporte le journal des opérations en CSV.'}
$script:commandDefinitions['Ouvrir le dossier data']=[pscustomobject]@{Shortcut='Ctrl+Alt+D';Description='Ouvre le dossier de stockage de l''année scolaire connectée.'}
$script:commandDefinitions['Contrôler les classeurs']=[pscustomobject]@{Shortcut='Ctrl+Alt+C';Description='Vérifie la présence et la cohérence des classeurs de données.'}
$script:commandDefinitions['Enregistrer les seuils']=[pscustomobject]@{Shortcut='Ctrl+Alt+S';Description='Enregistre les capacités déclenchant le basculement de stockage.'}
$script:commandDefinitions['Enregistrer le dossier']=[pscustomobject]@{Shortcut='Ctrl+S';Description='Valide les champs puis enregistre l''inscription ou la modification du dossier.'}
$script:commandDefinitions['Vérifier puis confirmer']=[pscustomobject]@{Shortcut='Ctrl+S';Description='Vérifie la correction du paiement et ouvre la confirmation.'}
$script:commandDefinitions['Confirmer la correction']=[pscustomobject]@{Shortcut='Ctrl+Enter';Description='Confirme la correction affichée et autorise son enregistrement.'}
$script:commandDefinitions['Continuer vers la destination']=[pscustomobject]@{Shortcut='Ctrl+Enter';Description='Valide le contrôle du rapport et passe au choix du fichier de sortie.'}
$script:commandDefinitions['Aperçu avant impression']=[pscustomobject]@{Shortcut='Ctrl+P';Description='Affiche l''aperçu d''impression de la fiche de l''apprenant.'}
$script:commandDefinitions['Enregistrer et protéger']=[pscustomobject]@{Shortcut='Ctrl+S';Description='Enregistre le nouveau mot de passe et les paramètres de protection.'}
$script:commandDefinitions['Précédent']=[pscustomobject]@{Shortcut='Alt+Left';Description='Revient à l''étape précédente de l''inscription.'}
$script:commandDefinitions['Suivant']=[pscustomobject]@{Shortcut='Alt+Right';Description='Vérifie les champs de l''étape et passe à la suivante.'}
$script:commandDefinitions['Se connecter']=[pscustomobject]@{Shortcut='Enter';Description='Vérifie le mot de passe et ouvre la session.'}
$script:commandDefinitions['Annuler']=[pscustomobject]@{Shortcut='Escape';Description='Ferme le formulaire sans valider les modifications.'}
$script:commandDefinitions['Fermer']=[pscustomobject]@{Shortcut='Escape';Description='Ferme cette fenêtre.'}
$script:commandDefinitions['Quitter']=[pscustomobject]@{Shortcut='Escape';Description='Quitte la fenêtre de connexion.'}
$script:commandDefinitions['Revenir sans modifier']=[pscustomobject]@{Shortcut='Escape';Description='Annule la confirmation et revient à la correction.'}
$script:commandDefinitions['Revenir aux filtres']=[pscustomobject]@{Shortcut='Escape';Description='Revient aux paramètres du rapport sans exporter.'}
$script:commandDefinitions['Fermer après conservation du code']=[pscustomobject]@{Shortcut='Enter';Description='Ferme cette fenêtre après consultation du code de récupération.'}

$script:commandDefinitions['Encaisser']=[pscustomobject]@{Shortcut='Ctrl+Alt+P';Description='Enregistre un paiement de caisse et prépare son transfert unique au secrétariat.'}
$script:commandDefinitions['Nouvelle dépense']=[pscustomobject]@{Shortcut='Ctrl+Alt+E';Description='Enregistre une sortie de caisse avec sa référence et son libellé.'}
$script:commandDefinitions['Journal CSV']=[pscustomobject]@{Shortcut='Ctrl+Alt+J';Description='Exporte le journal des recettes et dépenses de lʼannée scolaire.'}
$script:commandDefinitions['Reçu / imprimer']=[pscustomobject]@{Shortcut='Ctrl+Alt+R';Description='Affiche la pièce de caisse sélectionnée pour impression ou PDF via lʼimprimante PDF.'}
$script:commandDefinitions['Transférés en brouillon']=[pscustomobject]@{Shortcut='Ctrl+Alt+B';Description='Affiche les paiements reçus à rapprocher ; importe la sélection sans double écriture.'}
$script:commandDefinitions['Paramètres SCOLIA']=[pscustomobject]@{Shortcut='Ctrl+Alt+F2';Description='Configure lʼétablissement, les classes, le calendrier et la sécurité des comptes.'}
$script:commandDefinitions['Configuration SCOLIA']=[pscustomobject]@{Shortcut='Ctrl+Alt+F3';Description='Applique les classes et mensualités à une base scolaire préparée sans paiements.'}
$script:commandDefinitions['À propos de SCOLIA']=[pscustomobject]@{Shortcut='Ctrl+Alt+A';Description='Affiche la présentation de SCOLIA et les coordonnées du programmeur.'}
$script:commandDefinitions['Vérifier opération en attente']=[pscustomobject]@{Shortcut='Ctrl+Alt+V';Description='Vérifie le journal après une interruption et débloque la caisse uniquement après rapprochement.'}
$script:commandDefinitions['Synchronisation']=[pscustomobject]@{Shortcut='Ctrl+Alt+F6';Description='Affiche lʼétat du réseau, la dernière réception et le rapprochement des paiements comptables et scolaires.'}
$script:commandDefinitions['Brouillons de caisse']=[pscustomobject]@{Shortcut='Ctrl+Alt+F7';Description='Consulte, valide après reconnexion ou annule les brouillons locaux de caisse non comptabilisés.'}
$script:commandDefinitions['Configurer le réseau']=[pscustomobject]@{Shortcut='Ctrl+Alt+F5';Description='Crée un partage Windows sur le poste hôte ou recherche et mémorise le dossier partagé de lʼétablissement.'}
$script:commandDefinitions['Dossier SCOLIA']=[pscustomobject]@{Shortcut='Ctrl+Alt+F4';Description='Ouvre le dossier partagé contenant la configuration et le journal de caisse.'}
$script:commandDefinitions['Importer les paiements']=[pscustomobject]@{Shortcut='Ctrl+Alt+Shift+P';Description='Ouvre les paiements reçus à contrôler avant importation dans la base locale.'}
$script:commandDefinitions['Importer les apprenants']=[pscustomobject]@{Shortcut='Ctrl+Alt+Shift+I';Description='Ouvre les apprenants publiés par le secrétariat pour actualiser le répertoire de caisse.'}
$script:commandDefinitions['Visualiser lʼimportation']=[pscustomobject]@{Shortcut='Ctrl+Alt+Shift+V';Description='Consulte les données déjà importées sans nouvelle écriture.'}
$script:commandDefinitions['Brouillons reçus']=[pscustomobject]@{Shortcut='Ctrl+Alt+Shift+B';Description='Consulte les données reçues conservées localement en attente de contrôle et dʼimportation.'}
$script:commandDefinitions['Gestion des cours']=[pscustomobject]@{Shortcut='Ctrl+Alt+Shift+C';Description='Gère les cours par promotion : numéro, volume horaire annuel et cotation maximale.'}
$script:commandDefinitions['Attribution des cours']=[pscustomobject]@{Shortcut='Ctrl+Alt+Shift+T';Description='Associe un cours de la promotion à un seul enseignant, avec historique des réattributions.'}
$script:commandDefinitions['Charges des enseignants']=[pscustomobject]@{Shortcut='Ctrl+Alt+Shift+H';Description='Affiche les cours et promotions de chaque enseignant ainsi que le total des heures annuelles prévues.'}
$script:commandDefinitions['Discipline']=[pscustomobject]@{Shortcut='Ctrl+Alt+Shift+S';Description='Gère les renvois temporaires et définitifs, les convocations des parents et leur historique annuel.'}
function Convert-ITMShortcutKeys([string]$shortcut) {
    if([string]::IsNullOrWhiteSpace($shortcut)){return 0}
    $keys=0
    foreach($part in $shortcut.Split('+')){
        $name=switch($part){'Ctrl'{'Control'};default{$part}}
        $keys=$keys -bor [int][Enum]::Parse([Windows.Forms.Keys],$name)
    }
    return $keys
}
function Get-ITMCommandInfo([string]$label) {
    $aliases=@{
        'Connecter un classeur'='Ouvrir données';'Déconnecter'='Fermer données';'Nouvelle inscription'='Inscrire';'Modifier le dossier'='Modifier';'Modifier ce dossier'='Modifier';'Enregistrer un paiement'='Paiement';'Enregistrer le paiement'='Enregistrer';'Sélectionner cet apprenant'='Sélectionner';'Effacer la recherche'='Effacer recherche';'Corriger le paiement sélectionné'='Corriger le reçu';'Exporter en PDF'='Exporter PDF';'Exporter en Excel'='Exporter Excel';'Exporter en Word'='Exporter Word';'Ouvrir le dernier rapport'='Dernier rapport';'Créer une sauvegarde'='Sauvegarder tout';'Restaurer la copie sélectionnée'='Restaurer copie';'Créer le classeur suivant'='Classeur suivant';'Créer un raccourci sur le Bureau'='Raccourci Bureau';'Archiver et préparer la nouvelle année'='Clôture annuelle';'Activer le mot de passe'='Mot de passe';'Désactiver la protection'='Désactiver';'Verrouiller maintenant'='Verrouiller';'Enregistrer les préférences'='Enregistrer';'Mémoriser ce classeur'='Mémoriser';'Ouvrir le classeur mémorisé'='Ouvrir mémorisé';'Générer et connecter'='Nouveau classeur';'Actualiser les feuilles'='Actualiser feuilles';'Exporter les retards et contacts'='Retards en CSV'
    }
    $name=$(if($aliases.ContainsKey($label)){$aliases[$label]}else{$label})
    if(-not $script:commandDefinitions.ContainsKey($name)){return $null}
    $entry=$script:commandDefinitions[$name]
    return [pscustomobject]@{Description=$entry.Description;Shortcut=$entry.Shortcut;Keys=$(if([string]::IsNullOrWhiteSpace([string]$entry.Shortcut)){0}else{Convert-ITMShortcutKeys $entry.Shortcut})}
}
function Set-ITMCommandHelp($button,$info) {
    if(-not $info){$info=Get-ITMCommandInfo $button.Text}
    if(-not $info){return}
    $script:buttonCommands[$button]=$info
    $text=$info.Description+$(if([string]::IsNullOrWhiteSpace([string]$info.Shortcut)){''}else{"`r`nRaccourci : "+$info.Shortcut})
    $script:commandHelp.SetToolTip($button,$text)
    $button.AccessibleDescription=$text
}
function Invoke-ITMKeyboardButton($button,$event) {
    $event.Handled=$true;$event.SuppressKeyPress=$true
    if(-not $button.Enabled -or ($script:busy -and $button.DialogResult -eq [Windows.Forms.DialogResult]::None)){return}
    Invoke-ITMRibbonControl $button
}
function Enable-ITMMainShortcuts {
    $script:main.KeyPreview=$true
    for($i=0;$i -lt $script:tabsNames.Count;$i++){
        $name=$script:tabsNames[$i]
        $script:commandHelp.SetToolTip($script:nav[$name],"Ouvre l'onglet "+$name+".`r`nRaccourci : Alt+"+$(if($i -eq 9){0}else{$i+1}))
    }
    $script:main.Add_KeyDown({param($sender,$e)
        if($e.Handled){return}
        if($e.Alt -and -not $e.Control -and -not $e.Shift -and $e.KeyCode -eq [Windows.Forms.Keys]::D0 -and $script:tabsNames.Count -ge 10){$e.Handled=$true;$e.SuppressKeyPress=$true;if(-not $script:busy -and $script:nav['Scolarité'].Enabled){Show-Page 'Scolarité'};return}
        if($e.Alt -and -not $e.Control -and -not $e.Shift -and [int]$e.KeyCode -ge [int][Windows.Forms.Keys]::D1 -and [int]$e.KeyCode -le [int][Windows.Forms.Keys]::D9){
            $index=[int]$e.KeyCode-[int][Windows.Forms.Keys]::D1
            if($index -lt $script:tabsNames.Count){$e.Handled=$true;$e.SuppressKeyPress=$true;$name=$script:tabsNames[$index];if(-not $script:busy -and $script:nav[$name].Enabled){Show-Page $name};return}
        }
        if([int]$e.KeyData -eq (Convert-ITMShortcutKeys 'Ctrl+F1')){
            $e.Handled=$true;$e.SuppressKeyPress=$true
            try{Toggle-ITMRibbonVisibility}catch{App-Error $_}
            return
        }
        # Connection shortcuts navigate to their sole command location first.
        foreach($route in @(@{Name='Ouvrir données';Button=$script:connect},@{Name='Préparer';Button=$script:prepare},@{Name='Fermer données';Button=$script:disconnect})){
            if((Get-ITMCommandInfo $route.Name).Keys -eq [int]$e.KeyData){
                $e.Handled=$true;$e.SuppressKeyPress=$true
                if($script:busy){return}
                try{Show-Page 'Données';Invoke-ITMKeyboardButton $route.Button $e}catch{App-Error $_}
                return
            }
        }
        # Ribbon first: exactly the same availability and Click handler as the visible command.
        foreach($group in $script:ribbonFlow.Controls){foreach($button in $group.Controls){
            if($button -is [Windows.Forms.Button] -and $button.Tag -and $button.Tag.PSObject.Properties['Keys'] -and $button.Tag.Keys -eq [int]$e.KeyData){Invoke-ITMKeyboardButton $button $e;return}
        }}
        # Commands unique to the active page/subtab, including promotion and storage settings.
        foreach($button in @($script:buttonCommands.Keys)){
            if(-not $button.IsDisposed -and $button.Visible -and $button.FindForm() -eq $sender -and $script:buttonCommands[$button].Keys -eq [int]$e.KeyData){Invoke-ITMKeyboardButton $button $e;return}
        }
    })
    $script:main.Add_FormClosed({$script:commandHelp.Dispose();$script:buttonCommands.Clear()})
}
function Enable-ITMDialogShortcuts($dialog) {
    if($dialog -eq $script:main -or $script:shortcutDialogs.ContainsKey($dialog)){return}
    $script:shortcutDialogs[$dialog]=$true;$dialog.KeyPreview=$true
    foreach($button in @($script:buttonCommands.Keys)){
        if(-not $button.IsDisposed -and $button.FindForm() -eq $dialog -and $button.Text -eq 'Enregistrer un paiement'){
            Set-ITMCommandHelp $button ([pscustomobject]@{Description='Ouvre la saisie d''un paiement pour cet apprenant.';Shortcut='Ctrl+Shift+P';Keys=(Convert-ITMShortcutKeys 'Ctrl+Shift+P')})
        }
    }
    $dialog.Add_KeyDown({param($sender,$e)
        if($e.Handled){return}
        foreach($button in @($script:buttonCommands.Keys)){
            if(-not $button.IsDisposed -and $button.Visible -and $button.FindForm() -eq $sender -and $script:buttonCommands[$button].Keys -eq [int]$e.KeyData){Invoke-ITMKeyboardButton $button $e;return}
        }
    })
    $dialog.Add_Disposed({param($sender,$e)
        $script:shortcutDialogs.Remove($sender)
        foreach($button in @($script:buttonCommands.Keys)){if($button.IsDisposed){$script:buttonCommands.Remove($button)}}
    })
}
$script:shortcutDialogs=@{}

$script:commandDefinitions['Compatibilité et sauvegardes']=[pscustomobject]@{Shortcut='Ctrl+Alt+T';Description='Vérifie Windows, Excel, la mémoire de connexion et les sauvegardes sans données nominatives.'}

$script:commandDefinitions['Exporter le diagnostic']=[pscustomobject]@{Shortcut='Ctrl+Alt+X';Description='Enregistre le rapport de compatibilité au format texte.'}

$script:commandDefinitions['Sauvegardes automatiques']=[pscustomobject]@{Shortcut='Ctrl+Alt+B';Description='Règle la sauvegarde quotidienne et la conservation des copies automatiques.'}

$script:commandDefinitions['Journal nominatif']=[pscustomobject]@{Shortcut='Ctrl+Alt+J';Description='Affiche les 200 dernières opérations avec leur compte et leur rôle.'}

$script:commandDefinitions['Gérer les rôles']=[pscustomobject]@{Shortcut='Ctrl+Alt+R';Description='Attribue les permissions aux comptes locaux ; commande administrateur.'}

$script:commandDefinitions['Vérifier la sauvegarde']=[pscustomobject]@{Shortcut='Ctrl+Alt+V';Description='Contrôle toutes les empreintes de la copie sélectionnée sans restaurer les données.'}

$script:commandDefinitions['Enregistrer le rôle']=[pscustomobject]@{Shortcut='Ctrl+Enter';Description='Enregistre l''autorisation choisie pour le compte sélectionné.'}
$script:commandDefinitions['Enregistrer les sauvegardes']=[pscustomobject]@{Shortcut='Ctrl+Enter';Description='Enregistre l''activation et le nombre de sauvegardes automatiques à conserver.'}

# Scolarité: shortcuts apply in this tab and share the same command handlers.
# Outils de suivi intégrés au ruban Scolarité. Ces entrées doivent exister avant
# la construction des commandes afin qu'une aide manquante ne bloque jamais le démarrage.
$script:commandDefinitions['Recherche globale']=[pscustomobject]@{Shortcut='Ctrl+K';Description='Recherche dans les apprenants, paiements et reçus accessibles à ce compte.'}
$script:commandDefinitions['À traiter']=[pscustomobject]@{Shortcut='Ctrl+Shift+K';Description='Ouvre le centre des actions, observations, transferts et brouillons à vérifier.'}
$script:commandDefinitions['Présences']=[pscustomobject]@{Shortcut='Ctrl+Alt+F8';Description='Ouvre le suivi des présences et absences des apprenants pour la période sélectionnée.'}
$script:commandDefinitions['Réinscription']=[pscustomobject]@{Shortcut='Ctrl+Alt+F9';Description='Prépare et contrôle la réinscription des apprenants pour l''année scolaire active.'}
$script:commandDefinitions['Enseignants']=[pscustomobject]@{Shortcut='Ctrl+Alt+F10';Description='Ouvre le suivi des enseignants, de leur pointage et de leur assiduité.'}
$script:commandDefinitions['Salaires']=[pscustomobject]@{Shortcut='Ctrl+Alt+F11';Description='Ouvre le calcul et le suivi mensuel des salaires des enseignants selon les droits du compte.'}
$script:commandDefinitions['Aperçu des cartes']=[pscustomobject]@{Shortcut='Ctrl+Shift+P';Description='Affiche les cartes cochées, huit par page A4.'}
$script:commandDefinitions['Exporter les cartes PDF']=[pscustomobject]@{Shortcut='Ctrl+Shift+E';Description='Exporte les cartes cochées en PDF A4, avec les repères de découpe choisis.'}
$script:commandDefinitions['Exporter les cartes Word']=[pscustomobject]@{Shortcut='Ctrl+Shift+W';Description='Crée un document Word modifiable, huit cartes par page A4.'}
$script:commandDefinitions['Imprimer les cartes']=[pscustomobject]@{Shortcut='Ctrl+Shift+I';Description='Choisit une imprimante et imprime les cartes sur A4 à taille réelle.'}
$script:commandDefinitions['Cocher les cartes']=[pscustomobject]@{Shortcut='Ctrl+Shift+A';Description='Coche toutes les cartes visibles après application des filtres.'}
$script:commandDefinitions['Décocher les cartes']=[pscustomobject]@{Shortcut='Ctrl+Shift+U';Description='Décoche toutes les cartes visibles.'}
$script:commandDefinitions['En-tête des cartes']=[pscustomobject]@{Shortcut='Ctrl+Shift+H';Description='Modifie et mémorise les sept mentions administratives de l''en-tête.'}
$script:commandDefinitions['Session actuelle']=[pscustomobject]@{Shortcut='Ctrl+Shift+M';Description='Rétablit le mois et l''année de la date actuelle.'}
$script:commandDefinitions['Historique des cartes']=[pscustomobject]@{Shortcut='Ctrl+Shift+J';Description='Retrouve les lots PDF et Word exportés et ouvre leurs fichiers.'}
$script:commandDefinitions['Dossier des cartes']=[pscustomobject]@{Shortcut='Ctrl+Alt+Shift+K';Description='Ouvre le dossier exports/cartes.'}

$script:commandDefinitions['Enregistrer l''en-tête']=[pscustomobject]@{Shortcut='Ctrl+S';Description='Vérifie et mémorise les sept mentions affichées.'}

$script:commandDefinitions['Prises en charge']=[pscustomobject]@{Shortcut='Ctrl+Alt+K';Description='Gère les bénéficiaires, leurs dix mensualités couvertes et le retour au statut payant.'}
$script:commandDefinitions['Nouvelle prise en charge']=[pscustomobject]@{Shortcut='Ctrl+N';Description='Enregistre la couverture du dossier sélectionné au tarif de sa promotion.'}
$script:commandDefinitions['Modifier la prise en charge']=[pscustomobject]@{Shortcut='Ctrl+M';Description='Modifie le garant et les mois futurs, avec un motif conservé au journal.'}
$script:commandDefinitions['Déclarer payant']=[pscustomobject]@{Shortcut='Ctrl+Shift+Y';Description='Termine la couverture à partir du mois choisi ; conserve les mois antérieurs.'}
$script:commandDefinitions['Exporter les prises en charge']=[pscustomobject]@{Shortcut='Ctrl+E';Description='Exporte la liste et le détail mensuel des bénéficiaires en CSV.'}
$script:commandDefinitions['Enregistrer la prise en charge']=[pscustomobject]@{Shortcut='Ctrl+S';Description='Confirme et mémorise la couverture avec son historique.'}

$script:commandDefinitions['Détail des mois couverts']=[pscustomobject]@{Shortcut='Ctrl+D';Description='Présente les dix montants couverts et les modifications du dossier sélectionné.'}

# Grade commands are also constructed during startup when frequent actions are collected.
$script:commandDefinitions['Cotes et délibération']=[pscustomobject]@{Shortcut='Ctrl+Alt+G';Description='Prépare la grille de la promotion, analyse les cotes XLSX et remplit une copie classée avec les décisions de délibération.'}
$script:commandDefinitions['Grilles remplies']=[pscustomobject]@{Shortcut='Ctrl+Alt+D';Description='Ouvre le dossier des grilles préparées, des copies remplies et des journaux de délibération.'}

$script:commandDefinitions['Cartes dʼexamen']=[pscustomobject]@{Shortcut='Ctrl+Shift+K';Description='Affiche directement la préparation et la sélection des cartes dans Exports.'}
