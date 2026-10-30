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

## Publier un vrai IPA signé (appareil physique)

Un fichier du simulateur ne peut pas devenir un IPA par renommage. La CI fournit désormais
le flux GitHub Actions **SCOLIA Phone iPhone - IPA signe** dans
`.github/workflows/scolia-phone-ipa.yml`. Ce workflow est **manuel** et ne livre rien
sans signature Apple. Pour rendre son bouton manuel disponible, le workflow doit être
présent sur la branche principale de GitHub (via la PR approuvée).

Dans **GitHub > Settings > Secrets and variables > Actions > New repository secret**,
configurer les cinq secrets suivants, sans les envoyer par message :

- `SCOLIA_IOS_CERT_P12_BASE64` : contenu base64 du certificat Apple Distribution
  au format P12 **avec sa clé privée**.
- `SCOLIA_IOS_CERT_P12_PASSWORD` : mot de passe du fichier P12.
- `SCOLIA_IOS_PROFILE_BASE64` : contenu base64 du profil de distribution
  `.mobileprovision` correspondant à `com.jpservices.scolia.ios`.
- `SCOLIA_IOS_CODESIGN_KEY` : nom exact de l'identité « Apple Distribution: … ».
- `SCOLIA_IOS_PROFILE_NAME` : nom ou UUID du profil correspondant.

Exécuter ensuite **Actions > SCOLIA Phone iPhone - IPA signe > Run workflow**.
La compilation utilise macOS / Xcode 27, publie pour `ios-arm64`, vérifie la
signature du `.app` dans l'IPA, puis affiche un téléchargement nommé
`SCOLIA_Phone_iPhone_IPA_Signe` avec le vrai fichier
`SCOLIA_Phone_iPhone_iOS_Signed.ipa` à l'intérieur.

**Choix de distribution :** un profil Ad Hoc nécessite l'enregistrement des iPhone
cibles auprès d'Apple ; un profil App Store permet l'envoi vers TestFlight ou l'App Store,
mais le simple téléchargement de l'IPA n'installe pas l'application sur l'iPhone.
Le certificat, le profil et les autorisations Apple doivent appartenir à
l'établissement ou au responsable autorisé. Les secrets doivent rester hors du dépôt.
