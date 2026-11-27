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
# Une release Android doit exposer exactement la même version que le programme.
# Sans ce contrôle, 1.2.91 fut empaquetée avec versionName=1.2.90 et
# l'application proposait indéfiniment une mise à jour déjà installée.
version_android="$(sed -n 's:.*<ApplicationDisplayVersion>\(.*\)</ApplicationDisplayVersion>.*:\1:p' "$racine/src/Scolia.Mobile/Scolia.Mobile.csproj" | head -1)"
if [[ "$version_android" != "$version" ]]; then
  echo "::error::Version Android ($version_android) différente de la version produit ($version). Publication bloquée." >&2
  exit 1
fi

dossier="$sortie/android"
sdk="${ANDROID_HOME:-${ANDROID_SDK_ROOT:-}}"
if [ -z "$sdk" ]; then echo "ANDROID_HOME n'est pas défini : le SDK Android est nécessaire." >&2; exit 1; fi

# Numéro de version Android toujours croissant : minutes écoulées depuis 2020 à la date du commit. Une compilation
# plus récente a donc toujours un numéro plus grand, et s'installe comme une mise à jour de la précédente.
code="${SCOLIA_VERSION_CODE:-}"
if [ -z "$code" ] && horodatage="$(git -C "$racine" log -1 --format=%ct 2>/dev/null)" && [ -n "$horodatage" ]; then
  code=$(( (horodatage - 1577836800) / 60 ))
fi
# Le retour au design natif reste une mise à jour, y compris depuis le candidat 1.3.0.
minimum=0
case "$version" in
  1.3.1) minimum=3630120 ;;
  1.3.2) minimum=3630477 ;;
  1.3.3) minimum=3630478 ;;
  1.3.4) minimum=3630479 ;;
  1.3.5) minimum=3630480 ;;
  1.3.6) minimum=3630481 ;;
  1.3.7) minimum=3630482 ;;
  1.3.8) minimum=3630483 ;;
  1.3.9) minimum=3630484 ;;
  1.3.10) minimum=3630485 ;;
  1.3.11) minimum=3630486 ;;
esac
if (( minimum > 0 )); then
  if [[ -n "${SCOLIA_VERSION_CODE:-}" && ( ! "$code" =~ ^[0-9]+$ || "$code" -lt "$minimum" ) ]]; then
    echo "::error::versionCode inférieur au minimum de mise à jour $minimum." >&2
    exit 1
  fi
  if [[ -z "$code" || "$code" -lt "$minimum" ]]; then code="$minimum"; fi
fi
options=()
if [ -n "$code" ]; then options+=("-p:ApplicationVersion=$code"); fi
# Le mode Flutter officiel est volontairement désactivé par défaut.
# La signature historique et le canal de publication restent inchangés.
if [[ "${SCOLIA_FLUTTER_OFFICIAL:-false}" == "true" ]]; then
  attendu="E5:B5:F7:FA:41:54:11:EE:AC:4B:F7:34:84:90:24:FF:CF:7F:93:50:0C:60:13:83:99:31:B0:20:38:F5:89:80"
  if [[ "${SCOLIA_SIGNATURE_EMPREINTE:-}" != "$attendu" || -z "${SCOLIA_KEYSTORE:-}" || -z "${SCOLIA_KEYSTORE_PASS:-}" ]]; then
    echo "::error::Flutter officiel nécessite la signature historique vérifiée." >&2
    exit 1
  fi
  options+=("-p:ScoliaFlutterOfficial=true")
fi

echo "==> Application Android $version${code:+ (numéro $code)}"
rm -rf "$dossier" && mkdir -p "$dossier"
dotnet publish "$racine/src/Scolia.Mobile" -c Release -f net10.0-android \
  -p:AndroidSdkDirectory="$sdk" -p:AndroidPackageFormat=apk -o "$dossier/publication" "${options[@]}"

apk="$(ls "$dossier"/publication/*-Signed.apk | head -1)"
cible="$dossier/SCOLIA_Pro_${version}_Android.apk"
cp "$apk" "$cible"
rm -rf "$dossier/publication"
(cd "$dossier" && sha256sum "$(basename "$cible")" > "$(basename "$cible").sha256")
echo "Application : $cible"
