# SCOLIA Pro — activer HTTPS sans manipuler de certificat

## Pour l'établissement

Sur l'**ordinateur principal Windows** qui stocke les données scolaires, ouvrir
**Poste et réseau → HTTPS sécurisé automatiquement → Activer HTTPS**.

SCOLIA :
1. Vérifie que cet appareil est bien le serveur, que le compte peut modifier la
   configuration et que Tailscale est installé, actif et connecté.
2. Lit **sa propre identité DNS** auprès du client Tailscale local ; refuse les
   domaines Internet arbitraires.
3. Vérifie l'état de `tailscale serve status --json` pour **ne jamais écraser
   une autre application** utilisant déjà le port HTTPS 443.
4. En cas de port libre, configure `tailscale serve --bg --https=443 http://127.0.0.1:5080`.
   Le port public 443 est disponible **uniquement dans le tailnet**, pas sur
   Internet : **Tailscale Serve**, jamais Funnel.
5. Relit l'état du proxy. L'URL est affichée **seulement si le port 443 redirige
   réellement vers le SCOLIA local**.
6. Génère ensuite dans le **code QR de jumelage** une adresse HTTPS du nom
   `https://<machine>.<tailnet>.ts.net/`, en plus des éventuelles adresses HTTP
   du Wi-Fi historique. Les nouveaux clients SCOLIA utilisent HTTPS en priorité
   et **ne reviennent pas silencieusement à HTTP** si HTTPS est indisponible.

### Ce qui est automatique

- Certificat HTTPS de confiance publique : fourni et renouvelé par Tailscale,
  **aucun fichier PFX ni mot de passe à créer ou à saisir dans SCOLIA**.
- Relance du partage HTTPS après redémarrage : `tailscale serve --bg` est
  persistant et reste géré par le service Tailscale.
- Adresse HTTPS dans les nouvelles invitations QR, après détection du proxy.
- Détection d'un partage déjà actif : pas de nouvelle commande de configuration.

### Accord initial obligatoire

L'administrateur du tailnet doit autoriser **MagicDNS et HTTPS Certificates**.
Tailscale peut demander une première confirmation dans le navigateur. Chaque
appareil client doit être connecté au même tailnet et autorisé par ses règles
d'accès (ACL). SCOLIA conserve ses comptes, ses permissions et son authentification.

Les noms des appareils ayant un certificat public apparaissent dans le
journal de transparence des certificats (CT). Ne pas utiliser de nom
d'appareil comprenant des informations personnelles ou sensibles.

### Limitations et sécurité

- Cette automatisation concerne **l'ordinateur serveur Windows** avec client
  Tailscale CLI disponible. Elle ne configure pas encore le serveur Android.
- Le serveur HTTP SCOLIA continue d'écouter sur localhost. **Tailscale Serve
  termine HTTPS sur l'hôte**, puis transmet au processus SCOLIA en HTTP
  **sur 127.0.0.1 uniquement**, sans transiter par le réseau en clair.
- **Si le partage HTTP sur le Wi-Fi local était déjà activé, il reste ouvert** :
  ce mécanisme ne le désactive pas ni ne crée un certificat pour les IP LAN.
  Pour ne conserver que l'accès chiffré distant, désactiver le partage LAN
  après vérification du chemin HTTPS et du jumelage des appareils.
- Ne jamais activer Tailscale Funnel pour SCOLIA : Funnel publierait le site
  vers Internet, ce que ce parcours ne fait pas.
- Ne pas désactiver la vérification TLS ni accepter un certificat inconnu.
  Aucun certificat autosigné n'est importé dans Android.
- En l'absence de Tailscale, de MagicDNS/HTTPS, ou si le port 443 est occupé,
  SCOLIA affiche le problème et **ne modifie rien**.
- Une installation déjà en production conserve son réseau existant jusqu'à
  l'action volontaire d'un responsable. Le bouton n'installe rien en arrière-plan.
- Un téléphone qui n'a pas Tailscale actif ne peut pas utiliser l'URL HTTPS
  privée : cela n'est pas une défaillance du certificat.

## Références techniques

- https://tailscale.com/docs/features/tailscale-serve
- https://tailscale.com/docs/reference/tailscale-cli/serve
- https://tailscale.com/docs/how-to/set-up-https-certificates

Cet ajout reste distinct de la configuration HTTPS native Kestrel avec PFX :
celle-ci est toujours disponible pour les sites disposant de leur propre
certificat et de leur domaine.
