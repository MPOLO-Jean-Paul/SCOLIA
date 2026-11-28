# SCOLIA Pro 1.2.44 — pages d'accès adaptées à l'écran Android

## Démarrage, jumelage et connexion
- L'interface iOS claire garde le QR et les fonctions principales prioritaires, les options facultatives du jumelage sont repliées au premier affichage sur Android.
- Une classe d'adaptation `micro` est activée sous 570 points de hauteur utile : marges et cartes resserrées, éléments décoratifs supprimés sans masquer les champs, actions de sécurité ni contrôles de connexion.
- Connexion : « Autres options de connexion » regroupe le changement de serveur et les informations secondaires.
- Veille, récupération du mot de passe et configuration initiale : champs lisibles, coins arrondis sobres, titres adaptés, contrôles accessibles.
- Accueil Android : carte de salutation redimensionnée selon la largeur disponible.

## Accessibilité
Le défilement horizontal reste désactivé sur les écrans d'accès. Sur des formulaires longs ou lorsque le clavier occupe une part importante de l'écran, le défilement vertical reste autorisé : il évite de comprimer les textes et les zones tactiles.

## Continuité et sécurité
- Version 1.2.44, Android versionCode 164.
- Aucune modification des données, des comptes, de la biométrie, du jumelage ou des protocoles.
- Pour mettre à jour l'application installée, la signature APK officielle historique de la PR 18 doit être conservée et vérifiée par GitHub Actions.
