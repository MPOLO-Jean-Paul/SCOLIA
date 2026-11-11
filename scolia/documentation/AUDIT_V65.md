# Audit de SCOLIA V65

## Résultat

La version V65 apporte une correction de compatibilité des horodatages et des règles communes de présentation des fenêtres. Elle conserve le contenu fonctionnel de V64 et les données présentes dans l'archive.

## Anomalie corrigée

Certaines versions de PowerShell convertissent automatiquement les chaînes de date JSON en objets DateTime. La copie ou le rechargement d'un registre pouvait alors perdre la représentation initiale avec décalage horaire et empêcher une validation de signature du personnel. Le lecteur commun conserve les chaînes originales. Windows PowerShell 5.1 continue à utiliser son lecteur natif ; les versions plus récentes conservent explicitement les chaînes.

## Interfaces et fenêtres

- Ajustement des dimensions et du placement à l'écran réel, y compris les écrans secondaires à coordonnées négatives.
- Conservation des dispositions spécialisées ; adaptation des formulaires standard avec paires libellé et champ.
- Largeur des options des listes déroulantes et retour à la ligne des libellés automatiques.
- Présentation des tableaux, noms accessibles, infobulles et raccourcis communs.
- Application des thèmes communs aux fenêtres auxiliaires et libération des ressources à leur fermeture.
- Préservation des valeurs des contrôles, des contextes stockés dans Form.Tag et des permissions des commandes.

## Contrôles exécutés

Quatorze suites de tests exécutables dans cet environnement ont été utilisées : personnel, enseignements, discipline, paiements, cotes, signataires, rapport de rentrée, ruban adaptatif, seuil horaire, calendrier des retards, grilles de cours, qualité JSON et géométrie, services du personnel et sources de paie.

Les contrôles comprennent notamment 43 vérifications du personnel, 33 des enseignements, 46 de discipline, 32 ciblées sur les paiements avec Excel simulé, 288 dispositions de ruban et des contrôles d'écrans réduits et multiples. Le test de lecture JSON contrôle la précision, le décalage horaire, les tableaux vides, les valeurs nulles, les accents et les copies de données.

## Limites de validation

Le présent environnement ne peut pas afficher Windows Forms ni automatiser Microsoft Office sur Windows. Les fenêtres réelles, la compilation des composants graphiques Windows, le fonctionnement avec le classeur réel ouvert dans Excel et la conversion Word vers PDF ne sont donc pas déclarés vérifiés ici. Les tests natifs nécessaires sont fournis dans `00_Demarrage/VERIFIER_AUDIT_V65.cmd`.

Les contrôles de syntaxe et d'intégrité de l'archive et du manifeste de mise à jour sont effectués avant livraison. Les dossiers de données, de configuration et d'archives sont comparés à la version précédente pour vérifier leur conservation.
