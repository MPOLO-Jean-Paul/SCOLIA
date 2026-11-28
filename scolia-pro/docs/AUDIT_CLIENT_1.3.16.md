# Audit des versions clientes — SCOLIA Pro 1.3.16

Périmètre : client natif partagé téléphone/ordinateur, serveur scolaire, documents PDF des deux plateformes, exports Excel et états du tableau de bord. Réalisé à partir de la branche de distribution, commit `7fd86bd6d0130bacdd305997310a67b61d5a085d`.

## Défauts corrigés

| Priorité | Constat et reproduction | Correction |
| --- | --- | --- |
| Haute | Un montant autorisé de 36,75 était affiché 37 ; 0,01 devenait 0. Un formulaire de modification de frais pouvait renvoyer cette valeur arrondie au serveur. | Format monétaire partagé conservant deux décimales si nécessaires ; initialisation exacte des frais, mensualités, salaires et rubriques de paie. Tests d'un paiement réel en USD et d'un formulaire enregistré sans modification. |
| Haute | Les exports et documents utilisaient le même arrondi d'affichage. Le classeur imposait un format entier même aux cellules décimales. | Reçus/relevés PDF du poste et du téléphone alignés sur le format commun ; cellules Excel numériques avec deux décimales pour les montants fractionnaires. Les données entières conservent leur format. |
| Haute | Le paiement des salaires créait directement une dépense sans contrôler le disponible en espèces. Les rubriques de paie n'appliquaient pas toutes les limites monétaires de l'école ; le journal de modification imposait « FC » même en USD. | Vérification du disponible sous le verrou d'écriture avant toute mutation ; validation des décimales et limites des salaires, rubriques et net ; devise réelle dans le journal. Tests de refus atomique, reprise par banque, paiement en espèces financé et double paiement refusé. |
| Moyenne | Les choix de sections/promotions n'étaient chargés qu'une fois par écran. Revenir sur un tableau de bord conservé dans l'historique pouvait garder une promotion retirée. | Relecture des choix à chaque chargement, conservation des identifiants valides et remplacement des listes seulement lorsqu'elles changent. Aucun événement de filtre ni chargement supplémentaire pendant cette synchronisation. |
| Moyenne | Après une erreur, une sélection pouvait rester annoncée en actualisation tandis que des informations auxiliaires provenaient du chargement précédent. | État d'erreur explicite, retrait des données périmées et invalidation de l'export ; scénario de reprise après HTTP 500 pour les deux tableaux de bord. |
| Moyenne | Une sauvegarde configurée toutes les heures restait indiquée à jour pendant près de trois heures. | Tolérance de 10 % de l'intervalle, plafonnée à deux heures. Une horloge future de plus de cinq minutes n'est pas considérée comme une preuve de sauvegarde récente. |

## Vérification

- Contrôles locaux : 773 tests du client de bureau, 57 tests du téléphone, 41 scénarios serveur ciblés, 51 contrôles natifs du fonctionnement, 32 profils des quatre comptes et 196 rendus d'interfaces. Aucun débordement détecté dans ces rendus.
- Extraction du texte des reçus PDF du téléphone et du poste : affectation et total de 36,75 $, solde de 63,25 $ confirmés dans les deux documents.
- Tests de précision : montants positifs, négatifs, entiers et centimes isolés ; paiement USD réel, compte, tableau de bord, export XLSX et reçu PDF.
- Scénarios natifs contrôlés : filtres actualisés, conservation de la sélection, erreurs HTTP 500, blocage de l'export et reprise ; enregistrement d'une catégorie de frais sans perte de centimes.
- Tests de fréquence des sauvegardes horaires, quotidiennes et hebdomadaires, aux limites et avec une date future.
- Paie et caisse : refus d'espèces insuffisantes sans écriture partielle, dépense unique en banque, caisse exacte après salaire décimal, journal monétaire et validation des valeurs interdites.
- Contrôles des interfaces et des quatre rôles sur les formats mobiles et de bureau ; tests métier, droits et fonctionnement fournis par les suites de validation du dépôt.

Les rapports d'exécution et captures sont produits dans les artefacts de validation de la version. Ces contrôles utilisent des bases temporaires et des données de démonstration ; ils ne remplacent pas une observation des appareils et réseaux réels de chaque école.
