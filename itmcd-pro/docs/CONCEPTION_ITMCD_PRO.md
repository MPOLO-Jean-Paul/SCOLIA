# ITMCD Pro — dossier de conception

Ce document part de l'étude complète d'ITMCD (voir `ANALYSE_FONCTIONNEMENT_ITMCD.md`). Il reformule
l'idée de l'application, puis la perfectionne : modèle métier, cycle de l'année, rôles, parcours,
documents et architecture. Les règles de calcul actuelles sont conservées ; chaque changement est
justifié et les points qui demandent une décision sont regroupés à la fin.

---

## 1. L'idée principale, reformulée

### Ce que fait ITMCD aujourd'hui

ITMCD n'est pas une application de gestion scolaire générale. C'est un **registre officiel des
cotes et un moteur de délibération** pour l'Institut Technique Médical Cœur de Dieu.

Sa raison d'être tient en une chaîne :

```
 Enseignants              Secrétariat académique         Jury                 Direction
 encodent leurs cotes ──▶ consolide par apprenant ──▶ délibère et décide ──▶ signe et publie
 (chacun son cours)       (bulletin, grille)          (ADMIS / ECHEC)        (passage de classe)
```

1. **Décentraliser la saisie** : chaque enseignant encode lui-même les cotes de ses cours, depuis son
   téléphone, au lieu de remettre des feuilles papier au secrétariat.
2. **Centraliser le calcul** : une seule règle transforme ces cotes en points, en pourcentage et en
   décision, la même pour tous les apprenants.
3. **Rendre la délibération rapide et juste** : le jury voit immédiatement qui réussit, qui échoue,
   quelles cotes manquent et quels cours posent problème.
4. **Produire les documents officiels** : bulletins, grille des cotes, relevés par cours.
5. **Enchaîner les années** : les admis passent dans la promotion suivante, les autres redoublent.

### La promesse, en une phrase

> **De la cote de l'enseignant au bulletin signé, sans recopie, sans erreur de calcul et sans
> contestation possible.**

C'est cette promesse qu'ITMCD Pro doit tenir. L'application actuelle tient bien les points 1, 2 et 4.
Elle tient mal le point 3 et la notion de « sans contestation », car rien n'est tracé ni figé.

### Le pari technique

Aujourd'hui, toute cette chaîne dépend d'un hébergeur gratuit et d'Internet. ITMCD Pro fait l'inverse :
**les données vivent dans l'établissement**, sur un appareil serveur, et les enseignants s'y relient
de n'importe où par un réseau privé Tailscale, comme SCOLIA Pro.

---

## 2. Les idées fortes à conserver

Ces choix sont bons et définissent l'identité d'ITMCD. Ils sont repris tels quels.

| Idée | Pourquoi elle est bonne |
|---|---|
| L'enseignant n'encode que **ses** cours | Responsabilité claire ; personne ne saisit à la place d'un autre |
| Saisie rapide **par code apprenant** | Rapide au téléphone, évite les confusions d'homonymes |
| Chaque cours a son **maxima** | Respecte la pondération réelle des matières, sans coefficient abstrait |
| Cours **S1, S2 ou annuel** | Correspond à l'organisation réelle des enseignements |
| **Moyenne d'appui** sur 10 qui soutient un examen faible | Valorise le travail de l'année sans effacer l'examen |
| **Recalcul immédiat** à chaque cote | Le jury voit toujours un état à jour |
| **Interrupteurs d'encodage** par l'administration | Le secrétariat contrôle les périodes de saisie |
| Délibération **confirmée par mot de passe** | Acte solennel, pas un clic accidentel |
| Passage de classe par **promotion suivante** | Simple et adapté à un cursus linéaire |

---

## 3. Ce qui manque à l'idée actuelle

L'étude fait apparaître cinq faiblesses de **conception**, plus profondes que de simples bogues.

1. **L'année n'a pas de déroulement.** Elle se résume à « active » ou non. Rien ne dit si l'on est en
   encodage du S1, en délibération ou en publication. Les interrupteurs remplacent ce déroulement à la main.
