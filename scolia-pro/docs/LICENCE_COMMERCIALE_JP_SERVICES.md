# SCOLIA Pro — Architecture de licence et continuité scolaire

## Responsabilités
JP‑Services gère un **établissement serveur** et les licences de cet établissement ; l'administrateur de JP‑Services n'a pas accès aux comptes internes, aux apprenants, aux paiements ou aux données stockées localement.

Le serveur scolaire conserve les données ; il interroge le service de licences par HTTPS, en restant utilisable hors ligne pendant les fenêtres réseau. L'API scolaire est **l'autorité effective** sur les écritures, et pas le client Avalonia, pour éviter qu'un poste lié n'échappe aux contrôles.

## Calcul
La facturation ne démarre que par une action explicite de JP‑Services. Le calcul combine `billing_enabled`, `trial_days`, `created_at`, `paid_until` et `grace_until`. Une dérogation manuelle temporaire (`allow`) peut lever un verrou automatique ; `read_only` bloque les écritures immédiatement, même avant l'échéance. La suppression de la décision manuelle rétablit les règles automatiques.

- Si la licence est complète : lecture et modifications autorisées.
- Si la licence est suspendue ou échue : accès à l'authentification, au réseau et aux dossiers en lecture seule ; création, modification et suppression interdites sur le serveur et sur tous les postes.
- Si le service de licence est momentanément hors ligne : utiliser la **dernière décision** validée, avec fenêtre de grâce limitée pour un droit antérieurement valide ; ne jamais transformer automatiquement un refus explicite en accès complet.
- Les opérations déjà en file restent en attente et ne deviennent pas des conflits ; elles sont renvoyées seulement après réactivation de la licence.
- Les données, le système de sauvegarde local et l'historique scolaire ne sont pas supprimés par une restriction commerciale.

## Chemin utilisateur
- **Paramètres → Licence SCOLIA Pro**, disponible sur le serveur avec autorisation d'administration scolaire : inscription volontaire du serveur et statut.
- **Barre inférieure** : badge de licence à côté du thème et nombre de jours restants, quand le serveur a une licence payante.
- **Appareils liés** : voient le statut et l'avertissement, mais ne peuvent pas gérer la licence commerciale.
- **Console propriétaire** : tableau de bord, facture active, renouvellement, jours d'essai, dérogations 7/30 jours, lecture seule immédiate et levée des mesures.

## Déploiement
Toutes les fonctionnalités nécessitent des tests et un serveur propriétaire réel ; le simple dépôt d'un fichier CI n'installe pas Cloudflare. Garder séparés l'URL publique HTTPS du service et ses secrets : seul le service connaît le jeton propriétaire, le serveur scolaire connaît uniquement son jeton établissement. Le statut local doit être signé par le service distant authentifié et protégé par les mécanismes TLS et de chiffrement du serveur.
