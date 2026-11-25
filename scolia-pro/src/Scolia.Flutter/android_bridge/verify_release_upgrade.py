"""Check an official signed upgrade without accessing signing secrets."""
import argparse
import re
import subprocess
import zipfile
from pathlib import Path

PACKAGE = "com.jpservices.scolia"

def run(*args):
    return subprocess.check_output(args, text=True, stderr=subprocess.STDOUT)

def identify(apk, aapt, signer):
    if not apk.is_file():
        raise ValueError(f"APK absent: {apk.name}")
    info = run(aapt, "dump", "badging", str(apk))
    match = re.search(r"^package: name='([^']+)' versionCode='(\d+)'", info, re.M)
    if not match or match.group(1) != PACKAGE:
        raise ValueError("Package non officiel ou métadonnées invalides")
    certs = run(signer, "verify", "--print-certs", str(apk))
    fingerprints = frozenset(re.findall(
        r"^Signer #\d+ certificate SHA-256 digest: ([0-9a-fA-F]{64})\s*$", certs, re.M))
    if not fingerprints:
        raise ValueError("Aucun certificat de signature vérifié")
    return int(match.group(2)), {x.lower() for x in fingerprints}

def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument("--previous", required=True, type=Path)
    p.add_argument("--candidate", required=True, type=Path)
    p.add_argument("--aapt", required=True)
    p.add_argument("--apksigner", required=True)
    a = p.parse_args()
    previous, old_certs = identify(a.previous, a.aapt, a.apksigner)
    candidate, new_certs = identify(a.candidate, a.aapt, a.apksigner)
    if candidate <= previous:
        raise ValueError("versionCode doit être strictement supérieur")
    if old_certs != new_certs:
        raise ValueError("Signature différente de l'APK officiel de référence")
    with zipfile.ZipFile(a.candidate) as apk:
        files = set(apk.namelist())
        for abi in ("arm64-v8a", "armeabi-v7a"):
            for lib in ("libapp.so", "libflutter.so"):
                if f"lib/{abi}/{lib}" not in files:
                    raise ValueError(f"Flutter absent : {abi}/{lib}")
        if "assets/flutter_assets/AssetManifest.bin" not in files:
            raise ValueError("Assets Flutter absents")
    print(f"PASS: package officiel, versionCode {previous} -> {candidate}, certificats identiques et Flutter ARM présent")
    print("La migration des données reste à vérifier sur un appareil de test.")

if __name__ == "__main__":
    main()
