# SCOLIA Pro — étude des bulletins maternelle, primaire et secondaire (RDC)

État : **recherche et préparation pédagogique, NON OFFICIEL**, 11 novembre 2026. PR de développement Windows / Android, non fusionnée.

## Sources et degré de vérification

- Ministère de l'Éducation nationale, calendrier 2026-2027 : https://edu-nc.gouv.cd/calendrier-scolaire
- Déclaration sur les imprimés **2025-2026**, confirmée par l'ACP (26 février 2026) : rose au préscolaire, vert au primaire, bleu au secondaire, blanc au terminal. https://acp.cd/nation/rdc-les-bulletins-scolaires-delivres-a-la-cloture-de-lannee-scolaire-2025-2026-ministere-de-leducation-nationale/
- Le ministère rappelle que **la certification des bulletins officiels lui appartient** : https://edu-nc.gouv.cd/communiques/clarifications-et-assurances-du-ministere-de-leducation-nationale-et-nouvelle-citoyennete-concernant-la-production-des-bulletins-scolaires
- Modèle ancien de maternelle (illustratif, source tierce non certifiée pour 2026-2027) : https://fr.scribd.com/document/880982744/BULLETIN-soft
- Modèle ancien de primaire 1re/2e (illustratif) : https://fr.scribd.com/document/851612175/Bulletin-1-2-Primaire
- Modèle ancien de 6e primaire (illustratif) : https://fr.scribd.com/document/853391258/BULLETIN-DE-LA-6e-ANNEE-DEGRE-TERMINAL
- Modèle ancien de 7e/8e CTEB (illustratif) : https://fr.scribd.com/document/836269173/BULLETIN-DE-LA-7e-et-8e-CTEB
- Modèle ancien des humanités scientifiques (illustratif) : https://fr.scribd.com/document/879188283/Bulletin-4e-Scient-Mathematiques-Physique

**Inconnu à ce stade** : fichiers sources ministériels détaillés des bulletins 2026-2027, leurs codes de maquette, règles de maxima, modalités de validation et variantes optionnelles. Ne pas déclarer leur conformité à l'avance.

## Modèles visuels et rubriques repérés

| Cycle | Présentation observée | Représentation actuellement préparée |
| --- | --- | --- |
| Maternelle | Identité de l'enfant, groupes d'activités, observations/appréciations par trimestre, signatures. **Certains modèles anciens ont des maxima numériques.** | Carnet descriptif par période, sans score forcé. Une variante chiffrée exige d'abord validation pédagogique et réglementaire. |
| Primaire | Grille de branches et sous-branches, domaines, maxima, périodes, trimestres, totaux, appréciations et décisions. Le regroupement des domaines varie selon le degré. | Relevé de cotes par branche et période, regroupées selon le calendrier annuel ; pas de total officiel calculé automatiquement. |
| Secondaire | Grilles de branches, maxima, travaux journaliers, examens, semestres, sous-totaux, résultat annuel et éventuellement repêchage. Les humanités ont des options spécialisées. | Relevé provisoire semestriel avec matières configurées par promotion et option ; aucune délibération automatique. |
| Terminal / technique | Maquettes potentiellement distinctes, examen d'État et épreuves propres à l'option. | Exiger un modèle spécifique validé avant tout document définitif. |

## Règles produit pour le prochain lot

1. **Une école/complexe, plusieurs cycles** : les modèles dépendent de la classe, de son cycle et de l'année ; jamais du seul nom de l'établissement.
2. **Paramétrage par année** : codes de branches, domaines, maxima, sessions/périodes et option ; pas de liste réglementaire figée ni de seuil de réussite codé sans texte de référence.
3. **Maternelle** : garder observations narratives et éventuelle cote officielle comme **deux modes distincts**, jamais convertir rétroactivement un texte en score.
4. **Primaire** : référentiels par degré/classes (1–2, 3–4, 5–6) à valider sur spécimens authentiques. Ne pas supposer la même grille pour tous.
5. **Secondaire** : parcours général/CTEB/humanités et option avant impression ; ne pas réutiliser automatiquement les règles ITM/IM.
6. **Authenticité** : les aperçus portent une mention non officielle. Aucun logo, sceau ou QR code présenté comme une certification de l'État.
7. **Conserver l'existant** : anciennes migrations ITM/IM, paiements, délibérations et données ne changent pas. Interface Avalonia conserve les composants administrateur/secrétariat.
8. **Contrôles** : tests par cycle, classe, période, année et droit d'accès ; validation du PDF sur exemplaire approuvé avant activation ou distribution.

## Avancement technique — deuxième lot

- **Variantes par degré et niveau** : maternelle (1re–3e), primaire (élémentaire 1re–2e, moyen 3e–4e, terminal 5e–6e), secondaire (CTEB 7e–8e et humanités). Ces appellations sont des **repères de présentation**, non des tableaux officiels reproduits.
- **Matières sans cote** : la grille primaire/secondaire inclut toutes les branches actives du programme annuel même lorsque personne n'a encore encodé de note. Une cellule non évaluée reste vide / « Aucune évaluation saisie », jamais zéro.
- **Archives** : une matière devenue inactive mais comportant d'anciennes évaluations reste consultable, sans réécriture des notes.
- **Évaluations multiples** : affichées individuellement dans une cellule matière × période ; pas de total, de moyenne ou de décision calculé tant que le règlement exact n'est pas vérifié.
- **Options** : le libellé de section/option existante de la classe est conservé dans l'aperçu ; aucune option technique ne reçoit un règlement inventé.
- **Avalonia** : prévisualisation responsive réutilisant les styles administrateur/secrétariat (`phonegroup`, `groupcaps`, `muted`).
- **Compatibilité** : les contrats API sont enrichis de champs facultatifs ; aucune migration, aucun changement des bulletins ITM/IM et aucune activation automatique des nouveaux profils.
- **Publication** : PR de développement seulement. Les validations CI sur chaque nouveau commit ne prouvent ni homologation du bulletin ni autorisation d'imprimer un document officiel.

