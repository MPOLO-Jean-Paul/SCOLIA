# SCOLIA — fondation du serveur central Cloudflare (pilote technique)

**Version source :** SCOLIA Pro 1.2.86.
**Environnement de test existant :** Worker `scolia-api`, D1 `scolia-test` (binding `DB`), bucket R2 privé `scolia-files` (binding `FILES`), `https://api.jp-services.org`.

## Ce qui est opérationnel et ce qui ne l'est pas

Le test `/health` du Worker a déjà confirmé des connexions à D1 et R2, **mais il ne valide ni connexion de compte, ni paiement, ni synchronisation scolaire**.

Les migrations `0001_core.sql` et `0002_invitations.sql` préparent la **structure centrale minimale** nécessaire à un futur service métier :
- écoles séparées ;
- utilisateurs et rôles liés à leur école ;
- appareils liés au compte utilisateur autorisé par un responsable, avec auteur de révocation tracé ;
- invitations de 10 minutes liées au compte cible, dont seul le SHA-256 du code QR est conservé ;
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
- `GET /v1/admin/devices?after=...&limit=50` : liste paginée (1 à 100 éléments par page, curseur stable par identifiant) des appareils du **seul établissement authentifié**, avec métadonnées d'approbation/révocation et aucun secret. La réponse contient `devices`, `next`, `has_more`.
- `POST /v1/admin/devices/revoke` : corps `{"device_id":"identifiant"}`, réservé au secrétariat/direction. Marque l'appareil comme révoqué et conserve `revoked_by` dans le même UPDATE D1. Une deuxième révocation retourne `already_revoked:true`; une tentative sur l'appareil de la session active est bloquée afin d'éviter un verrouillage accidentel du poste administratif.
- `POST /v1/admin/devices/invite` avec `{"target_user_id":"identifiant"}` : génère 256 bits aléatoires valables 10 minutes pour un compte actif du même établissement, et ne conserve que leur empreinte SHA-256 dans D1. Le code en clair est retourné **une seule fois**, dans une réponse HTTPS `no-store`. Au maximum **3 invitations non expirées et non consommées** par compte ; le dépassement répond HTTP 429 sans code d'invitation.
- `GET /v1/sync/operations/{operation_id}` : retourne pour le seul compte et appareil émetteurs le statut `pending`, `applied` ou `rejected` ; aucun montant, nom d'apprenant ni détail financier.
- Une révocation est appliquée à la **prochaine requête réseau** du téléphone : elle n'est pas instantanée si le téléphone est hors ligne. La politique locale de durée de validité reste à définir et à tester.
- `POST /v1/identity/pairing-proof` : vérifie l'identifiant et le **mot de passe réel** du compte invité contre son empreinte ASP.NET Identity V3 PBKDF2-HMAC-SHA512 (`0x01`, 100 000 itérations), sans jamais renvoyer l'empreinte ou le mot de passe. Une preuve JWT signée côté Worker est délivrée pour **120 secondes**, seulement pour le jumelage ; elle ne donne pas accès aux API scolaires.
- Le compteur D1 `cloud_auth_attempts` est rattaché à **l'établissement et au compte**, pas au QR : cinq tentatives de mot de passe déclenchent deux minutes de verrouillage ; l'incrément se fait atomiquement avant le calcul PBKDF2 pour limiter les tentatives parallèles. Un mot de passe correct réinitialise le compteur, si le compte n'est pas en situation de verrouillage.
- Deux bindings Cloudflare sont **obligatoires** pour ouvrir le point d'accès : `SCOLIA_LOGIN_RATE_LIMITER` (par invitation, 8/60 s) et `SCOLIA_LOGIN_SOURCE_LIMITER` (par provenance, seuil plus large 120/60 s). Le service retourne 503 si l'un manque. Les limites Workers sont approximatives et ne remplacent ni le verrouillage D1 ni une protection WAF. 
- Le trigger D1 `cloud_auth_security_revision` augmente `permission_revision` quand le hash du mot de passe, l'activation du compte, son rôle ou l'obligation de changer son mot de passe changent : les anciennes preuves signées perdent leur validité au jumelage.
- `0003_password_identity.sql` ajoute `cloud_users.password_hash` (empreinte salée uniquement) et `must_change_password` (par défaut 1). Les empreintes doivent être importées **uniquement via une migration contrôlée des comptes existants**, sans exporter de mots de passe en clair et sans commit dans GitHub. La modification locale d'un mot de passe doit augmenter `permission_revision` dans le cloud afin de rendre les preuves précédentes invalides.
- `POST /v1/pair/accept` : prototype de consommation en une seule opération D1. Il exige simultanément une invitation QR non expirée et non utilisée, une **preuve JWT d'authentification du compte cible** émise par un service d'identité distinct et une **signature ECDSA P-256** produite par la clé privée du téléphone. Le JWT de jumelage (durée maximale 5 minutes) est lié à l'établissement, au compte, à l'invitation, à l'identifiant du téléphone, à la révision de ses permissions et à l'empreinte de sa clé publique. Le QR seul ne permet jamais de créer un appareil.
- `0002_invitations.sql` comporte un déclencheur SQLite : une seule UPDATE conditionnelle consomme le QR et insère l'appareil avec sa clé publique dans la **même transaction**, afin qu'une erreur ou réutilisation ne laisse pas un appareil à moitié approuvé.
- **Blocage avant déploiement** : le service qui authentifie réellement les utilisateurs et émet la preuve `scolia-identity` n'existe pas encore. Le secret distinct `SCOLIA_PAIRING_AUTH_KEY` doit être géré uniquement côté serveur. L'activation n'émet **aucune session** et ne permet pas de se connecter à l'application. Le renouvellement des jetons et la preuve d'appareil lors de chaque nouvelle session restent à développer.
- Les routes de lecture actuelles ne sont accessibles qu'aux rôles de direction et secrétariat. Les autorisations spécifiques des enseignants, de la comptabilité et des étudiants seront construites avant l'accès aux contenus correspondants.
- Les paramètres secrets **distincts** `SCOLIA_AUTH_HMAC_KEY` (sessions existantes du prototype) et `SCOLIA_PAIRING_AUTH_KEY` (vérification/émission des preuves temporaires de jumelage) doivent chacun être générés aléatoirement et conservés exclusivement comme **secrets Workers**, jamais dans le dépôt, les sauvegardes publiques ou les APK. Leur valeur n'est pas définie dans le dépôt.
- La vérification SCOLIA actuelle utilise **100 000 itérations PBKDF2-HMAC-SHA512** ; le vérificateur limite les paramètres acceptés à un intervalle contrôlé. **Workers Free ne dispose que de 10 ms de CPU par requête selon la documentation Cloudflare**. Le runtime fournit PBKDF2 mais les tests Node ne prouvent pas qu'une vérification passera ce quota. Mesurer le CPU réel sur un Worker de préproduction avant d'utiliser le service ; ne pas réduire le coût cryptographique pour contourner la limite. Si le quota est insuffisant, déplacer la vérification vers un service d'identité disposant des ressources adaptées.
- Le premier couplage exige **Internet**. Une fois les sessions et appareils approuvés, les applications doivent rester entièrement utilisables hors ligne sur les données locales préchargées ; la validation immédiate d'une révocation sur un appareil sans réseau est impossible.
- Un compte dont `must_change_password=1` doit d'abord modifier son mot de passe dans le flux autorisé du serveur existant ; aucune preuve de jumelage n'est émise. Les anciennes empreintes Identity V2/SHA1 et les empreintes V3 non compatibles ne sont **pas acceptées** par cette route et nécessitent une remise à niveau approuvée.
- Le secret `SCOLIA_AUTH_HMAC_KEY` (clé aléatoire base64url de 32 octets minimum) doit être défini exclusivement dans les paramètres protégés du Worker **après** création d'un service d'identité approuvé. Jamais dans le dépôt, une APK ou les fichiers de configuration Android.
- **Aucun service d'émission ou de renouvellement des jetons n'existe encore**. Le prototype ne peut pas authentifier les utilisateurs réels à ce stade.

