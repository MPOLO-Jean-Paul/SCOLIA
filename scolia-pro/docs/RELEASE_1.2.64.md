# SCOLIA Pro 1.2.64 — Affinage natif avant l'accueil et uniformité enseignant

## Périmètre : Android et Windows uniquement

La version 1.2.64 conserve le design natif **Avalonia/C#** de la 1.2.63, sans ajouter Flutter, WebView ni cartes flottantes. Elle harmonise les alignements et les détails visuels des pages qui précèdent l'accueil.

### Connexion et accès
- Connexion, récupération du mot de passe, verrouillage et choix du serveur : même largeur utile de formulaire (420 unités, 440 sur desktop large), champs et boutons principaux de 52 unités, rayons cohérents et rythme vertical régulier.
- Hiérarchie des titres et logo calibrée ; champs/labels/liens plus nettement alignés, y compris avec le clavier virtuel.
- Palette dynamique SCOLIA (accent, texte et fonds des champs) en mode clair comme sombre ; aucun bleu fixe nécessaire aux actions principales.
- Les classes d'adaptation `compact`, `tiny`, `short` et `micro` restent actives ; aucune nouvelle carte ou page Android défilante.
- Sur Android, entête du jumelage, saisie de code, serveur Wi-Fi et navigation inférieure utilisent une grille de marges commune.
- La configuration initiale Android reprend les sections plates de l'accès natif et abandonne ses anciens panneaux blancs avec ombre.

### Enseignants
- L'accueil enseignant partage les ressources `desktopwelcome`, `heroaction` et `desktopgroup` avec les autres espaces.
- Même densité de contenu que le secrétariat ; meilleur retour à la ligne des actions et des noms.
- La gestion des comptes enseignants conserve les tableaux, filtres et fiches du kit commun ; l'encart temporaire d'identifiants s'adapte aux écrans étroits.
- Droits, cours, notes, fichiers, authentification, biométrie, hors ligne et synchronisation inchangés.

### Vérifications de publication
- Tests `NativePreHomePolishTests` et suite existante sur responsive, thème sombre, confidentialité, mises à jour, navigation, connexion et métier.
- Vérifier les captures Android 320×568 et Windows 460×640 / 800×600.
- Ne publier que l'APK Android **signé avec le certificat historique PR #18**, plus l'installateur Windows x64. Aucun paquet iOS.
