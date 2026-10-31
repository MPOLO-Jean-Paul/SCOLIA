# SCOLIA Phone pour iPhone et iPad

Le client natif iOS partage la meme interface Avalonia, les formulaires et les regles metier avec SCOLIA Pro Windows et SCOLIA Phone Android.

## Etat

- Le projet source iOS est en place sous `src/Scolia.Phone.iOS`.
- La compilation de verification pour simulateur est lancee par `.github/workflows/scolia-phone-ios.yml` sur un environnement Apple Silicon avec Xcode 27.
- **iOS utilise un IPA, jamais un APK.** Pour obtenir une application installable sur un iPhone physique, une signature Apple et un profil de distribution sont necessaires.
- Aucune archive de simulateur non signee ne doit etre presentee comme un IPA pour iPhone reel.
- Le mode iPhone est CLIENT d'un SCOLIA Phone Serveur Android ou d'un serveur Windows. Aucun service de base scolaire en permanence en arriere-plan n'est promis sous iOS.

## Liaison Tailscale

1. Installer Tailscale depuis son App Store officiel et rejoindre le tailnet de l'etablissement.
2. Dans Tailscale iOS, configurer VPN On Demand si approprie : SCOLIA ne peut pas demarrer silencieusement le VPN d'une autre app.
3. Dans SCOLIA, cliquer Rejoindre par Tailscale et indiquer l'IP privee ou le nom MagicDNS du serveur.
4. L'API SCOLIA et les droits du compte restent obligatoires, meme dans le tailnet.

## Compilation

Le projet n'est pas inclus dans `Scolia.sln`, pour ne pas obliger Linux et Windows a installer le SDK Apple.

Installer .NET 10, Xcode adapte et le workload iOS sur un Mac, puis :

```bash
dotnet workload install ios
dotnet build src/Scolia.Phone.iOS/Scolia.Phone.iOS.csproj -f net10.0-ios -c Debug -r iossimulator-arm64 -p:EnableCodeSigning=false
```

La compilation iPhone physique exige `ios-arm64`, un certificat et un profil Apple. Elle ne devient installable qu'apres signature et validation sur un vrai iPhone.

## Compiler et publier depuis GitHub (sans Mac personnel)

Le dépôt comporte **deux workflows indépendants** : la compilation du simulateur
(`.github/workflows/scolia-phone-ios.yml`) et la **publication d'un véritable IPA signé**
(`.github/workflows/scolia-phone-ipa.yml`). Les deux utilisent les Mac Apple Silicon
de GitHub Actions et **Xcode 27**. Seul le second est installable sur iPhone/iPad
réels, suivant la distribution définie par Apple.

### Avant de générer un IPA réel

Un responsable du compte Apple Developer doit disposer d'un certificat
**Apple Distribution (.p12 avec clé privée)**, de son mot de passe et d'un
profil `.mobileprovision` non expiré pour l'identifiant
**`com.jpservices.scolia.ios`**. Le profil doit correspondre **au même
certificat** et au mode de distribution choisi.

Dans **GitHub > Settings > Secrets and variables > Actions**, configurer
**quatre secrets obligatoires**, sans jamais les publier dans le dépôt :

- `SCOLIA_IOS_CERT_P12_BASE64` : fichier `.p12` encodé en base64 ;
- `SCOLIA_IOS_CERT_P12_PASSWORD` : mot de passe du P12 ;
- `SCOLIA_IOS_PROFILE_BASE64` : profil `.mobileprovision` encodé en base64 ;
- `SCOLIA_IOS_CODESIGN_KEY` : identité exacte « Apple Distribution: … ».

`SCOLIA_IOS_PROFILE_NAME` reste **facultatif** : nom ou UUID du profil,
comme vérification supplémentaire. Ces secrets ne sont jamais exposés
aux exécutions de compilation non signée.

### Exécution

1. Après intégration du workflow sur la **branche GitHub par défaut**,
   ouvrir **Actions > SCOLIA Phone iPhone - IPA signe > Run workflow**.
2. Choisir la branche approuvée contenant les dernières corrections natives.
3. Sélectionner le canal : **ad-hoc** pour les appareils enregistrés dans
   Apple Developer ; **app-store** pour TestFlight ou l'App Store.
4. Le workflow vérifie les secrets, la date d'expiration, le certificat,
   son empreinte, l'identifiant de l'application et le type de profil
   **avant** toute publication.
5. Seule une compilation réussie avec signature valide fournit l'artefact
   **`SCOLIA_Phone_iPhone_IPA_Signe`**, contenant :
   - `SCOLIA_Phone_iPhone_iOS_Signed.ipa` ;
   - `SCOLIA_Phone_iPhone_iOS_Signed.ipa.sha256` ;
   - `LIRE_MOI.txt`.

**Ad Hoc :** les iPhone et iPad cibles doivent avoir été enregistrés dans
le profil chez Apple ; utiliser Xcode ou Apple Configurator pour installer.
**App Store :** transmettre le fichier à App Store Connect / TestFlight :
un fichier IPA App Store ne s'installe pas par simple téléchargement.
Une compilation réussie ne remplace pas les tests sur un vrai iPhone
(Face ID, connexion, synchronisation, pointage, reçus).

### Tests des mécanismes de signature sans certificat Apple

Depuis le répertoire `scolia-pro` :

```bash
python3 -m unittest discover -s scripts -p test_ios_signing.py -v
```

Ces tests sont également exécutés automatiquement par GitHub avec la
compilation du simulateur (sur chaque modification du projet iOS natif).
Les profils expirés, mauvais certificats, mauvaises identités d'application,
canaux incompatibles ou signatures invalides entraînent un **échec explicite**,
sans artefact IPA trompeur.

Voir [le guide GitHub iOS détaillé](../../docs/IOS_GITHUB.md).
