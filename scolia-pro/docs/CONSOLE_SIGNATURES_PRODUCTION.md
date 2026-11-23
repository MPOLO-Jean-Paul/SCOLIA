# SCOLIA Console — première signature de production Android / Windows

**Cette procédure concerne exclusivement SCOLIA Console Administrateur** ; elle ne modifie ni les clés ni la base de données scolaires.

## Android : une clé permanente, créée sur votre propre PC

1. Sur un PC Windows avec Java JDK 17+ installé, ouvrez PowerShell dans le dépôt SCOLIA, puis exécutez :

```powershell
powershell -ExecutionPolicy Bypass -File .\scolia-pro\scripts\owner\new-permanent-android-key.ps1
```

2. Choisissez et conservez des mots de passe solides. Ne faites jamais une deuxième création pour les mises à jour.
3. Conservez deux sauvegardes chiffrées du fichier JKS en lieux distincts.
4. Dans GitHub > dépôt SCOLIA > Settings > Secrets and variables > Actions :
   - **Secrets** : `OWNER_ADMIN_KEYSTORE_B64`, `OWNER_ADMIN_KEYSTORE_PASSWORD`, `OWNER_ADMIN_KEY_ALIAS`, `OWNER_ADMIN_KEY_PASSWORD`.
   - **Variables** : `OWNER_ADMIN_CERT_SHA256` (empreinte publique de 64 caractères hexadécimaux, affichée par le script).
5. Pour encoder le keystore en Base64 **sur votre ordinateur**, exécutez :

```powershell
[Convert]::ToBase64String([IO.File]::ReadAllBytes("$HOME\SCOLIA-PrivateSigning\owner-admin-release.jks")) | Set-Clipboard
```

Collez dans la valeur secrète `OWNER_ADMIN_KEYSTORE_B64`, puis effacez le presse-papiers. Ne publiez ni le Base64, ni les mots de passe, ni le JKS dans les messages ou les journaux. Les mots de passe GitHub doivent correspondre aux mots de passe choisis à la création. `OWNER_ADMIN_KEY_ALIAS` par défaut : `scolia-admin-release`.

Le workflow officiel bloque la publication si une des quatre valeurs manque, si la signature est une clé de débogage, ou si l'empreinte ne correspond pas à `OWNER_ADMIN_CERT_SHA256`. Le paquet Android officiel est `com.jpservices.scolia.owner`, et les versions futures doivent augmenter `ApplicationVersion` et conserver la même clé.

## Windows : certificat Authenticode d'une autorité reconnue

- **NE PAS** utiliser un certificat Windows autosigné pour la distribution publique.
- Obtenir un certificat de signature de code auprès d'une autorité (ex. DigiCert, SSL.com) après vérification d'identité. Vérifier préalablement l'éligibilité des éditeurs établis en RDC et la méthode de signature (token/HSM/cloud).
- IMPORTANT : les certificats modernes sont souvent associés à un dispositif HSM ou à un service cloud et leur clé n'est généralement **pas exportable en PFX**. Le workflow Windows actuel attend encore `SCOLIA_WINDOWS_SIGNING_PFX` + `SCOLIA_WINDOWS_SIGNING_PASSWORD`. **Ne pas acheter sans prévoir l'adaptation du workflow au fournisseur choisi.**
- La publication officielle exige aussi `SCOLIA_WINDOWS_CERT_THUMBPRINT` et valide EXE + Setup avec Authenticode.
- Un EXE doit être horodaté lors de la signature ; le renouvellement du certificat ne doit pas rompre l'identité de l'application.
- SmartScreen peut encore afficher un avertissement pour un tout nouvel éditeur signé, même avec un certificat valide.

## Publication

Le workflow `.github/workflows/scolia-owner-app-release.yml` sur `main` est déclenché *manuellement* uniquement par le propriétaire, avec `publish_release=true`. Aucune PREVIEW ne remplace le canal officiel `scolia-owner-v*.`

Ne lancez **pas** la première publication officielle tant que les secrets Android permanents et une solution Authenticode Windows valide ne sont pas présents et vérifiés.
