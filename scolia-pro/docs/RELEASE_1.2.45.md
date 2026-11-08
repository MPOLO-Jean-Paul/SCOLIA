# SCOLIA Pro 1.2.45 — remplacement de l'écran de jumelage Android qui défile

## Correction structurelle
- Le premier écran AVANT la connexion a été entièrement remplacé **sur Android**.
- L'ancienne vue empilait QR, adresses réseau, recherche Wi-Fi et serveurs détectés dans un `ScrollViewer`. Replier les sections ne résolvait pas la hauteur excessive.
- Une interface native **sans ScrollViewer** affiche une seule étape à la fois, avec navigation fixe en bas :
  - **QR** : scanne directement le code du serveur ;
  - **Code** : importe une image ou permet de coller une invitation, avec validation inchangée ;
  - **Wi-Fi** : recherche un serveur, puis ouvre le choix des établissements ou l'adresse locale sur une page distincte.
- Une page d'état affiche le résultat complet et la phase de vérification après scan dispose d'un écran propre.
- Le mode « ce téléphone comme serveur » reste disponible en bas sur les écrans habituels.

## Adaptation aux petits téléphones
- Dimensionnement d'après la largeur et la hauteur **réellement disponibles**.
- Sous faible hauteur, l'en-tête décoratif et les éléments non indispensables s'effacent sans réduire le formulaire à une taille illisible.
- Aucun défilement de page n'est nécessaire dans la nouvelle vue de jumelage, y compris lorsque l'utilisateur change de méthode. La liste des serveurs est un sélecteur et non une liste verticale sans limite.
- Les autres pages (connexion à un compte, récupération, configuration longue) conservent leurs fonctions et les protections de sécurité existantes.

## Vérifications
- Nouveau test source `AndroidPreloginNoScrollTests` garantissant qu'aucune `ScrollViewer` ne réapparaisse dans ce parcours.
- Nouveau contrôle Avalonia headless `connexion-compacte` sur 320×420, 320×568, 360×640 et 412×892 : vérifie le cadre de chaque étape, le pied de page et l'absence de défilement. Intégré à la compilation GitHub Actions.
- Les tests et la compilation doivent réussir avant de distribuer la version.

## Publication et sécurité
- Version 1.2.45, Android `versionCode` 165.
- Aucun changement des comptes, de la base SQLite ou du protocole de jumelage.
- La signature officielle Android historique issue de la PR n°18 doit être conservée, sinon la mise à jour par-dessus la version installée est impossible.
