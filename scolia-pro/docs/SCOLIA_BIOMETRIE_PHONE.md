# Connexion biométrique SCOLIA Phone

La connexion par empreinte (Android/Touch ID) ou Face ID (iPhone) est une **option**, proposée uniquement si le système indique qu'une biométrie a été enregistrée sur le téléphone.

1. Une première connexion par identifiant et mot de passe est obligatoire.
2. L'utilisateur autorise explicitement l'option sur l'écran de connexion.
3. SCOLIA demande au système Android/iOS une authentification biométrique. Ni la photo, ni le gabarit, ni l'empreinte ne sont lus ou conservés.
4. SCOLIA mémorise uniquement le compte autorisé et l'adresse du serveur, dans le stockage local protégé. Les informations de session déjà sauvegardées par SCOLIA restent dans son stockage local chiffré ; aucun nouveau mot de passe n'est conservé pour cette fonctionnalité.
5. Aux ouvertures suivantes, la biométrie ne peut restaurer que le **dernier compte autorisé** sur le **même serveur** et avec un cookie de session toujours valide. Le serveur vérifie le cookie et l'identité réelle avant de délivrer la session.
6. Après une déconnexion explicite, la révocation ou l'expiration du cookie, un nouveau mot de passe sera nécessaire. La connexion hors ligne avec biométrie seule n'est pas autorisée.
7. Android 9 ou antérieur, appareils sans biométrie configurée ou iOS sans Face ID/Touch ID : le formulaire de mot de passe reste disponible.

**Attention aux téléphones partagés :** le système d'exploitation confirme qu'une biométrie enregistrée sur cet appareil est reconnue. Il ne dit pas laquelle. Toute empreinte ou visage ajouté par le propriétaire du téléphone peut déverrouiller la session de ce compte. Pour les comptes scolaires sensibles, privilégier un appareil personnel, un code de verrouillage robuste, des droits SCOLIA limités et un contrôle des appareils autorisés.

Le changement d'établissement ou de serveur ne réutilise pas les informations biométriques de l'ancien serveur.
L'option utilise les API natives BiometricPrompt d'Android et LocalAuthentication d'iOS. Les builds automatisés ne remplacent pas les tests réels sur Android 16 et iPhone.
