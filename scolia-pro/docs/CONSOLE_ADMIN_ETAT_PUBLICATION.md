# SCOLIA Console (console administrateur JP-Services) — état et reste à faire

Date de l'audit : 10 novembre 2026. Branche de travail : `claude/escolia-admin-console-config-h9ej62`,
basée sur la révision la plus récente de la console (`7e4ab94`, branche
`feature/cloudflare-sqlite-d1-automatic-sync-20261109`, 9 novembre 23 h 18).

## 1. Où en est la console

La console administrateur est composée de trois éléments, tous dans `scolia-pro/` :

| Élément | Dossier | Dernier état constaté |
| --- | --- | --- |
| Worker Cloudflare (API + page `/console`) | `deploy/owner-console` | **En production** sur `scolia-pro-owner.jeanpaulmpolo242.workers.dev`, révision `742c81f` (déploiement manuel réussi le 9 nov. 13 h 29 UTC). La révision `0c9a831` épinglée ensuite sur `main` n'a jamais été déployée. Les 46 tests passent sur la révision actuelle. |
| Application native **SCOLIA Console** 1.1.1 (Avalonia 12, Android + Windows) | `owner-app/avalonia` | Pilote 1.1.0 publié en préversion (`scolia-console-avalonia-preview-v1.1.0-143471b2`). **1.1.1 bloquée** : les 3 derniers essais échouent au test sur émulateur (`INSTALL_FAILED_NO_MATCHING_ABIS`). Aucune version officielle signée n'existe. |
| Ancienne coque WebView 1.0.0 (`SCOLIA-JPServices-Admin`) | `owner-app/android`, `owner-app/windows` | Préversion `scolia-owner-preview-v1.0.0-c4afab5d` seulement. Le workflow de `main` (`scolia-owner-app-release.yml`) pointait encore dessus ; il n'a jamais été lancé. Même paquet Android et même canal que la version Avalonia : les deux ne peuvent pas coexister en version officielle. |
| Netlify HTTPS de secours | `deploy/netlify-backup` | Tests verts (6), service `health-only`, boîte aux lettres désactivée. Aucun déploiement Netlify constaté. Facultatif. |

Lignée des branches console (de la plus ancienne à la plus récente) :
`feature/jp-services-admin-app-20261109` (WebView 1.0.0) → `feature/jp-services-admin-avalonia-20261109` (Avalonia 1.1.0)
→ `feature/jp-services-admin-server-discovery-20261109` (1.1.1, TURN, Netlify) → `feature/offline-cloud-spool-android-reconnect-20261109`
→ `feature/cloudflare-d1-school-pairing-free-20261109` (D1 scolaire `SCHOOL_DB`) → `feature/cloudflare-sqlite-d1-automatic-sync-20261109`
(journal chiffré automatique). Les PR #124, #125 et #127 enchaînent ces branches ; aucune n'est fusionnée.

## 2. Ce qui a été corrigé et ajouté sur cette branche

Commit `fa74f05` (révision à épingler) — `.github/workflows/scolia-owner-avalonia.yml` :

1. **Cause de l'échec 1.1.1 corrigée.** Le test émulateur compilait avec `-r android-x64`, mais l'empaquetage
   Android suit la propriété `RuntimeIdentifiers` du `.csproj` (`android-arm64;android-arm`) : l'APK ne contenait
   aucune bibliothèque x86_64. Le workflow surcharge maintenant `-p:RuntimeIdentifiers=android-x64`, vérifie
   `native-code: x86_64` avant de lancer l'émulateur, teste la **configuration Release** (celle de l'APK officiel)
   et contrôle que l'application reste ouverte 25 secondes.
2. **Canal de publication officielle** : entrée `workflow_call` (`publish_release`, `source_sha`), job
   `android-release` (APK Release signé avec le certificat Admin permanent, refus d'un certificat de débogage,
   contrôle paquet/version/ABI, fichier `.sha256`), job `release` qui publie `scolia-owner-v1.1.1` de façon
   immuable avec l'APK, l'installateur Windows et leurs empreintes. C'est le tag que l'application lit pour
   « Vérifier les mises à jour ».
