# Audit complet de SCOLIA Pro · 1er novembre 2026

Ce document fait suite à la demande du 1er novembre : recherche façon Play Store, notifications des autres postes,
rapport journalier envoyé automatiquement, audit complet et catalogue du fonctionnement.

## 1. Résultat

| Contrôle | Résultat |
|---|---|
| Tests de l'application (Bureau) | 253 réussis, 0 échec |
| Tests du serveur | 223 réussis, 0 échec, dont 9 nouveaux pour le rapport journalier (e-mail compris) |
| Tests du téléphone serveur (PDF) | 11 réussis, 0 échec, dont 1 nouveau pour le rapport journalier |
| Commandes essayées une à une, téléphone | 135, 0 à revoir |
| Commandes essayées une à une, ordinateur | 173, 0 à revoir |
| Textes tronqués sur téléphone | 43 avant, 11 après |
| Textes coupés ou hors de l'écran | 0 |
| Compilation Android | réussie |

## 2. Défauts trouvés et corrigés

| N° | Défaut | Effet pour l'utilisateur | Correction |
|---|---|---|---|
| 1 | Les sauvegardes n'étaient jamais annoncées aux autres postes | Personne ne savait qu'une sauvegarde venait d'être faite | Chaque sauvegarde, manuelle ou automatique, est annoncée en direct |
| 2 | Recherche du téléphone en petite fenêtre | Peu lisible, différente des usages Android | Recherche plein écran façon Play Store : récentes, accès rapides, résultats en lignes |
| 3 | Sous-titres des pages coupés à une ligne sur téléphone | Le nombre de reçus, le total ou la période étaient cachés | Sous-titre sur deux lignes |
| 4 | Bouton « Vérifier les mensualités de la promotion » coupé | Texte illisible dans Encaisser | Le texte passe à la ligne |
| 5 | « Paie d'novembre » | Faute visible dans Personnel et paie | « Paie de novembre », « Paie d'avril » |
| 6 | Taille « Très grand » tronquée | Choix illisible dans Texte et polices | Le libellé passe à la ligne |
| 7 | Rapport du jour perdu si l'ordinateur principal était éteint à l'heure prévue | Pas de rapport ce jour-là | Rattrapage : le rapport de la veille part au démarrage suivant |
| 8 | Jeton WhatsApp lisible sur le disque | Risque si le dossier des données est copié | Jeton chiffré par la protection de clés du serveur, jamais renvoyé à l'écran |

## 3. Nouveautés livrées

### Recherche façon Play Store
- La loupe de l'accueil ouvre une page plein écran : champ en haut avec le clavier ouvert, bouton retour, croix pour effacer.
- Sans saisie : « Recherches récentes » (six au plus, avec « Effacer ») et « Accès rapides » en tuiles colorées.
- Avec saisie : une ligne par apprenant, reçu ou fonction, avec une pastille de type.

### Notifications et bannières
- Un encaissement, un lot, un reçu annulé ou une sauvegarde faits sur un autre poste s'annoncent par une bannière en haut de l'écran.
- La bannière indique qui l'a fait, sur quel poste, et ouvre le journal des reçus ou les sauvegardes d'un toucher.
- Quand SCOLIA est en arrière-plan, la même alerte arrive dans les notifications d'Android, ou d'iOS sur iPhone.
- Réglages dans Paramètres, Sons, rubrique Notifications : un interrupteur par sorte d'alerte et un bouton « Essayer ».

### Rapport journalier automatique
- Le PDF contient :
  - les chiffres clés en tuiles ;
  - les encaissements par promotion (effectif, reçus, payeurs, encaissé, reste échu, absents) ;
  - les modes de paiement ;
  - le détail des reçus ;
  - les dépenses ;
  - les nouvelles inscriptions.
- Dans Rapports et exports, la carte « Rapport journalier » propose :
  - « PDF » : le rapport du jour, ou du jour « Au » ;
  - « Planifier » : l'heure, les numéros et le compte WhatsApp ;
  - « Envoyer » : l'envoi tout de suite.
- À l'heure choisie, l'ordinateur principal prépare le PDF, l'enregistre dans le dossier « rapports », l'envoie, puis prévient tous les postes par la bannière « Rapport du jour prêt ».

### Info sur l'application
- Dans Aide, juste après « À propos ». Le catalogue compte 12 modules et 53 fonctions décrites, avec une recherche.

### Complément : envoi par e-mail et mise en veille

#### Rapport par e-mail
- Dans « Planifier », la partie « Destinataires » accepte des numéros WhatsApp et des adresses e-mail : l'un, l'autre ou les deux.
- La partie « Envoi par e-mail » demande le serveur de messagerie, le port, la connexion chiffrée, l'identifiant et le mot de passe, chiffré sur l'ordinateur principal. L'adresse d'envoi est facultative.
- Exemple avec Gmail :
  - serveur smtp.gmail.com, port 587, connexion chiffrée ;
  - un « mot de passe d'application » créé dans le compte Google.
