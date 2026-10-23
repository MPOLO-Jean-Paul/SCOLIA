# SCOLIA V53 : inscription et présentation sobre

Le formulaire d'inscription est organisé en trois sections : identité, scolarité, contacts et résidence. Il reste dans une seule fenêtre. Les champs utilisent les éditeurs Windows existants, entourés de cadres sobres avec un contour de focus et un signal d'erreur. Les libellés restent au-dessus de leur champ.

Le pied de fenêtre garde les actions visibles. Le corps défile verticalement quand la hauteur disponible est insuffisante ; la largeur choisit une, deux ou trois colonnes. Sur une grande fenêtre, la scolarité et les contacts sont placés côte à côte pour réduire la hauteur nécessaire. La taille de lecture choisie reste stable pendant les redimensionnements. Ce comportement remplace le placement précédent qui désactivait le défilement et pouvait laisser des champs hors de la zone visible. Les erreurs de validation amènent le focus sur le champ concerné.

La confirmation vide est masquée. Une validation refusée affiche une instruction courte ; la confirmation de sauvegarde utilise une autre couleur. Une nouvelle saisie efface le message précédent. La saisie consécutive, le matricule automatique, les valeurs optionnelles, les règles d'année/promotion et les appels d'enregistrement existants sont conservés.

Les thèmes partagés passent à des gris neutres, des surfaces blanches ou anthracite et un accent bleu plus discret. Les boutons secondaires utilisent des icônes de la couleur de leur texte. Les pages qui utilisent ces composants bénéficient de cette harmonisation. Les clés du cache des icônes correspondent désormais à celles employées par la navigation, afin de conserver les bonnes images après un changement de thème.

## Vérifications et limites

Les contrôles locaux couvrent les guillemets et délimiteurs des scripts modifiés, les références locales, les JSON, les empreintes et l'intégrité des archives. Les classeurs, fichiers scolaires, archives, configuration et corrections VBA issus du ZIP fourni sont préservés. La maquette HTML sert à revoir la présentation dans un navigateur ; elle ne constitue pas une capture de l'application Windows. Le navigateur de l'environnement est bloqué par son bac à sable, donc aucun rendu local de cette maquette n'est annoncé comme vérifié.

La suite `00_Demarrage/VERIFIER_V53.cmd` inclut la compilation native, la disposition de l'inscription, les deux thèmes, les icônes, la confirmation, les contenus adaptables et les tests fonctionnels sélectionnés de la V52. Les tests de disposition vérifient les petites fenêtres, les bornes des contrôles, les actions fixes, la date et les contacts. Ils sont fournis pour exécution sous Windows et n'ont pas été exécutés dans l'environnement Linux de préparation. Les opérations Excel et le rendu réel des fenêtres doivent être vérifiés sur un poste Windows avec une copie de travail.

## Mettre à jour une installation existante

Extraire l'archive complète dans un autre dossier, puis lancer `00_Demarrage/METTRE_A_JOUR_V53.cmd` depuis cette nouvelle copie. Sélectionner le `APPLICATION.vbs` de l'installation actuelle. Le programme contrôle les empreintes, analyse les nouveaux scripts et compile les deux composants C# modifiés avant d'écrire dans l'installation. Les anciens fichiers sont sauvegardés et une erreur de remplacement déclenche leur restauration. La liste des remplacements contient uniquement le programme et sa documentation. Le lanceur sélectionné est ouvert après les contrôles, avec ses données et sa configuration existantes. Ce parcours de mise à jour doit également être vérifié sous Windows.

## Parcours à vérifier sur Windows

1. Ouvrir une nouvelle inscription, puis modifier un dossier existant : mêmes données et aucune valeur facultative perdue.
2. Réduire la fenêtre : aucun défilement horizontal ; tous les champs restent accessibles par défilement et Tabulation ; les actions restent visibles.
3. Changer le thème : champs, textes, listes et icônes lisibles ; aucun bitmap invalide lors du changement d'onglet.
4. Saisir une date ou un téléphone invalides : le champ est signalé et reçoit le focus. Corriger le champ : le signal disparaît.
5. Enregistrer un dossier : confirmation, matricule et préparation de l'inscription suivante. Fermer un dossier non enregistré : avertissement habituel conservé.
