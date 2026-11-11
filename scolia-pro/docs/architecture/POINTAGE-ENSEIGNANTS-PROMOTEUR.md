# SCOLIA Pro — cadre métier du pointage du personnel et du rôle Promoteur

Statut : **cadre métier étudié ; Promoteur agrégé implémenté sur la branche de fonctionnalité ; pointage kiosque sécurisé à réaliser après validation du modèle d'identité**.

## 1. Contrainte Android non négociable

L'API publique Android `BiometricPrompt` ne fournit **pas** l'identité d'un doigt enregistré, uniquement une authentification du propriétaire de l'appareil. Sur un téléphone commun, on ne peut donc pas attribuer un pointage à un enseignant en faisant simplement toucher le capteur Android. Une réussite biométrique d'un appareil partagé ne prouve jamais lequel des employés a pointé. Ne pas collecter les empreintes brutes à travers une API non publique ni attribuer arbitrairement un nom à l'authentification Android.

**La biométrie déjà ajoutée à SCOLIA est réservée à l'ouverture de la session d'un compte personnel**, non à l'identification des enseignants d'un établissement avec un seul téléphone.

## 2. Méthode recommandée sans lecteur externe (phase 1)

Un **téléphone kiosque de l'établissement**, supervisé par un responsable autorisé, sert de borne d'entrée/sortie.

1. Enrôler le dossier professionnel de chaque enseignant (identifiant immuable, statut actif, calendrier et horaires approuvés). Effectuer un contrôle physique initial.
2. Délivrer un badge NFC ou QR individuel. Le badge sert à **sélectionner** le dossier, pas à authentifier seul (QR statique copiable).
3. Exiger une preuve indépendante propre à l'enseignant : code personnel secret vérifié côté serveur avec limite d'essais ; la validation par le surveillant peut être une garantie supplémentaire, jamais la seule. Un code PIN très court ne suffit pas sans verrouillage et supervision. Aucune image d'empreinte n'est traitée.
4. Le kiosque lié cryptographiquement à l'établissement soumet un événement `entry` ou `exit` portant un identifiant d'opération aléatoire, l'identité vérifiée et l'identifiant du terminal.
5. Le service principal **attribue la date/heure authoritative**, applique les règles et renvoie un accusé d'enregistrement. Aucun pointage ne devient « confirmé » sur une réponse locale seule.
6. L'enseignant voit une confirmation explicite : prénom affiché, entrée/sortie, heure et statut « confirmé ».
7. Si réseau indisponible : évènement signé, horodaté localement et marqué **provisoire** dans une file chiffrée ; aucune promesse de présence officielle avant réconciliation. Vérification de dérive d'horloge, limite de rétention et blocage des doublons à la reconnexion.
8. Chaque modification/correction ultérieure est un nouvel événement signé, justifié et audité ; jamais une modification silencieuse du pointage original.

### Alternative plus forte sans capteur externe

Chaque enseignant utilise **son propre téléphone**, avec une passkey/clé privée liée à son compte, éventuellement déverrouillée par `BiometricPrompt`. La borne affiche un défi à usage unique (QR tournant) ou le terminal effectue un défi/réponse. **Cette méthode ne suppose pas que la borne identifie le doigt** : chaque clé est liée à un enseignant spécifique. Prévoir une voie de secours équitable pour les enseignants sans smartphone.

### Phase 2 : lecteur d'empreintes externe

Seulement si l'institution décide de réellement identifier différentes personnes par empreintes :
- lecteur externe USB OTG/Bluetooth certifié, pilote/SDK Android documenté, algorithme de reconnaissance 1:N évalué sur le matériel exact ;
- évaluation des risques et de la conformité (données biométriques) avant enrôlement ;
- consentement/information, durées de conservation, alternatives non biométriques, suppression au départ, contrôle des faux rejets/acceptations ;
- modèles biométriques chiffrés localement en environnement sécurisé, jamais photos d'empreintes ni données en clair dans des sauvegardes ;
- lecteur non réseau ne signifie pas absence de service de synchronisation ; vérifier les certificats et la fiabilité du fournisseur.

Aucun achat ou développement de capteur externe ne doit être présenté comme déjà opérationnel.

## 3. Règles de pointage des enseignants

Un événement journalier possède `staffId`, `type` (entrée/sortie), `recordedAtUtc`, `officialTimeZone`, `deviceId`, `proofMethod`, `operationId`, `status` (provisoire/confirmé/rejeté), `serverRevision`, `actorId`, `auditId`.

- Un identifiant du personnel + une journée locale + un type = un pointage confirmé au maximum ; doublons d'envoi idempotents.
- La sortie est interdite sans entrée confirmée, et doit être postérieure à l'entrée.
- Une journée est **présente** lorsqu'entrée **et** sortie sont confirmées. Sinon statut incomplet/pending. Les absences sont déterminées seulement après la clôture et le délai de régularisation défini.
- Les retards comparent l'heure d'entrée à l'horaire du planning réel, avec fuseau de l'école. La règle historique SCOLIA est 08:00, à confirmer pour les horaires particuliers ; ne pas compter tous les samedis comme absence par défaut.
- La règle historique « 3 retards dans une semaine = 1 absence » est conservée uniquement après application d'un planning et de justificatifs cohérents. Ne jamais déclencher de déduction salariale sur événement non synchronisé ou contesté.
- Les cas de mission, jour férié, repos, permission, maladie et formation nécessitent un statut et un responsable de validation.
- Contestation : correction motivée par un utilisateur ayant le droit, deux opérations visibles dans l'audit et notification à l'intéressé. Aucune correction rétroactive anonyme.

