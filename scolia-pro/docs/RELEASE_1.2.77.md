# SCOLIA Pro 1.2.77 — Plein écran Windows dès la connexion

## Problèmes sur les captures
- La fenêtre SCOLIA Pro était limitée à 460 × 640 px pendant la connexion au lieu d'occuper l'écran Windows.
- La page « Relier mon appareil » était trop pâle, ses textes peu contrastés et la zone de travail trop étroite.

## Correctifs
- SCOLIA démarre maximisé dès l'ouverture, pour la connexion, le jumelage, la restauration du mot de passe et la configuration du serveur.
- La barre de titre Windows et la barre des tâches restent visibles ; l'utilisateur peut restaurer et redimensionner la fenêtre.
- MainWindow n'applique plus le mode compact historique de 460 × 640 px sur ordinateur ; Android garde son interface native.
- Le formulaire PC et sa colonne d'actions secondaires sont centrés ensemble, sans ajouter de carte.
- La page de jumelage de bureau utilise un dégradé bleu plus profond, des textes blancs et bleu clair, un bouton principal blanc et des actions secondaires cobalt.
- Zone centrale du jumelage élargie jusqu'à 820 px sur grand écran, sans contour autour de la page.
- Les commandes QR, code d'invitation, Wi-Fi, compte, authentification et synchronisation sont inchangées.

## Vérification et livraison
- Tests de non-régression du démarrage maximisé, des colonnes du login et du contraste de la page de jumelage.
- Version SCOLIA Pro 1.2.77 ; Android versionCode 197.
- Publication après succès des workflows Android et Windows avec signature Android historique issue de la PR #18 et contrôle SHA-256.
- Le rendu définitif doit être confirmé sur un ordinateur Windows réel après installation.
