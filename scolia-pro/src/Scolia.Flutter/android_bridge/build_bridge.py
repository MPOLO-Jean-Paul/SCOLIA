"""Compile the Java bridge against the exact Flutter embedding from the generated AAR POM."""
from pathlib import Path
import os
import re
import subprocess
import urllib.request
import urllib.error
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
# Flutter's embedding is distributed as a JAR, not necessarily an AAR.
# Reuse the exact artifact Gradle already resolved when constructing the module.
import shutil
cache = Path(os.environ.get("GRADLE_USER_HOME", str(Path.home() / ".gradle"))) / "caches/modules-2/files-2.1"

def resolve_artifact(artifact, extensions):
    folder = cache / "io.flutter" / artifact / version
    for ext in extensions:
        name = f"{artifact}-{version}.{ext}"
        cached = sorted(folder.glob(f"*/{name}"))
        if cached:
            target = out / name
            shutil.copyfile(cached[0], target)
            return target
    errors = []
    for ext in extensions:
        name = f"{artifact}-{version}.{ext}"
        url = f"https://storage.googleapis.com/download.flutter.io/io/flutter/{artifact}/{version}/{name}"
        try:
            target = out / name
            with urllib.request.urlopen(url, timeout=120) as response:
                target.write_bytes(response.read())
            return target
        except urllib.error.HTTPError as ex:
            if ex.code != 404:
                raise
            errors.append(name)
    raise RuntimeError(f"Flutter artifact unavailable: {', '.join(errors)}")

embedding = resolve_artifact("flutter_embedding_release", ("jar", "aar"))
if embedding.suffix == ".aar":
    with zipfile.ZipFile(embedding) as archive:
        (out / "embedding.jar").write_bytes(archive.read("classes.jar"))
    shutil.copyfile(embedding, out / "embedding-package.aar")
else:
    shutil.copyfile(embedding, out / "embedding.jar")
    # Normalize to an AAR so .NET receives the same packaging input on either layout.
    with zipfile.ZipFile(out / "embedding-package.aar", "w", zipfile.ZIP_DEFLATED) as archive:
        archive.write(out / "embedding.jar", "classes.jar")
        archive.writestr("AndroidManifest.xml",
            '<manifest xmlns:android="http://schemas.android.com/apk/res/android" package="io.flutter.embedding" />')
with zipfile.ZipFile(out / "embedding.jar") as archive:
    if "io/flutter/embedding/engine/FlutterEngine.class" not in archive.namelist():
        raise RuntimeError("Resolved embedding does not contain FlutterEngine")
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
module_aars = list(repo.rglob("flutter_release-*.aar"))
if len(module_aars) != 1:
    raise RuntimeError("Expected exactly one release module AAR")
shutil.copyfile(module_aars[0], out / "flutter-module.aar")
architectures = [("arm64_v8a_release", "arm64-v8a"), ("armeabi_v7a_release", "armeabi-v7a")]
if os.environ.get("SCOLIA_FLUTTER_EMULATOR") == "1":
    architectures = [("x86_64_release", "x86_64")]
for artifact, abi in architectures:
    target = resolve_artifact(artifact, ("jar", "aar"))
    with zipfile.ZipFile(target) as archive:
        matches = [n for n in archive.namelist() if n == f"{abi}/libflutter.so" or n.endswith(f"/{abi}/libflutter.so")]
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
