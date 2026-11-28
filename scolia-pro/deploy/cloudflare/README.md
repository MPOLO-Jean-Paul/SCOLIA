# SCOLIA Pro — Déployer le relais Cloudflare gratuit (sans toucher à Render)

## Architecture réelle

- Le serveur principal et la **base scolaire SQLite restent sur Windows / Android**. Aucune table d'apprenants, paiement, nom ou fichier de l'école n'est stocké par Cloudflare.
- Le Worker et son Durable Object transmettent les trames **WebSocket WSS** chiffrées par SCOLIA (AES-256-GCM côté application).
- Le relais conserve en mémoire/attachement WebSocket uniquement une empreinte de capacité et un nonce de session ; aucun déchiffrement de documents ou de données métier.
- Le protocole est celui du relais .NET actuel : `GET /health`, `/v1/host/{room}`, `/v1/client/{room}`. La base de données, les autorisations et le journal hors ligne ne changent pas.
- Les connexions peuvent hiberner quand elles sont inactives, sans maintenir en permanence une instance en mémoire. La durée de vie physique d'une connexion, les quotas gratuits et la disponibilité ne sont toutefois pas garantis.
- Limites identiques au relais .NET : un client doit envoyer sa requête en 15 s et ne reste pas plus de 15 min ; une session hôte dure au plus 65 min (SCOLIA la renouvelle à 55 min) ; 16 clients par salle ; 120 tentatives de connexion par minute et par adresse IP (compteur en mémoire de chaque instance Cloudflare, sans stockage). Les délais sont appliqués par une alarme du Durable Object : c'est le seul appel de stockage.
- Quand le serveur SCOLIA se reconnecte, sa nouvelle session **remplace** l'ancienne (même après une coupure réseau mal détectée) au lieu d'être refusée.

## 1. Créer le Worker dans son compte Cloudflare

