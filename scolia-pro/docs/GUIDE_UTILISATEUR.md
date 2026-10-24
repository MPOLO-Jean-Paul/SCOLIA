# Guide de l'utilisateur — SCOLIA Pro

## 0. L'interface en un coup d'œil

- **Barre de titre** : accès rapide (actualiser, nouvel encaissement, commandes) et recherche globale.
- **Fichier** (bouton bleu à gauche) : informations sur l'établissement, sauvegarde immédiate, contrôle de la base et du journal, compte.
- **Onglets et ruban** : chaque onglet regroupe des commandes par thème. Cliquer sur un onglet affiche ses commandes ; une info-bulle décrit chaque commande.
- **Onglets contextuels** : sur une fiche d'apprenant ou un reçu, un onglet coloré apparaît avec les commandes de cette page.
- **Barre d'état** : page affichée, année, date de la dernière sauvegarde et zoom de l'interface.

Touches d'accès : appuyer sur Alt seul affiche une lettre sur chaque onglet. Taper la lettre ouvre l'onglet, puis taper la lettre d'une commande la lance. Échap annule.
Réduire le ruban : Ctrl+F1, ou la flèche à droite des onglets. Préférences (thème, densité, barre d'état) : groupe Apparence de l'onglet Affichage.

## 1. Premier lancement

1. Ouvrir `SCOLIA.exe`. Une fenêtre s'ouvre, et une icône apparaît près de l'horloge.
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
`Alt+1` tableau de bord, `Alt+2` apprenants, `Alt+3` encaisser, `Alt+4` recouvrement, `F2` encaisser, `F5` actualiser. La liste complète est dans l'onglet Aide.

## 3. Inscrire un apprenant

1. Onglet Secrétariat, groupe Apprenants : Nouvelle inscription.
2. Saisir le nom, les prénoms, la date de naissance et les contacts du parent.
3. Le matricule se génère seul : code de la promotion sur 4 caractères, puis le rang.
4. Si un homonyme existe, SCOLIA le signale et demande une confirmation avant de créer le doublon.

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

1. Onglet Administration, groupe Données : Données et sauvegardes, puis la section Importer depuis SCOLIA V79.
2. Choisir le classeur `.xlsm`. SCOLIA affiche d'abord un essai à blanc : rien n'est écrit.
3. Vérifier le rapport, puis appliquer l'import.
4. Relancer l'import ne crée aucun doublon.

Le classeur Excel reste intact. Il peut être archivé.

## 15. Problèmes courants

| Symptôme | Action |
|---|---|
| La page affiche « Connexion expirée » | Se reconnecter. La session dure 10 heures sans activité. |
| Un poste ne trouve pas SCOLIA | Vérifier que le serveur est allumé, que le réseau est « privé », puis lancer `ACTIVER_RESEAU_LOCAL.cmd`. |
| « Compte verrouillé » | Attendre quinze minutes, ou demander à l'administrateur de déverrouiller le compte. |
| L'icône a disparu | Relancer `SCOLIA.exe`. Une seule instance tourne : le second lancement rouvre la fenêtre. |
