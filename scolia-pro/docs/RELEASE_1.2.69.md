# SCOLIA Pro 1.2.69 — Déconnexion Android stable et écrans d'accès affinés

## Correctif de déconnexion

- **Problème constaté en 1.2.68** : un clic sur « Se déconnecter » pouvait afficher « l'application a cessé de fonctionner », particulièrement lorsqu'une requête de fin de session échouait (API, délai réseau, accès hors ligne).
- **Correction** : le client arrête la synchronisation et la reconnexion du compte avant de fermer la session ; une déconnexion distante qui ne répond pas est bornée à 5 secondes. Même en cas d'échec réseau, le compte courant est fermé localement et la page de connexion réapparaît.
- L'événement de déconnexion mobile intercepte les exceptions imprévues et conserve l'application ouverte.
- La déconnexion est idempotente : des clics répétés ne lancent pas plusieurs fins de session.
- **Jumelage préservé** : le code QR autorise l'appareil, et non la session individuelle. Se déconnecter n'efface plus cette autorisation ; les comptes enregistrés restent proposés sans connexion automatique et l'identifiant + mot de passe demeurent obligatoires.
- Mot de passe et champs de récupération sont réinitialisés à la fermeture de session, tandis que les données scolaires locales ne sont pas effacées.

## Harmonisation native avant l'accueil

- Connexion, reprise/verrouillage de session, jumelage, connexion directe et configuration Android : même couleur de curseur, même sélection de texte discrète et contraste par le thème SCOLIA.
- Les classes responsive compact/tiny/micro/wide sont actualisées dès l'attachement visuel, évitant une première disposition non adaptée lors du retour à la connexion.
- Aucun changement d'architecture, de parcours de sécurité, de style des pages, de signature APK ou de base de données.
- Tests natifs de déconnexion hors réseau, de double déconnexion et de cohérence des styles d'accès.

## Distribution

- Windows / Android **1.2.69**, Android versionCode **189**.
- Builds et publication via la distribution officielle ; APK signé avec le système de signature historique documenté depuis PR #18, fichiers vérifiés SHA-256.
