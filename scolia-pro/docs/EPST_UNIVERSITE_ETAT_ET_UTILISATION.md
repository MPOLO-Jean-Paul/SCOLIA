# SCOLIA Pro — EPST et université : état livré et mode d'emploi

Mis à jour le 14 novembre 2026. Remplace la proposition du même jour, validée par le propriétaire
avec ces décisions : EPST d'abord, maternelle conservée, règles par défaut confirmées, résultats du S1
visibles par l'étudiant, frais universitaires en dollars américains.

Les ITM et IM ne changent pas : leurs cours, cotes, délibérations et menus restent ceux d'avant.

## 1. Ce qui est livré

| Domaine | Fonctionnalité | Serveur | Écran Windows et Android |
| --- | --- | --- | --- |
| Commun | Type d'établissement verrouillé à l'installation | Déjà en place | Déjà en place |
| Commun | Règlement pédagogique par année, proposé, modifiable, validé, rouvrable avec motif | Oui | Organisation pédagogique › Règlement pédagogique |
| Commun | Menus et ruban adaptés au type (les écrans ITM disparaissent pour les écoles et universités) | — | Oui |
| EPST | Maximum de chaque branche par période, périodes d'examen | Oui | Cycles scolaires |
| EPST | Points par période, totaux par trimestre ou semestre, total général, pourcentage, place, échecs | Oui | Scolarité › Résultats et bulletins |
| EPST | Application et conduite par période (E, TB, B, AB, M, MA) | Oui | Résultats et bulletins › Application et conduite |
| EPST | Bulletin officiel PDF de la classe ou d'un élève, sur le modèle national (voir § 2 bis) | Oui | Résultats et bulletins |
| EPST | Province, commune et code de l'école ; N° permanent de l'élève | Oui | Paramètres › Établissement ; fiche de l'élève |
| EPST | Délibération : calcul, jury motivé, première session, repêchage, clôture | Oui | Scolarité › Délibération de fin d'année |
| EPST | Procès-verbal et palmarès PDF | Oui | Délibération de fin d'année |
| EPST | Passage de classe selon la décision définitive | Oui | Secrétariat › Réinscriptions |
| EPST | Candidats ENAFEP, fin du CTEB, Examen d'État ; export ; résultats saisis | Oui | Scolarité › Examens nationaux |
| Université | Promotion rattachée à une filière (L1 à M2) | Oui | Paramètres › promotion |
| Université | Frais académiques en tranches datées, en USD | Oui | Organisation LMD › Frais par tranches |
| Université | Notes des EC par session | Oui | Scolarité › Notes des EC |
| Université | Publication des résultats partiels du S1, première session sur tous les cours, deuxième session | Oui | Scolarité › Délibération LMD |
| Université | Jury motivé, admis, admis avec dettes, ajourné | Oui | Délibération LMD |
| Université | Relevés de notes (S1, première session, définitif) et procès-verbal PDF | Oui | Délibération LMD |
| Université | Dettes : création automatique et apurement | Oui | Scolarité › Dettes académiques |
| Université | Passage selon la décision définitive | Oui | Secrétariat › Réinscriptions |
| Enseignants | Attribution des branches (titulaire de classe au primaire) et des EC | Oui | Enseignants et cours › Attribution des branches ou des EC |
| Enseignants | Saisie par l'enseignant de ses seules branches ou EC, mêmes verrous | Oui | Accueil de l'enseignant : Mes branches et notes |
| Étudiants | Accès créés par le secrétariat (identifiant = matricule, code provisoire) | Oui | Secrétariat › Accès étudiants |
| Étudiants | Consultation des seuls résultats publiés (S1, première session, définitif), dettes, relevé PDF | Oui | Accueil de l'étudiant : Mes résultats |
| Téléphone serveur | Bulletins, procès-verbaux et relevés produits aussi par un téléphone serveur | Oui | — |

Captures réelles dans `docs/captures-epst-lmd-20261114/` (écrans Windows, écran téléphone, bulletins primaire
et secondaire, relevé, procès-verbaux).

