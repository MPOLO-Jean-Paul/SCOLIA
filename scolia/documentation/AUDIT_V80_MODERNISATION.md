# SCOLIA V80 — Audit de performance, évaluation des modules et feuille de route

## 1. Ce qu'est SCOLIA aujourd'hui (architecture réelle)

| Couche | Technologie | Rôle |
|---|---|---|
| Lancement | `APPLICATION.vbs` → Windows PowerShell 5.1 (`Lancer.ps1`) | Démarrage, contrôle de syntaxe, instance unique |
| Interface | Windows Forms piloté par ~1,4 Mo de scripts PowerShell (88 fichiers) | Écrans, ruban, thèmes, recherches |
| Contrôles visuels | 14 fichiers C# (15 avec le noyau V80) compilés à la volée (`ChromeModerne.cs`, `DashboardChart.cs`…) | Boutons, onglets, graphiques, notifications |
| Base de données | Classeurs Excel `.xlsm` pilotés par COM + modules VBA | Apprenants, paiements, promotions |
| Passerelle | Macros VBA qui renvoient du JSON (`ITMAppSnapshot`…) | Lecture/écriture de la base |
| Partage comptable | Journal XML/JSON sur dossier réseau (`ScoliaLedger.cs`) | Secrétariat ↔ Comptabilité |

Le point fort : un design soigné (Fluent/iOS, clair/sombre), des protections
sérieuses (transactions, sauvegardes de récupération, empreintes, journaux) et une
batterie de 142 tests (143 avec celui de la V80). Le point faible : chaque clic traverse PowerShell
interprété, puis Excel par COM, puis du VBA. Ces trois couches sont lentes par nature.

## 2. Causes de lenteur identifiées et traitées en V80

### 2.1 Chargement de la base : coût quadratique dans Excel (cause n°1)

`ITMAppSnapshot` construisait sa réponse avec `s = s & ...`. En VBA, chaque `&`
recopie la totalité du texte déjà produit. Le temps croît donc avec le carré du
volume : la lenteur s'aggrave au fil de l'année scolaire, à mesure que les
paiements s'accumulent.

| Volume | Taille transmise | V79 (modèle théorique) | V80 |
|---|---|---|---|
| 79 apprenants, 300 paiements | 0,4 Mo | ~1 s | linéaire, négligeable |
| 200 apprenants, 1 000 paiements | 1,3 Mo | ~14 s | linéaire, négligeable |
| 400 apprenants, 3 600 paiements | 4,0 Mo | ~2 min | linéaire, négligeable |

Correctif : tampon de fragments + `Join` en une passe ; échappement des textes par
`Replace` natif. Le texte produit est identique (équivalence de l'échappement
vérifiée sur 200 000 chaînes aléatoires). Déploiement automatique et sécurisé via
le mécanisme existant (`SCOLIA-PERF-7`, sauvegarde préalable, code personnalisé
jamais écrasé, migration V79 → V80 testée).

### 2.2 Boucles PowerShell exécutées à chaque frappe ou actualisation

