#!/usr/bin/env python3
"""Contrôle les finitions natives avec quatre comptes de démonstration et plusieurs formats."""
from concurrent.futures import ThreadPoolExecutor
from collections import deque
import argparse
import os
from pathlib import Path
import re
import socket
import subprocess
import tempfile
import threading
import time
import urllib.error
import urllib.request


SCENARIOS = {
    'mobile': ('390x844', True, False, 1),
    'mobile-sombre': ('390x844', True, True, 1),
    'mobile-compact': ('320x640', True, False, 1),
    'mobile-texte': ('360x740', True, False, 1.3),
    'mobile-paysage': ('640x360', True, False, 1),
    'bureau': ('1440x1000', False, False, 1),
    'bureau-sombre': ('1440x1000', False, True, 1),
    'bureau-compact': ('1024x768', False, False, 1.2),
}
ACCOUNTS = ['admin', 'comptable', 'promoteur', 'enseignant']


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--dotnet', default='dotnet')
    parser.add_argument('--configuration', default='Release')
    parser.add_argument('--output', default='artifacts/finitions-roles')
    parser.add_argument('--scenarios', choices=SCENARIOS, nargs='+', default=list(SCENARIOS))
    parser.add_argument('--server', help='Réutiliser un serveur de démonstration déjà préparé, sans réinstallation.')
    args = parser.parse_args()
    root = Path(__file__).resolve().parents[1]
    output = (root / args.output).resolve()
    output.mkdir(parents=True, exist_ok=True)
    capture = root / f'tests/Scolia.Bureau.Captures/bin/{args.configuration}/net10.0/Scolia.Bureau.Captures.dll'
    api = root / f'src/Scolia.Api/bin/{args.configuration}/net10.0/SCOLIA-Serveur.dll'
    if not capture.exists() or (not args.server and not api.exists()):
        raise SystemExit('Compiler les projets Scolia.Api (net10.0) et Scolia.Bureau.Captures avant ce contrôle.')

    failures = []
    with tempfile.TemporaryDirectory(prefix='scolia-finitions-') as folder:
        temporary = Path(folder)
        server = None
        server_log = None
        address = args.server
        try:
            if not address:
                while True:
                    with socket.socket() as listener:
                        listener.bind(('127.0.0.1', 0))
                        port = listener.getsockname()[1]
                    if port != 50002:
                        break
                address = f'http://127.0.0.1:{port}/'
                config = temporary / 'server-config'
                config.mkdir()
                environment = dict(os.environ, ASPNETCORE_ENVIRONMENT='Production',
                    Scolia__Stockage__DataDirectory=str(temporary / 'data'), Scolia__Reseau__Port=str(port),
                    Scolia__OuvrirNavigateur='false', XDG_CONFIG_HOME=str(config))
                server_log = (output / 'serveur.log').open('w', encoding='utf-8')
                server = subprocess.Popen([args.dotnet, str(api)], cwd=root, env=environment,
                    stdout=server_log, stderr=subprocess.STDOUT)
                deadline = time.monotonic() + 90
                while True:
                    if server.poll() is not None:
                        raise RuntimeError('Le serveur de démonstration est arrêté ; voir serveur.log.')
                    try:
                        with urllib.request.urlopen(address + 'api/sante', timeout=2):
                            break
                    except (OSError, urllib.error.URLError):
                        if time.monotonic() >= deadline:
                            raise TimeoutError('Le serveur de démonstration ne répond pas.')
                        time.sleep(0.2)
                with (output / 'preparation.log').open('w', encoding='utf-8') as log:
                    subprocess.run(['node', str(root / 'scripts/audit/preparer-roles.mjs'), address],
                        cwd=root, stdout=log, stderr=subprocess.STDOUT, check=True, timeout=120)

            # Les scénarios partagent une adresse locale. Respecter la limite de connexion
            # du serveur ; les protections de production restent actives pendant les tests.
            admission = threading.Condition()
            attempts = deque([time.monotonic()] * 4)
            cooldown = [0.0]

            def admit():
                with admission:
                    while True:
                        now = time.monotonic()
                        while attempts and now - attempts[0] >= 61:
                            attempts.popleft()
                        if now >= cooldown[0] and len(attempts) < 8:
                            attempts.append(now)
                            return
                        admission.wait(timeout=1)

            def render(case):
                name, account = case
                size, mobile, dark, scale = SCENARIOS[name]
                destination = output / name / account
                destination.mkdir(parents=True, exist_ok=True)
                for report in ['debordements.txt', 'finitions-verifiees.txt']:
                    (destination / report).unlink(missing_ok=True)
                config = temporary / (name + '-' + account)
                config.mkdir()
                environment = dict(os.environ, XDG_CONFIG_HOME=str(config), SCOLIA_MOBILE=str(int(mobile)),
                    SCOLIA_CAPTURE_DARK=str(int(dark)), SCOLIA_TAILLE=size, SCOLIA_COMPTE=account,
                    SCOLIA_PREMIUM_TEXTE=str(scale), SCOLIA_ASTUCES='0')
                log_path = destination / 'verification.log'
                for retry in range(2):
                    admit()
                    with log_path.open('w', encoding='utf-8') as log:
                        result = subprocess.run([args.dotnet, str(capture), str(destination), address,
                            'Scolia2026!', 'finitions-roles'], cwd=root, env=environment,
                            stdout=log, stderr=subprocess.STDOUT, timeout=180, check=False)
                    if not result.returncode or 'Trop de tentatives' not in log_path.read_text(encoding='utf-8') or retry:
                        break
                    with admission:
                        cooldown[0] = max(cooldown[0], time.monotonic() + 61)
                    print(f'ATTENTE {name}/{account} : fenêtre de connexion du serveur.', flush=True)
                report = destination / 'finitions-verifiees.txt'
                status = report.read_text(encoding='utf-8').splitlines()[0] if report.exists() else 'rapport absent'
                layout = destination / 'debordements.txt'
                overflows = sum(int(value) for value in re.findall(r': (\d+) débordement\(s\)',
                    layout.read_text(encoding='utf-8') if layout.exists() else ''))
                failed = result.returncode != 0 or not report.exists() or overflows > 0
                print(f'{"ECHEC" if failed else "OK"} {name} {size} · {status} Débordements : {overflows}.', flush=True)
                return name + '/' + account if failed else None

            cases = [(name, account) for name in args.scenarios for account in ACCOUNTS]
            with ThreadPoolExecutor(max_workers=4) as executor:
                failures = [failure for failure in executor.map(render, cases) if failure]
        finally:
            if server is not None:
                server.terminate()
                try:
                    server.wait(timeout=10)
                except subprocess.TimeoutExpired:
                    server.kill()
                    server.wait()
            if server_log is not None:
                server_log.close()
    if failures:
        raise SystemExit('Parcours en échec : ' + ', '.join(failures))


if __name__ == '__main__':
    main()
