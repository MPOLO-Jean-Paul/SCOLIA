# SCOLIA · Netlify HTTPS de secours (préparation technique)

**Ne pas confondre avec un troisième relais WSS ni un service TURN.** L'application
SCOLIA actuelle utilise WebSocket pour **transporter des requêtes et réponses
chiffrées**, et non seulement pour la signalisation. Une URL Netlify ne peut pas
remplacer le WebSocket ni assurer automatiquement la continuité des opérations.

Ce sous-projet fournit :
- un point de contrôle public HTTPS \`GET /api/scolia-backup/health\` ;
- un protocole de **boîte aux lettres chiffrée expérimental**, limité à des
  trames de 8 KiB, avec identité hôte/client fondée sur les jetons existants,
  nonce par session, expiration courte, limite de file et accusés idempotents ;
- un nettoyage périodique des messages expirés ;
- un état **désactivé par défaut**. Les clients Android et Windows doivent
  disposer d'un adaptateur spécifiquement testé avant toute activation.

## Déployer ce projet Netlify (séparément du Worker Cloudflare)

Sur Netlify, créer un projet à partir de \`MPOLO-Jean-Paul/SCOLIA\`,
sélectionner la branche dédiée après validation et définir le répertoire racine
\`scolia-pro/deploy/netlify-backup\`. Aucun build n'est requis : la publication
est \`public\` et les fonctions sont sous \`netlify/functions\`. Installer les
dépendances depuis \`package.json\`. **Ne pas copier les secrets Cloudflare.**
Vérifier \`https://NOM-DU-SITE.netlify.app/api/scolia-backup/health\` : le
JSON doit porter \`SCOLIA Netlify HTTPS Backup\` avec \`enabled: false\`.

La variable Netlify Functions \`SCOLIA_NETLIFY_MAILBOX_ENABLED\` doit rester
**absente ou \`false\`**. Ne la passer à \`true\` qu'après revue de sécurité
du protocole, mise en place d'une politique d'anti-abus/quotas, tests réels
Android/Windows, consentement explicite par établissement et validation de
l'absence de données en clair. Le code n'enregistre que les métadonnées
techniques de salle et des messages chiffrés; il n'implique pas de transfert
de dossiers d'élèves.

## Confidentialité et limitations

- Pas de jetons, cookies, bases d'élèves ou IP scolaires dans le catalogue de
  la console propriétaire.
- Les seuls messages admis par le pilote sont des **trames opaques** de taille
  bornée et les participants doivent posséder les jetons d'une salle existante.
  Ne pas utiliser ce pilote avec des messages métier non chiffrés.
- Netlify Blobs possède un mode de lecture à cohérence forte, mais n'offre
  pas de transactions multi-objets. Une session est courte (90 secondes),
  chaque message possède une clé unique, les écritures sont conditionnelles
  et la reprise nécessite un contrôle de bout en bout d'idempotence.
- Les opérations dont la réponse dépasse 8 KiB **ne sont pas compatibles**
  avec ce chemin tel quel. Rien ne bascule automatiquement sur ce pilote.
- La disponibilité de Netlify et de Blobs, ainsi que les quotas du forfait,
  doivent être suivis avant une éventuelle exploitation réelle.

Tests sans accès Netlify : \`npm test\`. Les paramètres et données d'accès
sont conservés uniquement dans l'application propriétaire, jamais dans les
appareils scolaires tant que le protocole n'est pas validé.
