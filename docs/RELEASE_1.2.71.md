# SCOLIA Pro 1.2.71 — Lisibilité des champs de connexion

## Correctif principal

- **Identifiant et mot de passe visibles** sur le fond bleu cobalt de la page de connexion : le texte saisi, le curseur et les caractères masqués ne se confondent plus avec le fond.
- Les indications des champs (« Identifiant », « Mot de passe ») utilisent un bleu très clair lisible et conservent leur contraste lorsque le champ reçoit le focus.
- Le thème global de SCOLIA n'écrase plus l'apparence des champs de connexion, même après le passage du mode clair au mode sombre.
- Les écrans de récupération utilisent les mêmes règles de contraste pour le nouveau mot de passe, sa confirmation et le code de secours.
- Sur les petits téléphones, un espacement adapté garde les premiers caractères éloignés des icônes dans les champs.

## Conformité et stabilité

- Conservation du design précédent : **fond bleu ciel, panneau bleu cobalt, logo de l'application et actions compactes par icônes**.
- Aucune modification de l'authentification, de la politique des mots de passe, des comptes enregistrés ni de la synchronisation des enseignants.
- Tests automatisés de visibilité et de contraste sur les cinq champs, du style global et des espacements compacts ; compilation et tests métier/API réussis.

## Installation et sécurité

- Version Android et Windows **1.2.71** ; Android `versionCode` **191**.
- Même certificat de distribution Android que les versions précédentes : mise à jour par-dessus l'application existante, sans désinstallation ni perte du stockage local.
- Ne jamais installer un APK marqué « validation uniquement » sur un appareil de production.
- Vérifier la sauvegarde de la base scolaire principale avant de mettre à jour le serveur Windows ; ne pas effacer les données de SCOLIA.
