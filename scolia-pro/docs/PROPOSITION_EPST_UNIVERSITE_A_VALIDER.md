# SCOLIA Pro — proposition : adapter l'application aux EPST et aux universités

**Statut : proposition à valider. Rien n'est implémenté tant que les décisions de la section 6 ne sont pas prises.**
Étude réalisée le 14 novembre 2026 sur la branche de publication `release/direct-apk-scolia-pro-20261103` (version 1.2.71).

## 1. Ce qui existe déjà (à ne pas refaire)

Le travail multi-établissements a commencé le 11 novembre. Il est solide côté modèle mais incomplet côté usage.

| Brique | État réel dans le code |
| --- | --- |
| Choix du type à l'installation | **Fait** sur Windows : ITM/IM, maternelle, primaire, secondaire/technique, complexe scolaire, université LMD. |
| Vocabulaire (apprenant / élève / étudiant) | **Fait** sur Windows. **Absent** sur Android. |
| Cycles scolaires, périodes par cycle, matières annuelles | **Fait** : écran « Cycles scolaires », API `/cycles`. |
| Saisie des évaluations primaire/secondaire | **Partiel** : enregistrement par élève et aperçu PDF **non officiel**, sans total, sans pourcentage, sans place, sans décision. |
| Structure universitaire (faculté, département, filière ; domaine, mention, parcours, spécialité) | **Fait** en base et dans l'écran « Organisation LMD ». |
| Maquette LMD (UE, EC, S1/S2, crédits) | **Fait** : règle des 30 crédits par semestre et 25 h par crédit (décret 22/39). |
| Résultats S1, première session, crédits | **Calcul écrit mais jamais appelé** : aucun écran, aucune API, aucun relevé. |
| Délibération, jury, palmarès, PV | **ITM seulement**. Le moteur ITM (60 %, mentions A à E, épreuves de jury) ne s'applique pas aux autres profils. |
| Application Android | **Aucune adaptation** : menus et vocabulaire ITM pour tous les profils. |

Les ITM/IM gardent l'architecture actuelle sans aucune modification : c'est déjà une règle du code (`InstitutionProfiles.ItmIm`).

## 2. Principe général proposé

1. **Un type par établissement, choisi à la première configuration**, et **verrouillé** dès la première inscription ou la première note. Un changement de type après coup exigera un assistant de migration (hors de ce lot).
2. **Un règlement pédagogique par année scolaire**, propre au type : périodes, maxima, seuils de réussite, repêchage, mentions. SCOLIA propose un **modèle RDC pré-rempli** que l'établissement relit, ajuste et valide. Le nom du validateur et la date sont gardés. Aucune décision officielle n'est produite avant cette validation.
3. **Un moteur de résultats par type**, à côté du moteur ITM, jamais à sa place. Les briques communes sont réutilisées : enseignants, codes anonymes, verrouillage du dépôt des cotes, journal d'audit, PDF, paiements.
4. **Windows et Android ensemble** : chaque écran livré sur Windows l'est aussi sur Android, au minimum en consultation.

## 3. EPST — primaire, secondaire et technique

Tous les cours sont **annuels**. Seul le découpage des évaluations change.

### 3.1 Organisation
- **Primaire** : 1re à 6e année, regroupées en degrés élémentaire, moyen et terminal. Année en **3 trimestres** de 2 périodes chacun, plus un examen par trimestre.
- **Secondaire** : 7e et 8e de l'éducation de base (CTEB), puis 1re à 4e des humanités. Année en **2 semestres** de 2 périodes chacun, plus un examen par semestre.
- **Technique et professionnel** : mêmes classes que le secondaire, avec une **option** par classe (par exemple commerciale, pédagogique, mécanique, électricité, nutrition). L'option porte ses propres cours, ses travaux pratiques et ses stages.
- **Complexe scolaire** : maternelle, primaire et secondaire dans une seule base, chacun avec son calendrier.

### 3.2 Fonctionnalités proposées
1. **Programme de la classe** : liste des branches avec maxima par période et par examen. Un catalogue modèle (français, langues congolaises, mathématiques, éveil scientifique, éducation civique et morale, éducation physique, etc.) est proposé et reste entièrement modifiable.
2. **Titulaire de classe au primaire** : un enseignant est titulaire de toutes les branches de sa classe. Au secondaire et au technique, un **professeur par cours**.
3. **Encodage des points** par période et par examen, par classe ou par élève, avec dépôt et verrouillage par période comme pour les ITM.
4. **Bulletin officiel** : points par branche et par période, total du trimestre ou du semestre, total général, pourcentage, **place sur l'effectif**, application et conduite, signature du titulaire et du chef d'établissement. Un modèle pour le primaire, un pour le secondaire, un pour le technique.
5. **Délibération de fin d'année** : décision par élève (passe, double, réorienté), **repêchage** (seconde session) limité à un nombre de branches défini dans le règlement, palmarès de classe et procès-verbal.
6. **Conduite et assiduité** : reprise des absences et de la discipline existantes dans le bulletin.
7. **Examens nationaux** : liste des finalistes pour l'ENAFEP (6e primaire), l'examen de fin de CTEB (8e) et l'Examen d'État (4e humanités), avec export. Les résultats nationaux sont saisis, jamais calculés.
8. **Passage de classe** : promotion automatique des élèves admis vers la classe suivante, au début de l'année suivante.
9. **Frais** : minerval mensuel par cycle, frais connexes, et prise en charge déjà présente. Pour un établissement public, aucune facturation n'est activée sans validation réglementaire (règle déjà dans le code).

