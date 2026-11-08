# SCOLIA Pro 1.2.53 — sauvegardes toujours accessibles

## Amélioration de sécurité et continuité scolaire

- En mode **licence lecture seule**, le responsable disposant de la permission **Gestion des sauvegardes** peut toujours **exporter sous forme chiffrée une sauvegarde déjà créée**, pour protéger les archives scolaires et les transférer de façon sécurisée.
- Le transport vérifié du serveur (liaison locale approuvée ou HTTPS) et la phrase secrète restent obligatoires ; le système ne donne aucun droit supplémentaire aux utilisateurs non autorisés.
- Les écritures scolaires, encaissements, restaurations, imports de sauvegarde, suppressions et nettoyages restent **interdits** tant que la licence est en lecture seule.
- Tests de non-régression pour vérifier qu'aucune commande de modification ne peut se faire passer pour un export.

## Console propriétaire JP-Services

- Le code de la console **0.1.2**, avec contrôle Cloudflare Access et jeton propriétaire et pipeline D1, a été intégré à la branche officielle avant cette version.
- **La console n'est pas automatiquement rendue publique par l'APK** : son déploiement Cloudflare exige toujours une infrastructure réelle, des secrets protégés et la validation préalable des contrôles de sécurité.
- Les établissements existants ne sont pas facturés ni verrouillés sans activation explicite par JP-Services.

## Installation et signature

- Android : `ApplicationVersion` 173 ; **conserver le certificat historique** de l'application Android. Ne jamais installer un APK de validation signé avec une clé temporaire à la place de l'APK officiel.
- Windows : installer la nouvelle version par-dessus la version précédente. Conserver une sauvegarde chiffrée avant une mise à jour.
- Aucune suppression ou migration destructive des données SQLite.
- Les paquets APK et EXE ne sont officiels qu'après compilation, contrôle des empreintes SHA-256 et publication sur GitHub Releases.
