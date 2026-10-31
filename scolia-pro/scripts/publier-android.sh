#!/usr/bin/env bash
# Produit l'application Android de SCOLIA Pro (fichier APK à installer sur le téléphone).
# Prérequis : SDK .NET 10 avec le workload android, et le SDK Android (variable ANDROID_HOME).
# Signature : si SCOLIA_KEYSTORE et SCOLIA_KEYSTORE_PASS sont définies, l'APK est signé avec cette clé,
# ce qui permet d'installer les versions suivantes par-dessus ; sinon une clé de développement est utilisée.
# Usage : scripts/publier-android.sh [dossier_de_sortie]
set -euo pipefail
racine="$(cd "$(dirname "$0")/.." && pwd)"
sortie="${1:-$racine/artifacts}"
version="$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$racine/Directory.Build.props")"
dossier="$sortie/android"
sdk="${ANDROID_HOME:-${ANDROID_SDK_ROOT:-}}"
if [ -z "$sdk" ]; then echo "ANDROID_HOME n'est pas défini : le SDK Android est nécessaire." >&2; exit 1; fi

echo "==> Application Android $version"
rm -rf "$dossier" && mkdir -p "$dossier"
dotnet publish "$racine/src/Scolia.Mobile" -c Release -f net10.0-android \
  -p:AndroidSdkDirectory="$sdk" -p:AndroidPackageFormat=apk -o "$dossier/publication"

apk="$(ls "$dossier"/publication/*-Signed.apk | head -1)"
cible="$dossier/SCOLIA_Pro_${version}_Android.apk"
cp "$apk" "$cible"
rm -rf "$dossier/publication"
(cd "$dossier" && sha256sum "$(basename "$cible")" > "$(basename "$cible").sha256")
echo "Application : $cible"
