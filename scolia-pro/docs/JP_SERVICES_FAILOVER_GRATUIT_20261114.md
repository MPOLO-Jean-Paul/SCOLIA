# SCOLIA Pro — relais gratuits et bascule automatique (JP-Services)

Mise à jour du 14 novembre 2026. **Cette configuration est prête dans GitHub mais n'active aucun service Cloudflare tant que le workflow manuel n'a pas été exécuté et validé.**

## Infrastructure de départ

- Serveur scolaire : la base SQLite principale reste locale et fonctionne hors ligne.
- Relais existant Render : `https://scolia-pro-relay.onrender.com/health` / `wss://scolia-pro-relay.onrender.com/`. Service gratuit existant, ne pas le supprimer.
- Relais Cloudflare à publier : `https://relay.jp-services.org/health` / `wss://relay.jp-services.org/`, Worker `scolia-pro-relay` + Durable Object SQLite.
- Base de **gestion commerciale** SCOLIA : D1 `scolia-pro-owner` via le Worker propriétaire. Elle n'héberge pas la base métier des écoles.
- Sauvegardes facultatives : R2 privé `scolia-pro-backups` et chiffrement propre à chaque établissement.

## Déploiement gratuit du relais Cloudflare

1. Dans Cloudflare, vérifier que la zone `jp-services.org` est active.
2. Vérifier que `relay.jp-services.org` n'a **pas** de CNAME existant incompatible avec l'ajout d'un **Worker Custom Domain**. Ne pas effacer une entrée sans audit.
3. Dans GitHub → Settings → Environments, préparer `scolia-cloudflare-free` (approbation recommandée) et enregistrer dans ses **secrets** :
   - `CLOUDFLARE_API_TOKEN` avec les droits nécessaires à Workers, Durable Objects et au domaine personnalisé ;
   - `CLOUDFLARE_ACCOUNT_ID`.
   Ne jamais saisir ces valeurs dans un commit, une capture ou dans la conversation.
4. Fusionner la PR après les tests. Dans GitHub Actions, sur `release/direct-apk-scolia-pro-20261103`, ouvrir **SCOLIA Pro - relais Cloudflare gratuit (publication contrôlée)** et lancer manuellement en validant le formulaire.
5. Le workflow effectue tests, typage, build à blanc, puis déploie avec `wrangler.free.jsonc` ; Cloudflare rattache le domaine et le certificat TLS. L'ancienne configuration `wrangler.jsonc` pour le développement est intacte.
6. Vérifier l'URL de santé `https://relay.jp-services.org/health`. Elle doit renvoyer `{"service":"SCOLIA relais","protocol":1,"provider":"cloudflare"}`.
7. Faire un test WSS réel avec un serveur SCOLIA et un client Windows/Android avant la bascule ; un JSON `/health` ne suffit pas.

**Option sans domaine :** conserver provisoirement l'adresse `workers.dev` réellement fournie par Cloudflare. Cette adresse ne doit pas être supposée.

## Comment SCOLIA bascule automatiquement

Le code existant `RemoteConnection.StartHostIfMissing` ouvre deux sessions **sortantes** WSS vers les deux relais en même temps. `RemoteHttpHandler` teste le chemin de secours lors d'un échec transport (ou HTTP 502/503/504), le préfère provisoirement pendant 30 secondes, puis revient au chemin principal. Les mêmes identifiants de salle, clés de session et droits d'appareil sont conservés. Il ne s'agit **pas** d'une synchronisation entre deux bases de données différentes.

Dans SCOLIA sur le **serveur de l'école**, après validation du Worker :
- principal : `wss://relay.jp-services.org/`
- secondaire : `wss://scolia-pro-relay.onrender.com/`
- cliquer « Activer ou vérifier Internet » puis vérifier **les deux** états. Les clients reçoivent les adresses en jumelage sécurisé.
- dans le cas des licences commerciales gérées par la console JP-Services, sélectionner le fournisseur Cloudflare comme principal dans la console ; l'ancien relais Render est déjà prévu comme secours par `RemoteLink.EffectiveBackupRelay`.

Le serveur local doit rester allumé et connecté à Internet pour répondre aux appareils éloignés. La bascule automatique ne remplace pas la poursuite du travail hors ligne si **les deux** chemins ou la machine principale sont inaccessibles.

**Important :** ne pas renvoyer automatiquement une écriture sensible sans idempotence côté serveur. Conserver le journal des opérations, l'accusé de réception et le contrôle anti-doublons lors du basculement.

## Limites réelles du gratuit

- **Cloudflare Workers Free** : quota d'exécutions journalières. Un dépassement peut interrompre le service au lieu d'entraîner automatiquement une facturation.
- **Cloudflare Durable Objects** avec SQLite : disponible sur Workers Free, sous quotas de requêtes, durée active, lecture/écriture et stockage.
- **Cloudflare D1 Free** : 10 bases maximum ; 500 Mo par base ; 5 Go de stockage total, avec limites de lecture/écriture. Le passage à l'offre payante ne doit jamais être automatique sans choix du propriétaire.
- **Render Free** : après 15 minutes sans trafic entrant, le service peut s'endormir ; son redémarrage peut prendre environ une minute. Il n'a pas de disque persistant et n'est pas un secours garanti 24 h/24.
- Une **bascule automatique de D1 vers Postgres Render** n'est **pas** configurée : elle nécessiterait une réplication transactionnelle, des contrôles de cohérence et une stratégie anti-split-brain. L'offre Postgres gratuite de Render expire après 30 jours, donc ne convient pas à une base commerciale durable.
- R2 est facultatif, sous réserve de son activation et des quotas en vigueur. Il ne faut pas confondre sauvegarde R2 et failover D1.

## Contrôles nécessaires avant ouverture commerciale

- Test Cloudflare actif / Render indisponible, puis inversement, sans redemander le jumelage.
- Révocation d'un appareil et refus de l'accès, y compris sur le secours.
- Paiement non doublé après une coupure pendant l'accusé de réception.
- Aucune donnée scolaire accessible par le Worker ou par la console propriétaire.
- Synchronisation automatique et lecture/écriture hors ligne avant reconnexion.
- Surveillance des quotas : ne pas accepter de frais automatiques non validés.
- Console propriétaire : Cloudflare Access + MFA requis avant publication sur `console.jp-services.org`.

Documentation : https://developers.cloudflare.com/workers/configuration/routing/custom-domains/ et https://developers.cloudflare.com/durable-objects/platform/pricing/ et https://render.com/docs/free
