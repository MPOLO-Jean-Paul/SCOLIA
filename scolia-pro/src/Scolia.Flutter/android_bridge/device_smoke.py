"""Smoke test on a dedicated Android test device, never a production installation."""
import argparse
import re
import subprocess
import time
import zipfile
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
    parser.add_argument("--offline-resume", action="store_true",
                        help="Disable external network on a disposable emulator before resume")
    parser.add_argument('--capture-dir', type=Path)
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
    with zipfile.ZipFile(args.apk) as archive:
        files = set(archive.namelist())
    compatible = [x for x in abi.split(",") if
                  f"lib/{x}/libflutter.so" in files and f"lib/{x}/libapp.so" in files]
    if not compatible:
        raise RuntimeError("No compatible Flutter ABI in the validation APK")
    if args.offline_resume and adb("shell", "getprop", "ro.kernel.qemu") != "1":
        raise RuntimeError("Offline network changes are restricted to disposable emulators")
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
        # am start -W can time out on a slow first managed-runtime startup.
        # Only the subsequent foreground + two Flutter markers prove success.
        result = adb("shell", "am", "start", "-W", "-n", component, timeout=120, check=False)
        print("Activity launch response:", result, flush=True)
        if re.search(r"(?im)^\s*(Error|Exception|SecurityException):", result):
            raise RuntimeError("Android rejected the validation activity")

    def observe(previous=None):
        deadline = time.monotonic() + 180
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

    def capture(name):
        if args.capture_dir is None:
            return
        args.capture_dir.mkdir(parents=True, exist_ok=True)
        result = subprocess.run(["adb", "-s", args.serial, "exec-out", "screencap", "-p"],
                                capture_output=True, check=True, timeout=20)
        if not result.stdout.startswith(b"\x89PNG\r\n\x1a\n"):
            raise RuntimeError("Invalid Android screenshot")
        (args.capture_dir / (name + ".png")).write_bytes(result.stdout)

    try:
        launch()
        first = observe()
        capture("android-01-demarrage")
        print("PASS: cold startup, Flutter first frame, real .NET bridge response")
        adb("shell", "input", "keyevent", "KEYCODE_HOME")
        time.sleep(3)
        if args.offline_resume:
            adb("shell", "svc", "wifi", "disable")
            adb("shell", "svc", "data", "disable")
        try:
            launch()
            observe(first)
            capture('android-02-reprise')
            print("PASS: background/foreground recovery with new frame and native response")
            if args.offline_resume:
                print("PASS: Flutter and native channel resume with Wi-Fi/mobile data disabled")
        finally:
            if args.offline_resume:
                adb("shell", "svc", "wifi", "enable")
                adb("shell", "svc", "data", "enable")
        print("No school data, no real login, no uninstall or data clearing performed.")
    except Exception:
        if args.offline_resume and adb("shell", "getprop", "ro.kernel.qemu") == "1":
            # CI emulator only, no actual school accounts or personal logs.
            diagnostic = adb("logcat", "-d", "-b", "crash", "-t", "300", check=False)
            diagnostic += "\n" + adb("logcat", "-d", "-t", "500", "-s",
                                     "SCOLIA-Flutter:V", "SCOLIA:W", "AndroidRuntime:E",
                                     "monodroid:E", "DOTNET:E", "flutter:V",
                                     "FlutterLoader:V", "FlutterJNI:V", "FlutterEngine:V",
                                     "linker:E", check=False)
            print("Isolated emulator diagnostics:\n" + diagnostic, flush=True)
            if args.capture_dir is not None:
                args.capture_dir.mkdir(parents=True, exist_ok=True)
                (args.capture_dir / "android-diagnostic.txt").write_text(diagnostic, encoding="utf-8")
                try:
                    capture("android-echec")
                except Exception:
                    pass
        raise


if __name__ == "__main__":
    main()
