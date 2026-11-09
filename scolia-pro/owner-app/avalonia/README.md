# SCOLIA Console — Avalonia 12 (.NET 10)

Nom commercial : **SCOLIA Console** ; éditeur : **JP-Services**.

Version native distincte des applications SCOLIA Client. Une seule base UI `Core/OwnerShell.cs`
et une seule palette `Core/OwnerPalette.cs` pour Windows et Android.

- Android : `com.jpservices.scolia.owner.preview` en Debug, `com.jpservices.scolia.owner` en Release SIGNÉE.
- Windows : fenêtre Avalonia + installateur Inno Setup. Ni WinForms ni WebView2.
- Interface actuelle : navigation et composants Avalonia natifs, tableau de bord sans fausses données, pages établissements, licences, serveurs, activité, sauvegardes, compte.
- Opérations administratives : tant que le flux d'authentification native Cloudflare Access/OIDC n'est pas implémenté et audité, les actions sensibles ouvrent le site officiel protégé dans le navigateur système. **Le jeton et les cookies Cloudflare ne sont jamais embarqués ou copiés dans l'application.** Cette version est un pilote de la nouvelle interface native, pas encore une administration 100 % hors navigateur.
- Mises à jour : canal propriétaire indépendant `scolia-owner-v...`, filtre la liste des Releases GitHub. Les APK Preview ne sont pas des releases signées permanentes.

Ne pas publier la version Android officielle sans les quatre secrets de signature Admin et un audit d'intégration Cloudflare Access.
