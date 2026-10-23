# Sécurité et protection des données

## Modèle de menace

SCOLIA Pro contient des données personnelles d'élèves et de parents, ainsi que
des flux financiers. Les risques retenus sont :

- un accès non autorisé depuis le réseau local ;
- un utilisateur qui consulte ou modifie des données hors de son rôle ;
- une falsification du journal ou des reçus après coup ;
- la perte de la base (panne, vol, erreur de manipulation) ;
- une attaque web classique (injection, script inséré, requête forgée).

## Authentification

- Comptes nominatifs. Aucun compte partagé n'est proposé.
- Mots de passe : au moins 8 caractères, lettres et chiffres exigés.
- Verrouillage du compte après 5 échecs, pendant 15 minutes.
  Le message ne révèle pas si le compte existe.
- Limitation du nombre de tentatives de connexion : 10 par minute et par adresse.
- Un compte désactivé ou dont le mot de passe change ferme immédiatement ses sessions.
- Le dernier administrateur actif ne peut pas être supprimé ni désactivé.

## Session

- Cookie `scolia.session` : `HttpOnly` (inaccessible au JavaScript),
  `SameSite=Strict`, `Secure` lorsque la connexion est en HTTPS.
- Durée de 10 heures, prolongée à chaque activité.
- L'empreinte de sécurité de l'utilisateur est revalidée à chaque requête.
- Protection anti-CSRF : chaque requête qui modifie des données doit porter l'en-tête `X-Scolia`.
  Un site tiers ne peut pas faire exécuter une action à la place d'un utilisateur connecté.

## Autorisations

Les droits sont attribués par rôle et vérifiés côté serveur sur chaque point d'API.
L'interface masque les menus, mais ce masquage n'est pas la protection réelle.

| Rôle | Périmètre principal |
|---|---|
| Administrateur | Tout, dont utilisateurs, sauvegardes, paramètres |
| Direction | Lecture et écriture des apprenants et de la scolarité, annulation de paiements, audit, personnel, sauvegardes |
| Secrétariat | Apprenants, encaissements, scolarité, rapports |
| Comptabilité | Encaissements, annulations, dépenses, arrêté de caisse, rapports |
| Enseignant | Cours, notes, présences, lecture des apprenants |

## Intégrité des données

- Chaque opération sensible écrit une entrée d'audit dans la même transaction.
- Chaque entrée porte l'empreinte SHA-256 de son contenu et de l'entrée précédente.
  La vérification (menu Journal d'audit) signale la première entrée altérée.
- Les reçus sont numérotés sans trou. Un reçu erroné est annulé avec un motif,
  jamais supprimé. Le PDF annulé porte la mention « ANNULÉ ».
- Un reçu peut être vérifié publiquement grâce à son code (page `/verifier/{code}`),
  sans connexion, avec les seules informations qui figurent sur le reçu.
- L'arrêté de caisse verrouille la période : aucune opération antérieure ne peut être modifiée.
- Les encaissements portent une clé d'idempotence : un double envoi ne crée pas de second reçu.

## Sauvegardes

- Sauvegarde automatique quotidienne, puis avant toute mise à jour de schéma.
- Chaque sauvegarde est contrôlée (`PRAGMA integrity_check`) avant d'être conservée.
- Une sauvegarde est un fichier unique, copiable tel quel sur une clé USB ou un disque externe.
- Restauration : une sauvegarde de sécurité de l'état actuel est faite d'abord, et la restauration est tracée dans le journal.
- Conservation : 30 jours et au moins 10 sauvegardes.

## En-têtes HTTP

| En-tête | Valeur | Effet |
|---|---|---|
| `Content-Security-Policy` | `default-src 'self'` et règles dédiées | Bloque les scripts et ressources externes |
| `X-Frame-Options` | `DENY` | Empêche l'intégration dans un autre site |
| `X-Content-Type-Options` | `nosniff` | Empêche la fausse détection de type |
| `Referrer-Policy` | `no-referrer` | N'envoie aucune adresse de page |
| `Permissions-Policy` | caméra limitée à l'application, micro et géolocalisation désactivés | Limite les capteurs |

## Erreurs

Les erreurs suivent le format RFC 9457 (`application/problem+json`). Elles décrivent
la cause sans révéler de détail technique interne : la pile d'appel reste dans le journal.

## Journaux

- Journal quotidien dans `donnees\journaux`, conservé 60 jours.
- Les mots de passe, jetons et cookies ne sont jamais journalisés.

## Réseau local

- Par défaut, SCOLIA n'écoute que sur ce poste (`localhost`).
- L'accès réseau est une action explicite (`ACTIVER_RESEAU_LOCAL.cmd`).
  Il ouvre le port uniquement sur les profils réseau privé et de domaine.
- Le trafic est en HTTP sur le réseau local de l'établissement. Pour un accès depuis l'extérieur,
  placez SCOLIA derrière un reverse proxy HTTPS ou un tunnel VPN : ne l'exposez jamais directement sur Internet.

## Signalement d'une faille

Ne publiez pas une faille dans un ticket public. Contactez le responsable du projet
en privé, avec la version concernée et les étapes pour la reproduire.
