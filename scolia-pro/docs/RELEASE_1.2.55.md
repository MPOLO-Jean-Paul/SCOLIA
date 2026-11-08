# SCOLIA Pro 1.2.55 — sauvegarde automatique en ligne et relève du serveur

## Ce qui change pour l'école
- **Données › Sauvegarde en ligne** : le responsable l'active une fois sur l'ordinateur principal, note le **code de secours**, et tout se fait ensuite seul.
- **Sauvegarde automatique, sans intervention** : toutes les heures si des données ont changé, et au moins une fois par jour, une copie cohérente de la base est compressée, **chiffrée sur place avec la clé de l'école** (AES-256-GCM) et envoyée à la console JP-Services (Cloudflare R2). L'empreinte du fichier est vérifiée à l'arrivée. Rétention : 24 copies sur 48 h, une par jour sur 30 jours, une par mois sur 12 mois.
- **Relève quand le serveur local tombe** : téléphones et postes continuent de travailler ; chaque opération gardée est aussi **déposée en ligne**, chiffrée pour le serveur. Elle est en sécurité même si l'appareil est perdu. La barre d'état indique « N en attente, M en sécurité en ligne ».
- **Retour du serveur local** : toutes les 2 minutes, il relève la boîte en ligne et **complète sa base** : chaque opération est appliquée avec ses règles habituelles et les droits de son auteur, une seule fois. L'appareil apprend qu'elle est faite et la retire de sa file ; s'il la renvoie quand même, aucun doublon (clé d'idempotence, désormais gardée 90 jours).
- **Opérations refusées** (mois déjà payé ailleurs, compte désactivé…) : listées avec leur motif sur la page, jamais perdues en silence.
- **Nouvel ordinateur après une panne ou un vol** : JP-Services crée une invitation de reprise ; le nouveau serveur récupère une copie en ligne avec le code de secours (elle arrive dans « Sauvegardes », prête à être restaurée). L'ancien serveur est révoqué.

## Console JP-Services 0.2.0
- Sur le même déploiement Cloudflare (même base D1) : nombre d'écoles protégées, copies en retard (> 26 h), volume stocké, opérations en attente ; par école, état, dernière copie, présence du serveur et invitation de reprise.
- Le contenu reste illisible pour JP-Services : ni clé, ni lien de téléchargement.

## Sécurité
- Clé de sauvegarde générée sur le serveur, protégée par le système, jamais transmise ; le code de secours ne s'affiche que sur l'ordinateur principal (consultation tracée dans le journal d'audit).
- Les appareils reçoivent seulement une clé de dépôt et une délégation signée par le serveur (30 jours) ; le jeton de dépôt ne permet aucune lecture.
- Au relevé, une opération sans délégation valide, d'un compte désactivé ou modifié, ou hors des écritures du quotidien (comptes, réglages, annulations, effacements…) est refusée.
- Le rejeu interne utilise un secret tiré au hasard à chaque démarrage, accepté seulement depuis l'ordinateur lui-même.
- La licence en lecture seule ne bloque jamais la sauvegarde ; les opérations en attente restent dans la boîte jusqu'à la réactivation.

## Contrôles
- Console : 4 nouveaux tests (activation, envoi vérifié, cloisonnement entre écoles, dépôt/relevé/issue, reprise concurrente, rétention).
- Serveur : 3 nouveaux tests d'intégration avec une fausse console (chiffrement et récupération, application unique au nom de l'auteur, refus des opérations frauduleuses, code de secours).

## Distribution
- Android : **1.2.55**, **versionCode 175** ; signature officielle inchangée.
- Activation côté JP-Services : relancer le workflow « publier console JP-Services » (le jeton Cloudflare doit avoir « Workers R2 Storage : Edit »).
