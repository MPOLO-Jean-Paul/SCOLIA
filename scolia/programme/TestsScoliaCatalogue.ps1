$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'TestsCatalogueCommandes.ps1')
$script:scolia=[pscustomobject]@{Id='test'};$script:testCommands=@();$script:tabsNames=@($pages)+@('Comptabilité')
foreach($page in $script:tabsNames){$script:ribbonPage='';$script:currentPage=$page;Update-ITMRibbonState}
foreach($label in @('Synchronisation','Brouillons de caisse','Configurer le réseau','Encaisser','Nouvelle dépense','Transférés en brouillon','Paramètres SCOLIA','Configuration SCOLIA','À propos de SCOLIA','Vérifier opération en attente')){
 $command=@($script:testCommands|Where-Object {$_.Label -ceq $label});if(-not $command.Count){throw "Commande absente : $label"};if(-not (Get-ITMCommandInfo $label)){throw "Aide absente : $label"}
}
foreach($label in @('Encaisser','Nouvelle dépense','Vérifier opération en attente','Brouillons de caisse')){if((Get-ITMCommandPermission $label) -ne 'Encaisser'){throw "Permission caisse incorrecte : $label"}}
if((Get-ITMCommandPermission 'Transférés en brouillon') -ne 'Recevoir'){throw 'Permission réception incorrecte'}
'PASS rubans SCOLIA, descriptions, raccourcis, permissions caisse/réception et catalogue de lancement'
$script:scoliaSession=[pscustomobject]@{Role='Comptabilité'}
foreach($page in @('Tableau de bord','Administration','Données')){
 $script:testCommands=@();$script:ribbonPage='';$script:currentPage=$page;Update-ITMRibbonState
 if(@($script:testCommands|Where-Object {$_.Label -in @('Inscrire','Paiements','Ouvrir données','Préparer','Transférés en brouillon','Clôture annuelle')}).Count){throw "Commandes du secrétariat visibles dans le ruban comptable : $page"}
}
if((Get-ITMCommandPermission 'Configurer le réseau') -ne 'Administrer'){throw 'Permission réseau incorrecte'}
if(-not @($script:testCommands|Where-Object {$_.Label -ceq 'Configurer le réseau'}).Count){throw 'Assistant réseau absent du ruban comptable'}
if(-not (Get-ITMCommandInfo 'Dossier SCOLIA')){throw 'Aide du partage absente'}
'PASS rubans comptables distincts : tableau de bord, administration et données sans commandes scolaires'
