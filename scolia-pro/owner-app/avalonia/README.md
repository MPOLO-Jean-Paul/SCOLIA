# SCOLIA Console — Avalonia 12 (.NET 10)

Nom commercial : **SCOLIA Console** ; éditeur : **JP-Services**.

Version native distincte des applications SCOLIA Client. Une seule base UI `Core/OwnerShell*.cs` et ses composants
et une seule palette `Core/OwnerPalette.cs` pour Windows et Android.

- Android : `com.jpservices.scolia.owner.preview` en Debug, `com.jpservices.scolia.owner` en Release SIGNÉE.
- Windows : fenêtre Avalonia + installateur Inno Setup. Ni WinForms ni WebView2.
- **Version 1.2.1 : interface Fluent et administration native.** Vue d'ensemble chiffrée, établissements (liste, fiche,
  invitation et invitation de reprise), licences (paiement, délai, essai, blocage en lecture seule, dérogation,
  facturation), serveurs, relais WSS (création, activation/désactivation et choix Cloudflare principal), journal d'activité et sauvegardes.
- **Authentification** : chaque appel porte un **jeton de service Cloudflare Access** (`CF-Access-Client-Id` /
  `CF-Access-Client-Secret`) et le **jeton propriétaire** (`Authorization: Bearer`), exactement comme la console web
  exige Access + jeton. Les redirections ne sont jamais suivies : une redirection vers la connexion Access est un refus.
- **Coffre** : les trois secrets sont vérifiés auprès du service avant d'être enregistrés, puis chiffrés par la
  plateforme — DPAPI (compte Windows courant) ou clé AES-256 non exportable de l'Android Keystore. Ils ne sont jamais
  affichés ni journalisés ; « Effacer l'accès de cet appareil » les supprime.
- **Écritures** : toute action modifiant une licence, une sauvegarde ou un relais demande une confirmation en deux temps.
- Restent dans la console web : stockage R2, découverte TURN et Netlify.
- Mises à jour : canal propriétaire indépendant `scolia-owner-v...`, filtre la liste des Releases GitHub. Les APK Preview ne sont pas des releases signées permanentes.
- Tests : `Tests/` (client, coffre, libellés, navigation concurrente, refus sans perte de saisie, rendu de chaque page en sept formats/thèmes ; renouvellement et relais contre le vrai Worker sur SQLite en mémoire ;
  `SCOLIA_CONSOLE_CAPTURES=<dossier>` enregistre les captures).

Ne pas publier la version Android officielle sans les quatre secrets de signature Admin.

## Publication

- Préversion (certificat de test) : lancer « SCOLIA Console natif Avalonia - Android et Windows » avec
  `publish_release` = false et `publish_preview` = true. Un push compile sans publier.
- Version officielle : depuis `main`, « JP-Services Admin - publication Android et Windows » appelle ce workflow
  épinglé à une révision testée avec `publish_release` = true. Elle exige les quatre secrets `OWNER_ADMIN_*`
  et publie le tag immuable `scolia-owner-vX.Y.Z` (APK signé + installateur Windows + empreintes `.sha256`).
- État complet, secrets à créer et procédure pas à pas : `scolia-pro/docs/CONSOLE_ADMIN_ETAT_PUBLICATION.md`.

## Finitions communes Windows et Android

Les illustrations Fluent officielles et les polices Inter sont embarquées, avec leurs licences. Les surfaces, tailles et contrastes suivent le thème clair/sombre ; le choix Système/Clair/Sombre prend effet immédiatement pour la session. Les petits écrans utilisent cinq onglets et un menu donnant accès à tous les outils. Les listes sont recherchables et affichées par groupes de 40 cartes.

Un changement de largeur ou un refus du service conserve les champs en cours. La navigation annule les anciennes lectures ; une réponse tardive ne remplace pas la page actuelle. Les écritures ne peuvent pas se superposer et les confirmations longues restent lisibles.

Pour lancer tous les tests natifs, installer .NET 10 et Node.js 22 ou supérieur, puis exécuter `dotnet test Tests/Scolia.Owner.Tests.csproj -c Release`. Le serveur local des tests crée uniquement des écoles fictives dans une base en mémoire. Les tests du Worker se lancent avec `npm test` depuis `deploy/owner-console`.

Avec Playwright et Chromium installés, `node scripts/verifier-console-web.cjs` depuis `scolia-pro` vérifie cinq formats/thèmes, un paiement confirmé et un verrouillage pendant une lecture. `CHROMIUM_PATH` sélectionne un Chromium système ; `SCOLIA_CONSOLE_WEB_CAPTURES` choisit le dossier des images et du rapport. Ce contrôle démarre et ferme son propre Worker local de test.
