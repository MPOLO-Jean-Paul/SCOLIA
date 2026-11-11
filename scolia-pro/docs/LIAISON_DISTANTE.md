# Secrétariat et Comptabilité à distance

## Ce qui reste inchangé

Deux profils : Secrétariat et Comptabilité. Aucun compte enseignant ajouté. Les droits, dossiers, paiements, reçus, règles, comptes, sauvegardes et interfaces existants sont conservés. Le partage UDP/LAN/Wi-Fi reste disponible indépendamment d’Internet.

L’appareil principal (Android ou Windows) garde la base scolaire SQLite. L’autre appareil utilise le même serveur métier par un transport Internet chiffré ; il garde localement son cache et son journal SQLite chiffré. Il ne s’agit pas de deux bases métier multimaster : les écritures sont confirmées par l’appareil principal. Sans connexion, seules les opérations éligibles déjà prévues par SCOLIA sont mises en attente et seules les pages précédemment lues sont consultables. Une inscription ou un paiement en attente n’est pas annoncé comme confirmé par l’autre poste.

## Installer le relais technique

Prérequis réels : une petite machine Linux/VPS avec Docker Compose, un domaine pointant vers elle, TCP 443 et 80 accessibles. Le fournisseur peut facturer la machine et la bande passante ; aucun prix ni hébergement gratuit garanti. Pas de Netlify/serverless pour le WebSocket persistant. Le frontend web demeure compilable indépendamment.

Depuis `scolia-pro` :

```sh
export RELAY_DOMAIN=le-domaine-que-vous-controlez
docker compose -f deploy/relay/compose.yml up -d --build
```

Caddy obtient le certificat TLS. Vérifier `https://VOTRE_DOMAINE/health` puis utiliser `wss://VOTRE_DOMAINE/` dans SCOLIA. Ne pas remplacer la validation TLS. L’adresse `ws://` est acceptée uniquement sur loopback pour les tests locaux. Les volumes Caddy ne gardent que les certificats, pas les dossiers scolaires. Le relais fonctionne sans volume de données, sans base et sans journal d’accès. Les trames restent brièvement en mémoire pendant leur transmission, jamais en file durable. Une coupure du relais conserve les opérations sur les appareils ; il ne peut pas livrer lorsque l’appareil principal est absent.

Compilation sans Docker : `dotnet publish src/Scolia.Relay -c Release -o artifacts/relais` puis exécuter derrière un reverse proxy TLS supportant les WebSockets. Limiter l’accès et la consommation réseau au niveau du pare-feu de l’hébergeur. Le service borne les salles, connexions, messages, durées et tentatives ; il n’est pas un service multitenant public avec facturation et gestion d’abonnements.

## Relier les deux appareils

1. Sur l’appareil principal, ouvrir une session Secrétariat puis **Réseau et activité**.
2. Renseigner le relais sécurisé puis **Créer ou renouveler la liaison**. L’activation ne change pas le partage local.
3. Copier l’invitation et la transmettre en privé à l’autre appareil. Elle contient une clé de liaison : éviter les captures publiques et groupes de discussion.
4. Sur l’autre appareil, dans le choix de l’établissement, coller l’invitation sous **Rejoindre à distance** puis **Vérifier et rejoindre**.
5. Se connecter avec le compte habituel Comptabilité ou Secrétariat. La possession d’une invitation ne remplace pas le mot de passe et n’accorde aucun droit métier supplémentaire.

Le principal doit être allumé, connecté et garder son service SCOLIA actif. Sur Android, ce service est visible dans une notification, comme le service LAN existant. Une liaison expire après 30 jours. Renouveler la liaison révoque l’ancienne et impose une nouvelle invitation, tout en conservant l’identité du cache local et sa file en attente ; la révocation ne supprime pas les données déjà consultées sur un appareil hors ligne. Désactiver Internet conserve la base, les sauvegardes et le partage local.

## Protocole et protection

- Deux connexions sortantes WSS ; le relais permet de franchir CGNAT et réseaux distincts sans port entrant sur les téléphones. Transport natif .NET adapté à Avalonia ; pas de WebRTC, STUN ou TURN présenté comme implémenté. Ces services ne sont pas nécessaires à ce transport relayé.
- Secret d’hébergement distinct de l’invitation ; identité de salle liée au SHA-256 de ce secret. Jeton du second appareil contrôlé par le relais. L’invitation ne contient jamais la clé d’hébergement.
- AES-256-GCM applicatif, clés HKDF-SHA256 distinctes par requête/direction/session, compteur par trame authentifié, UUID par requête ; anti-rejeu durant la session, nouvelle époque aléatoire à chaque reconnexion. Un relais compromis peut interrompre les échanges mais ne peut pas déchiffrer les données ou fabriquer une réponse valide sans l’invitation privée.
- Les cookies, mots de passe et corps HTTP passent à l’intérieur des messages chiffrés ; aucune donnée scolaire dans les URL du relais. L’API vérifie toujours session, révocation du compte, CSRF et permissions. Le transport n’accepte que les routes `/api/` vers le serveur loopback fixé ; pas de proxy vers une adresse fournie par un tiers.
- Configuration et valeurs sensibles du journal/cache SQLite protégées via Android Keystore AES-GCM ou DPAPI du compte Windows. Sous Linux de test, clé au compte avec permissions 0600 ; pas de garantie matérielle. La base métier principale reste SQLite dans le dossier privé Android, protégée par le chiffrement système du téléphone ; cette version n’ajoute pas SQLCipher à la base scolaire existante. Les sauvegardes métier existantes conservent leur format et doivent rester privées.
- Import distant limité à 512 Kio par requête ; réponses/documents limités à 64 Mio. Pour les gros imports et sauvegardes, utiliser le partage local existant. Flux en direct renouvelé après 15 minutes ; hôte renouvelé toutes les heures ; reconnexion progressive de 2 à 60 secondes avec aléa.

