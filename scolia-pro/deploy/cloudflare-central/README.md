# SCOLIA — fondation du serveur central Cloudflare (pilote technique)

**Version source :** SCOLIA Pro 1.2.86.
**Environnement de test existant :** Worker `scolia-api`, D1 `scolia-test` (binding `DB`), bucket R2 privé `scolia-files` (binding `FILES`), `https://api.jp-services.org`.

## Ce qui est opérationnel et ce qui ne l'est pas

Le test `/health` du Worker a déjà confirmé des connexions à D1 et R2, **mais il ne valide ni connexion de compte, ni paiement, ni synchronisation scolaire**.

Le script `migrations/0001_core.sql` prépare la **structure centrale minimale** nécessaire à un futur service métier :
- écoles séparées ;
- utilisateurs et rôles liés à leur école ;
- appareils liés au compte utilisateur autorisé par un responsable ;
- opérations identifiées de façon stable avec contrainte d'idempotence ;
- journal chronologique de changements validés et curseurs par appareil.

Chaque table et relation est prévue pour empêcher une référence inter-établissements. La **vérification cryptographique des identités, des permissions, des révocations et des règles métier incombe au Worker** ; ces tables ne suffisent pas à assurer la sécurité à elles seules.

Le journal ne contient pour l'instant que des métadonnées d'événements et leurs empreintes, **aucune donnée d'apprenant, montant ou note**. Il ne permet pas encore de reconstituer l'état métier.

**Ce dossier n'est pas automatiquement déployé dans le compte Cloudflare de l'établissement.** En particulier, ne pas exécuter la migration sur une base comportant des données réelles avant les contrôles de compatibilité et de restauration.

## Vérifier les migrations hors production

Depuis la racine du dépôt :

```sh
python3 -m unittest discover -s scolia-pro/tests -p 'test_cloudflare_central_schema.py' -v
```

Tests réalisés sur une vraie base SQLite temporaire en mémoire, sans réseau, couvrant :
- idempotence par école ;
- appareil/auteur et identité de même établissement ;
- refus des événements tant que l'opération n'a pas été appliquée ;
- impossibilité d'associer un événement à l'opération d'une autre école ;
- curseurs et identités isolés ;
- réexécution additive du script.

Un workflow GitHub Actions exécute également ces tests à chaque modification du dossier.

## Portes de validation avant activation de l'API

1. Inventorier **toutes** les données métier réelles du modèle EF Core 1.2.86 et définir une migration complète vers D1.
2. Concevoir les sessions natives, les jetons de renouvellement et l'approbation des appareils, sans placer de secret central dans un APK.
3. Implémenter la gestion des comptes du secrétariat avec droits vérifiés côté cloud ; propagation des révocations.
4. Développer les API métier des inscriptions, notes, présences et surtout des paiements (atomiques, idempotents, conformes aux règles SCOLIA existantes).
5. Implémenter `push/pull` des changements validés, pagination par révision, tombstones et traitement des conflits.
6. Mettre à jour les clients Android/Windows pour qu'ils utilisent leur SQLite locale et se synchronisent directement avec `api.jp-services.org`.
7. Tester migrations/restaurations, montées en charge, coupures Internet, redémarrages, paiements concurrents et isolation multi-établissements avant toute migration réelle.

Le PC du secrétariat restera **poste d'administration et cache local** ; l'objectif est que Cloudflare devienne serveur central indépendant du PC, mais ce basculement **n'est pas encore réalisé**.

## Coûts et prudence

- D1 / R2 / Workers ont des quotas : les surveiller et prévoir un mode dégradé sans perte des données.
- Ne pas activer de route générique non authentifiée exposant des tables scolaires.
- Ne pas rendre public le bucket R2.
- Aucun utilisateur ne doit obtenir un reçu définitif pendant une coupure sans acquittement du serveur central.