2. **L'apprenant et son inscription sont confondus.** Une nouvelle fiche est recréée chaque année,
   avec un nouveau code. On perd le fil d'un apprenant sur son cursus, et les écrans mélangent les années.
3. **La délibération ne laisse aucune trace.** Elle recalcule des colonnes, mais ne produit ni
   procès-verbal, ni date, ni liste des membres. Une cote modifiée après le jury change la décision
   en silence.
4. **Le jury corrige des cotes au lieu de décider.** Pour repêcher un apprenant, il faut modifier ses
   cotes. La cote de l'enseignant est alors perdue, sans motif ni auteur.
5. **La moyenne d'appui est invisible.** Elle change la cote finale mais n'apparaît nulle part.
   Le jury et l'apprenant ne peuvent pas comprendre un résultat.

ITMCD Pro corrige ces cinq points en premier.

---

## 4. Le modèle métier perfectionné

### 4.1 Vocabulaire

| Terme actuel | Terme ITMCD Pro | Définition |
|---|---|---|
| `coefficient` | **Maxima** | Points maximum d'un cours pour une période |
| Cours / élément | **Cours** | Matière d'une promotion, semestrielle (S1 ou S2) ou annuelle |
| Apprenant (une ligne par an) | **Apprenant** + **Inscription** | La personne, puis son inscription pour une année dans une promotion |
| `code_unique` | **Matricule** + **code d'encodage** | Matricule permanent ; code court annuel pour la saisie |
| `cote_s1`, `cote_s2` | **Cote de période** | Points obtenus sur le maxima pour le S1 ou le S2 |
| `moyenne` | **Moyenne d'appui** | Moyenne des travaux sur 10, qui soutient une cote faible |
| `cote`, `examen` | **Cotes héritées** | Anciennes données, lues et converties, jamais saisies |
| Délibération (bouton) | **Session de délibération** | Acte daté du jury, avec ses résultats figés et son PV |
| `decision_*` | **Résultat** + **Décision du jury** | Le résultat calculé, puis la décision officielle prise par le jury |

### 4.2 Entités

```
Établissement ─┬─ Année académique ── Période (S1, S2, Annuel)
               │        │
               │        └─ Promotion ─┬─ Cours (maxima, type, enseignant)
               │                      └─ Inscription ── Apprenant (personne, matricule)
               │                              │
               │                              ├─ Cote de période (par cours et période)
               │                              ├─ Moyenne d'appui (par cours)
               │                              └─ Décision du jury (par période)
               │
               ├─ Session de délibération (période, promotion, jury, état, PV)
               ├─ Utilisateur (rôle, enseignant lié)
               └─ Journal d'audit (toute écriture, chaînée)
```

Règles d'intégrité :

- Une **inscription** par apprenant et par année. Tous les écrans travaillent sur les inscriptions de
  l'année choisie, ce qui supprime le mélange des années.
- Une **cote** est unique par inscription, cours et période. Elle garde son auteur, sa date et
  l'historique de ses valeurs.
- Le **matricule** est attribué une seule fois et ne change jamais. Le code d'encodage est unique dans l'année.
- Un cours, une promotion ou un enseignant **ayant des cotes** ne peut pas être supprimé : il est archivé.

### 4.3 Le cycle de l'année académique

C'est l'apport le plus important. L'année suit des **étapes explicites**, affichées à tous :

```
 Préparation ─▶ Encodage S1 ─▶ Délibération S1 ─▶ Encodage S2 ─▶ Délibération S2
                                                                       │
                       Clôture ◀── Passage de classe ◀── Publication ◀── Jury annuel
```

| Étape | Ce qui est permis | Ce qui est bloqué |
|---|---|---|
| Préparation | Promotions, cours, maxima, enseignants, inscriptions | Encodage |
| Encodage S1 / S2 | Les enseignants saisissent et **remettent** leurs cotes de la période | Délibération de cette période |
| Délibération S1 / S2 | Le jury examine, décide, clôture la session | Saisie des enseignants pour cette période |
| Jury annuel | Décision finale de l'année | Toute saisie |
| Publication | Bulletins et listes officielles disponibles | Toute modification sans réouverture tracée |
| Passage de classe | Création des inscriptions de l'année suivante | — |
| Clôture | Archive en lecture seule | Tout |

