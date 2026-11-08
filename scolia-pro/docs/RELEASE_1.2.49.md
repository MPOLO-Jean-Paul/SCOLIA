# SCOLIA Pro 1.2.49 — chaîne de publication fiabilisée

## Signature et publication
- La version officielle est signée avec la clé historique de la PR #18 : même certificat que toutes les versions installées (empreinte SHA-256 `E5:B5:F7:FA:41:54:11:EE:AC:4B:F7:34:84:90:24:FF:CF:7F:93:50:0C:60:13:83:99:31:B0:20:38:F5:89:80`), mise à jour directe sur les téléphones.
- Les compilations de la branche de publication passent désormais sans les secrets de signature : elles servent de validation et ne sont jamais publiées ni jointes au paquet officiel.

## Application
- Contenu identique à la 1.2.48 : délibération complète (codes anonymes, encodage, comptes enseignants et privilège jury, épreuves du jury, procès-verbal, grille, bulletins vérifiables par QR).

## Déploiement
- Android versionCode **169**, version affichée **1.2.49** ; même identité de signature officielle que les versions précédentes.
- Aucune migration de base de données.
