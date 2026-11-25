"""Unit tests for the release guard, without signing keys or real APK downloads."""
import io
import sys
import tempfile
import unittest
import zipfile
from contextlib import redirect_stdout
from pathlib import Path
from unittest.mock import patch

import verify_release_upgrade as guard

CERT = "ab" * 32
OTHER = "cd" * 32

class UpgradeGuardTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.old = Path(self.temp.name) / "old.apk"
        self.new = Path(self.temp.name) / "new.apk"
        self.old.touch()
        with zipfile.ZipFile(self.new, "w") as apk:
            for abi in ("arm64-v8a", "armeabi-v7a"):
                for lib in ("libflutter.so", "libapp.so"):
                    apk.writestr(f"lib/{abi}/{lib}", b"fixture")
            apk.writestr("assets/flutter_assets/AssetManifest.bin", b"fixture")

    def check(self, code=201, version="1.2.100", certificate=CERT,
              package=guard.PACKAGE, require_flutter=True):
        def fake(*args):
            old = args[-1] == str(self.old)
            if args[0] == "aapt":
                return (f"package: name='{guard.PACKAGE if old else package}' "
                        f"versionCode='{200 if old else code}' "
                        f"versionName='{'1.2.99' if old else version}'")
            if args[0] == "signer":
                fingerprint = CERT if old else certificate
                return f"V3.0 Signer: certificate SHA-256 digest: {fingerprint}"
            raise AssertionError(args)
        argv = ["verify_release_upgrade.py", "--previous", str(self.old),
                "--candidate", str(self.new), "--aapt", "aapt", "--apksigner", "signer"]
        if require_flutter:
            argv.append("--require-flutter")
        with patch.object(guard, "run", side_effect=fake), patch.object(sys, "argv", argv), redirect_stdout(io.StringIO()):
            guard.main()

    def test_valid_upgrade(self):
        self.check()

    def test_wrong_certificate(self):
        with self.assertRaisesRegex(ValueError, "Signature différente"):
            self.check(certificate=OTHER)

    def test_experimental_package(self):
        with self.assertRaisesRegex(ValueError, "Package non officiel"):
            self.check(package=guard.PACKAGE + ".fluttervalidation")

    def test_version_code_collision(self):
        with self.assertRaisesRegex(ValueError, "versionCode"):
            self.check(code=200)

    def test_version_name_collision(self):
        with self.assertRaisesRegex(ValueError, "versionName"):
            self.check(version="1.2.99")

    def test_missing_flutter(self):
        with zipfile.ZipFile(self.new, "w"):
            pass
        with self.assertRaisesRegex(ValueError, "Flutter absent"):
            self.check()
        self.check(require_flutter=False)

if __name__ == "__main__":
    unittest.main()
