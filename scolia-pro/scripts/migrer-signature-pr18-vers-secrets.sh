#!/usr/bin/env bash
# Migration ponctuelle de la vraie cle historique de la PR #18 vers GitHub Actions Secrets.
# Le fichier keystore et le mot de passe ne quittent jamais le runner par un artefact,
# un log, un commit ou un parametre de commande. gh chiffre les valeurs localement.
set -euo pipefail
set +x
umask 077

cache="${1:?Dossier du cache de la PR 18 obligatoire}"
expected="E5:B5:F7:FA:41:54:11:EE:AC:4B:F7:34:84:90:24:FF:CF:7F:93:50:0C:60:13:83:99:31:B0:20:38:F5:89:80"

if [[ "${GITHUB_ACTIONS:-}" != "true" ||
      "${GITHUB_EVENT_NAME:-}" != "pull_request" ||
      "${GITHUB_REF:-}" != "refs/pull/18/merge" ||
      "${GITHUB_REPOSITORY:-}" != "MPOLO-Jean-Paul/SCOLIA" ||
      "${GITHUB_HEAD_REF:-}" != "release/direct-apk-scolia-pro-20261103" ||
      "${GITHUB_BASE_REF:-}" != "feature/scolia-recovery-login-lock-20261103" ||
      "${SCOLIA_MIGRATION_PR18:-}" != "true" ||
      "${SCOLIA_CACHE_HIT:-}" != "true" ]]; then
  echo "::error::Contexte refuse : seule la PR interne #18, avec un cache effectivement restaure, est autorisee." >&2
  exit 1
fi

for tool in gh keytool openssl; do
  command -v "$tool" >/dev/null 2>&1 || {
    echo "::error::Commande de migration manquante : $tool" >&2
    exit 1
  }
done

if [[ ! -s "$cache/scolia.keystore" || ! -s "$cache/mot-de-passe" ]]; then
  echo "::error::La PR #18 ne dispose pas des deux fichiers historiques attendus." >&2
  exit 1
fi

# L'environnement protegee scolia-signature-migration doit fournir ce jeton
# de courte duree avec la seule permission Secrets: read/write sur ce depot.
if [[ -z "${SCOLIA_SIGNING_ADMIN_TOKEN:-}" ]]; then
  echo "::error::Jeton d'administration des secrets absent. Configurer et proteger l'environnement de migration." >&2
  exit 1
fi

cleanup() {
  rm -f "$cache/scolia.keystore" "$cache/mot-de-passe" 2>/dev/null || true
  unset SCOLIA_CACHE_KEYSTORE_PASS GH_TOKEN SCOLIA_SIGNING_ADMIN_TOKEN password details
}
trap cleanup EXIT

password="$(cat "$cache/mot-de-passe")"
if [[ -z "$password" ]]; then
  echo "::error::Mot de passe historique manquant : aucun changement de secrets." >&2
  exit 1
fi

export SCOLIA_CACHE_KEYSTORE_PASS="$password"
if ! details="$(keytool -list -v -keystore "$cache/scolia.keystore" -alias scolia \
  -storepass:env SCOLIA_CACHE_KEYSTORE_PASS 2>/dev/null)"; then
  echo "::error::Keystore ou mot de passe incorrect. Aucun secret publie." >&2
  exit 1
fi
actual="$(printf '%s\n' "$details" | sed -n 's/^.*SHA256: *//p' | head -1)"
if [[ "$actual" != "$expected" ]]; then
  echo "::error::Empreinte inattendue : ce cache ne correspond pas a la cle Android officielle." >&2
  exit 1
fi
unset details SCOLIA_CACHE_KEYSTORE_PASS actual

# gh secret set chiffre chaque valeur localement avec la cle publique de GitHub.
# Le keystore et son mot de passe sont uniquement transmis par stdin : ni
# arguments de processus, ni traces de shell, ni GitHub Actions artifacts.
unset GH_DEBUG GH_TRACE 2>/dev/null || true
export GH_TOKEN="$SCOLIA_SIGNING_ADMIN_TOKEN"
openssl base64 -A -in "$cache/scolia.keystore" |
  gh secret set SCOLIA_ANDROID_KEYSTORE --app actions --repo "$GITHUB_REPOSITORY"
printf '%s' "$password" |
  gh secret set SCOLIA_ANDROID_KEYSTORE_PASS --app actions --repo "$GITHUB_REPOSITORY"

# Ne controler que les NOMS (GitHub ne permet pas de relire la valeur des secrets).
names="$(gh secret list --app actions --repo "$GITHUB_REPOSITORY" | awk '{print $1}')"
for name in SCOLIA_ANDROID_KEYSTORE SCOLIA_ANDROID_KEYSTORE_PASS; do
  if ! grep -Fxq "$name" <<<"$names"; then
    echo "::error::Impossible de verifier le secret $name apres migration." >&2
    exit 1
  fi
done

echo "Migration de la signature Android de la PR #18 terminee : deux secrets GitHub Actions presents."
echo "Certificat historique verifie ; aucune cle privee ni mot de passe publie."
