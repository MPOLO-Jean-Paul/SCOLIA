# SCOLIA Pro 1.2.35 — connexion simplifiée

## Deux modes seulement pour les utilisateurs

- **Réseau local (Wi-Fi / LAN)** : les appareils sont sur le même réseau privé de l'établissement ; découverte automatique ou saisie d'une adresse IP privée en dépannage. Ce mode local utilise HTTP : ne pas l'employer sur un Wi-Fi public ou non fiable.
- **Internet sécurisé via Render** : les appareils peuvent se trouver sur des réseaux différents (Wi-Fi, Airtel, Orange, Vodacom). Le relais `wss://scolia-pro-relay.onrender.com/` ne conserve pas la base scolaire : le serveur principal et sa base SQLite doivent rester actifs.

Tailscale a été retiré des écrans, de l'assistant de configuration, du QR émis par les nouvelles versions et des mécanismes d'installation/activation Android. Le lecteur de codes et les réglages historiques savent encore reconnaître une ancienne adresse privée pour proposer une migration sûre, **sans tenter de lancer le VPN**.

## Sur l'ordinateur ou le téléphone principal

1. Installer SCOLIA Pro 1.2.35 tout en gardant la même base scolaire.
2. Configurer l'établissement comme auparavant. Il n'y a plus de demande d'installation VPN dans l'assistant.
3. Pour travailler sur le même Wi-Fi, ouvrir le partage du **réseau local** dans « Poste et réseau », seulement sur un réseau de confiance.
4. Pour permettre le travail à distance, ouvrir « Réseau et activité » et cliquer sur **Activer ou vérifier Render**. L'adresse est préconfigurée ; elle n'est modifiable que dans **Réglages avancés**.
5. Ouvrir « Relier un appareil » pour afficher le QR. Il est valable 5 minutes et ne peut être accepté qu'une seule fois.

## Sur le téléphone / autre ordinateur

1. Choisir « Rejoindre votre établissement », puis scanner le QR ou importer/coller le code.
2. SCOLIA essaie le réseau local d'abord, puis le relais Internet sécurisé si le code l'autorise.
3. L'appareil enregistre sa propre autorisation locale et peut se reconnecter ensuite sans nouveau QR tant que son droit n'a pas expiré ou n'a pas été révoqué. La permission reste propre au compte invité.
4. « Connexion sur le réseau local » contient uniquement « Rechercher un serveur proche » et « Saisir une adresse locale ». La saisie directe d'une adresse Internet est refusée : les connexions distantes exigent un code autorisé.

## Si un appareil utilisait Tailscale

Si l'ancienne configuration contient une adresse Tailscale, SCOLIA n'essaie plus de démarrer le VPN. Il tente d'abord le chemin Render **déjà autorisé**. S'il n'a pas de permission Internet mémorisée, il présente l'écran de jumelage. Le responsable doit alors afficher un nouveau QR sur le serveur principal. Aucun effacement volontaire de la base scolaire n'est requis ; sauvegarder les données et terminer les opérations en attente avant une mise à jour importante.

## Limitations

- L'ordinateur principal doit rester allumé et son service SCOLIA doit répondre.
- Render gratuit peut se mettre en veille, ce qui retarde parfois la première connexion.
- En cas de coupure, seules les données préparées pour le hors-ligne et les opérations prises en charge restent accessibles ; la synchronisation est confirmée à la reconnexion.
- La publication d'un APK Android pour remplacer une version existante doit être signée avec **le certificat historique de SCOLIA**, vérifié par GitHub Actions.
- Pour déclarer ce fonctionnement validé sur les réseaux congolais, le tester sur un vrai Windows et un Android, sur Wi-Fi commun puis réseaux différents.

## Vérification de recette

1. Rejoindre par QR sur le même Wi-Fi ; aucune option ni demande Tailscale ne doit apparaître.
2. Passer en réseau mobile, relancer SCOLIA et retrouver Render sans nouveau QR.
3. Tester les opérations hors ligne, la reprise et l'absence de doublons.
4. Révoquer un appareil ; il ne doit pas réussir à se reconnecter.
5. Vérifier que les données scolaires restent sur le serveur local, non sur Render.
