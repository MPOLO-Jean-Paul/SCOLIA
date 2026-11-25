"""Compile the Java bridge against the exact Flutter embedding from the generated AAR POM."""
from pathlib import Path
import os
import re
import subprocess
import urllib.request
import xml.etree.ElementTree as ET
import zipfile

root = Path(__file__).resolve().parent.parent
repo = root / "build/host/outputs/repo"
ns = {"m": "http://maven.apache.org/POM/4.0.0"}
versions = set()
for pom in repo.rglob("*.pom"):
    for dep in ET.parse(pom).findall(".//m:dependency", ns):
        if dep.findtext("m:artifactId", namespaces=ns) == "flutter_embedding_release":
            versions.add(dep.findtext("m:version", namespaces=ns))
if len(versions) != 1:
    raise RuntimeError("Expected exactly one Flutter release embedding version in AAR POM")
version = versions.pop()
if not version or not re.fullmatch(r"[A-Za-z0-9_.-]+", version):
    raise RuntimeError("Invalid Flutter embedding version")
out = root / "build/bridge"
out.mkdir(parents=True, exist_ok=True)
embedding = out / "embedding.aar"
url = ("https://storage.googleapis.com/download.flutter.io/io/flutter/flutter_embedding_release/"
       f"{version}/flutter_embedding_release-{version}.aar")
with urllib.request.urlopen(url, timeout=120) as response:
    embedding.write_bytes(response.read())
with zipfile.ZipFile(embedding) as archive:
    (out / "embedding.jar").write_bytes(archive.read("classes.jar"))
sdk = Path(os.environ.get("ANDROID_HOME") or os.environ["ANDROID_SDK_ROOT"])
platforms = sorted((p for p in (sdk / "platforms").glob("android-*")
                    if p.name[8:].isdigit() and (p / "android.jar").is_file()),
                   key=lambda p: int(p.name[8:]))
if not platforms:
    raise RuntimeError("Android SDK platform not installed")
classes = out / "classes"
classes.mkdir(exist_ok=True)
sources = sorted((root / "android_bridge/src").rglob("*.java"))
subprocess.run(["javac", "--release", "17", "-cp", os.pathsep.join(
    [str(out / "embedding.jar"), str(platforms[-1] / "android.jar")]),
    "-d", str(classes), *map(str, sources)], check=True)
subprocess.run(["jar", "--create", "--file", str(out / "scolia-flutter-bridge.jar"),
                "-C", str(classes), "."], check=True)
print("Android bridge JAR compiled against", version)

# Collect only the release module and Flutter engine binaries. The .NET host
# already owns AndroidX; dependency conflicts are checked by its own APK build.
import shutil
module_aars = list(repo.rglob("flutter_release-*.aar"))
if len(module_aars) != 1:
    raise RuntimeError("Expected exactly one release module AAR")
shutil.copyfile(module_aars[0], out / "flutter-module.aar")
for artifact, abi in (("arm64_v8a_release", "arm64-v8a"),
                      ("armeabi_v7a_release", "armeabi-v7a")):
    base = ("https://storage.googleapis.com/download.flutter.io/io/flutter/"
            f"{artifact}/{version}/{artifact}-{version}")
    target = out / f"{artifact}.jar"
    with urllib.request.urlopen(base + ".jar", timeout=120) as response:
        target.write_bytes(response.read())
    with zipfile.ZipFile(target) as archive:
        matches = [n for n in archive.namelist() if n.endswith(f"/{abi}/libflutter.so")]
        if len(matches) != 1:
            raise RuntimeError(f"Missing exact Flutter native engine for {abi}")
        native = out / "native" / abi
        native.mkdir(parents=True, exist_ok=True)
        (native / "libflutter.so").write_bytes(archive.read(matches[0]))

# Generate a separate manifest from the current client, without modifying it.
manifest = root.parent / "Scolia.Mobile/AndroidManifest.xml"
xml = manifest.read_text(encoding="utf-8")
if xml.count('com.jpservices.scolia.fichiers') != 1:
    raise RuntimeError("Unexpected FileProvider authority")
xml = xml.replace('com.jpservices.scolia.fichiers',
                  'com.jpservices.scolia.fluttervalidation.fichiers')
xml = xml.replace('android:label="SCOLIA"', 'android:label="SCOLIA Flutter Validation"')
(out / "AndroidManifest.validation.xml").write_text(xml, encoding="utf-8")
