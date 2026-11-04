#!/usr/bin/env bash
# Bloque l'ajout accidentel de bases locales, secrets, caches ou sauvegardes
# d'etablissements dans le depot Git public de SCOLIA.
# Ne lit JAMAIS le contenu des documents ou des cles.
set -euo pipefail

repository_root="$(git -C "$(dirname "$0")" rev-parse --show-toplevel)"
sensitive_path() {
  local path="${1,,}"
  path="${path#./}"
  # Les six fichiers de notice, expressément conservés par .gitignore,
  # décrivent les dossiers sans contenir de dossiers scolaires.
  case "$path" in
    scolia/data/lire_moi.txt|scolia/journaux/lire_moi.txt|scolia/configuration/lire_moi.txt|scolia/archives/lire_moi.txt|scolia/exports/lire_moi.txt|scolia/sources/lire_moi.txt)
      return 1 ;;
  esac
  case "$path" in
    scolia-pro/scolia/*|scolia/data/*|scolia/journaux/*|scolia/configuration/*|scolia/archives/*|scolia/exports/*|scolia/sources/*)
      return 0 ;;
    */hors-ligne/*|*/idempotence/*|*/donnees/*|*/cles/*|*/journaux/*)
      return 0 ;;
    *.db|*.db-shm|*.db-wal|*.sqlite|*.sqlite-shm|*.sqlite-wal|*.sqlite3|*.sqlite3-shm|*.sqlite3-wal)
      return 0 ;;
    *.keystore|*.jks|*.p12|*.pfx|*.pem|*.key|*.env|*/.env|*/.env.*|*/id_rsa|*/id_ed25519)
      return 0 ;;
  esac
  return 1
}

if [[ "${1:-}" == "--self-test" ]]; then
  should_block=(
    "scolia-pro/SCOLIA/hors-ligne/exemple.json"
    "scolia-pro/src/Scolia.Api/donnees/school.db"
    "scolia-pro/security/official.keystore"
    "scolia-pro/database/clients.sqlite-wal"
    "scolia/archives/sauvegarde.csv"
    "scolia-pro/.env"
    "scolia-pro/config/.env.production"
    "scolia-pro/cles/appareils-autorises.json"
  )
  should_allow=(
    "scolia-pro/src/Scolia.Api/Security/Idempotency.cs"
    "scolia-pro/src/Scolia.Mobile/Scolia.Mobile.csproj"
    "scolia-pro/docs/PRIVACY_REPOSITORY_20261104.md"
    "scolia-pro/tests/Scolia.Tests/IdempotencySecurityTests.cs"
    ".github/workflows/scolia-repository-privacy.yml"
    "scolia/data/LIRE_MOI.txt"
    "scolia/archives/LIRE_MOI.txt"
  )
  for path in "${should_block[@]}"; do
    if ! sensitive_path "$path"; then
      echo "::error::La regle de confidentialite doit bloquer ce chemin de test : $path" >&2
      exit 1
    fi
  done
  for path in "${should_allow[@]}"; do
    if sensitive_path "$path"; then
      echo "::error::Faux positif de l'audit de confidentialite : $path" >&2
      exit 1
    fi
  done
  echo "Tests des chemins prives : reussis."
  exit 0
fi

blocked=0
while IFS= read -r -d '' path; do
  if sensitive_path "$path"; then
    echo "::error file=$path::Fichier d'execution ou cle privee suivie par Git. Retirer ce chemin de l'index sans effacer la copie locale (git rm --cached)." >&2
    blocked=1
  fi
done < <(git -C "$repository_root" ls-files -z)

if [[ "$blocked" -ne 0 ]]; then
  echo "Echec : des fichiers a risque sont presents dans l'index Git. Ne pas publier ces donnees." >&2
  exit 1
fi
echo "Controle des chemins suivis par Git : aucune base, cle privee ou cache d'etablissement trouve."
