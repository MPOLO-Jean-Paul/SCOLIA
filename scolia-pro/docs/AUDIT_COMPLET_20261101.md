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
- Dans Aide, juste après « À propos ». Le catalogue compte 12 modules et 50 fonctions décrites, avec une recherche.

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

## 4. Limites à connaître

| Sujet | Limite | Solution proposée |
|---|---|---|
| Envoi WhatsApp sans intervention | Il faut un compte WhatsApp Business de l'établissement chez Meta : identifiant du numéro et jeton d'accès. Pour écrire en premier, il faut aussi un modèle de message approuvé, avec un document en en-tête. Aucun identifiant n'a été inventé. | Créer le compte sur Meta for Developers, puis saisir les trois valeurs dans « Planifier ». Sans elles, le PDF est préparé chaque jour et la notification propose de le partager. |
| Notifications téléphone application fermée | Les alertes arrivent tant que SCOLIA est ouvert ou récemment mis en arrière-plan. Une fois l'application fermée par le système, il faudrait un service de notifications push. | Ajouter un relais push (Firebase pour Android, APNs pour iPhone) le jour où l'établissement crée ces comptes. |
| iPhone en arrière-plan | iOS suspend vite les applications : la notification n'arrive que dans les instants qui suivent la mise en arrière-plan. | Même solution : notifications push. |
| Vérification sur appareil | L'empreinte réelle, la bannière du système et l'envoi WhatsApp n'ont pas été essayés sur un vrai téléphone ni avec un vrai compte Meta. | Essai sur votre téléphone avec l'APK livré. |

## 5. Pistes suivantes

- Relais push pour les notifications quand l'application est fermée.
- Rapport hebdomadaire et mensuel, sur le même modèle que le rapport journalier.
- Envoi du rapport par e-mail en plus de WhatsApp, pour les établissements qui ont une messagerie.
