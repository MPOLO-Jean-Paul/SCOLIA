# SCOLIA Pro — origine démontrée du problème de signature Android et migration durable

## Diagnostic confirmé sur GitHub Actions (5 novembre 2026)

Les exécutions de CI sont **sur des périmètres de cache GitHub distincts**, bien que leur clé logique de cache soit identique.

| Origine | Workflow/Job | Cache restauré | Observation |
|---|---|---|---|
| PR **#18** `refs/pull/18/merge` | Exécution `41947270491`, job `126983494443` | `scolia-android-signature-v1` — **3843 octets** | Certificat historique reconnu, compilation APK et contrôle de la signature réussis |
| **Push** sur `release/direct-apk-scolia-pro-20261103` | Exécution `41947264571`, job `126983344192` | Même nom logique — **3814 octets** | Échec : `La cle du cache ne correspond pas au certificat officiel de 1.2.16` |
| Autre job du push de distribution | Exécution `41947264513`, job `126985165695` | Même cache de **3814 octets** | Échec identique |

**Empreinte du certificat public officiel (SHA-256)** :
`E5:B5:F7:FA:41:54:11:EE:AC:4B:F7:34:84:90:24:FF:CF:7F:93:50:0C:60:13:83:99:31:B0:20:38:F5:89:80`.

Les caches créés dans le contexte d'une PR sont limités à la référence `refs/pull/<n>/merge`. Une exécution `push` sur la branche source, même avec le même `key:`, ne lit pas le même cache. Modifier simplement son nom ou restaurer des clés approximatives est incorrect et dangereux. Voir la documentation GitHub Actions, *Dependency caching reference*, section *Restrictions for accessing a cache*.

**Les clés privées et leurs mots de passe ne doivent pas être enregistrés dans le cache Actions**. Cette migration est une exception ponctuelle pour récupérer la clé historique déjà présente, puis supprimer le cache.

## Migration en un seul passage, sans extraction publique

Cette PR introduit :

- `.github/workflows/scolia-migration-signature-pr18.yml` : un job de contrôle sans secret, et un job de migration **strictement limité à la PR interne #18**.
- `scolia-pro/scripts/migrer-signature-pr18-vers-secrets.sh` : vérifie le contexte, le cache effectivement retrouvé, l'alias `scolia` et l'empreinte historique ; si tout est correct, chiffre et enregistre les deux secrets GitHub Actions. Les données de la clé privée passent uniquement sur `stdin` à `gh secret set`, sans artefact ou sortie de logs.
- L'environnement GitHub **`scolia-signature-migration`** pour exiger l'approbation d'un administrateur avant toute lecture du jeton de migration.

### Actions nécessaires de l'administrateur

1. Vérifier que la branche `release/direct-apk-scolia-pro-20261103` est protégée et que seules des personnes de confiance peuvent la modifier. Vérifier la PR #18 et ne pas approuver un changement de code inattendu.
2. Dans **Settings → Environments**, créer `scolia-signature-migration` et activer **Required reviewers**. Ne pas fournir de secrets tant que cette protection n'est pas effective.
3. Créer un jeton GitHub à courte durée de vie, limité au dépôt `MPOLO-Jean-Paul/SCOLIA`, avec la permission repository **Secrets: Read and write** uniquement. Placer sa valeur comme secret de cet environnement sous le nom **`SCOLIA_SIGNING_ADMIN_TOKEN`** ; ne jamais la copier dans le code, une issue ou cette conversation.
4. Après revue, fusionner la PR de migration dans la branche de distribution. Le changement de cette branche déclenche la PR #18 et le nouveau job peut restaurer le cache dans son propre contexte ; l'administrateur doit **vérifier le SHA exact** avant de cliquer sur **Approve and deploy** pour `scolia-signature-migration`.
5. Observer uniquement l'état succès/échec du job. En cas de succès, vérifier les deux **noms** dans *Settings → Secrets and variables → Actions* : `SCOLIA_ANDROID_KEYSTORE` et `SCOLIA_ANDROID_KEYSTORE_PASS`. Ne pas essayer de lire les valeurs des secrets.
6. **Révoquer immédiatement** le jeton de migration, retirer `SCOLIA_SIGNING_ADMIN_TOKEN` de l'environnement, puis désactiver ou retirer ce workflow de migration pour qu'il ne reste pas actif.
7. Sur la branche officielle, lancer une compilation signée par les secrets : l'empreinte avant et après la compilation APK doit correspondre exactement à celle ci-dessus. Tester une mise à jour sur un appareil équipé de la 1.2.17. Seulement ensuite supprimer **les anciens caches contenant les clés privées**, y compris celui de `refs/pull/18/merge`, depuis les paramètres GitHub Actions Caches, en identifiant soigneusement leurs références.

Le workflow est conçu pour **échouer sans modifier les secrets** si la PR, la portée du cache, l'empreinte du certificat ou les autorisations d'administration sont incorrectes. Il ne doit jamais créer une nouvelle clé de signature Android pour une mise à jour.

## Publication 1.2.18

La préparation Android est ensuite couverte par la PR **#61**. La publication officielle requiert **également** le certificat et les secrets de signature Windows Authenticode (`SCOLIA_WINDOWS_SIGNING_PFX`, `SCOLIA_WINDOWS_SIGNING_PASSWORD`) : ce sont des exigences **distinctes**, auxquelles la migration Android ne répond pas.

Ne pas fusionner la version 1.2.18 en contournant ces contrôles. La version déjà publiée 1.2.17 reste disponible et intacte jusqu'à la validation complète.