1. Se connecter à <https://dash.cloudflare.com/>.
2. Ouvrir **Workers & Pages → Create application → Import a repository / GitHub** (l'intitulé peut varier).
3. Autoriser Cloudflare à accéder au dépôt GitHub **MPOLO-Jean-Paul/SCOLIA**. Ne pas lui donner un accès illimité à tous les dépôts si ce n'est pas nécessaire.
4. Sélectionner le dépôt **SCOLIA**, la branche de préparation **release/direct-apk-scolia-pro-20261103** (puis `main` une fois la branche fusionnée) et le **Root directory** : `scolia-pro/deploy/cloudflare`.
5. Nom du Worker : **scolia-pro-relay** (doit correspondre au champ `name` du fichier `wrangler.jsonc`).
6. Build command : `npm run test && npm run typecheck`. Deploy command : `npx wrangler deploy`. Aucun conteneur Docker, base Postgres ou serveur Render supplémentaire n'est nécessaire.
7. Choisir **Workers Free** et confirmer le déploiement. Le fichier Wrangler crée un Durable Object **SQLite-backed** avec la migration `v1` ; cela respecte le plan gratuit. Si Cloudflare indique qu'un plan payant est requis, ne pas souscrire et relever le message.
8. Cloudflare attribue une adresse réelle du type `https://scolia-pro-relay.<votre-sous-domaine>.workers.dev`. Ce sous-domaine **ne peut pas être deviné ici**.

Si le tableau de bord ne permet pas de choisir la branche ou le répertoire, utiliser GitHub Integration > Settings > Builds pour préciser le répertoire et la branche avant déploiement. Alternative depuis un PC disposant de Git et Node.js :

```bash
git clone -b release/direct-apk-scolia-pro-20261103 https://github.com/MPOLO-Jean-Paul/SCOLIA.git
cd SCOLIA/scolia-pro/deploy/cloudflare
npm ci
npm run check
npx wrangler login
npx wrangler deploy
```

**Ne jamais transmettre de jetons Cloudflare, mots de passe GitHub, QR privés ou fichiers de clés à un assistant, une messagerie ou un tiers.**

## 2. Tester le service avant de toucher à SCOLIA

- Ouvrir `https://<votre-adresse-workers.dev>/health` (remplacer le modèle par l'URL réellement donnée par Cloudflare).
- Attendu : `{"service":"SCOLIA relais","protocol":1,"provider":"cloudflare"}`.
- Ouvrir un autre chemin `/v1/client/...` sans jeton : il ne doit pas exposer une session ni des données.
- Consulter le tableau de bord Cloudflare pour les erreurs et les quotas de Workers et Durable Objects.

Une réponse correcte de `/health` **ne prouve pas à elle seule** que les connexions chiffrées Windows et Android fonctionnent.

## 3. Essai sur SCOLIA Pro

1. **Conserver Render actif.** Avant toute modification du relais, sauvegarder la base de l'établissement et vérifier les écritures hors ligne en attente.
2. Depuis SCOLIA Pro **1.2.90**, Cloudflare est proposé par défaut sous `wss://relay.jp-services.org/` et Render reste le secours sous `wss://scolia-pro-relay.onrender.com/`. L'appareil principal peut confirmer cette configuration depuis **Réseau et activité → Réglages avancés → Activer ou vérifier Internet**. Les serveurs sous licence gérée doivent d'abord obtenir l'approbation du nouveau fournisseur depuis la console propriétaire.
3. Le changement de fournisseur ne fait **pas** tourner les clés de salle ou de jumelage : les appareils autorisés conservent leurs droits, et les anciens liens Render restent compatibles avec le secours. Seule une révocation explicite retire un appareil.
4. Pour **un nouvel appareil**, générer un QR SCOLIA depuis le serveur principal après sa configuration, puis scanner l'invitation officielle une seule fois. Les anciennes invitations expirées et les autorisations révoquées ne sont jamais réactivées.
5. Tester l'accès sur le même Wi-Fi, puis passer le téléphone en 4G (Orange/Airtel/Vodacom). Vérifier la connexion à Cloudflare, la reprise via Render, la reconnexion après fermeture/réouverture, la révocation d'un appareil et la synchronisation après une courte coupure.
6. Tester l'ordinateur serveur arrêté : les clients doivent afficher leur état hors ligne correctement, **sans fausse confirmation d'encaissement**.
7. **Conserver Render activé comme secours** après les essais Android–Windows : le désactiver supprimerait le basculement automatique en cas de panne Cloudflare.

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
- **Branch :** `release/direct-apk-scolia-pro-20261103` (et surtout pas `main`).
- **Root directory :** `scolia-pro/deploy/cloudflare`, sans barre oblique initiale.
- Enregistrer le formulaire Settings → Builds, puis vérifier les réglages après rechargement.
- Un bouton « Retry build » sur un ancien build `main` peut continuer à utiliser l'ancien commit. Un **nouveau commit** sur la branche de production déclenche un nouveau build via l'intégration GitHub.
- Ne pas modifier les jetons API, ne pas supprimer le Worker et ne pas déplacer la base SQLite.


## Échec du contrôle `Workers Builds: scolia-pro-relay` sur GitHub

Ce contrôle **provient de l'intégration GitHub gérée dans le tableau de bord Cloudflare**,
pas du workflow GitHub Actions `SCOLIA Cloudflare relais - tests sans deploiement`.
Un test GitHub vert ne prouve donc pas que le Worker a été déployé. Si le contrôle
externe est rouge, ouvrir sa page de build Cloudflare pour relever l'**étape** et
le **message exact** ; ne pas déduire automatiquement une erreur de code ou de quota.

Pour vérifier la configuration actuelle dans **Cloudflare → Workers & Pages →
scolia-pro-relay → Settings → Builds** :

| Paramètre | Valeur attendue |
| --- | --- |
| Dépôt GitHub | `MPOLO-Jean-Paul/SCOLIA` |
| Branche de production | `release/direct-apk-scolia-pro-20261103` |
| Root directory | `scolia-pro/deploy/cloudflare` (sans `/` initial) |
| Build command | `npm ci --no-audit --no-fund && npm run check` |
| Deploy command | `npx wrangler deploy` |
| Worker name | `scolia-pro-relay` |

Le nom est défini dans `wrangler.jsonc` ; il **doit correspondre** au nom
du Worker relié au dépôt. Enregistrer les paramètres Cloudflare, puis relancer
un **nouveau** build sur la bonne branche. Si l'interface n'affiche pas les
paramètres de build, vérifier la connexion du dépôt au Worker existant,
sans révoquer l'accès de tous les autres projets GitHub.

**Aucune modification de ce fichier README ne change automatiquement les
paramètres d'un compte Cloudflare.** Le présent dépôt n'a ni les secrets
Cloudflare ni accès au tableau de bord. La migration D1 et les comptes réels
ne doivent pas être touchés par une tentative de réparation du relais WebSocket.

Source officielle : <https://developers.cloudflare.com/workers/ci-cd/builds/configuration/>
et <https://developers.cloudflare.com/workers/ci-cd/builds/troubleshoot/>.

## Continuité locale en cas d'expiration de l'ancien relais

Le relais WebSocket transporte les accès au PC serveur ; il ne constitue
**pas** le serveur métier des paiements. Si ce relais expire, Android tente
désormais la synchronisation des sections D1 déjà approuvées **et** le dépôt
des opérations chiffrées pour lesquelles le serveur scolaire a préalablement
délivré une délégation encore valide.

- Toutes les saisies restent dans SQLite et dans l'outbox pendant une coupure.
- L'accusé de **dépôt** cloud n'est jamais un reçu de paiement validé.
- Si la délégation est expirée/révoquée, l'envoi est suspendu sans perdre
  l'opération : un compte autorisé devra renouveler son accès.
- Les règles Android (batterie, Doze, arrêt forcé et disponibilité du réseau)
  ne permettent pas de garantir un délai de synchronisation instantané.
- L'autonomie **métier complète** sans PC principal nécessitera encore la
  migration validée des inscriptions, notes et paiements vers le serveur central
  D1, ainsi qu'un déploiement de préproduction isolé et des essais réels.


## Inspection et réparation guidées depuis GitHub Actions

Le workflow **SCOLIA Cloudflare - inspecter ou reparer les builds du relais (manuel)**
est déjà disponible sur la branche par défaut `main` :
[Ouvrir le workflow GitHub Actions](https://github.com/MPOLO-Jean-Paul/SCOLIA/actions/workflows/scolia-cloudflare-builds-repair.yml).

1. Dans GitHub → **Settings → Environments**, ouvrir `scolia-relay-builds`
   et y définir les secrets `CLOUDFLARE_BUILD_SETTINGS_TOKEN`
   et `CLOUDFLARE_ACCOUNT_ID`. Ne jamais les envoyer en conversation
   ou les écrire dans le dépôt.
2. Le jeton doit être un **API token Cloudflare lié à l'utilisateur** (et non
   un jeton de compte ou le build token de déploiement), limité aux permissions
   **Workers Builds Configuration: Edit** et **Workers Scripts: Read**.
   Le Worker conserve son propre build token, qui n'est pas modifié.
3. Dans l'onglet **Actions**, sélectionner ce workflow, puis **Run workflow**.
   Choisir `inspect`, renseigner la confirmation `REPARER_RELAIS`
   et lancer. Cette opération n'exécute aucun PATCH.
4. La vérification affiche seulement les **noms des champs divergents** et,
   s'ils sont lisibles, une **catégorie fixe d'erreur** du dernier build échoué.
   Aucun contenu brut des logs ni aucun secret Cloudflare n'est imprimé.
5. Si la vérification confirme la mauvaise branche, le mauvais répertoire
   ou les mauvaises commandes, relancer manuellement en mode `apply`,
   avec la même confirmation. Le script ne change que ces paramètres
   du Worker nommé `scolia-pro-relay`.
6. Le correcteur relit les réglages pour vérifier qu'ils correspondent
   aux paramètres attendus. Il ne crée ni Worker, ni migration D1, ni
   déploiement. Relancer ensuite un build depuis le tableau de bord
   Cloudflare, puis vérifier la réussite et le chemin `/health`.

**Si les paramètres sont déjà conformes**, ne pas relancer le mode `apply`
pour tenter de corriger autre chose. Consulter les causes proposées par
l'inspection (répertoire, point d'entrée, jeton, permissions, npm, quota).
La cause réelle ne peut être conclue qu'après lecture du journal Cloudflare.
Même après correction, tester les WebSockets WSS et les appareils réels :
un endpoint `/health` ne prouve pas les échanges chiffrés.

Référence : https://developers.cloudflare.com/workers/ci-cd/builds/api-reference/
