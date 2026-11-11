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

## Code préparatoire

`SchoolBulletinLayouts.For(SchoolCycleKind)` permet aux API et interfaces de distinguer les trois présentations provisoires sans saisir de faux maxima ni de barèmes. Les profils restent désactivés à l'installation. **Aucun bulletin définitif n'est généré ou publié par ce lot.**
