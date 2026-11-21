# SCOLIA Pro — serveur Cloudflare central et expérience Local-First (21/11/2026)

## Point de départ vérifié

- Source applicative de ce chantier : `fix/scolia-1277-desktop-maximized-login-pairing-20261114` (SCOLIA 1.2.77).
- Point d'accès déjà testé manuellement : `https://api.jp-services.org/health`.
- Ressources de test créées dans Cloudflare : Worker `scolia-api`, D1 `scolia-test` (binding `DB`), R2 privé `scolia-files` (binding `FILES`).
- Le Worker `/health` vérifie seulement `SELECT 1` et `R2.list()` : **il ne constitue pas une API scolaire opérationnelle**.
- Les implémentations historiques utilisent encore un serveur SQLite sur le poste de l'établissement et des dépôts/sauvegardes cloud. Le pilote de la branche `feature/cloudflare-sqlite-d1-automatic-sync-20261109` n'implémente **pas** D1 comme serveur métier central. Ne pas confondre sauvegarde R2 et synchronisation bi-directionnelle complète.

## Contrat utilisateur attendu

1. Après un premier appairage et préchargement **en ligne**, un compte autorisé lit ses données localement sans attendre une connexion.
2. Les écritures autorisées sont enregistrées **durablement** dans SQLite avec leur outbox dans la même transaction logique, et sont visibles localement (projection « read-your-writes »).
3. Un déclencheur dès l'écriture et le retour du réseau démarre un envoi différé, sans blocage des écrans. Android WorkManager peut reporter les tâches si Android applique Doze/économie d'énergie.
4. La réception s'effectue par curseur, par établissement et par compte, sans télécharger toute la base à chaque ouverture.
5. Le poste du secrétariat administre les comptes et autorisations mais **n'héberge plus le serveur partagé**. Cloudflare applique les règles d'accès après validation centrale.
6. Les comptes et droits préalablement validés restent utilisables localement pendant une coupure, selon une politique explicite de durée de validité et de révocation ; la révocation distante ne peut pas être instantanée sur un appareil hors ligne.
7. L'interface ne bloque pas l'utilisateur avec un bandeau réseau. Elle signale discrètement le nombre d'opérations en attente et affiche clairement les rares opérations nécessitant validation centrale, notamment les paiements.

## Cohérence des données

- Séparer par `school_id` **toutes** les données et requêtes côté serveur, filtrées sur l'identité authentifiée, jamais sur un identifiant scolaire librement envoyé par le client.
- `operation_id` / clé d'idempotence stable, contrainte UNIQUE sur le serveur et réponse stable en cas de nouvelle tentative.
- Ordre des changements par révision/curseur serveur monotone. Faire la distinction entre copie locale, opération en attente, opération appliquée et conflit.
- Pour les reçus et encaissements : numéro et validation définitifs **uniquement après réponse métier serveur**. Une prévisualisation locale est explicitement provisoire. Ne pas résoudre les conflits financiers par « last write wins ».
- Conserver les suppressions (tombstones) jusqu'à ce que les clients aient obtenu leur révision, avec mécanisme de re-synchronisation complète si l'historique a expiré.
- Persister le curseur de réception uniquement **après** application locale atomique des changements.
- Un changement de compte ne doit pas solder les opérations d'un autre compte ; ne jamais supprimer l'outbox pour « réparer » une erreur réseau.

## Sécurité avant tout essai réel

- Le Worker peut être accessible en HTTPS, **sans données publiques**. Déployer une authentification native (jeton court, renouvellement sécurisé, appareil autorisé), plutôt que demander une page Cloudflare Access aux clients Windows/Android.
- Vérification des permissions et des règles métier à chaque écriture dans le Worker / backend cloud ; la seule copie locale d'un rôle secrétaire ne confère aucun pouvoir au serveur.
- D1 comme base métier centrale après migration contrôlée ; R2 bucket **privé** pour fichiers et sauvegardes chiffrées ; aucune clé R2 ou secret admin intégrée dans une APK.
- Chiffrer et protéger le cache sensible local, les journaux, les sauvegardes et les jetons de session.
- Ne pas charger les données réelles dans `scolia-test`. Préparer un environnement de production distinct, sa stratégie de migration et des restaurations vérifiées.
- Tester l'accès non autorisé, les comptes d'établissements différents, la saturation 429 et la réplication des révocations.

## Plan de livraison — portes de validation

### Phase A — durabilité du client (ce chantier)

- [x] Préserver l'outbox SQLite sur réponses transitoires 3xx/408/425/429/5xx au cours du rejeu.
- [x] Préserver également l'outbox lorsqu'une écriture immédiate reçoit un rejet temporaire.
- [x] Désactiver le suivi silencieux des redirections HTTP du client natif afin d'éviter qu'une page Access 200 soit prise pour un accusé métier.
- [x] Ajouter les tests de non-régression HTTP loopback correspondants.
- [ ] Obtenir la compilation et l'exécution des tests .NET dans GitHub Actions, puis valider sur Windows et Android réels.

### Phase B — backend Cloudflare central (non commencé)

- [ ] Inventorier tables, contraintes, permissions et opérations de SCOLIA 1.2.77.
- [ ] Migrer le modèle métier réellement utilisé vers D1, sans importer de données scolaires dans la base de test.
- [ ] Implémenter API authentifiée, isolation établissement, idempotence, journal de changements, conflits et accès R2 privé.
- [ ] Définir et tester les transactions/quotas Cloudflare sur les paiements et les inscriptions.

### Phase C — clients Local-First cloud (non commencé)

- [ ] Connecter les clients Windows et Android à `https://api.jp-services.org` sans dépendance à un PC central.
- [ ] Gérer premier appairage/préchargement, outbox, push, pull, curseur, retry avec backoff, verrou multi-compte et révocation.
- [ ] Assurer le travail hors ligne sur les écrans préchargés et la projection des écritures en attente.
- [ ] Tests de coupure 4G/Wi-Fi, redémarrage, fermeture Android, changement de compte et conflits réels.

### Phase D — transition en production (non commencé)

- [ ] Sauvegarde vérifiée de SQLite historique, migration dry-run, comparaison des données, période de double lecture.
- [ ] Mise en production progressive par établissement, suivi des erreurs et procédure de retour arrière.
- [ ] Publication d'installateurs et APK après signature officielle et validation humaine.

## Restriction actuelle

Les correctifs de ce chantier améliorent la conservation des écritures de l'architecture **actuelle**. Ils ne remplacent pas encore la source métier par D1 et ne doivent pas être présentés comme une synchronisation cloud centrale déjà disponible.
