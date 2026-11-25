#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
sdk="${ANDROID_HOME:-${ANDROID_SDK_ROOT:-}}"
test -n "$sdk"
tools="$(find "$sdk/build-tools" -mindepth 1 -maxdepth 1 -type d | sort -V | tail -1)"
mkdir -p build/validation/captures
adb -s emulator-5554 shell wm size 1080x2400
adb -s emulator-5554 shell wm density 420
adb -s emulator-5554 shell input keyevent KEYCODE_WAKEUP
adb -s emulator-5554 shell wm dismiss-keyguard
adb -s emulator-5554 shell settings put system screen_off_timeout 600000
python3 android_bridge/device_smoke.py --serial emulator-5554 --apk build/validation/SCOLIA_Flutter_VALIDATION.apk --aapt "$tools/aapt" --apksigner "$tools/apksigner" --isolated-device --offline-resume --capture-dir build/validation/captures
