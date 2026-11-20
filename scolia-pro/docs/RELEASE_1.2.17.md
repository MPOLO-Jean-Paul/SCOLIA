# SCOLIA Pro 1.2.17 — préparation de publication

> Document de préparation. La version n'est **officiellement publiée** qu'après
> une compilation Android et Windows réussie, une signature Android d'origine
> vérifiée et une GitHub Release dotée de ses deux exécutables.

## Améliorations incluses dans la série 1.2.17

- Contrôles empêchant l'envoi dans Git de bases, caches, clés de signature,
  journaux de données scolaires et fichiers d'environnement locaux.
- Processus de compilation Android plus tolérant aux erreurs réseau du SDK ;
  aucune substitution de clé de signature pour les versions officielles.
- Vérification de l'empreinte SHA-256 du certificat de signature et blocage
  d'une APK signée avec une clé différente.
- Installation Windows autonome et distribution Android indépendante, sans
  joindre un APK signé avec la clé de validation d'une pull request.
- Information explicite sur le risque du HTTP local lors de l'activation
  du partage Wi-Fi ; la future migration HTTPS est suivie séparément.
- Confirmation avant l'abandon d'une opération hors ligne en conflit.

## Prérequis de la publication

- Les tests GitHub « SCOLIA Natif », « SCOLIA Pro » et confidentialité passent.
- Le dépôt possède les secrets GitHub Actions `SCOLIA_ANDROID_KEYSTORE`
  (contenu base64 de la clé de signature d'origine) et
  `SCOLIA_ANDROID_KEYSTORE_PASS`.
- Le certificat de cette clé **doit correspondre** à celui de la version déjà
  installée ; le CI refuse toute différence.
- Les fichiers officiels `SCOLIA_Pro_1.2.17_Android.apk` et
  `SCOLIA_Pro_Installation_1.2.17_Windows_x64.exe` passent les contrôles
  de signature et de somme de contrôle SHA-256.
- Une sauvegarde des données scolaires sur le poste serveur est recommandée
  avant l'installation, sans désinstaller l'application Android existante.

## Publication

La chaîne GitHub Actions `SCOLIA Natif` publie les deux fichiers sur le tag
`scolia-pro-v1.2.17` seulement depuis la branche officielle et après
réussite de tous les contrôles. Ne pas créer de release partielle sous ce tag,
car les mises à jour clientes consultent la dernière GitHub Release.
