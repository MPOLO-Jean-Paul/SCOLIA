# Audit ciblé SCOLIA Pro — fluidité et fonctionnement hors ligne
**Date :** 4 novembre 2026 — Windows (Avalonia), Android (vues Avalonia), stockage local SQLite chiffré

## 1. Constats vérifiés dans les sources

| Point | Avant | Correction apportée |
| --- | --- | --- |
| Naviguer après perte du serveur | Chaque page retentait le réseau, avec un délai HTTP global de 60 s | Circuit hors ligne : lecture immédiate du cache dès la panne connue |
| Première lecture sur réseau coupé | Attente excessive possible avant la copie locale | Limite de 4 s LAN / 7 s relais distant par lecture, puis cache |
| Sonde de reconnexion | Risquait d'hériter du délai HTTP global | Limite de 3 s LAN / 6 s relais distant, sans bloquer la navigation |
| Connexion du dernier compte | Tentative de connexion réseau malgré panne déjà confirmée | Vérification PBKDF2 locale ; dernier compte seul, mot de passe obligatoire |
| Logo pendant l'ouverture | Même cache disponible, téléchargement tenté | Logo local déchiffré dès qu'une coupure est connue |
| Écritures validées pendant une panne | File d'attente créée puis nouvelle tentative réseau bloquante | File durable gardée immédiatement, avis de mise en attente |
| Retour du serveur sans nouvelle écriture | L'écran pouvait conserver son ancienne photographie | Actualisation silencieuse après reconnexion, même sans modification à envoyer |
| Message d'actualisation | « Données à jour » même si copie locale ou échec | Distinction données du serveur, copie locale et échec |
| Liste des apprenants | Un mot recherché hors ligne modifiait l'URL et ne trouvait plus sa page | Filtrage sur les pages du registre précédemment téléchargées ; avertissement si l'index est partiel |
| Accueil desktop et mobile | Pastille « Hors ligne » peu explicite | Bandeau compact, accessible, contrasté, sans masquer les outils et les cartes |

## 2. Mécanisme exact

- Chaque compte reste isolé dans le cache SQLite chiffré de son établissement.
- Les pages déjà consultées sont réutilisables sans réseau. Les requêtes non mises en cache ne sont jamais remplacées par des chiffres inventés.
- Les saisies d'exploitation déjà éligibles au journal hors ligne conservent leurs clés d'idempotence ; un refus au retour du serveur est un conflit à résoudre, non une écriture effacée.
- La détection de retour du serveur et la file d'attente appartiennent à `SyncCenter`; l'interface n'attend pas leur fin.
- L'ordinateur principal local peut servir la base même sans Internet lorsqu'il est allumé et que son serveur local fonctionne.

## 3. Limites qui exigent un autre projet

Ce correctif **n'implémente pas une réplication intégrale** de toutes les données du serveur sur chaque appareil. Les dossiers détaillés, historiques, périodes ou pages jamais consultés ne deviennent pas miraculeusement disponibles hors ligne. Une recherche de la liste des apprenants peut être partielle ; le nombre de dossiers locaux et le nombre connu sur le serveur sont affichés. Les nouveaux PDF, exports et reçus numérotés à émettre par le serveur ne sont pas promis sans son accès.

Pour une garantie de **100 % des fonctionnalités indépendamment du serveur**, il faudrait une réplique locale structurée (SQLite et index de recherche), un journal d'opérations métier et des règles de résolution des conflits validées pour les frais, les paiements, les autorisations et les doubles inscriptions. Ne pas transformer silencieusement un cache de consultation en serveur financier.

## 4. Tests de non-régression ajoutés

1. Lecture d'une page chiffrée depuis le cache en moins de 2 secondes lorsque le serveur ne répond pas ; isolation par compte.
2. Erreur hors ligne rapide pour une page non cachée (pas d'appel de 60 secondes).
3. Écriture du quotidien enregistrée localement sans délai réseau, et clé de reprise conservée.
4. Logo récupéré depuis le cache, sans attendre la connexion.
5. Connexion au dernier compte en mode hors ligne avec empreinte locale du mot de passe.
6. Recherche locale des apprenants : nom, promotion, statut, téléphone, pagination et signalement d'index incomplet.

## 5. Contrôles à faire sur appareils réels avant diffusion

- Windows serveur local sans Internet : naviguer sur accueil, liste, caisse et rapports ; vérifier que le serveur local fonctionne sans accès extérieur.
- Windows client avec câble réseau débranché et Android en mode avion : connexion hors ligne (après une première connexion réussie), accueil et listes précédemment consultés.
- Rechercher un apprenant dont la page a été téléchargée ; vérifier l'avertissement si une partie du registre manque.
- Tester un paiement hors ligne avec utilisateur autorisé : identifier l'opération **en attente**, ne jamais prétendre qu'un reçu a déjà été confirmé.
- Rétablir le réseau : vérifier la reprise, les conflits, les comptes et l'actualisation sans forcer la fermeture de SCOLIA.
- Tester le changement de compte et d'établissement : aucune donnée du compte précédent ne doit apparaître.
- Vérifier les vues sur téléphone étroit, grandes polices et thèmes clair/sombre, sans chevauchement des bandeaux.

## 6. Pistes d'amélioration suivantes, non encore livrées

- « Préparer mon appareil pour le hors ligne » : copie locale complète et incrémentale des dossiers auxquels le compte a accès avec barre de progression et volume estimé.
- Index local FTS, écrans détaillés des apprenants et historique scolaire consultables sans serveur.
- Gestion formelle des brouillons et de la délivrance des reçus après confirmation distante ; cas de conflits et paiements concurrents.
- Préchargement intelligent au Wi-Fi et économie batterie sur Android ; tests de charge et mesures P95 réelles.
- Tests manuels Windows/Android de longue durée et audit d'accessibilité.

**Règle :** ne pas fusionner ou annoncer de nouvelle version installable avant les validations automatiques et la compilation de l'APK et de l'EXE.
