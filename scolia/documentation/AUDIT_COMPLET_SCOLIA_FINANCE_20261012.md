# AUDIT COMPLET — SCOLIA FINANCE

Date : 12 octobre 2026

## Périmètre contrôlé

- 169 scripts PowerShell dans `programme/`.
- 14 composants C#.
- 99 scripts de tests `Tests*.ps1`.
- 54 classeurs XLSM présents dans le paquet.
- Configurations JSON/XML, ressources graphiques et manifeste de mise à jour V57.
- Parcours finance : paiements, import réseau, brouillons, reçus communs, corrections, rapprochement, motifs de paiement et comptes nominatifs.

## Corrections appliquées

1. **Référence de reçu unique par apprenant**
   - La publication réseau refuse désormais un même numéro de reçu rattaché à plusieurs matricules.
   - Les paiements multi-mois du même apprenant restent autorisés et regroupés normalement.

2. **Journal de transfert réseau durci**
   - Une référence source vide ou un identifiant durable invalide est refusé.
   - Si un même reçu source pointe vers deux identifiants différents, l'import s'arrête avec un message de rapprochement au lieu d'écraser silencieusement une correspondance.
   - Les identifiants durables sont normalisés en minuscules avant comparaison.

3. **Corrections de reçus hérités**
   - Une correction ne choisit plus arbitrairement le premier apprenant lorsqu'un ancien classeur contient un numéro de reçu réutilisé pour plusieurs personnes.
   - Le doublon doit être corrigé avant synchronisation.

4. **Origine comptable cohérente**
   - Les opérations saisies depuis l'espace caisse/comptabilité sont maintenant enregistrées avec `Compte = Comptabilité`, même lorsque l'utilisateur possède un compte de rôle `Mixte` ou accède à l'espace depuis un compte secrétaire autorisé.
   - L'identité réelle de l'utilisateur reste conservée séparément dans `OperatorId` et `OperatorName`.

5. **Reprise des brouillons de caisse**
   - Les anciens brouillons enregistrés avec `Compte = Mixte` ou `Compte = Secrétariat` restent validables par leur propriétaire.
   - Après contrôle de l'identifiant nominatif du compte, ils sont normalisés vers `Comptabilité`.
   - Un brouillon appartenant à un autre compte nominatif reste refusé.

6. **Vérification V57 rendue complète**
   - Le vérificateur principal ne contenait qu'une liste partielle (48 tests uniques sur 99 disponibles).
   - Il découvre maintenant automatiquement tous les `Tests*.ps1` présents, les exécute isolément et journalise chaque résultat.

7. **Compilation C# complétée**
   - `TestsCompilation.ps1` vérifie désormais les 14 composants C#, y compris `ScoliaLedger.cs` et `ScoliaNetwork.cs` qui étaient absents de cette vérification.
   - Les tests qui compilent directement ces deux composants utilisent désormais les mêmes références .NET que l'application (`System.Runtime.Serialization`, `System.Core`, etc.).

8. **Tests auxiliaires réellement exécutables seuls**
   - `TestsInstallationSession.ps1`, `TestsHistorique.ps1` et `TestsLocalisation.ps1` exécutent désormais leur contrôle lorsqu'ils sont lancés directement, tout en restant sûrs lorsqu'ils sont dot-sourcés par l'installateur.

9. **Manifeste V57 réparé**
   - Plusieurs empreintes SHA-256 du manifeste V57 ne correspondaient plus aux fichiers réellement présents.
   - Les 78 entrées du manifeste ont été recalculées et sont maintenant cohérentes avec le paquet.

## Contrôles d'intégrité effectués dans l'environnement d'audit

- Délimiteurs PowerShell : aucune anomalie détectée par le contrôle lexical statique.
- Références littérales `Join-Path $PSScriptRoot` : aucun fichier réellement absent après normalisation des séparateurs Windows.
- JSON : aucun fichier invalide.
- XML autonome : aucun fichier invalide.
- XLSM : 54/54 archives Office passent le contrôle d'intégrité ZIP.
- Images PNG/JPG/ICO : ressources lisibles.
- Son de notification WAV : structure valide.
- Manifeste V57 : 78/78 fichiers présents et 78/78 empreintes conformes.
- Aucune suppression de fichier de l'archive d'origine.

## Vérification finale à effectuer sur Windows

L'environnement d'audit ne contient pas Windows PowerShell 5.1, Microsoft Excel COM, WinForms ni le compilateur .NET Framework de Windows. Il est donc impossible d'exécuter ici les tests natifs qui ouvrent Excel ou les fenêtres de SCOLIA.

Sur le poste Windows cible, exécuter :

`00_Demarrage\VERIFIER_V57.cmd`

Le vérificateur exécute maintenant automatiquement les 99 tests disponibles. Les rapports sont enregistrés dans le dossier `journaux`.

## Fichiers fonctionnels principalement corrigés

- `programme/ImportsReseau.ps1`
- `programme/FinanceSynchronisee.ps1`
- `programme/ScoliaIntegration.ps1`
- `programme/SynchronisationScolia.ps1`
- `programme/VerifierV57.ps1`
- `programme/TestsCompilation.ps1`
- plusieurs tests de finance/réseau afin d'aligner leurs dépendances sur l'exécution réelle
- `programme/manifest-v57.json`
