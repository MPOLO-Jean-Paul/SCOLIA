#!/usr/bin/env bash
# Exception temporaire 1.2.17 : restaurer la cle qui a signe 1.2.16 depuis le cache historique.
# NE JAMAIS enregistrer ce cache ou la cle dans Git, les logs ou un artefact.
# Ce script n'est utilisable que sur un push de la branche officielle, pour la version 1.2.17.
set -euo pipefail
umask 077

cache="${1:?dossier du cache historique obligatoire}"
version="$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' Directory.Build.props | head -1)"
expected="E5:B5:F7:FA:41:54:11:EE:AC:4B:F7:34:84:90:24:FF:CF:7F:93:50:0C:60:13:83:99:31:B0:20:38:F5:89:80"
if [[ "${GITHUB_EVENT_NAME:-}" != "push" ||
      "${GITHUB_REF:-}" != "refs/heads/release/direct-apk-scolia-pro-20261103" ||
      "$version" != "1.2.17" ]]; then
  echo "::error::La restauration de cle historique n'est autorisee que pour la version officielle 1.2.17." >&2
  exit 1
fi
if [[ -z "${GITHUB_ENV:-}" || -z "${RUNNER_TEMP:-}" ||
      ! -s "$cache/scolia.keystore" || ! -s "$cache/mot-de-passe" ]]; then
  echo "::error::Fichiers de signature absents ou incomplets dans le cache Actions." >&2
  exit 1
fi

mot="$(cat "$cache/mot-de-passe")"
if [[ -z "$mot" ]]; then
  echo "::error::Le mot de passe du cache historique est vide." >&2
  exit 1
fi
echo "::add-mask::$mot"
export SCOLIA_CACHE_KEYSTORE_PASS="$mot"
# keytool lit le mot de passe dans l'environnement : ne pas le mettre dans ses arguments.
if ! details="$(keytool -list -v -keystore "$cache/scolia.keystore" -alias scolia \
  -storepass:env SCOLIA_CACHE_KEYSTORE_PASS 2>/dev/null)"; then
  echo "::error::Impossible de verifier l'ancienne cle Android." >&2
  exit 1
fi
fingerprint="$(printf '%s\n' "$details" | sed -n 's/^.*SHA256: *//p' | head -1)"
if [[ "$fingerprint" != "$expected" ]]; then
  echo "::error::La cle du cache ne correspond pas au certificat officiel de 1.2.16. Publication interdite." >&2
  exit 1
fi

private="$RUNNER_TEMP/scolia-cle-historique-1217"
mkdir -p -m 700 "$private"
cp "$cache/scolia.keystore" "$private/scolia.keystore"
chmod 600 "$private/scolia.keystore"
# Le cache restaure ne doit pas rester dans le dossier personnel du runner.
rm -f "$cache/scolia.keystore" "$cache/mot-de-passe"
rmdir "$cache" 2>/dev/null || true

{
  printf 'SCOLIA_KEYSTORE=%s\n' "$private/scolia.keystore"
  printf 'SCOLIA_KEYSTORE_PASS=%s\n' "$mot"
  printf 'SCOLIA_SIGNATURE_ORIGINE=cle historique verifiee pour la distribution 1.2.17\n'
  printf 'SCOLIA_SIGNATURE_EMPREINTE=%s\n' "$fingerprint"
} >> "$GITHUB_ENV"
unset SCOLIA_CACHE_KEYSTORE_PASS
mot=''
echo "Cle de signature originale reconnue : certificat identique a l'APK 1.2.16. Aucun secret expose."
