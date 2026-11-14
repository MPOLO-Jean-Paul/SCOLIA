# SCOLIA Pro 1.2.72 — Correction réelle des champs de connexion Android et Windows

## Problème constaté sur Android
- Le champ « Mot de passe » apparaissait entièrement blanc sur la page cobalt : les caractères et les points du mot de passe étaient invisibles.
- La correction 1.2.71 changeait les styles du `TextBox`, mais ne garantissait pas la couleur du `Border#PART_BorderElement` créé par le modèle visuel Fluent d'Avalonia.

## Correctif 1.2.72
- Forçage d'un fond cobalt `#1845B1` et d'un texte blanc directement sur les champs, avec priorité sur les styles généraux.
- Forçage du même fond sur la bordure interne du template pour les états normal, survol, focus et désactivé.
- Indications des champs en bleu très clair, curseur blanc et maintien du contraste du mot de passe masqué ou affiché.
- Application aux cinq champs de connexion et récupération : identifiant, mot de passe, nouveau mot de passe, confirmation, code de secours.
- Même protection sur la saisie du code de déverrouillage ; exemption des champs spécifiques des styles métiers généraux.
- Conservation de l'interface native bleu ciel/cobalt et de l'ergonomie des petits écrans.


## Harmonisation de la page de connexion
- Bouton **Options** réduit, stylé dans les mêmes tons cobalt et intégré au panneau principal au lieu d'une grande boîte blanche isolée sous la carte.
- Actions avancées repliées au départ et toujours accessibles : changer de serveur, retirer la liaison locale, désactiver la biométrie.
- Marges, logo, titre, boutons secondaires et interlignes affinés ; tailles tactiles et adaptations aux petits écrans conservées.

## Vérification attendue
- Tests automatisés des valeurs explicites de couleur et des sélecteurs du template.
- Compilation des projets Avalonia, API et Android/Windows et publication uniquement si les workflows GitHub Actions réussissent.
- Validation visuelle finale sur un appareil Android réel : vérifier le fond bleu du mot de passe, les caractères en mode affiché et les points en mode masqué.
- Aucun changement de la base de données ou des comptes.

## Installation
- Version : `1.2.72` ; Android `versionCode` : `192`.
- Conserver la signature Android historique de la PR #18 ; ne pas désinstaller l'application existante.
- Sauvegarder la base principale avant toute mise à niveau du serveur Windows.