Les interrupteurs actuels restent disponibles pour les cas particuliers, mais l'étape en cours décide
de l'essentiel. Le secrétariat ne règle plus cinq boutons à la main à chaque période.

**Réouverture** : une période déjà délibérée peut être rouverte par le président du jury, avec un
motif obligatoire. La réouverture et toutes les corrections qui suivent sont inscrites au journal.

---

## 5. Le moteur de calcul

Un seul moteur, utilisé par **tous** les écrans et documents : délibération, grille, bulletins,
relevés, statistiques et tableau de bord. Aujourd'hui, quatre formules différentes coexistent.

### 5.1 Règles conservées à l'identique

1. **Cote d'une période** : la cote saisie pour cette période, sur le maxima du cours.
2. **Cours annuel, résultat final** : moyenne des cotes du S1 et du S2.
3. **Moyenne d'appui** : si la cote ne dépasse pas 75 % du maxima, on ajoute
   `moyenne / 10 × 25 % du maxima`, sans dépasser le maxima.
4. **Cours en échec** : points inférieurs à la moitié du maxima.
5. **Résultat ADMIS** : au moins 60 % du total des maxima et aucune cote manquante ; sinon ECHEC.
6. **Mentions** : A dès 90 %, B dès 80 %, C dès 70 %, D dès 60 %, E en dessous.
7. **Cotes héritées** : l'ancienne `cote` vaut pour la période ; l'ancien `examen` sur 10 est converti
   en `examen / 10 × maxima`.

### 5.2 Exemples de référence

Ces cas serviront de tests automatiques et de contrôle avec le jury.

| Cas | Données | Résultat |
|---|---|---|
| Cours S1, sans appui | maxima 20, cote 14 | 14 / 20, réussi |
| Appui appliqué | maxima 20, cote 12, appui 7 / 10 | 12 + 0,7 × 5 = 15,5 / 20 |
| Appui sans effet | maxima 20, cote 16 (plus de 75 %), appui 10 / 10 | 16 / 20 |
| Appui plafonné | maxima 20, cote 15, appui 10 / 10 | 15 + 5 = 20 / 20 |
| Cours annuel | maxima 40, S1 30, S2 20 | (30 + 20) / 2 = 25 / 40 |
| Apprenant complet | 3 cours, total 130 / 200 | 65 %, ADMIS, mention D |
| Cote manquante | 2 cours sur 3 cotés, 90 % sur ceux-ci | ECHEC (incomplet) |

### 5.3 Améliorations du calcul

- **Résultat « Incomplet » distinct de « Échec »** pendant l'encodage. Un apprenant à qui il manque une
  cote apparaît « Incomplet », pas « Échec ». Au moment du jury, la règle actuelle s'applique : sans
  décision contraire, un dossier incomplet est un échec.
- **Cours annuel encodé seulement en S1** : il est compté comme **cote manquante** au résultat final,
  au lieu de valoir S1 / 2. *(À confirmer, voir § 10.)*
- **Moyenne d'appui rendue visible** : elle figure sur la fiche du jury, la grille interne et le relevé
  de l'enseignant (cote de base, appui, cote retenue). Le bulletin officiel peut l'afficher ou non,
  selon un réglage.
- **Rang dans la promotion** : calculé pour chaque période, affichable sur le bulletin et la grille.
- **Arrondis** : points conservés au centième, pourcentages arrondis à 2 décimales, comme aujourd'hui,
  mais avec des nombres décimaux exacts pour éviter les écarts d'arrondi.

---

## 6. Rôles et droits

Aujourd'hui, un « admin » fait tout. ITMCD Pro sépare les responsabilités, sans alourdir une petite équipe :
une même personne peut cumuler plusieurs rôles.

| Rôle | Peut faire | Ne peut pas faire |
|---|---|---|
| **Administrateur** | Comptes, établissement, sauvegardes, appareils | Saisir des cotes |
| **Secrétariat académique** | Année, promotions, cours, inscriptions, étapes, interrupteurs, documents | Décider à la place du jury |
| **Enseignant** | Saisir et remettre les cotes et appuis de **ses** cours, voir ses statistiques et relevés | Voir les autres cours, modifier après remise |
| **Jury** (président et membres) | Examiner, décider, motiver, clôturer, rouvrir (président seul) | Modifier une cote sans motif tracé |
| **Direction** (préfet, proviseur) | Tout consulter, signer les documents | Modifier quoi que ce soit |

