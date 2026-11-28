# SCOLIA Pro 1.2.46 — transitions épurées et connexion plus compacte

## Démarrage natif Android
- Lancement centré avec **le logo SCOLIA**, **SCOLIA Pro** et **trois points animés** uniquement.
- Suppression visuelle de « ESPACE SCOLAIRE », « Votre établissement, en toute sécurité » et « Ouverture de votre espace… ».
- Animation d'assemblage du logo et mouvement des trois points conservés.
- Si le démarrage échoue, le diagnostic et le bouton **Réessayer** ou **Fermer** restent visibles : les informations d'erreur ne sont jamais dissimulées.

## Transition après connexion
- Écran bleu existant conservé pour la continuité visuelle.
- Uniquement le logo, « SCOLIA Pro » et trois indicateurs de vérification animés.
- Suppression des textes de salutation et d'étapes techniques, de la barre de progression et du texte de bas de page.
- L'écran de transition tient dans le viewport sans défilement ; aucune modification des étapes internes de vérification ou de l'ouverture de session.

## Connexion iOS plus compacte (demande précédente)
- Diminution du logo, de la hauteur des champs et boutons, de l'espacement entre les éléments et du remplissage de carte.
- Champ de mot de passe autour de 50 points ; boutons principaux de 50 points et secondaires de 48 points.
- Compte reconnu resserré, mêmes couleurs, ombres légères et coins arrondis qu'en version précédente.
- La navigation au clavier, la récupération, la biométrie et les règles de sécurité restent inchangées.

## Validation / distribution
- Tests source supplémentaires des trois éléments visibles des deux écrans de transition et des valeurs de densité de connexion.
- Compiler et tester les vues Android, les simulations de tailles d'écran et l'API avant de publier officiellement.
- Version Android : 1.2.46 (versionCode 166), certificat historique officiel issu de la PR n°18 à conserver pour permettre l'installation par-dessus la version précédente.
