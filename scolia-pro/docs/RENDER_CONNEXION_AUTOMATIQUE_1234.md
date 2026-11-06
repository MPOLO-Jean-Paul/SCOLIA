# SCOLIA Pro 1.2.34 — connexion distante avec Render

## Principe

SCOLIA conserve le serveur métier et la base scolaire SQLite sur l'ordinateur (ou téléphone) principal. Le **relais chiffré WSS** Render `wss://scolia-pro-relay.onrender.com/` transmet les requêtes chiffrées entre les appareils : ce n'est **ni un hébergement de la base**, ni un VPN, ni une liaison WebRTC/P2P directe. Les appareils n'ont plus besoin d'être sur le même Wi-Fi ni d'avoir Tailscale pour communiquer par Internet, à condition que le serveur SCOLIA principal soit allumé, que son service fonctionne et que la connexion Internet soit disponible.

Le relais Render est actuellement en **offre gratuite de test** et peut s'endormir après inactivité. La première requête peut donc être lente. Pour un service scolaire permanent, prévoir une instance sans mise en veille et des contrôles de disponibilité.

## Configuration manuelle, une seule fois sur le serveur principal

1. Sur l'ordinateur qui contient la base SQLite, installer la nouvelle version signée de SCOLIA Pro et se connecter avec un compte autorisé à gérer les paramètres.
2. Ouvrir **Réseau et activité → Connexion Internet sécurisée · Render**. L'adresse `wss://scolia-pro-relay.onrender.com/` est renseignée automatiquement. Il est possible de la remplacer si l'établissement change de fournisseur.
3. Cliquer **Activer ou vérifier la liaison Render**. SCOLIA vérifie `https://scolia-pro-relay.onrender.com/health`, sauvegarde la configuration de manière chiffrée et ouvre une connexion **sortante** WSS. Il n'y a pas de port Internet entrant à ouvrir sur le routeur et pas de Tailscale à installer pour cette voie.
4. Dans **Poste et réseau → Relier un appareil**, générer le QR privé. Le ticket qui autorise la première session dure **5 minutes**, ne fonctionne qu'une fois et doit être transmis en privé au bon appareil. Ce n'est pas un code permanent.

## Configuration manuelle, une seule fois sur le téléphone ou autre PC

1. Installer **la même version 1.2.34**. Choisir **Rejoindre votre établissement**.
2. Scanner le QR (ou coller le code complet sur Windows). SCOLIA essaie les adresses locales approuvées, Tailscale si disponible, puis Render WSS. Si Tailscale HTTPS annoncé ne répond pas, SCOLIA peut passer à **WSS Render**, jamais rétrograder silencieusement à HTTP non chiffré.
3. Accepter l'invitation : SCOLIA mémorise localement le secret propre à cet appareil et au serveur, protégé par DPAPI Windows ou Android Keystore. Si la première session passe par le LAN, le même QR mémorise le chemin Render comme **secours autorisé**.
4. Pour les lancements suivants, l'appareil tente sa connexion mémorisée, et si le LAN ne fonctionne pas il vérifie le chemin Render chiffré. La reprise peut se faire sans Tailscale et sans nouveau QR. L'authentification repose toujours sur l'autorisation de l'appareil ; un appareil non approuvé reste bloqué.

## Validité, sécurité et révocation

- Le QR d'installation : **5 minutes, usage unique**.
- L'autorisation d'appareil : **180 jours**, renouvelés silencieusement lors d'une connexion autorisée dans les **30 derniers jours** de sa validité ; une révocation ou un changement de compte invalidant son `SecurityStamp` reste prioritaire. Après 180 jours sans connexion, un nouveau QR est nécessaire.
- La liaison chiffrée au relais : **365 jours** pour toute nouvelle configuration, renouvelable ensuite par l'administrateur. Une liaison historique de 30 jours conserve sa date existante jusqu'à son renouvellement : elle n'est pas secrètement prolongée.
- Ne pas transmettre d'invitations, secrets, bases ou captures de QR dans des salons publics ou dans GitHub. Désactiver Render sur le serveur coupe la liaison distante ; cette option ne supprime pas les données locales. La sécurité d'un appareil volé nécessite une révocation depuis **Appareils autorisés**.
- Les modes **Wi-Fi local**, **adresse LAN**, **Tailscale privé** et **Internet Render** restent disponibles ; la connexion Render ne nécessite ni partage de Wi-Fi ni VPN sur les appareils.
- Une coupure Internet ou l'arrêt du serveur principal suspend les opérations distantes. La file SQLite chiffrée peut continuer à stocker les opérations éligibles et reprendre ensuite ; une opération en attente ne doit jamais être affichée comme confirmée.
- Sur Android, le mode serveur et les tâches en arrière-plan restent soumis aux restrictions batterie et services Android. Render ne les supprime pas.

## Tests avant de déclarer la version prête

1. Dans le tableau de bord Render, vérifier que `scolia-pro-relay` est **Live** et que `/health` répond.
2. Appairer un appareil via QR sur le Wi-Fi, puis quitter le Wi-Fi et utiliser une connexion mobile Airtel/Orange/Vodacom. Fermer et rouvrir SCOLIA. La session doit reprendre via Render **sans QR**.
3. Désactiver Tailscale et recommencer. La connexion WSS doit continuer de fonctionner avec le serveur principal actif.
4. Révoquer l'appareil depuis le serveur : la connexion automatique doit être refusée. Ne pas confondre reconnexion réseau et autorisation de compte.
5. Arrêter le serveur principal : tester consultation hors ligne des pages préparées, file d'attente, redémarrage et non-duplication des paiements.
6. Renouveler une autorisation approchant l'échéance et confirmer la persistance après redémarrage sur le même appareil.
7. Vérifier l'installation de l'APK officiel **avec le même certificat** que les précédentes versions. Un APK de CI de branche peut porter une signature provisoire : **ne pas l'installer en mise à jour de production**.

La présence du code et le statut `Live` de Render ne suffisent pas à garantir ces essais en conditions réelles.
