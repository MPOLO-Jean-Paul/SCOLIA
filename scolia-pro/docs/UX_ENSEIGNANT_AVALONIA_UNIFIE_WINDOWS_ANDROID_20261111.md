# SCOLIA Pro — compte enseignant, interface Avalonia unifiée

## Demande et choix de design

L'espace enseignant sur **Android** doit reprendre **exactement le même langage visuel**
que les comptes administrateur et secrétariat de SCOLIA Avalonia. Aucune maquette
ou illustration générée, aucun nouveau thème, aucune identité visuelle parallèle.

Composants natifs déjà fournis par `Themes/Controls.axaml` et `Themes/Phone.axaml` :
- `phonegroup` pour les conteneurs ;
- `groupcaps` pour les rubriques ;
- `settingsentry` pour les actions ;
- `appicon medium` pour les icônes harmonisées ;
- `field`, `fieldlabel`, `ComboBox`, `ListBox` et `DataGrid` pour les saisies.

## Écrans livrés dans cette PR

### Mon espace enseignant
- En-tête de compte en `phonegroup` avec rôle et salutation, sans décoration artificielle.
- Boutons **Encoder mes cotes** / **Mon code d'accès** en deux lignes tactiles
  au lieu de deux boutons débordant horizontalement.
- Statistiques sous forme des mêmes lignes que sur l'accueil du secrétariat.
- Cours en lignes tactiles : titre, promotion, période, nombre de copies,
  statut et barre de progression.
- Annonces et six règles d'encodage en lignes du thème existant.
- Le code provisoire bloque toujours l'encodage ; la sélection de son code
  reste accessible.

### Mes cours et cotes
- Les promotions et natures de cote sont des champs Avalonia plein écran.
- Les cours sont des items sélectionnables de `ListBox`, avec l'état de
  sélection déjà défini par le thème SCOLIA.
- La feuille anonyme conserve `DataGrid`, la saisie de cote et la retenue.
- Sur téléphone enseignant, la feuille n'affiche ni le nom ni le matricule :
  le code anonyme reste l'identifiant à l'écran.
- **Aucune modification** des API d'encodage, des règles de jury, des calculs
  ni des autorisations d'accès.

### Menu et navigation
- **Aucune nouvelle interface pour le menu** : le menu du compte enseignant
  reste le même `PhoneFrame.axaml` partagé avec le secrétariat et l'administrateur.
- Les onglets et accords du menu restent identiques.
- Aucune modification des styles globaux `Themes/Controls.axaml` et
  `Themes/Phone.axaml`.

## Protection Windows, ITM/IM et distribution
- La vue existante `TeacherHomeView` est préservée derrière `NotPhone`.
- La grille de saisie Windows et celles du secrétariat sont préservées derrière
  `UseClassicMarksLayout`.
- Android enseignant utilise `IsTeacherPhone` pour isoler le changement.
- Les bases de données et migrations ne sont pas touchées par ce lot UI.
- Les nouveaux profils des autres établissements restent désactivés.
- Pas de version iOS, ni de publication automatique depuis cette PR brouillon.

## Contrôles prévus
- Compiler le client Avalonia, Windows et Android.
- Passer les tests du domaine, de la navigation, de l'anonymat,
  des liaisons OneWay et de la compatibilité des écrans existants.
- Tester visuellement les téléphones compacts et le thème sombre.
- Fusion uniquement après validation.
