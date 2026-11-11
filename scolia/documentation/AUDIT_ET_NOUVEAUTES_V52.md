# SCOLIA V52 — audit, corrections et fonctionnalités ajoutées

La livraison corrige la continuité du parcours de démarrage et une incohérence dans les soldes après actualisation ciblée. Elle ajoute une recherche comptable, un bilan de session et des commandes pour copier/exporter les rapports.

## Portée réelle de la vérification

Le programme est une application Windows PowerShell 5.1 / .NET Framework / Windows Forms, avec Excel de bureau et des macros VBA. Le poste de travail utilisé pour préparer cette livraison est Linux, sans ces moteurs. Aucun parcours Windows/Excel n’a donc été exécuté ici et aucun résultat de test fonctionnel Windows n’est présenté comme acquis.

Contrôles exécutés ici : comparaison avec le ZIP V51, intégrité des fichiers conservés, lecture des JSON, existence des références locales sélectionnées, cohérence des clés de solde et équilibre lexical des fichiers modifiés. Ce dernier contrôle ne remplace pas le parseur natif PowerShell ou une compilation C#.

L’inventaire `INVENTAIRE_FONCTIONS_V52.csv` recense 433 définitions de fonctions PowerShell dans 41 modules de production. Il aide à retrouver chaque fonction et sa ligne ; il ne constitue pas une preuve de fonctionnement de chaque fonction. Les composants C# et les macros VBA restent couverts par leurs sources et les tests existants.

## Corrections appliquées

1. **Attente entre deux écrans.** Le moteur dispose d’une confirmation de premier rendu. Les appels de transition attendent cette confirmation, avec une limite de 1,2 seconde, avant de poursuivre la fermeture de l’écran courant. Le thread de l’animation reste indépendant des chargements PowerShell et des appels Excel. Aucun délai artificiel de plusieurs secondes n’est ajouté.
2. **Messages de chargement.** Une mise à jour effectuée avant la création du handle n’est plus perdue. Le thread visuel reprend le dernier message au premier affichage puis à chaque tick. Les messages tiennent sur plusieurs lignes ; le titre indique « Veuillez patienter ». Après 12 secondes, la carte indique le temps écoulé, sans inventer un pourcentage d’avancement.
3. **Lisibilité et placement.** La carte tient compte du DPI et de l’écran de la fenêtre active ; elle utilise l’écran du curseur en l’absence de fenêtre active. Les thèmes clair et sombre sont conservés.
4. **Un seul indicateur pendant le démarrage.** La reconnexion au classeur réutilise la carte de transition, et les messages de lecture/préparation y sont transmis. Les indicateurs sont fermés avant une boîte d’erreur pour ne pas la masquer. L’installateur ferme la carte à son affichage.
5. **Connexion.** Une carte animée accompagne aussi la vérification du compte, pendant laquelle le compte et le mot de passe sont désactivés. Un refus ferme l’indicateur et réactive les champs pour réessayer. Lors d’un déverrouillage ou changement de compte, l’attente est fermée dès le retour à l’accueil déjà affiché. Le dialogue de récupération réseau ferme aussi la carte pour rester lisible.
6. **Soldes après écriture.** Le chargement complet et `Paid` utilisaient `MATRICULE::MOIS`, tandis que l’actualisation ciblée utilisait `MATRICULE|MOIS`. Une fonction commune construit maintenant les clés. Les anciens mois du dossier actualisé sont supprimés puis remplacés ; les autres apprenants sont conservés. Cette correction affecte l’affichage des paiements, des restes annuels et des retards après une écriture.
7. **Navigation comptable.** La clé d’icône de Comptabilité utilise maintenant le même séparateur que les autres onglets.
8. **Sélection du journal.** Une identité technique masquée permet de conserver la sélection de la même opération après actualisation, sans confondre deux lignes du même apprenant. La première colonne visible reste la date.
9. **Validation de caisse.** Le contrôle du contenu de l’écriture intervient avant la création du marqueur local d’opération en attente. Les contrôles définitifs du journal partagé et des doublons restent exécutés lors de la publication.
10. **Restauration.** Une sauvegarde dont le registre appartient à un autre dossier de données est refusée avant copie, comme le contrôle d’intégrité des sauvegardes le faisait déjà.

