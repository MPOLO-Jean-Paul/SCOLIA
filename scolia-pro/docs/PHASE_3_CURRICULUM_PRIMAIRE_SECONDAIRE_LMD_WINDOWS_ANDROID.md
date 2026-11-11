# SCOLIA Pro — phase 3 : référentiel pédagogique multi-établissements

**Plateformes : Windows et Android uniquement. iOS reporté.**

Cette phase crée les fondations de programmes pédagogiques sans substituer les
moteurs de notes, de délibération ou de paiement des établissements ITM/IM.

## École primaire et école secondaire

- Les cours sont des **branches annuelles** rattachées à une promotion et à une année.
- Les périodes d'évaluation proviennent du calendrier pédagogique (phase 2).
- Les périodes de paiement restent intégralement dans la table historique `Periodes`.
- Les référentiels annuels sont stockés dans `ProgrammesPedagogiques`, séparés
  des cours existants `Cours`. Il n'y a **aucune conversion automatique**.

## Universités et instituts supérieurs — LMD

- Structure : **faculté → département → filière** (`StructuresAcademiques`).
- Cursus d'une promotion : **UE** (unité d'enseignement) contenant un ou plusieurs
  **EC** (éléments constitutifs).
- Chaque UE et EC appartient exclusivement au **S1 ou S2**, jamais à un cours annuel.
- Crédits décimaux autorisés ; la somme des crédits des EC d'une UE doit être
  égale au crédit annoncé pour cette UE.
- Les notes des EC déterminent un relevé provisoire S1/S2.
- Le calcul `LmdFirstSessionCalculator` consolide la première session annuelle
  lorsque les deux semestres sont complets ; aucun « admis »/« ajourné »
  n'est inventé avant le paramétrage des règles officielles de jury.
- Les crédits des UE et des EC ne sont **pas comptés deux fois** dans les moyennes.
- La pondération par crédits est une synthèse informative, pas encore une
  décision académique juridiquement opposable.

## Terminologie

| Profil | Nomenclature |
|---|---|
| ITM/IM | Apprenant / Apprenants |
| Primaire | Élève / Élèves |
| Secondaire/technique | Élève / Élèves |
| Supérieur LMD | Étudiant / Étudiants |

Les routes et entités techniques existantes (notamment `api/apprenants`) restent
stables pour la rétrocompatibilité.

## Compatibilité, migrations et contrôle

Migration uniquement additive `20261111100000_CursusStructures` :
- **Nouvelles tables** : `StructuresAcademiques` et `ProgrammesPedagogiques`.
- **Aucune écriture** : `Apprenants`, `Inscriptions`, `Cours`, `Notes`,
  `Periodes`, `Promotions`, `Recus` et les données de synchronisation.
- Les relations utilisent `Restrict`, sans suppression en cascade vers le passé.
- Les nouveaux profils restent désactivés dans l'assistant tant que les
  moteurs de notes, bulletins, évaluations, rattrapage et droits ne sont pas prêts.

Les tests couvrent les branches annuelles, la hiérarchie LMD, les dépendances
UE/EC, les crédits, les cours annuels interdits au LMD, le relevé S1 intermédiaire,
la synthèse annuelle conditionnelle et la non-régression ITM.

**Prochaine étape** : services transactionnels de configuration des programmes,
périodes propres à chaque cycle, affectations des matières/UE aux promotions,
bulletins officiels personnalisés, décisions semestrielles et première session,
puis activation par profil après tests sur des bases anonymisées.

## Distribution

Le workflow de vérification iOS automatique est suspendu sur cette branche ;
le workflow manuel existant est conservé pour une reprise éventuelle.
Les distributions Windows/Android passent uniquement après les validations CI.
Cette phase **n'autorise pas** une fusion vers les branches de production.
