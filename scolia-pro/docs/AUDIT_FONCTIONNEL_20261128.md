# Vérification fonctionnelle du client — 28 novembre 2026

Base : version officielle 1.3.13, commit `2ab9d2f84f235c9ff6c3caac152fbee04d91ce44`. Les corrections de ce lot concernent les opérations financières, les notes et la fiche « À propos », avec les vues natives partagées entre Android et Windows.

## Anomalies reproduites et corrections

| Parcours | Problème observé | Comportement corrigé |
| --- | --- | --- |
| Historique de caisse | Un arrêté ultérieur était pris comme ouverture d'une date antérieure. | Seuls les arrêtés jusqu'à la date consultée participent au calcul. |
| Arrêté de caisse | La correction de l'historique doit conserver l'interdiction d'arrêter une date antérieure au dernier arrêté global. | Le contrôle utilise le dernier arrêté global, indépendamment de la date de consultation. |
| Dépense antidatée | Des espèces disponibles hier, mais déjà dépensées aujourd'hui, pouvaient être dépensées à nouveau. | La disponibilité est vérifiée à la date saisie et à la date actuelle avant tout enregistrement. |
| Journal des dépenses | Seules les 200 premières lignes étaient accessibles et leur somme était annoncée comme le total du journal. | Navigation par pages, total de tous les résultats filtrés, retour à la première page après changement de filtre. |
| Évaluations | Une réponse tardive pouvait ajouter les notes d'une ancienne évaluation dans la nouvelle. | Chaque chargement garde sa sélection et ignore les réponses devenues obsolètes. |
| Cotes ITM/IM et notes LMD | Changer de semestre, d'EC ou de session pendant un chargement pouvait afficher la mauvaise feuille. | Les lignes sont invalidées immédiatement et la réponse est rattachée au contexte demandé. |
| Enregistrement des notes | La fin d'un envoi pouvait recharger la feuille et effacer une modification faite pendant l'envoi. | Le rafraîchissement a lieu seulement si la feuille et la saisie n'ont pas changé. L'envoi conserve sa cible et ses valeurs initiales. Le suivi des cotes et des notes LMD prend ensuite les valeurs effectivement acceptées comme référence, sans renvoyer les valeurs déjà enregistrées. |
| Code anonyme enseignant | Une ancienne vérification pouvait valider un nouveau code saisi pendant l'attente. La fin d'un enregistrement pouvait vider la copie suivante. | Résultats et retours d'enregistrement sont rattachés au code, à la saisie et à la feuille d'origine. |
| Remise et import des cotes | Une confirmation ou un fichier en attente pouvait s'appliquer après un changement de feuille. | Une feuille différente exige de relancer l'action. L'aperçu d'import identifie le cours et la nature des cotes. |
| À propos | Fonds superposés, icônes mal contenues et actions irrégulières. | Un seul cadre de 40 points par contact, pictogramme centré de 24 points, actions centrées dans 44 points. |
| Copie des coordonnées, adresses et invitations | Une confirmation pouvait être affichée sans gestionnaire de copie, ou une erreur du presse-papiers pouvait remonter sans traitement. | Gestion commune du presse-papiers : confirmation après copie effective ; message explicite en cas d'indisponibilité, d'annulation ou d'échec, y compris pour une fenêtre détachée. |

## Contrôles reproductibles

- Les trois nouveaux tests API de `BusinessApiTests` reproduisent l'historique de caisse, le refus d'un ancien arrêté et le dépassement d'espèces par une dépense antidatée. Avant correction, deux de ces trois scénarios échouaient.
- Le mode natif `fonctionnement` de `Scolia.Bureau.Captures` utilise un serveur local de test avec des réponses volontairement retardées. Il vérifie les changements de sélection, les valeurs et les cibles envoyées, la conservation des saisies, la remise, le choix d'un fichier, la pagination de 401 dépenses et la copie des coordonnées.
- Le mode natif `a-propos` mesure le centrage et les limites de chaque cadre et de chaque action. Il capture quatre formats de téléphone, dont un écran de 320 × 640 avec texte à 130 %, et deux formats d'ordinateur, en clair et en sombre.
- Ces deux modes sont obligatoires dans le workflow de publication native et leurs rapports et captures sont conservés comme artefacts de contrôle.

Les contrôles utilisent des établissements de démonstration et des bases temporaires. Aucun message WhatsApp ou e-mail n'est envoyé par les tests. Aucune migration de données n'est nécessaire.

## Résultats locaux

- 580 tests métier et API : réussis, dont les trois nouveaux scénarios de caisse.
- 766 tests du client Avalonia : réussis après les corrections.
- 56 tests spécifiques au mobile : réussis.
- 26 scénarios natifs avec réponses retardées et presse-papiers contrôlé : réussis, y compris l'absence de renvoi des valeurs déjà enregistrées.
- 12 variantes de la fenêtre « À propos » : cadres et actions centrés, sans débordement horizontal sur téléphone.
- 32 variantes des quatre métiers sur téléphone et ordinateur : réussies, sans débordement.
- 196 vues natives, soit 49 pages en clair et sombre sur téléphone et ordinateur : rendues sans échec ni débordement.

Les binaires de distribution sont ensuite reconstruits par le workflow natif. L'APK publié doit conserver le certificat du client existant et un versionCode supérieur à 3633022.
