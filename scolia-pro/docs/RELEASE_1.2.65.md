# SCOLIA Pro 1.2.65 — Accueil enseignant et connexion locale

## Windows et Android uniquement

La version conserve Avalonia/C# et les composants natifs du secrétariat, sans ajouter de moteur Flutter ni de cartes de type Web.

### Espace enseignant
- **Android :** l'accueil utilise désormais les mêmes `phonegroup`, lignes `settingsentry`, pictogrammes, fonds et séparateurs que l'accueil du secrétariat.
- L'identité, les deux actions (Encoder mes cotes et Mon code d'accès), les quatre indicateurs et les cours s'affichent dans des groupes compacts.
- Les détails des cours, la progression, le statut, les annonces et les règles d'encodage restent accessibles.
- **Windows :** l'accueil conserve la présentation bureau. Le contenu des deux boutons est désormais une chaîne de texte, ce qui empêche l'affichage erroné `Avalonia.Controls.TextBlock`.

### Navigation Android
- Libellé « Mes cours et cotes » abrégé en **« Cours & cotes »** dans la barre inférieure.
- Le bouton Menu reçoit la même largeur et le même indicateur de sélection que les onglets du rôle.
- Icônes et textes actifs/inactifs utilisent la même palette ; espaces et alignements sont resserrés.

### Connexion hors ligne
- Le rappel devient un bandeau discret et la phrase est limitée à **« Hors ligne · Connectez-vous localement »**.
- Les erreurs restent explicites et les identifiants locaux/synchronisation continuent de fonctionner comme auparavant.

### Validation et distribution
- Version Android **185**, affichage **1.2.65**, installateur Windows 1.2.65.
- Tests `TeacherPhoneAndOfflineUxTests` plus tests natifs et SCOLIA Pro existants.
- Compilation/signature Android officielle via certificat historique de la PR #18, jamais une clé temporaire. Pas d'iOS.
