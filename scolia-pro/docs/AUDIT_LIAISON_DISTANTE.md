# État de référence et architecture de la liaison distante

Référence publiée : `b858631080725e8b7367fa4266537b89282c7db7`, branche `codex/mobile-desktop-fluidity`. Sauvegarde locale complète avec manifestes SHA-256 et historique git dans `/workspace/SCOLIA-sauvegarde-avant-distance`. Aucun dossier de données réel fourni, aucune base utilisateur modifiée.

## Périmètre confirmé par le propriétaire

Uniquement Secrétariat et Comptabilité. Aucun nouveau compte enseignant, administrateur ou direction. Les modules scolaires et personnel existants restent présents, sans nouveau rôle. Conserver réseau local, découverte UDP et Wi-Fi. Ajouter la longue distance.

## Inventaire constaté dans le code

- .NET 10, Avalonia 12.1.4, application Android native qui partage les vues du client Windows. Le frontend React/TypeScript est distinct ; ni PWA ni Capacitor ne pilotent l’application Android.
- Serveur ASP.NET Core embarqué sur Android (`PhoneServer`/`EmbeddedServer`) ou Windows ; EF Core/SQLite local (`ScoliaDbContext`, migrations versionnées). Aucune dépendance PostgreSQL/Supabase trouvée dans ce chemin.
- Installation, établissement, années, promotions et barèmes ; apprenants, matricules, inscriptions/réinscriptions, photos, cartes et lots ; échéances, comptes, prises en charge ; paiements par mois/par lot, reçus, annulations, dépenses et arrêtés ; rapports, débiteurs et suivis mensuels ; cours, attributions, évaluations, résultats, bulletins, présences et discipline ; personnel, pointage et paie ; import Excel historique, exports, PDF, sauvegardes, restauration, intégrité, journal d’audit chaîné, utilisateurs et mots de passe.
- Les routes réelles sont dans `ScoliaEndpoints.cs` et `LiveEndpoints.cs`. Authentification cookie + validation de la sécurité du compte à chaque requête ; autorisations serveur par permission. Deux rôles attribuables (`Permissions.Assignable`), anciens rôles conservés pour migration.
- `ScoliaClient` gère les cookies, le CSRF, les identifiants d’opération, le cache local et la reprise. `SyncCenter` utilise SSE, battements de présence et verrous. La découverte LAN reste indépendante.
- Hors ligne initial : fichiers JSON de pages, de session et de file d’attente, identifiants UUID. Les pages jamais ouvertes et certains réglages nécessitent toujours l’appareil principal. Ce n’est pas une réplique intégrale indépendante de la base scolaire.

## Risques démontrés

- File JSON et conflits dans des fichiers séparés : déplacement non transactionnel lors d’une coupure. Données du cache non chiffrées.
- Reprise des opérations non filtrée par utilisateur dans `FlushAsync` : un autre compte pouvait essayer une opération du précédent.
- Idempotence HTTP sauvegardée après l’écriture et purgée après sept jours ; le paiement possède déjà sa propre clé unique dans SQLite, mais le client doit transmettre la même clé dans le corps pour couvrir une réponse perdue avant l’écriture du cache HTTP.
- Nouvelle régression de compilation Android du thème sombre : ambiguïté du nom `Application`, corrigée en qualifiant `Avalonia.Application`.

## Architecture retenue

L’appareil principal conserve la base scolaire SQLite et ses transactions métier, les références séquentielles et l’audit existants. Le second appareil conserve son cache et sa file durable locale ; le même moteur de reprise fonctionne en LAN et à distance. Cela évite une fusion arbitraire de paiements concurrents et des collisions de matricules. Pas de réécriture multimaster des règles financières.

Une liaison sortante WSS transporte des requêtes et réponses HTTP chiffrées de bout en bout (AES-256-GCM, contexte par requête et séquence). Le relais ne connaît ni les clés applicatives ni les données, ne stocke aucun message et ne sert aucun dossier scolaire. Deux connexions sortantes traversent les réseaux mobiles/CGNAT sans ouvrir le port de la base. Les comptes et autorisations sont toujours vérifiés par l’API scolaire.

Choix de compatibilité : transport WSS natif .NET pour cette intégration Avalonia Android/Windows ; pas de bibliothèque WebRTC/Capacitor/Kotlin ajoutée sans validation de sa prise en charge. Aucun STUN/TURN nécessaire au transport WSS. Ce transport est un relais chiffré, pas une prétendue connexion WebRTC directe. FCM ne peut être activé sans projet Firebase et identité serveur ; ne pas inventer de secrets ni annoncer des notifications opérationnelles.

La disponibilité de l’appareil principal reste nécessaire pour confirmer les écritures. Sans lui, les opérations éligibles restent en attente. L’écran reste utilisable. Les limites Android (Doze, arrêt forcé, téléphone éteint) doivent être affichées et documentées. Aucune garantie de synchronisation instantanée en arrière-plan.
