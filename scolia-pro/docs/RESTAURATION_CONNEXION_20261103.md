# Restauration des mots de passe depuis l'écran de connexion

SCOLIA Pro reconnaît le dernier compte utilisé sur chaque appareil et n'exige plus de ressaisir l'identifiant à chaque ouverture. « Changer » permet d'ouvrir un autre compte. Sur le PC serveur, lorsqu'aucune connexion n'a encore été mémorisée, SCOLIA propose un compte actif du serveur.

## Accès perdu : aucune navigation dans les paramètres nécessaire

1. Sur la page de connexion, choisir « Mot de passe oublié ? ».
2. Saisir et confirmer le nouveau mot de passe.
3. Choisir la preuve : empreinte déjà activée pour ce compte, ou code de secours fourni par le responsable du serveur.
4. Après validation, se reconnecter avec le nouveau mot de passe.

L'empreinte déverrouille le secret déjà protégé par Android Keystore ; celui-ci doit encore être accepté par le serveur. Une empreinte associée à un code périmé ne suffit pas.

## Code de secours sur le serveur Windows

À la première ouverture de cette version, le serveur crée automatiquement `donnees/cles/code-de-secours.txt` à côté des exécutables Windows, sauf si `Scolia:Stockage:DataDirectory` pointe vers un autre dossier : dans ce cas, le fichier se trouve dans `<DataDirectory>/cles/`. Le code est un secret aléatoire à usage unique. **Seule une personne autorisée ayant accès au dossier du serveur doit pouvoir le lire.** Conserver une copie dans un coffre sûr.

Ce code peut être saisi dans la page de connexion de tout appareil relié au serveur. Après une restauration réussie, le code est immédiatement renouvelé : la copie précédente n'est plus valide et doit être remplacée.

Le serveur vérifie le code et la politique de mots de passe, limite les essais, consigne la restauration, invalide les sessions en ligne et conserve intégralement les données scolaires.

## Précautions

- Les fichiers de données du serveur, y compris le code de secours, doivent être protégés par les droits de l'OS et les règles d'accès aux sauvegardes.
- Le code ne doit pas être transmis sur un réseau non chiffré. Pour l'accès à distance, employer un tunnel chiffré tel que Tailscale ou une connexion HTTPS correctement configurée.
- Une restauration ne peut pas être exécutée sur un serveur éteint ou injoignable. Elle ne supprime pas les données mais n'invalide pas instantanément d'éventuels identifiants déjà mis en cache sur des appareils hors ligne ; leur révocation sera prise en compte à leur reconnexion.
- Sur un téléphone Android utilisé comme serveur, le fichier de secours appartient au stockage privé de l'application et peut ne pas être accessible avec un gestionnaire de fichiers standard. Avant de perdre l'accès, une méthode de sauvegarde autorisée de ce code doit donc être prévue, ou utiliser la biométrie enregistrée.
