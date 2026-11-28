#!/usr/bin/env python3
"""Rend les interfaces natives contre un serveur et une base de démonstration temporaires."""
import argparse
import json
import os
from pathlib import Path
import re
import socket
import subprocess
import tempfile
import time
import urllib.error
import urllib.request


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--dotnet", default="dotnet")
    parser.add_argument("--configuration", default="Release")
    parser.add_argument("--output", default="artifacts/interfaces-fluent")
    args = parser.parse_args()
    root = Path(__file__).resolve().parents[1]
    output = (root / args.output).resolve()
    output.mkdir(parents=True, exist_ok=True)
    server_dll = root / f"src/Scolia.Api/bin/{args.configuration}/net10.0/SCOLIA-Serveur.dll"
    capture_dll = root / f"tests/Scolia.Bureau.Captures/bin/{args.configuration}/net10.0/Scolia.Bureau.Captures.dll"
    if not server_dll.exists() or not capture_dll.exists():
        raise SystemExit("Compiler Scolia.Api (net10.0) et Scolia.Bureau.Captures avant ce contrôle.")

    # Port éphémère, distinct des ports de l'application et des services de l'environnement.
    with socket.socket() as listener:
        listener.bind(("127.0.0.1", 0))
        port = listener.getsockname()[1]
    if port == 50002:
        raise SystemExit("Port réservé sélectionné ; relancer le contrôle.")
    address = f"http://127.0.0.1:{port}"
    password = "DemoFluent2026!"
    failures = []
    with tempfile.TemporaryDirectory(prefix="scolia-fluent-") as temporary:
        temp = Path(temporary)
        config = temp / "server-config"
        config.mkdir()
        server_env = dict(os.environ, ASPNETCORE_ENVIRONMENT="Production",
                          Scolia__Stockage__DataDirectory=str(temp / "data"),
                          Scolia__Reseau__Port=str(port), Scolia__OuvrirNavigateur="false",
                          XDG_CONFIG_HOME=str(config))
        with (output / "serveur.log").open("w", encoding="utf-8") as server_log:
            server = subprocess.Popen([args.dotnet, str(server_dll)], cwd=root,
                                      env=server_env, stdout=server_log, stderr=subprocess.STDOUT)
            try:
                deadline = time.monotonic() + 90
                while True:
                    if server.poll() is not None:
                        raise RuntimeError("Le serveur de démonstration s'est arrêté ; voir serveur.log.")
                    try:
                        with urllib.request.urlopen(address + "/api/sante", timeout=2):
                            break
                    except (OSError, urllib.error.URLError):
                        if time.monotonic() >= deadline:
                            raise TimeoutError("Le serveur de démonstration ne répond pas.")
                        time.sleep(0.2)
                setup = dict(schoolName="Institut de démonstration Fluent", shortName="SCOLIA Démo",
                             city="Lubumbashi", currencyCode="CDF", currencySymbol="FC",
                             yearLabel="2026-2027", startMonth=9, monthCount=10,
                             adminUserName="admin", adminDisplayName="Administration de démonstration",
                             adminPassword=password, loadDemoData=True,
                             institutionType="ItmIm", governance="Prive")
                request = urllib.request.Request(address + "/api/installation",
                    data=json.dumps(setup).encode(), method="POST",
                    headers={"Content-Type": "application/json", "X-Scolia": "1"})
                with urllib.request.urlopen(request, timeout=90):
                    pass
                scenarios = [("desktop", "1440x1000", False, False),
                             ("desktop-sombre", "1440x1000", False, True),
                             ("mobile", "390x844", True, False),
                             ("mobile-sombre", "390x844", True, True)]
                for name, size, mobile, dark in scenarios:
                    destination = output / name
                    destination.mkdir(exist_ok=True)
                    # Un rapport existant ne doit pas cumuler les résultats d'anciens rendus.
                    (destination / "debordements.txt").unlink(missing_ok=True)
                    (destination / "interfaces-verifiees.txt").unlink(missing_ok=True)
                    config = temp / name
                    config.mkdir()
                    env = dict(os.environ, XDG_CONFIG_HOME=str(config),
                               SCOLIA_MOBILE=str(int(mobile)), SCOLIA_CAPTURE_DARK=str(int(dark)),
                               SCOLIA_TAILLE=size, SCOLIA_ASTUCES="0")
                    with (destination / "verification.log").open("w", encoding="utf-8") as log:
                        result = subprocess.run([args.dotnet, str(capture_dll), str(destination),
                                                 address + "/", password, "visite-fluent"],
                                                cwd=root, env=env, stdout=log, stderr=subprocess.STDOUT,
                                                timeout=240, check=False)
                    report = destination / "interfaces-verifiees.txt"
                    status = report.read_text(encoding="utf-8").splitlines()[0] if report.exists() else "rapport absent"
                    layout_report = destination / "debordements.txt"
                    overflows = sum(int(value) for value in re.findall(r": (\d+) débordement\(s\)",
                        layout_report.read_text(encoding="utf-8") if layout_report.exists() else ""))
                    failed = result.returncode != 0 or overflows > 0
                    if failed:
                        failures.append(name)
                    print(f"{'ECHEC' if failed else 'OK'} {name} {size} : {status} "
                          f"Débordements : {overflows}.", flush=True)
            finally:
                server.terminate()
                try:
                    server.wait(timeout=10)
                except subprocess.TimeoutExpired:
                    server.kill()
                    server.wait()
    if failures:
        raise SystemExit("Interfaces en échec : " + ", ".join(failures))


if __name__ == "__main__":
    main()
