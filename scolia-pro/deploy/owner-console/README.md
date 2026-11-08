# SCOLIA Pro — Console propriétaire JP‑Services

## Principe
Service commercial séparé du serveur de chaque établissement. Il **ne reçoit jamais** les dossiers d'apprenants, le contenu des écritures comptables, les comptes internes ou les justificatifs. Il enregistre uniquement : nom de l'établissement, pseudonyme d'installation, nom de l'ordinateur serveur, IP de connexion (maximum 30 jours), jour d'activité, statut d'abonnement et durée des droits.

Le responsable est informé de cette finalité lors de la **première inscription volontaire**, par invitation à usage unique. Le Wi-Fi scolaire et l'accès distant sont distincts du suivi des licences ; une coupure Internet ne transmet rien jusqu'au rétablissement. Les événements commerciaux du serveur sont mis dans une file locale chiffrée, renvoyés à la connexion et dédupliqués par identifiant.

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

## Déploiement (actions explicites de JP‑Services)
1. Dans `scolia-pro/deploy/owner-console`, exécuter `npm test`. Les tests d'intégration emploient SQLite embarqué sous Node.js 22 (sans base distante).
2. Créer Cloudflare D1 `scolia-pro-owner` et copier **son identifiant réel** dans `wrangler.jsonc` (placeholder actuel non déployable).
3. `npx wrangler d1 migrations apply scolia-pro-owner --remote`.
4. Stocker le secret administrateur avec `npx wrangler secret put OWNER_ADMIN_TOKEN`, jamais dans Git.
5. Configurer Cloudflare Access + MFA, règles anti-bruteforce et un domaine HTTPS de la console.
6. `npm run deploy`. Configurer dans l'installation de l'API scolaire `Scolia:Licence:ServiceUrl` avec l'URL HTTPS **publique du service** ; aucun secret propriétaire dans l'APK.
7. Enregistrer et consentir depuis Paramètres → Licence sur le seul serveur de l'établissement. Les clients liés héritent automatiquement de l'état du serveur.

La console ne sera **pas opérationnelle** tant que D1, l'URL du service et les secrets n'auront pas été configurés, les flux de connexion validés et la distribution officielle signée.

**Contrôle avant mise en production :** l'identifiant D1 dans `wrangler.jsonc` est volontairement un placeholder. Ne jamais publier la console directement avec ce fichier inchangé ; créer la base, configurer Cloudflare Access + MFA, protéger `OWNER_ADMIN_TOKEN` et vérifier l'URL HTTPS réelle. L'ancien endpoint `PATCH /owner/schools/{id}` renvoie 410 pour éviter une confirmation de suspension trompeuse : utiliser uniquement `PATCH /owner/schools/{id}/license`.

## API
- `GET /health`, `GET /api/v1/notice` : publics, description des informations et du consentement.
- `POST /api/v1/register` : code d'invitation, identité du serveur et accord explicite ; retourne un secret au serveur une seule fois.
- `POST /api/v1/heartbeat` : événement idempotent (serveur seulement), IP serveur et statut de licence, jamais de données scolaires.
- `GET /api/v1/owner/summary`, `/schools`, `/schools/{id}` : synthèse et activité.
- `PATCH /api/v1/owner/schools/{id}/license` : essai, abonnement, dérogation et verrouillage.
- `GET|POST /api/v1/owner/relays` et `PATCH /api/v1/owner/relays/{id}` : configuration des fournisseurs sécurisés.
- `GET /api/v1/server/relay-settings`, `POST /api/v1/server/relay-consent`, `POST /api/v1/server/relay-approve` : décisions du serveur scolaire, protégé par son jeton.

## Restrictions
Aucune surveillance cachée des appareils ni récupération de leurs fichiers. Les mises à jour GitHub Actions vérifient les sources, mais **ne contiennent pas de secrets de licence ou de relais**. Les changements de fournisseur peuvent être publiés dans la console sans reconstruire l'APK, sous réserve d'acceptation de l'établissement.
