# SCOLIA Pro — plan technique de finition UX/UI native
**Cible :** application métier réellement utilisable sur Windows et Android 16, sans données fictives ni rupture de la logique scolaire.

## Base et principes
- Code natif actuel : **C#/.NET, Avalonia** pour les écrans Windows et Android, **Android Views/Drawable** pour le démarrage avant Avalonia, API scolaire distincte.
- Sources de vérité : logo logiciel vectoriel `ScoliaMark` (palette et drawable natif), logo de l'établissement exclusivement issu de sa configuration ; **jamais** un logo d'établissement codé dans les ressources.
- Comportements : navigation courte ; hiérarchie informationnelle ; contrastes de textes et de focus ; grande cible tactile ; aucune animation bloquante ; animations désactivables ; respect de l'échelle de caractères et de l'orientation.
- La référence Android 16 concerne l'ergonomie, les surfaces et les gestes ; Windows conserve ses contrôles et comportements système. Pas de duplication esthétique aveugle d'un OS.

## Lot 1 — navigation, paramètres et démarrage (implanté sur cette branche)
| Fichier | Travail effectué | Critère de vérification |
|---|---|---|
| `src/Scolia.Bureau/ViewModels/Administration.cs` | Choix des paramètres en deux niveaux, ouverture ciblée d'une rubrique, action Retour | Aucune liste déroulante mobile imposée ; rubriques cliquables |
| `src/Scolia.Bureau/Views/SettingsView.axaml` | Accueil des réglages par familles, pictogrammes de couleur, détails individuels | Affichage mobile 360–600 dp et bureau 1366×768 sans débordement |
| `src/Scolia.Mobile/Resources/drawable/scolia_mark.xml` | Tracé original du logo SCOLIA en Android VectorDrawable | Même géométrie que `Themes/Palette.axaml` |
| `src/Scolia.Mobile/StartupScreen.cs` | Remplacement de l'initiale par le logo, apparition légère respectant la réduction des animations | Pas de logo d'établissement fantôme ; aucun délai artificiel |
| `src/Scolia.Mobile/Resources/drawable/lancement.xml` | Fond et vrai logo avant l'ouverture de l'activité | Pas d'écran blanc sans marque sur Android antérieur à 12 |
| `src/Scolia.Mobile/Resources/values-v31/styles.xml` | Logo du lancement système Android 12+ | Continuité visuelle avant/après création de l'activité |
| `src/Scolia.Bureau/Views/MainView.axaml` | Logo SCOLIA dans les états « vérification » et « ouverture », selon la palette claire/sombre | Aucun chevauchement ni contraste faible |
| `tests/Scolia.Bureau.Tests/SettingsNavigationTests.cs` | Tests de l'accueil et des détails des paramètres | La navigation desktop reste intacte |

## Lot 2 — composants et navigation transversaux
| Fichier | Travail à contrôler/améliorer |
|---|---|
| `src/Scolia.Bureau/Themes/Palette.axaml` | Jetons lumineux et sombres : surfaces, couleurs d'action, contraste AA, typographie |
| `src/Scolia.Bureau/Themes/Controls.axaml` | Champs, grilles, cartes, boutons, menus : focus clavier et états sélectionnés |
| `src/Scolia.Bureau/Themes/Phone.axaml` | Cibles tactiles, navigation inférieure, feuilles d'action, transitions sobres |
| `src/Scolia.Bureau/ViewModels/FeatureVisuals.cs` | Table sémantique d'icônes : éliminer la réutilisation confuse et vérifier les packs |
| `src/Scolia.Bureau/Views/PhoneFrame.axaml(.cs)` | Navigation par métier, états, retour Android, conservation du défilement |
| `src/Scolia.Bureau/ViewModels/Shell.cs` | Chargements incrémentaux, mémorisation de la navigation et absence de rafraîchissements inutiles |
| `src/Scolia.Bureau/Views/MainView.axaml.cs` | Défilement, redimensionnement, transitions courtes, absence de capture bitmap Android |

## Lot 3 — écrans métier complets
| Fichiers | Priorité de vérification |
|---|---|
| `Views/HomeView.axaml`, `Views/Phone/HomeView.axaml`, `Views/DashboardView.axaml` | Indicateurs, actions rapides et chargement progressif |
| `Views/StudentsView.axaml`, `Views/StudentDetailView.axaml`, `Views/FormView.axaml` | Inscription et recherche, champs adaptatifs et erreurs accessibles |
| `Views/CashierView.axaml`, `Views/BatchPaymentView.axaml`, `Views/ReceiptsView.axaml` | Montants, mois, dettes et boutons de confirmation non masqués |
| `Views/GradesView.axaml`, `Views/AttendanceView.axaml`, `Views/DisciplineView.axaml` | Listes lisibles, filtres et actions contextuelles |
| `Views/ReportsView.axaml`, `Views/Phone/ReportsView.axaml`, `Views/DataView.axaml` | Exports, progression, sauvegarde et erreurs présentées sans perte de saisie |
| `Views/SetupView.axaml`, `Views/Phone/SetupView.axaml`, `Views/LoginView.axaml` | Premier lancement direct, progression réelle, connexion sans clignotement |

## Lot 4 — validation, sécurité et distribution
- Exécuter les tests domaine/API ainsi que les tests de navigation, écrans mobiles et bureau, contraste, mise en page et migrations.
- Générer des captures **360×640**, **412×915**, **tablette**, **1366×768** et texte agrandi. Vérifier clavier ouvert, grands caractères, thème sombre et paysage.
- Compiler l'APK Android et le paquet Windows via GitHub Actions ; exécuter les tests de démarrage.
- Ne pas modifier les règles métiers, le schéma de données ni les paiements en tant que simple conséquence de cette refonte. Conserver une copie de sécurité avant toute mise à jour.
- Livrer les binaires uniquement après réussite de la compilation et des tests. La validation visuelle sur un vrai téléphone reste nécessaire.
