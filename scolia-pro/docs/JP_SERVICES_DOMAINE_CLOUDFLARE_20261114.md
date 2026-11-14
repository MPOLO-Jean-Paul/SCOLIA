# JP-Services / SCOLIA Pro — domaine, contact et console Cloudflare

Mise à jour du 14 novembre 2026 — **plan de déploiement, non preuve de mise en production**.

## Contact professionnel

- Contact éditeur et assistance SCOLIA : `contact@jp-services.org`.
- Réception déjà testée : Cloudflare Email Routing transfère vers une boîte Gmail vérifiée.
- Envoi : Brevo ; vérifier son approbation d'envoi et le statut d'authentification du domaine, distinct du seul statut « expéditeur vérifié ».
- SCOLIA affiche ce contact depuis `AboutInfo.Email` dans « À propos », « Paramètres / À propos » et les assistants de première installation PC/mobile.
- Les coordonnées des écoles doivent rester indépendantes ; ne jamais remplacer leur propre email ou téléphone institutionnel par celui de l'éditeur.

## Architecture cible

| Service | Domaine envisagé | Infrastructure | État |
| --- | --- | --- | --- |
| Console propriétaire + API de licence | `console.jp-services.org` | Worker existant `scolia-pro-owner`, D1 `scolia-pro-owner` | À rattacher et valider |
| Relais synchronisation | `relay.jp-services.org` | Worker existant `scolia-pro-relay`, Durable Objects SQLite / WebSockets | À rattacher et tester |
| Site de présentation | `scolia.jp-services.org` | Cloudflare Pages ou Workers Static Assets | À concevoir / déployer |
| Site JP-Services | `www.jp-services.org` | Pages / Workers Static Assets | À concevoir / déployer |
| Sauvegardes scolaires chiffrées | Pas d'adresse publique requise | R2 privé `scolia-pro-backups` | À vérifier / provisionner sans écraser |
| Base de métadonnées éditeur | Pas d'adresse publique directe | D1 privé, binding `OWNER_DB` | Déploiement existant prévu |
| Installer / APK | À définir après contrôle des artefacts | GitHub Releases, éventuellement distribution via R2 | Ne pas publier d'URL fictive |

Les noms d'hôte prévus ne sont **pas** des URL actuellement mises en ligne.

### Séparation des données

- La **base SQLite locale par établissement** demeure la source opérationnelle : inscriptions, paiements, présence et travaux hors ligne.
- **D1 propriétaire** stocke écoles, identifiants de serveurs, invitations, politiques de licence et métadonnées de connectivité ; ne pas la transformer en base globale d'apprenants.
- **R2** contient uniquement les copies chiffrées de chaque école, avec son consentement, pas de sauvegardes accessibles aux opérateurs JP-Services.
- **Durable Objects / WebSocket** coordonnent les connexions actives ; leurs messages ne remplacent pas une file persistante, les accusés de réception et la reprise des opérations hors ligne.
- Une identité de domaine est une adresse DNS, **pas** un serveur ou une base de données en soi.

## Configuration du compte Cloudflare

1. Dans `Workers & Pages`, vérifier l'existence et l'état des deux Workers `scolia-pro-owner` et `scolia-pro-relay`.
2. Dans `Storage & Databases > D1`, **rechercher** `scolia-pro-owner`. S'il existe, réutiliser son UUID ; sinon laisser le workflow sécurisé le créer.
3. Dans `Storage & Databases > R2`, vérifier le bucket privé `scolia-pro-backups`. Les options R2 peuvent demander l'activation d'une souscription même avec quotas gratuits.
4. Configurer les domaines personnalisés **depuis chaque Worker** : `Settings > Domains & Routes > Add > Custom Domain`. Éviter de créer des CNAME manuels concurrents.
5. Lier `console.jp-services.org` au Worker propriétaire. Le Worker doit exposer `/health`, `/api/v1/notice`, `/api/v1/owner/*` et `/console` conformément au code actuel.
6. Protéger **uniquement** `/console` et `/api/v1/owner/*` par Cloudflare Access + MFA + identités administrateur autorisées. Ne pas bloquer `/api/v1/register`, `/api/v1/heartbeat` ou les services scolaires nécessaires.
7. Garder le jeton administrateur `SCOLIA_OWNER_ADMIN_TOKEN` exclusivement dans GitHub Actions Secrets / Cloudflare Worker Secrets, **jamais** dans Git, un QR, l'APK ou des captures.
8. Vérifier les routes publiques, l'isolation par établissement, la protection contre l'usurpation, les contrôles de licence, les sauvegardes et la restauration avant tout pilote réel.
9. Une fois le domaine validé, configurer l'URL du service de licences dans la distribution SCOLIA ; **ne pas** activer par défaut le verrouillage commercial tant que les clients hors ligne n'ont pas été testés.

## Publication contrôlée du Worker propriétaire

Le dépôt inclut déjà `.github/workflows/scolia-owner-production.yml`, déclenché manuellement depuis
`release/direct-apk-scolia-pro-20261103`, avec l'environnement GitHub `scolia-owner-production`.

- Secrets requis : `CLOUDFLARE_API_TOKEN`, `CLOUDFLARE_ACCOUNT_ID`, `SCOLIA_OWNER_ADMIN_TOKEN`.
- Variable : `SCOLIA_OWNER_DOMAIN=console.jp-services.org` ; cette valeur doit correspondre à un domaine réellement lié au Worker.
- Variable : `SCOLIA_OWNER_ACCESS_AUD` égale à l'audience réelle de l'application Cloudflare Access.
- Le workflow prépare la configuration D1, exécute les migrations manquantes, déploie puis effectue une vérification HTTP ; **ne le lancer qu'après configuration Access et tests du domaine**, avec l'approbation prévue.
- L'exécution d'un pipeline ou la présence d'un fichier de configuration ne garantit pas qu'une base Cloudflare réelle a déjà été provisionnée.

## Réseau temps réel

`Calcul / Workers` désigne le **runtime de code**, pas automatiquement une fonction « temps réel ».
Pour SCOLIA, on peut utiliser les Durable Objects + WebSockets hibernables pour les sessions actives.
Le code actuel du relais Cloudflare contient déjà une classe `ScoliaRoom` liée à Durable Objects ; auditer sa sécurité, sa facturation et son comportement en panne avant d'étendre le service.
Préserver les accusés de réception et une synchronisation incrémentale locale.

## Contrôles avant diffusion

- Tester la connexion et l'assistant de configuration **Windows / Android** sur petits et grands écrans.
- Tester le contact affiché dans la page « À propos », les paramètres et les pages d'installation.
- Vérifier que la console renvoie 403/503 sans Cloudflare Access et n'expose aucune donnée scolaire sensible.
- Tester la récupération des données après coupure Internet, l'isolation des écoles, la rotation des secrets et la restauration d'une sauvegarde chiffrée.
- Contrôler facturation D1 / R2 / Workers / Durable Objects, avec seuils et alertes, avant activation à plusieurs écoles.

## Documentation

- [D1](https://developers.cloudflare.com/d1/get-started/)
- [Workers Custom Domains](https://developers.cloudflare.com/workers/configuration/routing/custom-domains/)
- [Cloudflare Access pour Workers](https://developers.cloudflare.com/workers/configuration/cloudflare-access/)
- [Durable Objects WebSockets](https://developers.cloudflare.com/durable-objects/best-practices/websockets/)
- [R2](https://developers.cloudflare.com/r2/get-started/)
