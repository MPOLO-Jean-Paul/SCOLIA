# SCOLIA Pro 1.2.20 — détecter les nouvelles mises à jour jusqu'à installation

## Comportement de l'application Android

1. À chaque retour dans SCOLIA, la vérification de l'APK téléchargé reste **immédiate** et peut proposer « Installer » sans attendre Internet.
2. **Même si** une mise à jour est téléchargée ou en cours de téléchargement, SCOLIA poursuit la recherche de nouvelles versions GitHub Releases.
3. Le bouton « Vérifier les mises à jour » interroge toujours GitHub (sans cache) ; une mise à jour locale en attente ne l'interrompt plus.
4. Lorsqu'une version plus récente que celle téléchargée est publiée, SCOLIA la reconnaît et signale sa disponibilité, sans supprimer ni remplacer le téléchargement déjà en attente.
5. Un nouveau téléchargement n'est commencé **qu'après consentement explicite**. Lorsque le nouveau DownloadManager a reçu sa tâche, les fichiers et la notification de l'ancien téléchargement peuvent être nettoyés.
6. « Plus tard » diffère les popups pour limiter les interruptions, mais laisse l'information accessible depuis les notifications Android et ne désactive jamais les contrôles périodiques.
7. Quand l'APK est téléchargé, une notification d'installation reste présente jusqu'à l'installation ou le remplacement du téléchargement. Android exige toujours sa confirmation d'installation et, si nécessaire, la permission d'installer depuis SCOLIA.
8. Après l'installation, SCOLIA constate la version installée et nettoie les anciennes notifications ainsi que le fichier temporaire.
9. Si GitHub est momentanément inaccessible, un APK déjà téléchargé reste installable localement.

## Fréquence et limites Android

- Ouverture/reprise de l'application : consultation limitée à une fois par **15 minutes** par défaut ; bouton manuel toujours sans cache.
- Android WorkManager : contrôle périodique de **6 heures** sur un réseau disponible. Android peut différer l'exécution (Doze, économie de batterie, appareil éteint), donc il ne s'agit pas d'une garantie en temps réel.
- Aucun téléchargement ni installation silencieux : accord utilisateur obligatoire.
- Les APK proviennent des Releases officielles, avec SHA-256 vérifié et contrôle de l'identifiant d'application, du numéro de version et de la signature par Android.
- Si les notifications sont désactivées par l'utilisateur, l'interface et le retour au premier plan restent disponibles pour informer et proposer l'installation.
- Aucun remplacement des données scolaires ou des paramètres de jumelage pendant la mise à jour.

## Tests de non-régression

- Une version GitHub 1.2.20 est reconnue même si l'APK 1.2.19 est en attente.
- Le même numéro téléchargé ne provoque pas un deuxième téléchargement.
- Une version antérieure ne remplace jamais une version plus récente en attente.
- Les APK corrompus, expirés ou incohérents sont refusés.
- Les rappels ne perturbent pas la navigation ou le service Android en arrière-plan.

Publication depuis la branche officielle avec la signature Android historique vérifiée et les empreintes SHA-256. Les tests CI sont nécessaires, mais n'excluent pas de vérifier ensuite le parcours sur un véritable appareil Android 16.
