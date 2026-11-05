#!/usr/bin/env bash
# Enregistrement LOCAL des secrets de signature SCOLIA. Ne jamais executer dans
# un job GitHub Actions ni avec une cle de test. Aucune cle privee dans Git.
set -euo pipefail
set +x
umask 077

repo="${SCOLIA_GITHUB_REPOSITORY:-MPOLO-Jean-Paul/SCOLIA}"
keystore="${1:-}"
expected="E5:B5:F7:FA:41:54:11:EE:AC:4B:F7:34:84:90:24:FF:CF:7F:93:50:0C:60:13:83:99:31:B0:20:38:F5:89:80"

if [[ -z "$keystore" || ! -f "$keystore" || ! -r "$keystore" ]]; then
  echo "Usage : bash scripts/enregistrer-secrets-android-github.sh /chemin/prive/scolia.keystore" >&2
  exit 2
fi
for cmd in gh openssl keytool; do
  command -v "$cmd" >/dev/null 2>&1 || {
    echo "Commande requise absente : $cmd" >&2
    exit 2
  }
done
gh auth status -h github.com >/dev/null 2>&1 || {
  echo "Connectez GitHub CLI sur ce poste de confiance : gh auth login" >&2
  exit 2
}
if [[ ! -r /dev/tty ]]; then
  echo "Un terminal prive est necessaire pour saisir le mot de passe." >&2
  exit 2
fi

printf "Mot de passe de la cle historique (saisie masquee) : " >/dev/tty
IFS= read -r -s mot </dev/tty || exit 2
printf "\n" >/dev/tty
if [[ -z "$mot" ]]; then
  echo "Mot de passe vide : operation annulee." >&2
  exit 2
fi

export SCOLIA_LOCAL_KEYSTORE_PASS="$mot"
if ! details="$(keytool -list -v -keystore "$keystore" -alias scolia \
  -storepass:env SCOLIA_LOCAL_KEYSTORE_PASS 2>/dev/null)"; then
  echo "Cle illisible, mauvais mot de passe ou alias scolia introuvable." >&2
  unset SCOLIA_LOCAL_KEYSTORE_PASS mot
  exit 1
fi
empreinte="$(printf '%s\n' "$details" | sed -n 's/^.*SHA256: *//p' | head -1)"
if [[ "$empreinte" != "$expected" ]]; then
  echo "Le certificat ne correspond pas a la signature historique. Aucun secret n'a ete modifie." >&2
  unset SCOLIA_LOCAL_KEYSTORE_PASS mot
  exit 1
fi
unset SCOLIA_LOCAL_KEYSTORE_PASS details

# gh chiffre la valeur localement avant de l'envoyer a GitHub.
# Ni le fichier de cle ni le mot de passe ne sont ecrits dans Git ou dans des logs.
openssl base64 -A -in "$keystore" |
  gh secret set SCOLIA_ANDROID_KEYSTORE --app actions --repo "$repo"
printf '%s' "$mot" |
  gh secret set SCOLIA_ANDROID_KEYSTORE_PASS --app actions --repo "$repo"
unset mot

for name in SCOLIA_ANDROID_KEYSTORE SCOLIA_ANDROID_KEYSTORE_PASS; do
  if ! gh secret list --app actions --repo "$repo" | awk '{print $1}' | grep -Fxq "$name"; then
    echo "Echec de verification de la presence du secret $name." >&2
    exit 1
  fi
done
echo "Les deux secrets existent dans $repo ; certificat historique verifie localement."
echo "Etape suivante : fusionner les workflows et verifier le build officiel, sans publier de nouvelle version tant que la signature n'est pas confirmee."