- Le PDF part en pièce jointe. Si un canal échoue, l'autre part quand même, et le message indique ce qui est parti.
- Vérifié par un test : un petit serveur de messagerie lancé pour l'occasion reçoit bien le message, avec le PDF et le bon destinataire.

#### Mise en veille
- Réglage dans Sécurité du compte, rubrique « Mise en veille » : Jamais, 1, 2, 5, 10, 15 ou 30 minutes. 5 minutes par défaut.
- Sans toucher, clic ni frappe pendant ce délai, SCOLIA se verrouille. Le temps passé en arrière-plan compte aussi.
- L'écran « SCOLIA est en veille » redemande le code, ou l'empreinte si elle est activée.
- La session reste ouverte derrière : on reprend exactement sur la même page.
- Un mauvais code compte comme une erreur de connexion : cinq erreurs, deux minutes de blocage.
- Sans réseau, le code est vérifié sur l'appareil.
- En veille, aucune bannière ne s'affiche : le contenu reste privé.
- Le geste retour et les raccourcis clavier sont bloqués.
- « Verrouiller maintenant » met en veille tout de suite ; « Changer de compte » ferme la session.

#### Fiabilité de l'envoi WhatsApp
- La version de l'API de Meta se règle dans « Planifier ». Laissée vide, c'est la version du compte de l'établissement qui s'applique : SCOLIA ne dépend plus d'une version qui sera retirée.
- Quatre tests face à un faux serveur Meta vérifient :
  - le dépôt du PDF, avec le jeton d'accès ;
  - le numéro converti au format international ;
  - le message avec modèle, le PDF en en-tête ;
  - le message sans modèle, en document ;
  - le refus de Meta, rapporté avec son propre message.
- Seul l'envoi réel reste à faire, avec le compte Meta de l'établissement.

#### Mises à jour de l'APK sans désinstaller
- Tous les APK sont signés avec la même clé SCOLIA, créée une fois puis gardée par GitHub. Une clé de l'établissement, placée dans les secrets du dépôt, passe en priorité.
- Le numéro de version augmente à chaque compilation.
- Un nouvel APK s'installe donc par-dessus l'ancien, comme une mise à jour.
- Il faut désinstaller une dernière fois l'ancienne version, signée par une clé jetable.

#### Recherche façon Google partout, avec la voix
- Toutes les recherches reprennent la barre de la recherche de l'accueil :
  - retour à gauche ;
  - champ sans cadre ;
  - croix pour effacer ;
  - micro à droite.
- Endroits concernés :

| Recherche | Téléphone | Ordinateur |
|---|---|---|
| Accueil, la référence | Plein écran | Fenêtre, même contenu |
| Menu, outils et réglages | Barre en haut du menu, mêmes lignes de résultats | Sans objet |
| Filtre d'une liste | Barre à la place de l'en-tête | Barre en pilule dans l'en-tête |
| Recherche des réglages | Même page que l'accueil | Même fenêtre que l'accueil |
| Info sur l'application | Barre en pilule | Barre en pilule |
| Champs « Rechercher » des pages | Remplacés par le filtre de l'en-tête | Pilule arrondie avec loupe |

- La voix :
  - Android : la fenêtre « Parlez maintenant » du service vocal du téléphone, en français. SCOLIA n'a pas besoin de la permission du micro.
  - iPhone : la reconnaissance vocale d'Apple, en français. iOS demande une fois l'accord pour le micro et la dictée. L'écoute s'arrête seule après un court silence.
  - Pendant l'écoute, le champ affiche « Parlez maintenant… » et le micro passe au rouge.
  - Le texte dicté lance la recherche aussitôt.
  - Sans service vocal, le micro ne s'affiche pas.
  - Windows : pas de micro dans SCOLIA. La dictée de Windows, touches Windows+H, écrit dans n'importe quel champ.
- Vérifié par les essais automatiques avec une voix simulée : le micro est présent partout, « Parlez maintenant… » s'affiche, le texte dicté arrive dans le champ. La reconnaissance réelle reste à essayer sur un téléphone.

#### Voix hors ligne
- Android 12 et plus, si le téléphone le permet : l'écoute se fait dans le téléphone, sans Internet. SCOLIA demande l'accès au micro au premier toucher du micro.
- Si le français n'est pas encore installé, SCOLIA en demande le téléchargement (Android 13 et plus) pour les fois suivantes. Cette fois-là, la fenêtre du service vocal prend le relais.
- Sinon, la fenêtre « Parlez maintenant » du service vocal est réglée pour préférer le modèle hors ligne quand il est présent.
- iPhone : la reconnaissance se fait sur l'appareil quand l'iPhone le sait, sans Internet.

