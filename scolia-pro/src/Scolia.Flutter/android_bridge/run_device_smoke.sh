#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
sdk="${ANDROID_HOME:-${ANDROID_SDK_ROOT:-}}"
test -n "$sdk"
tools="$(find "$sdk/build-tools" -mindepth 1 -maxdepth 1 -type d | sort -V | tail -1)"
mkdir -p build/validation/captures
actual_sdk="$(adb -s emulator-5554 shell getprop ro.build.version.sdk | tr -d '\r')"
if [[ -n "${SCOLIA_EXPECT_ANDROID_SDK:-}" && "$actual_sdk" != "$SCOLIA_EXPECT_ANDROID_SDK" ]]; then
  echo "Version Android incorrecte : API $actual_sdk au lieu de $SCOLIA_EXPECT_ANDROID_SDK" >&2
  exit 1
fi
adb -s emulator-5554 shell getprop ro.build.version.release > build/validation/android-version.txt
echo "$actual_sdk" > build/validation/android-sdk.txt
adb -s emulator-5554 shell wm size 1080x2400
adb -s emulator-5554 shell wm density 420
adb -s emulator-5554 shell input keyevent KEYCODE_WAKEUP
adb -s emulator-5554 shell wm dismiss-keyguard
adb -s emulator-5554 shell settings put system screen_off_timeout 600000
extra=()
if [[ -n "${SCOLIA_MIGRATION_SEED_APK:-}" ]]; then extra+=(--previous "$SCOLIA_MIGRATION_SEED_APK"); fi
python3 android_bridge/device_smoke.py --serial emulator-5554 --apk build/validation/SCOLIA_Flutter_VALIDATION.apk --aapt "$tools/aapt" --apksigner "$tools/apksigner" --isolated-device --offline-resume --capture-dir build/validation/captures "${extra[@]}"
