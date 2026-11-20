# SCOLIA Pro — Console propriétaire JP‑Services

## Principe
Service commercial séparé du serveur de chaque établissement. Il **ne reçoit jamais** les dossiers d'apprenants, le contenu des écritures comptables, les comptes internes ou les justificatifs. Il enregistre uniquement : nom de l'établissement, pseudonyme d'installation, nom de l'ordinateur serveur, IP de connexion (maximum 30 jours), jour d'activité, statut d'abonnement et durée des droits.

Le responsable est informé de cette finalité lors de la **première inscription volontaire**, par invitation à usage unique. Le Wi-Fi scolaire et l'accès distant sont distincts du suivi des licences ; une coupure Internet ne transmet rien jusqu'au rétablissement. Les événements commerciaux du serveur sont mis dans une file locale chiffrée, renvoyés à la connexion et dédupliqués par identifiant.

## Continuité en ligne (0.2.0) : sauvegarde et relève

Activée **une fois** par le responsable de l'école sur son ordinateur principal (Données › Sauvegarde en ligne), elle tourne ensuite **sans intervention** :

- **Sauvegarde automatique** : toutes les heures si des données ont changé, et au moins une fois par jour, le serveur scolaire fait une copie cohérente de sa base, la compresse, la **chiffre avec la clé de l'école** (AES-256-GCM) et l'envoie ici (`PUT /api/v1/server/cloud/backups`, empreinte SHA-256 vérifiée à la réception). Les fichiers vont dans le bucket R2 privé `scolia-pro-backups` ; D1 ne garde que la date, la taille et les empreintes. Rétention : 24 copies sur 48 h, une par jour sur 30 jours, une par mois sur 12 mois, 1 Gio maximum par école.
- **Relève** : quand l'ordinateur principal est injoignable, chaque téléphone ou poste continue de travailler et dépose ses opérations, chiffrées pour le serveur, dans la boîte de l'école (`POST /api/v1/device/mailbox`, jeton de dépôt : dépôt seulement, aucune lecture). Toutes les 2 minutes, le serveur relève la boîte, applique chaque opération **avec ses propres règles et les droits de son auteur** (délégation signée par le serveur, 30 jours), puis publie l'issue (`applied` / `refused`, gardée 180 jours). L'appareil l'apprend et ne renvoie pas l'opération ; s'il la renvoie quand même, la clé d'idempotence empêche tout doublon.
- **Reprise après sinistre** : la console crée une **invitation de reprise** pour un nouvel ordinateur ; son usage révoque l'ancien serveur (jamais deux serveurs principaux). Le nouveau serveur liste les copies et les déchiffre avec le **code de secours** de l'école (14 groupes de 4 caractères), seul moyen de lire une copie ailleurs.
- **Ce que voit JP-Services** : par école, état (active, en retard > 26 h), date et taille des copies, présence du serveur, nombre d'opérations en attente. Jamais leur contenu, ni la clé, ni un lien de téléchargement.

Limites du plan gratuit Cloudflare à surveiller : R2 10 Go, D1 5 Go et 100 000 lignes écrites par jour, Workers 100 000 requêtes par jour et 100 Mo par requête (une copie compressée de plus de 90 Mo est refusée et signalée sur le serveur).

### Tout se règle ensuite dans la console

Panneau **« Sauvegarde en ligne des écoles — stockage Cloudflare R2 »** :

1. **Stockage** : deux possibilités, détectées automatiquement.
   - Liaison R2 créée par le workflow de déploiement (si le jeton GitHub `CLOUDFLARE_API_TOKEN` a « Workers R2 Storage : Edit ») : rien à saisir.
   - Sinon, **champs de la console** : identifiant du compte Cloudflare, nom du bucket, Access Key ID et Secret Access Key d'un **jeton API R2** (Cloudflare › R2 › Gérer les jetons API › « Object Read & Write » limité au bucket). « Tester et enregistrer » écrit, relit et supprime un fichier témoin ; rien n'est gardé si le test échoue. La clé secrète est chiffrée dans D1 (AES-256-GCM, clé dérivée du jeton propriétaire) et n'est jamais réaffichée ; après un changement du jeton propriétaire, ressaisissez-la.
