"""Fail closed if the experimental APK is not isolated or lacks Flutter runtime files."""
from pathlib import Path
import os
import re
import shutil
import subprocess
import zipfile

root = Path(__file__).resolve().parent.parent
mobile = root.parent / "Scolia.Mobile/bin/Release"
candidates = sorted(mobile.rglob("*-Signed.apk"))
if not candidates:
    raise RuntimeError("No signed validation APK found")
sdk = Path(os.environ.get("ANDROID_HOME") or os.environ["ANDROID_SDK_ROOT"])
tools = sorted((p for p in (sdk / "build-tools").iterdir() if
                (p / "aapt").is_file() and (p / "apksigner").is_file()),
               key=lambda p: tuple(int(x) for x in re.findall(r"\d+", p.name)))
if not tools:
    raise RuntimeError("Android build tools unavailable")
aapt = tools[-1] / "aapt"
signer = tools[-1] / "apksigner"
out = root / "build/validation"
out.mkdir(parents=True, exist_ok=True)
verified = []
for apk in candidates:
    info = subprocess.check_output([str(aapt), "dump", "badging", str(apk)], text=True)
    if not info.startswith("package: name='com.jpservices.scolia.fluttervalidation' "):
        continue
    subprocess.run([str(signer), "verify", str(apk)], check=True)
    with zipfile.ZipFile(apk) as z:
        names = set(z.namelist())
        for abi in ("arm64-v8a", "armeabi-v7a"):
            for lib in ("libflutter.so", "libapp.so"):
                if f"lib/{abi}/{lib}" not in names:
                    raise RuntimeError(f"Missing {abi}/{lib}")
        if "assets/flutter_assets/AssetManifest.bin" not in names:
            raise RuntimeError("Flutter assets missing")
    verified.append(apk)
if not verified:
    raise RuntimeError("No isolated Flutter validation package")
# Avoid ambiguous candidates if publish and bin both contain the same APK.
import hashlib
digests = {hashlib.sha256(p.read_bytes()).hexdigest() for p in verified}
if len(digests) != 1:
    raise RuntimeError("Multiple distinct validation APKs")
shutil.copyfile(verified[0], out / "SCOLIA_Flutter_VALIDATION.apk")
(out / "IMPORTANT.txt").write_text(
    "APK EXPERIMENTAL, distinct de SCOLIA Pro. Ne pas connecter de données scolaires réelles. "
    "La vérification statique du paquet ne prouve pas le démarrage sur téléphone.\n", encoding="utf-8")
print("Isolated Flutter validation APK verified")
