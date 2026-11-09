# SCOLIA Pro — base D1 scolaire (pilote gratuit, opt-in)

## Périmètre exact

Le PC serveur garde SQLite et reste l'autorité métier. Le service HTTPS
`scolia-pro-owner` conserve sa base commerciale D1 `OWNER_DB` (licences,
appareils, métadonnées) et ses sauvegardes R2. Une **seconde base indépendante**
`scolia-pro-school-sync` (`SCHOOL_DB`) conserve seulement des **trames
chiffrées par l'école**, un curseur et l'empreinte de jumelage du serveur.
Aucun identifiant ou mot de passe scolaire en clair n'y est enregistré.

Ce pilote n'est **pas** une réplication bidirectionnelle automatique de
SQLite vers D1, et **pas** un serveur capable de valider des paiements pendant
que le PC est éteint. Des adaptateurs métier, une migration de schéma et une
gestion des conflits sont nécessaires pour cela.

### Coût du forfait Cloudflare Workers Free (vérifié novembre 2026)
- Gratuit sans nécessité de carte bancaire ; maximum 10 bases D1.
- Maximum **500 Mo par base**, 5 Go au total.
- 5 millions de lignes lues par jour ; 100 000 lignes écrites par jour.
- Après dépassement du forfait gratuit, des opérations sont refusées.
  **Il n'y a pas de bascule ni de facturation automatique** vers Workers Paid.
  L'utilisateur doit décider séparément de l'éventuelle mise à niveau.
- Le budget est commun aux services de ce compte ; les licences et le relais
  consomment déjà certaines ressources.

### Provisionnement de la base D1 scolaire

Il est préparé dans `scripts/prepare-production.mjs` : lors d'un lancement
**manuel autorisé** du workflow `.github/workflows/scolia-owner-production.yml`
depuis la branche de publication, il récupère l'identifiant de la D1 commerciale,
cherche la D1 `scolia-pro-school-sync`, la crée **uniquement si absente**
et génère une configuration Wrangler temporaire non committée.
Les migrations `migrations/` s'appliquent **uniquement** à `OWNER_DB`;
les migrations `school-migrations/` s'appliquent **uniquement** à `SCHOOL_DB`.

Pré-requis :
1. Workflow revu et fusionné sur la branche de publication protégée.
2. GitHub Environment `scolia-owner-production` correctement configuré
   avec `CLOUDFLARE_API_TOKEN`, `CLOUDFLARE_ACCOUNT_ID` et
   `SCOLIA_OWNER_ADMIN_TOKEN`, sans afficher leur valeur.
3. Jeton Cloudflare autorisé pour D1 et Workers, compte ayant moins de dix
   bases D1 si en formule gratuite.
4. Cloudflare Access/MFA réellement actif sur les routes propriétaire.
5. Une activation explicite de « continuité en ligne » par l'école, avec
   conservation du code de secours et confirmation locale du jumelage.

Ne jamais tenter de modifier manuellement la base commerciale, de copier
des secrets entre fournisseurs, ni de déployer directement `wrangler.jsonc`
avec ses identifiants fictifs.

### Jumelage du PC serveur

Après inscription du serveur scolaire et activation de la continuité chiffrée,
le responsable autorisé, **depuis le PC principal uniquement**, peut appeler
`POST /api/nuage/jumelage-d1` avec `{"confirm":true}` via la session scolaire
authentifiée protégée par les droits de sauvegarde.

Le serveur utilise son propre jeton scolaire enregistré dans son coffre local
pour appeler `POST /api/v1/server/cloud/mirror/pair` sur la console Cloudflare.
Ce second endpoint vérifie le jeton scolaire (sa révocation comprise), l'accord
explicite, la clé de chiffrement de l'école et l'empreinte de l'installation.
Il refuse une autre machine ou une autre clé sans migration supervisée.

### API pilote pour le serveur (jamais accessible aux comptes utilisateur)

- `GET /api/v1/server/cloud/mirror/status` : état et curseur.
- `POST /api/v1/server/cloud/mirror/frames` : écritures idempotentes de
  24 Kio maximum, avec référence de flux opaque, identifiant d'opération,
  révision et SHA-256 des octets chiffrés.
- `GET /api/v1/server/cloud/mirror/frames?after=0&limit=20` : relire les
  trames chiffrées de **son établissement uniquement**, par curseur.
- Le journal est plafonné à 1 000 trames par établissement pour protéger
  le quota gratuit ; aucun ancien contenu n'est supprimé silencieusement.

Aucune route n'accepte les identifiants de compte des élèves, cookies,
reçus officiels, mots de passe ou secret de chiffrement. Les appareils
ne reçoivent **aucun** accès directement à cette API pour le moment.

### Reste à développer avant exploitation réelle

- Raccordement automatique du moteur SQLite local (journal de changements,
  snapshots chiffrés par établissement, checkpoints, replay).
- Lecture autorisée depuis les appareils, après permission école/utilisateur,
  et contrôles de révocation, sans distribuer la clé maître.
- Validation métier dans le cloud si l'on veut que deux appareils partagent
  immédiatement les paiements pendant l'arrêt du PC.
- Stratégie de migration/rétention, sauvegardes, tests de pertes réseau,
  erreurs 429/quota, sécurité et incompatibilités entre schémas.
- Tests réels Android/Windows, puis publication d'une nouvelle version.
Le déploiement, la facturation et l'activation ne sont **pas** déclenchés
par le simple ajout de ce dossier au dépôt GitHub.
