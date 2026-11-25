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
