# Signature officielle Android de SCOLIA Pro — récupération depuis la PR #18

**Repère de publication vérifié le 7 novembre 2026.** Cette procédure est applicable aux versions suivantes tant que la clé n'a pas été transférée dans les secrets GitHub Actions du dépôt.

## Source et identité du certificat
- Dépôt : `MPOLO-Jean-Paul/SCOLIA` (et non `gestion-scolaire`).
- Pull request historique : [PR #18](https://github.com/MPOLO-Jean-Paul/SCOLIA/pull/18), **head** `release/direct-apk-scolia-pro-20261103`, **base** `feature/scolia-recovery-login-lock-20261103`.
- Cache **privé au contexte PR18** de GitHub Actions, clé : `scolia-android-signature-v1`, chemin de travail `~/.scolia-signature` ; deux fichiers attendus : `scolia.keystore`, `mot-de-passe`.
- Alias de signature Android : `scolia`.
- **Empreinte SHA-256 attendue du certificat :**
  `E5:B5:F7:FA:41:54:11:EE:AC:4B:F7:34:84:90:24:FF:CF:7F:93:50:0C:60:13:83:99:31:B0:20:38:F5:89:80`.
- La 1.2.36 a été publiée avec ce certificat. **Une autre clé signerait un APK incompatible avec les versions déjà installées.**

## Mécanisme technique et contrôle de sécurité
1. La branche officielle contient la nouvelle version dans `scolia-pro/Directory.Build.props` et `scolia-pro/src/Scolia.Mobile/Scolia.Mobile.csproj` (AndroidVersionCode strictement croissant), ainsi que `scolia-pro/docs/RELEASE_<version>.md`. Ne jamais remplacer un tag déjà publié.
2. La branche `release/direct-apk-scolia-pro-20261103` reste le **head de la PR18 ouverte**. Après un push, GitHub Actions exécute le workflow `.github/workflows/scolia-natif.yml` dans le contexte `refs/pull/18/merge`.
3. Le workflow définit `SCOLIA_RECOVERY_PR18=true` **uniquement** lorsque le numéro de PR, le dépôt du head, le head et la base correspondent exactement aux valeurs attendues. Il checkout explicitement le **SHA du head de publication** et non un merge non vérifié.
4. L'action `actions/cache/restore@v4` charge le cache `scolia-android-signature-v1`. Son absence provoque un **échec bloquant**, jamais une génération automatique de clé pour la distribution.
5. Le script `scolia-pro/scripts/recuperer-cle-android-pr18-1226.sh` exige le contexte PR18, le cache présent, la branche et le dépôt attendus, et une version officielle >= 1.2.36. Il vérifie l'alias et l'empreinte avec `keytool`, puis n'installe le keystore que dans le stockage temporaire isolé du job. Le mot de passe est masqué dans les logs. Le cache restauré est supprimé du dossier après copie.
6. `scripts/publier-android.sh` compile l'APK. Le workflow exécute ensuite `apksigner verify --print-certs` et compare **l'empreinte du paquet produit** au certificat historique. Un écart interrompt la publication.
7. Les tests API, les tests d'interface, les builds Windows et Android, les empreintes SHA-256 des fichiers et la cohérence de la tête de branche sont nécessaires avant la commande `gh release create`. La release est publiée depuis la PR18 via `android-publication-officielle`.
8. La publication via **push** de la branche officielle exige deux secrets GitHub Actions : `SCOLIA_ANDROID_KEYSTORE` et `SCOLIA_ANDROID_KEYSTORE_PASS`. En leur absence, ce job échoue volontairement ; la **PR18 est la voie de récupération autorisée**, si son cache est encore accessible.

## Diagnostic
- **`Les secrets ... sont obligatoires en production`** sur le workflow `push` : les secrets GitHub ne sont pas configurés. Vérifier la compilation depuis la PR18 ; ne pas utiliser une clé générée localement.
- **`Cache not found`** sur la PR18 : vérifier que la PR18 est ouverte et que le contexte de cache d'origine existe. Ne jamais remplacer la clé par une autre.
- **`Source de distribution PR18 non autorisee`** : vérifier le numéro de la PR, les noms exacts des deux branches, le contexte `pull_request`, le dépôt, la version et le cache.
- **`Empreinte ... differe`** : interrompre immédiatement la distribution et contrôler l'origine de la clé sans afficher ni transférer celle-ci dans les logs.
- **Signature Android de validation éphémère** : cette compilation sert aux tests seulement ; ne jamais l'installer sur les postes déjà équipés de SCOLIA Pro officiel.
- **`tag deja publie`** : version immuable. Incrémenter la version et préparer des notes plutôt qu'écraser les APK/EXE précédents.

## Pérennisation recommandée — encore à réaliser
Transférer **une seule fois et de façon confidentielle** le keystore historique et son mot de passe vers les secrets protégés `SCOLIA_ANDROID_KEYSTORE` et `SCOLIA_ANDROID_KEYSTORE_PASS` du dépôt, puis vérifier la même empreinte lors d'une compilation sur la branche de publication. **Ce transfert n'est pas considéré comme effectué.** Le cache PR18 est un mécanisme de récupération, pas une sauvegarde permanente garantie. Ne jamais committer, afficher, télécharger dans les artefacts ou envoyer dans le chat le keystore ou le mot de passe.

## Versions vérifiées
- 1.2.36 : release antérieure publiée avec le certificat historique.
- 1.2.37 : récupération depuis PR18 adaptée pour la publication ; vérifier les résultats de GitHub Actions et la présence réelle des deux actifs dans GitHub Releases avant de déclarer la publication terminée.
