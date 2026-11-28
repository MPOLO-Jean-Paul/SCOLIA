# SCOLIA Pro — transfert permanent de la signature Android vers GitHub Actions Secrets

## Invariant de distribution

La version 1.2.17 a ete publiee avec la cle historique. **Toute mise a jour Android officielle doit conserver la meme cle privee et le meme certificat.** La regeneration d'une cle, meme avec le meme alias `scolia`, casserait les mises a jour installees.

Empreinte SHA-256 attendue (certificat public, pas une cle privee) :

`E5:B5:F7:FA:41:54:11:EE:AC:4B:F7:34:84:90:24:FF:CF:7F:93:50:0C:60:13:83:99:31:B0:20:38:F5:89:80`

## Secrets obligatoires

Dans le depot `MPOLO-Jean-Paul/SCOLIA`, creer **deux secrets GitHub Actions** :

- `SCOLIA_ANDROID_KEYSTORE` : contenu du fichier `scolia.keystore` historique encode en Base64, sans nouvelle ligne.
- `SCOLIA_ANDROID_KEYSTORE_PASS` : mot de passe exact du keystore historique.

Les deux workflows `.github/workflows/scolia-natif.yml` et `.github/workflows/scolia-pro.yml` refusent la publication officielle si l'un de ces secrets est absent. Ils verifient aussi l'empreinte du certificat et, apres compilation, celle de l'APK. Les PR continuent d'utiliser une cle ephemere de validation non distribuable.

**Ne jamais** placer un keystore, un mot de passe, un export de secrets ou des fichiers `.env` contenant la cle dans le depot, des logs, une issue, un commentaire ou un artefact public.

## Enregistrement une fois depuis un poste de confiance

Cette operation exige l'original du keystore historique (ou une sauvegarde privee identique), son mot de passe et un compte GitHub avec autorisation de gerer les secrets du depot. Ne pas inventer ni reconstituer le mot de passe.

1. Installer GitHub CLI (`gh`), Java (`keytool`) et OpenSSL sur un poste de confiance ; executer `gh auth login`.
2. Depuis le dossier `scolia-pro`, executer :

   ```bash
   bash scripts/enregistrer-secrets-android-github.sh /chemin/prive/scolia.keystore
   ```

3. Saisir le mot de passe dans le terminal (la saisie reste masquee). Le script verifie **avant tout envoi** la correspondance du certificat historique.
4. Verifier uniquement les noms et les dates des secrets (jamais leur contenu) :

   ```bash
   gh secret list --repo MPOLO-Jean-Paul/SCOLIA --app actions
   ```

Alternative manuelle : dans GitHub, ouvrir le depot > **Settings > Secrets and variables > Actions > New repository secret**, puis enregistrer ces deux noms avec leur valeur. Ne jamais envoyer leurs valeurs dans une conversation.

## Validation avant la prochaine release

1. Fusionner le changement de workflows dans `release/direct-apk-scolia-pro-20261103` apres avoir ajoute les secrets.
2. Verifier les jobs declenches par le **push de fusion** sur la branche officielle : ils doivent utiliser les secrets et confirmer l'empreinte Android. Un lancement manuel (`workflow_dispatch`), meme depuis cette branche, est traite comme une **validation** et ne produit pas d'APK distribuable.
3. Ne publier la version suivante qu'apres verification de l'empreinte, du `versionCode` croissant et de la possibilite de mise a jour d'un appareil de test deja equipe de la version officielle.
4. Conserver une copie de secours **chiffree et hors depot** du keystore et de son mot de passe, avec controle d'acces limite. Une fois le chemin de publication confirme, supprimer l'ancien cache de signature depuis les parametres GitHub Actions et ne plus utiliser l'ancien script `recuperer-cle-android-cache-1217.sh`.

## Limite de migration depuis un cache GitHub Actions

La cle n'est pas recuperable depuis un secret existant : GitHub ne permet pas de relire les valeurs de Secrets. Le `GITHUB_TOKEN` d'un workflow ordinaire n'a pas l'autorisation d'administration permettant de creer ces secrets. Si **l'unique copie** du keystore est dans le cache historique Actions, la recuperation doit etre organisee par un administrateur de confiance, vers un emplacement prive, sans publication en artefact ni sortie de log. Ce transfert n'est pas realise par la modification du YAML.

**Ne pas activer la prochaine distribution officielle tant que les deux secrets n'ont pas ete enregistres et testes.** La version 1.2.17 publiee reste inchangee.
