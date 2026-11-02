# SCOLIA Pro — audit d'usage, 31 octobre 2026

But : une application que l'on comprend sans formation. Chaque écran a été parcouru comme un
utilisateur qui le découvre, sur téléphone (412 × 915 et 360 × 780) et sur ordinateur
(1366 × 768 et 1024 × 700), avec un établissement rempli et un établissement tout neuf.

Base de travail : les branches Codex du 31 octobre, réunies sur la branche de travail.

## Points corrigés

| N° | Écran | Ce qui gênait | Correction |
| --- | --- | --- | --- |
| 1 | Encaisser | Sur téléphone, le bouton « Encaisser » était tout en bas, après environ 2 400 points de formulaire. | Barre fixe en bas : total, mois choisis, bouton « Encaisser ». Toujours visible, téléphone et ordinateur. |
| 2 | Encaisser | Sans apprenant choisi, tout le formulaire de versement s'affichait, vide. | Une seule consigne : « Cherchez d'abord l'apprenant ». Le versement apparaît ensuite. |
| 3 | Encaissement par lot | La liste commençait par des dizaines de lignes « À jour » sans rien à encaisser. | Seuls les apprenants qui ont à régler s'affichent. Barre fixe « Enregistrer le lot » avec le total coché. |
| 4 | Encaissement par lot | Liste vide sans explication. | Le message dit comment encaisser plus tôt : « Prochain mois » ou « Tous les mois ouverts ». |
| 5 | Listes du téléphone | Cinq à six filtres avant la première ligne de la liste. | La recherche reste visible ; les autres filtres se replient derrière « Filtres », qui compte ceux qui sont changés. |
| 6 | Cartes du téléphone | Lignes vides (« Résultat », « Diagnostic ») ; textes longs tassés à droite. | Une ligne vide disparaît ; une valeur longue passe sous son libellé. |
| 7 | Recherche générale | « ACC » donnait d'abord « Thème et couleurs » (mot-clé caché « accent »). | Une fonction nommée passe avant un mot-clé caché ; une saisie de matricule cherche d'abord les dossiers. |
| 8 | Cours et notes | Grille vide sous « Sélectionnez un cours » ; ordre des blocs inversé sur téléphone. | Étapes numérotées 1, 2, 3 ; chaque bloc vide dit quoi faire ; ordre de lecture rétabli. |
| 9 | Présences | « 0 absent » alors que l'appel n'était pas fait. | « Pas encore fait », et un encadré qui explique comment faire l'appel. |
| 10 | Accueil, établissement neuf | Rien ne disait par où commencer ; « Faire l'appel » sans aucun apprenant. | « Pour bien démarrer » : 1. promotions et frais, 2. premier apprenant, 3. première mensualité. |
| 11 | Apprenants, liste vide | « Aucun apprenant trouvé · Modifiez la recherche », même sans aucune recherche. | Sans filtre : « Aucun apprenant inscrit · Inscrivez le premier ». Avec filtre : comment l'élargir. |
| 12 | Prises en charge, Présences | Grille vide sans message. | Message qui dit où et comment ajouter. |
| 13 | Formulaires | L'aide d'un champ apparaissait deux fois : dans le champ et dessous. | Une seule fois, sous le champ : elle reste lisible pendant la saisie. |
| 14 | Inscription | Avec une seule promotion, il fallait quand même la choisir. | Préremplie quand il n'y a qu'un choix. |
| 15 | Journal d'audit | Les connexions étaient signées « système ». | Elles portent le nom du compte qui se connecte. |

## Vérifications faites

| Contrôle | Résultat |
| --- | --- |
| Tests automatiques (client, serveur, mobile) | 221 + 181 + 10, tous réussis |
| Toutes les commandes du menu, téléphone | 135 essayées, 0 à revoir |
| Toutes les commandes du menu, ordinateur | 171 essayées, 0 à revoir |
| Parcours complet (inscription, encaissement, notes, appel, paie, sauvegarde…) | 0 échec |
| Mise en page téléphone, 6 tailles et 2 tailles de texte | 0 échec |
| Mise en page ordinateur | 0 échec |
| Compilation Android du code partagé | réussie |

## Ce qui reste à vérifier sur un vrai appareil

- Le geste du doigt et la barre fixe sur un téléphone Android réel.
- Le repli des filtres avec le clavier du téléphone ouvert.
- L'écran d'un établissement neuf de bout en bout, avec un vrai compte Comptabilité.

## Captures

Dans `docs/audit-20261031/` : Encaisser avec la barre fixe, listes aux filtres repliés,
Cours et notes guidé, établissement neuf guidé.
