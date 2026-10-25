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
- Verrouillage après 5 échecs, pendant 2 minutes. Le formulaire affiche un compte à rebours jusqu'à 0 seconde, puis l'accès revient seul. Les identifiants inconnus se verrouillent de la même façon, pour ne pas révéler quels comptes existent.
  Le message ne révèle pas si le compte existe.
- Limitation du nombre de tentatives de connexion : 10 par minute et par adresse.
- Un compte désactivé ou dont le mot de passe change ferme immédiatement ses sessions.
- Le dernier compte secrétariat actif ne peut pas être désactivé ni changer de rôle : c'est lui qui administre SCOLIA.

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

Deux comptes, comme dans la version 79. Le secrétariat est l'administrateur.

| Rôle | Périmètre principal |
|---|---|
| Secrétariat | Tout : apprenants, encaissements, scolarité, personnel, et aussi le paramétrage, les utilisateurs, les sauvegardes et le journal d'audit |
| Comptabilité | Encaissements, annulations, dépenses, arrêté de caisse, journal et rapports. Pas de paramétrage ni de gestion des comptes |

Une base d'une version précédente est convertie au démarrage. Les comptes Administrateur et Direction deviennent Secrétariat. Un ancien compte Enseignant est désactivé ; le secrétariat peut le réactiver en lui donnant l'un des deux rôles.

## Intégrité des données

- Chaque opération sensible écrit une entrée d'audit dans la même transaction.
- Une écriture qui ne laisse pas de trace est refusée par le contexte de base de données :
  l'enregistrement échoue, il ne passe jamais en silence. Un test vérifie ce comportement.
- Chaque entrée porte l'empreinte SHA-256 de son contenu et de l'entrée précédente.
  La vérification (menu Journal d'audit) signale la première entrée altérée.
- Les reçus sont numérotés sans trou. Un reçu erroné est annulé avec un motif,
  jamais supprimé. Le PDF annulé porte la mention « ANNULÉ ».
- Un reçu peut être vérifié publiquement grâce à son code (page `/verifier/{code}`),
  sans connexion. La réponse ne contient que le numéro, la date, le montant, le nom de l’apprenant,
  l’établissement et le statut du reçu. Le code de vérification est aléatoire et sans rapport avec le numéro.
- L'arrêté de caisse verrouille la période : aucune opération antérieure ne peut être modifiée.
- Les encaissements portent une clé d'idempotence : un double envoi ne crée pas de second reçu.

## Sauvegardes

- Sauvegarde automatique quotidienne, puis avant toute mise à jour de schéma.
- Chaque sauvegarde est contrôlée (`PRAGMA integrity_check`) avant d'être conservée.
- Une sauvegarde est un fichier unique, copiable tel quel sur une clé USB ou un disque externe.
- Restauration : une sauvegarde de sécurité de l'état actuel est faite d'abord, et la restauration est tracée dans le journal.
- Conservation : 30 jours et au moins 10 sauvegardes.

## Responsabilités

Ce que SCOLIA garantit :

- Chaque action qui modifie des données porte le nom de la personne qui l'a faite, son adresse et l'heure.
- La modification d'un dossier d'apprenant conserve l'état avant et après.
- Un journal modifié en dehors de SCOLIA est détecté, et la vérification indique la première entrée altérée.

Ce que SCOLIA ne garantit pas, et qui relève de l'établissement :

- Quiconque a accès au fichier de la base sur le poste serveur peut le modifier. Le journal le révèle, mais ne l'empêche pas. Il faut donc restreindre les droits du dossier `donnees` au compte du service et conserver des sauvegardes hors du poste.
- Le choix des mots de passe, la désactivation des comptes des personnes qui quittent l'établissement et la vérification régulière du journal restent des tâches d'administration.
- Les données personnelles des élèves et des parents relèvent de la politique de protection de l'établissement. SCOLIA ne supprime aucun dossier automatiquement. Une durée de conservation reste à définir par l'établissement.

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
- L'accès réseau est une action explicite (onglet Fichier, rubrique « Poste et réseau », avec l'autorisation administrateur Windows).
  Il ouvre le port uniquement sur les profils réseau privé et de domaine.
- Le trafic est en HTTP sur le réseau local de l'établissement. Pour un accès depuis l'extérieur,
  placez SCOLIA derrière un reverse proxy HTTPS ou un tunnel VPN : ne l'exposez jamais directement sur Internet.

## Signalement d'une faille

Ne publiez pas une faille dans un ticket public. Contactez le responsable du projet
en privé, avec la version concernée et les étapes pour la reproduire.
