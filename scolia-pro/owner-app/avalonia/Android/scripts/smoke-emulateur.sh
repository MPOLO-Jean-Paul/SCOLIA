#!/usr/bin/env bash
# Test de fumee SCOLIA Console sur emulateur Android (GitHub Actions).
# Usage : smoke-emulateur.sh <paquet Android> <chemin APK>
# L'action android-emulator-runner execute chaque ligne de son "script" separement avec sh :
# toute la logique vit donc ici, dans un seul appel bash.
set -euo pipefail

package="${1:?paquet Android requis}"
apk="${2:?chemin APK requis}"

diagnostic() {
  echo "--- appareil ---"
  adb shell getprop ro.product.cpu.abilist || true
  echo "--- processus ---"
  adb shell ps -A | grep -i scolia || echo "(aucun processus scolia)"
  echo "--- activites ---"
  adb shell dumpsys activity activities | grep -iE 'scolia|mResumedActivity|topResumed' | head -20 || true
  echo "--- logcat filtre ---"
  adb logcat -d | grep -iE 'scolia|AndroidRuntime|FATAL|mono|DOTNET|Fatal signal|has died|Force finishing' | tail -200 || true
  echo "--- logcat brut (300 dernieres lignes) ---"
  adb logcat -d -t 300 || true
}

running_pid() {
  local pid
  pid="$(adb shell pidof "$package" 2>/dev/null | tr -d '\r ' || true)"
  if [[ -z "$pid" ]]; then
    pid="$(adb shell ps -A 2>/dev/null | awk -v p="$package" '$NF==p {print $2; exit}' | tr -d '\r ' || true)"
  fi
  printf '%s' "$pid"
}

adb install -r "$apk"
adb logcat -c

component="$(adb shell cmd package resolve-activity --brief -a android.intent.action.MAIN -c android.intent.category.LAUNCHER "$package" | tr -d '\r' | tail -1)"
if [[ "$component" != "$package"/* ]]; then
  echo "::error::Aucune activite de lancement trouvee pour $package : $component"
  adb shell dumpsys package "$package" | head -80 || true
  exit 1
fi
echo "Activite de lancement : $component"
# am start -W renvoie Status/LaunchState/TotalTime : une erreur de resolution est visible ici.
adb shell am start -W -n "$component" | tr -d '\r' || true
sleep 15
pid="$(running_pid)"
if [[ -z "$pid" ]]; then
  diagnostic
  echo "::error::SCOLIA Console se ferme après le lancement."
  exit 1
fi
# L'application doit rester ouverte, pas seulement demarrer.
sleep 10
pid2="$(running_pid)"
if [[ -z "$pid2" || "$pid2" != "$pid" ]]; then
  diagnostic
  echo "::error::SCOLIA Console a redémarré ou s'est fermée après quelques secondes."
  exit 1
fi
# L'activite principale doit etre au premier plan (pas seulement un processus en arriere-plan).
if ! adb shell dumpsys activity activities | grep -q "$package"; then
  diagnostic
  echo "::error::Aucune activité SCOLIA Console visible."
  exit 1
fi
echo "SCOLIA Console ouverte et stable sur émulateur Android (PID: $pid)."
