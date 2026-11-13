# SCOLIA Pro 1.2.68 — Icônes de l'aperçu enseignant et cohérence des menus

## Correction confirmée à partir de la capture Android 1.2.67

- **Cause réelle** : les quatre statistiques de l'accueil enseignant utilisent le modèle `KpiVm` dont la propriété couleur s'appelle `Accent`. La vue recherchait `Brush`, propriété inexistante. La liaison de fond échouait et laissait apparaître des pictogrammes blancs sur un fond blanc.
- **Correction** : la carte « Aperçu » lie maintenant le fond à `Accent`, affiche un pictogramme blanc sur les tons opaques prévus par `IconPalette` et évite une seconde modification concurrente des icônes par `TileGlyph` dans cette carte précise.
- **Tests de non-régression** : contrôle du vrai contrat `KpiVm.Accent`, des attributs XAML et de l'absence de liaison fautive `{Binding Brush}` dans la carte. Les autres tuiles SCOLIA conservent leurs propres styles.

## Organisation cohérente Windows et Android

- Les commandes **Cycles scolaires** et **Organisation LMD** rejoignent le groupe **Organisation pédagogique**, rattaché au bloc **Gestion scolaire** ; elles ne sont plus mélangées aux préférences techniques.
- Les permissions existantes `SettingsWrite` restent indispensables à leur utilisation. Les anciens établissements ITM/IM ne reçoivent pas de commandes pédagogiques incompatibles avec leur profil.
- Le groupe **Réseau et postes** possède désormais son icône réseau dédiée ; **Organisation pédagogique** utilise un symbole scolaire, sans changement de comportement des fonctions.
- Un test contrôle l'affectation unique des rubriques à leurs blocs et l'absence de duplication entre scolarité et paramètres.

## Intégrité et distribution

- Version **1.2.68** (Android versionCode **188**, Windows **1.2.68**).
- Pas de changement des données pédagogiques, de la base SQLite, des notes, des paiements, des comptes, de la signature Android ni des mécanismes de synchronisation.
- APK signé selon le procédé de signature historique PR #18 ; installateur Windows sans prétendre à une signature Authenticode.
- Publication après compilation, tests et vérification SHA-256 de l'APK et de l'installateur Windows.

