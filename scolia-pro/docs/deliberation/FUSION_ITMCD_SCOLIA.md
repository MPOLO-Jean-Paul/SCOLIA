# Délibération dans SCOLIA Pro — audit et fusion avec ITMCD

Ce document résume l'audit des deux applications de gestion scolaire et ce qui a été fusionné dans SCOLIA Pro
pour la gestion des délibérations.

Sources étudiées :

- **Délibération** : le dépôt `MPOLO-Jean-Paul/ITMCD` (application PHP/MySQL, `admin/deliberation.php`,
  règles de calcul dans `config/db.php`) ;
- **Item CD** : la branche `claude/item-cd-mobile-app-ulf4ju` de SCOLIA (`itmcd-pro/docs/ANALYSE_FONCTIONNEMENT_ITMCD.md`
  et `itmcd-pro/docs/CONCEPTION_ITMCD_PRO.md`) ;
- **SCOLIA Pro** : d'abord la branche `release/scolia-pro-1-2-37-20261107`, puis la version la plus récente,
  **1.2.42** (`release/direct-apk-scolia-pro-20261103`), sur laquelle le module est porté
  (branche `feature/deliberation-scolia-pro-1-2-42-20261107`).
- Le dépôt `MPOLO-Jean-Paul/Deliberation` a été examiné le 7 novembre 2026 : il est encore vide (aucun commit).

---

## 1. Ce que fait chaque application

| | ITMCD (délibération) | SCOLIA Pro |
|---|---|---|
| Nature | Registre des cotes et moteur de délibération | Gestion du secrétariat : apprenants, inscriptions, finances, personnel, scolarité |
| Apprenant | Une ligne par apprenant **et par année** (nouveau code chaque année) | Dossier permanent + inscription annuelle |
| Cours | Maxima (1 à 2000), semestre S1 / S2 / ANNUEL, un enseignant | Numéro, intitulé, coefficient, volume horaire, cotation maximale, attribution avec historique |
| Cotes | `cote_s1`, `cote_s2`, moyenne d'appui sur 10 | Évaluations pondérées, moyennes sur 20 |
| Décision | Admis si ≥ 60 % et aucune cote manquante ; mentions A à E | Moyenne générale sur 20 ; « Admis » dès 10 |
| Délibération | Recalcul confirmé par mot de passe, jury annuel qui **corrige les cotes** | Absente |
| Codes | `code_unique` aléatoire (format L D D L D L), saisie par code | Matricule permanent |
| Traçabilité | Aucune | Journal d'audit chaîné, sauvegardes, droits par rôle |

## 2. Fonctionnalités retenues (les meilleures de chaque côté)

**Reprises d'ITMCD, à l'identique** : maxima par cours ; organisation S1 / S2 / annuel ; moyenne d'appui
(si la cote ne dépasse pas 75 % du maxima : + appui/10 × 25 % du maxima, plafonné) ; échec d'un cours sous la
moitié du maxima ; décision à 60 % sans cote manquante ; mentions A ≥ 90, B ≥ 80, C ≥ 70, D ≥ 60, E ;
recalcul immédiat ; interrupteurs d'encodage S1 / S2 / appui ; saisie par code ; confirmation par mot de passe ;
statistiques par promotion et par cours ; grille des cotes PDF et Excel ; passage de classe par promotion suivante.

**Gardées de SCOLIA** : apprenants et inscriptions existants (aucune ressaisie), cours et enseignants déjà
attribués, filtrage strict par année scolaire, journal d'audit chaîné, droits par rôle, documents PDF/Excel
du serveur, réinscriptions.

**Corrigées grâce à l'étude (dossier Item CD)** :

| Faiblesse d'ITMCD | Correction dans SCOLIA |
|---|---|
| Années mélangées dans la délibération et la grille | Tout est calculé sur les inscriptions de l'année courante |
| Quatre formules de calcul différentes selon l'écran | Un seul moteur, `DeliberationEngine`, pour tous les écrans et documents |
| Code aléatoire sans contrôle d'unicité, deux formats | Code tiré au hasard cryptographique, unique dans l'année, jamais réattribué ; un code officiel ITMCD peut être saisi tel quel |
| Le jury écrase les cotes pour repêcher | Décision motivée du jury ; la cote de l'enseignant n'est jamais modifiée |
| Délibération jamais figée, aucun PV | Clôture : résultats figés, cotes verrouillées, procès-verbal ; réouverture avec motif et mot de passe |
| Cours annuel encodé au seul S1 = S1/2, compté comme coté | Compté « cote manquante » au jury annuel |
| Échec et cote manquante confondus pendant l'encodage | Résultat « Incomplet » distinct ; à la clôture, incomplet sans décision du jury = ajourné |
| Aucune trace des modifications | Chaque cote, décision, clôture et réouverture est au journal d'audit avec l'ancienne valeur |

## 3. Ce qui est en place

### Codes anonymes (import des apprenants existants)

- « Importer et attribuer les codes » reprend les inscriptions actives de l'année (toutes promotions ou une seule)
  et donne à chaque apprenant sans code un **code officiel** au format ITMCD (`P13W5R`). Relancer l'opération
  ne change aucun code existant.
- « Saisir un code officiel » enregistre le code qu'un apprenant porte déjà (par exemple `ITM-26-AB12`).
- Remplacer un code exige le droit du jury et un motif ; l'ancien code reste réservé et n'est plus reconnu.
- La liste des codes (PDF, Excel) est confidentielle : seul le jury (`deliberation.gerer`) la voit. Le procès-verbal
  ne porte jamais les codes.

### Encodage des cotes

