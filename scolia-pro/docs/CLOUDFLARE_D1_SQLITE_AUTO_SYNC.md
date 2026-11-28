# SCOLIA Pro — synchronisation automatique SQLite / Cloudflare D1 + R2

## Périmètre réellement implémenté

SQLite sur le PC de l'établissement reste la **base active et l'autorité métier**.
Après activation volontaire de « Sauvegarde et relève en ligne » et consentement
du responsable au jumelage D1, le service `CloudContinuityService` exécute
automatiquement son cycle à intervalles de deux minutes :

1. **Cloud vers SQLite :** il relève les opérations chiffrées déposées
   par les appareils dans la boîte existante de la console Cloudflare,
   puis les rejoue dans l'API SQLite sur le serveur principal.
   Les droits de l'auteur, le verrou de licence, les contrôles métier et
   l'idempotence continuent d'être vérifiés côté serveur.
2. **SQLite vers Cloudflare R2 :** si les données ont changé, une copie
   cohérente de SQLite est compressée et chiffrée avec la clé de l'école
   puis sauvegardée dans le bucket privé R2. Elle est renouvelée au
   maximum toutes les heures en présence de changements, ou au moins
   quotidiennement. R2 contient les octets de la base sauvegardée ;
   D1 ne reçoit jamais les tables en clair.
3. **SQLite vers D1 scolaire :** si le serveur est jumelé, une trame
   AES-256-GCM contenant uniquement l'identifiant R2, la révision,
   la taille et l'empreinte SHA-256 est écrite dans la base
   `scolia-pro-school-sync`. L'outbox du manifeste est persistée
   dans `nuage.sec` avant l'envoi pour permettre une reprise exacte
   après coupure ou redémarrage. Un accusé perdu déclenche une relance
   avec **la même trame chiffrée**, jamais un doublon.
4. **D1 vers le serveur :** le journal est relu par curseur,
   chaque enveloppe étant vérifiée et déchiffrée localement.
   L'enregistrement d'une nouvelle révision ne remplace jamais
   la base SQLite active. Une restauration depuis R2 est une procédure
   distincte et protégée.

Cette synchronisation assure une **relève automatique de données et
sauvegardes**, mais **pas** l'équivalence de deux moteurs de base
multi-maîtres. Pendant l'arrêt du PC, les appareils utilisent leur
SQLite local, déposent des opérations chiffrées quand ils ont Internet,
et obtiennent la validation finale lors du retour du serveur principal.
Il n'est pas encore possible pour deux clients de consulter mutuellement
en temps réel une base scolaire D1 vivante qui encaisse des paiements ;
cela exigerait un backend métier cloud complet, avec contrôles d'accès,
conflits, règles et tests de sécurité.

## Activation et contrôle

- Activation et conservation du code de secours dans le serveur scolaire ;
- appel authentifié, depuis le PC principal uniquement :
  `POST /api/nuage/jumelage-d1` avec `{"confirm":true}` ;
- consultation des progrès (sans secrets) :
  `GET /api/nuage/jumelage-d1`, réponse contenant `paired`,
  `lastMirroredBackupId`, `lastLocalBackupId`, `remoteCursor`
  et `pendingUpload`.

La création réelle de la base D1 est **séparée** du logiciel :
sur la branche de publication, le workflow protégé
`.github/workflows/scolia-d1-school-bootstrap.yml`
lance le script additif `scripts/provision-school-d1.mjs`.
Il cherche d'abord la base, la crée seulement si nécessaire,
applique `school-migrations/0001_school_replica.sql`,
vérifie la présence des tables et **ne déploie pas** de nouvelle
application, ne change pas de forfait ni la facturation.
Ce workflow utilise les secrets GitHub de l'environnement protégé
`scolia-owner-production`, jamais ceux transmis dans une conversation.

La console propriétaire devra être redéployée depuis une branche
compatible avec le binding `SCHOOL_DB` pour que les routes
`/api/v1/server/cloud/mirror/*` soient accessibles. La seule
création de D1 dans Cloudflare ne rend pas la synchronisation
active et n'active aucun jumelage scolaire.

## Sécurité / limites

- Séparation physique `OWNER_DB` (licences) et `SCHOOL_DB` (manifestes).
- Chaque établissement possède un jumelage distinct, avec empreinte du
  serveur, clé de chiffrement inconnue du cloud, jeton scolaire et
  consentement explicite.
- Aucun mot de passe, cookie, identifiant scolaire ou paiement en clair
  n'est transféré dans les manifestes D1.
- Les paiements en attente ne deviennent définitifs qu'après validation
  du serveur et un reçu provisoire n'est pas présenté comme officiel.
- Le journal D1 comporte un plafond conservateur de 1 000 trames
  par établissement. En cas de quota atteint, les sauvegardes locales
  et R2 restent conservées ; aucune suppression silencieuse.
- Les limitations Cloudflare Free s'appliquent à l'ensemble du compte.
  Le plan ne bascule pas automatiquement en abonnement payant.
- Des tests de bout en bout restent requis avec une vraie base D1,
  une vraie console Cloudflare, des téléphones Android, le PC serveur,
  des modifications concurrentes et les scénarios de restauration.

Ne pas confondre le pilote D1/R2 avec l'ancien service Netlify :
Netlify reste **health-only** et n'accepte aucune donnée scolaire.