2. **Activer les écoles** : « Activer la sauvegarde en ligne pour toutes les écoles », ou école par école dans sa fiche (« Activer » / « Retirer »). Au contrôle de licence suivant (15 minutes au plus), le serveur de l'école active seul la sauvegarde avec **sa propre clé**, envoie une première copie, puis continue sans intervention. Son responsable est invité, à chaque connexion, à noter le code de secours. Un arrêt décidé par l'école n'est jamais annulé depuis la console.

Seule la **première mise en ligne de la console** passe par GitHub (secrets de l'environnement `scolia-owner-production`), car la console n'existe pas encore pour les recevoir.

## Gestion des licences
Tableau de bord dans la console JP‑Services :
- Facturation **désactivée par défaut** : aucune école existante ne peut être verrouillée lors de l'installation.
- Activation de la facturation par établissement, période d'essai (0–365 jours calculée à partir de la date d'inscription), échéance de paiement, jours restants et date de renouvellement.
- Lecture seule **automatique** lorsque les droits payants, l'essai et la période de grâce sont échus.
- Dérogation manuelle limitée dans le temps (120 jours au maximum), avec motif, renouvellement, suppression de dérogation et **lecture seule manuelle**. En ligne, les restrictions s'appliquent au prochain contrôle du serveur (habituellement dans les 15 minutes), et non instantanément sur les postes déconnectés.
- La décision commerciale est sauvegardée dans Cloudflare D1. Le serveur scolaire consulte cette décision par HTTPS avec son propre jeton protégé, puis refuse toutes les mutations via le middleware API (HTTP 423). Les sessions et les lectures restent ouvertes. Les écritures locales non encore envoyées sont conservées sans faux conflit.
- Le serveur conserve la dernière décision pour une interruption réseau (jusqu'à 7 jours de grâce pour un droit antérieurement valide), sans permettre de contourner un refus explicite déjà reçu.
- Le badge de licence et les jours restants sont visibles dans les paramètres et dans la barre d'état de SCOLIA.
- La console affiche le statut calculé et le nombre réellement verrouillé d'établissements. Elle expose un journal limité aux interventions commerciales, sans noms d'apprenants.
- Les inscriptions et les événements d'activité sont transactionnels et idempotents dans D1 ; un code d'invitation n'est consommé qu'une fois le compte créé.
- La désactivation d'un relais inactif ne modifie pas le relais actif. Toute bascule réelle change la révision et exige une nouvelle approbation du serveur scolaire.

**Cette logique doit passer des tests intégrés avant d'être distribuée à des établissements.** Une restriction commerciale ne doit jamais supprimer leurs données ni empêcher l'export et l'accès aux archives.

## Configuration et sécurité des relais
La console gère les adresses de fournisseurs WSS (jamais les clés dans le dépôt). Les établissements voient uniquement Activer/Désactiver l'accès Internet. Un changement de fournisseur requiert l'approbation du responsable scolaire, avec révision. La liaison Wi-Fi locale demeure utilisable, et la licence est suivie indépendamment de ce bouton.

## Première mise en ligne sans domaine payant — workers.dev (pilote)

L'application Cloudflare Zero Trust `licence` peut protéger les deux chemins
`/console` et `/api/v1/owner/*` du Worker
`https://scolia-pro-owner.jeanpaulmpolo242.workers.dev`, sans bloquer les
API publiques (`/health`, `/api/v1/notice`, `/api/v1/register`,
`/api/v1/heartbeat`) ni les API scolaires authentifiées.

Le workflow de publication prend maintenant **par défaut** cette adresse et
l'**AUD Cloudflare Access transmis par le propriétaire** (identifiant d'application
non secret). Les variables facultatives de l'environnement GitHub
`SCOLIA_OWNER_DOMAIN` et `SCOLIA_OWNER_ACCESS_AUD` peuvent remplacer ces valeurs.
Aucune variable supplémentaire n'est nécessaire pour choisir l'adresse gratuite.

