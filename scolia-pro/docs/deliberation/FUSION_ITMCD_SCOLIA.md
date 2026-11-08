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
- **Projet Délibération** (`ITM_Deliberation.zip`, le dépôt `MPOLO-Jean-Paul/Deliberation` étant vide) : variante
  d'ITMCD (même base PHP/MySQL) qui ajoute les autorités, les épreuves du jury, les totaux officiels, les codes P/A/E,
  la grille de délibération officielle et son PDF. Les sauvegardes SQL du zip contiennent des données réelles :
  elles n'ont servi qu'à lire le schéma et n'ont été copiées nulle part.

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

### Apports du projet Délibération (lot 4)

| Projet Délibération | Dans SCOLIA |
|---|---|
| Cours « jury » STAGE, SITUATION D'INTÉGRATION, DOSSIER D'ÉVALUATION, ÉPREUVE D'INTÉGRATION (TOTAL 1) et BLOCS PRÉVENTION, DIAGNOSTIC, GESTION (SOUS-TOTAL 2), créés d'office par promotion avec un faux compte enseignant « JURY » | Organisations « Épreuve du jury » et « Bloc du jury » ; « Créer les épreuves du jury » ajoute les sept épreuves standard (maxima 150, 50, 90, 225, 60, 50, 50), modifiables ; cote « Jury » réservée au droit `deliberation.gerer`, sans compte fictif |
| Points officiels = coefficient × 10 | Le maxima est saisi directement (même résultat, sans conversion) |
| TOTAL 1, SUBTOTAL 1, SUBTOTAL 2, TOTAL 2, TOTAL GÉNÉRAL | Mêmes totaux au tableau du jury, sur la fiche, le bulletin et la grille |
| Décision finale P / A / E | Code du jury annuel : P (réussite partout), A (moyenne atteinte, cours en échec), E (sous le seuil) ; la décision Admis/Ajourné et la décision motivée du jury restent la référence |
| Cote manquante comptée comme échec (code A possible) | Gardé plus strict : dossier « Incomplet », ajourné à la clôture sauf décision motivée du jury |
| Table des autorités (préfet, proviseur, président, secrétaire, 4 membres) | « Autorités et jury » par année ; signatures du bulletin (préfet, proviseur), de la grille et du procès-verbal ; la clôture reprend le jury enregistré |
| Grille de délibération (en-têtes verticaux, signatures) | Grille officielle PDF/Excel dans le même ordre, en-têtes courts et légende |
| Grille de référence INF1 (pondérations codées en dur pour une promotion) | Non reprise : propre à un établissement ; les maxima se règlent dans « Cours et notes » |

### Comptes enseignants (lot 3, portail enseignant d'ITMCD)

- Rôle **Enseignant** dans « Utilisateurs » : le compte est relié à une fiche du personnel en fonction
  (une fiche = un compte actif). Droit unique `cotes.encoder` : ni dossiers, ni finances, ni résultats du jury.
- « Mes cours » s'ouvre à la connexion : les promotions et les cours attribués aujourd'hui à sa fiche,
  avec l'avancement et la remise de chaque feuille. Le retrait de l'attribution coupe l'accès.
- Feuille **toujours anonyme** pour l'enseignant (codes, jamais les noms), saisie par code, remise,
  relevé et liste des échecs de ses cours. Après la remise, seul le jury corrige ou renvoie la feuille.
- Le serveur vérifie l'attribution à chaque appel (refus 403) ; les anciens comptes enseignants sans fiche
  restent désactivés.

### Privilège jury, notifications et documents (lot 5)

- **Le jury est un comité, pas un compte** : le secrétariat coche « Membre du jury » sur le compte d'un enseignant
  (droit `jury.encoder`). Il encode alors les épreuves du jury de toutes les promotions, en feuille anonyme ou par code,
  sans en devenir titulaire et sans voir les résultats. Le privilège se retire à tout moment ; les cotes déjà encodées restent.
- **Notifications (ITMCD)** : bannière (et notification du téléphone) aux enseignants à l'ouverture ou la fermeture de
  l'encodage et au renvoi d'une feuille ; au jury à chaque remise. Les trois dernières annonces s'affichent dans « Mes cours ».
- **Confidentialité du direct** : un compte enseignant ne reçoit ni les encaissements, ni les codes rattachés aux
  matricules, ni la présence des autres postes ; seulement les annonces de la délibération.
- **Bulletin** : toujours sur une seule page (contenu réduit, jamais coupé) ; « Tous les bulletins » produit la promotion
  entière, un apprenant par page, dans l'ordre du rang.
- **Grille** : police et marges resserrées selon le nombre de colonnes, en-tête répété sur chaque page.
- **Cours** : pondération (coefficient, moyenne sur 20 de « Cours et notes ») et maxima (points de la délibération,
  1 à 2000, vide = pondération × 10) sont distincts, bornés et expliqués dans tous les formulaires.

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
réouverture et réglages avec le nouveau droit `deliberation.gerer` (accordé au secrétariat) ; l'enseignant
encode ses seuls cours avec `cotes.encoder` ; un membre du jury encode les épreuves du jury avec `jury.encoder`.

## 5. Décisions prises (modifiables)

1. Cours annuel encodé au seul S1 : **cote manquante** au jury annuel (ITMCD comptait S1/2).
2. Le jury peut admettre sous le seuil, avec un motif obligatoire (« Admis par délibération »).
3. Le rang n'est donné qu'aux dossiers complets ; les ex æquo partagent le rang.
4. Les codes anonymes sont annuels, comme dans ITMCD ; le matricule SCOLIA reste l'identifiant permanent.

## 6. Prochains lots proposés

1. Reprise des cotes d'ITMCD (export) rattachées aux codes officiels déjà repris.
2. Code QR de vérification sur le bulletin de délibération.
3. Seconde session (repêchage), si l'établissement l'organise.
