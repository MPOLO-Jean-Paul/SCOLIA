# Tests sur poste Windows

Ce document décrit comment valider le paquet SCOLIA Pro sur un poste Windows réel.
Il complète les tests automatiques (xUnit, Vitest, Playwright, parcours smoke), qui tournent sur Linux.

Le paquet ne contient aucun script. Tout ce qui concerne le poste (service, réseau local) se fait dans SCOLIA lui-même,
depuis l'onglet Fichier, rubrique « Poste et réseau ». Le test n'a donc besoin d'aucun outil externe.

## 1. Préparer le poste

| Élément | Exigence |
|---|---|
| Système | Windows 10 (22H2) ou Windows 11, en 64 bits |
| Compte | Un compte **utilisateur standard** pour les tests principaux ; un compte administrateur pour la section 4 |
| Dossier | `C:\SCOLIA` (pas `Program Files`) |
| Données | Une installation **de test** avec les données de démonstration fictives |
| Logiciels | Aucun : ni PowerShell, ni Excel, ni .NET, ni navigateur pour le client natif |

1. Décompresser `SCOLIA_Pro_<version>_Windows_x64.zip` dans `C:\SCOLIA`.
2. Vérifier l'empreinte du zip avec l'outil de votre choix (par exemple l'explorateur de fichiers ou `certutil -hashfile`), à comparer au fichier `.sha256` livré.
3. Double-cliquer sur `SCOLIA.exe`. Le serveur `SCOLIA-Serveur.exe` est lancé automatiquement, à côté.

## 2. Vérification manuelle

Noter le résultat : `OK`, `ECHEC`, ou `N/A` avec la raison. Consigner dans la dernière colonne ce qui a été observé.

### Démarrage et affichage

| # | Contrôle | Attendu | Résultat |
|---|---|---|---|
| 1 | Premier lancement du client | Fenêtre de connexion en moins de 10 secondes | |
| 2 | Connexion avec le compte de test | Tableau de bord affiché, ruban visible | |
| 3 | Monnaie | Tous les montants se terminent par **FC** (tuiles, tableaux, barre d'état) | |
| 4 | Mise à l'échelle Windows 125 % | Textes lisibles, aucune coupure d'en-tête ni de bouton | |
| 5 | Mise à l'échelle Windows 150 % | Idem, la fenêtre reste utilisable à 1280 × 800 | |
| 6 | Redimensionnement | Passer de 1024 × 700 à 2560 × 1440 : le texte grandit ou se réduit avec la fenêtre, sans chevauchement | |
| 7 | Écran réduit (1366 × 768) | Ruban et tableaux défilent, rien n'est masqué sans accès | |
| 8 | Thème sombre | Bascule immédiate ; le choix est conservé après redémarrage | |
| 9 | Taille du texte « Très grand » | Tous les écrans restent lisibles, sans chevauchement | |
| 10 | Dates | Calendriers en français, format jj/mm/aaaa | |

### Ruban et clavier

| # | Contrôle | Attendu | Résultat |
|---|---|---|---|
| 11 | Chaque onglet du ruban | Accueil, Secrétariat, Finances, Scolarité, Administration, Affichage, Aide : commandes présentes, sans erreur | |
| 12 | Onglet Fichier | Menu avec Informations, Mot de passe, Poste et réseau, Préférences, Aide | |
| 13 | Réduire le ruban | Ctrl+F1 et la flèche masquent et réaffichent les commandes | |
| 14 | Raccourcis | F5 actualise, Ctrl+K ouvre la recherche, F2 ouvre l'encaissement, Échap ferme une boîte | |
| 15 | Saisie | Accents (é, è, ç), apostrophes des noms et clavier AZERTY s'écrivent correctement | |

### Parcours métier

| # | Contrôle | Attendu | Résultat |
|---|---|---|---|
| 16 | Encaissement | Apprenant trouvé, répartition affichée, reçu numéroté créé | |
| 17 | Reçu PDF | Le PDF s'ouvre dans l'application par défaut et se trouve dans Téléchargements | |
| 18 | Impression (Ctrl+P) | Le reçu s'ouvre pour impression depuis la page du reçu | |
| 19 | Annulation de reçu | Motif obligatoire ; le reçu reste dans le journal avec le statut Annulé | |
| 20 | Inscription | Matricule attribué ; doublon signalé et confirmable | |
| 21 | Notes | Saisie, enregistrement, puis relecture après actualisation | |
| 22 | Appel | Statuts enregistrés pour la date choisie | |
| 23 | Relevé de compte | PDF généré, montants en FC | |
| 24 | Export Excel | Classeur ouvert dans Excel ou l'application par défaut, données correctes | |
| 25 | Import V79 | Essai à blanc affiché avant toute écriture, puis import confirmé | |

### Serveur et dossier des données

| # | Contrôle | Attendu | Résultat |
|---|---|---|---|
| 26 | Client sans serveur | Ouvrir `SCOLIA.exe` seul démarre le serveur automatiquement | |
| 27 | Fermeture du client | Le serveur lancé par le client s'arrête avec lui | |
| 28 | Dossier des données | `donnees\scolia.db`, `donnees\journaux` et `donnees\sauvegardes` créés | |
| 29 | Sauvegarde | Sauvegarde immédiate visible dans la liste (installation de test) | |

### Poste et réseau (onglet Fichier)

Ces contrôles demandent un compte administrateur Windows pour l'élévation (UAC), et un compte standard pour l'état affiché.

| # | Contrôle | Attendu | Résultat |
|---|---|---|---|
| 30 | État initial | Démarrage automatique « Non installé », accès réseau « Désactivé », adresse locale affichée | |
| 31 | Installer le service (compte administrateur) | Invite UAC, puis message de succès ; le service « SCOLIA Pro » apparaît dans `services.msc` et est démarré | |
| 32 | Redémarrage du poste | Le service démarre seul, sans session ouverte ; http://localhost:5080 répond | |
| 33 | Activer l'accès réseau local | Invite UAC, règle « SCOLIA Pro (réseau local) » créée dans le pare-feu, adresses affichées | |
| 34 | Autre poste du réseau local | http://<adresse>:5080 ouvre l'interface web, connexion réussie | |
| 35 | Désactiver l'accès réseau local | Règle supprimée ; l'autre poste ne se connecte plus après redémarrage de SCOLIA | |
| 36 | Retirer le service | Invite UAC, service supprimé, les données restent dans `donnees` | |
| 37 | Annulation UAC | Refus de l'invite : message « Opération annulée », aucun changement | |

### Sécurité et mise à jour

| # | Contrôle | Attendu | Résultat |
|---|---|---|---|
| 38 | Première exécution | SmartScreen peut avertir (exécutable non signé) : noter le message exact | |
| 39 | Verrouillage | Cinq mots de passe faux bloquent le compte 15 minutes | |
| 40 | Mise à jour | Remplacer les fichiers sauf `donnees` : les données restent accessibles | |

## 3. Résultats

| Poste | Windows | Version du paquet | Date | Testeur | Échecs | Observations |
|---|---|---|---|---|---|---|
| | | | | | | |

Tout échec bloquant (1, 2, 3, 16, 17, 31, 32, 33) empêche la livraison. Les autres se corrigent avant le prochain paquet.
