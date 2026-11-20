# SCOLIA Console — Avalonia 12 (.NET 10)

Nom commercial : **SCOLIA Console** ; éditeur : **JP-Services**.

Version native distincte des applications SCOLIA Client. Une seule base UI `Core/OwnerShell.cs`
et une seule palette `Core/OwnerPalette.cs` pour Windows et Android.

- Android : `com.jpservices.scolia.owner.preview` en Debug, `com.jpservices.scolia.owner` en Release SIGNÉE.
- Windows : fenêtre Avalonia + installateur Inno Setup. Ni WinForms ni WebView2.
- **Version 1.2.0 : administration native complète.** Vue d'ensemble chiffrée, établissements (liste, fiche,
  invitation et invitation de reprise), licences (paiement, délai, essai, blocage en lecture seule, dérogation,
  facturation), serveurs, relais WSS (activation/désactivation), journal d'activité et sauvegardes.
- **Authentification** : chaque appel porte un **jeton de service Cloudflare Access** (`CF-Access-Client-Id` /
  `CF-Access-Client-Secret`) et le **jeton propriétaire** (`Authorization: Bearer`), exactement comme la console web
  exige Access + jeton. Les redirections ne sont jamais suivies : une redirection vers la connexion Access est un refus.
- **Coffre** : les trois secrets sont vérifiés auprès du service avant d'être enregistrés, puis chiffrés par la
  plateforme — DPAPI (compte Windows courant) ou clé AES-256 non exportable de l'Android Keystore. Ils ne sont jamais
  affichés ni journalisés ; « Effacer l'accès de cet appareil » les supprime.
- **Écritures** : toute action modifiant une licence, une sauvegarde ou un relais demande une confirmation en deux temps.
- Restent dans la console web : stockage R2, découverte TURN, ajout d'un relais, Netlify.
- Mises à jour : canal propriétaire indépendant `scolia-owner-v...`, filtre la liste des Releases GitHub. Les APK Preview ne sont pas des releases signées permanentes.
- Tests : `Tests/` (client, coffre, libellés, rendu hors écran de chaque page sur un service simulé ;
  `SCOLIA_CONSOLE_CAPTURES=<dossier>` enregistre les captures).

Ne pas publier la version Android officielle sans les quatre secrets de signature Admin.

## Publication

- Préversion (certificat de test) : lancer « SCOLIA Console natif Avalonia - Android et Windows » avec
  `publish_release` = false, ou pousser sur la branche surveillée.
- Version officielle : depuis `main`, « JP-Services Admin - publication Android et Windows » appelle ce workflow
  épinglé à une révision testée avec `publish_release` = true. Elle exige les quatre secrets `OWNER_ADMIN_*`
  et publie le tag immuable `scolia-owner-vX.Y.Z` (APK signé + installateur Windows + empreintes `.sha256`).
- État complet, secrets à créer et procédure pas à pas : `scolia-pro/docs/CONSOLE_ADMIN_ETAT_PUBLICATION.md`.
