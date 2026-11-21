# SCOLIA — réparer le build du relais Cloudflare

Le dépôt GitHub a pour branche par défaut **main**. Cette branche ne contient
**pas** le dossier scolia-pro/deploy/cloudflare ; le relais est développé dans
**release/direct-apk-scolia-pro-20261103**. Si Cloudflare est configuré sur
main, le build échouera faute de dossier. Le journal du build Cloudflare reste
nécessaire pour confirmer que c'est la cause de l'échec observé.

## Valeurs attendues dans Cloudflare → Workers & Pages → scolia-pro-relay → Settings → Builds

| Réglage | Valeur |
| --- | --- |
| Dépôt | MPOLO-Jean-Paul/SCOLIA |
| Branche | release/direct-apk-scolia-pro-20261103 |
| Root directory | scolia-pro/deploy/cloudflare |
| Build command | npm ci --no-audit --no-fund && npm run check |
| Deploy command | npx wrangler deploy |
| Nom du Worker | scolia-pro-relay |

Le nom du Worker correspond à celui de wrangler.jsonc. Les tests GitHub
valident l'installation npm, les tests de sécurité, les types et
wrangler deploy --dry-run, mais ils ne déploient rien.

## Réparation automatique des paramètres, avec contrôle

Le script scripts/repair-build-settings.mjs lit la configuration courante
par l'API officielle Cloudflare Workers Builds. Par défaut, il affiche
seulement les champs divergents. Avec l'option --apply, il corrige
**uniquement** les valeurs de branche, root, build et deploy attendues.

Il vérifie auparavant le dépôt exact et l'identifiant du Worker pour éviter
de modifier un autre service. Il **refuse** de continuer si le jeton de build
est absent, ou si le dépôt associé n'est pas MPOLO-Jean-Paul/SCOLIA.

### Prérequis pour GitHub Actions

Le workflow manuel se trouve sur la branche GitHub **main** pour être visible
dans l'onglet Actions ; il utilise la version validée du script sur la branche
release. Créer un environnement GitHub protégé nommé scolia-relay-builds,
puis y placer les secrets suivants :

- CLOUDFLARE_BUILD_SETTINGS_TOKEN : API token **utilisateur** avec
  Workers Builds Configuration (Edit) et Workers Scripts (Read).
- CLOUDFLARE_ACCOUNT_ID : ID du compte Cloudflare.

Aucun jeton ne doit être collé dans Git, une conversation, les logs ou l'APK.
Dans Actions, sélectionner le workflow Cloudflare de réparation, choisir
d'abord **inspect**, puis éventuellement **apply**, avec la confirmation
REPARER_RELAIS. Après un PATCH réussi, relancer un build Cloudflare de la
bonne branche et lire les logs du nouveau build.

Ce script ne déploie pas de Worker, ne renouvelle pas les secrets, ne change
pas les previews et **ne touche pas aux migrations D1, données, écoles,
appareils ni paiements**. Si le token de build Cloudflare a été révoqué,
sélectionner un token actif dans les réglages Cloudflare ; le script n'en
choisit jamais un autre automatiquement.

Documentation officielle :
- https://developers.cloudflare.com/workers/ci-cd/builds/api-reference/
- https://developers.cloudflare.com/workers/ci-cd/builds/troubleshoot/
