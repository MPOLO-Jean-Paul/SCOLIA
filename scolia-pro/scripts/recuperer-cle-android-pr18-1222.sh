#!/usr/bin/env bash
# Exception UNIQUE pour la distribution SCOLIA Pro 1.2.22 a partir de la PR interne #18.
# Cache porte par refs/pull/18/merge; ne jamais l'utiliser sur un push de branche
# ni pour une autre version. N'exporte jamais de cle ni de mot de passe en artefact.
set -euo pipefail
set +x
umask 077

cache="${1:?Dossier du cache PR18 obligatoire}"
expected="E5:B5:F7:FA:41:54:11:EE:AC:4B:F7:34:84:90:24:FF:CF:7F:93:50:0C:60:13:83:99:31:B0:20:38:F5:89:80"
version="$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' Directory.Build.props | head -1)"

if [[ "$version" != "1.2.22" ||
      "${GITHUB_EVENT_NAME:-}" != "pull_request" ||
      "${GITHUB_REF:-}" != "refs/pull/18/merge" ||
      "${SCOLIA_RECOVERY_PR18:-}" != "true" ||
      "${SCOLIA_CACHE_HIT:-}" != "true" ||
      "${GITHUB_REPOSITORY:-}" != "MPOLO-Jean-Paul/SCOLIA" ||
      "${GITHUB_HEAD_REF:-}" != "release/direct-apk-scolia-pro-20261103" ||
      "${GITHUB_BASE_REF:-}" != "feature/scolia-recovery-login-lock-20261103" ]]; then
  echo "::error::Source de distribution non autorisee pour SCOLIA Pro 1.2.22." >&2
  exit 1
fi

if [[ -z "${GITHUB_ENV:-}" || -z "${RUNNER_TEMP:-}" ||
      ! -s "$cache/scolia.keystore" || ! -s "$cache/mot-de-passe" ]]; then
  echo "::error::La cle historique verifiee de la PR #18 est inaccessible." >&2
  exit 1
fi

password="$(cat "$cache/mot-de-passe")"
if [[ -z "$password" ]]; then
  echo "::error::Mot de passe historique absent." >&2
  exit 1
fi
echo "::add-mask::$password"
export SCOLIA_PR18_STOREPASS="$password"
if ! details="$(keytool -list -v -keystore "$cache/scolia.keystore" -alias scolia \
  -storepass:env SCOLIA_PR18_STOREPASS 2>/dev/null)"; then
  echo "::error::Le cache PR #18 contient une cle illisible ou un alias incorrect." >&2
  exit 1
fi
actual="$(printf '%s\n' "$details" | sed -n 's/^.*SHA256: *//p' | head -1)"
if [[ "$actual" != "$expected" ]]; then
  echo "::error::L'empreinte du cache PR #18 differe du certificat officiel : publication refusee." >&2
  exit 1
fi

# Les valeurs ne sont utilisees que dans le job Android, jamais stockees dans Git.
private="$RUNNER_TEMP/scolia-signature-pr18-1222"
install -d -m 700 "$private"
install -m 600 "$cache/scolia.keystore" "$private/scolia.keystore"
rm -f "$cache/scolia.keystore" "$cache/mot-de-passe"
rmdir "$cache" 2>/dev/null || true
{
  printf 'SCOLIA_KEYSTORE=%s\n' "$private/scolia.keystore"
  printf 'SCOLIA_KEYSTORE_PASS=%s\n' "$password"
  printf 'SCOLIA_SIGNATURE_ORIGINE=cache historique PR18 valide pour SCOLIA 1.2.22\n'
  printf 'SCOLIA_SIGNATURE_EMPREINTE=%s\n' "$actual"
} >> "$GITHUB_ENV"
unset SCOLIA_PR18_STOREPASS password details actual
echo "Le certificat Android original a ete verifie : la distribution SCOLIA 1.2.22 peut etre compilee."
