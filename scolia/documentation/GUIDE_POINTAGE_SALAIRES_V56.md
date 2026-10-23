# Présences et salaires mensuels dans SCOLIA V56

## Déclarer les enseignants

Ouvrez **Enseignants**, puis **Gérer les enseignants**. Renseignez le nom et la date de début d'activité. Une date de fin permet de retirer un enseignant des jours suivants en conservant ses signatures antérieures. La gestion du personnel et des justifications exige le droit d'administration.

## Signer l'entrée et la sortie

Sur la ligne de l'enseignant, cochez **Entrée** à son arrivée et **Sortie** à son départ. Chaque action est enregistrée immédiatement avec l'heure locale de l'ordinateur, son décalage horaire et le compte de saisie. La sortie exige une entrée le même jour. Les heures ne sont pas modifiables et une case déjà enregistrée ne peut pas être décochée.

Les journées passées sont consultables. Une nouvelle signature se fait uniquement le jour même. Ces cases représentent un pointage dans SCOLIA sous le compte ouvert ; elles ne constituent pas une signature manuscrite ou biométrique.

## Déclarer les jours ouvrables du mois

Dans **Salaires**, choisissez le mois, du premier au dernier jour. Ouvrez **Calendrier**, cochez les jours réellement prestables et retirez les congés ou jours fériés. Le bouton **Lundi à vendredi** facilite la sélection mais la liste doit être confirmée. Le nombre de jours ouvrables est calculé depuis les dates sélectionnées, y compris pour février de 28 ou 29 jours.

La déclaration vaut pour les enseignants actifs sur ces jours. Une période antérieure à l'embauche ou postérieure à la fin d'activité ne crée pas d'absence. Les jours non ouvrables restent exclus des retenues.

## Comprendre les statuts

- Deux signatures : **Présent**.
- Entrée seule pendant le jour en cours : **Sortie attendue**.
- Jour ouvrable passé avec zéro ou une signature : **Absent non justifié**, sauf motif enregistré.
- Motif valide : **Absence justifiée**. Le motif et son annulation éventuelle restent dans l'historique.
- Jour futur : **À venir**, sans retenue anticipée.

Le jour se termine au changement de date locale. Vérifiez les deux signatures avant de quitter l'établissement. Une erreur de motif peut être corrigée ou annulée avec un motif d'annulation, avant finalisation de la paie.

## Renseigner le salaire et calculer la retenue

Renseignez le salaire brut mensuel en FC pour chaque enseignant, puis enregistrez-le. Le taux journalier est calculé sans arrondi intermédiaire :

`Taux journalier = salaire brut / nombre total de jours ouvrables déclarés`

`Net non arrondi = salaire brut − taux journalier × jours d'absence déductibles`

Le net est arrondi une seule fois au FC, les demi-francs vers le montant supérieur. La retenue affichée vaut **brut − net**, afin que les montants se réconcilient. Par exemple, 100 000 FC sur 22 jours ouvrables avec deux absences déductibles donnent un net de **90 909 FC** et une retenue de **9 091 FC**.

Les absences justifiées sont payées par défaut. La règle **Absence justifiée payée** est modifiable pour le mois. Si elle est désactivée, les jours justifiés entrent aussi dans les retenues. Le montant saisi reste la base mensuelle choisie par l'établissement ; le programme ne crée pas de proratisation supplémentaire pour une période d'activité partielle.

Un calendrier vide ne permet pas de diviser le salaire ni de finaliser un calcul. Renseignez les jours ouvrables et les salaires avant de préparer les relevés.

## Vérifier et finaliser

Pendant le mois, le montant affiché est provisoire : il tient compte des absences déjà constatées et suppose qu'aucune nouvelle absence ne surviendra. Les journées futures ou encore ouvertes ne sont pas déclarées absentes.

Après la fin du mois, vérifiez le détail quotidien, les motifs et les salaires, puis utilisez **Finaliser le mois**. Tous les enseignants actifs sur les jours ouvrables doivent disposer d'un salaire. SCOLIA conserve un instantané avec calendrier, heures, motifs, montants et révisions des sources. Le calendrier, les montants et les présences utilisés sont ensuite figés. Le relevé finalisé représente le montant dû ; le versement effectif relève des opérations de comptabilité de l'établissement.

Les exports CSV nécessitent les droits d'administration et d'export. L'année clôturée reste consultable.

## Présences mensuelles des apprenants

Dans **Présences et absences**, sélectionnez un mois et une promotion. Déclarez leurs jours de cours dans **Calendrier**. Une case **Présence du jour** enregistre immédiatement une seule présence par apprenant et par date, même si cet apprenant change de promotion. Le détail affiche chaque jour du premier au dernier du mois.

Pour les jours de cours passés et confirmés, l'absence de signature produit une absence non justifiée. Le jour en cours reste **À signer**. Les dates futures et celles qui précèdent l'inscription sont exclues. Si la date d'inscription ou le changement de promotion n'est pas connu, le programme signale l'information à vérifier et suspend les absences déduites pour la période ambiguë.

Une justification exige un motif. Le bilan distingue les absences non justifiées, les justifiées et leur total, avec les dates concernées. Il compte chaque apprenant une seule fois par jour, y compris lors du rapprochement des anciens appels.

## Suivi et alertes des apprenants

La commande de seuils permet de régler le nombre d'absences non justifiées et le nombre de jours de cours consécutifs déclenchant une alerte. La valeur initiale est trois pour chacun. Les jours non ouvrables ne coupent pas une série de jours de cours ; une présence, un retard ou une justification la terminent.

Les alertes sont affichées dans le bilan et dans **À traiter**, avec un accès au dossier du mois concerné. Le centre couvre le mois courant et le précédent ; les autres mois se consultent dans le bilan. Les alertes restent internes à SCOLIA.

Après un transfert, le suivi conserve la promotion précédente avant sa date effective. Les modifications de dossier réalisées dans SCOLIA alimentent cet historique. **Début du suivi** permet de confirmer une date si les données plus anciennes sont incomplètes. Vérifiez cette date avant d'utiliser des absences automatiques.

## Conservation et vérification

Les sauvegardes du dossier data incluent le personnel, les salaires, les calendriers, les signatures et le suivi. La restauration crée un nouveau dossier, contrôle les empreintes et conserve les fichiers originaux. Les fonctionnalités et données V55 sont conservées.

La livraison inclut des tests isolés pour les règles métier, les signatures, la concurrence, les retenues, les alertes et les sauvegardes. Les contrôles statiques et l'archive ont été vérifiés sous Linux. L'exécution Windows PowerShell, le rendu WinForms et les échanges Excel restent à confirmer sur Windows avec **VERIFIER_V56.cmd**.
