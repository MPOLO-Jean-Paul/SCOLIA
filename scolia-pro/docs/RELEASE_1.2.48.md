# SCOLIA Pro 1.2.48 — délibération (fusion d'ITMCD et du projet Délibération)

## Codes anonymes officiels
- **Délibération > Codes anonymes** : les apprenants déjà inscrits reçoivent chacun un code officiel unique pour l'année ; relancer ne change aucun code existant.
- Reprise des codes déjà portés (export d'ITMCD, liste Excel ou CSV) : aperçu, puis attribution des seules lignes reconnues.
- Un code remplacé n'est jamais réattribué ; la liste des codes reste confidentielle au jury.

## Encodage des cotes
- Cotes du S1, du S2, moyennes d'appui (sur 10) et cotes du jury, par feuille, par code anonyme ou par reprise d'une liste (export d'ITMCD).
- Remise officielle d'une feuille complète ; ensuite seul le jury corrige (tracé) ou renvoie la feuille avec un motif.
- **Suivi de l'encodage** : avancement par cours et par enseignant, échecs, moyenne, feuilles remises ou à relancer ; relevé du cours et liste des échecs.

## Enseignants et jury
- Rôle **Enseignant** relié à la fiche du personnel : « Mes cours » ne montre que les cours attribués, toujours en feuille anonyme (codes, jamais les noms).
- **Privilège jury** : le secrétariat coche « Membre du jury » sur le compte d'un enseignant pour qu'il encode les épreuves du jury ; il se retire à tout moment, les cotes encodées restent.
- Notifications : ouverture ou fermeture de l'encodage, remise et renvoi d'une feuille. Les comptes enseignants ne reçoivent ni encaissements ni activité des autres postes.

## Délibération
- Moteur unique repris d'ITMCD : appui jusqu'à 25 % du maxima sous 75 %, échec sous la moitié, admis dès 60 % sans cote manquante, mentions A à E, rang.
- Épreuves du jury (TOTAL 1 : stage, situation d'intégration, dossier, épreuve d'intégration) et blocs (SOUS-TOTAL 2), totaux officiels et codes P / A / E au jury annuel.
- Décisions motivées du jury, clôture figée avec mot de passe, réouverture tracée, passage de classe selon le jury annuel.
- Autorités de l'année : préfet, proviseur, président, secrétaire et membres du jury, imprimés sur les documents.

## Documents
- Procès-verbal, grille de délibération officielle (PDF paysage et Excel), palmarès.
- Bulletin toujours sur une seule page ; « Tous les bulletins » : la promotion entière, un apprenant par page.
- Bulletin officiel vérifiable par QR code (nom masqué), comme les reçus ; une réouverture invalide les anciens codes.

## Cours
- **Pondération** (coefficient, moyenne sur 20 de « Cours et notes ») et **maxima** (points de la délibération, 1 à 2000 ; vide = pondération × 10) sont distincts et expliqués dans tous les formulaires.

## Déploiement
- Android versionCode **168**, version affichée **1.2.48** ; même identité de signature officielle que les versions précédentes.
- Six migrations de base de données, toutes en ajout (aucune donnée existante modifiée), appliquées automatiquement au démarrage du serveur.
- Les anciens comptes enseignants sans fiche du personnel restent désactivés.
