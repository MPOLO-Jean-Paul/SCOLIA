# Audit de performances SCOLIA V78

Base : V77. Audit statique des sources, examen des parcours critiques et tests isolés.
Ce rapport distingue les corrections de code des limites qui demandent un profilage
sur Windows. Il ne constitue pas une preuve que toutes les latences sont supprimées.

| Parcours | Cause constatée | Modification V78 |
|---|---|---|
| Choix d'un apprenant / historique | Filtre global, normalisation et tri des dates à chaque sélection | Index par matricule ; tris mémorisés ; cache borné à 128 sélections ; invalidation au remplacement du tableau source |
| Recherche comptable | Normalisation de chaque reçu, tri, projection et totaux à chaque frappe | Index normalisé et projection triée réutilisés ; invalidation par source/année |
| Discipline | Parcours de toutes les décisions pour chaque apprenant ; double énumération des fichiers | Une énumération, index ordinal par matricule, mêmes règles et validations avant application |
| Réception automatique | Sérialisation XML, vérification et écriture des brouillons sur le fil UI | Lecture et sauvegarde dans une même tâche C# ; résultat livré seulement après succès durable |
| Moniteur réseau | Tick réentrant possible pendant une autre action ; réponses d'un contexte antérieur ; nouveaux objets pour un journal identique | Verrou logique avec finally ; clé de contexte ; remplacement seulement si identifiants/révisions changent |
| Publication avant paiement | Reconstruction et lectures Excel répétées sans changement ; ancienne publication parfois encore en vol | Réutilisation restreinte aux données privées verrouillées ; invalidation explicite respectée ; dernière révision publiée avant paiement |
| Attentes réseau | GetResult/Commit synchrones sur le fil UI | Tâche C# + boucle de messages native pendant l'attente ; propagation des erreurs et maintien des identités en attente |
| Fin d'un lot | Recalcul du tableau de bord et du journal des paiements même masqués | Mise à jour de la vue active ; état des contrôles restauré une fois par Do-Work |
| Diagnostics | Agrandissements successifs d'un tableau PowerShell avec copie | List générique puis conversion finale |
| Mise en page paiement | Remesure après Set-Grid alors que celui-ci traite déjà l'affichage | Suppression de la seconde remesure systématique |

## Garanties de cohérence testées

- Recherche : ordre, dates invalides, filtres, accents, plusieurs mots, absence de résultat,
  remplacement des données après sauvegarde, changement d'année et taille du cache.
- Discipline : mêmes décisions indexées et non indexées, dernier jour inclus, retour à
  la fin du renvoi, renvoi définitif d'une année antérieure, annulation, permissions,
  verrouillage et restauration. Les contrôles avant admission ne sont pas mis en cache.
- Réception : brouillon durable avant résultat, objet retourné indépendant du cache,
  erreurs de verrou et archive corrompue propagées sans écrasement silencieux.
- Publication : données inchangées, invalidation forcée, modification des données,
  classeur partagé non mis en cache, publication antérieure encore en vol, échec à
  réessayer et absence d'autorisation refusée immédiatement.
- Une erreur d'interface pendant une tâche réseau non terminée reste une erreur
  d'issue incertaine. Le marqueur d'opération ne doit pas être supprimé comme s'il
  s'agissait d'un refus métier confirmé.
- Les transactions, écritures ciblées, migrations VBA et soldes antérieurs restent
  couverts par les suites existantes. Les modules VBA n'ont pas été modifiés dans V78.

## Mesure reproductible

`TestsPerformancesV78.ps1` construit 10 000 paiements pour 1 000 apprenants et compare
20 sélections avec le parcours antérieur puis avec l'index préparé. Une première
exécution a mesuré **20 346 ms contre 41,54 ms**. La construction initiale de l'index
est exclue ; ce test porte seulement sur la sélection d'historiques en mémoire.
Les valeurs de la validation finale figurent dans `VALIDATION_V78.txt`.
Ce résultat ne mesure ni un paiement complet, ni Excel, ni le réseau, ni WinForms.

## Autres parcours examinés et limites restantes

- **Démarrage** : cache de compilation C# et validation des scripts déjà présents.
  La première compilation, l'ouverture Excel, les migrations connues et la première
  photographie des données peuvent rester longues. Les validations ne sont pas retirées.
- **Enregistrement Excel** : copies de récupération, contrôles et sauvegarde dans
  StockageClasseurs et ModuleFiabilite restent synchrones. Application.Calculate et
  ThisWorkbook.Save dépendent du classeur. Aucune réussite anticipée n'est affichée.
- **Publication initiale** : la préparation du répertoire et la lecture du journal
  Excel restent sur le fil principal. Seuls les doublons de publication ont été évités.
- **Exports / PDF / impression** : accès Excel et génération de documents potentiellement
  longs ; pas de refonte du moteur d'export dans cette version.
- **Sauvegardes quotidiennes** : déclenchées à échéance avec garde sur les dialogues
  et travaux actifs ; copie et vérification conservées pour protéger les données.
- **Brouillons d'inscription** : délai après frappe et empreinte existants évitent déjà
  les écritures identiques ; fonctionnement conservé.
- **Sécurité** : timer de verrouillage et contrôles d'autorisation conservés.
- **Thèmes et redimensionnement** : mise à jour lors d'un changement réel ; mesures
  de texte et de lignes visibles encore nécessaires. Une grande première vue n'est
  pas encore virtualisée et conserve une préparation proportionnelle à ses données.
- **Imports manuels** : leur orchestration reste distincte du moniteur automatique ;
  certains traitements Excel et sauvegardes locales s'y exécutent encore sur l'UI.

Pour supprimer aussi ces blocages structurels, il reste à isoler **toute la session
Excel** dans un worker STA dédié et à transformer les appels en continuations UI.
Le faire partiellement en partageant les objets COM existants entre fils serait
risqué. Cette refonte n'a pas été intégrée sans tests Windows des transactions,
arrêts, reprises, exports et accès simultanés.

## Conditions de validation

22 suites automatisées passent sous PowerShell 7.4.6/Linux. Pour cinq anciennes
suites C#, seules les références d'assemblages .NET Framework ont été adaptées au
runtime Core par le lanceur de tests. Trois défauts de test ont été corrigés :
expression régulière interpolée à tort dans V72, dépendance manquante de réception,
et références d'assemblages du sous-processus de réouverture des brouillons.
Leurs assertions métier sont conservées.

La véritable interface Windows et Excel/VBA ne sont pas exécutables dans cet
 environnement. Un test Windows de traitement des événements pendant l'attente
 réseau est fourni ; il reste à exécuter. Sur une copie représentative des données,
 vérifier navigation, recherche, inscription, paiement simple et multiple,
 synchronisation des deux comptes, fermeture/réouverture et exports, puis consulter
 le journal `performance-v78.csv`.

## Inventaire statique

Sources inventoriées hors scripts de tests : 88 fichiers .ps1, 14 fichiers .cs, 63 fichiers .txt. Les fichiers texte comprennent les modules VBA et leurs variantes de compatibilité. Cet inventaire ne signifie pas une exécution exhaustive de chaque fonction.

Fichiers de production modifiés par rapport à V77 :

- `programme/Application.ps1`
- `programme/DisciplineV60.ps1`
- `programme/Lancer.ps1`
- `programme/PaiementsMultiplesV72.ps1`
- `programme/PerformanceInterface.ps1`
- `programme/ScoliaIntegration.ps1`
- `programme/ScoliaLedger.cs`
