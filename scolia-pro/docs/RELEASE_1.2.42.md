# SCOLIA Pro 1.2.42 — adaptation des écrans Android et icônes stables

## Présentation des écrans
- Connexion, récupération de mot de passe et jumelage : cartes, champs et textes restent lisibles sur un fond clair, même si l'application utilise le thème sombre.
- Marges, logos et titres ajustés sur les petits téléphones ; les formulaires restent défilables et les boutons gardent leur hauteur tactile.
- Les méthodes secondaires de jumelage QR, les établissements détectés et les actions réseau Wi-Fi se replient sur les écrans étroits ; un toucher permet de les ouvrir et de les refermer.

## Couleurs des icônes
- Les icônes de navigation et d'action conservent les neuf couleurs SCOLIA du thème clair en mode sombre, ainsi que leurs pictogrammes blancs.
- Les teintes originales sont préservées sur les tuiles existantes sans recolorer les images, logos ni fonds personnalisés.
- Les pinceaux de couleurs sont construits sans partager un même objet Avalonia entre différents threads.

## Continuité et sécurité
- Version Android affichée 1.2.42, numéro de version 162 (la compilation officielle applique aussi un code de version croissant).
- Aucune migration de la base SQLite, aucun changement des paiements, des comptes, de la synchronisation, du protocole réseau ou des appareils jumelés.
- L'APK officiel doit être signé avec le certificat historique associé à la PR n° 18 ; un certificat différent est bloquant. L'installateur Windows demeure distinct.
- Les versions déjà publiées restent inchangées.

## Avant l'installation
Synchroniser les opérations en attente et vérifier la sauvegarde de l'établissement. Installer la mise à jour par-dessus l'application existante : ne pas désinstaller SCOLIA pour contourner un échec de signature.
