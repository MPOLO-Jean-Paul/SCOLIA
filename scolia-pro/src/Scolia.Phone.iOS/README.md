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