3. **Plantage Android au lancement corrigé** (révélé par le test émulateur une fois l'ABI réparée) :
   l'activité utilisait `Theme.Material.Light.NoActionBar`, alors qu'Avalonia.Android exige un thème
   AppCompat (« You need to use a Theme.AppCompat theme »). L'application se fermait sur **tout**
   téléphone, préversion 1.1.0 comprise. Nouveau style `ScoliaConsoleTheme` (AppCompat). Les autres
   obstacles levés dans le test : restauration hôte `NU1102`, KVM absent (démarrage > 10 min) et
   script exécuté ligne par ligne par l'action émulateur.
4. **Windows** : contrôle de cohérence des quatre numéros de version, signature Authenticode facultative (mêmes
   secrets que SCOLIA Pro), empreinte `.sha256`.

Commit suivant — fichiers **à copier tels quels sur `main`** (ils sont inertes sur cette branche) :

- `.github/workflows/scolia-owner-app-release.yml` : appelle le workflow Avalonia épinglé à `fa74f05` au lieu de
  l'ancienne coque WebView.
- `.github/workflows/scolia-owner-production.yml` : épingle `fa74f05` (Worker avec `SCHOOL_DB`, TURN, Netlify
  désactivé) et ajoute l'étape `wrangler d1 migrations apply SCHOOL_DB`. Le script `prepare-production.mjs`
  crée la base `scolia-pro-school-sync` seulement si elle n'existe pas.

## 3. Configurations qui restent à faire (hors code)

### A. Secrets GitHub du dépôt — bloquant pour l'APK officiel

Le journal du dernier build natif confirme : « Pas de clé Admin permanente ». Il faut créer **une seule fois**
un keystore réservé à la console, le conserver hors ligne (toute perte empêche définitivement les mises à jour
Android par-dessus la version installée), puis enregistrer quatre secrets de dépôt :

| Secret | Contenu |
| --- | --- |
| `OWNER_ADMIN_KEYSTORE_B64` | le fichier `.jks` encodé en base64 sur une seule ligne |
| `OWNER_ADMIN_KEYSTORE_PASSWORD` | mot de passe du keystore |
| `OWNER_ADMIN_KEY_ALIAS` | alias de la clé (par exemple `scolia-console`) |
| `OWNER_ADMIN_KEY_PASSWORD` | mot de passe de la clé |

Génération sur un poste de confiance (jamais dans le dépôt, jamais dans une conversation) :

```bash
keytool -genkeypair -v -keystore scolia-console-admin.jks -alias scolia-console \
  -keyalg RSA -keysize 4096 -validity 10950 \
  -dname "CN=SCOLIA Console, O=JP-Services, C=CG"
base64 -w0 scolia-console-admin.jks > scolia-console-admin.jks.b64
```

Ne pas réutiliser la clé de SCOLIA Pro (clients) : le canal administrateur doit rester indépendant.

### B. Secrets facultatifs — signature Windows

`SCOLIA_WINDOWS_SIGNING_PFX`, `SCOLIA_WINDOWS_SIGNING_PASSWORD` (secrets) et `SCOLIA_WINDOWS_CERT_THUMBPRINT`
(variable), déjà prévus pour SCOLIA Pro. Sans certificat de signature de code, l'installateur fonctionne mais
Windows SmartScreen affiche un avertissement. Le workflow signe automatiquement si les secrets existent.

### C. Environnement GitHub `scolia-owner-production` — déjà en place, à vérifier

Les déploiements réussis du 9 novembre prouvent la présence de `CLOUDFLARE_API_TOKEN`, `CLOUDFLARE_ACCOUNT_ID`,
`SCOLIA_OWNER_ADMIN_TOKEN` et `SCOLIA_TURN_DISCOVERY_TOKEN`. Deux points à contrôler avant le prochain déploiement :

- Le jeton API doit avoir **D1 Edit** (création de la seconde base `scolia-pro-school-sync`), **Workers R2 Storage
  Edit** et **Workers Scripts Edit**. Le forfait gratuit accepte 10 bases D1 au maximum.
- La règle de déploiement de l'environnement n'autorise que `main` : le workflow de la branche
  `release/direct-apk-scolia-pro-20261103` a été refusé le 9 novembre (job sans aucune étape). Garder `main`
  comme seule branche autorisée et continuer à publier depuis `main`.

### D. Cloudflare Access — déjà configuré, règle à conserver

L'application Zero Trust protège `/console` et `/api/v1/owner/*` (AUD `beb99695…`). Le déploiement de 12 h 43 a
échoué parce que `/api/v1/notice` renvoyait 403 ; la configuration a été corrigée avant 13 h 19. **`/health`,
`/api/v1/notice`, `/api/v1/register`, `/api/v1/heartbeat` et `/api/v1/server/*` doivent rester hors Access**,
sinon les serveurs des écoles sont bloqués. Le workflow échoue volontairement dans ce cas.

### E. Netlify — facultatif, rien à faire pour les installateurs

Le pilote reste désactivé (`SCOLIA_NETLIFY_MAILBOX_ENABLED` absent). Un site Netlify n'est nécessaire que pour
tester le point `health`.

## 4. Procédure pour obtenir les installateurs finaux

1. **Valider cette branche sur GitHub** : Actions → « SCOLIA Console natif Avalonia - Android et Windows » →
   *Run workflow* → branche `claude/escolia-admin-console-config-h9ej62`, `publish_release` = false.
   Attendu : 4 jobs verts (Windows, Android preview, tests Worker, émulateur) et une préversion
   `scolia-console-avalonia-preview-v1.1.1-fa74f058`. C'est le seul moyen de vérifier la compilation .NET :
   l'environnement d'audit ne dispose pas du SDK .NET.
2. **Créer le keystore et les 4 secrets** (section 3.A). Facultatif : certificat Windows (3.B).
3. **Mettre `main` à jour** en y copiant les deux fichiers `.github/workflows/scolia-owner-app-release.yml` et
   `.github/workflows/scolia-owner-production.yml` de cette branche (un commit sur `main`, comme pour les
   épinglages précédents). Le push déclenche automatiquement le job « Tests sans secrets ».
4. **Publier les installateurs** : Actions → « JP-Services Admin - publication Android et Windows » depuis `main`,
   `publish_release` = true. Résultat : release **`scolia-owner-v1.1.1`** avec
   `SCOLIA_Console_1.1.1_Android.apk`, `SCOLIA_Console_Setup_1.1.1_Windows_x64.exe` et leurs `.sha256`.
   Sans les 4 secrets, le workflow s'arrête avant toute publication.
5. **Redéployer le Worker** : Actions → « SCOLIA Pro - Console JP-Services (production protegee) » depuis `main`,
   `confirm_production` coché. Le Worker passe de `742c81f` à `fa74f05` (D1 scolaire, TURN, Netlify désactivé).
6. **Vérifier** : `https://scolia-pro-owner.jeanpaulmpolo242.workers.dev/health` répond `SCOLIA Pro Owner` ;
   `/console` demande Cloudflare Access puis le jeton ; sur un téléphone, désinstaller les préversions
   (`com.jpservices.scolia.owner.preview`), installer l'APK officiel, ouvrir Compte → « Vérifier les mises à
   jour » : « La version administrateur est à jour sur le canal officiel ».

## 5. Pour les versions suivantes

Avant chaque nouvelle publication, incrémenter ensemble : `OWNER_VERSION` et `RELEASE_TAG` dans
`scolia-owner-avalonia.yml`, `Version` dans `Desktop/Scolia.Owner.Desktop.csproj`, `ApplicationDisplayVersion`
**et** `ApplicationVersion` (+1) dans `Android/Scolia.Owner.Android.csproj`, `AdminVersion` dans
`Installer.Avalonia.iss`, `Version` dans `Core/OwnerShell.cs`. Le job Windows refuse toute incohérence. Une
release publiée n'est jamais remplacée.

## 6. Limites connues (pas des blocages)

- SCOLIA Console 1.1.1 est un pilote d'interface : les opérations sensibles ouvrent la console web protégée par
  Access dans le navigateur ; l'authentification native Cloudflare Access n'est pas implémentée.
- Le jumelage D1 scolaire et le journal chiffré automatique restent des pilotes à tester de bout en bout avec
  une vraie école (voir `CLOUDFLARE_D1_SQLITE_AUTO_SYNC.md`).
- Les secrets et variables GitHub n'ont pas pu être listés par l'API depuis cet environnement ; leur présence est
  déduite des journaux d'exécution.
