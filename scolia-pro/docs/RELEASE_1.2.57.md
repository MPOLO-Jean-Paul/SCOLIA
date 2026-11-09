# SCOLIA Pro 1.2.57 — audit par rôle : secrétaire, comptable, promoteur, enseignant

Chaque compte a été connecté sur ordinateur (1366 × 860) et sur téléphone (412 × 892) ; chaque page de son menu a été ouverte et photographiée, et les parcours métier ont été rejoués (inscription, encaissement, dépenses, notes, appel, personnel, utilisateurs, sauvegarde, recherche, encodage anonyme des cotes). Aucune page ne s'ouvre en erreur.

## Droits : chacun ne voit que ce qu'il peut faire
- **Promoteur et enseignant** : le ruban n'affiche plus « Tableau de bord » ni « Rapports et exports » quand le compte n'a pas le droit de les lire.
- **Comptable** : « Effacer des dossiers d'apprenants », « Importer », « Modèle d'import » et « Restaurer » n'apparaissent plus ; le serveur les refuse aussi (403). L'effacement des paiements reste réservé au droit d'annulation.
- Fenêtre d'effacement : seuls les choix permis au compte sont proposés.

## Enseignant
- **Téléphone** : la barre du bas propose « Mon espace » et « Mes cours et cotes » (avant : seulement « Menu »).
- Après « Enregistrer les cotes », le poste ne s'annonce plus « Nouvelles données d'un autre poste » pour son propre enregistrement, et la barre d'état n'affiche plus de ligne vide « · : ».

## Secrétariat
- **Comptes enseignants** : la liste montre les enseignants (fonction d'enseignement, cours attribués ou compte déjà créé) ; « Tout le personnel » affiche le reste au besoin. Les chiffres du haut portent sur les enseignants.
- **Suivi de l'encodage** : colonnes lisibles en entier ; le pourcentage est joint à « Encodées » (11 / 22 · 50 %).

## Mise en page
- Titres de colonnes non tronqués (Mes cours, Comptes enseignants, Suivi de l'encodage).
- Caisse sur téléphone : l'aide « Chercher l'apprenant : nom ou matricule » passe à la ligne au lieu de déborder.
- Accueil sur téléphone : la ligne du compte tient sur deux lignes.
- Menu du téléphone : « Réseau et postes » rangé sous **Connexions**, comme sur ordinateur.
- Sauvegarde en ligne : sous-titre clair quand le serveur n'est pas encore inscrit auprès de JP-Services.

## Contrôles
- Nouveau parcours automatique « audit visuel par rôle » : 4 comptes × 2 formats, rapport page par page, captures publiées.
- Tests de frontière des rôles : comptable (effacement de dossiers, modèle d'import, restauration → 403), promoteur (pilotage permis, dossiers et paiements refusés).

## Distribution
- Android : **1.2.57**, **versionCode 177** ; signature officielle inchangée.
