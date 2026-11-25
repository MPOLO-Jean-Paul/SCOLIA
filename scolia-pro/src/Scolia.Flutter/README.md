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

## Transport Android — étape d'intégration
Le canal utilise JSONMethodCodec des deux côtés. `android_bridge` contient un canal Java réel pour FlutterEngine et un script qui compile le JAR contre l'embedding référencé par le POM du module AAR. `FlutterJsonDispatcher` valide les arguments et sérialise les réponses de l'adaptateur en camelCase. Le canal est limité à une commande simultanée, n'affiche pas les exceptions internes et rejette les réponses tardives après fermeture. Il ne crée aucune session ni accès réseau indépendant. Il reste à intégrer le JAR et les dépendances Flutter dans le projet Android .NET, connecter le callback au dispatcher Avalonia, afficher la vue Flutter et valider le cycle de vie sur appareil. La compilation de l'APK officiel reste inchangée.

### Liaison gérée expérimentale
`Scolia.Flutter.AndroidBinding` génère les bindings .NET à partir du JAR Java et expose `ChannelConnection`. `Scolia.Mobile/FlutterIntegration.cs` connecte le callback au dispatcher Avalonia et à l'adaptateur existant, uniquement sous `ScoliaFlutterExperiment=true`. Cette option ne démarre pas encore un moteur ni une vue Flutter et ne constitue pas un APK utilisable. Le workflow compile la bibliothèque de liaison, sans toucher à la signature, au package ou au stockage du client de production. Prochaine validation : empaquetage de toutes les dépendances de l'embedding, création d'un moteur Flutter dans le processus Android et attachement de la vue avec gestion du verrouillage, du clavier et des retours natifs.

## Hôte Android expérimental
Le code de l'activité conserve le Shell et les services existants. Le moteur Flutter s'affiche en superposition seulement après démarrage, sur un écran sûr ; le verrouillage, les écrans natifs non migrés et la mise en arrière-plan retirent la vue. La configuration expérimentale est compilée par CI mais n'est ni signée avec la clé officielle ni publiée. La résolution des dépendances Android et les tests sur téléphone restent à valider. Les interactions natives complexes peuvent interrompre l'expérience Flutter ; le retour à Avalonia est volontaire jusqu'à migration complète.

## APK de validation isolé
Le workflow expérimental utilise `com.jpservices.scolia.fluttervalidation`, une autorité FileProvider propre et la signature de validation uniquement. Le client officiel conserve son identifiant, sa signature, son manifeste et ses données. Les mises à jour officielles sont désactivées pour ce paquet expérimental. Le contrôle de sortie vérifie l'identifiant, la signature, les assets Flutter et les bibliothèques natives. Ne pas connecter un établissement réel : utiliser une base et des comptes de test, sur un appareil ou un émulateur réservé à la validation. La validation de migration depuis l'APK officiel est une étape séparée, à effectuer avec des sauvegardes de test.

### Correction de résolution Maven
Le journal CI a confirmé une erreur HTTP 404 sur l'URL .aar de l'embedding. Le script réutilise désormais le cache Gradle de la version déclarée dans le POM ; en secours il essaie les extensions JAR et AAR, vérifie la présence de FlutterEngine et normalise l'archive de packaging. Les bibliothèques ABI utilisent le même résolveur. Le succès du nouveau workflow reste à confirmer.

### Cycle de vie du canal
L'adaptateur de commandes est conservé par Shell entre les recréations de la vue Flutter, afin de conserver le verrou des opérations en cours. Les réponses d'une vue Android fermée ne peuvent plus être envoyées à une nouvelle vue ni déclencher son retour natif. Le paquet expérimental conserve les mêmes options .NET sans AOT ni trimming que le client officiel afin de ne pas éliminer les composants du serveur embarqué. Ces corrections sont à valider dans la CI Android et sur appareil.

### Coexistence du paquet de validation
Le serveur local du paquet expérimental utilise le port 15080, tandis que le port de production reste inchangé. Un premier lancement sans configuration prépare un contexte local de validation au lieu de joindre automatiquement le premier établissement découvert. Les tests doivent néanmoins être réalisés sur un téléphone ou un réseau de test, sans compte ni données scolaires réels. La connexion volontaire à un serveur distant reste possible et n'est pas une isolation réseau totale. Les deux modifications sont réservées à la compilation SCOLIA_FLUTTER_EXPERIMENT.

## Couverture fonctionnelle du catalogue
L'accueil Flutter reçoit maintenant tout le catalogue métier autorisé par Sidebar (ITM/IEM, EPST, LMD et rôles spécifiques), avec les libellés réels et les groupes. Les modules non portés sont ouverts dans leur interface Avalonia opérationnelle ; aucune permission supplémentaire n'est créée. Inscription et accès à la fiche ou à l'encaissement d'un dossier visible utilisent les formulaires et commandes existants. Ce parcours hybride ne constitue pas encore la migration graphique complète : chaque formulaire et écran métier reste à porter en Flutter et à tester sur appareil.

## Continuité visuelle et métier
Flutter reprend les couleurs claires et sombres de Themes/Material3.axaml, les rayons des cartes (12) et champs (8), la présentation compacte de Phone/HomeView.axaml et les actions rapides autorisées. La carte d'accueil lit la véritable session et l'état du SyncCenter natif ; elle ne simule pas une connexion. Les modèles de calcul et d'export des cartes d'examen ne sont pas modifiés. Les autres pages gardent leur présentation native jusqu'à leur portage effectif. Les tests de compilation et de rendu sur téléphone restent indispensables pour confirmer la parité.

## Fiche scolaire Flutter
La fiche affiche les rubriques de StudentDetailPage : identité, inscriptions et, si PaymentsRead, indicateurs, échéances, reçus et prises en charge. Les données et montants sont fournis par le modèle métier existant. Le dossier doit appartenir à la dernière liste autorisée de la session ; les droits sont revérifiés après lecture. Les actions de modification, changement de statut, relevé et autres fonctions restent accessibles dans la fiche native, et l'encaissement utilise le parcours natif vérifié. Les tests Flutter couvrent l'affichage et le retour au registre. Aucun modèle PDF ni calcul n'a été changé.

## Inscription Flutter
Le formulaire Flutter est généré à partir de la même instance FormViewModel que la version native. La validation, les promotions actives, la terminologie institutionnelle, les champs EPST, le contrôle des doublons (confirmation sur la même identité) et la file hors ligne sont partagés. Le jeton de formulaire est lié à la session et invalidé lors de toute autre commande ; les données ne sont pas conservées par Flutter après fermeture. Si aucune promotion n'existe, l'assistant natif reste disponible. Le résultat exact du moteur distingue inscription acceptée et inscription en attente de confirmation serveur. Tests et validation sur appareil restent requis.