## 2. Règles appliquées

**EPST (règlement par défaut, modifiable avant validation).**
- Primaire : 3 trimestres ; secondaire et technique : 2 semestres ; 2 périodes de travail puis un examen.
- Le maximum de l'examen vaut 2 fois celui d'une période. Les branches pèsent par leurs maxima.
- Plusieurs interrogations d'une même période sont ramenées au maximum de la période.
- Une case sans évaluation n'est jamais comptée comme zéro : l'élève est « incomplet », sans place.
- Sous 50 % : double. À 50 % ou plus sans échec : passe. Avec 1 ou 2 branches sous 50 % : repêchage. Au-delà : double.
- Après le repêchage : passe si chaque branche présentée atteint 50 %, sinon double.
- Place « 1, 2, 2, 4 » parmi les élèves complets.

**Université LMD.**
- Moyenne d'UE pondérée par les crédits des EC, à défaut par leurs heures, à défaut à parts égales.
- UE acquise à 10 sur 20, avec compensation entre les EC de l'UE, sans compensation entre UE.
- 30 crédits par semestre, 60 par an. Une UE acquise est capitalisée.
- Deuxième session : seules les UE non acquises ; la meilleure des deux notes de chaque EC est retenue.
- Décision finale : admis avec 60 crédits ; admis avec dettes s'il manque au plus 15 crédits ; ajourné sinon.

**Garde-fous.** Après la clôture d'une première session, les cotes, la conduite, les maxima, le calendrier
du cycle et le règlement de la classe ou de la promotion sont figés. Chaque décision du jury exige un motif
et apparaît au procès-verbal. Toute opération est inscrite au journal d'audit.

## 2 bis. Bulletin officiel EPST

Le bulletin reprend la structure des bulletins nationaux de la RDC, telle qu'on la retrouve sur les modèles
publiés en ligne (secondaire et primaire). L'ordinateur et le téléphone serveur impriment exactement la même
mise en page, sur une page A4 portrait par élève.

- **En-tête :** drapeau, « République démocratique du Congo », ministère (par défaut : Éducation nationale et
  Nouvelle citoyenneté), division provinciale.
- **Identification :** à gauche, province, ville, commune ou territoire, école et code de l'école en cases ;
  à droite, élève, sexe, lieu et date de naissance, classe et N° permanent en cases. Une case vide reste à
  remplir à la main.
- **Tableau :** une colonne par trimestre au primaire et par semestre au secondaire. Chaque partie comprend les
  travaux journaliers (1re P, 2e P…), l'examen et le total ; vient ensuite le total général (T.G.). Au
  secondaire s'ajoutent la colonne noircie et « Examen de repêchage » (% et signature du professeur).
- **Branches regroupées par maxima :** chaque groupe commence par une ligne MAXIMA, et les groupes sont rangés
  du plus petit maximum au plus grand.
- **Synthèse :** maxima généraux, totaux, pourcentage, place / nombre d'élèves, application, conduite et
  signature du responsable. Les cases sans objet sont noircies, comme sur le modèle.
- **Décision :** les mentions « passe », « double », « a échoué et est orienté vers… » et « ne pourra passer…
  s'il n'a subi avec succès un examen de repêchage en … » sont imprimées. Après la délibération, les mentions
  écartées sont biffées et la décision est rappelée. Les branches du repêchage sont nommées.
- **Pied :** fait à… le…, sceau de l'école, le chef d'établissement, signature de l'élève. Le bas de page porte
  « (1) Biffer la mention inutile » et « le bulletin est sans valeur s'il est raturé ou surchargé ».

Le numéro de formulaire imprimé par l'État (code IGE/P.S/…) n'est pas reproduit : il désigne les imprimés
officiels vendus aux écoles, et SCOLIA n'en imprime pas le numéro de série.

## 3. Mise en route