## Fonctionnalités proposées et mises en pratique

### Recherche dans le journal comptable

Dans **Comptabilité**, saisissez une référence, un nom, une promotion, un libellé, un type d’opération ou un opérateur. La recherche ignore les accents et la casse. Plusieurs mots doivent tous être présents. Vider le champ retrouve le journal complet. Le filtrage attend 180 ms après la frappe pour éviter de reconstruire la grille à chaque touche.

Le bouton **Journal CSV** exporte les lignes correspondant à la recherche. Les recettes, dépenses et le solde affichés restent ceux de l’année complète. Le compteur « Journal affiché : X / Y » indique la taille du résultat filtré.

### État de mon espace

Dans **Diagnostic > État de mon espace**, un rapport indique les points à vérifier et la marche à suivre : compte connecté, données actualisées, année clôturée, écriture de caisse en attente, état de synchronisation, sauvegarde automatique du jour et observations de qualité des données.

La comptabilité n’exige pas un classeur scolaire pour encaisser. Le contrôle respecte cette distinction. Il ne modifie ni les dossiers ni les écritures. Le statut de sauvegarde lit les marqueurs existants ; il ne recalcule pas les empreintes et ne certifie pas qu’une restauration est possible.

### Rapports plus faciles à utiliser

Les fenêtres de détail disposent de **Copier le texte**, **Exporter en texte** et **Fermer**. L’export respecte l’autorisation Exporter du compte et produit un fichier UTF-8. Les rapports d’état ne contiennent pas de noms d’apprenants ou de mots de passe.

### Vérification sur Windows

`00_Demarrage/VERIFIER_V52.cmd` lance 17 contrôles dans des processus distincts et produit un résumé CSV ainsi qu’un journal par test. La sélection utilise des données temporaires et des objets simulés. Le test visuel affiche brièvement une carte d’attente. Ce lanceur ne démarre pas Excel et ne manipule pas les classeurs scolaires.

Il contrôle notamment le parseur natif de tous les scripts, la compilation des composants, les soldes ciblés, les nouvelles fonctions, les transactions simulées, les identités en double, les comptes, le journal de caisse, la réception des paiements, les prises en charge, la mémorisation de connexion, les retards et les caches de navigation. Il ne remplace pas les tests d’intégration Excel existants.

## Cartographie fonctionnelle et scénarios à valider

