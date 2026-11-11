# Configuration, présence et paie dans SCOLIA V57

## Installer sans perdre les données

Fermez SCOLIA et Excel. Extrayez le ZIP dans un nouveau dossier, puis lancez **00_Demarrage/METTRE_A_JOUR_V57.cmd**. Choisissez **APPLICATION.vbs de votre installation actuelle**, et non celui de la nouvelle copie. La mise à jour valide le contenu et la syntaxe avant remplacement, crée une sauvegarde du programme et conserve les données et paramètres. Pour une installation neuve, utilisez **APPLICATION.vbs** dans la nouvelle copie.

## Configuration initiale

Les sections regroupent l'établissement, les données et les accès. Les libellés et champs suivent une disposition mesurée selon l'espace disponible. Les actions de navigation restent accessibles ; un défilement du contenu permet de travailler sur un écran réduit ou avec une mise à l'échelle Windows élevée. Les contrôles des chemins, des classes et des mots de passe restent actifs.

## Pointage des enseignants

Dans **Enseignants**, ajoutez chaque enseignant et sa période d'activité. Cochez **Entrée** à l'arrivée et **Sortie** au départ. Une case enregistre immédiatement l'heure locale de l'ordinateur, son décalage horaire et le compte ouvert. La sortie nécessite une entrée le même jour. Les signatures sont conservées et ne sont pas modifiables.

Avant **08:00:00**, l'entrée est à l'heure. À **08:00:00** et après, elle est en retard. L'accès aux cases reste ouvert. Après 08:00, une entrée encore attendue est signalée ; seul un retard effectivement signé peut entrer dans la conversion hebdomadaire.

Deux signatures clôturent la journée comme présente ou présente en retard. Avec zéro ou une signature, la journée reste en attente pendant le jour courant ; sur un jour ouvrable passé, elle devient absente. Les jours futurs, non ouvrables ou hors période d'activité ne créent aucune absence automatique.

## Justifier une absence

Une absence justifiée exige un **motif**. Elle est comptée dans le bilan et reste **rémunérée** avec les règles V57. Le motif et son annulation éventuelle restent tracés. Pour lever une justification avant finalisation, donnez le motif de l'annulation.

## Convertir les retards

La semaine va du **lundi au dimanche**. Chaque groupe de trois retards signés sur des journées ouvrables clôturées donne une absence équivalente, affectée à la date du troisième retard. Six retards donnent deux absences équivalentes. Les retards restants ne sont pas reportés à la semaine suivante.

Le compteur traverse les limites mensuelles : des retards les 30 et 31 janvier puis le 1er février dans la même semaine produisent une absence équivalente en février. Le calendrier de janvier doit donc être confirmé pour calculer février. Une semaine incomplètement documentée bloque la finalisation du relevé.

Une absence physique justifiée n'alimente pas le compteur des retards. Pour justifier une absence équivalente, renseignez un motif **sur la date du troisième retard**. La conversion est alors comptée mais ne provoque aucune retenue. Un motif sur une autre date ne justifie pas automatiquement la conversion.

Le calcul ne déduit **qu'une journée par date**. Ainsi, trois journées en retard avec une seule signature chacune produisent trois absences physiques et une conversion hebdomadaire, mais seulement trois jours de retenue. Une journée incomplète suivie de deux journées complètes en retard produit deux jours déductibles, si aucun motif ne les justifie.

## Préparer la paie mensuelle en FC

Dans **Salaires**, choisissez le mois, du premier au dernier jour. Déclarez les jours réellement prestables, en retirant les congés et jours fériés. Le bouton **Lundi à vendredi** facilite la sélection, qui doit être confirmée. Renseignez ensuite le salaire mensuel en FC pour chaque enseignant.

`Taux journalier = salaire brut / nombre total de jours ouvrables déclarés`

`Net = salaire brut − taux journalier × nombre de dates déductibles`

Le net est arrondi une seule fois au FC, les demi-francs vers le montant supérieur. La retenue affichée vaut **brut − net**. Par exemple, 100 000 FC sur 22 jours avec deux jours déductibles donnent **90 909 FC net** et **9 091 FC retenus**. Le montant saisi reste la base mensuelle de l'établissement ; aucune proratisation supplémentaire n'est créée pour une période d'activité partielle.

Le relevé distingue les retards, les absences physiques, les conversions hebdomadaires et les retenues. Pendant le mois, les montants sont provisoires. Un calendrier vide ne permet pas de calculer le taux ni de finaliser.

Après la fin du mois, vérifiez les jours, motifs et montants, puis utilisez **Finaliser le mois**. L'instantané conserve les sources qui ont contribué au calcul, y compris les jours du début de semaine dans le mois précédent. Ces sources sont ensuite protégées contre une modification affectant une paie finalisée. Les anciennes paies finalisées V56 restent historiques et ne sont pas recalculées.

## Faire l'appel des apprenants à la fin de la journée

Dans **Présences et absences**, sélectionnez la promotion et le mois, puis confirmez les jours de cours. À la fin de la journée, cochez une fois la présence de chaque apprenant. **Aucune heure limite ne provoque de retard.** L'heure conservée dans le détail sert à l'audit de saisie.

Sur un jour de cours passé, une absence de signature devient une absence non justifiée lorsque la date d'inscription et l'appartenance à la promotion sont connues. Le jour courant reste à signer ; les dates futures et les périodes ambiguës ne produisent aucune absence déduite.

Les seuils d'absences et de jours de cours consécutifs déclenchent des alertes internes dans le bilan et dans **À traiter**. Les motifs d'absence sont conservés. Le changement de promotion garde son historique et une présence ne peut pas être dupliquée entre deux promotions pour la même date.

## Contrôles fournis

**VERIFIER_V57.cmd** lance les tests isolés sur Windows PowerShell 5.1, notamment la disposition de la configuration, les seuils horaires, les règles de paie, les semaines intermensuelles, les sources finalisées et l'appel de fin de journée. Les contrôles statiques et l'intégrité de cette archive sont vérifiés sous Linux ; aucun rendu Windows ou échange Excel n'a été exécuté dans cet environnement.
