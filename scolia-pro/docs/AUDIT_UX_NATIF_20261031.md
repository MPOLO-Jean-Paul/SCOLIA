# SCOLIA Pro — audit utilisateur novice, application native
**Périmètre : Windows desktop, Android, iPhone/iPad (Avalonia). Pas de produit web.**
**Référence de travail : 31 octobre 2026.** Les modules restent accessibles selon les droits du compte.

## Principe de contrôle
Pour chaque parcours : (1) ouvrir sans formation, (2) comprendre quoi faire en moins de quelques secondes,
(3) pouvoir annuler sans perdre les données, (4) obtenir un retour explicite, (5) retrouver le résultat
sur le bon écran, (6) vérifier l'autorisation côté serveur, (7) tester le clavier desktop, le tactile
Android et le tactile iOS.

**L'audit de code ne vaut pas essai sur appareil physique.** Les scénarios non exécutés sur de vrais
appareils sont marqués « à vérifier ». Aucune correction ne doit supprimer une fonction existante.

| Point | Module / parcours | Constat et intervention | Tests / décision |
| --- | --- | --- | --- |
| 01 | **Recherche du menu** | Risque de retour à Accueil lorsque le menu se ferme avant d'ouvrir le résultat. Navigation d'abord, fermeture automatique par changement de page. Recherche limitée aux fonctions et réglages autorisés. | Tests natifs de destination ciblée et section visible ; essais tactiles à refaire |
| 02 | **Recherche générale** | Une ligne du dialogue exigeait parfois double-clic. Désormais un bouton par résultat, avec sa propre destination : clic unique / toucher ; Entrée au clavier. | Test C# du résultat et compilation Avalonia ; navigation de données réelles à confirmer |
| 03 | **Recherche de l'en-tête desktop** | Elle lançait toujours la recherche générale. L'en-tête Windows affiche désormais un filtre contextuel sur les listes connues. Accueil et pages sans recherche locale : recherche générale. Ctrl+K reste global. | Tests de contexte apprenants/reçus et compilation de la vue |
| 04 | **Recherche de l'en-tête Android/iOS** | Liaison des champs `Search` des pages correspondantes ; jamais un résultat d'une autre rubrique. Menu : uniquement réglages/outils ; accueil : recherche générale. | Tests d'isolement et cas par type de page |
| 05 | **Accueil par rôle** | Les raccourcis « Rapports et exports » et « Tableau de bord » pouvaient apparaître sans permission. Ajout des gardes de droits. | Code C# + tests existants de permissions ; vérifier chaque profil connecté |
| 06 | **Compte : mot de passe** | Cartes desktop/mobile existantes, instructions désormais communes et plus compréhensibles. Le mot de passe reste exigé pour un changement. | Tests de validation / authentification à rejouer |
| 07 | **Compte : biométrie** | Un texte Android apparaissait sur iOS, où l'enrôlement SCOLIA n'est pas encore réalisé. Titre Face ID/Touch ID et état explicite iOS ; activation impossible tant que le service natif d'enrôlement est absent. Empreinte Android existante préservée. | Aucun Face ID iOS déclaré opérationnel sans preuve réelle et stockage sécurisé |
| 08 | **Première installation et connexion** | Audit initial du `SetupViewModel` et du `LoginViewModel`. Contrôler configuration initiale, message d'échec, verrouillage et accès hors ligne. | Tests existants ; essais sur appareils réels à compléter |
| 09 | **Inscription et gestion d'apprenants** | Validation guidée, contrôle des doublons et protection contre les validations simultanées dans les formulaires natifs : changements de la branche d'audit précédente. | Tests `NativeFormUsabilityTests`, opérations sur base de test |
| 10 | **Encaisser (mensualités)** | Mois choisi et montant calculé, possibilité de plusieurs mois, mois encaissés hors ligne exclus de la liste, contrôle du paiement et du reçu. | Tests métier et essais de caisse à poursuivre |
| 11 | **Paiements par lot** | Mode « retards / prochain mois / tous », calcul par apprenant, prévisualisation et erreurs par ligne. | Vérifier doublons, rejets partiels et impression de chaque reçu |
| 12 | **Journal des reçus** | Recherche locale du reçu ou du nom ; actions de détail et annulation à motif. | Vérifier sélection, retour au reçu, annulation et export PDF |
| 13 | **Recouvrement et suivi mensuel** | Recherches locales distinctes, statuts et échéances. | Vérifier filtres croisés et cohérence des sommes |
| 14 | **Notes, cours et résultats** | Pages `Academics.cs` et `Teaching.cs` recensées. | Vérifier saisie, import, retour visuel, bulletins et droits par rôle |
| 15 | **Présences, discipline, pointage personnel** | Parcours natifs existants. La biométrie ordinaire du téléphone ne permet pas d'identifier plusieurs enseignants à tour de rôle : besoin d'un dispositif ou d'un mécanisme d'identification fiable distinct. | Tester entrée + sortie, corrections d'heures, absences et journal d'audit |
| 16 | **Cartes d'accès et rapports** | Recherche de cartes propre au module, exports PDF et documents natifs. | Vérifier logo dynamique, pagination, export et ouverture du document |
| 17 | **Données et sauvegardes** | Modules d'export, restauration, vérification et suppression protégée. | Exiger simulation de restauration sur copie et contrôle de non-perte |
| 18 | **Synchronisation et réseau privé** | États en ligne/hors ligne et opérations en attente ; certains échecs de sondage sont silencieux par design. | Tests de reprise hors ligne, conflit de paiement, idempotence, partage Wi-Fi/Tailscale |
| 19 | **Accessibilité / ergonomie** | Boutons de recherche et sécurité agrandis, actions directes, textes explicites. | Contrôle 320 px, grandes polices, mode sombre, clavier Windows et VoiceOver/TalkBack |
| 20 | **Livraison native** | iOS : GitHub Xcode 27 + workflow IPA signé ; Android et Windows : workflows de compilation. Contrôle C# natif séparé des tests Playwright web. | Ne livrer de nouveau binaire qu'après réussite CI et contrôles réels |

