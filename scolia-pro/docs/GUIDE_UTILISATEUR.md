# Guide de l'utilisateur — SCOLIA Pro

## 0. L'interface en un coup d'œil

- **Barre de titre** : accès rapide (actualiser, nouvel encaissement, commandes) et recherche globale.
- **Fichier** (bouton bleu à gauche) : informations sur l'établissement, sauvegarde immédiate, contrôle de la base et du journal, compte.
- **Onglets et ruban** : chaque onglet regroupe des commandes par thème. Cliquer sur un onglet affiche ses commandes ; une info-bulle décrit chaque commande.
- **Onglets contextuels** : sur une fiche d'apprenant ou un reçu, un onglet coloré apparaît avec les commandes de cette page.
- **Barre d'état** : page affichée, année, date de la dernière sauvegarde et zoom de l'interface.

Touches d'accès : appuyer sur Alt seul affiche une lettre sur chaque onglet. Taper la lettre ouvre l'onglet, puis taper la lettre d'une commande la lance. Échap annule.
Réduire le ruban : Ctrl+F1, ou la flèche à droite des onglets. Préférences (thème, densité, barre d'état) : groupe Apparence de l'onglet Affichage.

## Préférences d'affichage

Onglet Affichage, groupe Apparence, bouton de lanceur (la petite flèche du groupe) : ouvre les préférences.

- **Thème** : clair, sombre ou selon le système. Le bouton lune ou soleil de la barre d'état bascule aussitôt du clair au sombre.
- **Couleur d'accent** : bleu, indigo, violet, vert, orange ou graphite, pour les boutons principaux et les sélections.
- **Taille du texte** : petit, normal, grand ou très grand. Elle s'applique à tous les écrans et au ruban.
- **Densité** : confortable ou compacte.
- **Barre d'état**, **info-bulles**, **quadrillage des tableaux**, **animations** : chacun s'active ou se désactive séparément.
- **Rétablir les valeurs par défaut** : remet tous ces réglages à leur valeur d'origine.

Les réglages sont mémorisés sur ce poste. Ils ne sont pas partagés avec les autres utilisateurs.

### Connexion, animations et traitements

- La connexion s'ouvre dans une petite fenêtre à la taille du formulaire, avec le logo de l'établissement. Une fois connecté, la fenêtre s'agrandit et l'application apparaît en fondu.
- Les boîtes de dialogue, les pages et le changement de thème s'animent brièvement. La préférence « Animations » les désactive.
- Un traitement qui dure plus d'un instant, comme un export ou une sauvegarde, affiche « Traitement en cours… » au centre de la fenêtre.

### Logo de l'établissement

Paramètres, Établissement, « Changer le logo ». Le logo figure sur l'écran de connexion, en tête du volet, sur les reçus, les listes PDF et Excel, et les cartes d'accès. Sans logo enregistré, le sceau de l'établissement le remplace.

### Clic droit et double-clic

- **Clic droit sur une ligne** d'un tableau : la ligne est choisie et ses actions s'affichent, par exemple Encaisser, Ouvrir la fiche, Relancer par WhatsApp. Le menu propose aussi de copier la cellule ou la ligne, puis les actions de la page.
- **Clic droit ailleurs sur la page** : les actions générales de la page, comme les exports Excel et PDF ou Actualiser.
- **Double-clic sur une ligne** : son action principale, en général l'ouverture du détail.
- **Au clavier** : Entrée lance l'action principale de la ligne choisie ; la touche Menu ou Maj+F10 ouvre son menu.
- **Aperçu rapide** : au clic droit sur un apprenant, une petite fenêtre résume son dossier, son parent et ce qui reste à payer, sans quitter la liste.

### Taille de l'écran

L'interface suit la taille de la fenêtre. Texte, champs, icônes et marges grandissent ou rapetissent ensemble : un petit écran affiche la même page qu'un grand, en plus petit.

- Quand la place manque dans l'en-tête, les boutons en trop passent dans le menu « … ». Le titre de la page reste lisible.
- Dans une fenêtre étroite, le volet de gauche se replie seul. Le bouton du volet, en haut à gauche, l'ouvre ; il se referme dès qu'une page est choisie.
- Les tableaux masquent leurs colonnes secondaires quand elles ne tiennent plus.
- La taille du texte choisie dans les préférences s'ajoute à cette adaptation.

Les commandes du ruban ouvrent directement la page ou la boîte de saisie correspondante : nouvelle dépense, arrêt de caisse, nouveau cours, nouvelle évaluation, incident, promotion, année scolaire, utilisateur, membre du personnel, import du classeur V79. Les exports (Excel, PDF, CSV) sont dans les groupes Finances et Administration.

## 1. Premier lancement

1. Ouvrir `SCOLIA.exe`. La fenêtre de SCOLIA s'ouvre et démarre seule le serveur.
2. L'assistant demande :
   - le nom, la ville, le téléphone et le logo de l'établissement ;
   - l'année scolaire, le mois de début et le nombre de mois de scolarité ;
   - le compte administrateur et son mot de passe.
3. Option : charger les données de démonstration (128 apprenants fictifs) pour découvrir
   le logiciel. Elles se suppriment ensuite depuis Administration.
4. Une fois l'installation terminée, créer les promotions et leurs frais dans Paramètres.

## 2. Se connecter

Chaque personne a son compte. Le mot de passe initial peut être imposé au premier
usage. Après cinq erreurs, le compte est bloqué quinze minutes.

Raccourcis : `Ctrl+K` ouvre la recherche globale et toutes les commandes.
`Ctrl+D` tableau de bord, `F2` encaisser, `Ctrl+K` recherche, `F5` actualiser, `Ctrl+P` imprimer. Chaque commande du ruban a son raccourci : posez le curseur sur une commande pour voir sa description et son raccourci. La liste complète est dans l'onglet Aide, touche `F1`.

## 3. Inscrire un apprenant

1. Onglet Secrétariat, groupe Apprenants : Nouvelle inscription.
2. Saisir le nom, les prénoms, la date et le lieu de naissance, l'adresse et le contact du parent.
3. Le matricule se génère seul : code de la promotion sur 4 caractères, puis le rang.
4. Si un homonyme existe, SCOLIA le signale et demande une confirmation avant de créer le doublon.

Champs du formulaire : nom, post-nom, prénom, sexe, date et lieu de naissance, parent ou tuteur, adresse, contact parent, promotion et date d'inscription.
Obligatoires, comme dans SCOLIA V79 : nom, post-nom, prénom, sexe, date et lieu de naissance, contact parent et promotion.
Un seul numéro est saisi, celui du parent. Les relances WhatsApp et SMS partent vers ce numéro.

## 4. Encaisser

1. Encaisser, puis rechercher l'apprenant par nom ou matricule.
2. Saisir le montant reçu et le mode de paiement (espèces, mobile money, banque, chèque, autre).
3. SCOLIA répartit le versement sur les échéances les plus anciennes, sans jamais dépasser le reste dû.
4. Imprimer le reçu ou l'envoyer. Chaque reçu porte un numéro sans trou et un QR de vérification.

Un reçu erroné ne se supprime pas : ouvrir le journal des reçus, puis l'annuler en indiquant le motif.

## 5. Recouvrement et rappels

- Recouvrement classe les retards par ancienneté : 0-30, 31-60, 61-90 jours, plus de 90 jours.
- Pour chaque parent en retard, un bouton ouvre un message prêt à envoyer par WhatsApp ou SMS.
  SCOLIA ne remplace pas votre téléphone : le message part de votre compte.

## 6. Prises en charge

Une bourse, un sponsor ou une exonération se crée depuis la fiche de l'apprenant.
Elle couvre un pourcentage de chaque frais. Le net ne peut jamais descendre sous
ce qui est déjà payé.

## 7. Caisse et arrêté

1. Onglet Finances, groupe Caisse, puis Caisse. L'arrêté se fait sur cette page.
2. Compter les espèces présentes et saisir le montant.
3. Après l'arrêté, les opérations de la période ne peuvent plus être modifiées.
   Une erreur se corrige dans la période suivante, avec une écriture explicite.

## 8. Dépenses

Onglet Finances, groupe Caisse, Dépenses : catégorie, bénéficiaire, montant et mode de paiement.
Chaque dépense reçoit un numéro DP-AAAA-… Le paiement de salaires crée automatiquement une dépense.

## 9. Scolarité

- Cours et notes : créer les cours par promotion, les évaluations, puis saisir les notes.
  Les moyennes sont pondérées, et le classement et le bulletin PDF se calculent seuls.
- Présences : appel quotidien par promotion. Le taux d'assiduité est donné sur 30 jours.
- Discipline : signaler un incident, avec sa sanction et son suivi.

## 10. Personnel et paie

Chaque membre du personnel a une fiche avec son salaire de base. Le bulletin mensuel
calcule le net : salaire, primes, retenues et avances. Le paiement crée la dépense correspondante.

## 11. Données et sauvegardes

- Sauvegarde automatique chaque jour. Une sauvegarde immédiate se lance depuis Données et sauvegardes.
- Pour protéger vos données, copiez régulièrement le dossier `donnees\sauvegardes` sur une clé USB ou un disque externe.
- Restaurer une sauvegarde remplace les données actuelles. SCOLIA crée d'abord une sauvegarde de l'état actuel.
- Le contrôle d'intégrité de la base est disponible dans le même écran.

## 12. Journal d'audit

Le journal liste qui a fait quoi et quand. La vérification de la chaîne confirme
qu'aucune entrée n'a été modifiée en dehors de SCOLIA.

## 13. Plusieurs postes

Voir le paquet, fichier `LIRE_MOI.txt`, section « Plusieurs postes en réseau local ».

## 14. Reprendre les données de SCOLIA V79 (Excel)

1. Données, Sauvegardes, bouton « Importer SCOLIA V79 ».
2. Choisir le classeur `.xlsm` ou `.xlsx`. SCOLIA affiche d'abord un essai à blanc : rien n'est écrit.
3. Vérifier le rapport, puis appliquer l'import.
4. Relancer l'import ne crée aucun doublon.

Le classeur Excel reste intact. Il peut être archivé.

### Données tenues dans un autre fichier Excel

1. Données, Sauvegardes, bouton « Modèle d'import ». SCOLIA enregistre un classeur vide.
2. Le classeur compte trois feuilles : PROMOTIONS, APPRENANTS et PAIEMENTS. La feuille NOTICE explique chaque colonne.
3. Recopier les données à partir de la ligne 5. La ligne 3 montre un exemple et n'est pas importée.
4. Importer ce classeur avec « Importer SCOLIA V79 », comme ci-dessus.

Données prises en charge :

| Feuille | Contenu | Obligatoire |
|---|---|---|
| PROMOTIONS | code sur 4 caractères, désignation, section, niveau, mensualité, activation | code et mensualité |
| APPRENANTS | matricule, nom, post-nom, prénom, sexe, naissance, lieu, promotion, parent, deux contacts, date d'inscription, statut, adresse | matricule, promotion, naissance, lieu et contact parent 1 |
| PAIEMENTS | date, ancien numéro de reçu, matricule, mois payé, montant | matricule, mois et montant |

Formats acceptés : `.xlsx` et `.xlsm`. Dates en jj/mm/aaaa ou en date Excel. Montants avec ou sans « FC ». Mois en toutes lettres, avec ou sans l'année.

Non repris par l'import : notes, présences, discipline, personnel et dépenses. Ils se saisissent dans SCOLIA.

### Sortir les données de SCOLIA

- Données, Rapports et exports : chaque liste en PDF ou en Excel.
- Données, Sauvegardes : la base complète en un seul fichier, à copier sur une clé USB.
- Journal d'audit et registre de discipline : en CSV, lisible dans Excel.

## 15. Problèmes courants

| Symptôme | Action |
|---|---|
| La page affiche « Connexion expirée » | Se reconnecter. La session dure 10 heures sans activité. |
| Un poste ne trouve pas SCOLIA | Vérifier que le serveur est allumé, que le réseau est « privé », puis activer l'accès réseau local dans l'onglet Fichier, rubrique « Poste et réseau ». |
| « Compte verrouillé » | Attendre quinze minutes, ou demander à l'administrateur de déverrouiller le compte. |
| La fenêtre ne s'ouvre pas | Relancer `SCOLIA.exe`. Si le service est installé, le serveur tourne déjà et la fenêtre s'y connecte. |
