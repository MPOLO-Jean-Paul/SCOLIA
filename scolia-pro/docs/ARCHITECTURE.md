# Architecture de SCOLIA Pro

## Vue d'ensemble

```
 Postes du réseau local (Edge, Chrome)          Poste serveur
 ┌─────────────────────────┐   HTTP    ┌──────────────────────────────────────┐
 │ Interface React (PWA)   │ ───────▶ │ SCOLIA.exe  (ASP.NET Core, Kestrel)  │
 │ même rendu sur chaque   │  cookie   │  ├─ Api : sécurité, points d'API      │
 │ poste, rien à installer │  + X-Scolia│  ├─ Application : cas d'usage        │
 └─────────────────────────┘           │  ├─ Domain : règles métier            │
                                       │  └─ Infrastructure : SQLite, PDF,     │
                                       │     Excel, sauvegardes, import V79    │
                                       │ donnees/scolia.db (WAL)               │
                                       │ donnees/sauvegardes/  journaux/       │
                                       └──────────────────────────────────────┘
```

Le serveur et l'interface sont livrés dans un seul exécutable autonome. Sur le
poste qui l'exécute, SCOLIA s'ouvre dans une fenêtre d'application Edge ou Chrome
et place une icône dans la zone de notification. En service Windows, il démarre
avec la machine, sans session ouverte.

## Couches (architecture propre)

| Projet | Contenu | Dépend de |
|---|---|---|
| `Scolia.Domain` | Entités, règles de calcul (soldes, répartition, matricules, notes, paie), validation | rien |
| `Scolia.Application` | Services de cas d'usage, DTO, abstractions (base, audit, horloge, documents) | Domain |
| `Scolia.Infrastructure` | EF Core/SQLite, migrations, QuestPDF, ClosedXML, sauvegardes, import Excel V79 | Application |
| `Scolia.Api` | Hôte web, authentification, autorisations, en-têtes de sécurité, erreurs RFC 9457, bureau Windows | tous |

Les règles métier n'ont aucune dépendance technique : elles sont testées seules,
en quelques millisecondes.

## Modèle de données

- Identifiants Guid v7 : uniques sur tous les postes, triés dans le temps.
- Montants stockés en centièmes entiers : aucun arrondi binaire.
- Dates en UTC dans la base, affichées en heure locale.
- Concurrence optimiste : chaque ligne porte une version ; une modification
  simultanée du même dossier est refusée avec un message clair au lieu d'écraser.
- Noms de tables en français, pour qu'un auditeur lise la base directement.

## Écritures et cohérence

Toutes les écritures passent par une **porte d'écriture** unique : une transaction
par opération, sérialisée. Exemples de garanties :

- deux caissiers qui encaissent au même instant ne peuvent pas dépasser le solde dû ;
- le numéro de reçu suivant est attribué dans la même transaction que le reçu :
  la numérotation `RC-AAAA-000001` n'a jamais de trou ni de doublon ;
- une clé d'idempotence par encaissement évite le double reçu après un double clic
  ou une coupure réseau ;
- l'entrée d'audit est écrite dans la même transaction que l'opération : pas
  d'opération sans trace, pas de trace sans opération.

## Journal d'audit infalsifiable

Chaque entrée contient l'empreinte SHA-256 de son contenu et de l'entrée
précédente. Une modification ou une suppression faite en dehors de SCOLIA, par
exemple avec un éditeur SQLite, casse la chaîne. La vérification indique la
première entrée altérée.

## Règles métier reprises de SCOLIA V79

- Matricule : code promotion sur 4 caractères suivi d'un rang sur 2 caractères (01 à 99 puis alphanumérique).
- Mensualité due le 6 du mois suivant ; une échéance par mois de l'année scolaire.
- Un versement est réparti de l'échéance la plus ancienne à la plus récente et ne peut pas dépasser le reste dû.
- Prise en charge : pourcentage de couverture par frais ; le net ne descend jamais sous ce qui est déjà payé.
- Changement de promotion : les paiements suivent les frais équivalents de la nouvelle promotion.

## Décisions d'architecture (ADR)

### ADR-001 : SQLite plutôt qu'Excel ou un serveur de base

- **Contexte** : V79 lisait et écrivait des classeurs par COM ; le temps croissait avec le volume.
- **Décision** : SQLite en mode WAL, fichier unique, sauvegarde en ligne par l'API native.
- **Conséquences** : aucune installation, lectures concurrentes, écritures sûres.
  Pour un groupe scolaire avec des dizaines de postes simultanés, un fournisseur
  PostgreSQL pourra être ajouté sans toucher au domaine (voir la feuille de route).

### ADR-002 : interface web servie localement plutôt que WinForms

- **Contexte** : WinForms piloté par PowerShell imposait une installation par poste.
- **Décision** : React servi par le même exécutable ; fenêtre d'application Edge ou Chrome.
- **Conséquences** : un seul poste à mettre à jour, même interface partout, impression
  et PDF natifs, utilisable sur tablette.

### ADR-003 : session par cookie plutôt que jetons dans le navigateur

- **Décision** : cookie `HttpOnly`, `SameSite=Strict`, en-tête anti-CSRF obligatoire,
  empreinte de sécurité revalidée à chaque requête.
- **Conséquences** : un script ne peut pas lire la session ; désactiver un compte
  ou changer un mot de passe ferme immédiatement les sessions ouvertes.

### ADR-004 : annulation plutôt que suppression

- **Décision** : un reçu, une dépense ou une écriture validée n'est jamais supprimé ;
  il est annulé avec un motif, reste visible et porte la mention « ANNULÉ ».
- **Conséquences** : conforme aux usages comptables ; l'arrêté de caisse verrouille la période.

### ADR-005 : exécutable autonome multi-cible

- **Décision** : `net10.0` pour le serveur multiplateforme et les tests,
  `net10.0-windows` pour le bureau ; publication en fichier unique compressé, précompilé (ReadyToRun).
- **Conséquences** : démarrage en environ deux secondes, aucun prérequis. Le paquet
  pèse environ 100 Mo, car il embarque le moteur .NET.

## Performances mesurées

| Mesure | Résultat |
|---|---|
| Démarrage de l'exécutable autonome, base de démonstration | 1,7 s |
| Interface compressée transmise au navigateur | 135 Ko de JavaScript, 5 Ko de CSS |
| Import du classeur V79 réel, 57 apprenants et 57 reçus | totaux identiques au classeur, réimport sans doublon |
