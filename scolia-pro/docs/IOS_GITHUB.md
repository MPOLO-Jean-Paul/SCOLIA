# SCOLIA Phone iOS — compiler directement sur GitHub

Cette procédure concerne **uniquement l'application native Avalonia** `src/Scolia.Phone.iOS`. Aucun site web ou composant React n'est requis pour compiler le client iPhone/iPad.

## 1. Deux chaînes de compilation complémentaires

| GitHub Actions | Déclenchement | Résultat |
| --- | --- | --- |
| `scolia-phone-ios.yml` | Modifications du code natif / PR, ou manuel | `.app` de simulateur **non signé**, non installable sur iPhone réel |
| `scolia-phone-ipa.yml` | **Manuel uniquement** | **IPA signé** avec certificat Apple + SHA-256, selon Ad Hoc ou App Store |

Les deux workflows tournent sur **GitHub-hosted macOS avec Xcode 27** et .NET 10 (`net10.0-ios`). Le code et les vues Avalonia sont partagés avec les clients Windows et Android : pas de PWA, pas de navigateur.

**Pas besoin de posséder un Mac pour exécuter la compilation** : GitHub met à disposition une machine Apple. En revanche, il faut des informations de signature Apple valides pour produire un IPA destiné à un véritable iPhone.

## 2. Préparer les certificats Apple

Sur le compte Apple Developer autorisé à publier SCOLIA :

1. Enregistrer **l'App ID explicite `com.jpservices.scolia.ios`**.
2. Créer un certificat **Apple Distribution**. Conserver **sa clé privée** et exporter certificat + clé privée ensemble dans un fichier `.p12` protégé par mot de passe : un `.cer` seul est insuffisant.
3. Créer un profil de **distribution `.mobileprovision`**, avec le même App ID et le même certificat.
4. Pour le canal **Ad Hoc**, enregistrer préalablement tous les identifiants des iPhone/iPad à tester chez Apple et ajouter ces appareils au profil ; un appareil absent ne pourra pas installer l'application.
5. Pour le canal **App Store**, créer un profil App Store ; l'IPA servira à **App Store Connect / TestFlight**, pas à une installation directe depuis les téléchargements.

Un profil de développement ou expiré est refusé par SCOLIA.

## 3. Installer les secrets dans GitHub

Ouvrir : **Dépôt SCOLIA > Settings > Secrets and variables > Actions > New repository secret**.

| Secret | Valeur |
| --- | --- |
| `SCOLIA_IOS_CERT_P12_BASE64` | Fichier P12 complet encodé en base64 |
| `SCOLIA_IOS_CERT_P12_PASSWORD` | Mot de passe du P12 |
| `SCOLIA_IOS_PROFILE_BASE64` | Fichier mobileprovision complet encodé en base64 |
| `SCOLIA_IOS_CODESIGN_KEY` | Nom exact de l'identité, par exemple `Apple Distribution: École (ABCDEFGHIJ)` |
| `SCOLIA_IOS_PROFILE_NAME` | **Facultatif**, nom/UUID du profil : contrôle supplémentaire |

Pour convertir en base64 sur **Windows** (PowerShell local, jamais un site public) :

```powershell
[Convert]::ToBase64String([IO.File]::ReadAllBytes('C:\chemin\distribution.p12'))
[Convert]::ToBase64String([IO.File]::ReadAllBytes('C:\chemin\SCOLIA.mobileprovision'))
```

Sur **macOS** : `base64 -i distribution.p12 | tr -d '\n'` et `base64 -i SCOLIA.mobileprovision | tr -d '\n'`.

Ne **jamais** envoyer ces éléments via chat, captures d'écran, fichiers du dépôt, pull requests ou tickets GitHub.

## 4. Compiler un véritable IPA

1. Intégrer d'abord le workflow `scolia-phone-ipa.yml` dans la **branche par défaut** du dépôt pour faire apparaître le bouton de lancement manuel.
2. Ouvrir **GitHub > Actions > SCOLIA Phone iPhone - IPA signe > Run workflow**.
3. Sélectionner une branche de confiance contenant les changements natifs validés.
4. Sélectionner **ad-hoc** ou **app-store**, suivant le profil Apple préparé.
5. Lancer. GitHub vérifie la présence des secrets, la validité du profil, l'identifiant `com.jpservices.scolia.ios`, le certificat et sa clé privée, et le canal choisi. Une erreur interrompt immédiatement la production.
6. GitHub compile en `Release` pour `ios-arm64` et réalise l'archivage Apple avec signature.
7. Le workflow décompresse l'IPA, vérifie `codesign`, l'architecture `arm64`, l'App ID et le profil intégré, puis produit l'empreinte SHA-256.
8. **Seulement si tout réussit**, ouvrir l'exécution GitHub, section **Artifacts**, et télécharger `SCOLIA_Phone_iPhone_IPA_Signe`.

L'archive contient :
- `SCOLIA_Phone_iPhone_iOS_Signed.ipa`
- `SCOLIA_Phone_iPhone_iOS_Signed.ipa.sha256`
- `LIRE_MOI.txt`

**Ad Hoc :** installation via Xcode / Apple Configurator sur les appareils enregistrés.
**App Store :** envoyer l'IPA à App Store Connect/TestFlight ; un téléchargement direct ne constitue pas une installation iPhone.

## 5. Diagnostiquer et vérifier sans certificat

Depuis `scolia-pro`, tester les contrôles avant de demander à Apple :

```bash
python3 -m unittest discover -s scripts -p test_ios_signing.py -v
```

Les 10 cas de test vérifient notamment : expiration, mauvais App ID, profil de développement, identité de signature absente, certificat différent, canal incompatible et distribution Enterprise non autorisée. La compilation de simulateur sur GitHub exécute automatiquement ces tests sans aucun secret.

Une compilation iOS de simulateur réussie n'est **pas** une preuve d'installation sur iPhone réel. Après signature, les essais sur iPhone et iPad restent obligatoires : connexion, Face ID, réseau privé, synchronisation, pointage et reçus.

## Documents officiels

- [GitHub : installer des certificats Apple sur macOS Actions](https://docs.github.com/en/actions/how-tos/deploy/deploy-to-third-party-platforms/sign-xcode-applications)
- [GitHub : runners macOS et Xcode](https://github.com/actions/runner-images)
- [Apple : profil de distribution Ad Hoc](https://developer.apple.com/help/account/provisioning-profiles/create-an-ad-hoc-provisioning-profile)
- [Avalonia : publication iOS](https://docs.avaloniaui.net/docs/deployment/ios)
- [Microsoft : publication .NET iOS en ligne de commande](https://learn.microsoft.com/en-us/dotnet/maui/ios/deployment/publish-cli?view=net-maui-10.0)
