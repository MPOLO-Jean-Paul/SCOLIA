#!/usr/bin/env bash
# Produit le paquet Windows autonome de SCOLIA Pro (aucun prérequis sur le poste).
# Usage : scripts/publier-windows.sh [dossier_de_sortie]
set -euo pipefail
racine="$(cd "$(dirname "$0")/.." && pwd)"
sortie="${1:-$racine/artifacts}"
version="$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$racine/Directory.Build.props")"
paquet="$sortie/SCOLIA"

echo "==> Interface web"
(cd "$racine/web" && npm ci --no-audit --no-fund && npm run build)

echo "==> Exécutable Windows $version"
rm -rf "$paquet"
dotnet publish "$racine/src/Scolia.Api" -c Release -f net10.0-windows -r win-x64 --self-contained \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:EnableCompressionInSingleFile=true -p:PublishReadyToRun=true \
  -p:DebugType=None -p:DebugSymbols=false -p:IsTransformWebConfigDisabled=true \
  -o "$paquet"

echo "==> Outils d'exploitation"
cp "$racine"/scripts/windows/* "$paquet/"
rm -f "$paquet"/*.pdb

archive="$sortie/SCOLIA_Pro_${version}_Windows_x64.zip"
rm -f "$archive"
(cd "$sortie" && zip -qr -9 "$archive" SCOLIA)
(cd "$sortie" && sha256sum "$(basename "$archive")" > "$(basename "$archive").sha256")
echo "Paquet : $archive"
