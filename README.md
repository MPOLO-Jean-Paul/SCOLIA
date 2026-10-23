# SCOLIA

Plateforme de gestion des activités du secrétariat scolaire (inscriptions,
paiements, comptabilité synchronisée, scolarité, personnel, exports PDF).

## Contenu du dépôt

- `scolia/` : code source de l'application (édition V80), sans aucune donnée
  d'établissement. Les dossiers `data`, `configuration`, `journaux`, `archives`,
  `exports` et `sources` ne contiennent que leur notice.
- `scolia/programme/` : scripts PowerShell, composants C#, corrections VBA et tests.
- `scolia/documentation/AUDIT_V80_MODERNISATION.md` : audit de performance,
  évaluation des modules, feuille de route et questions ouvertes.

## Lancement (Windows)

1. Extraire le paquet complet dans un nouveau dossier.
2. Lancer `00_Demarrage\APPLICATION.vbs`.
3. Vérifier l'édition avec `00_Demarrage\VERIFIER_PERFORMANCES_V80.cmd`.

Prérequis : Windows 10/11, Windows PowerShell 5.1, .NET Framework 4.x, Microsoft Excel.

## Confidentialité

Ce dépôt est public. Ne jamais y ajouter de classeurs `.xlsm`, de journaux ou de
fichiers de configuration réels : `.gitignore` les exclut.
