# SCOLIA Android — reprise après une écriture enregistrée hors ligne

## Comportement ajouté

Lorsqu'un appareil Android client enregistre une opération **durablement** dans sa file hors ligne, le centre de synchronisation demande à WorkManager une tâche unique `scolia-synchronisation-pending`, conditionnée par la disponibilité d'un réseau.

- La tâche conserve la même identité de session et les clés d'idempotence de la file existante.
- `ExistingWorkPolicy.Keep` empêche l'empilement de tâches identiques quand plusieurs opérations sont enregistrées.
- L'absence de réseau laisse la tâche programmée jusqu'à disponibilité, sous la supervision d'Android.
- En cas d'erreur transitoire, WorkManager applique un report exponentiel ; il ne faut pas demander d'exemption batterie par défaut à tous les clients.
- La synchronisation périodique de 15 minutes et la reprise à l'ouverture restent en place pour les situations où la première tâche n'est pas exécutée immédiatement.
- Un serveur SCOLIA sur téléphone utilise toujours son service dédié au premier plan : **ce code n'installe pas un service permanent sur les téléphones clients**.

## Limites

- Les restrictions Doze, Battery Saver, l'absence de Tailscale/VPN actif, les arrêts forcés et l'expiration de session peuvent retarder les envois. Cela ne garantit PAS une synchronisation immédiate quand l'application cliente est fermée.
- Une transaction conservée dans l'outbox n'est **pas encore encaissée définitivement** sur l'appareil principal ; un reçu final ne peut être confirmé qu'après la validation par le serveur.
- Si deux appareils présentent des opérations incompatibles, le journal des conflits doit permettre leur examen. Aucun écrasement silencieux des paiements n'est autorisé.
- Les erreurs de planification WorkManager ne doivent pas empêcher l'enregistrement local de l'opération.

## Scénarios à vérifier sur de vrais téléphones

1. Client hors ligne : enregistrer une opération autorisée, fermer la fenêtre, retrouver la file après redémarrage.
2. Rétablir le réseau sans ouvrir SCOLIA : vérifier l'envoi lorsque WorkManager obtient une fenêtre d'exécution, en évitant de promettre un délai fixe.
3. Enregistrer plusieurs opérations : vérifier qu'une seule tâche unique reste programmée et que chaque clé d'idempotence produit au plus une écriture serveur.
4. Expirer la session : garder les opérations et demander une reconnexion sans effacer la file.
5. Tester Wi-Fi, 4G et connexion privée Tailscale ; observer les restrictions constructeur et la consommation batterie.
6. Refaire le test sur Android 16 avec économies d'énergie activées puis désactivées, et après une mise à jour APK.

Les tests CI vérifient les sources et les builds ; ils ne remplacent pas ces essais système.
