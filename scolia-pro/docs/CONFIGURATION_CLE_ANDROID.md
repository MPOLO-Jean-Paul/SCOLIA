# Configurer la clé Android officielle pour SCOLIA Pro 1.2.17

## Pourquoi cette étape est nécessaire

Les anciennes versions utilisaient le cache GitHub Actions
`scolia-android-signature-v1` avec le fichier
`~/.scolia-signature/scolia.keystore` et le mot de passe associé.
Ce cache n'est **pas** un stockage sécurisé permanent de secrets ; il
n'est ni garanti disponible, ni identique à la clé de la dernière
version officielle. Depuis la PR #43, sa réutilisation automatique est
interdite.

Le certificat attendu pour mettre à jour la version officielle 1.2.16 est :

`E5:B5:F7:FA:41:54:11:EE:AC:4B:F7:34:84:90:24:FF:CF:7F:93:50:0C:60:13:83:99:31:B0:20:38:F5:89:80`

L'APK public ne contient **pas** la clé privée permettant de le signer.
Si le fichier original n'est pas conservé, aucune commande ne peut
le recréer avec ce certificat.

## Procédure recommandée sur l'ordinateur du propriétaire

1. Rechercher une sauvegarde locale ou un coffre-fort d'équipe contenant
   **le fichier de clé historique** et son mot de passe. Ne pas récupérer
   ni publier un keystore non vérifié, issu de PRs de validation.
2. Installer GitHub CLI (`gh`) et un JDK (`keytool`), puis se
   connecter au compte autorisé avec `gh auth login`.
3. Depuis PowerShell, dans le dossier `scolia-pro/scripts`, lancer :

   `./configurer-signature-android.ps1 -Keystore "C:\chemin\vers\scolia.keystore"`

4. Le script demande le mot de passe **sans l'afficher**, compare le
   certificat avec l'empreinte attendue et, seulement s'il correspond,
   crée les deux secrets du dépôt `MPOLO-Jean-Paul/SCOLIA` :
   `SCOLIA_ANDROID_KEYSTORE` et `SCOLIA_ANDROID_KEYSTORE_PASS`.
5. Vérifier dans GitHub → Settings → Secrets and variables → Actions
   que **les deux noms** figurent dans la liste. Les valeurs ne
   doivent pas être visibles dans GitHub.
6. Relancer la validation et publication 1.2.17 **uniquement après**
   les tests et la vérification du certificat réel de l'APK.

## Si la seule copie était dans le cache GitHub Actions

Le cache historique peut avoir été supprimé ou expirer ; sa présence
est incertaine. **Ne pas** réintroduire son utilisation dans le pipeline
de distribution : un cache GitHub Actions n'est pas un coffre à secrets
et peut être exposé aux exécutions de pull request. La récupération et
migration sécurisées de ce fichier doivent être effectuées par un
administrateur disposant des droits sur les runners et les secrets.
Une sauvegarde du fichier de clé et de son mot de passe doit ensuite
être placée dans un coffre-fort privé de l'établissement.

Ne jamais publier un APK signé avec une clé temporaire sous
`scolia-pro-v1.2.17` ; ne jamais désinstaller l'application existante
pour contourner un conflit de signature, sous peine de perdre des
données scolaires stockées sur le téléphone.
