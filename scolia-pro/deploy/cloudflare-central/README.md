# SCOLIA — fondation du serveur central Cloudflare (pilote technique)

**Version source :** SCOLIA Pro 1.2.86.
**Environnement de test existant :** Worker `scolia-api`, D1 `scolia-test` (binding `DB`), bucket R2 privé `scolia-files` (binding `FILES`), `https://api.jp-services.org`.

## Ce qui est opérationnel et ce qui ne l'est pas

Le test `/health` du Worker a déjà confirmé des connexions à D1 et R2, **mais il ne valide ni connexion de compte, ni paiement, ni synchronisation scolaire**.

Le script `migrations/0001_core.sql` prépare la **structure centrale minimale** nécessaire à un futur service métier :
- écoles séparées ;
- utilisateurs et rôles liés à leur école ;
- appareils liés au compte utilisateur autorisé par un responsable, avec auteur de révocation tracé ;
- opérations identifiées de façon stable avec contrainte d'idempotence ;
- journal chronologique de changements validés et curseurs par appareil.

Chaque table et relation est prévue pour empêcher une référence inter-établissements. La **vérification cryptographique des identités, des permissions, des révocations et des règles métier incombe au Worker** ; ces tables ne suffisent pas à assurer la sécurité à elles seules.

Le journal ne contient pour l'instant que des métadonnées d'événements et leurs empreintes, **aucune donnée d'apprenant, montant ou note**. Il ne permet pas encore de reconstituer l'état métier.

**Ce dossier n'est pas automatiquement déployé dans le compte Cloudflare de l'établissement.** En particulier, ne pas exécuter la migration sur une base comportant des données réelles avant les contrôles de compatibilité et de restauration.

## Vérifier les migrations hors production

Depuis la racine du dépôt :

```sh
python3 -m unittest discover -s scolia-pro/tests -p 'test_cloudflare_central_schema.py' -v
```

Tests réalisés sur une vraie base SQLite temporaire en mémoire, sans réseau, couvrant :
- idempotence par école ;
- appareil/auteur et identité de même établissement ;
- refus des événements tant que l'opération n'a pas été appliquée ;
- impossibilité d'associer un événement à l'opération d'une autre école ;
- curseurs et identités isolés ;
- réexécution additive du script.

Un workflow GitHub Actions exécute également ces tests à chaque modification du dossier.

## Nouvelle API de synchronisation — prototype testé, NON déployé

Le fichier `src/index.mjs` contient une première API Cloudflare Workers ne permettant **aucune mutation scolaire**. Les tests `tests/auth-sync.test.mjs` s'exécutent sans données réelles.

Fonctionnement protégé :
- `GET /v1/sync/events?after=0&limit=50` retourne les métadonnées des changements **du seul établissement authentifié**. Le `limit` accepté est compris entre 1 et 100, et les événements ne contiennent aucun contenu métier.
- `POST /v1/sync/ack` avec `{"revision": 10}` conserve une progression monotone, uniquement pour le téléphone/ordinateur autorisé ; l'application doit d'abord appliquer localement les données dans une transaction, avant d'envoyer l'accusé.
- Les routes demandent un JWT **HS256 court (15 minutes maximum)**, audience `scolia-api`, émetteur `scolia-central`, contenant `school_id`, `sub`, `device_id` et `permission_revision`.
- Le Worker vérifie également dans D1 que l'établissement est actif, que le compte est habilité et que l'appareil n'a pas été révoqué.
- `GET /v1/admin/devices` : retourne au maximum 100 identifiants d'appareils et leurs métadonnées d'approbation / révocation du **seul établissement authentifié**, sans secrets.
- `POST /v1/admin/devices/revoke` : corps `{"device_id":"identifiant"}`, réservé au secrétariat/direction. Marque l'appareil comme révoqué et conserve `revoked_by` dans le même UPDATE D1. Une deuxième révocation retourne `already_revoked:true`; une tentative sur l'appareil de la session active est bloquée afin d'éviter un verrouillage accidentel du poste administratif.
- Une révocation est appliquée à la **prochaine requête réseau** du téléphone : elle n'est pas instantanée si le téléphone est hors ligne. La politique locale de durée de validité reste à définir et à tester.
- Ces routes **ne créent pas d'appareil** : l'appairage QR sécurisé et la délivrance initiale des comptes restent à implémenter avant toute utilisation réelle.
- Les routes de lecture actuelles ne sont accessibles qu'aux rôles de direction et secrétariat. Les autorisations spécifiques des enseignants, de la comptabilité et des étudiants seront construites avant l'accès aux contenus correspondants.
- Le secret `SCOLIA_AUTH_HMAC_KEY` (clé aléatoire base64url de 32 octets minimum) doit être défini exclusivement dans les paramètres protégés du Worker **après** création d'un service d'identité approuvé. Jamais dans le dépôt, une APK ou les fichiers de configuration Android.
- **Aucun service d'émission ou de renouvellement des jetons n'existe encore**. Le prototype ne peut pas authentifier les utilisateurs réels à ce stade.

Depuis `scolia-pro/deploy/cloudflare-central`, exécuter :

```sh
npm test
```

Le workflow `SCOLIA Cloudflare Central Preview - securite API` vérifie ces routes, notamment les jetons invalides, comptes désactivés, appareils révoqués, paramètres malformés, isolation des établissements et ACK bornés.

**Ne pas déployer ce prototype sur le domaine de production.** Il n'est relié ni aux API métier actuelles, ni aux tables de dossiers scolaires, ni à la gestion des comptes. Le `/health` de ce prototype ne teste pas réellement D1/R2, contrairement au `/health` déjà configuré manuellement dans le Worker de l'établissement.

## Portes de validation avant activation de l'API

1. Inventorier **toutes** les données métier réelles du modèle EF Core 1.2.86 et définir une migration complète vers D1.
2. Concevoir les sessions natives, les jetons de renouvellement et l'approbation QR des appareils, sans placer de secret central dans un APK. Le prototype permet déjà liste/révocation des appareils, **mais pas l'activation initiale ou la création des comptes**.
3. Implémenter la gestion des comptes du secrétariat avec droits vérifiés côté cloud ; propagation des révocations.
4. Développer les API métier des inscriptions, notes, présences et surtout des paiements (atomiques, idempotents, conformes aux règles SCOLIA existantes).
5. Implémenter `push/pull` des changements validés, pagination par révision, tombstones et traitement des conflits.
6. Mettre à jour les clients Android/Windows pour qu'ils utilisent leur SQLite locale et se synchronisent directement avec `api.jp-services.org`.
7. Tester migrations/restaurations, montées en charge, coupures Internet, redémarrages, paiements concurrents et isolation multi-établissements avant toute migration réelle.

Le PC du secrétariat restera **poste d'administration et cache local** ; l'objectif est que Cloudflare devienne serveur central indépendant du PC, mais ce basculement **n'est pas encore réalisé**.

## Coûts et prudence

- D1 / R2 / Workers ont des quotas : les surveiller et prévoir un mode dégradé sans perte des données.
- Ne pas activer de route générique non authentifiée exposant des tables scolaires.
- Ne pas rendre public le bucket R2.
- Aucun utilisateur ne doit obtenir un reçu définitif pendant une coupure sans acquittement du serveur central.
