# SCOLIA Pro — étape 2 : calendrier et vocabulaire (Windows + Android)

## Périmètre

**Windows et Android uniquement**. Aucune livraison ni correction iOS n'est incluse.

### Nomenclature officielle de l'application
| Profil | Singulier affiché | Pluriel affiché |
|---|---|---|
| `ItmIm` | Apprenant | Apprenants |
| `Primaire` | Élève | Élèves |
| `Secondaire` | Élève | Élèves |
| `SuperieurLmd` | Étudiant | Étudiants |

La nomenclature est centralisée dans `InstitutionTerminology`, injectée dans
la session du serveur sous `institutionType` et utilisée dans les interfaces
Avalonia partagées par Windows et Android. Les champs de session facultatifs
`InstitutionType` ont la valeur `ItmIm` par défaut pour les clients anciens
et les sessions locales enregistrées. Titres, menus, compteur de dossiers,
recherche contextuelle et formulaires principaux utilisent les termes adaptés.

Les noms internes **ne changent pas** : `Student`, `Apprenants`,
`api/apprenants`, matricules, IDs, index et droits `apprenants.*`.

Les traductions des documents officiels, des notifications secondaires et des
fonctionnalités futures seront couvertes à l'activation de chaque moteur métier.

### Séparation du calendrier pédagogique et des paiements
Nouvelle table `PeriodesPedagogiques`, indépendante de `Periodes` qui reste
la table des mensualités et échéances. Règles préparatoires :
- ITM/IM : moteur existant inchangé, nouveaux calendriers inutilisés.
- Primaire et secondaire : cours annuels ; périodes d'évaluation configurables.
- Supérieur LMD : cours S1 ou S2 uniquement ; calendrier en deux semestres.
- Le moteur de délibération ITM existant n'est pas modifié.

Les profils non-ITM restent **désactivés à l'installation** tant que les
règles de calcul, la grille de cours et les bulletins officiels ne sont pas validés.

## Vérifications requises avant fusion
- `dotnet build` Windows et Android, `dotnet test` pour domaine, API et
  calendrier, `dotnet ef migrations has-pending-model-changes`.
- Démarrer les anciennes bases ITM et comparer les résultats et encaissements.
- Vérifier la langue des menus, formulaires, rubans et recherches sur Windows
  et les écrans Android étroits.
- Aucun déploiement automatique, aucune fusion vers `main` sans revue.
