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

Le catalogue embarque également les tailles optiques Microsoft : 168 tracés de 16 points et 191 tracés de 20 points, avec la même provenance et leurs empreintes dans le manifeste. Les commandes sélectionnent le dessin adapté à leur taille ; les géométries de connexion et de navigation partagées utilisent ce même catalogue. Les dessins personnalisés conservent leur grille d'origine.

Le contrôle de rendu des icônes couvre tous les écrans principaux, les flèches, le réseau et l'action de salaire à huit tailles ou proportions de conteneur et trois densités (100 %, 125 % et 200 %). Il vérifie les pixels visibles, l'absence de coupe, les proportions, l'identité du rendu des commandes et des PathIcon partagés, ainsi que l'absence de doublons entre écrans à 16, 20 et 24 points. Une planche claire et sombre permet aussi leur contrôle visuel.

La comparaison a révélé que l'alias automatique « Outline » pouvait réutiliser à 16 points le dessin d'une autre fonction lorsque sa variante native manquait. Les alias optiques ne partagent désormais leur dessin que si leurs tracés d'origine de 24 points sont identiques ; sinon le catalogue choisit la taille native suivante. « Résultats » et « Mes résultats » gardent ainsi leurs identités distinctes jusque dans les petites commandes.

## Exécutions locales

- 35 tests serveur ciblés, puis le scénario de révocation des délégations en ligne : réussis.
- 61 contrôles fonctionnels natifs : réussis.
- Matrice des rôles en mode mixte et séparé : réussie ; correction de la colonne Salaire confirmée dans les deux formats.
- Catalogue Fluent : six tests réussis après ajout du pictogramme spécifique.
- Migration et modèle de données : aucune modification de modèle en attente après génération de la migration.

La chaîne d'intégration rejoue les suites complètes et les captures des rôles avant la publication. Les vérifications utilisent des bases temporaires et des données fictives.

## Téléchargement des mises à jour Android

Le signalement « Télécharger ne fait rien apparaître » a conduit à retenir la publication avant sa signature. Le lien public de l'APK répond correctement, y compris avec le User-Agent de l'application, une requête fraîche et les redirections GitHub (réponse HTTP 206 pour la lecture partielle).

Le téléchargement affiche désormais une fenêtre dans SCOLIA dès son démarrage, avec les octets reçus, le pourcentage et le motif d'attente. Les notifications Android ne sont plus le seul retour visible. La recherche manuelle retrouve un transfert actif ; un transfert disparu, échoué ou périmé peut être proposé à nouveau. Un fichier terminé reste disponible pour la vérification et l'installation.

Chaque tentative utilise une destination distincte pour éviter les collisions avec un ancien transfert. Les liens HTTPS restent limités au dépôt officiel. La méthode Navigateur ouvre ce même fichier officiel ; Android conserve le contrôle du certificat lors de l'installation. Le parcours interne conserve la vérification SHA-256 et du numéro de version. Les erreurs de stockage, les échecs de téléchargement et les fichiers manquants ou refusés sont présentés dans une fenêtre persistante.

Les 80 tests mobiles réussissent, dont 23 nouveaux cas sur la progression, les attentes Wi-Fi/réseau, les erreurs de stockage et la reprise des transferts. Le typage du code de l'interface a aussi été vérifié avec les références Android et AndroidX utilisées par l'application. Le build APK complet et les autres contrôles sont rejoués avant la publication officielle.
