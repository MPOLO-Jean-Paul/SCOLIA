# SCOLIA Pro — profils pédagogiques : phase 1 (socle sécurisé)

## Objectif
Préparer les profils ITM/IM, primaire, secondaire/technique et supérieur LMD sans changer
les règles actuellement utilisées dans les ITM/IM.

## Périmètre livré dans cette branche
- Ajout de `SchoolProfile.InstitutionType` (valeur initiale `ItmIm`).
- Migration SQLite additive `20261111060000_InstitutionProfiles`: nouvelle colonne texte
  `Etablissement.InstitutionType` non nulle, valeur par défaut `ItmIm`.
- Contrat API rétrocompatible : les anciens clients qui n'envoient pas de profil
  restent ITM/IM ; la consultation de l'établissement restitue `institutionType`.
- Assistant Windows : liste des quatre catégories avec disponibilité clairement indiquée.
- Sécurité métier : seule `ItmIm` peut être activée au cours de cette phase.
  Une tentative de création primaire/secondaire/LMD est refusée par le serveur,
  même si elle contourne l'interface.
- Le profil n'est pas modifiable par l'API classique des paramètres d'établissement.
- Tests unitaires de valeurs par défaut et de disponibilité.

## Invariants de non-régression
1. Aucune table de notes, de paiements, de délibérations, de promotions ou de
   synchronisation n'est modifiée par cette migration.
2. Les écoles existantes héritent automatiquement de `ItmIm`.
3. Les formules de délibération, les règles financières, les cartes et les
   inscriptions ITM restent inchangées.
4. Aucune modification automatique du profil d'une école déjà configurée.
5. Aucune publication officielle ni fusion implicite.

## Étapes suivantes avant activation
- **Phase 2** : refonte des périodes pédagogiques (distinctes des mois de paiement),
  sous-profils secondaires et référentiel de cours annuels ; programmes et
  modèles de bulletins vérifiés avec les textes et établissements de référence.
- **Phase 3** : moteurs primaire et secondaire, examens, bulletin, décision annuelle.
- **Phase 4** : modèle LMD (facultés, départements, filières, promotions, UE, EC,
  crédits, S1/S2), résultats partiels semestriels, consolidation de première session,
  rattrapages, capitalisation et règles officielles de jury.
- **Phase 5** : activation progressive des profils, droits, documents, statistiques,
  synchronisation, tests et livraisons Android/Windows.

## Vérifications obligatoires
- Compiler `Scolia.sln` avec la version SDK définie dans `global.json`.
- Exécuter `dotnet test` (unitaires et API) et les tests de migration sur une
  copie de base ITM réelle anonymisée.
- Comparer les résultats ITM avant/après sur les mêmes cours et promotions.
- Vérifier l'interface de première installation sur Windows et petits écrans.
- Tester les clients Android anciens pendant la période de transition.
- Ne fusionner ni publier avant validation de ces points.

> Cette phase **n'active pas encore** les établissements primaire, secondaire
> ni universitaire. Elle fournit une fondation exploitable sans imposer
> leurs règles aux établissements ITM en production.
