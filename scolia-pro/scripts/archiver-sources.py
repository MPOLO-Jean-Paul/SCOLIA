#!/usr/bin/env python3
"""Archive les sources suivies par Git, sans cache, base, session ou clé de signature."""
import hashlib
import json
import pathlib
import subprocess
import sys
import zipfile


def archive(repository: pathlib.Path, output: pathlib.Path) -> int:
    excluded = {"SCOLIA", "donnees", "hors-ligne", "artifacts", "bin", "obj", "node_modules",
                "TestResults", "test-results", "playwright-report", ".git", "__pycache__"}
    secret_extensions = {".db", ".db-shm", ".db-wal", ".sqlite", ".sqlite3", ".keystore", ".jks", ".pfx", ".p12", ".key"}
    names = subprocess.check_output(["git", "ls-tree", "-rz", "--name-only", "HEAD"], cwd=repository).split(b"\0")
    output.parent.mkdir(parents=True, exist_ok=True)
    manifest = {}
    with zipfile.ZipFile(output, "w", zipfile.ZIP_DEFLATED) as result:
        for raw in names:
            if not raw:
                continue
            name = raw.decode("utf-8")
            path = pathlib.PurePosixPath(name)
            if not (name.startswith("scolia-pro/") or name.startswith(".github/") or name in {"README.md", ".gitignore"}):
                continue
            if any(part.lower() in {value.lower() for value in excluded} for part in path.parts) or path.suffix.lower() in secret_extensions:
                continue
            if (path.name.startswith(".env") and not path.name.endswith((".example", ".sample"))) or path.name in {"session.json", "credentials.json", "protection.key"}:
                continue
            data = subprocess.check_output(["git", "show", f"HEAD:{name}"], cwd=repository)
            result.writestr(name, data)
            manifest[name] = hashlib.sha256(data).hexdigest()
        result.writestr("SOURCE-MANIFEST.sha256.json", json.dumps(manifest, indent=2, ensure_ascii=False))
    checksum = hashlib.sha256(output.read_bytes()).hexdigest()
    output.with_suffix(".sha256").write_text(f"{checksum}  {output.name}\n", encoding="utf-8")
    return len(manifest)


if __name__ == "__main__":
    root = pathlib.Path(subprocess.check_output(["git", "rev-parse", "--show-toplevel"], text=True).strip())
    destination = pathlib.Path(sys.argv[1]) if len(sys.argv) > 1 else pathlib.Path("artifacts/SCOLIA_Pro_Sources.zip")
    print(f"Archive de sources : {archive(root, destination)} fichiers, sans données d'exploitation.")