Nouveau noyau compilé `AccelerationScolia.cs` (C# 5, compatible PowerShell 5.1) :

| Traitement | Avant | Après (mesure Linux/PowerShell 7, 1 500 lignes) |
|---|---|---|
| Rapprochement d'un tableau affiché | 476 ms | 7 ms |
| Clés de recherche (3 000 textes nouveaux) | 355 ms | 83 ms |
| Construction d'un tableau | boucle par cellule | une seule méthode compilée |
| Réselection du dossier | parcours de toutes les lignes en PowerShell | recherche compilée dans la vue liée |
| Filtre apprenants | boucle PowerShell | filtre compilé |

Sous Windows PowerShell 5.1 (moteur plus lent que PowerShell 7), l'écart est
plus grand encore. Chaque fonction garde son ancienne implémentation en repli.
`TestsAccelerationV80.ps1` prouve l'équivalence stricte avec la V79.

### 2.3 Coûts cachés permanents

- **Gestionnaire « Idle » en PowerShell** : Windows déclenche cet événement après
  chaque mouvement de souris, frappe ou dessin. Le script parcourait toutes les
  fenêtres à chaque fois. Il est remplacé par `ITMFormWatcher` (C#), qui n'appelle
  PowerShell que lorsqu'une nouvelle fenêtre doit être thématisée.
- **`Get-Command` dans les chemins chauds** : 134 appels (création de chaque bouton,
  chaque tableau, chaque opération). Cette commande consulte aussi le disque et le
  PATH Windows ; quand une fonction est absente elle coûte environ 15 ms par appel
  (mesuré). Remplacée par `Test-Path function:` (~0,05 ms).

### 2.4 Points vérifiés et laissés en l'état

- La double analyse syntaxique au lancement n'existe pas : l'empreinte est relue,
  mais l'analyse n'est refaite que si le fichier change. Coût faible.
- Les DLL compilées sont déjà mises en cache par empreinte (`CompilationCache.ps1`).
- `ConvertFrom-Json` de Windows PowerShell 5.1 sur la réponse complète reste un coût
  notable sur une très grande base. Un analyseur compilé est possible (voir 4.1).

## 3. Évaluation des modules

| Module | État | Constat | Recommandation |
|---|---|---|---|
| Connexion, comptes, sécurité | Solide | Comptes nominatifs, verrouillage d'inactivité, chiffrement | Ajouter l'authentification Windows Hello en option |
| Tableau de bord | Bon | Indicateurs et graphiques ; recalcul en PowerShell par apprenant | Porter le calcul des restes dus dans le noyau C# |
| Apprenants (inscription, brouillons, doublons, réinscription) | Très complet | Contrôles d'identité et de doublons poussés | Photo de l'apprenant, import CSV guidé |
| Paiements (encaissement, multiples, reçus QR, prises en charge) | Solide | V79 a déjà ciblé la relecture après un reçu | Envoi du reçu par SMS/WhatsApp, échéancier |
| Comptabilité synchronisée (réseau) | Solide mais complexe | Journal partagé, rapprochement, brouillons de caisse | Sauvegarde hors site chiffrée |
| Exports, listes, cartes d'examens, PDF | Bon | Moteur PDF interne, police Roboto | Modèles personnalisables par l'établissement |
| Scolarité (cours, cotes, délibérations, discipline, présences, rapport de rentrée) | Riche | Plusieurs filtres déjà temporisés (45 ms) | Bulletins de notes, statistiques par promotion |
| Personnel et salaires | Bon | Pointage, retards, paie | Fiche de paie PDF signée |
| Données, sauvegardes, diagnostic | Très prudent | Transactions, empreintes, nettoyage | Tableau de santé de la base (taille, croissance) |
| Préférences, thèmes, ruban, raccourcis | Excellent | Clair/sombre, ruban adaptatif, recherche globale | Conserver tel quel |

Dette technique à résorber :
- 79 versions empilées : 41 notices `LIRE_MOI_*` ou `LISEZ_MOI_*` à la racine, modules suffixés
  `V55`…`V79`, 46 fichiers de compatibilité VBA. Une notice unique et un dossier
  `historique` rendraient le paquet lisible.
- Deux tests V79 étaient cassés et ne pouvaient pas réussir, même sous Windows
  (`TestsGrillesCoursV59`, `TestsFluiditeV77`). Ils sont corrigés en V80.
- Tous les écrans sont construits au démarrage, même ceux qui ne seront pas ouverts.

## 4. Feuille de route proposée (style éditeur de logiciel)

### Étape 1 — fait en V80
Correctifs ciblés, sans changement visible ni migration de données.

### Étape 2 — base de données moderne (gain le plus fort après la V80)
Remplacer Excel comme base de travail par **SQLite**, un fichier unique et
transactionnel, utilisé par la plupart des logiciels de gestion installés. Excel reste
disponible pour les exports et les états. Les recherches et encaissements deviennent
instantanés, quel que soit le volume. Les écritures simultanées sont sûres, et le
lancement d'Excel en arrière-plan disparaît. Une migration automatique depuis les
classeurs actuels serait livrée avec une vérification ligne par ligne.

### Étape 3 — application native compilée, même design
Porter progressivement la logique PowerShell vers une application **C# / .NET 8**.
Les contrôles visuels existants (`ChromeModerne.cs`, `DashboardChart.cs`…) sont déjà
en C# et seraient réutilisés tels quels : l'apparence reste identique. Bénéfices :
démarrage en une à deux secondes, installateur signé, mise à jour automatique,
plus de dépendance à la politique d'exécution PowerShell. La V80 a commencé ce
mouvement avec le noyau `AccelerationScolia.cs`.

### 4.1 Optimisations supplémentaires possibles sans changer d'architecture
- Analyseur JSON compilé pour la réponse complète d'Excel (règles exactes de
  Windows PowerShell 5.1).
- Construction différée des pages rarement ouvertes (Administration, Diagnostic).
- Calcul du tableau de bord et des retards de paiement dans le noyau C#.

## 5. Fonctionnalités proposées

1. Reçu envoyé au parent par SMS ou WhatsApp, avec QR de vérification.
2. Échéancier personnalisé et rappels automatiques des retards.
3. Bulletin de notes et relevé de cotes en PDF, à partir des délibérations.
4. Photo de l'apprenant sur la fiche et la carte d'examen.
5. Sauvegarde automatique chiffrée vers une clé USB ou un cloud choisi.
6. Tableau de santé : volume de la base, croissance, durée des dernières opérations.
7. Journal d'audit consultable : qui a modifié quoi et quand.
8. Portail parent en lecture seule (soldes, présences), à plus long terme.

## 6. Questions ouvertes

1. Combien d'apprenants et de paiements par an la base contient-elle en fin d'année ?
2. Quelles actions restent lentes après la V80 ? Le fichier `performance-v80.csv` les chiffre.
3. Le secrétariat et la comptabilité travaillent-ils sur deux postes en réseau ?
4. Acceptez-vous que la base de travail passe d'Excel à SQLite, Excel restant pour les exports ?
5. Les postes peuvent-ils recevoir une application .NET 8 installée, ou faut-il rester sans installation ?
6. Parmi les fonctionnalités de la section 5, lesquelles sont prioritaires ?

## 7. Confidentialité

Le dépôt GitHub du projet est public. L'archive `SCOLIA OK.zip` qui s'y trouve
contient des données réelles : classeurs, noms d'apprenants, montants de paiements.
Il est recommandé de rendre le dépôt privé, ou de retirer cette archive de
l'historique Git. Le code de la V80 est publié sans aucune donnée de l'établissement.
