# SCOLIA Pro — relier les téléphones et ordinateurs par Tailscale

## Ce que Tailscale fait et ne fait pas

Tailscale crée un **réseau privé chiffré** entre des appareils approuvés (tailnet). Les données de SCOLIA restent dans la **base locale de l'appareil qui héberge l'établissement**, par exemple un téléphone Android. Aucune base scolaire n'est hébergée dans Tailscale ; Tailscale utilise néanmoins ses services de coordination et, si nécessaire, des relais de paquets chiffrés. Les comptes SCOLIA, les permissions et les sauvegardes restent obligatoires.

- Mode téléphone principal : SCOLIA lance son serveur local, accessible sur le port TCP 5080. Les autres appareils entrent l'adresse privée de ce téléphone (IP Tailscale ou nom MagicDNS complet).
- Mode client : les appareils secondaires rejoignent **le même établissement** sur cette adresse ; le serveur fait autorité pour les opérations en ligne. Les opérations gardées hors ligne sont réconciliées par les mécanismes SCOLIA existants.
- Les annonces UDP Wi-Fi ne traversent pas automatiquement Tailscale : pour joindre un appareil à distance, utiliser **Rejoindre par Tailscale** et son adresse privée. Ne pas activer l'accès Internet public, ne pas ouvrir/forwarder le port 5080 sur le routeur.
- Les deux appareils doivent avoir une connexion Internet, le VPN Tailscale actif et des autorisations réseau adaptées.

## A. Installer et autoriser

### Parcours d'installation depuis SCOLIA

- Sur l'écran de première liaison du **téléphone serveur**, toucher **Installer le relais privé Tailscale**. Ce bouton ouvre le Play Store officiel (Internet indispensable pour cette installation) ou Tailscale s'il est déjà installé. Installer et ouvrir Tailscale avec confirmation de l'utilisateur ; relier cet appareil au tailnet de l'établissement.
- Configurer SCOLIA sur ce téléphone comme serveur. La base scolaire demeure locale et le service SCOLIA écoute sur TCP 5080.
- Depuis une session SCOLIA autorisée sur le téléphone serveur, ouvrir **Réseau et activité > Partager via Tailscale · autoriser le VPN**. SCOLIA ouvre Tailscale ; dans Tailscale, activer le réseau et accepter la boîte de dialogue VPN Android. Ce bouton **ne peut pas autoriser le VPN à la place de Tailscale**, ni garantir qu'il est actif : tester la liaison avec un appareil client.
- Installer Tailscale sur les téléphones clients et les faire approuver dans le même tailnet. Dans SCOLIA client, renseigner l'IP Tailscale du serveur suivie de **:5080** et sélectionner **Rejoindre par Tailscale**.
- Après autorisation, Tailscale peut rester connecté en arrière-plan sans réouverture constante de son écran, mais Android affiche toujours son indicateur VPN et peut signaler le service actif. L'utilisateur peut couper le VPN dans les réglages. Ne jamais présenter cet indicateur comme masquable.
- Le **relais WebSocket WSS** de SCOLIA est un autre mécanisme qui nécessite un relais séparé ; il n'est **pas nécessaire** pour une liaison Tailscale. Ne pas activer ou annoncer les deux mécanismes comme interchangeables automatiquement.
- L'installation Internet initiale de Tailscale ne doit **pas** empêcher SCOLIA de continuer à fonctionner hors ligne sur les réseaux locaux déjà configurés.


Dans **SCOLIA > Rejoindre votre établissement > Réseau privé Tailscale**, le bouton **Installer ou ouvrir Tailscale** lance l'application officielle si elle est installée, sinon ouvre sa page d'installation officielle. Android demandera toujours l'accord de l'utilisateur pour installer l'application et activer son VPN. SCOLIA ne contient pas le moteur VPN Tailscale et ne stocke aucune clé de connexion Tailscale.

1. Depuis chaque téléphone Android, installer Tailscale via https://tailscale.com/docs/install/android, accepter sa configuration VPN et se connecter au **même tailnet** avec un compte autorisé.
2. Depuis chaque PC Windows, installer et ouvrir https://tailscale.com/docs/install/windows, puis se connecter au même tailnet.
3. L'administrateur du tailnet active l'approbation des appareils, si souhaitée : https://tailscale.com/docs/features/access-control/device-management/device-approval. Dans l'onglet **Machines**, examiner chaque appareil marqué **Needs approval**, puis choisir **Approve**. N'approuver que les appareils reconnus.
4. Vérifier les **grants/ACL** : n'autoriser que les postes scolaires autorisés à atteindre le serveur SCOLIA sur **TCP 5080**. Ne pas partager de clés, d'invitations, de mots de passe ni de captures des comptes.
5. Dans la liste **Machines** de Tailscale, noter l'IP privée du téléphone principal (par exemple `100.110.22.33`, exemple fictif) ou son nom MagicDNS complet `nom.telecole.ts.net` (exemple fictif).
6. Garder une seule connexion VPN active sur Android. Dans les réglages Android **VPN**, activer **VPN permanent / Always-on VPN** pour Tailscale si disponible ; SCOLIA doit rester inclus dans le trafic Tailscale. Tester la connectivité après redémarrage.

