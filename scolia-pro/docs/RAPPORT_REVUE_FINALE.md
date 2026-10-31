# SCOLIA Pro : revue finale

Date de la revue : 25 octobre 2026. Version 1.1.0.

Ce rapport rend compte du dernier tour complet du logiciel : ce qui a été vérifié, les défauts trouvés et corrigés, les améliorations proposées et appliquées, et ce qui reste à faire.

## 1. Résultat

Le logiciel fonctionne de bout en bout sur une installation neuve. Un défaut grave a été trouvé et corrigé : le tableau de bord plantait les jours où un apprenant fête son anniversaire ou un parent est convoqué.

| Contrôle | Résultat |
|---|---|
| Tests du serveur | 149 réussis sur 149 |
| Tests de l'application Bureau | 55 réussis sur 55 |
| Parcours complet de l'application Bureau | 38 étapes réussies sur 38 |
| Parcours complet de l'interface web, dans un vrai navigateur | 15 scénarios réussis sur 15 |
| Exports PDF, Excel et CSV | 23 fichiers produits et relus |
| Écrans capturés en clair et en sombre | 46 et 46 |

## 2. Méthode

1. Installation neuve, sur une base vide, avec les données de démonstration.
2. Parcours automatique de l'application Bureau : connexion, apprenants, fiche, encaissement, reçus, recouvrement, prises en charge, caisse, dépenses, réinscriptions, cours, notes, résultats, appel, discipline, personnel, paie, paramètres, utilisateurs, sauvegardes, journal d'audit, création d'une dépense, d'une inscription, d'un cours et d'une évaluation, modification d'un apprenant.
3. Parcours automatique de l'interface web dans Chromium : installation guidée, recherche, inscription, encaissement, annulation de reçu, recouvrement, notes, ruban, préférences, verrouillage après cinq erreurs.
4. Capture de chaque écran en thème clair et en thème sombre, puis relecture écran par écran.
5. Production et relecture de chaque export.

## 3. Défauts trouvés et corrigés

### 3.1 Plantage du tableau de bord, grave

- **Symptôme :** l'application se fermait à l'ouverture du tableau de bord.
- **Quand :** les jours où un apprenant fête son anniversaire, ou un parent est convoqué.
- **Cause :** dans la liste « Anniversaires » et « Parents convoqués », le nom de la promotion s'affichait avec le format « · ACC1 ». Ce texte se liait dans les deux sens : le texte formaté revenait à la source, était formaté à nouveau, « · · ACC1 », et ainsi de suite jusqu'au débordement de mémoire.
- **Correction :** liaisons à sens unique pour ces textes et pour les barres de progression, qui présentent le même risque.
- **Prévention :** un test relit désormais toutes les vues et refuse ce type de liaison.

### 3.2 Réinscriptions la première année

- **Symptôme :** à l'ouverture, un message d'erreur « Aucune année précédente à partir de laquelle réinscrire » s'affichait.
- **Correction :** la page explique qu'une seule année existe et propose un bouton « Nouvelle année » pour créer l'année suivante.

### 3.3 Libellé ambigu sur le tableau de bord

- La case « Appel » affichait « 27 mentions ». Elle affiche maintenant « 27 apprenants pointés ».

### 3.4 Champs obligatoires dans l'interface web

- Les champs obligatoires n'étaient pas signalés. Ils portent maintenant un astérisque rouge, comme dans l'application Bureau.

### 3.5 Tests de parcours périmés

- Le parcours Bureau ne remplissait pas les champs devenus obligatoires à l'inscription, et utilisait le champ « Téléphone » retiré. Il est à jour.
- Le parcours web inscrivait un nom que les données de démonstration, tirées au hasard, produisaient parfois. L'homonyme bloquait le test. Il utilise maintenant un nom absent de ces données.
- Le parcours web cherchait le libellé exact « Mot de passe ». Avec l'astérisque des champs obligatoires, il accepte maintenant « Mot de passe * ».

## 4. Améliorations proposées et appliquées

| Amélioration | Où | Intérêt |
|---|---|---|
| Entrée ouvre la ligne choisie d'un tableau | Tous les tableaux | Travail au clavier, sans souris |
| Touche Menu ou Maj+F10 ouvre le menu de la ligne | Tous les tableaux | Même menu que le clic droit |
| « Relancer par WhatsApp » depuis la fiche apprenant | Fiche apprenant | Relancer un parent sans repasser par le recouvrement |
| Bouton « Nouvelle année » dans Réinscriptions | Réinscriptions | Créer l'année suivante là où on en a besoin |
| Filtre des captures par écran | Outil de captures | Isoler un écran pour diagnostiquer un problème |

## 5. Ce qui a été vérifié et fonctionne

- **Connexion :** petite fenêtre à la taille du formulaire, logo de l'établissement, agrandissement après connexion.
- **Apprenants :** inscription avec les champs obligatoires de la version 79, un seul contact parent, détection des homonymes, fiche, modification.
- **Encaissement :** choix des mois, reçu numéroté, impression, annulation motivée, vérification publique par QR.
- **Suivi mensuel :** chiffres justes sur les 10 mois de l'année, filtres par état et par promotion, relances WhatsApp et SMS.
- **Exports :** listes PDF et Excel avec logo, largeurs de colonnes selon le contenu, onglets Excel lisibles.
- **Cartes d'accès :** identité sur une ligne par champ, classe avec l'identité, bande de couleur sans texte, PDF et Word.
- **Interface :** adaptation à la taille de l'écran, clic droit, double-clic, aperçu rapide, thèmes clair et sombre, couleur d'accent, indicateur de traitement.
- **Données :** sauvegardes vérifiées, modèle d'import, import du classeur V79 sans doublon.

## 6. Propositions pour la suite

Ces points demandent une décision ou un travail plus long. Ils ne sont pas faits.

1. **Interface web au même niveau que Bureau.** Le style macOS, le suivi mensuel, le modèle d'import et les menus contextuels n'existent que dans l'application Bureau.
2. **Essai sur un vrai poste Windows.** Les captures viennent de Linux. Le centrage de la fenêtre, la mise à l'échelle de Windows et l'ouverture de WhatsApp sont à confirmer sur le poste de l'établissement.
3. **Relance groupée.** WhatsApp ne permet d'envoyer qu'un message à la fois sans abonnement payant. Une relance groupée demanderait l'API WhatsApp Business, payante et liée à internet.
4. **Matricule sur la carte d'accès.** Il n'est plus imprimé depuis le retrait du texte de la bande. Il reste dans le QR. Il peut être ajouté à l'identité si l'établissement le souhaite.
5. **Taille des exports.** Le logo par défaut pèse environ 220 Ko et alourdit chaque fichier Excel. Une version réduite du sceau ramènerait les fichiers à quelques dizaines de Ko.
6. **Phrase du message de relance.** Le message finit par « Ce message est automatique », alors que l'envoi se fait à la main depuis le téléphone de l'école. La phrase peut être retirée.

## 7. Limites de la revue

- Les animations ne se voient pas sur une capture. Elles ont été vérifiées par le code.
- Les tests de parcours ont laissé des données de test, comme « SMOKETEST » ou « CONTROLE », dans la base de revue. Elles n'existent pas dans le paquet livré, qui démarre sur une base vide.
