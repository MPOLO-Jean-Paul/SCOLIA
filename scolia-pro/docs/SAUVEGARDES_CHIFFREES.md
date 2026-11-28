# SCOLIA Pro — sauvegardes chiffrées portables

## Nouveaux parcours

Dans **Données et sauvegardes**, sélectionner une copie puis **Exporter chiffré**. SCOLIA vérifie d'abord l'intégrité du fichier SQLite et son empreinte SHA-256, demande une phrase secrète de 12 caractères minimum et enregistre le fichier `scolia-….db.scolia-enc` dans les téléchargements.

Pour récupérer cette archive sur un autre poste autorisé, ouvrir **Importer chiffré**, choisir le fichier `.scolia-enc`, saisir la phrase secrète et laisser SCOLIA vérifier le fichier. L'import crée **une nouvelle copie de sauvegarde**, sans écraser la base active. Une restauration ultérieure nécessite l'action de restauration ordinaire et sa confirmation.

## Protection cryptographique

- AES-256-GCM, nonce aléatoire de 96 bits et authentification de l'en-tête de version.
- Dérivation de clé PBKDF2-HMAC-SHA256, 600 000 itérations et sel aléatoire de 128 bits.
- Une corruption ou un mauvais mot de passe entraîne le refus de déchiffrement ; une base SQLite non intègre est rejetée.
- Le mot de passe n'est jamais écrit dans le dépôt, les fichiers de configuration ou le journal d'audit.
- Une archive peut être copiée hors du serveur ; le mot de passe doit être conservé **séparément**, dans un coffre sécurisé.
- Cette première version de l'export chiffré accepte des bases jusqu'à **48 Mio** pour maîtriser la mémoire des téléphones. Pour une base plus volumineuse, prévoir un format de chiffrement par flux et une procédure adaptée.

## Restriction des liaisons

Les opérations d'export/import chiffré exigent HTTPS ou un appel depuis le serveur lui-même (loopback). Sur un téléphone client connecté au serveur via HTTP LAN, elles seront refusées : basculer d'abord sur un transport HTTPS approuvé, ou réaliser l'opération localement sur le serveur. Ne jamais contourner la vérification TLS pour transmettre une phrase secrète.

## Compatibilité et limites explicites

- Le format de l'archive chiffrée est distinct du SQLite non chiffré. Ne pas renommer une archive .scolia-enc en .db.
- Les **sauvegardes automatiques existantes restent en SQLite** pour préserver la restauration des versions historiques ; elles ne sont **pas automatiquement chiffrées** par cette modification. Le dossier de sauvegardes doit rester accessible uniquement au compte du serveur, surtout sur un poste partagé.
- La commande « Télécharger » existante exporte toujours le fichier SQLite brut. Pour sortir une copie du poste, préférer **Exporter chiffré**. La suppression de l'export brut distant exige une migration séparée.
- Ni HTTPS ni les sauvegardes chiffrées ne remplacent la sauvegarde hors appareil, les droits d'accès et les exercices de restauration.
- Aucun secret de restauration n'est synchronisé automatiquement. La perte de la phrase secrète signifie l'impossibilité de déchiffrer l'archive.

## Endpoints pour intégrations autorisées

Requêtes authentifiées et protégées par le header applicatif `X-Scolia` :
- `POST /api/sauvegardes/{name}/export-chiffre` : corps JSON `{"passphrase":"..."}`, réponse `application/octet-stream`.
- `POST /api/sauvegardes/import-chiffre` : formulaire multipart contenant `archive` et `passphrase`, réponse décrivant la copie importée.

Ne jamais mettre la phrase secrète dans une URL, une issue GitHub, un code QR ou un outil de journalisation.
