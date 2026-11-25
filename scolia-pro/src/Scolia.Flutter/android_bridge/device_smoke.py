"""Smoke test on a dedicated ARM Android test device, never a production installation."""
import argparse
import re
import subprocess
import time
from pathlib import Path

PACKAGE = "com.jpservices.scolia.fluttervalidation"
PRODUCTION = "com.jpservices.scolia"
MARKERS = ("validation:first-frame", "validation:bridge-ready")


def run(*args, timeout=90, check=True):
    result = subprocess.run(args, capture_output=True, text=True, timeout=timeout)
    if check and result.returncode:
        raise RuntimeError(f"Command failed: {args[0]} {args[1]} (exit {result.returncode})")
    return result.stdout.strip()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--serial", required=True, help="Explicit ADB device serial")
    parser.add_argument("--apk", type=Path, required=True)
    parser.add_argument("--aapt", required=True, help="Path to Android build-tools aapt")
    parser.add_argument("--apksigner", required=True, help="Path to Android build-tools apksigner")
    parser.add_argument("--isolated-device", action="store_true", required=True,
                        help="Confirm dedicated test device, isolated from real school servers")
    args = parser.parse_args()

    if not args.apk.is_file():
        raise RuntimeError("APK absent")
    info = run(args.aapt, "dump", "badging", str(args.apk))
    if not info.startswith(f"package: name='{PACKAGE}' "):
        raise RuntimeError("Refusing any APK except the isolated Flutter validation package")
    run(args.apksigner, "verify", str(args.apk))

    def adb(*cmd, **kwargs):
        return run("adb", "-s", args.serial, *cmd, **kwargs)

    if adb("get-state") != "device":
        raise RuntimeError("ADB device unavailable")
    abi = adb("shell", "getprop", "ro.product.cpu.abilist")
    if not any(x in abi.split(",") for x in ("arm64-v8a", "armeabi-v7a")):
        raise RuntimeError("This APK requires ARM Android")
    for package in (PRODUCTION, PACKAGE):
        if "package:" in adb("shell", "pm", "path", package, check=False):
            raise RuntimeError("Refusing a device with a pre-existing SCOLIA installation")
    if "Success" not in adb("install", "-g", str(args.apk), timeout=240):
        raise RuntimeError("Validation APK installation failed")

    component = adb("shell", "cmd", "package", "resolve-activity", "--brief",
                    "-a", "android.intent.action.MAIN", "-c",
                    "android.intent.category.LAUNCHER", PACKAGE).splitlines()[-1]
    if not component.startswith(PACKAGE + "/"):
        raise RuntimeError("Cannot resolve isolated launcher")

    def launch():
        result = adb("shell", "am", "start", "-W", "-n", component)
        if "Status: ok" not in result:
            raise RuntimeError("Android did not start the validation activity")

    def observe(previous=None):
        deadline = time.monotonic() + 90
        while time.monotonic() < deadline:
            pid = adb("shell", "pidof", PACKAGE, check=False).split()
            if len(pid) == 1 and re.fullmatch(r"\d+", pid[0]):
                logs = adb("logcat", "-d", "--pid", pid[0], "-s",
                           "SCOLIA-Flutter:I", "AndroidRuntime:E")
                if "FATAL EXCEPTION" in logs or "Interface expérimentale indisponible" in logs:
                    raise RuntimeError("Android runtime or Flutter host failure")
                counts = tuple(logs.count(marker) for marker in MARKERS)
                old_counts = previous[1] if previous and previous[0] == pid[0] else (0, 0)
                resumed = adb("shell", "dumpsys", "activity", "activities")
                foreground = any(PACKAGE + "/" in line and
                                 ("mResumedActivity" in line or "topResumedActivity" in line)
                                 for line in resumed.splitlines())
                if foreground and all(n > o for n, o in zip(counts, old_counts)):
                    return pid[0], counts
            time.sleep(2)
        raise RuntimeError("No verified foreground Flutter frame and native snapshot within timeout")

    launch()
    first = observe()
    print("PASS: cold startup, Flutter first frame, real .NET bridge response")
    adb("shell", "input", "keyevent", "KEYCODE_HOME")
    time.sleep(3)
    launch()
    observe(first)
    print("PASS: background/foreground recovery with new frame and native response")
    print("No school data, no real login, no uninstall or data clearing performed.")


if __name__ == "__main__":
    main()
