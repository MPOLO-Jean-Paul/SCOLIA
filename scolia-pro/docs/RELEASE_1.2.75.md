# SCOLIA Pro 1.2.75 — bouton « Se connecter » accessible sur écran Android très court

## Anomalie
Sur SCOLIA Pro 1.2.74, le test de fluidité Android en résolution 320 × 300 pixels (clavier ouvert ou fenêtre très courte) échouait : le bouton de connexion n'était plus entièrement visible après un défilement au bas de la page. La refonte sans carte avait déplacé les options textuelles Compte/Serveur sous le bouton.

## Correction
- Ajout d'un défilement ciblé dans le conteneur d'authentification : ne déplace que la distance nécessaire pour rendre l'action entièrement visible dans sa fenêtre.
- Lors du focus du mot de passe ou d'un redimensionnement lié au clavier Android, le bouton « Se connecter » est repositionné si nécessaire.
- Le parcours de test vérifie l'accès réel au bouton et aux options textuelles à partir du défilement, sans supposer qu'ils occupent tous les deux l'extrême bas.
- Conservation intégrale du fond plein écran sans carte, du logo SCOLIA, des champs à contraste fort et de la disposition PC (options à droite) / téléphone (options sous formulaire).
- Aucune modification des identifiants, paiements, données, protocoles de jumelage ou synchronisation.

## Contrôles prévus
- Compilations Avalonia, Android et Windows.
- Tests de navigation, auth, clavier et fluidité sur 320 × 300, 320 × 568, 360 × 640 et 412 × 892.
- Tests métier/API ; signatures Android issues de la PR #18 et vérification des empreintes SHA-256.
- Essai sur téléphone réel nécessaire avant confirmation définitive.

## Distribution
- Version application : 1.2.75 ; Android versionCode : 195.
- Installer l'APK officiel signé par-dessus la version précédente, sans désinstallation.
