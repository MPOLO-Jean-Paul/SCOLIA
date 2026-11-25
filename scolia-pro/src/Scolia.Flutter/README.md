# SCOLIA Flutter — migration Android

Module de présentation Flutter Material 3, distinct du client Windows. **Non raccordé au moteur natif à ce stade ; aucune version de production.** Pas de données fictives en production, ni authentification Dart parallèle. Les actions restent bloquées en absence de pont.

## Architecture retenue
- Flutter : présentation, navigation et formulaires.
- Moteur .NET existant : serveur embarqué, API, session et autorisations, SQLite, outbox, relais, identité du poste, biométrie et mises à jour.
- Contrat MethodChannel versionné : `com.jpservices.scolia/mobile/v1`. Les méthodes enum Operation transmettent des commandes, pas des accès SQL ou HTTP arbitraires. Réponse : protocol=1, stage, school, user, year, modules (autorisés), notice. Le moteur doit contrôler les autorisations à chaque commande, et ne jamais renvoyer de cookie, de secret d'appareil ou de mot de passe.
- À réaliser : preuve d'intégration AAR dans l'hôte .NET Android, implémentation du pont natif avec le cycle de vie existant, écrans de détail et formulaires métier Flutter, export de fichiers, synchronisation et reprise après interruption, validation téléphone serveur et client, tests de mise à jour depuis 1.2.99 avec données et clé officielle inchangées.

## Validation
Le workflow Flutter analyse le module, exécute les tests et construit un AAR non publiable seul. Il ne remplace pas l'APK Android officiel. Le package Android officiel reste com.jpservices.scolia ; le module a son propre espace de noms. Ne pas publier de nouvel APK avant validation complète de la compatibilité et du stockage. Aucun changement iOS ou Windows.

Documentation : https://docs.flutter.dev/add-to-app/android/project-setup
