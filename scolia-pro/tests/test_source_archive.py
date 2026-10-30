import importlib.util
import pathlib
import subprocess
import tempfile
import unittest
import zipfile

spec = importlib.util.spec_from_file_location("source_archive", pathlib.Path(__file__).parents[1] / "scripts" / "archiver-sources.py")
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)


class SourceArchiveTests(unittest.TestCase):
    def test_tracked_runtime_data_and_keys_never_enter_delivery(self):
        with tempfile.TemporaryDirectory() as folder:
            root = pathlib.Path(folder)
            subprocess.run(["git", "init", "-q"], cwd=root, check=True)
            files = {
                "scolia-pro/src/Main.cs": "source",
                ".github/workflows/build.yml": "workflow",
                "scolia-pro/SCOLIA/hors-ligne/session.json": "PRIVATE-SESSION",
                "scolia-pro/donnees/scolia.db": "PRIVATE-SCHOOL",
                "scolia-pro/src/.env": "PRIVATE-SECRET",
                "scolia-pro/src/app.keystore": "PRIVATE-KEY",
            }
            for name, data in files.items():
                path = root / name
                path.parent.mkdir(parents=True, exist_ok=True)
                path.write_text(data)
            subprocess.run(["git", "add", "."], cwd=root, check=True)
            subprocess.run(["git", "-c", "user.name=Test", "-c", "user.email=test@example.invalid", "commit", "-qm", "fixture"], cwd=root, check=True)
            output = root / "delivery.zip"
            self.assertEqual(2, module.archive(root, output))
            with zipfile.ZipFile(output) as archive:
                self.assertIn("scolia-pro/src/Main.cs", archive.namelist())
                self.assertIn(".github/workflows/build.yml", archive.namelist())
                self.assertFalse(any(b"PRIVATE-" in archive.read(name) for name in archive.namelist()))


if __name__ == "__main__":
    unittest.main()