Rappel : les imprimés officiels 2025-2026 ont été annoncés rose (préscolaire), vert (primaire), bleu (secondaire) et blanc (terminal). Cette information de **2025-2026** ne préjuge pas des fichiers modèles ou des couleurs de l'année **2026-2027**. Source ACP : https://acp.cd/nation/rdc-les-bulletins-scolaires-delivres-a-la-cloture-de-lannee-scolaire-2025-2026-ministere-de-leducation-nationale/

## Aperçu PDF A4 — troisième lot de développement

- Endpoint protégé : `GET /api/etablissement/cycles/evaluations/{inscriptionId}/apercu.pdf` avec `AcademicsRead`. Le même contrôle de cycle et d'inscription que l'aperçu JSON est appliqué.
- Export depuis la page Avalonia existante via l'action **Exporter l'aperçu PDF** ; le client emploie le téléchargement de fichiers SCOLIA.
- Génération avec **QuestPDF et Roboto intégrée**, sans nouveau paquet ni thème d'interface.
- En-tête dynamique : nom/adresse/ville de l'établissement, nom et matricule de l'élève, année scolaire et classe/option.
- **Maternelle** : périodes et observations rédigées. **Primaire/secondaire** : tableaux de branches, quatre périodes maximum par bloc A4 pour la lisibilité.
- `—` dans les cellules signifie **aucune évaluation saisie**, jamais zéro. Plusieurs évaluations d'une même branche restent distinctes.
- Mention **DOCUMENT PÉDAGOGIQUE NON OFFICIEL** en en-tête et rappel dans le pied de chaque page. Pas de sceau, de QR gouvernemental, de verdict, de classement ni de pourcentage inventé.
- Le PDF est généré **par le serveur scolaire** : l'aperçu JSON déjà mis en cache ne garantit pas, à ce stade, la production du fichier en l'absence d'accès au serveur. Aucune promesse d'export hors connexion.
- Tests : PDF maternelle et primaire (cellules sans cotation), rejet explicite d'un document marqué officiel, refus du PDF pour un ITM historique.

**Attention :** cette sortie sert à vérifier la mise en page et les données pédagogiques. Les imprimés, grilles, maxima et circuits de certification ministériels de l'année applicable restent à authentifier avant une version de bulletin officielle.

## Conformité, confidentialité et conservation des archives

- **Certification :** dans son communiqué du **10 juillet 2025**, le ministère rappelle être la seule autorité habilitée à certifier et valider les bulletins scolaires et autres titres scolaires. Un export scolaire SCOLIA ne doit donc jamais être annoncé comme bulletin ministériel authentifié. Source primaire : https://edu-nc.gouv.cd/communiques/clarifications-et-assurances-du-ministere-de-leducation-nationale-et-nouvelle-citoyennete-concernant-la-production-des-bulletins-scolaires
- **Aperçus de l'année applicable :** les couleurs diffusées pour **2025-2026** ne fournissent ni le barème ni le spécimen ministériel de **2026-2027** ; ne pas utiliser une couleur pour suggérer une homologation.
- **Cotes historiques hors maquette :** une évaluation liée à une matière absente du programme annuel courant est conservée séparément dans l'API, dans Avalonia et dans le PDF, sans l'attribuer à une autre branche ni la sommer. Les colonnes annuelles ne changent pas.
- **Données sensibles :** la réponse PDF porte `Cache-Control: private, no-store, max-age=0` et `Pragma: no-cache`; accès `AcademicsRead` inchangé.
- **Lecture SQL :** le service d'export partage le contexte scolaire déjà résolu, sans recharger la même inscription deux fois.
- **Contrôles :** tests d'API pour les évaluations historiques non classées, le PDF et son en-tête de cache ; tests de format pour la maternelle et le primaire.
- **Limites :** l'API et le client utilisent toujours une connexion au serveur pour l'impression. Aucun export hors connexion ni certification ministérielle n'est annoncé.

## Code préparatoire

`SchoolBulletinLayouts.For(SchoolCycleKind)` permet aux API et interfaces de distinguer les présentations provisoires par cycle, degré et niveau sans saisir de faux maxima ni de barèmes. Les profils restent désactivés à l'installation. **Aucun bulletin définitif n'est généré ou publié par ce lot.**


## Correction métier — codes d'évaluation par matière (12 novembre 2026)

Une école peut saisir « INTERRO1 » en français et « INTERRO1 » en mathématiques
pour un même élève durant la même période. L'identité technique devient
**élève inscrit + période du cycle + matière/activité + code**.
Deux enregistrements avec ce même quadruplet restent interdits.
Les activités maternelles sans matière conservent une unicité spécifique
(élève + période + code) par index filtré.

La migration `20261112190000_AssessmentCodesPerSubject` modifie uniquement
les index des nouvelles évaluations scolaires : ni notes historiques ni
mensualités ITM ne sont converties ou supprimées. Le retour à l'ancien index
peut être bloqué si des codes sont réutilisés sur plusieurs matières ;
aucune donnée n'est effacée pour forcer le retour arrière.

Tests de régression : aperçus primaire/secondaire et persistance SQLite
avec deux matières partageant le code « INTERRO1 ». Ce correctif **n'active
pas** les profils scolaires et ne rend pas un bulletin officiel.
