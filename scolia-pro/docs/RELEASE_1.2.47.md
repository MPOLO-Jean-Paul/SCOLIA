# SCOLIA Pro 1.2.47 — reprise de données et administration de licences

## Reprise automatique
- Points de reprise durables dans la base locale chiffrée, par compte, établissement et révision.
- Relance après retour du réseau, reconnexion ou réouverture, sans recommencer inutilement les ressources déjà validées.
- Réconciliation des copies modifiées sans supprimer l'outbox, les écritures en attente ni les comptes autorisés.
- Reprise programmée sous Android dans les limites d'exécution du système.

## Licence de l'établissement serveur
- Paramètres > **Licence SCOLIA Pro** sur le serveur ; consultation du statut et des jours restants.
- Badge compact de licence dans la barre d'état Windows.
- Console propriétaire JP-Services séparée des données scolaires : établissements, serveurs, dernière connexion, durée d'essai, échéances et dérogations.
- Mode gratuit par défaut. Facturation activable ultérieurement dans la console ; lecture seule manuelle ou automatique pour un établissement licencié.
- Le serveur SCOLIA applique l'interdiction des POST/PUT/PATCH/DELETE de données scolaires, pour tous les clients ; connexion et consultation restent possibles ; aucune suppression des données.
- Vérifications d'autorisation : les actions administratives de licence et d'approbation des relais requièrent une connexion locale au serveur principal.
- Cache protégé du statut : les interruptions temporaires du service central n'interrompent pas immédiatement les écoles.

## Accès distant par Internet
- **Activer** ou **Désactiver** dans la page réseau ; le fournisseur est configuré depuis la console JP-Services.
- L'appareil serveur présente et confirme le fournisseur avant d'utiliser un nouveau relais. L'adresse technique et les secrets ne sont pas modifiables par les postes scolaires.
- Le partage Wi-Fi fonctionne indépendamment des relais et de la télémétrie commerciale.
- Seules les métadonnées de l'établissement serveur sont collectées après information et accord ; jamais les comptes internes, élèves, paiements ou documents.

## Déploiement
- Android versionCode **167**, version affichée **1.2.47** ; même identité de signature officielle que les versions 1.2.17/1.2.18.
- La **console propriétaire** est livrée sous `deploy/owner-console/` mais **n'est pas automatiquement hébergée** : nécessite une base Cloudflare D1, un Worker HTTPS, un jeton administrateur secret, les migrations et la configuration de l'URL du service dans le serveur SCOLIA.
- Tant que l'URL du service central n'est pas configurée, les établissements restent hors facturation et la console ne reçoit aucun événement.
- Les modifications du relais dans la console ne nécessitent pas de nouveau binaire ; les changements de code utilisent GitHub Actions. Ne jamais inscrire de clé privée ni de jeton dans GitHub ou l'APK.
