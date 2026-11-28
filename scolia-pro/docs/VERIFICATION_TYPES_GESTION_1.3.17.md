# Vérification de la gestion mixte et séparée

La version 1.3.17 prolonge l'audit publié en 1.3.16. Le [cadre métier](CADRE_METIER_TYPES_GESTION.md) définit la répartition avant les modifications de l'interface et des contrôles du serveur.

## Comportements vérifiés

- Le mode par défaut d'une école existante est mixte. Le choix est commun à l'école, réservé au secrétariat, authentifié avec son code actuel, réversible et journalisé.
- La séparation est refusée sans compte Comptabilité actif. Le secrétariat conserve les dossiers, la scolarité, les paramètres, les comptes, les sauvegardes et la consultation individuelle.
- Les requêtes comptables du secrétariat séparé sont refusées côté API. La comptabilité séparée définit les salaires et réalise la paie et les prises en charge ; elle ne modifie pas les dossiers administratifs du personnel.
- Une modification administrative d'une fiche ne remplace pas son salaire masqué par zéro. Les bulletins déjà préparés restent des instantanés conservés.
- Un paiement déjà autorisé, mais bloqué avant son écriture, est refusé si les droits ont été retirés entre-temps. Le test contrôle l'absence de reçu créé.
- Les anciens cookies valides sont mis à niveau en mode mixte. Un changement de mode révoque les sessions et délégations des deux métiers concernés ; de nouvelles délégations permettent les seules opérations encore autorisées.
- Le choix d'une option et l'annulation du formulaire n'écrivent rien. Le formulaire d'application exige un champ secret. Le serveur ancien et l'absence de connexion sont expliqués.
- Le retrait des copies locales ne supprime ni les opérations en attente ni les identifiants protégés. Une réponse de consultation ancienne ne peut pas recréer une copie retirée après modification des droits.

## Interfaces

Les parcours des quatre comptes sont contrôlés sur téléphone compact (320 × 640) et ordinateur compact (1024 × 768). Les captures incluent le menu Compte, les deux options, le formulaire du code actuel, les dossiers individuels et le personnel.

Le contrôle a révélé que l'adaptation automatique des colonnes pouvait réafficher la colonne Salaire du secrétariat séparé. La disposition respecte désormais une autorisation distincte de la largeur disponible. Les deux formats ont été revérifiés après correction.

Le nouveau pictogramme de modification du salaire provient du catalogue Microsoft Fluent épinglé du projet. Les tests du catalogue détectent tout identifiant dépourvu de tracé natif.

## Exécutions locales

- 35 tests serveur ciblés, puis le scénario de révocation des délégations en ligne : réussis.
- 61 contrôles fonctionnels natifs : réussis.
- Matrice des rôles en mode mixte et séparé : réussie ; correction de la colonne Salaire confirmée dans les deux formats.
- Catalogue Fluent : six tests réussis après ajout du pictogramme spécifique.
- Migration et modèle de données : aucune modification de modèle en attente après génération de la migration.

La chaîne d'intégration rejoue les suites complètes et les captures des rôles avant la publication. Les vérifications utilisent des bases temporaires et des données fictives.