L'environnement GitHub `scolia-owner-production` doit toujours disposer de trois
**secrets** : `CLOUDFLARE_API_TOKEN`, `CLOUDFLARE_ACCOUNT_ID` et
`SCOLIA_OWNER_ADMIN_TOKEN` (minimum 32 caractères). Ne jamais copier ces secrets
dans Git, une issue ou une discussion. Le jeton API doit disposer des permissions
Workers, D1 et R2 nécessaires. La configuration de la base D1 et du bucket R2
reste automatisée au premier lancement du workflow manuel.

1. Vérifier dans Zero Trust la présence de l'application Access avec **la même
   audience**, uniquement les chemins `/console` et `/api/v1/owner/*`,
   la politique e-mail administrateur et la MFA réellement appliquée.
2. Vérifier les trois secrets de l'environnement GitHub et limiter la branche
   autorisée à `release/direct-apk-scolia-pro-20261103`.
3. Lancer manuellement « SCOLIA Pro - publier console JP-Services (production) »
   depuis cette branche en cochant `confirm_production`.
4. Vérifier que `/health` et `/api/v1/notice` répondent en HTTPS sans Access,
   tandis que `/console` demande une connexion Cloudflare, puis le jeton
   propriétaire. Tester aussi que les routes propriétaire ne sont jamais ouvertes
   anonymement. Ne déduire **aucune mise en ligne** de la seule présence du code.
5. Une fois le test accepté, configurer l'URL HTTPS du service de licences sur les
   serveurs SCOLIA concernés ; ne jamais intégrer le jeton propriétaire dans l'APK.

Cloudflare considère `workers.dev` comme un **environnement de démarrage et de
test**, pas une recommandation pour une exploitation commerciale critique.
Préférer un domaine personnalisé ultérieurement. Le workflow conserve ce mode
en définissant la variable `SCOLIA_OWNER_DOMAIN` à un domaine Cloudflare réel.

## Déploiement de production sécurisé

La console ne doit pas être mise en ligne avec le `wrangler.jsonc` de développement : son identifiant D1 est volontairement fictif. Le déploiement sécurisé (domaine gratuit ou personnalisé) se fait uniquement par GitHub Actions :

- Workflow : **SCOLIA Pro - publier console JP-Services (production)**.
- Branche autorisée : `release/direct-apk-scolia-pro-20261103`, déclenchement manuel par le propriétaire du dépôt.
- Environnement GitHub : `scolia-owner-production`. Configurer l'approbation d'un responsable, la limitation aux branches de distribution et les secrets suivants.

| GitHub, environnement `scolia-owner-production` | Utilité |
| --- | --- |
| Secret `CLOUDFLARE_API_TOKEN` | API Cloudflare (Workers Edit, D1 Read/Write, routes/domaines nécessaires). Jamais dans Git. |
| Secret `CLOUDFLARE_ACCOUNT_ID` | Identifiant de compte Cloudflare (32 caractères hexadécimaux). |
| Secret `SCOLIA_OWNER_ADMIN_TOKEN` | Jeton propriétaire aléatoire de 32 caractères minimum. Ne jamais le partager dans la conversation. |
| Variable facultative `SCOLIA_OWNER_DOMAIN` | Adresse du domaine personnalisé ; valeur par défaut : `scolia-pro-owner.jeanpaulmpolo242.workers.dev`. |
| Variable facultative `SCOLIA_OWNER_ACCESS_AUD` | Audience (AUD) de Cloudflare Access, valeur initiale déjà configurée dans le workflow. |

### Cloudflare Access : première configuration obligatoire

