# Tests sur poste Windows

Ce document décrit comment valider le paquet SCOLIA Pro sur un poste Windows réel.
Il complète les tests automatiques (xUnit, Vitest, Playwright, parcours smoke), qui tournent sur Linux.

## 1. Préparer le poste

| Élément | Exigence |
|---|---|
| Système | Windows 10 (22H2) ou Windows 11, en 64 bits |
| Compte | Un compte **utilisateur standard** (sans droits administrateur) pour les tests principaux |
| Dossier | `C:\SCOLIA` (pas `Program Files`) |
| Données | Une installation **de test** avec les données de démonstration fictives |
| Logiciels | Aucun : ni Excel, ni .NET, ni navigateur pour le client natif |

1. Décompresser `SCOLIA_Pro_<version>_Windows_x64.zip` dans `C:\SCOLIA`.
2. Vérifier l'empreinte : `Get-FileHash .\SCOLIA_Pro_<version>_Windows_x64.zip -Algorithm SHA256`, à comparer au fichier `.sha256` livré.
3. Copier `scripts\verification-windows\VERIFIER_SCOLIA.ps1` et `VERIFIER_SCOLIA.cmd` dans `C:\SCOLIA`, à côté de `SCOLIA.exe`.

## 2. Vérification automatique

Double-cliquer sur `VERIFIER_SCOLIA.cmd`, ou depuis PowerShell :

```powershell
cd C:\SCOLIA
powershell -ExecutionPolicy Bypass -File .\VERIFIER_SCOLIA.ps1 -Client
```

| Option | Effet |
|---|---|
| `-Client` | Lance aussi `SCOLIA-Bureau.exe`, vérifie que sa fenêtre s'ouvre, puis la ferme |
| `-Ecriture` | Crée une sauvegarde de test. **Installation de test uniquement** |
| `-Utilisateur` | Compte de test (`admin` par défaut) |
| `-Port` | Port du serveur, si différent de `appsettings.json` |

Le script démarre le serveur s'il n'est pas déjà actif, contrôle les fichiers du paquet, la santé du serveur,
la connexion, les lectures principales de l'API, la monnaie (FC), l'intégrité de la base, les journaux et,
avec `-Client`, la fenêtre native. Il écrit un rapport `verification-<date>.txt` dans le dossier du paquet.
Un code de sortie `0` signifie qu'aucun contrôle n'est en échec.

Le script ne voit pas l'affichage : les points de la section 3 restent à vérifier à la main.

## 3. Vérification manuelle

Noter le résultat : `OK`, `ECHEC`, ou `N/A` avec la raison. Consigner dans la dernière colonne ce qui a été observé.

### Démarrage et affichage

