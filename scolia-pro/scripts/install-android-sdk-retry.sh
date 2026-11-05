#!/usr/bin/env bash
# Les runners GitHub peuvent recevoir un timeout provisoire sur le manifeste Android.
# Reessayer la meme installation de facon bornee, sans masquer les echecs persistants.
set -euo pipefail

retry_command() {
  local description="$1"; shift
  local attempt
  for attempt in 1 2 3; do
    if "$@"; then
      echo "::notice::$description : reussi (tentative $attempt/3)."
      return 0
    fi
    if [[ "$attempt" -eq 3 ]]; then
      echo "::error::$description : 3 echecs consecutifs, arret de compilation." >&2
      return 1
    fi
    local pause=$(( attempt * ${SCOLIA_SDK_RETRY_DELAY:-15} ))
    echo "::warning::$description : tentative $attempt/3 echouee, nouvelle tentative dans $pause secondes." >&2
    sleep "$pause"
  done
}

if [[ "${1:-}" == "--self-test" ]]; then
  test_attempts=0
  mock_responds_on_third_attempt() {
    test_attempts=$((test_attempts + 1))
    [[ "$test_attempts" -ge 3 ]]
  }
  SCOLIA_SDK_RETRY_DELAY=0 retry_command "Test de reprise" mock_responds_on_third_attempt
  if [[ "$test_attempts" -ne 3 ]]; then
    echo "::error::La reprise n'a pas atteint la troisieme tentative." >&2
    exit 1
  fi
  echo "Test des trois tentatives : reussi."
  exit 0
fi

if [[ -z "${ANDROID_HOME:-}" || -z "${JAVA_HOME:-}" ]]; then
  echo "::error::ANDROID_HOME et JAVA_HOME doivent etre configures avant l'installation Android." >&2
  exit 1
fi

retry_command "Installation du workload .NET Android" dotnet workload install android
retry_command "Installation des dependances du SDK Android" dotnet build src/Scolia.Mobile \
  -t:InstallAndroidDependencies -f net10.0-android \
  "-p:AndroidSdkDirectory=$ANDROID_HOME" "-p:JavaSdkDirectory=$JAVA_HOME" \
  -p:AcceptAndroidSDKLicenses=True
