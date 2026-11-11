# Analyse des fonctionnalités de SCOLIA Pro

Analyse écran par écran du client natif, relue sur les captures de chaque écran à 1280 × 820,
et sur le code du serveur. Chaque point indique l'état, puis une proposition.

## 1. État par écran

| Écran | État | Constat | Proposition |
|---|---|---|---|
| Connexion | Corrigé | Affiche désormais le nom de l'établissement. Entrée valide le formulaire. | Mémoriser l'identifiant du poste (case à cocher). |
| Tableau de bord | Bon | Tuiles, graphiques et recouvrement lisibles. | Choix de période (mois, trimestre, année). |
| Apprenants | Dense | Neuf colonnes : défilement horizontal à 1280 px. | Afficher ou masquer les colonnes ; masquer le téléphone sur petit écran. |
| Fiche apprenant | Fonctionnel | Colonne « Sortie » coupée dans les inscriptions. | Élargir la carte ou replier les colonnes secondaires. |
| Encaisser | Corrigé | Paiement par mois complet, montant calculé, scolarité seule. | Décider le mode de paiement de l'inscription et de l'examen. |
| Journal des reçus | Dense | Neuf colonnes, promotion et caissier tronqués. | Colonne « Caissier » masquable ; filtres repliables. |
| Reçu | Fonctionnel | « Versé par » affiche le tuteur quand le payeur n'a pas été saisi. | Indiquer « (tuteur) » quand le nom est repris du dossier. |
| Recouvrement | Fonctionnel | Noms tronqués à 1280 px. | Colonne « Nom » élargie, autres colonnes en largeur fixe. |
| Caisse | Bon | Mouvements et situation cohérents. | Export de l'arrêté de caisse en PDF. |
| Dépenses | Dense | Numéro de dépense coupé en fin. | Numéro sur une seule ligne, colonnes secondaires masquables. |
| Prises en charge | Bon | Effet et couverture lisibles. | Filtre par bourse. |
| Réinscriptions | Bon | État vide explicite. | Rappeler l'année précédente requise. |
| Cours et notes | Bon | Volet vide tant qu'aucun cours n'est choisi. | Texte d'aide dans le volet vide. |
| Résultats | Dense | Moyennes par cours tronquées à droite. | Moyennes en colonne repliable. |
| Présences | Bon | Statuts en liste déroulante. | Saisie au clavier (flèches, Entrée). |
| Discipline | Bon | Nom d'apprenant tronqué. | Élargir la colonne « Apprenant ». |
| Personnel et paie | Bon | Paie vide avant le premier bulletin. | Bouton « Générer les bulletins du mois ». |
| Paramètres | Corrigé | Barre de défilement hors des champs. | Colonne « Périodes » plus large. |
| Utilisateurs | Bon | Rôles et dernière connexion. | Désactivation visible d'un compte. |
| Données | Bon | Sauvegardes et import V79 clairs. | Test de restauration guidé. |
| Journal d'audit | Bon | Filtres et vérification de la chaîne. | Export filtré. |
| Recherche (Ctrl+K) | Bon | Résultats typés. | Recherche par matricule seul. |
| Fichier > Poste et réseau | Corrigé | Interrupteurs groupés façon Android. | Tester l'élévation UAC sur Windows. |
| Préférences | Corrigé | « Quadrillage » et « Info-bulles » agissent maintenant. | Voir le point « Animations » ci-dessous. |

## 2. Défauts constatés et leur état

- Quadrillage des tableaux : le réglage n'agissait pas (style figé). Corrigé.
- Info-bulles : le réglage n'agissait pas. Corrigé pour le ruban.
- Animations : le réglage ne contrôle rien, car l'application n'a aucune animation. À retirer ou à implémenter.
- Montant libre à l'encaissement : supprimé, les paiements passent par les mois.
- Barre de défilement des formulaires recouvrant les champs : corrigé.
- Dates tronquées (« 24/10/202 ») dans plusieurs tableaux : corrigé.

## 3. Fonctionnalités : état métier

| Domaine | État | Écart avec la version précédente (V79) |
|---|---|---|
| Établissement et année | Complet | Années et périodes, mois de rentrée, durée. |
| Promotions et tarifs | Complet | Mensualité par promotion (55 000 FC pour les données importées). |
| Apprenants et inscriptions | Complet | Matricules conservés à l'import. |
| Encaissement | Par mois | Pas de paiement partiel ; inscription et examen non encaissables. |
| Reçus et annulation | Complet | Reçu lu en direct : une correction de dossier se répercute. |
| Caisse et dépenses | Complet | Pas de comparaison avec la clôture de la V79. |
| Recouvrement et rappels | Complet | Rappels WhatsApp et SMS. |
| Scolarité, notes, présences, discipline | Complet | Pas d'écart signalé. |
| Personnel et paie | Complet | Bulletins mensuels. |
| Comptes et rôles | Partiel | Pas de droits par compte (correction, frais) ; pas de rôle « mixte ». |
| Réseau entre postes | Non reproduit | La V79 échangeait les données par dossier partagé (envoi, réception, brouillons). Le serveur central les remplace, sauf en travail hors ligne. |
| Import V79 | Complet | Classeur et échanges réseau importés ; relance sans doublon. |
| Installation | Complet | Assistant en deux étapes, comptes secrétariat et comptabilité. |
| Windows (service, pare-feu, UAC) | Non testé sur Windows | À valider avec `docs/TESTS_WINDOWS.md`. |

## 4. Propositions, par ordre de priorité

1. **Frais d'inscription et d'examen** : décider comment ils se paient. Aujourd'hui ils ne sont pas encaissables.
2. **Échéance** : confirmer le 6 du mois suivant (règle actuelle) ou le 6 du mois lui-même.
3. **Droits par compte** : reprendre les cases « corriger les reçus » et « gérer les frais », et le rôle « mixte », comme dans la V79.
4. **Mode réseau** : serveur central (recommandé) ou échanges par dossier partagé pour le travail hors ligne.
5. **Tableaux denses** : colonnes masquables et défilement maîtrisé à partir de 1280 px.
6. **Animations** : retirer le réglage ou ajouter des transitions discrètes.
7. **Taille du paquet** : 143 Mo aujourd'hui (exécutable autonome). Une variante dépendante du runtime serait plus légère, mais demanderait l'installation de .NET sur le poste.
8. **Accessibilité** : navigation complète au clavier, contraste vérifié sur les deux thèmes.
9. **Non-régression visuelle** : lancer les captures d'écran dans l'intégration continue et comparer.