## Journal durable et paiements

Migration additive du cache vers `hors-ligne.db`, schéma SQLite `user_version=1`, WAL, transactions d’écriture immédiates, synchronisation FULL. Les valeurs sensibles sont chiffrées **avant** leur écriture, y compris dans le WAL. Une file ancienne illisible bloque sa migration au lieu d’être remplacée par une file vide. Les originaux migrés sont conservés dans des sauvegardes `.avant-sqlite.enc`, protégées par l’identité système de cet appareil. Les anciennes pages sont migrées à leur lecture. Ne pas désinstaller l’application pour revenir en arrière : cela détruirait les clés Keystore Android. Pour restaurer un ancien fichier, déchiffrer sa sauvegarde sur ce même appareil avant retour de version ; ne pas écraser une file contenant des opérations nouvelles.

Les opérations éligibles sont inscrites durablement avant l’envoi réseau. Le passage en conflit et sa reprise sont atomiques. Chaque compte ne consulte et ne renvoie que sa propre file. L’ordre local et les UUID sont conservés. Une confirmation réussie retire l’opération ; un refus métier la place en conflit ; une session expirée attend la reconnexion. Les paiements et lots conservent la même clé dans l’en-tête et dans le corps : la contrainte unique du reçu en SQLite protège le paiement même si le cache HTTP a disparu. Aucun dernier-écrivain-gagnant pour une mensualité : les règles du serveur rejettent les mois déjà réglés.

## Android en arrière-plan

Une tâche WorkManager unique, périodique (minimum 15 minutes), avec réseau connecté, batterie non faible et reprise exponentielle tente d’envoyer la file distante et de rafraîchir jusqu’à 32 pages JSON déjà consultées, sans toucher à l’écran. Elle utilise uniquement le cookie chiffré de la dernière session, jamais un mot de passe conservé ; si la session expire ou est révoquée, elle attend une connexion normale. La déconnexion efface le cookie de reprise sans supprimer les opérations en attente. Le service existant de l’appareil principal conserve sa notification visible. Doze, économie d’énergie, restrictions constructeur et arrêt forcé peuvent retarder ou empêcher le travail. Téléphone éteint ou hors réseau : aucune réception instantanée garantie.

FCM n’est **pas activé** : aucun projet Firebase, fichier `google-services.json`, compte serveur ni jeton réel fourni. Aucun faux service de notification ni clé privée embarquée. La reprise périodique ne dépend pas d’une notification FCM. Une intégration FCM ultérieure devra envoyer uniquement un signal technique via un serveur authentifié, sans dossier ni paiement, et déclencher le même travail natif.

## Validation et limites

Les tests automatisés démarrent réellement un serveur SQLite et un relais WebSocket séparés sur des ports loopback. Ils vérifient connexion, droits des deux profils, révocation du compte, secret invalide, flux en direct, coupure/reprise d’inscription, paiement répété après suppression du cache HTTP, chiffrement/altération/époque/ordre et migration/concurrence du journal. Ils ne remplacent pas un essai sur deux téléphones Airtel/Orange/Vodacom, le réseau public, le Keystore d’un téléphone physique ou Doze.

Le relais fourni doit être déployé et son adresse renseignée avant un usage à distance réel. Aucun domaine, compte Firebase ou hébergement actif inventé. Cette livraison ne prétend pas remplir une architecture WebRTC/FCM/multimaster intégrale : elle ajoute un chemin longue portée opérationnel dans le code tout en conservant les règles actuelles et leurs limites hors ligne.


## Mise à jour Android sans perte de données

L’APK doit être signé avec la **même clé** que la version déjà installée. Les secrets GitHub `SCOLIA_ANDROID_KEYSTORE` (keystore en base64) et `SCOLIA_ANDROID_KEYSTORE_PASS` permettent une signature stable. Sans ces secrets, la compilation utilise une clé de développement du runner : ne pas supposer qu’une mise à jour sur un ancien APK sera acceptée. **Ne pas désinstaller une application contenant des données pour contourner une erreur de signature.** Sauvegarder depuis SCOLIA et faire signer la nouvelle version avec la clé d’origine. Le Keystore Android contenant les secrets locaux est supprimé lors d’une désinstallation.
