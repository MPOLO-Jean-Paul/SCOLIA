# SCOLIA Pro 1.2.91 — interface documentaire Windows 11

## Interface Windows et Android

- Espace de travail Windows 11 de style Fluent, avec onglet de la page active et commande d'actualisation.
- Écran « Cartes d'accès » réorganisé : filtres, sélection et paramètres d'export regroupés, commandes de sélection accessibles.
- Petite fenêtre Windows : actions sous le sous-titre ; défilement de la page Cartes autorisé sans modifier les autres pages.
- Android : fiches individuelles avec cases accessibles, désactivées pour les dossiers non éligibles ; sous-titre court lisible dès 360 × 720.
- Adaptation contrôlée sur Windows 760 × 520, 1024 × 600, 1366 × 768, 1920 × 1080 et Android 360 × 720, 412 × 892.

## Compatibilité des données et des documents

- Aucun changement des migrations, données scolaires, comptes, licences, paramètres et synchronisation SQLite / Cloudflare / Render.
- Le modèle des cartes imprimées reste **strictement inchangé** : moteur `SCOLIA-CARTES-ID1-2026-11-V6`, 8 cartes sur A4, QR, logos, code anonyme, bande propre à la promotion, repères et données.
- Le PDF/Word continue d'être créé sur le serveur scolaire et ouvert dans le lecteur de documents de l'appareil.
- L'installateur Windows conserve les paramètres et le dossier de données existants.

## Publication officielle et signature

- Cette version remplace la 1.2.90 sans désinstaller les applications existantes.
- Android : **utiliser exclusivement la clé de distribution historique de SCOLIA Pro**, empreinte SHA-256 du certificat `E5:B5:F7:FA:41:54:11:EE:AC:4B:F7:34:84:90:24:FF:CF:7F:93:50:0C:60:13:83:99:31:B0:20:38:F5:89:80`. Ne jamais publier de clé de validation.
- Windows : conserver l'AppId `JP-Services.SCOLIA-Pro.Desktop`. Une signature Authenticode doit être vérifiée si les secrets du certificat de production sont présents ; ne pas prétendre signer si ce n'est pas le cas.
- Publication immuable sous `scolia-pro-v1.2.91`, seulement après validation du certificat Android et des empreintes SHA-256 de l'APK et de l'EXE.

## Validation avant diffusion

1. Vérifier tous les tests GitHub Actions, y compris captures de l'interface et test de compilation Windows/Android.
2. Vérifier la signature réelle de l'APK officiel (pas seulement les variables de l'environnement).
3. Vérifier l'installation **par-dessus** SCOLIA Pro 1.2.90 sur Android et Windows, sans effacement de la base SQLite.
4. Tester un établissement fictif : serveur scolaire, connexion Android, cartes PDF/Word et sauvegarde.
5. Tester la connexion Internet et la continuité du travail hors ligne, puis la synchronisation après reconnexion.

La publication est interdite si le certificat Android est absent, différent ou provisoire.