### Modèle de menace

Risques : badge copié, collègue qui prête son badge et son code, borne perdue, terminal compromis, heure modifiée, reconnexions répliquées, interférences réseau, ancien salarié, permissions trop larges. Contrôles : badge + secret individuel + supervision, liaison cryptographique du terminal, verrouillage, horloge serveur, journal append-only, anti-rejeu, révocation du terminal, règles RBAC **côté serveur**, sauvegardes chiffrées et tests de restauration.

## 4. Promoteur — troisième rôle **lecture seule**

Le rôle `Promoteur` reçoit exclusivement `pilotage.lire` côté serveur. Il **ne reçoit pas** `apprenants.lire`, `paiements.lire`, `paiements.encaisser`, `paiements.annuler`, `rapports.lire` (rapports potentiellement nominatifs), `utilisateurs.gerer`, `personnel.gerer`, `parametres.ecrire` ni `sauvegardes.gerer`.

Il peut personnaliser **sur son appareil** : thème, couleurs, police, taille, animations et sons, sans modifier les paramètres globaux de l'établissement. Les deux rôles existants restent intacts.

### Vue d'ensemble

- Effectif actif de l'année par promotion et total.
- Nombre de dossiers avec solde restant ; nombre de dossiers avec retard déjà exigible.
- Recettes validées **du jour**, du mois, de l'année, total général et par promotion.
- Solde restant total et par promotion ; portion déjà exigible distincte de la dette future.
- Pour chaque promotion et période de l'année : montants dus, montants affectés au mois de frais, reste à régler.
- Pour chaque promotion et mois civil : **encaissements datés dans ce mois**, qui peuvent régler des frais plus anciens. Ne jamais présenter les recettes encaissées en novembre comme étant forcément des frais de novembre.
- Les paiements annulés sont exclus des sommes ; les dossiers et reçus nominatifs ne quittent pas l'API de pilotage.
- Afficher l'année active, le moment de mise à jour et, sur téléphone déconnecté, **« dernière synchronisation »** plutôt que chiffres présentés comme instantanés.

### Définitions de gestion

- **Effectif** : inscriptions statut actif de l'année en cours, pas tous les anciens matricules.
- **Encaissement** : somme des reçus de statut valide, datés dans la fenêtre choisie. Un remboursement/annulation doit réduire le chiffre selon les règles comptables de l'application.
- **Solde restant** : somme des comptes nets après paiements et prises en charge ; peut inclure des frais à échéance future.
- **Exigible en retard** : sous-ensemble du solde dont l'échéance est dépassée.
- **Paiement sur mois de frais** : somme des allocations affectées aux rubriques de cette période, indépendamment de la date de paiement.
- **Période** : mois de l'année scolaire, qui peut traverser deux années civiles.

## 5. Sécurité et confidentialité

En RDC, le Code du numérique n° 23/010 encadre les données à caractère personnel, dont les données biométriques. Toute collecte d'empreintes doit passer par une évaluation juridique et une politique documentée. Les enseignants doivent connaître le traitement et disposer d'une solution appropriée lorsqu'ils ne souhaitent pas ou ne peuvent pas utiliser une empreinte.

Les contrôles essentiels sont : permissions contrôlées sur chaque route API et jamais uniquement par l'interface ; mot de passe individuel ; limitation des essais ; journal d'audit ; segmentation des données ; chiffrement du terminal ; sauvegardes testées ; révocation des comptes et terminaux ; paramètres de session limités ; mises à jour signées. Aucun système connecté ne peut être décrit comme « impossible à pirater ».

## 6. Critères de validation avant déploiement

1. Un compte Promoteur voit seulement le tableau de pilotage et ses préférences locales.
2. Toute requête Promoteur vers paiements, listes nominatives, correction de pointage, utilisateurs ou configuration globale retourne **403**.
3. Les totaux par promotion se réconcilient exactement avec les totaux globaux (même année, même date).
4. Une annulation de reçu ne figure plus dans les encaissements.
5. Les règlements de dettes antérieures ne sont pas attribués au mauvais mois de frais.
6. Un pointage en double ne crée pas deux signatures, même après reconnexion.
7. Aucun événement en attente hors ligne ne devient présence confirmée avant accusé serveur.
8. Aucune personne ne peut être « reconnue par son doigt » via `BiometricPrompt` sur la borne commune.
9. Le terminal perdu ou révoqué ne peut plus créer de nouveaux pointages.
10. Les tests sur Android réel couvrent le retour, l'accessibilité, la faible connexion, le changement de jour et les préférences.

## 7. Phasage sans casser l'existant

- **Lot A implémenté** : nouveau rôle Promoteur, permission isolée, API de statistiques agrégées, interface native de consultation, tests de séparation des droits.
- **Lot B cadré mais non activé** : enrôlement du badge NFC/QR + secret du personnel, borne supervisée et autorisée, enregistrement signé, idempotence, écran de confirmation et journal d'audit. Ne pas réutiliser le déverrouillage Android pour identifier tout le personnel.
- **Lot C étudié séparément** : dispositif biométrique externe conforme, avec alternative manuelle.
- **Lot D ultérieur** : évaluer la présence des apprenants sans copier automatiquement la méthode du personnel.