1. Dans Cloudflare Zero Trust, créer **une application Access** (avec la même audience AUD) protégeant uniquement les deux chemins `https://VOTRE_DOMAINE/console` et `https://VOTRE_DOMAINE/api/v1/owner/*`. Autoriser uniquement les administrateurs JP-Services nominativement, activer la MFA et les protections anti-force-brute.
2. **Ne jamais protéger tout le domaine par Access**, car les serveurs scolaires doivent communiquer par HTTPS avec `POST /api/v1/register`, `POST /api/v1/heartbeat` et `/api/v1/server/relay-*`, eux-mêmes protégés par invitation/jeton propres à chaque école.
3. Copier le vrai AUD dans `SCOLIA_OWNER_ACCESS_AUD`. La console vérifie `ctx.access.aud` **côté Worker**, en complément du jeton propriétaire. Si Access n'est pas configuré, la page et les routes propriétaire restent **fermées** (503/403).
4. Confirmer l’appartenance du domaine Cloudflare ; le workflow utilise `workers.dev` pour le pilote, ou un domaine personnalisé HTTPS si `SCOLIA_OWNER_DOMAIN` est défini.

### Publication reproductible

Après configuration, lancer manuellement le workflow et cocher `confirm_production`. Il exécute ces étapes dans cet ordre :

1. Vérifier secrets, domaine, AUD, identité du déclencheur et approbation de l'environnement GitHub.
2. Exécuter les tests d'intégration, dont l'absence d'accès propriétaire sans Cloudflare Access.
3. Rechercher la base D1 `scolia-pro-owner` ; la **réutiliser** si elle existe ou la créer si elle est absente. Ne supprimer ni écraser aucune base existante.
4. Générer temporairement `wrangler.production.json` (**ignoré par Git**) avec son vrai UUID, l’adresse HTTPS, le bon routage et l’AUD.
5. Appliquer uniquement les migrations D1 manquantes (sauvegarde automatique par Cloudflare), déployer le Worker et provisionner le jeton propriétaire par secret chiffré.
6. Contrôler `/health`, `/api/v1/notice` et l'interdiction des API propriétaire sans Access. Une erreur arrête le pipeline.

### Utilisation, supervision et retour arrière

- Accéder à `https://VOTRE_DOMAINE/console` uniquement après la connexion Cloudflare Access, puis saisir le jeton JP-Services. Utiliser **Verrouiller la console** à la sortie.
- Les changements commerciaux concernent le **serveur** de chaque établissement et sont récupérés au prochain contrôle HTTPS (habituellement sous 15 minutes). Ne pas annoncer un blocage instantané sur les appareils hors ligne.
- Suivre les erreurs Cloudflare Workers, l'activité D1, les échecs de connexion et les changements de licence depuis le journal. Avant toute migration non additive, réaliser une sauvegarde D1 et un test sur une base de préproduction.
- En cas d'incident, revenir à la version Worker précédente et restaurer la configuration antérieure ; ne pas restaurer aveuglément une ancienne base sans examiner les écritures postérieures.
- Si le domaine public n'a pas encore été communiqué au serveur SCOLIA, `Scolia:Licence:ServiceUrl` reste absent et aucune licence automatique ne démarre. Configurer cette URL HTTPS dans la distribution officielle **après** validation de la console, sans jamais intégrer le jeton propriétaire dans l'APK.
- La console ne traite pas les paiements en ligne : l'administrateur confirme manuellement un renouvellement. Ne pas représenter ce mécanisme comme un encaissement certifié automatique.

**État :** ce dépôt fournit le code et le pipeline de déploiement, mais ne prouve pas qu'une infrastructure Cloudflare de production ou ses secrets existent. La console n'est publique qu'après réussite du workflow et contrôle de l'URL réelle.

