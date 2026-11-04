#!/usr/bin/env bash
# Prépare la clé qui signe l'APK, la MÊME à chaque compilation : c'est ce qui permet à Android d'installer une
# nouvelle version par-dessus l'ancienne (mise à jour), au lieu de refuser un « paquet en conflit ».
#   1. Clé de l'établissement, si les secrets SCOLIA_ANDROID_KEYSTORE (base64) et SCOLIA_ANDROID_KEYSTORE_PASS existent.
#   2. Sinon, une clé temporaire de VALIDATION dans le dossier fourni (dossier $1).
# Le workflow officiel n'autorise jamais cette solution de secours pour une release.
# Le workflow de validation fournit un dossier temporaire distinct à chaque exécution.
# Aucune clé n'est jamais écrite dans le dépôt Git. Écrit SCOLIA_KEYSTORE et SCOLIA_KEYSTORE_PASS dans $GITHUB_ENV.
set -euo pipefail
# Le fichier .keystore et ses mots de passe ne doivent pas être lisibles par les autres utilisateurs du runner.
umask 077
dossier="${1:?dossier de la clé}"
mkdir -p "$dossier"
if [ -n "${SCOLIA_ANDROID_KEYSTORE:-}" ] && [ -n "${SCOLIA_ANDROID_KEYSTORE_PASS:-}" ]; then
  echo "$SCOLIA_ANDROID_KEYSTORE" | base64 -d > "$RUNNER_TEMP/scolia.keystore"
  cle="$RUNNER_TEMP/scolia.keystore"
  mot="$SCOLIA_ANDROID_KEYSTORE_PASS"
  origine="clé de l'établissement (secrets du dépôt)"
else
  if [ ! -s "$dossier/scolia.keystore" ]; then
    openssl rand -hex 24 > "$dossier/mot-de-passe"
    keytool -genkeypair -noprompt -keystore "$dossier/scolia.keystore" -alias scolia -keyalg RSA -keysize 3072 -validity 12000 \
      -storepass "$(cat "$dossier/mot-de-passe")" -keypass "$(cat "$dossier/mot-de-passe")" \
      -dname "CN=SCOLIA Pro, O=Jp-Services, C=CD"
    echo "Clé de test éphémère créée : cet APK ne remplacera pas la version officielle."
  fi
  cle="$dossier/scolia.keystore"
  mot="$(cat "$dossier/mot-de-passe")"
  origine="clé de validation temporaire (non officielle)"
fi
empreinte="$(keytool -list -v -keystore "$cle" -alias scolia -storepass "$mot" | sed -n 's/^.*SHA256: *//p' | head -1)"
echo "::add-mask::$mot"
{
  echo "SCOLIA_KEYSTORE=$cle"
  echo "SCOLIA_KEYSTORE_PASS=$mot"
  echo "SCOLIA_SIGNATURE_ORIGINE=$origine"
  echo "SCOLIA_SIGNATURE_EMPREINTE=$empreinte"
} >> "$GITHUB_ENV"
echo "Signature : $origine"
echo "Empreinte SHA-256 : $empreinte"
