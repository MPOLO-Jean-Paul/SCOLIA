# SCOLIA Pro

Logiciel de gestion scolaire professionnel : secrétariat, caisse, recouvrement,
scolarité, personnel et paie. Successeur de SCOLIA V80 (PowerShell + Excel),
sans aucune dépendance à Excel.

- **Un seul exécutable Windows** : aucun prérequis sur le poste (ni .NET, ni Excel, ni base à installer).
- **Multiposte** : un poste garde les données, les autres ouvrent SCOLIA dans leur navigateur.
- **Données sûres** : base SQLite transactionnelle, sauvegarde quotidienne vérifiée, restauration en un clic.
- **Traçabilité** : reçus numérotés sans trou, annulation motivée, journal d'audit chaîné par SHA-256.

## Démarrage rapide (utilisateur)

1. Télécharger `SCOLIA_Pro_<version>_Windows_x64.zip` et l'extraire dans `C:\SCOLIA`.
2. Lancer `SCOLIA.exe`. L'assistant crée l'établissement, l'année et le compte administrateur.
3. Pour plusieurs postes : sur le poste serveur, lancer `INSTALLER_SERVICE.cmd` puis
   `ACTIVER_RESEAU_LOCAL.cmd`. Les autres postes ouvrent l'adresse affichée.

Le détail est dans [`docs/GUIDE_UTILISATEUR.md`](docs/GUIDE_UTILISATEUR.md) et dans `LIRE_MOI.txt` du paquet.

## Client natif

`src/Scolia.Bureau` est le client de bureau en Avalonia : une fenêtre native, sans navigateur ni moteur web.
Il parle au serveur SCOLIA par la même API que l'interface web. Au lancement, il démarre le serveur `SCOLIA.exe`
du même dossier s'il n'est pas déjà actif. Le paquet Windows contient les deux exécutables.

Captures sans affichage (rendu Avalonia Headless, sur les données d'un serveur lancé) :

```bash
dotnet run --project tests/Scolia.Bureau.Captures -c Release -- <dossier_de_sortie> http://localhost:5080/ <mot_de_passe>
```

## Technologies

| Couche | Choix | Pourquoi |
|---|---|---|
| Serveur | .NET 10 (LTS), ASP.NET Core, API minimales | Performance, support long terme, un seul exécutable |
| Données | EF Core 10 + SQLite (WAL), migrations versionnées | Transactionnel, sans serveur à administrer, fichier unique |
| Interface | React 19, TypeScript, Vite, TanStack Query, React Router | Interface rapide, typée, même rendu sur tous les postes |
| Documents | QuestPDF (reçus A5, relevés, bulletins), QRCoder, ClosedXML | PDF vectoriels, QR de vérification, exports Excel |
| Journaux | Serilog, fichiers quotidiens conservés 60 jours | Diagnostic en production |
| Bureau | Icône de notification WinForms, service Windows | Démarrage avec Windows, arrêt propre |
| Qualité | xUnit, WebApplicationFactory, Vitest, Playwright, GitHub Actions | Chaque règle métier et chaque parcours est testé |

## Structure

```
scolia-pro/
├─ src/
│  ├─ Scolia.Domain/          règles métier pures (aucune dépendance technique)
│  ├─ Scolia.Application/     cas d'usage, DTO, autorisations
│  ├─ Scolia.Infrastructure/  EF Core/SQLite, PDF, Excel, sauvegardes, import V79
│  └─ Scolia.Api/             hôte web, sécurité, points d'API, bureau Windows
├─ web/                       interface React (compilée dans src/Scolia.Api/wwwroot)
├─ tests/Scolia.Tests/        tests du domaine et de l'API complète
├─ e2e/                       parcours Playwright sur le vrai serveur
├─ scripts/                   publication et outils Windows du paquet
└─ docs/                      architecture, sécurité, guide, feuille de route
```

## Développement

Prérequis : SDK .NET 10, Node.js 22.

```bash
# Interface (une fois, puis à chaque modification de web/)
cd web && npm ci && npm run build && cd ..

# Serveur sur http://localhost:5080 (OpenAPI : /openapi/v1.json en développement)
dotnet run --project src/Scolia.Api -f net10.0

# Interface avec rechargement à chaud (relaie /api vers le serveur)
cd web && npm run dev
```

Tests :

```bash
dotnet test Scolia.sln                 # domaine et API
cd web && npx vitest run               # fonctions de l'interface
cd e2e && npm ci && npx playwright test # parcours complets dans Chromium
```

Paquet Windows :

```bash
scripts/publier-windows.sh             # depuis Linux ou macOS
# ou, sous Windows :
powershell -ExecutionPolicy Bypass -File scripts\publier-windows.ps1
```

Nouvelle migration de base :

```bash
dotnet tool restore
dotnet ef migrations add <Nom> --project src/Scolia.Infrastructure --startup-project src/Scolia.Infrastructure --output-dir Persistence/Migrations
```

## Documentation

- [Architecture et décisions](docs/ARCHITECTURE.md)
- [Sécurité et protection des données](docs/SECURITE.md)
- [Guide de l'utilisateur](docs/GUIDE_UTILISATEUR.md)
- [Feuille de route et propositions](docs/FEUILLE_DE_ROUTE.md)