---

## 7. Parcours repensés

### 7.1 Enseignant : « mes cours, mes cotes »

1. **Accueil** : la liste de ses cours, avec pour chacun la période ouverte, l'avancement et la date limite.
2. **Feuille de cotes** d'un cours : la liste de tous les apprenants inscrits, une case par apprenant.
   Saisie en continu, avec le clavier numérique, contrôle immédiat du maxima, cases manquantes en évidence.
3. **Saisie par code** conservée comme mode rapide : taper le code, puis la cote.
4. **Moyennes d'appui** sur la même feuille, dans une seconde colonne, quand le réglage l'autorise.
5. **Remettre mes cotes** : l'enseignant valide la feuille complète. Elle passe en lecture seule et le
   secrétariat le voit. Une correction après remise passe par une demande au secrétariat.
6. **Hors connexion** : sur téléphone, la saisie continue sans réseau ; les cotes sont envoyées au
   serveur au retour de la liaison, sans doublon.

### 7.2 Secrétariat : « préparer et suivre l'année »

1. **Tableau de bord de l'année** : étape en cours, cours remis, cours en retard, cotes manquantes,
   apprenants incomplets.
2. **Préparation guidée** : promotions et promotion suivante, cours et maxima, enseignants, inscriptions.
   Avant d'ouvrir l'encodage, une liste de contrôle signale les cours sans enseignant ou sans maxima.
3. **Suivi de l'encodage** : un tableau promotion × cours, coloré selon l'avancement, avec relance des
   enseignants concernés par notification.
4. **Documents** : bulletins, grille, relevés, à tout moment en version provisoire, filigranés comme tels.

### 7.3 Jury : « délibérer en séance »

La délibération devient un assistant en cinq étapes :

1. **Complétude** : liste des cotes manquantes par cours. La séance peut avancer, mais elle est avertie.
2. **Résultats calculés** : par promotion, admis, échecs, incomplets, taux, rang.
3. **Cas à examiner** : l'assistant met en avant les dossiers proches du seuil, avec un seul cours en
   échec ou une cote manquante. Le jury les ouvre un par un.
4. **Décisions** : pour chaque cas, le jury confirme le résultat calculé ou prend une **décision
   motivée** (par exemple « Admis par délibération »). La cote de l'enseignant n'est jamais écrasée.
   Une vraie erreur de cote est corrigée par une **correction tracée**, avec l'ancienne valeur et le motif.
5. **Clôture** : confirmation par mot de passe, résultats figés, **procès-verbal PDF** généré avec la
   date, les membres présents, les statistiques et la liste des décisions.

### 7.4 Direction : « contrôler et signer »

Consultation de tout, sans modification ; signature des PV et des bulletins ; vue des statistiques
de réussite par promotion et par cours.

---

## 8. Documents officiels

| Document | Contenu | Amélioration |
|---|---|---|
| Bulletin | Cours, période, cote retenue, maxima, total, pourcentage, mention, décision, rang | Signataires et logo configurables, **code QR de vérification**, mention « provisoire » avant publication |
| Grille des cotes | Promotion × cours, total, pourcentage, décision | Même moteur que la délibération ; ligne de l'appui en version interne |
| Relevé d'un cours | Apprenants, cote de base, appui, cote retenue, pourcentage | Version « échecs seulement » conservée |
| Procès-verbal de délibération | Session, jury, statistiques, décisions motivées | **Nouveau** |
| Palmarès | Liste des admis et des échecs par promotion, dans l'ordre du rang | **Nouveau** |

Tous les documents sont produits en vrai PDF et en vrai Excel par le serveur, et non plus par capture
d'écran du navigateur.

Le code QR renvoie vers le serveur de l'établissement, qui confirme l'authenticité du bulletin. Un
bulletin falsifié ou modifié est ainsi détectable.

---

## 9. Architecture technique

Elle reprend celle de SCOLIA Pro, déjà éprouvée.

