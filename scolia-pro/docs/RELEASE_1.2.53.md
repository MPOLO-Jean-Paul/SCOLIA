# SCOLIA Pro 1.2.53 — gestion des délibérations rangée par nature et espace enseignant

## Menus rangés par nature
- Nouveau bloc **GESTION DES DÉLIBÉRATIONS**, dans l'ordre du travail :
  - **Enseignants et cours** : « Attribution des cours » (maxima, pondération, semestre, enseignant) et « Comptes enseignants » ;
  - **Délibération** : paramètres, codes anonymes, encodage des cotes, suivi de l'encodage, délibération.
- Le bloc **GESTION SCOLAIRE** garde le secrétariat et la vie de classe (cours et notes, résultats, présences, discipline).
- Ruban : nouvel onglet **Délibérations** (Enseignants et cours · Préparation · Encodage · Jury) ; l'onglet Scolarité ne mélange plus les deux.
- Un groupe du ruban dont aucune commande n'est permise disparaît, et un onglet vide aussi.
- Même rangement sur téléphone et tablette, et dans « Info sur l'application ».

## Comptes enseignants (secrétariat)
- **Nouvel enseignant** : la fiche du personnel (fonction « Enseignant ») et le compte sont créés en une seule étape.
- Identifiant proposé « prénom.nom » (sans accents, rendu unique : `jerome.kalombo2` pour un homonyme), modifiable à la création.
- **Code provisoire généré** (quatre lettres, un tiret, quatre chiffres, sans caractères ambigus), montré une seule fois sur la page, jamais enregistré en clair ni sur le poste.
- Sur la même page : nouveau code provisoire en cas d'oubli (les sessions ouvertes sont fermées), privilège jury, désactivation et réactivation (avec un nouveau code). Les cotes déjà encodées restent toujours.
- Liste des enseignants avec leurs cours de l'année, l'état du compte (pas de compte, code provisoire, actif, verrouillé, désactivé), le privilège jury et la dernière connexion ; recherche et filtre « sans compte actif ».

## Espace enseignant
- À la connexion, l'enseignant arrive sur **Mon espace** : salutation, ses chiffres (cours, cotes encodées, feuilles remises, encodage ouvert), une carte par cours avec son avancement et son état (à encoder, en cours, à remettre, remis, délibéré), les annonces de la délibération et les règles de l'encodage en vigueur.
- Un clic sur un cours ouvre directement sa feuille anonyme.
- Ses menus ne contiennent que son métier : « Mon espace » et « Mes cours et cotes ».

## Sécurité
- **Code provisoire obligatoirement remplacé** : tant que l'enseignant n'a pas choisi son propre code, le serveur refuse l'ouverture de ses cours, de ses feuilles et l'encodage. Le secrétariat, qui connaît le code provisoire, ne peut donc pas encoder au nom d'un enseignant.
- Les pages de comptes enseignants exigent la gestion des utilisateurs ; la création d'une fiche exige en plus la gestion du personnel.
- Nouveau test de cloisonnement : un compte enseignant reçoit un refus (403) sur les apprenants, inscriptions, paiements, dépenses, caisse, rapports, cartes, scolarité, personnel, utilisateurs, comptes enseignants, audit, sauvegardes, recherche, pilotage, liste des enseignants et pages de délibération du secrétariat.

## Contrôles
- Tests d'intégration : création des comptes enseignants, code provisoire, homonymes, réactivation, cloisonnement ; tests existants adaptés au remplacement obligatoire du code provisoire.
- Test de l'interface : bloc « GESTION DES DÉLIBÉRATIONS » et bloc « ESPACE ENSEIGNANT ».
- Aucun changement de schéma de base de données.

## Distribution
- Version Android affichée : **1.2.53** ; **versionCode 173**.
- Signature Android officielle inchangée (certificat de la PR #18), vérifiée avant publication.
