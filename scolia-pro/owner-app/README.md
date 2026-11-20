# SCOLIA Pro — JP-Services Administration (canal indépendant)

Application **réservée à JP-Services** et distincte des clients SCOLIA des établissements.

## Architecture

- **Android** : `com.jpservices.scolia.owner`, WebView Android intégré à une coque native. Aucun service serveur local, ni accès aux bases scolaires.
- **Windows** : `SCOLIA-JPServices-Admin.exe`, interface WinForms + WebView2, installeur EXE indépendant.
- **Console** : `https://scolia-pro-owner.jeanpaulmpolo242.workers.dev/console` (Cloudflare Access + MFA + jeton propriétaire transmis uniquement au même Worker).
- **Mises à jour** : canal GitHub Releases exclusif `scolia-owner-vX.Y.Z`, sans toucher aux versions `SCOLIA Pro` distribuées aux écoles.
- **Aucun secret intégré** : pas de jeton, clé Cloudflare ni données des établissements dans les APK, EXE, sources ou manifestes.
- **Signature Android** : clé propre à l'app Admin, fournie uniquement par Secrets GitHub. Une compilation de prévisualisation sans cette clé n'est **pas** une version évolutive officiellement distribuable.
- **Sécurité** : seuls HTTPS et les fournisseurs de connexion nécessaires sont autorisés dans le navigateur intégré. Les liens externes sont ouverts dans le navigateur système. Les mises à jour ne se lancent jamais sans accord de l'utilisateur.

## Prérequis de publication officielle

Les secrets GitHub suivants doivent exister avant une vraie release Android :
`OWNER_ADMIN_KEYSTORE_B64`, `OWNER_ADMIN_KEYSTORE_PASSWORD`, `OWNER_ADMIN_KEY_ALIAS`, `OWNER_ADMIN_KEY_PASSWORD`.

Il faut conserver la clé et son certificat pour toutes les futures versions : toute autre clé empêchera une mise à jour Android par-dessus la version précédente. **Ne jamais utiliser un cache éphémère comme seule source de signature.**

## Installation

- APK : installer l'APK signé publié dans une release `scolia-owner-v...`. Il coexiste avec `com.jpservices.scolia`.
- Windows : installer `SCOLIA_JPServices_Admin_Setup_..._Windows_x64.exe`, indépendant de l'installateur SCOLIA Pro destiné aux écoles.
- Une connexion Internet est nécessaire pour accéder à la console propriétaire. Les écoles gardent leur stockage local.
- Le runtime Microsoft Edge WebView2 est nécessaire sur Windows (présent sur la plupart des Windows 11 ; installable séparément si absent).

## Lancement et contrôle

`scolia-owner-native.yml` construit un aperçu Android et Windows à chaque push sur cette branche, puis fournit des artefacts GitHub Actions.
Une publication officielle requiert le déclenchement explicite du workflow, la présence de la **clé permanente Admin** et des deux binaires construits.

Le Worker n'est pas modifié par le build des applications. La refonte CSS de `deploy/owner-console/src/index.mjs` reste une modification séparée à vérifier avant de la promouvoir sur la production.

## Version

Version initiale `1.0.0`. Pour chaque mise à jour, incrémenter Android `versionCode`, Windows `AssemblyVersion`/version Inno Setup, puis publier `scolia-owner-v...`.
