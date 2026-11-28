# Confidentialite du depot SCOLIA Pro - 4 novembre 2026

## Fichiers d'execution retires de Git

Le dossier `scolia-pro/SCOLIA/` contenait des caches JSON de l'application, avec des lignes susceptibles d'inclure des noms, des identifiants de dossiers et des chiffres scolaires/financiers. Il ne s'agit pas de sources necessaires a la compilation. Le dossier est retire de l'arbre Git actuel et exclu par `.gitignore`.

**Important : les anciennes revisions Git peuvent toujours contenir ces fichiers.** Retirer les fichiers du dernier commit ne supprime pas leurs anciennes versions des clones, forks et caches. Le proprietaire du depot doit examiner l'origine des jeux de donnees et, si elles sont reelles, organiser une purge controlee de l'historique avec `git-filter-repo` et les consignes GitHub de retrait des donnees sensibles, puis traiter les sauvegardes et copies. La reecriture de l'historique peut casser les clones et les branches ; elle n'est pas declenchee automatiquement.

## Regles de publication

- Le controle GitHub Actions refuse tout fichier suivi sous `scolia-pro/SCOLIA/`.
- Les branches et PR de test ne doivent pas creer de GitHub Release officiel. Seul un `push` sur la branche de distribution, apres controles et compilation, peut publier.
- L'empreinte du certificat Android officiel doit etre conservee. Ne jamais la remplacer silencieusement par une signature generee en cache ; l'installation par-dessus l'ancienne version echouerait.
- Lorsque le cache de signature est incorrect, l'execution officielle est bloquee tant que la cle d'origine n'est pas restauree dans les secrets Actions du depot.

## Sujets restants et non modifies

- La communication HTTP en LAN doit etre migree vers un transport authentifie et chiffre avec distribution de confiance des certificats. Couper simplement HTTP dans Android casserait les installations existantes.
- Une revue de confidentialite reste necessaire pour les logs, exports, rapports, sauvegardes et traces de synchronisation.
- La disponibilite d'un telephone serveur sous Android Doze ne peut etre garantie sur tous les modeles.

## Barriere automatique sur toutes les modifications (ajout du 4 novembre)

Le script scolia-pro/scripts/audit-tracked-files.sh parcourt exclusivement les chemins actuellement suivis par Git et refuse les bases SQLite, les fichiers de cle privee et les repertoires de donnees/cache de l'etablissement. Son propre --self-test teste des chemins fictifs positifs et negatifs.

- Le workflow rapide .github/workflows/scolia-repository-privacy.yml s'execute sur chaque push et chaque pull_request, meme lorsque seule une documentation ou un fichier de donnees change.
- Le pipeline natif appelle egalement le meme controle pour ne pas autoriser une compilation avec des fichiers sensibles suivis.
- Le filtre porte uniquement sur les noms et emplacements de fichiers, sans inspecter leur contenu ; un nom anodin contenant des informations privees peut encore passer. Il ne remplace pas la revue des captures d'ecran (issue #41).
- gitignore ne retire pas les fichiers deja commits et ne nettoie pas l'historique Git. Les anciennes revisions sensibles doivent toujours etre traitees a part.