**École primaire ou secondaire.**
1. Organisation pédagogique › Règlement pédagogique : relire, modifier si besoin, valider.
2. Cycles scolaires : créer les périodes (cocher « examen » pour la dernière de chaque trimestre ou semestre),
   puis les branches de chaque classe avec leur maximum par période.
3. Saisir les évaluations, puis l'application et la conduite.
4. Paramètres › Établissement › Bulletins officiels : province, commune ou territoire, code de l'école ;
   sur la fiche de chaque élève, le N° permanent s'il est connu.
5. Résultats et bulletins : imprimer les bulletins.
6. Délibération de fin d'année : décisions du jury, clôture, repêchage, clôture définitive, procès-verbal.
7. Réinscriptions : les élèves admis montent, ceux qui doublent restent.

**Université.**
1. À l'installation, choisir « Université / institut supérieur LMD » : la monnaie est le dollar.
2. Règlement pédagogique : valider la proposition LMD.
3. Organisation LMD : facultés, départements, filières ; maquette UE et EC ; frais par tranches.
4. Paramètres › promotion : rattacher chaque promotion à sa filière.
5. Notes des EC : saisir les notes du S1 ; Délibération LMD › Publier les résultats du S1.
6. Notes du S2, puis Clôturer la première session ; notes de deuxième session ; Clôturer la deuxième session.
7. Dettes académiques : apurer les dettes l'année suivante.

## 4. Sécurité des accès étudiants

- Un compte étudiant n'a qu'un droit : lire ses propres résultats publiés. Le secrétariat ne le reçoit pas.
- Le serveur refuse à un compte étudiant toute route autre que ses résultats, sa session, la santé du serveur et
  la vérification publique d'un document. Un test appelle toutes les routes de lecture de l'API avec un compte étudiant.
- Le code provisoire doit être remplacé avant la première consultation. Le motif du jury et les autres étudiants
  ne sont jamais transmis. Un accès désactivé ferme la session ouverte.
- Les accès étudiants ne figurent pas parmi les utilisateurs du personnel et ne peuvent pas en devenir un.

## 5. Vérifications effectuées

| Suite | Résultat |
| --- | --- |
| Serveur (Scolia.Tests) | 536 tests réussis, dont 39 nouveaux (bulletin officiel compris) |
| Poste Windows (Scolia.Bureau.Tests) | 553 tests réussis |
| Mobile (Scolia.Mobile.Tests) | 48 tests réussis, dont 6 nouveaux pour les documents du téléphone et la mise en page du bulletin |
| Captures sans écran | École primaire, université, enseignant titulaire et étudiant créés par l'API : écrans ouverts sans erreur ; format téléphone sans débordement |

Le moteur ITM n'a pas été modifié : ses tests passent sans changement.

## 6. Ce qui reste à faire

1. **Modèles officiels de bulletins.** Le bulletin EPST suit la structure des modèles nationaux publiés en ligne.
   Les pages d'origine n'ont pas pu être ouvertes depuis l'environnement de travail : seuls des extraits ont été
   lus. Une photo ou un scan d'un bulletin vierge de primaire, de secondaire et de technique de l'année en cours
   permettra de caler l'ordre exact des branches et les intitulés de chaque section. Le relevé universitaire
   suit les normes LMD courantes, faute de modèle national publié.
2. **Consultation par les parents d'élèves (EPST).** Les accès en ligne concernent les étudiants d'université ;
   un accès « parent » pour le primaire et le secondaire suivrait le même modèle si vous le souhaitez.
3. **APK Android.** Les écrans sont partagés avec Android et testés en format téléphone, mais l'APK n'a pas été
   compilé ici : la compilation de la branche par GitHub Actions le confirmera.
4. **Fusion.** Ce travail est sur la branche `claude/escolia-admin-console-config-h9ej62`, qui contient aussi la
   console administrateur et la synchronisation D1 non encore fusionnées. Pour la branche de publication,
   les commits EPST, LMD, enseignants, étudiants et téléphone peuvent être repris seuls.
