# SCOLIA Pro — cadre métier des écoles privées et des complexes scolaires

Date de référence : 11 novembre 2026. **Plateformes ciblées : Windows et Android uniquement.**

## Sources légales principales

- Constitution de la RDC, article 43 : gratuité du primaire **dans les établissements publics** ; possibilité d'établissements privés agréés.
  https://maconstitution.cd/constitution-2006/article-43/
- Loi-cadre n° 14/004 du 11 février 2014, notamment :
  - article 17 : enseignement maternel / préscolaire ;
  - article 45 : établissements privés d'enseignement maternel, primaire, secondaire et professionnel ;
  - article 77 : la gratuité de l'éducation de base **ne s'applique pas** aux établissements privés agréés ;
  - article 180 : fixation des frais scolaires privés par le promoteur en concertation avec le comité des parents et le représentant des pouvoirs publics.
  https://www.droitcongolais.be/Legislation/Droit%20Public/enseignement/Loi14.004.11.02.2004.htm
- Décret n°14/029 du 18 novembre 2014 : l'éducation de base est obligatoire tant dans les établissements publics que privés ; la gratuité est attachée aux établissements publics.
  https://mail.leganet.cd/Legislation/Droit%20administratif/Enseignement/decret.14.029.18.11.2014.html
- Calendrier scolaire ministériel : les dates et la répartition des évaluations sont à définir **pour chaque année** et chaque cycle avant leur usage effectif.
  https://edu-nc.gouv.cd/calendrier-scolaire

**Important :** SCOLIA n'est pas un acte d'autorisation administrative ni un moteur automatique de conformité juridique. L'établissement demeure responsable de ses agréments et de ses tarifs.

## Cas 1 — école privée à cycle unique

L'établissement dispose d'un seul identifiant, d'un seul cycle (maternelle, primaire ou secondaire), d'une administration, d'une année scolaire, de classes et d'une caisse.

Les frais de scolarité privée sont **configurables**, et non gratuits par défaut. Les frais autres que la mensualité (examen, tenue, transport, carte...) ne sont pas imposés : ils restent dans le catalogue des catégories et les rubriques de frais par promotion.

## Cas 2 — complexe scolaire privé : une seule école, plusieurs cycles

La même organisation peut regrouper :
- maternelle + primaire ;
- primaire + secondaire ;
- maternelle + primaire + secondaire.

**Une base et un établissement** : identité, nom, logo, adresse, gestionnaire, utilisateurs, journal d'audit, sauvegardes et dossiers familiaux communs.

**Des cycles distincts** : code et agrément éventuel, classes et sections, programmes, calendrier d'évaluation et frais par cycle. Une classe appartient à un seul cycle, avec conservation lors de la reconduction annuelle. Les noms techniques de la section ITM existante ne sont pas réutilisés pour les cycles de l'école.

**Frais par cycle** : `SchoolCycleFeePolicy` est une grille de référence annuelle. La caisse existante conserve ses rubriques `FeeItem` par classe/promotion : aucune facturation nouvelle n'est automatiquement créée par l'ajout d'un cycle. La grille ne doit pas doubler les encaissements.

## Maternelle

Le préscolaire doit soutenir le suivi pédagogique et **l'observation** du développement, et ne pas forcer un examen chiffré ou un classement. Les périodes sont propres à la maternelle, et les observations seront rendues dans un carnet/bulletin approprié lors d'une étape ultérieure.

## Primaire / Secondaire

- Matières annuelles et évaluations par périodes distinctes.
- Le primaire peut être regroupé en trimestres et le secondaire en semestres conformément aux instructions de l'année.
- Une même étiquette `P1` peut exister à la fois au primaire et au secondaire sans collision, puisque les calendriers sont liés au **cycle + année**.
- Les divisions/options techniques du secondaire restent un niveau différent du cycle scolaire.

## Établissements publics : préparation, pas d'activation immédiate

Le domaine prévoit `SchoolGovernanceKind.Public`, mais **n'autorise pas** l'activation du modèle de mensualité privée pour un établissement public. Les règles financières officielles et provinciales doivent être intégrées et vérifiées séparément avant un usage opérationnel.

La configuration privée reste la valeur par défaut des nouveaux champs, y compris sur une ancienne installation ITM/IM ; le système actuel de mensualités et paiements ITM **n'est pas modifié**.

## État technique du lot

- Profil `Maternelle` et `ComplexeScolaire` reconnus mais **désactivés à l'installation**.
- `SchoolProfile.Governance` (privé par défaut) ; `SchoolCycle` (maternelle, primaire, secondaire) ;
  `SchoolCycleFeePolicy` (tarif facultatif, année, cycle) ; `SchoolCyclePeriod` (périodes d'évaluation propres au cycle).
- `Promotion.SchoolCycleId` nullable pour les anciennes classes ITM/IM.
- Migration additive `20261111140000_ComplexesScolairesPrives`, sans changement des données existantes de paiements ou de notes.
- Affichage et terminologie adaptés : `élève(s)` au niveau scolaire ; `apprenant(s)` demeure réservé à ITM/IM ; `étudiant(s)` au LMD.
- Les options de maternelle et complexe scolaire sont proposées dans la liste comme **à venir**.

## Obligations avant activation et publication

1. Écrans Avalonia de création/édition des cycles et de validation des tarifs du privé, respectant les composants de l'administrateur/secrétariat.
2. Rattachement obligatoire d'une classe au cycle et des enseignants à leurs classes, sans divulgation de dossiers d'autres cycles.
3. Calendrier de chaque cycle validé pour chaque année, y compris carnets d'observation de la maternelle.
4. Bulletins et règles d'évaluation spécifiques, validation pédagogique et contrôles d'accès.
5. Contrôle d'une ancienne base ITM/IM : inscriptions, mensualités, reçus, synchronisation et sauvegardes identiques.
6. Compilation, tests et distribution **Windows + Android uniquement**.
7. Ne pas confondre le regroupement scolaire maternelle-primaire-secondaire et le LMD supérieur.

Aucune fusion ni release automatique depuis cette PR de développement.