Depuis `scolia-pro/deploy/cloudflare-central`, exécuter :

```sh
npm test
```

Le workflow `SCOLIA Cloudflare Central Preview - securite API` vérifie ces routes, notamment les jetons invalides, comptes désactivés, appareils révoqués, paramètres malformés, isolation des établissements et ACK bornés.

**Ne pas déployer ce prototype sur le domaine de production.** Les routes d'identité ont besoin d'un Worker de staging, d'une base D1 de test isolée, de secrets Workers et d'une procédure de migration des comptes réels avant toute utilisation. Les contrôles de mot de passe et le compteur doivent aussi être protégés par une limitation des requêtes à la périphérie (WAF/rate limits), pas seulement par D1. Il n'est relié ni aux API métier actuelles, ni aux tables de dossiers scolaires, ni à la gestion des comptes. Le `/health` de ce prototype ne teste pas réellement D1/R2, contrairement au `/health` déjà configuré manuellement dans le Worker de l'établissement.

## Préconfiguration du vérificateur de mots de passe (STAGING uniquement)

Le modèle prêt à compléter se trouve dans `wrangler.staging.toml.example`. Il utilise une **nouvelle base D1** pour la préproduction, les deux rate-limiters et aucun domaine de production.

1. Créer une base D1 **vide et distincte** (sans donnée scolaire réelle). Compléter son identifiant dans une copie privée du modèle Wrangler, ainsi que deux `namespace_id` numériques distincts et propres au compte Cloudflare.
2. Appliquer, **une seule fois et dans l'ordre**, les migrations `0001_core.sql`, `0002_invitations.sql` et `0003_password_identity.sql`. La troisième ajoute deux colonnes via `ALTER TABLE` : ne pas réexécuter le fichier déjà appliqué. Sauvegarder/restaurer avant toute migration future.
3. Créer séparément les secrets Cloudflare `SCOLIA_PAIRING_AUTH_KEY` et `SCOLIA_AUTH_HMAC_KEY`, chacun avec au moins 32 octets aléatoires. **Ne jamais** les mettre dans un fichier GitHub, un e-mail, une capture ou une APK. Vérifier que les deux clés sont différentes.
4. Ne provisionner que des **comptes de test** et des empreintes IdentityV3 de test. Les hachages des comptes réels ne doivent être copiés qu'après validation de la migration, du chiffrement au repos, des autorisations institutionnelles et de la révocation des sessions.
5. Lancer `npm test` dans ce dossier et les tests SQLite `python3 -m unittest discover -s scolia-pro/tests -p 'test_cloudflare_central_schema.py' -v` depuis la racine du dépôt. Effectuer ensuite un essai **sur le runtime Cloudflare en préproduction**, notamment pour le coût CPU de PBKDF2 100 000.
6. **Ne pas substituer ce prototype à `api.jp-services.org`**. Les sessions Android/Windows, le renouvellement, les permissions fines et la synchronisation métier restent distincts.