```
 Téléphones et PC des enseignants          Appareil serveur de l'établissement
 ┌──────────────────────────────┐          ┌──────────────────────────────────────┐
 │ ITMCD Pro, client natif       │  réseau  │ ITMCD-Serveur (.NET, ASP.NET Core)    │
 │ Avalonia : Windows, Android,  │ ───────▶ │  ├─ Domaine : moteur de calcul        │
 │ iOS ; file d'attente hors     │ local ou │  ├─ Application : cas d'usage, droits │
 │ connexion                     │ Tailscale│  └─ Infrastructure : SQLite, PDF,     │
 └──────────────────────────────┘          │     Excel, sauvegardes, import MySQL  │
                                           └──────────────────────────────────────┘
```

- **Serveur** : un PC toujours allumé de préférence, ou un téléphone Android en mode serveur.
- **Données** : une base SQLite, sauvegardée chaque jour et vérifiée, avec restauration en un geste.
- **Accès à distance** : Tailscale, sans port ouvert sur le routeur et sans hébergeur.
- **Sécurité** : mots de passe hachés, verrouillage après échecs, biométrie optionnelle sur mobile,
  droits vérifiés par le serveur.
- **Traçabilité** : journal d'audit chaîné, où chaque cote, correction et décision est inscrite.
- **Notifications** : envoyées par le serveur aux appareils reliés, à la place du Web Push.
- **Reprise de l'existant** : import unique de l'export MySQL. Les fiches annuelles d'un même apprenant
  sont regroupées sous une seule personne lorsque la reconduction permet de les relier.

### Découpage du code

| Projet | Rôle |
|---|---|
| `Itmcd.Domain` | Entités, cycle de l'année, moteur de calcul ; aucune dépendance technique, testé seul |
| `Itmcd.Application` | Cas d'usage : encoder, remettre, délibérer, publier, passer de classe ; droits |
| `Itmcd.Infrastructure` | SQLite, PDF, Excel, sauvegardes, import MySQL |
| `Itmcd.Api` | Serveur local, authentification, API |
| `Itmcd.Client` | Écrans natifs Avalonia partagés par Windows, Android et iOS |
| `Itmcd.Tests` | Exemples du § 5.2 et tous les parcours |

---

## 10. Décisions à prendre par l'établissement

1. **Cours annuel encodé seulement en S1.** Le compter comme cote manquante au final (proposé) ou
   garder S1 / 2 comme aujourd'hui ?
2. **Décision motivée du jury.** Le jury peut-il déclarer admis un apprenant sous 60 % ? Si oui, quels
   libellés utiliser (par exemple « Admis par délibération ») ?
3. **Moyenne d'appui sur le bulletin.** L'afficher sur le bulletin officiel, ou seulement sur les
   documents internes ?
4. **Rang dans la promotion.** L'imprimer sur le bulletin ?
5. **Seconde session.** L'établissement organise-t-il des examens de repêchage ? Si oui, ce sera un
   module dédié, absent de l'application actuelle.
6. **Remise des cotes.** Rendre la remise obligatoire avant la délibération, ou seulement conseillée ?
7. **Appareil serveur.** PC toujours allumé (recommandé) ou téléphone Android.

---

## 11. Ordre de réalisation proposé

| Lot | Contenu | Résultat visible |
|---|---|---|
| 1 | Domaine : modèle, cycle de l'année, moteur de calcul et ses tests | Calculs prouvés identiques à ITMCD |
| 2 | Serveur, base SQLite, import MySQL, comptes et droits | Les données actuelles tournent en local |
| 3 | Client enseignant : mes cours, feuille de cotes, appuis, remise, hors connexion | Les enseignants encodent depuis leur téléphone |
| 4 | Client secrétariat : préparation, suivi, inscriptions, documents provisoires | L'année se prépare et se suit |
| 5 | Délibération : assistant, décisions motivées, clôture, PV | Le jury délibère dans l'application |
| 6 | Publication, bulletins avec QR, palmarès, passage de classe | Fin d'année complète |
| 7 | Tailscale, sauvegardes, paquets EXE, APK et IPA | Mise en service sans hébergeur |
