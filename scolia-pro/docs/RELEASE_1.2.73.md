# SCOLIA Pro 1.2.73 — Jumelage Android harmonisé avec la connexion

## Interface « Relier mon appareil »
- Fond bleu ciel dégradé identique à celui de la connexion SCOLIA Pro et panneau central bleu cobalt aux coins arrondis.
- Logo et en-tête équilibrés, titres blancs, descriptions bleu clair, boutons principaux blancs à texte cobalt et actions secondaires bleues.
- Navigation QR / Code d’invitation / Wi-Fi cohérente, y compris la sélection dans la barre inférieure.
- Les écrans « Serveurs disponibles », « Adresse du serveur », « État de la connexion » et « Vérification sécurisée » gardent le même langage graphique.
- Gestion du mode compact, du clavier ouvert et des petites hauteurs sans ScrollViewer ni suppression des actions importantes.

## Lisibilité et sécurité
- Les champs d’invitation et d’adresse ont un fond cobalt foncé, texte et curseur blancs ; leur fond interne Fluent est défini pour éviter les champs blancs illisibles observés en 1.2.71.
- Aucun changement des commandes de scan QR, d’invitation, de recherche Wi‑Fi, de connexion ou d’activation du mode serveur.
- Tests statiques des six pages, des commandes, des couleurs de base et de la lisibilité des champs.

## Distribution
- Version Android et Windows 1.2.73 ; Android versionCode 193.
- Conserver impérativement le certificat historique Android de la PR #18.
- Ne pas installer d'APK de validation sur une application de production.
- Vérifier visuellement sur téléphone après installation avant de considérer l'harmonisation comme validée par un appareil réel.
