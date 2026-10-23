# Validation SCOLIA V79

## Contrôles réalisés lors de la préparation

- cohérence des manifestes JSON ;
- cohérence des fichiers de compatibilité V78 utilisés par la migration performance ;
- présence des API VBA publiques attendues après réécriture du chemin de paiement ;
- absence du verrou de promotion/année fondé uniquement sur l'existence de paiements ;
- absence du rechargement intégral `ITMChargerDonnees` dans `ITMAppPaiement` ;
- lecture ciblée de l'historique de paiement ;
- présence du chemin différentiel post-écriture pour apprenants, paiements et soldes,
  avec repli complet réservé aux corrections structurelles ;
- recalcul limité au classeur dans le module de fiabilité ;
- suppression de l'auto-dimensionnement permanent des lignes des grilles génériques ;
- regroupement 16 ms des layouts responsifs et 45 ms des recherches concernées ;
- cohérence du manifeste de mise à jour V57 livré dans le paquet ;
- contrôle lexical/structurel des scripts PowerShell modifiés dans l'environnement de
  préparation.

## Validation Windows fournie

Exécuter :

`00_Demarrage\VERIFIER_PERFORMANCES_V79.cmd`

Le lanceur enchaîne notamment les contrôles de syntaxe PowerShell, V79/V78,
migration performance, enregistrement rapide, paiements multiples, réactivité,
Scolarité, Cours/Attributions et interfaces.

## Limite de l'environnement de préparation

L'environnement ayant servi à préparer le ZIP ne contient ni Microsoft Excel ni
Windows PowerShell/WinForms. Il n'est donc pas possible d'y compiler/exécuter la
macro VBA dans Excel ni de mesurer la vraie boucle graphique Windows. Le vérificateur
fourni termine ces contrôles sur le poste Windows cible. Il est recommandé de tester
d'abord sur une copie de la base réelle avant remplacement de la version utilisée en
production.
