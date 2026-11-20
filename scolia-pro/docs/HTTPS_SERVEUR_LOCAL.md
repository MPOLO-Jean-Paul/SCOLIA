# SCOLIA Pro — migration HTTPS du serveur local (activation volontaire)

La version historique accepte les connexions `http://` en Wi-Fi local. Le code fournit désormais un **mode HTTPS optionnel**, sans changement automatique de protocole sur les installations existantes.

## Prérequis

- Utiliser un **certificat serveur valide contenant une clé privée** au format PKCS#12 (`.pfx`), dont les noms DNS/IP couvrent le nom réellement utilisé par les clients.
- Le certificat et sa chaîne doivent être **explicitement approuvés par chaque appareil client**. Ne jamais désactiver la validation TLS ou accepter n'importe quel certificat pour « faire fonctionner » SCOLIA.
- Placer le fichier PFX **en dehors du dépôt Git, des dossiers publics et des artefacts**, avec permissions minimales au compte qui lance le service.
- Garder le mot de passe PFX dans un secret de service ou une variable d'environnement protégée. Ne pas le mettre dans `appsettings.json`, GitHub Actions Logs ni un QR code de jumelage.
- Préparer une sauvegarde et un essai sur une installation non productive avant la bascule.

## Activation côté serveur

Exemple PowerShell sur **un serveur Windows de test**, avec un certificat déjà préparé et validé par l'administrateur :

```powershell
$env:Scolia__Reseau__Https__Actif = 'true'
$env:Scolia__Reseau__Https__CertificatPfx = 'C:\\SCOLIA-Private\\tls\\scolia-serveur.pfx'
$env:Scolia__Reseau__Https__MotDePassePfx = '<saisir-ici-le-mot-de-passe-sur-le-serveur>'
$env:Scolia__Reseau__AccesReseauLocal = 'true'
# Redémarrer le service dans le contexte qui contient ces variables.
```

Les trois paramètres de transport sont également disponibles à travers la configuration .NET `Scolia:Reseau:Https:Actif`, `CertificatPfx` et `MotDePassePfx`. **Si HTTPS est actif mais que le fichier, son mot de passe ou la clé privée ne peuvent être chargés, le serveur refuse de démarrer** plutôt que de revenir silencieusement au HTTP.

Une valeur `ASPNETCORE_URLS` ou `urls` explicitement configurée avec `http://` est refusée lorsque HTTPS est actif. Avec HTTPS activé et l'accès local actif, l'adresse d'écoute par défaut devient `https://0.0.0.0:5080`.

## Migration sans interruption des autres appareils

1. Tester sur un poste non productif, avec sa propre base de test et des certificats approuvés.
2. Vérifier depuis chaque client que le nom du certificat correspond bien à l'adresse HTTPS déclarée dans SCOLIA.
3. Tester la connexion, l'inscription, l'encaissement, les reçus, les sauvegardes, la reconnexion et la synchronisation.
4. Mettre à jour manuellement les adresses mémorisées et les QR codes d'association. **Aucune conversion automatique de HTTP vers HTTPS n'est fournie par ce correctif.**
5. Planifier une intervention pour basculer les appareils du même établissement de manière coordonnée, avec possibilité de retour arrière documentée.
6. Pour Tailscale, conserver les autorisations de réseau privé : un tunnel chiffré **ne remplace pas** l'authentification des utilisateurs ni l'autorisation des appareils.

## Limites

- Le transport HTTP existant reste disponible tant que le paramètre est à `false`, pour compatibilité ; sa présence constitue une **limite de sécurité résiduelle**. Le verrouillage total de HTTP nécessite une migration opérationnelle des clients.
- Les appareils Android qui servent eux-mêmes de serveur sont soumis aux restrictions de mise en veille. L'activation de HTTPS ne modifie pas les garanties de disponibilité.
- Les sauvegardes et leurs clés de chiffrement constituent un chantier distinct. Un fichier SQLite copié depuis le dossier de données n'est pas chiffré uniquement parce que le serveur utilise HTTPS.
