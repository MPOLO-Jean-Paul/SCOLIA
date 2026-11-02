# SCOLIA Pro — audit général, 31 octobre 2026

Périmètre : Android, iPhone et iPad, ordinateur Windows.
Méthode :
- chaque écran est capturé sur téléphone, en 360 × 760, et sur ordinateur, en 1280 × 820 ;
- les parcours réels sont rejoués : inscription, encaissement, menus, défilement, clavier ;
- l'application iOS est compilée sur les Mac de GitHub.

## 1. iPhone et iPad

| N° | Point | Avant | Après |
| --- | --- | --- | --- |
| 1 | Fichier installable | Seul un paquet pour simulateur existait. L'IPA signé exige un compte Apple Developer. | Un IPA pour iPhone et iPad, arm64, est produit à chaque changement. Il se signe avec votre identifiant Apple, voir section 4. |
| 2 | Icône | Aucune : icône blanche sur l'écran d'accueil. | Logo SCOLIA, 1024 px, fond blanc. |
| 3 | Écran de lancement | Absent : iOS affichait l'application en format réduit d'ancien iPhone. | Écran de lancement déclaré, fond clair ou sombre. Plein écran sur tous les modèles. |
| 4 | Secrets locaux | Clé de chiffrement dans un fichier, à côté des données. | Clé dans le trousseau iOS, propre à l'appareil, exclue des sauvegardes. |
| 5 | Face ID et Touch ID | La case « Activer » apparaissait, mais rien ne s'activait. | Activation réelle : preuve dans le trousseau, liée aux visages et empreintes enregistrés. Elle s'efface s'ils changent. |
| 6 | Libellés | « Déverrouiller avec l'empreinte » sur iPhone. | « Face ID » ou « Touch ID » selon l'appareil. « l'empreinte » sur Android. |
| 7 | Reçus et relevés | Ne s'ouvraient pas. | Aperçu iOS avec le bouton Partager : Imprimer, Fichiers, WhatsApp. Visibles aussi dans l'app Fichiers. |
| 8 | Sons | Muets. | Sons discrets qui respectent le mode silencieux. |
| 9 | Retour dans l'application | Non détecté. | Détecté : actualisation et reprise de l'assistant Tailscale. |
| 10 | Serveur par Tailscale | Un nom `….ts.net` en HTTP était bloqué par iOS. | Autorisé pour ce domaine seulement : le tunnel WireGuard chiffre déjà la liaison. |
| 11 | Recherche du serveur | iOS réserve la diffusion Wi-Fi aux applications autorisées par Apple. | La recherche rend une liste vide sans erreur. L'écran propose de saisir l'adresse. |

## 2. Téléphone, Android et iOS

| N° | Écran | Avant | Après |
| --- | --- | --- | --- |
| 12 | Saisie | Le clavier cachait le champ, surtout dans Configuration. | L'écran s'arrête au bord du clavier et le champ remonte au-dessus. Les onglets du bas se retirent pendant la saisie. |
| 13 | Saisie | Le premier correctif effaçait la zone sûre du système, sous l'encoche et la barre d'état. | La zone sûre reste intacte. Seul le contenu se réduit. |
| 14 | Accueil | « Recouvreme / nt » coupé au milieu du mot. | Libellés courts : Encaisser, Inscrire, Retards, Rapports. |
| 15 | Réseau | Bouton « Synchroniser maintenan… » coupé. | « Synchroniser », en entier. |
| 16 | Réseau | Texte « VPN Android » affiché aussi sur iPhone et ordinateur. | Texte propre à chaque appareil. |
| 17 | Premier lancement | « Configurer l'établissement sur ce télép… » coupé. | « Configurer sur ce téléphone », en entier. |
| 18 | Résultats | Moyennes par cours en un seul bloc, avec des tirets pour les cours non notés. | Seuls les cours notés, un par ligne. |
| 19 | Thème | Six couleurs sur deux lignes, noms coupés. | Une seule ligne, noms entiers. |

## 3. Vérifications

| Contrôle | Téléphone | Ordinateur |
| --- | --- | --- |
| Écrans capturés et relus | 68 | 58 |
| Commandes de menu essayées | 135, 0 à revoir | 171, 0 à revoir |
| Inscription et encaissement réels | reçu créé | reçu créé |
| Fluidité et défilement | 0 échec | sans objet |
| Clavier simulé et zone sûre | 0 échec | sans objet |
| Contrastes et thèmes | 0 échec | 0 échec |
| Fenêtres, tailles, texte agrandi | sans objet | 0 échec |
| Tests automatiques | 229 réussis | 229 réussis |

Limites :
- aucun essai n'a pu être fait sur un vrai iPhone ni un vrai téléphone Android ;
- l'iPhone se connecte à un serveur SCOLIA (ordinateur ou téléphone Android serveur). iOS ne permet pas de garder un serveur actif en arrière-plan.

## 4. Installer SCOLIA sur iPhone ou iPad

Apple exige une signature avant toute installation.

**Avec un identifiant Apple gratuit, sans Mac :**
1. Téléchargez l'artefact « SCOLIA_Phone_iPhone_IPA_a_signer » et décompressez-le.
2. Sur un PC Windows ou un Mac, installez Sideloadly et iTunes.
3. Branchez l'iPhone en USB et faites-lui confiance.
4. Glissez `SCOLIA_Phone_iOS.ipa` dans Sideloadly, saisissez votre identifiant Apple, puis Start.
5. Sur l'iPhone, ouvrez Réglages, Général, VPN et gestion de l'appareil, puis faites confiance à votre identifiant.
6. Si iOS le demande, activez le Mode développeur dans Réglages, Confidentialité et sécurité.

Avec un identifiant gratuit, il faut re-signer l'application tous les 7 jours.

**Avec un compte Apple Developer, 99 USD par an :**
- ajoutez le certificat et le profil dans les secrets du dépôt, voir `src/Scolia.Phone.iOS/README.md` ;
- le workflow « SCOLIA Phone iPhone - IPA signe » produit alors un IPA signé, valable un an ou diffusable par TestFlight.