## B. Garder le téléphone principal en mode serveur

1. Dans SCOLIA sur le téléphone principal, choisir **Configurer cet appareil comme serveur** / **Utiliser ce téléphone**. La base scolaire locale n'est pas déplacée.
2. SCOLIA utilise un service Android de premier plan (**notification persistante « SCOLIA · serveur actif »**) pour maintenir le serveur local. Cette fonctionnalité nécessite l'accord de l'utilisateur et le bon fonctionnement de l'appareil.
3. Autoriser les notifications de SCOLIA. Garder Tailscale connecté. Sur les téléphones dont l'interface propose la gestion de la batterie par application, autoriser le fonctionnement en arrière-plan de **SCOLIA** et **Tailscale** si nécessaire. Vérifier l'autonomie : l'utilisation prolongée du serveur consomme batterie et données mobiles.
4. Le service tente désormais de **restaurer le serveur local** si Android recrée le service après une récupération mémoire. Ce n'est **pas** une garantie après arrêt forcé, redémarrage, restrictions du constructeur, coupure Internet ou batterie déchargée. Dans ces cas, l'utilisateur doit éventuellement rouvrir SCOLIA.
5. Pour une disponibilité sans interruption destinée à plusieurs utilisateurs, privilégier un **ordinateur toujours allumé** comme serveur local plutôt qu'un téléphone.

## C. Joindre SCOLIA depuis un autre appareil

1. Ouvrir SCOLIA → **Rejoindre votre établissement** → **Réseau privé Tailscale**.
2. Entrer `100.110.22.33:5080` ou `nom.telecole.ts.net:5080`, en remplaçant ces exemples par la véritable IP/le véritable nom affichés dans Tailscale.
3. Appuyer sur **Rejoindre par Tailscale**. SCOLIA vérifie que le serveur scolaire répond avant de mémoriser sa liaison.
4. Se connecter avec un **compte SCOLIA autorisé** (Secrétariat, Comptabilité, etc.). L'appareil ne doit pas être configuré avec une autre base scolaire par erreur.

## D. Synchronisation et limites

- **Application ouverte** : le flux de présence, les événements en direct et les écritures continuent à utiliser les mécanismes SCOLIA. Leur délai dépend du réseau et de la disponibilité du serveur.
- **Application cliente fermée** : Android WorkManager peut effectuer une reprise périodique, normalement pas plus fréquemment que toutes les 15 minutes et à la discrétion du système. Ce mécanisme n'offre **pas** de synchronisation instantanée permanente.
- **Téléphone serveur verrouillé** : la notification de service visible contribue à maintenir le serveur en vie, mais Android peut le suspendre ou l'arrêter. La restauration est tentée quand Android redémarre le service.
- **Coupure réseau** : les données gardées localement et la file d'attente continuent à s'appliquer ; les appareils se resynchronisent quand ils se reconnectent. Ne pas modifier manuellement deux copies indépendantes de la base de l'école.
- Le VPN Tailscale chiffre le transport, **il ne remplace pas** l'authentification, les autorisations des comptes SCOLIA, les sauvegardes ou la protection physique des téléphones.
- Si Windows héberge le serveur, vérifier que `SCOLIA-Serveur.exe` écoute sur l'interface réseau et que le **pare-feu Windows permet TCP 5080 seulement depuis le réseau Tailscale approuvé**, pas depuis Internet public.

## E. Vérifications

- Dans Tailscale, les appareils s'affichent **connectés** et **approuvés** dans le même tailnet.
- Depuis le client, la page SCOLIA **Rejoindre par Tailscale** doit reconnaître le nom réel de l'établissement.
- Effectuer un petit enregistrement test autorisé ; vérifier qu'il apparaît depuis l'autre appareil, que le journal et les paiements ne se dédoublonnent pas et que l'opération hors ligne passe après reconnexion.
- Verrouiller le téléphone serveur et tester depuis un autre appareil après quelques minutes ; répéter après redémarrage pour connaître le comportement réel du modèle Android.
- Ne pas diffuser les IP, identifiants ou codes d'invitation de l'école dans des canaux publics.

**Références :** https://tailscale.com/docs/how-to/connect-to-devices — https://developer.android.com/develop/background-work/services/fgs/restrictions-bg-start — https://developer.android.com/develop/background-work/services/fgs/timeout
