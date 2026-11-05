# SCOLIA Pro — consolidation préalable à la version 1.2.18

Ce document décrit une **branche de validation**, pas une version déjà publiée.
La version actuelle du projet reste 1.2.17 tant que les contrôles et les certificats ne sont pas prêts.

## Améliorations rassemblées

- Android : publication officielle avec la clé historique conservée dans les secrets GitHub Actions ; plus de récupération automatique de clé depuis le cache CI.
- Windows : contrôles Authenticode sur les deux exécutables et sur l'installateur officiel. Le certificat est requis, sinon la publication officielle est bloquée.
- Finance : encaissements partiels par mensualité, affectation aux échéances les plus anciennes, solde restant et véritable référence opérateur pour Mobile Money/chèque.
- Réseau : HTTPS activable avec certificat serveur valide ; le HTTP historique n'est pas supprimé tant que les clients ne sont pas migrés.
- Sauvegardes : archivage portable chiffré, import contrôlé dans une nouvelle copie, restauration distincte et permissions d'accès renforcées.
- Hors ligne : assistant de préchargement volontaire des fiches, index, années, promotions et comptes autorisés dans le cache SQLite chiffré.
- Android client : programmation d'une tâche unique WorkManager lorsqu'une opération est conservée dans la file en attente.

## Contrôles indispensables avant publication

1. CI des projets .NET, API, Avalonia, web et Android, plus tests métier, migrations et captures.
2. Vérifier les règles de paiements partiels, le refus des doublons et la résolution humaine des conflits simultanés.
3. Tester sur un Android 16 et un Windows 11 réels : installation, mise à jour depuis 1.2.17, reconnexion, mise en veille, coupure de réseau, sauvegarde et restauration.
4. Vérifier le transport HTTPS et les certificats sur un établissement **de test** avant une bascule du réseau productif.
5. Confirmer que les secrets `SCOLIA_ANDROID_KEYSTORE` et `SCOLIA_ANDROID_KEYSTORE_PASS` contiennent exactement la clé Android historique, et vérifier la même empreinte publique SHA-256.
6. Fournir un certificat de signature de code Windows reconnu, et tester `signtool verify /pa /all /v` après l'horodatage.
7. Auditer les anciennes révisions Git pour des caches réels et décider d'une purge contrôlée avant une diffusion large.
8. Avant le premier tag 1.2.18, mettre à jour ensemble les numéros de version des assemblages, l'Android VersionCode et les éventuelles métadonnées iOS.
9. Publier uniquement depuis la branche officielle protégée, avec une sauvegarde et un plan de retour arrière.

## Limites architecturales explicites

Cette consolidation **n'introduit pas** une réplication multi-maître P2P autonome. La confirmation d'un paiement et l'émission du numéro de reçu restent centralisées sur l'appareil principal de l'établissement. Android ne garantit pas de réveil immédiat après arrêt forcé ou optimisation batterie. Le dossier de sauvegardes automatiques utilise encore le format historique brut et doit être protégé par les droits système.

Les PR #51, #52, #53, #54, #58, #59 et #60 restent des modifications consultables séparément ; cette branche les rapproche pour des tests de compatibilité et une validation finale.
