"""Check an official signed upgrade without accessing signing secrets."""
import argparse
import re
import subprocess
import zipfile
import tempfile
from pathlib import Path

PACKAGE = "com.jpservices.scolia"

def run(*args):
    return subprocess.check_output(args, text=True, stderr=subprocess.STDOUT)

def identify(apk, aapt, signer):
    if not apk.is_file():
        raise ValueError(f"APK absent: {apk.name}")
    info = run(aapt, "dump", "badging", str(apk))
    match = re.search(r"^package: name='([^']+)' versionCode='(\d+)' versionName='([^']+)'", info, re.M)
    if not match or match.group(1) != PACKAGE:
        raise ValueError("Package non officiel ou métadonnées invalides")
    certs = run(signer, "verify", "--print-certs", str(apk))
    fingerprints = {re.sub(r"[\s:]", "", line.split("certificate SHA-256 digest:", 1)[1]).lower()
                    for line in certs.splitlines() if "Signer" in line and "certificate SHA-256 digest:" in line}
    if not fingerprints or any(not re.fullmatch(r"[0-9a-f]{64}", x) for x in fingerprints):
        raise ValueError("Aucun certificat de signature vérifié")
    version = match.group(3)
    if not re.fullmatch(r"\d+\.\d+\.\d+", version):
        raise ValueError("Version officielle invalide")
    return int(match.group(2)), fingerprints, tuple(int(x) for x in version.split("."))

def verify_dex(apk, dexdump):
    required = {b"Lio/flutter/embedding/engine/FlutterEngine;",
                b"Lcom/getkeepsafe/relinker/ReLinker;",
                b"Lcom/getkeepsafe/relinker/ReLinker$Logger;"}
    with tempfile.TemporaryDirectory() as directory:
        for name in sorted(n for n in apk.namelist() if re.fullmatch(r"classes(?:[0-9]+)?\.dex", n)):
            path = Path(directory) / name
            path.write_bytes(apk.read(name))
            output = subprocess.check_output([str(dexdump), "-f", str(path)], stderr=subprocess.PIPE)
            required -= set(re.findall(rb"(?m)^[ \t]*Class descriptor[ \t]*:[ \t]*'([^'\r\n]+)'", output))
            if not required:
                return
    raise ValueError("Classes Flutter manquantes: " + ", ".join(sorted(x.decode("ascii") for x in required)))

def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument("--previous", required=True, type=Path)
    p.add_argument("--candidate", required=True, type=Path)
    p.add_argument("--aapt", required=True)
    p.add_argument("--apksigner", required=True)
    p.add_argument("--require-flutter", action="store_true")
    a = p.parse_args()
    previous, old_certs, old_version = identify(a.previous, a.aapt, a.apksigner)
    candidate, new_certs, new_version = identify(a.candidate, a.aapt, a.apksigner)
    if new_version <= old_version:
        raise ValueError("versionName doit être strictement supérieur")
    if candidate <= previous:
        raise ValueError("versionCode doit être strictement supérieur")
    if old_certs != new_certs:
        raise ValueError("Signature différente de l'APK officiel de référence")
    if a.require_flutter:
        with zipfile.ZipFile(a.candidate) as apk:
            files = set(apk.namelist())
            for abi in ("arm64-v8a", "armeabi-v7a"):
                for lib in ("libapp.so", "libflutter.so"):
                    if f"lib/{abi}/{lib}" not in files:
                        raise ValueError(f"Flutter absent : {abi}/{lib}")
            if "assets/flutter_assets/AssetManifest.bin" not in files:
                raise ValueError("Assets Flutter absents")
            verify_dex(apk, Path(a.apksigner).with_name("dexdump"))
    print(f"PASS: package officiel, versionCode {previous} -> {candidate}, certificats identiques")
    print("La migration des données reste à vérifier sur un appareil de test.")

if __name__ == "__main__":
    main()