## API
- `GET /health`, `GET /api/v1/notice` : publics, description des informations et du consentement.
- `POST /api/v1/register` : code d'invitation, identité du serveur et accord explicite ; retourne un secret au serveur une seule fois.
- `POST /api/v1/heartbeat` : événement idempotent (serveur seulement), IP serveur et statut de licence, jamais de données scolaires.
- `GET /api/v1/owner/summary`, `/schools`, `/schools/{id}` : synthèse et activité.
- `PATCH /api/v1/owner/schools/{id}/license` : essai, abonnement, dérogation et verrouillage.
- `GET|POST /api/v1/owner/relays` et `PATCH /api/v1/owner/relays/{id}` : configuration des fournisseurs sécurisés.
- `POST /api/v1/owner/schools/{id}/recovery-invitation` : invitation de reprise (nouveau serveur, 3 jours, usage unique).
- `GET|PUT|DELETE /api/v1/owner/settings/storage` : stockage R2 (état, test et enregistrement chiffré des identifiants).
- `PATCH /api/v1/owner/schools/{id}/cloud`, `POST /api/v1/owner/cloud/request-all` : sauvegarde en ligne proposée à une école ou à toutes.
- `POST /api/v1/server/cloud/enable`, `PUT|GET /api/v1/server/cloud/backups[/{id}]`, `GET /api/v1/server/cloud/mailbox`, `POST /api/v1/server/cloud/mailbox/ack`, `POST /api/v1/server/cloud/deposit-token` : continuité, jeton du serveur scolaire.
- `POST /api/v1/device/mailbox`, `POST /api/v1/device/mailbox/outcomes`, `GET /api/v1/device/status` : dépôt par les appareils, jeton de dépôt.
- `GET /api/v1/server/relay-settings`, `POST /api/v1/server/relay-consent`, `POST /api/v1/server/relay-approve` : décisions du serveur scolaire, protégé par son jeton.

## Restrictions
Aucune surveillance cachée des appareils ni récupération de leurs fichiers. Les mises à jour GitHub Actions vérifient les sources, mais **ne contiennent pas de secrets de licence ou de relais**. Les changements de fournisseur peuvent être publiés dans la console sans reconstruire l'APK, sous réserve d'acceptation de l'établissement.


## Découverte automatique des relais Cloudflare Realtime TURN

La console utilise **deux catalogues distincts**.

- **WSS / signalisation :** les relais déjà configurés dans Cloudflare D1 apparaissent automatiquement dans l'interface. L'administrateur peut ajouter un secours manuel `wss://`. Toute activation nouvelle demande l'approbation de l'établissement.
- **TURN / STUN WebRTC :** la page `Relais Internet` peut interroger en lecture seule l'API officielle Cloudflare Realtime TURN (`GET /accounts/{account_id}/calls/turn_keys`) et récupérer automatiquement **les noms** des relais TURN du compte. Les noms sont disponibles depuis `GET /api/v1/owner/relays/cloudflare` uniquement après Cloudflare Access + jeton propriétaire. Le moteur de l'école ne reçoit aucune nouvelle configuration sans approbation.

### Configuration unique, sans afficher de secrets

1. Dans Cloudflare, créer un jeton API dédié à **Calls Read** limité au compte JP-Services (pas de droits Write nécessaires).
2. Ajouter sa valeur comme secret d'environnement GitHub `SCOLIA_TURN_DISCOVERY_TOKEN` dans `scolia-owner-production` ; ne jamais la mettre en clair dans les fichiers ou la conversation.
3. Le workflow protégé de publication utilise le secret facultatif pour installer `CLOUDFLARE_TURN_DISCOVERY_TOKEN` dans le Worker. L'Account ID GitHub existant est utilisé pour la variable Worker `CLOUDFLARE_TURN_ACCOUNT_ID`.
4. Si le jeton est absent ou l'API indisponible, la console affiche « configuration requise » ou « service indisponible », **sans casser le catalogue WSS manuel**.

Important : récupérer une clé TURN (nom et UID) ne fournit **pas** les identifiants temporaires de connexion. Cloudflare exige un appel serveur séparé pour les générer à durée limitée. TURN/TURNS (`turns:turn.cloudflare.com:5349`) et relais de signalisation WSS (`wss://...`) ne sont pas interchangeables. La découverte de noms **n'active ni ne facture aucun nouveau relais**. Voir la documentation officielle Cloudflare Realtime TURN pour les conditions de service et de facturation.

### Vérifications

`npm test` vérifie le catalogue, les données non exposées, la protection propriétaire, la confidentialité des tokens, les erreurs de permission et l'indépendance du secours WSS.
