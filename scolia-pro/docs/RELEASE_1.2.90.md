# SCOLIA Pro 1.2.90 — relais Cloudflare prioritaire, Render en secours

## Comportement

- Nouvelles liaisons : `wss://relay.jp-services.org/` en relais principal.
- Secours : `wss://scolia-pro-relay.onrender.com/`. Les connexions WSS utilisent la même salle, la même clé de session et les autorisations d'appareil du serveur scolaire. Aucune base métier ne réside sur ces relais.
- La connexion à Cloudflare et l'échange chiffré ont été validés par le test réel `ExternalRelayTests` de la branche `main` (2026-11-23). Cela ne remplace pas un essai sur Android et Windows physiques.
- L'application principale tente de maintenir les deux hébergements WSS indépendamment ; si le relais principal est indisponible, le client essaie Render et peut revenir à Cloudflare.
- Le basculement ne transforme jamais une écriture locale en encaissement définitif sans validation du serveur scolaire.

## Etablissements deja lies

- Les anciens appareils avec Render en primaire **ne sont pas déplacés silencieusement**. L'ancien QR, les secrets et les autorisations restent valables.
- Sur le poste principal non géré par la console, la page Réseau et activité propose Cloudflare en primaire et Render en secours ; le responsable valide le changement via « Activer ou vérifier Internet ». La même identité de jumelage est conservée.
- Un établissement soumis à la console JP-Services doit d'abord approuver la nouvelle révision du fournisseur Cloudflare dans la console propriétaire, puis activer ou vérifier le relais sur le serveur scolaire. Ne pas contourner cet accord par une migration silencieuse.
- Les anciens clients Render restent utilisables via le secours Render. Les nouveaux appareils reçoivent le relais Cloudflare dans leurs invitations à partir du serveur configuré.
- Seule une révocation demandée explicitement supprime un appareil. Ne pas régénérer les secrets simplement pour changer de fournisseur.

## Hors ligne et D1

- SQLite local conserve l'autorité sur les données et paiements. Les opérations en attente subsistent lorsque le serveur et les relais sont indisponibles.
- D1 scolaire conserve les manifestes chiffrés du pilote et R2 les sauvegardes chiffrées ; aucun passage silencieux à un serveur central de paiements.
- Une API D1 complète multi-appareils nécessite encore migration, autorisations métier, tests de conflits et essais réels avant tout transfert de données d'école.

## Validation avant déploiement général

1. Exécuter les tests natifs et les tests de transport sur la branche de version.
2. Rejouer le test WSS public `SCOLIA Cloudflare - test reel WSS sans donnees scolaires`.
3. Sur un PC serveur test : activer Cloudflare, vérifier « Principal connecté », créer une invitation, connecter Android en 4G.
4. Simuler la perte de Cloudflare et vérifier la reprise Render ; rétablir Cloudflare et vérifier sa priorité.
5. Révoquer un appareil de test ; vérifier qu'il ne peut utiliser ni Cloudflare ni Render.
6. Vérifier la persistance d'une inscription hors ligne et l'absence de double reçu après reconnexion.

Ne modifier ni les secrets de production ni la base des écoles pendant ces contrôles.
