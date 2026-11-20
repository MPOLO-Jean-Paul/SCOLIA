SCOLIA Pro 1.2.40 — Android et Windows

Connexion par Internet
- Deux relais WSS pour le même établissement : Cloudflare en principal lorsqu'il est configuré, Render en secours.
- Le serveur principal ouvre et entretient les deux connexions en parallèle. En cas de coupure du primaire, le client peut reprendre par le secours ; réessai du principal après stabilisation.
- Le changement d'adresse du relais ne renouvelle plus les clés de salle : les appareils déjà autorisés conservent leur accès, sans deuxième QR.
- Le contrôle des autorisations individuelles et la révocation restent exécutés sur le serveur SCOLIA ; les deux relais transmettent le même trafic chiffré.
- Les réglages Réseau et postes affichent deux adresses et l'état de chaque relais.

Copies locales et synchronisation
- Après ouverture d'une session autorisée, copie chiffrée locale automatique et progressive des dossiers et comptes accessibles (sans devoir cliquer sur « Préparer hors ligne »).
- Contrôle périodique de la révision du serveur, actualisation à la reprise et à la notification d'un autre poste.
- Les réponses JSON inchangées sont confirmées par ETag SHA-256 et HTTP 304 : aucun re-téléchargement de leur contenu.
- Envois hors ligne, accusés d'idempotence et conflits restent gérés par le journal local existant.
- Une connexion du serveur principal reste nécessaire pour réconcilier les opérations et consulter les nouvelles données ; les quotas réseau et les droits d'accès restent applicables.

Accueil Windows
- Carte de bienvenue harmonisée avec Android : avatar rond, statut réseau toujours visible, caisse et raccourcis mieux hiérarchisés.
- Raccourcis illustrés avec description, disposition responsive et droits du compte conservés.

Avant d'installer
- Synchroniser toutes les écritures en attente et laisser achever les sauvegardes ; une sauvegarde automatique précède l'installation.
- La signature Android officielle doit être celle des versions précédentes (récupération et contrôle PR18, jamais la clé temporaire d'une compilation de test).
- L'adresse Cloudflare déjà enregistrée reste prioritaire. Le relais Render doit être opérationnel pour offrir un secours réel.
