# SCOLIA Flutter — migration Android

Module de présentation Flutter Material 3, distinct du client Windows. **Adaptateur C# de commandes ajouté ; liaison Android/JNI au moteur Flutter non raccordée à ce stade. Aucune version de production.** Pas de données fictives en production, ni authentification Dart parallèle. Les actions restent bloquées en absence de pont.

## Architecture retenue
- Flutter : présentation, navigation et formulaires.
- Moteur .NET existant : serveur embarqué, API, session et autorisations, SQLite, outbox, relais, identité du poste, biométrie et mises à jour.
- Contrat MethodChannel versionné : `com.jpservices.scolia/mobile/v1`. Les méthodes enum Operation transmettent des commandes, pas des accès SQL ou HTTP arbitraires. Réponse : protocol=1, stage, school, user, year, modules (autorisés), notice. Le moteur doit contrôler les autorisations à chaque commande, et ne jamais renvoyer de cookie, de secret d'appareil ou de mot de passe.
- À réaliser : preuve d'intégration AAR dans l'hôte .NET Android, raccordement Android/JNI du MethodChannel à l'adaptateur FlutterCommandAdapter et gestion du cycle de vie, écrans de détail et formulaires métier Flutter, export de fichiers, synchronisation et reprise après interruption, validation téléphone serveur et client, tests de mise à jour depuis 1.2.99 avec données et clé officielle inchangées.

## Validation
Le workflow Flutter analyse le module, exécute les tests et construit un AAR non publiable seul. Il ne remplace pas l'APK Android officiel. Le package Android officiel reste com.jpservices.scolia ; le module a son propre espace de noms. Ne pas publier de nouvel APK avant validation complète de la compatibilité et du stockage. Aucun changement iOS ou Windows.

Documentation : https://docs.flutter.dev/add-to-app/android/project-setup

## Adaptateur C#
`Scolia.Bureau/Infrastructure/FlutterCommandAdapter.cs` utilise la session Shell, les commandes de connexion, de récupération et les droits existants. Les écrans métier non migrés, la configuration et le verrouillage nécessitent un retour explicite à la vue native par le futur hôte Android. Le statut `nativeScreen` ne signifie pas que cet hôte est déjà en service. Les opérations de connexion sont sérialisées et les secrets de formulaire effacés. Aucune interface réseau ou accès SQL supplémentaire. Le SDK 3.44.0 est fixé pour rendre la première validation reproductible ; la migration vers les nouveaux packages Material sera traitée séparément.

## Premier écran métier Flutter
Registre en lecture seule, recherche explicite et pagination. L'adaptateur appelle StudentsPage (cache local inclus) et revérifie la session après chargement ; seules les données nécessaires à la liste sont transférées. Fiche détaillée, écritures et filtres avancés restent à migrer. Les données de test ne sont utilisées que par les tests de widgets.

## Suivi des paiements
Écran Flutter : période, promotion, état, recherche, pagination de 30 lignes, export Excel/PDF/Word de la sélection entière. Le moteur réutilise la même instance MonthlyPage confirmée pour l'export (empreinte des identifiants déjà contrôlée côté serveur). Pas d'écriture financière nouvelle ; le raccordement Android effectif reste requis.