- Chaque cours indique son organisation (S1, S2, annuel) dans « Cours et notes » et « Attributions et charges ».
- Feuille par cours : cote du S1, du S2 ou moyenne d'appui, avec la cote retenue (appui compris) en regard ;
  case vidée = cote effacée.
- **Feuille anonyme** : triée par code, sans nom ni matricule.
- **Saisie par code anonyme** : code de la copie + cote, en série ; le nom n'est jamais affiché.
- Contrôles : 0 à maxima (0 à 10 pour l'appui), deux décimales, semestre accepté par le cours, encodage ouvert,
  période non clôturée, apprenant inscrit dans la promotion du cours.

### Remise des cotes et suivi de l'encodage (lot 2, repris d'ITMCD)

- **Remise** d'une feuille (S1, S2 ou appui) : refusée tant qu'il manque une cote (l'appui est facultatif).
  Après la remise, la feuille ne change plus que par le jury ; chaque correction est tracée « après la remise ».
  C'est le pendant de l'interrupteur « mise à jour des cotes » d'ITMCD, mais par feuille et avec une trace.
- **Renvoi** à l'enseignant par le jury, motif obligatoire ; impossible si la délibération est clôturée.
- **Suivi de l'encodage** (moniteur d'ITMCD) : par cours et par cote attendue, enseignant, cotes encodées,
  échecs, moyenne et état — à encoder, en cours, complet, remis, figé. Filtre « feuilles à relancer », export PDF/Excel.
- **Relevé du cours** et **liste des échecs** (export enseignant d'ITMCD), en PDF ou Excel.

### Reprise des codes officiels existants (lot 2)

- « Reprendre des codes officiels » : une liste `matricule ou nom ; [promotion ;] code`, collée depuis Excel
  ou lue dans un CSV (UTF-8 ou Windows-1252), est rattachée aux inscriptions de l'année.
- Aperçu d'abord, sans rien enregistrer ; seules les lignes reconnues sont attribuées. Les autres sont
  signalées : introuvable, ambigu (homonymes : ajouter la promotion), code invalide, code pris (jamais
  réattribué), code différent (remplacement manuel avec motif), doublon dans la liste.

### Délibération et jury

- Tableau par promotion et par période (premier semestre, second semestre, jury annuel) : rang, points,
  pourcentage, cotes manquantes, cours en échec, mention, résultat calculé, décision.
- Statistiques de la promotion et par cours (réussites, échecs, cotes manquantes, moyenne).
- « Cas à examiner » : dossiers proches du seuil ou avec une seule cote manquante.
- Décision motivée : « Admis par délibération » ou « Ajourné par délibération », motif obligatoire.
- Clôture : mot de passe, président et membres du jury ; résultats figés et cotes de la période verrouillées
  (le jury annuel fige tout). Réouverture : mot de passe et motif de 10 caractères au moins.
- Documents : procès-verbal (PDF, « provisoire » tant que la session est ouverte), grille des cotes (PDF paysage
  et Excel), palmarès, bulletin de délibération par apprenant.
- Réglages de l'année : encodage S1, encodage S2, moyennes d'appui, seuil de réussite (60 % par défaut).

### Passage de classe

Les réinscriptions lisent le jury annuel clôturé de l'année précédente : un admis est proposé dans la promotion
suivante, un ajourné dans sa promotion (redoublement, promotion créée automatiquement si besoin). La décision
s'affiche à côté de la promotion actuelle.

## 4. Où se trouve le code

| Couche | Fichiers |
|---|---|
| Domaine | `src/Scolia.Domain/Deliberation/` (entités, `DeliberationEngine`, `AnonymousCodeRules`) ; `Course.Semester` |
| Persistance | tables `CodesAnonymes`, `CotesDeliberation`, `ReglagesDeliberation`, `SessionsDeliberation`, `DecisionsJury`, `ResultatsDeliberation` ; migration `Deliberation` |
| Application | `DeliberationService`, `DeliberationDtos`, `DeliberationText` ; passage de classe dans `StudentService` |
| API | `/api/deliberation/...` dans `ScoliaEndpoints.MapDeliberation` ; lot 2 : `remise`, `renvoi`, `suivi`, `suivi/export`, `feuille/export`, `codes/import` |
| Bureau (PC et téléphone) | groupe « Délibération » : `AnonymousCodesPage`, `MarksPage`, `EncodingProgressPage`, `DeliberationPage` ; onglet Scolarité du ruban |
| Tests | `DeliberationEngineTests` (cas de référence du dossier ITMCD Pro), `DeliberationApiTests` (parcours complet) |

Droits : lecture avec `scolarite.lire`, encodage avec `scolarite.ecrire`, codes, décisions, clôture,
réouverture et réglages avec le nouveau droit `deliberation.gerer` (accordé au secrétariat).

## 5. Décisions prises (modifiables)

1. Cours annuel encodé au seul S1 : **cote manquante** au jury annuel (ITMCD comptait S1/2).
2. Le jury peut admettre sous le seuil, avec un motif obligatoire (« Admis par délibération »).
3. Le rang n'est donné qu'aux dossiers complets ; les ex æquo partagent le rang.
4. Les codes anonymes sont annuels, comme dans ITMCD ; le matricule SCOLIA reste l'identifiant permanent.

## 6. Prochains lots proposés

1. Comptes enseignants (portail enseignant d'ITMCD) : compte relié à la fiche du personnel, accès limité aux cours
   qui lui sont attribués, encodage et remise depuis le téléphone, tableau de bord de ses cours.
2. Reprise des cotes d'ITMCD (export) rattachées aux codes officiels déjà repris.
3. Comparaison avec le dépôt `Deliberation` dès qu'il contient le code.
4. Code QR de vérification sur le bulletin de délibération.
5. Seconde session (repêchage), si l'établissement l'organise.
