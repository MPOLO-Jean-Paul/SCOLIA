# SCOLIA Console — administration JP-Services

Application indépendante des clients scolaires, pour Windows et Android. L’interface actuelle est **native Avalonia 12 / .NET 10** : voir [avalonia/README.md](avalonia/README.md). Les dossiers historiques `android/` et `windows/` contiennent les anciennes coques WebView ; ils ne sont pas la version distribuée par le workflow Avalonia.

La console gère les établissements, invitations, licences, serveurs, relais et sauvegardes chiffrées. Elle n’accède pas aux dossiers des apprenants.

L’accès exige un jeton de service Cloudflare Access et un jeton propriétaire. Les identifiants validés sont chiffrés localement par DPAPI ou Android Keystore. Aucun secret n’est embarqué dans les binaires.

Version native : **1.2.1**, Android `versionCode` **5**. Les mises à jour officielles utilisent exclusivement le canal `scolia-owner-v*`, distinct de SCOLIA Pro.

## Compilation et publication

Le workflow `.github/workflows/scolia-owner-avalonia.yml` compile les deux plateformes, teste le Worker, le coffre et les écrans, puis vérifie le démarrage Android sur émulateur. Une préversion GitHub nécessite explicitement `publish_preview=true`. Une compilation seule ne publie rien.

La publication officielle depuis `main` exige une clé Android Admin permanente et les signatures Authenticode Windows vérifiées. Voir [la procédure de signature](../docs/CONSOLE_SIGNATURES_PRODUCTION.md). Les préversions de test ne remplacent pas le canal officiel et ne partagent pas la signature du client scolaire.

Le Worker web se déploie séparément avec le workflow de production et ses paramètres Cloudflare réels. La réussite des tests locaux ne prouve pas que ce déploiement a eu lieu.
