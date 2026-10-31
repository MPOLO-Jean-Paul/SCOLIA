#!/usr/bin/env bash
# Installation de secours du SDK .NET 10 pour le CI, quand actions/setup-dotnet échoue.
# setup-dotnet passe par le lien aka.ms, qui renvoie parfois l'erreur 409. Ici la version est demandée
# explicitement, donc le téléchargement passe par les dépôts officiels, l'un après l'autre.
# Si aucun ne répond, le SDK .NET 10 déjà présent sur la machine est utilisé.
set -uo pipefail
dossier="$HOME/.dotnet"
script="${RUNNER_TEMP:-/tmp}/dotnet-install.sh"
depots=(https://builds.dotnet.microsoft.com/dotnet https://ci.dot.net/public)

for depot in "${depots[@]}"; do
  curl -fsSL --retry 3 "$depot/scripts/v1/dotnet-install.sh" -o "$script" 2>/dev/null \
    || curl -fsSL --retry 3 https://dot.net/v1/dotnet-install.sh -o "$script" || continue
  version="$(curl -fsSL --retry 3 "$depot/release-metadata/10.0/releases.json" | jq -r '."latest-sdk"' 2>/dev/null)"
  [ -n "$version" ] && [ "$version" != "null" ] || continue
  echo "==> SDK .NET $version depuis $depot"
  if bash "$script" --version "$version" --install-dir "$dossier" --azure-feed "$depot" --skip-non-versioned-files; then
    echo "$dossier" >> "$GITHUB_PATH"
    echo "DOTNET_ROOT=$dossier" >> "$GITHUB_ENV"
    exit 0
  fi
done

if dotnet --list-sdks 2>/dev/null | grep -q '^10\.'; then
  echo "==> SDK .NET 10 déjà installé sur la machine"
  dotnet --list-sdks
  exit 0
fi
echo "Aucun SDK .NET 10 n'a pu être installé." >&2
exit 1
