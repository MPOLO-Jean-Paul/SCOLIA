# SCOLIA Pro — déploiement du relais sur Render

## Ce qui est déployé

Seul le projet `src/Scolia.Relay` est hébergé par Render. Il expose `/health` et les WebSockets `/v1/host/{room}` et `/v1/client/{room}`. C'est un **relais WSS chiffré applicativement** ; le code actuel **n'utilise pas WebRTC/STUN/TURN**. Le trafic chiffré passe par le relais, mais les dossiers scolaires, les mots de passe et la base SQLite ne sont pas conservés par Render. L'appareil principal doit rester joignable, faute de quoi les opérations restent en attente localement.

Le Dockerfile existant est dans `scolia-pro/deploy/relay/Dockerfile`. Render termine TLS et le conteneur écoute sur le port HTTP interne 8080 : **Caddy n'est pas requis sur Render**.

## Procédure de mise en service

1. Vérifier et intégrer les fichiers `render.yaml` et ce guide dans la branche de travail choisie. Attention : la branche `main` actuelle ne contient pas le code source de SCOLIA Pro.
2. Connecter le compte Render au dépôt GitHub `MPOLO-Jean-Paul/SCOLIA`. Depuis le tableau de bord Render : **New → Blueprint**, choisir le dépôt et la branche où se trouve `render.yaml`.
3. Avant de déployer, contrôler la branche `branch` du service dans `render.yaml` : elle doit correspondre aux sources SCOLIA Pro que l'on souhaite exécuter. Les chemins Docker sont relatifs à la racine du dépôt et le contexte Docker est `scolia-pro`.
4. Confirmer explicitement la création du service Render. Le plan `free` est réservé aux tests ; il peut se mettre en veille après 15 minutes d'inactivité, subir des redémarrages et met du temps à se réveiller.
5. Après le déploiement, ouvrir **l'URL HTTPS réellement attribuée par Render**, suffixée de `/health`. Attendre une réponse JSON indiquant `service: "SCOLIA relais"` et `protocol: 1`.
6. Sur l'appareil principal, ouvrir **Réseau et activité** et saisir l'adresse `wss://<nom-reel-du-service>.onrender.com/` dans le réglage du relais. Créer/renouveler la liaison, puis transmettre l'invitation **par canal privé** au second appareil. Sur celui-ci, choisir **Rejoindre à distance**, saisir l'invitation et s'authentifier avec le compte SCOLIA existant.
7. Tester la liaison Android ↔ Windows sur réseaux différents, puis un passage hors ligne et une reconnexion. Vérifier la reprise sans doublon de paiement et la conservation du journal local. Le test `/health` seul ne garantit pas la synchronisation métier.

## Sécurité et exploitation

- Ne jamais renseigner des jetons, invitations ou secrets d'établissement dans `render.yaml` ou dans les journaux publics.
- Le relais actuel garde les connexions et les salles **en mémoire du seul processus** : ne pas augmenter le nombre d'instances sans concevoir un routage partagé garantissant que chaque hôte et son client rejoignent la même instance.
- Les redéploiements et maintenances interrompent les WebSockets ; conserver la reconnexion exponentielle prévue côté clients. Un redéploiement ne doit jamais supprimer la file SQLite locale.
- Ne pas confondre ce relais avec un service P2P direct : un relais Render transporte effectivement les trames chiffrées, ce qui peut consommer de la bande passante facturable.
- Les appareils doivent continuer de conserver leurs clés privées et leur base SQLite ; **ne pas déployer `Scolia.Api` sur ce serveur public**.
- Pour un usage scolaire réel exigeant une disponibilité continue, après les tests, passer à un plan d'instance payant sans mise en veille ; le choix du plan et les frais reviennent au propriétaire du compte Render.

## Contrôles avant production

Validation GitHub du Dockerfile, compilation .NET 10, disponibilité `/health`, connexions WSS valides/invalides, rate limiting, permission des appareils, isolation inter-établissements, interruptions réseau, reprises des paiements. Aucun de ces tests de bout en bout ne peut être tenu pour réussi avant l'exécution sur un véritable service Render.

Sources Render : https://render.com/docs/blueprint-spec et https://render.com/docs/websocket et https://render.com/docs/free .