## 4. Université — système LMD

Aucun cours annuel : chaque UE appartient au **semestre 1 ou au semestre 2**.

### 4.1 Gestion académique complète
1. **Structures** : faculté, département, filière ; et en parallèle domaine, mention, parcours, spécialité (déjà en base, à compléter par la modification et la désactivation).
2. **Promotions par filière** : L1, L2, L3, M1, M2, avec capacité et filière obligatoire.
3. **Maquette** : UE et EC par semestre, crédits, volume horaire, coefficient, enseignant titulaire et assistants par EC.
4. **Inscription administrative** : étudiant, matricule, filière, promotion, frais.
5. **Inscription pédagogique** : liste des UE suivies par l'étudiant pendant l'année, y compris les **UE en dette** d'une année antérieure.
6. **Frais académiques par filière et par promotion**, payables par **tranches** à des dates fixées, en plus du mode mensuel actuel.

### 4.2 Cycle des résultats (votre description)
1. **Session du semestre 1** : encodage des cotes des EC du S1, dépôt par l'enseignant, jury restreint, puis **publication des résultats partiels du S1**. Ces résultats sont provisoires et conservés.
2. **Session du semestre 2** : même circuit pour les EC du S2.
3. **Résultats de la première session** : consolidation automatique de **tous les cours programmés pour la promotion** (S1 et S2), moyennes par UE, crédits capitalisés sur 60, décision du jury, procès-verbal et publication.
4. **Deuxième session** : rattrapage des seules UE non validées, puis délibération finale de l'année.
5. **Documents** : relevé de notes semestriel, relevé annuel, PV de délibération, attestation de réussite, palmarès de promotion.

### 4.3 Règles à paramétrer dans le règlement de l'année
- Note de validation d'une UE (habituellement 10 sur 20).
- Compensation entre les EC d'une même UE (oui ou non), et entre les UE d'un même semestre (oui ou non).
- Capitalisation : une UE validée reste acquise.
- Passage avec dettes : nombre maximal de crédits non acquis autorisé pour passer à l'année supérieure.
- Appréciations selon le barème de l'instruction académique n° 027 du 22 octobre 2025 (déjà dans le code).

## 5. Difficultés relevées et solutions proposées

1. **Deux systèmes de cours en parallèle.** Les ITM utilisent `Course` et `CourseMark` ; les nouveaux profils utilisent `CurriculumItem`. L'encodage, les enseignants, les cartes et les présences sont branchés sur `Course`. **Solution** : garder `Course` intact pour les ITM et brancher les services communs sur une interface commune « matière notée », implémentée par les deux.
2. **Règles officielles changeantes.** Maxima, seuils et repêchage évoluent avec les instructions de chaque année, et le code refuse aujourd'hui d'inventer ces valeurs. **Solution** : règlement versionné par année, modèle pré-rempli, validation explicite par l'établissement avant toute décision.
3. **Dettes LMD.** Le modèle actuel inscrit un étudiant dans une seule promotion par année. **Solution** : l'inscription pédagogique par UE (4.1, point 5).
4. **Frais universitaires.** La facturation actuelle est mensuelle et obligatoire. **Solution** : mode « tranches » par filière et promotion, sans modifier le mode mensuel ITM.
5. **Android en retard.** Aucun écran ne s'adapte au profil. **Solution** : chaque lot inclut sa partie Android.
6. **Effectifs** : classes de 60 à 80 élèves au primaire et au secondaire. Les bulletins et palmarès doivent s'imprimer par lot, hors ligne sur le PC serveur.
7. **Changement de type** sur une base remplie : interdit dans ce lot.

## 6. Décisions à prendre avant de commencer

1. **Ordre des lots** : EPST d'abord (le socle des cycles est plus avancé), ou université d'abord ?
2. **Maternelle** : la garder dans ce chantier (évaluation par observation, déjà prévue) ou la reporter ?
3. **Valeurs par défaut du règlement EPST** : seuil de réussite de 50 %, repêchage limité à 2 branches, classement par place affiché. À confirmer ou corriger.
4. **Valeurs par défaut du règlement LMD** : UE validée à 10 sur 20, compensation entre EC de la même UE, pas de compensation entre UE, passage avec au plus 15 crédits en dette. À confirmer ou corriger.
5. **Publication des résultats S1** : seulement imprimés, ou aussi visibles par l'étudiant sur l'application Android ?
6. **Modèles de bulletins** : pouvez-vous fournir un bulletin réel de primaire, de secondaire et de technique, ainsi qu'un relevé universitaire, pour reproduire leur mise en page ?
7. **Frais universitaires** : nombre de tranches et devise (USD, CDF ou les deux).

## 7. Découpage proposé une fois validé

| Lot | Contenu | Plateformes |
| --- | --- | --- |
| 1 | Verrou du type, règlement par année, menus et vocabulaire Android | Windows + Android |
| 2 | EPST : programme, titulaires, encodage par période, bulletin officiel | Windows + Android (consultation) |
| 3 | EPST : délibération, repêchage, palmarès, passage de classe, examens nationaux | Windows + Android (consultation) |
| 4 | LMD : promotions par filière, inscription pédagogique, frais par tranches | Windows + Android |
| 5 | LMD : sessions S1 et S2, résultats partiels, première session, PV, relevés | Windows + Android (consultation) |
| 6 | LMD : deuxième session, dettes, passage avec dettes, attestations | Windows + Android (consultation) |

Chaque lot passe les tests existants ITM sans modification, pour garantir que les instituts techniques médicaux ne sont pas touchés.