## Règles permanentes
- **Une recherche = un contexte clair** : Menu → fonctions/réglages ; Accueil/Ctrl+K → recherche générale ;
  Apprenants → apprenants ; Reçus → reçus ; Encaisser → apprenants à payer ; autres listes → métier de l'écran.
- **Un résultat = sa destination exacte**, jamais un retour silencieux à Accueil.
- **Une seule action de validation active** ; reçu confirmé avant de proposer les actions d'impression.
- **Un champ en erreur = explication près du champ** ; un échec réseau = état visible et possibilité de reprise.
- **Une fonction inaccessible = invisible ou clairement expliquée** ; les permissions restent vérifiées par l'API.
- Les composants Avalonia sont **partagés** entre Windows et Android/iOS, avec mise en page spécifique selon l'écran.
- **Aucun IPA signé installable n'est annoncé sans certificat Apple, profil et vérification effective.**

## Vérifications automatiques
- `.github/workflows/scolia-natif.yml` : compilation du client Avalonia et tests xUnit sans navigation web.
- `.github/workflows/scolia-phone-ios.yml` : compilation pour simulateur Apple Silicon.
- `.github/workflows/scolia-phone-ipa.yml` : IPA signé, **seulement** après validation des certificats Apple.
- `tests/Scolia.Bureau.Tests/NativeSearchNavigationTests.cs` : résultat à un clic, paramètres ciblés,
  changement de page et recherches indépendantes.

**Les points 01–07 comportent des corrections réelles dans cette branche ; les points 08–20
constituent l'inventaire de vérification et de non-régression, et ne doivent pas être marqués
« entièrement validés » sans essais et résultats traçables.**
