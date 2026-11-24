# SCOLIA Pro 1.2.92 — correctif de mise à jour Android refusée

## Correctif vérifié dans l'APK 1.2.91
L'APK officiel SCOLIA Pro 1.2.91 a été publié avec `versionCode=3627649` mais `versionName=1.2.90` (le numéro interne et le certificat sont corrects). Le mécanisme de vérification pouvait proposer à nouveau la 1.2.91, puis afficher « Mise à jour refusée : APK incorrect, non signé comme attendu ou version obsolète » car la même version interne était déjà installée.

## Changements
- `Directory.Build.props` et `Scolia.Mobile.csproj` utilisent tous deux **1.2.92**.
- Le vérificateur de mises à jour emploie la plus récente des versions connues : Android `versionName` ou version embarquée dans l'application.
- Les téléchargements d'une version déjà installée sont nettoyés, même si Android affiche encore un ancien `versionName`.
- Le script de publication bloque la sortie si `versionName` et la version officielle diffèrent.
- Tests de non-régression dédiés.
- Aucun changement aux modèles des cartes, données scolaires, bases SQLite, synchronisation, ni au fonctionnement de la console administrateur.

## Compatibilité et précautions
- Même identifiant Android `com.jpservices.scolia` et même certificat historique (SHA-256 : `E5:B5:F7:FA:41:54:11:EE:AC:4B:F7:34:84:90:24:FF:CF:7F:93:50:0C:60:13:83:99:31:B0:20:38:F5:89:80`).
- Le `versionCode` doit être **strictement supérieur** à 3627649 lors de la publication.
- Ne pas désinstaller l'application pour corriger un simple refus dans son vérificateur ; installer l'APK officiel supérieur par-dessus l'officiel existant.
- Une **ancienne préversion signée avec une clé temporaire** ne peut pas recevoir directement l'APK officiel. Sauvegarder et vérifier les données hors de l'application *avant* toute migration vers la version officielle ; Android refusera légitimement une signature différente.
- Vérifier l'installation sur un téléphone réel (Android 16 inclus) ; les tests CI seuls ne garantissent pas l'absence de particularités du fabricant.

## Publication
Publier uniquement après validation des tests GitHub Actions. Utiliser le mécanisme de certificat historique de la PR #18 et publier de manière immuable sous `scolia-pro-v1.2.92`. Vérifier après compilation l'empreinte du certificat Android, `versionCode`, `versionName`, packageName, le SHA-256 des APK/EXE et le chemin de mise à jour sur une installation 1.2.90/1.2.91.