Une preuve de jumelage n'est délivrée qu'après validation du vrai hash connu du compte ; une invitation QR isolée ou un compte sans empreinte migrée ne peut jamais produire une session.

## Portes de validation avant activation de l'API

1. Inventorier **toutes** les données métier réelles du modèle EF Core 1.2.86 et définir une migration complète vers D1.
2. Compléter le service d'identité et les sessions natives : émission sûre de la preuve `scolia-identity` **uniquement après vérification du vrai mot de passe du compte cible** ; renouvellement des JWT, protection des clés privées dans Android Keystore/Windows, et contrôle des appareils approuvés. Le prototype gère émission, consommation cryptographique du QR, liste et révocation, mais pas l'authentification réelle ni la migration des comptes.
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

## Déploiement staging manuel et protégé (préparé, NON exécuté)

Ce dépôt ne fournit pas de droits Cloudflare. Il ne faut **pas** réutiliser le
Worker `scolia-api`, la D1 `scolia-test`, le domaine
`api.jp-services.org` ni un bucket de production pour ces essais.

1. Dans Cloudflare, créer **une nouvelle base D1** appelée
   `scolia-identity-staging`. Copier son UUID, sans importer d'apprenants,
   d'enseignants ou de vrais mots de passe.
2. Choisir deux identifiants numériques **positifs et distincts** de
   Rate Limiting Workers pour le compte Cloudflare. Ils ne doivent pas
   partager le même état de limite entre les deux bindings.
3. Dans GitHub, préparer un environnement de dépôt nommé
   `scolia-staging` avec validation manuelle recommandée, et y enregistrer
   comme **variables** `SCOLIA_STAGING_D1_ID`,
   `SCOLIA_STAGING_RL_INVITE_NS` et
   `SCOLIA_STAGING_RL_SOURCE_NS`. En **secrets d'environnement** :
   `CLOUDFLARE_ACCOUNT_ID` et `CLOUDFLARE_API_TOKEN` (token limité
   à l'édition du Worker staging et de cette D1 uniquement).
4. Le workflow manuel
   `.github/workflows/scolia-identity-staging-manual.yml` exige une
   confirmation exacte `STAGING_ONLY`. Il exécute les tests JS/Python,
   valide l'UUID et les deux IDs distincts, écrit un
   `wrangler.staging.toml` **ignoré par Git**, puis applique les
   migrations à la D1 staging et déploie uniquement le Worker
   `scolia-identity-staging` sur son sous-domaine `workers.dev`.
   **Il ne faut pas lancer ce workflow avant la revue des variables et
   des permissions Cloudflare.**
5. Configurer dans les secrets du Worker staging deux clés
   aléatoires indépendantes de 32 octets minimum sous la forme base64url :
   `SCOLIA_PAIRING_AUTH_KEY` et `SCOLIA_AUTH_HMAC_KEY`.
   Ne jamais les transmettre dans le chat, les mettre dans GitHub,
   les entrer dans un fichier `wrangler.toml` ou les intégrer aux APK.
6. N'utiliser que des comptes et codes de test fictifs. Vérifier en staging
   le temps CPU réel du PBKDF2, la réponse HTTP 429, les cinq tentatives,
   le blocage D1 de deux minutes, les QR à usage unique, le changement de
   mot de passe et la révocation. **Ne pas basculer de vrais comptes**
   si le quota CPU ou le parcours métier n'est pas validé.

Le modèle de configuration `wrangler.staging.toml.example` contient
intentionnellement des espaces réservés invalides : il **ne peut pas**
être déployé tel quel. Le générateur exige des valeurs reçues de
l'environnement GitHub. Le script ne reçoit ni ne stocke les clés de
signature JWT.

Même après un déploiement staging, l'API scolaire centrale ne fonctionne
pas encore : aucun import de données réelles, aucune émission de session
scolaire définitive, aucune synchronisation cloud métier des paiements
ou des notes n'est prête.
