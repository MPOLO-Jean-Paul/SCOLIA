# SCOLIA Pro — jumelage sans mot de passe sur un poste lié

## Principes

Un appareil qui conserve la base et configure l'établissement est le **serveur**. Il conserve sa connexion SCOLIA normale. Un appareil qui rejoint le serveur par Wi-Fi, Tailscale ou relais Internet n'obtient aucun droit du seul fait de connaître son adresse : une **invitation approuvée** depuis la session du responsable est nécessaire.

Le responsable ouvre « Poste et réseau → Relier un appareil » sur le serveur. SCOLIA fabrique un QR contenant les voies réseau et une **invitation temporaire, à usage unique**, valable cinq minutes. Cette invitation est un titre d'accès : ne pas l'afficher publiquement, la diffuser dans un groupe ni la réutiliser.

Sur un autre appareil, « Scanner ou coller une invitation » ouvre la connexion réseau la plus adaptée (Wi-Fi, Tailscale, Internet chiffré), échange l'invitation contre une autorisation personnelle au poste et ouvre immédiatement le compte invité. **Le mot de passe du serveur n'est jamais transféré à l'appareil client**.

Le poste client conserve un secret de session propre, chiffré sur l'appareil avec Android Keystore / DPAPI Windows, puis le présente au serveur à ses ouvertures suivantes. Sa validité actuelle est limitée à **30 jours**. Le compte conserve les droits du compte ayant émis l'invitation : ne relier qu'un appareil de confiance, surtout lorsque le responsable possède tous les droits.

## Vérifications et révocation

Le serveur valide l'identité du compte, l'empreinte de sécurité et l'identifiant de l'appareil à chaque session. Le QR ne peut être échangé qu'une fois. Une réinitialisation du mot de passe ou un changement de droits invalide les anciennes sessions ; le secrétariat peut révoquer une autorisation d'appareil via l'API `POST /api/jumelage/appareils/{id}/revoquer` (avec authentification `utilisateurs.gerer`). La liste des autorisations est disponible à `GET /api/jumelage/appareils`. La déconnexion de l'appareil oublie localement le justificatif.

L'accès Tailscale **n'est qu'un tunnel privé**. Entrer une adresse IP Tailscale sans invitation n'est pas une preuve d'accès à SCOLIA ; le QR validé fournit cette preuve. Un ancien QR ne contenant que les adresses devra être renouvelé.

## Contraintes de sécurité et limites

- Le compte partagé via l'invitation ouvre une session avec ses permissions. Pour une séparation par fonctions, créez un compte nominatif possédant les droits adéquats et utilisez celui-ci pour l'invitation.
- Sur Wi-Fi local, le transport HTTP n'offre pas une confidentialité équivalente à HTTPS/Tailscale. Pour des données scolaires et des secrets de jumelage, **privilégiez Tailscale ou un transport chiffré**. Ne pas exposer directement le port SCOLIA sur Internet.
- Un appareil client a besoin de rejoindre le serveur lors de l'authentification par invitation ou lors de la reconnexion ; il ne devient jamais automatiquement serveur. Si le serveur est éteint, la réouverture de la session jumelée ne peut pas être vérifiée.
- Le certificat de l'appareil est une autorisation révocable, pas le mot de passe du compte. L'identifiant d'appareil seul n'est pas une preuve cryptographique ; l'appareil doit également posséder le secret aléatoire chiffré localement.
