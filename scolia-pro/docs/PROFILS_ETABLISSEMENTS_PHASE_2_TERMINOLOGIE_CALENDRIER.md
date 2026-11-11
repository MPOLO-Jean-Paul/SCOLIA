# SCOLIA Pro — étape 2 : terminologie et calendrier pédagogique (Windows + Android)

## Demande métier
- ITM/IEM : `apprenant` / `apprenants`.
- Primaire et secondaire/technique : `élève` / `élèves`.
- Université et établissements supérieurs : `étudiant` / `étudiants`.
- Aucun travail de livraison iOS demandé.

## Fonctionnement ajouté
1. Le serveur retourne le profil de l'établissement dans `/api/session` ;
   les anciens clients restent compatibles grâce à une propriété optionnelle.
2. Les clients Windows et Android partagent une seule interface Avalonia :
   la navigation « Apprenants », le titre de la liste, les états vides et
   certains indicateurs du tableau de bord utilisent le lexique du profil.
   La session mise en cache conserve le profil pour l'utilisation hors ligne.
3. Les identifiants techniques `Student`, `Apprenants` et `/api/apprenants`
   ne changent pas : aucune table de données scolaires ne doit être renommée.
4. `PedagogicalPeriod` est une nouvelle entité persistée séparément de
   `AcademicPeriod` (mensualités), avec type, numéro, dates, nom et verrouillage.
   Sa table SQLite `PeriodesPedagogiques` est ajoutée par migration additive.
5. Aucune période n'est créée automatiquement pour un établissement existant.
   Les règles ITM/IEM sont inchangées ; les profils primaire, secondaire et LMD
   ne sont pas activés par cette étape.

## À terminer dans les prochaines étapes
- Adapter tous les messages et les documents PDF/Excel, notamment les cartes,
  reçus, rapports et bulletins, en conservant les anciens modèles ITM.
- Ajouter les interfaces sécurisées d'édition des périodes et les validations
  d'absence de chevauchement avant de créer une période en base.
- Introduire les cursus scolaires, les programmes de branches annuelles,
  les UE/EC et crédits LMD, puis les moteurs de résultats spécifiques.
- Vérifier avec les programmes officiels en vigueur et des établissements pilotes
  les découpages scolaires et les modèles de documents avant activation.

## Vérifications avant fusion
- Tests unitaires : lexique, alias historique ITM/IM, semestres LMD et dates.
- Tests API, build serveur et Avalonia, migrations en cohérence avec le modèle EF.
- Test d'une copie anonymisée de base ITM/IEM pour prouver qu'aucun résultat,
  cours, inscription, paiement ni reçu n'est altéré.
- Contrôles sur Windows et Android, dont mode hors ligne.

Aucune fusion, publication ou exécutable de production n'est demandé à cette étape.
