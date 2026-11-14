SCOLIA Pro 1.2.41 — accueil Windows dans l'esprit de la page mobile

Accueil desktop :
- La carte de bienvenue rappelle le style natif Android/iPhone : avatar rond dégradé, salut, rôle, établissement et date.
- La connexion est toujours visible dans sa propre ligne, avec icône colorée, explication et accès direct à Réseau et postes ; la caisse occupe une ligne distincte.
- Le groupe RACCOURCIS utilise les mêmes quatre entrées et les mêmes autorisations que la page Android : Encaisser, Inscrire un apprenant, Recouvrement, Rapports et exports.
- Chaque raccourci est une ligne claire à icône de couleur, titre, phrase d'explication et chevron ; bords arrondis, séparateurs, effets de survol et ombrage discret.
- Le groupe APERÇU présente les indicateurs en lignes de même langage ; les groupes sont côte à côte sur grand écran et s'empilent sur écran étroit.
- Les deux commandes rapides du bandeau, la liste complète des outils, les tâches et les derniers reçus restent accessibles.

Interface mobile — ouverture, connexion et reprise :
- Écran de lancement natif Android : fond clair doux, symbole SCOLIA animé de manière sobre, titre et progression lisible, reprise en cas d'échec et respect des animations désactivées.
- Première page de connexion à un établissement : carte de jumelage QR prioritaire, recherche Wi-Fi secondaire et affichage clair des serveurs reconnus.
- Validation d'un nouvel appareil : vue de vérification dédiée, progression sans divulgation de jeton ni de données sensibles.
- Connexion au compte : carte iOS claire, champs tactiles, compte déjà reconnu, mot de passe masqué, validation inopérante si les champs manquent, biométrie seulement si déjà autorisée.
- Récupération du mot de passe : formulaire distinct avec confirmation, indicateurs de robustesse, trois champs confidentiels et vérification par code serveur ou biométrie explicitement autorisée.
- Session verrouillée : carte d'identité, code protégé, option biométrique, récupération et changement de compte sans contourner le serveur.
- Ces écrans sont réservés à Android : les écrans d'authentification Windows restent inchangés.
- Aucune modification des clés de jumelage, rôles, comptes, données locales, synchronisation Cloudflare/Render ou services métier.

Version et compatibilité :
- Windows 1.2.41 et Android versionCode 161 pour une distribution officielle cohérente.
- Aucune migration de la base SQLite ni modification du protocole Cloudflare/Render.
- Les droits des comptes, journaux de synchronisation et jumelages existants sont conservés.
- L'APK officiel doit garder le certificat historique PR18.

Avant mise à jour : laisser les envois hors ligne se synchroniser puis vérifier la sauvegarde locale de l'établissement.
