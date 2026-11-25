"""Exercise the actual signed SCOLIA upgrade on a dedicated offline ARM phone.

Does not clear data, uninstall packages, publish releases, or approve rollout.
Requires a clean device: no real SCOLIA installation or school network.
"""
import argparse
import hashlib
import json
import re
import time
from pathlib import Path

from device_smoke import run, MARKERS
from verify_release_upgrade import identify, PACKAGE


def sha256(path):
    with path.open("rb") as source:
        return hashlib.file_digest(source, "sha256").hexdigest()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--serial", required=True)
    parser.add_argument("--previous", required=True, type=Path)
    parser.add_argument("--candidate", required=True, type=Path)
    parser.add_argument("--commit", required=True, help="Exact checkout SHA of the candidate artifact")
    parser.add_argument("--aapt", required=True)
    parser.add_argument("--apksigner", required=True)
    parser.add_argument("--report", required=True, type=Path)
    parser.add_argument("--isolated-clean-arm-device", required=True, action="store_true")
    args = parser.parse_args()
    if not re.fullmatch(r"[0-9a-f]{40}", args.commit):
        raise ValueError("Expected full checkout commit SHA")
    if args.report.exists():
        raise ValueError("Choose a fresh report path; no existing report will be overwritten")
    old_code, old_cert, old_version = identify(args.previous, args.aapt, args.apksigner)
    new_code, new_cert, new_version = identify(args.candidate, args.aapt, args.apksigner)
    expected = {"e5b5f7fa415411eeac4bf734849024ffcf7f93500c6013839931b02038f58980"}
    if old_cert != expected or new_cert != expected:
        raise ValueError("Historical official certificate mismatch")
    if old_code >= new_code or old_version >= new_version:
        raise ValueError("Version must increase")
    run("python3", str(Path(__file__).with_name("verify_release_upgrade.py")),
        "--previous", str(args.previous), "--candidate", str(args.candidate),
        "--aapt", args.aapt, "--apksigner", args.apksigner, "--require-flutter")

    def adb(*command, **kwargs):
        return run("adb", "-s", args.serial, *command, **kwargs)

    def offline():
        if (adb("shell", "settings", "get", "global", "airplane_mode_on") != "1"
                or adb("shell", "settings", "get", "global", "wifi_on") != "0"
                or adb("shell", "settings", "get", "global", "mobile_data") != "0"):
            raise RuntimeError("Manually enable airplane mode and turn off Wi-Fi and mobile data")

    if adb("get-state") != "device":
        raise RuntimeError("ADB device unavailable")
    abi = adb("shell", "getprop", "ro.product.cpu.abilist").split(",")
    if not any(a in abi for a in ("arm64-v8a", "armeabi-v7a")) or adb(
            "shell", "getprop", "ro.kernel.qemu") == "1":
        raise RuntimeError("A physical ARM Android test device is required")
    installed = set(adb("shell", "pm", "list", "packages", "-u").splitlines())
    if any("package:" + p in installed for p in (PACKAGE, PACKAGE + ".fluttervalidation")):
        raise RuntimeError("Refusing device with existing SCOLIA installation")
    offline()

    def install(apk, update=False):
        flags = ["-r"] if update else []
        if "Success" not in adb("install", *flags, str(apk), timeout=240):
            raise RuntimeError("Android installation failed")

    def launch():
        lines = adb("shell", "cmd", "package", "resolve-activity", "--brief",
                    "-a", "android.intent.action.MAIN", "-c",
                    "android.intent.category.LAUNCHER", PACKAGE).splitlines()
        if not lines or not lines[-1].startswith(PACKAGE + "/"):
            raise RuntimeError("Cannot resolve official launcher")
        result = adb("shell", "am", "start", "-W", "-n", lines[-1],
                     timeout=120, check=False)
        if re.search(r"(?im)^\\s*(Error|Exception|SecurityException):", result):
            raise RuntimeError("Android rejected the activity")

    def observe(previous=None):
        deadline = time.monotonic() + 180
        while time.monotonic() < deadline:
            pid = adb("shell", "pidof", PACKAGE, check=False)
            if re.fullmatch(r"\\d+", pid):
                logs = adb("logcat", "-d", "--pid", pid, "-s",
                           "SCOLIA-Flutter:I", "AndroidRuntime:E")
                if any(x in logs for x in ("FATAL EXCEPTION", "Interface expérimentale indisponible",
                                            "Flutter initialization failed")):
                    raise RuntimeError("Flutter host failed")
                counts = tuple(logs.count(marker) for marker in MARKERS)
                earlier = previous[1] if previous and previous[0] == pid else (0, 0)
                resumed = adb("shell", "dumpsys", "activity", "activities")
                foreground = any(PACKAGE + "/" in line and
                                 ("mResumedActivity" in line or "topResumedActivity" in line)
                                 for line in resumed.splitlines())
                if foreground and all(n > o for n, o in zip(counts, earlier)):
                    return pid, counts
            time.sleep(2)
        raise RuntimeError("No confirmed foreground Flutter frame and .NET snapshot")

    install(args.previous)
    launch()
    input("Verify the old app opens on the isolated phone. Do not connect a real school. "
          "Press Enter to apply the in-place update; Ctrl-C to stop: ")
    offline()
    adb("shell", "am", "force-stop", PACKAGE)
    install(args.candidate, update=True)
    offline()
    launch()
    first = observe()
    adb("shell", "input", "keyevent", "KEYCODE_HOME")
    time.sleep(3)
    offline()
    launch()
    observe(first)
    offline()
    report = {
        "commit_declared_by_operator": args.commit,
        "previous_sha256": sha256(args.previous),
        "candidate_sha256": sha256(args.candidate),
        "previous_version_code": old_code,
        "candidate_version_code": new_code,
        "historical_certificate_verified": True,
        "in_place_update": "passed",
        "flutter_native_snapshot_and_resume_offline": "passed",
        "encrypted_school_data_migration": "not tested by this script",
        "business_workflows": "not tested by this script",
        "artifact_provenance": "operator must match SHA and artifact to GitHub Actions",
        "rollout_approval": False,
    }
    args.report.parent.mkdir(parents=True, exist_ok=True)
    with args.report.open("x", encoding="utf-8") as target:
        json.dump(report, target, indent=2)
    print("PASS: signed ARM upgrade and Flutter/.NET offline startup/resume.")
    print("No rollout approval. Validate data, business workflows and provenance separately.")


if __name__ == "__main__":
    main()