#### Saisie prédictive
- Fiche promotion, code « INF1 » : propositions « Première Infirmière », « Deuxième », « Troisième », « Quatrième », puis la tournure de la section.
- Code « ACC » ou désignation « deuxième acc » : « Deuxième Accoucheuse ».
- Chaque lettre resserre les propositions. Les désignations déjà utilisées ne sont plus proposées.
- Un choix remplit :
  - le code, par exemple ACC2 ;
  - le niveau ;
  - la promotion suivante, ACC3 ;
  - la section, si elle existe.
- SCOLIA apprend les tournures de l'établissement : une promotion « Première Soignante » fait proposer « Deuxième Soignante ».
- Assistant d'installation : mêmes propositions sous chaque ligne. « Ajouter » prépare la ligne suivante : INF1 puis INF2 « Deuxième Infirmière », même mensualité.
- Année scolaire : « 2026 » propose « 2026-2027 ». Le dernier mois suit le premier, dix mois d'école, tant qu'il n'est pas choisi à la main. Le résumé se recalcule : nombre de mois, période, exemple de retard.

#### Fluidité
| Écran | Avant | Après |
|---|---|---|
| Pointage, données | 666 ms, une requête par membre | 204 ms, une seule requête pour tout le personnel |
| Listes du téléphone | 40 cartes construites d'un coup | 8 cartes tout de suite, puis 8 par 8 entre deux images |
| Recherche | Résultats 250 ms après la frappe | 180 ms |

- Mesures faites sur l'ordinateur d'essai, pas sur un téléphone : sur un téléphone, les durées sont plus longues, mais l'écart reste.
- Un ordinateur principal pas encore mis à jour fonctionne toujours : SCOLIA revient alors à une requête par membre, six à la fois.

#### Écran de veille refait
- Présentation comme l'écran verrouillé d'un téléphone :
  - logo et nom de l'établissement en haut ;
  - heure et date en grand, l'heure se met à jour ;
  - pastille « Verrouillé depuis 14:32 » ;
  - carte du compte : initiales, nom, rôle et identifiant.
- Champ du code : icône de cadenas, œil pour afficher ou masquer, bordure bleue à la saisie.
- Erreur en rouge avec son icône. Le message parle du code, plus de l'identifiant : « Code incorrect. Encore 4 tentatives… ».
- Empreinte : un rond au centre, sous « ou », comme sur Android.
- « Changer de compte » reste sur l'écran de veille, avec l'explication : « Cette session reste verrouillée, vous pourrez y revenir ».

#### Retour depuis la page de connexion
- Avant, « Changer de compte » fermait la session : impossible de revenir.
- Maintenant, la session reste verrouillée derrière la page de connexion. La page de connexion propose en tête « Revenir à la session de Secrétariat ».
- Le geste retour d'Android et la touche Échap y ramènent aussi.
- Une nouvelle connexion réussie ferme la session verrouillée. Une déconnexion normale ne propose aucun retour.
- Vérifié par 5 contrôles automatiques, sur téléphone et sur ordinateur.

## 4. Limites à connaître

| Sujet | Limite | Solution proposée |
|---|---|---|
| Envoi WhatsApp sans intervention | Il faut un compte WhatsApp Business de l'établissement chez Meta : identifiant du numéro et jeton d'accès. Pour écrire en premier, il faut aussi un modèle de message approuvé, avec un document en en-tête. Aucun identifiant n'a été inventé. | Créer le compte sur Meta for Developers, puis saisir les trois valeurs dans « Planifier ». Sans elles, le PDF est préparé chaque jour et la notification propose de le partager. |
| Notifications téléphone application fermée | Les alertes arrivent tant que SCOLIA est ouvert ou récemment mis en arrière-plan. Une fois l'application fermée par le système, il faudrait un service de notifications push. | Ajouter un relais push (Firebase pour Android, APNs pour iPhone) le jour où l'établissement crée ces comptes. |
| iPhone en arrière-plan | iOS suspend vite les applications : la notification n'arrive que dans les instants qui suivent la mise en arrière-plan. | Même solution : notifications push. |
| Vérification sur appareil | L'empreinte réelle, la bannière du système, la recherche à la voix et l'envoi WhatsApp n'ont pas été essayés sur un vrai téléphone ni avec un vrai compte Meta. | Essai sur votre téléphone avec l'APK livré. |
| Voix sans réseau | Hors ligne seulement si le téléphone sait reconnaître le français sur l'appareil : Android 12 et plus selon le modèle, iPhone. Sur un téléphone plus ancien, la voix a besoin d'Internet. | Télécharger le français pour l'usage hors ligne dans les réglages vocaux du téléphone, une fois, avec Internet. |

## 5. Pistes suivantes

- Relais push pour les notifications quand l'application est fermée.
- Rapport hebdomadaire et mensuel, sur le même modèle que le rapport journalier.