| # | Contrôle | Attendu | Résultat |
|---|---|---|---|
| 1 | Premier lancement du client | Fenêtre de connexion en moins de 10 secondes | |
| 2 | Connexion avec le compte de test | Tableau de bord affiché, ruban visible | |
| 3 | Monnaie | Tous les montants se terminent par **FC** (tuiles, tableaux, barre d'état) | |
| 4 | Mise à l'échelle Windows 125 % | Textes lisibles, aucune coupure d'en-tête ni de bouton | |
| 5 | Mise à l'échelle Windows 150 % | Idem, la fenêtre reste utilisable à 1280 × 800 | |
| 6 | Écran réduit (1366 × 768) | Ruban et tableaux défilent, rien n'est masqué sans accès | |
| 7 | Thème sombre | Bascule immédiate ; le choix est conservé après redémarrage | |
| 8 | Taille du texte « Très grand » | Tous les écrans restent lisibles, sans chevauchement | |
| 9 | Dates | Calendriers en français, format jj/mm/aaaa | |

### Ruban et clavier

| # | Contrôle | Attendu | Résultat |
|---|---|---|---|
| 10 | Chaque onglet du ruban | Accueil, Secrétariat, Finances, Scolarité, Administration, Affichage, Aide : commandes présentes, sans erreur | |
| 11 | Onglet Fichier | Menu avec Informations, Mot de passe, Préférences, Aide, Déconnexion | |
| 12 | Réduire le ruban | Ctrl+F1 et la flèche masquent et réaffichent les commandes | |
| 13 | Raccourcis | F5 actualise, Ctrl+K ouvre la recherche, F2 ouvre l'encaissement, Échap ferme une boîte | |
| 14 | Saisie | Accents (é, è, ç), apostrophes des noms et clavier AZERTY s'écrivent correctement | |

### Parcours métier

| # | Contrôle | Attendu | Résultat |
|---|---|---|---|
| 15 | Encaissement | Apprenant trouvé, répartition affichée, reçu numéroté créé | |
| 16 | Reçu PDF | Le PDF s'ouvre dans l'application par défaut et se trouve dans Téléchargements | |
| 17 | Impression (Ctrl+P) | Le reçu s'ouvre pour impression depuis la page du reçu | |
| 18 | Annulation de reçu | Motif obligatoire ; le reçu reste dans le journal avec le statut Annulé | |
| 19 | Inscription | Matricule attribué ; doublon signalé et confirmable | |
| 20 | Notes | Saisie, enregistrement, puis relecture après actualisation | |
| 21 | Appel | Statuts enregistrés pour la date choisie | |
| 22 | Relevé de compte | PDF généré, montants en FC | |
| 23 | Export Excel | Classeur ouvert dans Excel ou l'application par défaut, données correctes | |
| 24 | Import V79 | Essai à blanc affiché avant toute écriture, puis import confirmé | |

### Zone de notification et serveur

| # | Contrôle | Attendu | Résultat |
|---|---|---|---|
| 25 | Icône du serveur | Icône visible dans la zone de notification de `SCOLIA.exe` (lancé seul) | |
| 26 | Menu de l'icône | Ouvrir, adresse réseau, dossier des données, Quitter fonctionnent | |
| 27 | Client sans serveur | Ouvrir `SCOLIA-Bureau.exe` seul démarre le serveur automatiquement | |
| 28 | Fermeture du client | Le serveur lancé par le client s'arrête avec lui | |
| 29 | Dossier des données | `donnees\scolia.db`, `donnees\journaux` et `donnees\sauvegardes` créés | |
| 30 | Sauvegarde | Sauvegarde immédiate visible dans la liste (installation de test) | |

### Service Windows et réseau local

| # | Contrôle | Attendu | Résultat |
|---|---|---|---|
| 31 | `INSTALLER_SERVICE.cmd` | Demande d'élévation, service « SCOLIA Pro » créé et démarré | |
| 32 | Redémarrage du poste | Le service démarre seul, sans session ouverte ; http://localhost:5080 répond | |
| 33 | `ACTIVER_RESEAU_LOCAL.cmd` | Règle de pare-feu créée, adresse réseau affichée | |
| 34 | Autre poste du réseau local | http://<adresse>:5080 ouvre l'interface web, connexion réussie | |
| 35 | `DESINSTALLER_SERVICE.cmd` | Service supprimé, le serveur ne démarre plus avec Windows | |

### Sécurité et mise à jour

| # | Contrôle | Attendu | Résultat |
|---|---|---|---|
| 36 | Première exécution | SmartScreen peut avertir (exécutable non signé) : noter le message exact | |
| 37 | Verrouillage | Cinq mots de passe faux bloquent le compte 15 minutes | |
| 38 | Mise à jour | Remplacer les fichiers sauf `donnees` : les données restent accessibles | |

## 4. Résultats

| Poste | Windows | Version du paquet | Date | Testeur | Échecs | Observations |
|---|---|---|---|---|---|---|
| | | | | | | |

Tout échec bloquant (1, 2, 3, 15, 16, 31, 32) empêche la livraison. Les autres se corrigent avant le prochain paquet.
