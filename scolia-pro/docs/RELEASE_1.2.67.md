# SCOLIA Pro 1.2.67 — Affichage natif de l'espace enseignant

## Correction ciblée Android et Windows

Cette mise à jour conserve les composants Avalonia et les mêmes ressources visuelles que le secrétariat.

### Accueil enseignant

- **Carte Aperçu** : les quatre tuiles (« Mes cours », « Cotes encodées », « Feuilles remises », « Encodage ») retrouvent des fonds opaques et colorés, avec des pictogrammes contrastés.
- Le pinceau des icônes utilise désormais `IconPalette.Get`, qui ne dépend pas d'une résolution tardive de couleur de thème et conserve sa couleur en modes clair et sombre.
- Les tuiles de l'aperçu adoptent la dimension `appicon large` utilisée dans les autres cartes natives ; séparateurs et alignements sont adaptés.
- **Progression d'un cours** : barre plus visible, hauteur 6 et couleur violette cohérente avec la tuile du cours.
- **Informations** : accordéon « Règles de l'encodage » étendu horizontalement avec pictogramme de sécurité bleu pétrole, fond du groupe et espacement cohérents.
- Aucun changement dans les droits de l'enseignant, les encaissements, les notes, la synchronisation ou la structure SQLite.

### Validation

- Tests d'interface assurant l'utilisation de la palette permanente pour les quatre indicateurs et la présence des composants natifs.
- Version Android `187` (affichage `1.2.67`) ; application Windows `1.2.67`.
- APK Android signé avec la clé historique vérifiée par la chaîne GitHub ; installateur Windows non signé Authenticode.
- Publication uniquement après validation des tests et empreintes SHA-256.
