# SCOLIA Pro 1.2.54 — continuité des sauvegardes sous licence en lecture seule

Cette mise à jour inclut les évolutions de la version 1.2.53 relatives aux délibérations et aux comptes enseignants.

## Correctif
- Lorsqu'une licence place le serveur en **lecture seule**, un responsable disposant de la permission de gestion des sauvegardes peut toujours exporter **une sauvegarde déjà existante** sous forme d'archive chiffrée.
- Cette opération exige toujours une liaison sûre, une phrase secrète et les permissions de sauvegarde ; aucun accès supplémentaire n'est accordé aux utilisateurs non autorisés.
- Les modifications scolaires, paiements, importations, restaurations, effacements, nettoyages et autres écritures restent bloqués conformément à la politique de licence.
- Tests de non-régression sur les chemins proches et les méthodes HTTP pour éviter les contournements.

## Console propriétaire
Le code de la console 0.1.2, son accès Cloudflare Access + jeton administrateur et son workflow de production protégé sont présents dans le dépôt. La console n'est pas considérée comme mise en production sans configuration réelle de Cloudflare D1, des secrets et de l'authentification multifacteur ; cette version n'active pas la facturation des établissements sans consentement.

## Distribution
- Android : version affichée 1.2.54, versionCode 174. **Le certificat de signature historique doit être conservé.** Toute distribution signée provisoirement est interdite comme mise à jour officielle.
- Windows : installateur 1.2.54 à installer par-dessus la version existante.
- Les bases scolaires SQLite ne sont ni supprimées ni migrées de manière destructive.
- Le paquet ne doit être annoncé comme disponible qu'après compilation, contrôles SHA-256 et publication réelle sur GitHub Releases.