| Fonctionnalité | Fonctionnement dans le programme | Vérification requise sur Windows |
|---|---|---|
| Installation des corrections | Copie et sauvegarde de la source ; traitement Excel dans un processus suivi ; résultat publié à la fin | Source existante, création de source, arrêt et reprise, réussite suivie de la configuration |
| Configuration initiale | Établissement, calendrier, classes, tarifs et comptes enregistrés dans un dossier choisi | Création, dossier partagé existant, validation des champs et dossier non accessible |
| Connexion et rôles | Vérification du profil, du compte, des tentatives et des permissions métier | Bon/mauvais mot de passe, compte sans protection, blocage, changement de rôle |
| Connexion au classeur | Localisation mémorisée, vérification de la passerelle, lecture du snapshot, mise à jour de l’espace | Excel absent, classeur déplacé, lecture seule, réponse invalide, connexion après installation |
| Inscription et modification | Validation des champs et de l’identité, transaction de sauvegarde, retour de confirmation | Nouvelle inscription, modification, doublon, annulation, erreur après sauvegarde |
| Recherche et sélection des dossiers | Index normalisé, filtres différés et sélection conservée par matricule | Accents, filtres, dossier masqué, identité ambiguë et actualisation |
| Paiements scolaires | Contrôle du reste payable, historique, solde mensuel et annuel | Paiement partiel/complet, trop-perçu refusé, correction et annulation |
| Comptabilité | Recettes/dépenses avec référence, publication asynchrone et marqueur local de reprise | Doublon de référence, coupure réseau avant/après publication, vérification d’une opération en attente |
| Journal et reçus | Journal de l’année, distinction paiement/dépense, résolution de référence et impression | Recherche, sélection après mise à jour, export filtré, reçu ambigu refusé et impression |
| Réception interpostes | Lecture incrémentale du partage et import ciblé selon l’année et l’état de réception | Paiement importé une fois, autre année exclue, identité absente ou en double, import manuel d’un brouillon |
| Tableau de bord et retards | Vue recalculée par révision/date, mensualités exigibles et montants restant dus | Changement de date, mois courant, versement partiel, mois corrigé et filtres |
| Prises en charge | Couverture des mensualités, reste payable et fichiers dédiés sauvegardés | Couverture totale/partielle, modification, impayés et restauration des fichiers de couverture |
| Scolarité et cartes d’examen | Sélection des apprenants, règles d’accès et génération des cartes | Éligibilité, filtres et fichiers PDF/Word réels |
| Cotes et délibération | Lecture des grilles Excel, rapprochement des identités, contrôle des repères et calculs | Fichiers réels, identité ambiguë, lignes/colonnes invalides et résultats attendus |
| Exports et listes | Prévisualisation, filtres, génération Excel/PDF/CSV et droits d’export | Promotions et périodes, résultat vide, destination non accessible, fichier final lisible |
| Stockage segmenté | Registre de classeurs, capacités, verrou et récupération de transaction | Passage de capacité, transaction interrompue, reprise et classeur manquant |
| Sauvegardes et restauration | Copies des segments et fichiers de couverture, empreintes et restauration dans un nouveau dossier | Copie complète, fichier altéré, autre dossier refusé, restauration réussie sur une copie |
| Année scolaire et promotions | Frais et promotions administrés ; clôture avec sauvegarde et préparation de l’année suivante | Tarifs modifiés, année clôturée, nouvelle année et conservation des archives |
| Diagnostic et doublons | Observations issues de la passerelle ; fusion soumise aux identités et à la décision de l’utilisateur | Observation résolue, fusion refusée si incompatible, nouvel état de session |
| Sécurité et journal utilisateur | Permissions vérifiées avant commande, protection et journal nominatif | Verrouillage, inactivité, profil modifié sur un autre poste et journal indisponible |
| Apparence, aide et raccourcis | Préférences, thème, disposition adaptative, aide recherchable et raccourcis | Clair/sombre, DPI 100/150/200 %, petit écran, second écran et navigation clavier |

Cette cartographie recense les parcours ; la colonne de droite contient des tests restant à exécuter dans l’environnement cible. Un contrôle statique ou simulé ne prouve pas qu’un export, une impression ou une transaction Excel réelle réussira.

## Tests ajoutés

- `TestsSoldesEtRechercheV52.ps1` : solde mensuel et annuel après actualisation, suppression d’un ancien mois, annulation, conservation d’un autre apprenant, recherche accentuée et multicritère, états de session, restauration d’un autre dossier refusée.
- `TestsTransitionRuntimeV52.ps1` : confirmation du premier rendu, message envoyé avant le handle, fermeture immédiate et Dispose répété. Lecture de la carte sur son propre thread par un délégué C#, sans callback PowerShell sur ce thread.

Ces deux tests sont livrés mais n’ont pas été exécutés sur le poste Linux.

## Recette des transitions

1. Sur une copie de test, installer les corrections puis observer le passage à la configuration : une carte doit apparaître avant fermeture de l’installateur.
2. Enregistrer la configuration, vérifier l’ouverture de la connexion sans attente aveugle.
3. Essayer un mauvais mot de passe : l’attente se ferme, le message explique le refus et les champs redeviennent utilisables.
4. Se connecter : la carte accompagne la construction de l’accueil et la reconnexion des données, puis disparaît lorsque l’accueil est affiché et le traitement initial terminé.
5. Tester le déverrouillage et le changement de compte : aucune attente ne doit rester affichée après le retour à l’accueil.
6. Refaire avec un partage lent, un classeur mémorisé absent, un thème sombre et un second écran.
7. Vérifier qu’une erreur reste visible et qu’aucune carte d’attente ne masque le dialogue.

## Utilisation de la livraison

Extraire le nouveau ZIP dans un dossier distinct. Lancer `SCOLIA/00_Demarrage/APPLICATION.vbs` pour utiliser le programme, et `VERIFIER_V52.cmd` pour la vérification technique sur Windows. Les sources, archives, ressources, données et macros livrées dans V51 sont conservées à l’identique. Ne pas copier un dossier de configuration vide sur celui d’une installation utilisée : conserver les chemins et fichiers de configuration existants.
