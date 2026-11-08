# SCOLIA Pro 1.2.55 — grilles de délibération dynamiques par promotion

## Grille unique, onglets automatiques
- Nouvelle commande **Délibération → Grilles de toutes les promotions (Excel)**, réservée au responsable autorisé du jury.
- Chaque téléchargement interroge l'année courante et crée automatiquement **un onglet pour chaque promotion réellement configurée** (même si elle a été ajoutée après l'installation). Aucun nom ou nombre de classes INF/ACC n'est codé en dur.
- En-tête reprenant le **nom de l'établissement configuré**, la promotion, la période de délibération, l'année scolaire et les cours réellement attribués à la promotion.
- Lignes issues des **inscriptions de l'année**. Le code anonyme existant, les cotes encodées, maxima, sous-totaux, total, pourcentage et décision viennent des mêmes calculs du serveur que la délibération.
- Présentation inspirée de la grille Excel fournie : colonnes de cours, grandes rubriques, codes, identités, maxima et blocs de résultats ; bordures, affichage et impression, feuille par promotion, moyenne par colonne recalculable.
- **Aucune cote ni aucun code fictif**. Si un apprenant n'a pas reçu son code officiel, l'export est refusé avec une explication permettant au jury de l'attribuer d'abord (l'attribution est journalisée).
- Une nouvelle promotion ne nécessite ni mise à jour du logiciel ni ajout manuel d'onglet : elle apparaît au prochain export. La modification des cours ou cotes se reflète au prochain export.

## Document individuel
- Les bulletins de délibération restent disponibles en PDF A4, avec une fiche par apprenant sur Windows ; le mode serveur Android doit être vérifié séparément pour l'impression groupée.

## Compatibilité et sécurité
- Aucune migration destructrice de la base SQLite. Les classeurs exportés sont des copies : leur modification ne change pas les cotes enregistrées dans SCOLIA.
- Les identités associées aux codes ne sont diffusées qu'aux comptes ayant la permission **Gérer les délibérations**.
- Version Android 1.2.55, versionCode 175. La signature historique Android doit être reprise depuis la procédure GitHub Actions PR #18 ; jamais de signature de validation pour une publication.
- Publication seulement après tests, compilation et vérification des empreintes SHA-256.
