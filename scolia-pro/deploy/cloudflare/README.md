# SCOLIA Pro — Déployer le relais Cloudflare gratuit (sans toucher à Render)

## Architecture réelle

- Le serveur principal et la **base scolaire SQLite restent sur Windows / Android**. Aucune table d'apprenants, paiement, nom ou fichier de l'école n'est stocké par Cloudflare.
- Le Worker et son Durable Object transmettent les trames **WebSocket WSS** chiffrées par SCOLIA (AES-256-GCM côté application).
- Le relais conserve en mémoire/attachement WebSocket uniquement une empreinte de capacité et un nonce de session ; aucun déchiffrement de documents ou de données métier.
- Le protocole est celui du relais .NET actuel : `GET /health`, `/v1/host/{room}`, `/v1/client/{room}`. La base de données, les autorisations et le journal hors ligne ne changent pas.
- Les connexions peuvent hiberner quand elles sont inactives, sans maintenir en permanence une instance en mémoire. La durée de vie physique d'une connexion, les quotas gratuits et la disponibilité ne sont toutefois pas garantis.

## 1. Créer le Worker dans son compte Cloudflare

1. Se connecter à <https://dash.cloudflare.com/>.
2. Ouvrir **Workers & Pages → Create application → Import a repository / GitHub** (l'intitulé peut varier).
3. Autoriser Cloudflare à accéder au dépôt GitHub **MPOLO-Jean-Paul/SCOLIA**. Ne pas lui donner un accès illimité à tous les dépôts si ce n'est pas nécessaire.
4. Sélectionner le dépôt **SCOLIA**, la branche de préparation **feature/scolia-cloudflare-relay-20261107** et le **Root directory** : `scolia-pro/deploy/cloudflare`.
5. Nom du Worker : **scolia-pro-relay** (doit correspondre au champ `name` du fichier `wrangler.jsonc`).
6. Build command : `npm run test && npm run typecheck`. Deploy command : `npx wrangler deploy`. Aucun conteneur Docker, base Postgres ou serveur Render supplémentaire n'est nécessaire.
7. Choisir **Workers Free** et confirmer le déploiement. Le fichier Wrangler crée un Durable Object **SQLite-backed** avec la migration `v1` ; cela respecte le plan gratuit. Si Cloudflare indique qu'un plan payant est requis, ne pas souscrire et relever le message.
8. Cloudflare attribue une adresse réelle du type `https://scolia-pro-relay.<votre-sous-domaine>.workers.dev`. Ce sous-domaine **ne peut pas être deviné ici**.

Si le tableau de bord ne permet pas de choisir la branche ou le répertoire, utiliser GitHub Integration > Settings > Builds pour préciser le répertoire et la branche avant déploiement. Alternative depuis un PC disposant de Git et Node.js :

```bash
git clone -b feature/scolia-cloudflare-relay-20261107 https://github.com/MPOLO-Jean-Paul/SCOLIA.git
cd SCOLIA/scolia-pro/deploy/cloudflare
npm install
npm run check
npx wrangler login
npx wrangler deploy
```

**Ne pas transmettre de jetons Cloudflare, mots de passe GitHub, QR privés ou fichiers de clés dans ChatGPT.**

## 2. Tester le service avant de toucher à SCOLIA

- Ouvrir `https://<votre-adresse-workers.dev>/health` (remplacer le modèle par l'URL réellement donnée par Cloudflare).
- Attendu : `{"service":"SCOLIA relais","protocol":1,"provider":"cloudflare"}`.
- Ouvrir un autre chemin `/v1/client/...` sans jeton : il ne doit pas exposer une session ni des données.
- Consulter le tableau de bord Cloudflare pour les erreurs et les quotas de Workers et Durable Objects.

Une réponse correcte de `/health` **ne prouve pas à elle seule** que les connexions chiffrées Windows et Android fonctionnent.

## 3. Essai sur SCOLIA Pro

1. **Conserver Render actif.** Avant toute modification du relais, sauvegarder la base de l'établissement et vérifier les écritures hors ligne en attente.
2. Sur **l'appareil serveur principal**, dans **Réseau et activité → Réglages avancés**, renseigner l'adresse Cloudflare sous forme WSS : `wss://scolia-pro-relay.<votre-sous-domaine>.workers.dev/` (le protocole WSS et la barre finale sont requis).
3. Cliquer sur **Activer ou vérifier**. Le bouton peut encore afficher « Render » dans la version 1.2.35 : le code de connexion accepte une adresse de relais WSS personnalisée.
4. La modification du fournisseur **renouvelle la liaison chiffrée** : générer un **nouveau QR SCOLIA** sur l'appareil serveur pour les téléphones et PC qui doivent rejoindre ce nouveau chemin. Les anciens codes Render ne doivent pas servir à autoriser un nouvel appareil Cloudflare.
5. Sur l'autre appareil, scanner le QR officiel une seule fois. Tester l'accès sur le même Wi-Fi, puis désactiver le Wi-Fi du téléphone pour passer sur Orange/Airtel/Vodacom. Vérifier la reconnexion automatique après fermeture/réouverture, la révocation d'un appareil et la synchronisation après une courte coupure.
6. Tester l'ordinateur serveur arrêté : les clients doivent afficher leur état hors ligne correctement, **sans fausse confirmation d'encaissement**.
7. Ne désactiver Render qu'après réussite des essais réels Android–Windows, y compris les paiements.

## 4. Coût et limites

- `workers.dev` et les **Durable Objects SQLite-backed** sont utilisables sur Workers Free, avec des quotas. La version gratuite peut refuser des requêtes une fois une limite atteinte ; elle ne fournit pas de garantie professionnelle 24h/24.
- Le relais ne démarre pas un ordinateur principal éteint et n'enregistre pas la base scolaire.
- Le trafic peut être mesuré par Cloudflare ; les contenus scolaires restent chiffrés applicativement. Ne jamais enregistrer les tokens WebSocket ni les corps de messages dans les logs.
- La première invitation reste privée, à usage unique et de courte durée. Un appareil révoqué ne doit pas retrouver son accès par un autre fournisseur.
- Vérifier régulièrement les limites d'utilisation dans **Workers & Pages → Metrics**.

Documentation officielle :
- https://developers.cloudflare.com/durable-objects/best-practices/websockets/
- https://developers.cloudflare.com/durable-objects/platform/limits/
- https://developers.cloudflare.com/workers/ci-cd/builds/


## Dépannage : `Failed: root directory not found`

Cette erreur survient avant l'installation npm. Vérifier sur la carte du **nouveau** build Cloudflare :
- **Git repository :** `MPOLO-Jean-Paul/SCOLIA`.
- **Branch :** `feature/scolia-cloudflare-relay-20261107` (et surtout pas `main`).
- **Root directory :** `scolia-pro/deploy/cloudflare`, sans barre oblique initiale.
- Enregistrer le formulaire Settings → Builds, puis vérifier les réglages après rechargement.
- Un bouton « Retry build » sur un ancien build `main` peut continuer à utiliser l'ancien commit. Un **nouveau commit** sur la branche de production déclenche un nouveau build via l'intégration GitHub.
- Ne pas modifier les jetons API, ne pas supprimer le Worker et ne pas déplacer la base SQLite.
