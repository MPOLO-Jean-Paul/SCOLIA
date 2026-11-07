# SCOLIA Pro 1.2.43 — écrans d'accès iOS sur le téléphone et l'ordinateur

## Ajustement à l'écran (téléphone)
- Premier écran (jumelage QR), connexion, récupération du mot de passe, session verrouillée et configuration de l'établissement tiennent dans la largeur de l'écran, sans défilement horizontal ni texte coupé, de 320 à 412 points de large.
- Les marges, le logo, les titres, les champs et les boutons suivent la taille de l'écran : petits téléphones (moins de 390 points), très petits écrans (moins de 340 points), écrans bas (clavier ouvert, paysage) et grands écrans.
- Les textes des boutons et des liens passent à la ligne au lieu de sortir de l'écran ; les cartes sont centrées verticalement quand la place le permet.
- Configuration sur le téléphone : en-tête clair iOS, champs côte à côte empilés sur écran étroit, boutons du bas à parts égales sans chevauchement.

## Style iOS aussi sur l'ordinateur
- Premier écran, connexion, récupération du mot de passe et session verrouillée reprennent sur Windows la page claire iOS du téléphone : carte arrondie, champs tactiles, bouton principal bleu, liens discrets, plus professionnelle.
- Le premier écran de l'ordinateur propose toujours « Configurer cet ordinateur comme serveur ».
- Sur l'ordinateur, le curseur se place directement dans l'identifiant, le mot de passe ou le code de veille ; Entrée valide, Échap revient à la session verrouillée.
- Configuration initiale sur l'ordinateur : bandeau bleu très clair, étapes et cartes arrondies ; le bandeau se resserre et les champs s'empilent dans une fenêtre moyenne.

## Faire de ce téléphone le serveur
- Correction : après « Configurer ce téléphone comme serveur », l'application affichait la page de connexion avant même que le serveur du téléphone soit démarré ; en cas de démarrage lent ou impossible, on restait bloqué sur la connexion sans explication.
- Désormais, l'écran reste sur le choix du serveur avec une progression (« Démarrage du serveur SCOLIA sur ce téléphone… », jusqu'à deux minutes la première fois), puis ouvre directement l'assistant de configuration de l'établissement.
- Si le serveur ne démarre pas, un message clair indique la cause détectée et le bouton permet de réessayer.
- Si ce téléphone était déjà serveur d'un établissement, la connexion s'affiche avec une explication : utiliser un compte créé sur ce téléphone.

## Continuité et sécurité
- Version 1.2.43, Android versionCode 163.
- Aucune migration SQLite, aucun changement des comptes, mots de passe, biométrie, jumelage, synchronisation ou protocole réseau : seules les vues changent.
- L'APK officiel doit rester signé avec le certificat historique (PR n° 18).
