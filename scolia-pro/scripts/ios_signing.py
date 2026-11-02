"""Preflight for SCOLIA native iOS Apple distribution signing on GitHub macOS runners.

Only profile metadata and keychain public identity descriptions are inspected.
No private key or raw certificate is printed or uploaded.
"""
from __future__ import annotations

import argparse
from dataclasses import dataclass
from datetime import datetime, timezone
import hashlib
from pathlib import Path
import plistlib
import re
import subprocess
import sys

IOS_BUNDLE_ID = "com.jpservices.scolia.ios"
IDENTITY_PATTERN = re.compile(r'^\s*\d+\)\s+([A-Fa-f0-9]{40})\s+"([^"]+)"', re.MULTILINE)


@dataclass(frozen=True)
class ProfileCheck:
    uuid: str
    kind: str
    expires_on: str
    device_count: int
    name: str


def verify_profile(profile: dict, bundle_id: str, codesign_key: str, identities_text: str,
                   requested_channel: str = "auto", now: datetime | None = None) -> ProfileCheck:
    now = now or datetime.now(timezone.utc)
    if now.tzinfo is None:
        now = now.replace(tzinfo=timezone.utc)
    expiration = profile.get("ExpirationDate")
    if not isinstance(expiration, datetime):
        raise ValueError("Profil Apple invalide : expiration absente.")
    if expiration.tzinfo is None:
        expiration = expiration.replace(tzinfo=timezone.utc)
    if expiration <= now:
        raise ValueError("Le profil Apple a expiré : générez un nouveau .mobileprovision.")

    uuid = profile.get("UUID")
    if not isinstance(uuid, str) or not re.fullmatch(r"[0-9a-fA-F-]{36}", uuid):
        raise ValueError("Le profil Apple ne contient pas d'UUID valide.")
    team_ids = profile.get("TeamIdentifier")
    if not isinstance(team_ids, list) or len(team_ids) != 1 or not isinstance(team_ids[0], str):
        raise ValueError("Le profil Apple n'indique pas une équipe Apple unique.")
    team = team_ids[0]
    entitlement = profile.get("Entitlements") or {}
    if entitlement.get("application-identifier") != f"{team}.{bundle_id}":
        raise ValueError(f"Le profil Apple ne correspond pas à l'identifiant de SCOLIA iOS ({bundle_id}).")
    if entitlement.get("get-task-allow") is not False:
        raise ValueError("Profil de développement détecté. Une distribution iOS exige un profil de distribution.")
    if profile.get("ProvisionsAllDevices"):
        raise ValueError("Profil Enterprise non pris en charge par ce processus de distribution.")

    devices = profile.get("ProvisionedDevices") or []
    if not isinstance(devices, list):
        raise ValueError("Profil Apple : liste d'appareils invalide.")
    kind = "ad-hoc" if devices else "app-store"
    if requested_channel not in ("auto", kind):
        raise ValueError(f"Le profil est de type {kind}, mais le canal demandé est {requested_channel}.")

    # security find-identity supplies the fingerprints of usable identities WITH a private key.
    available = IDENTITY_PATTERN.findall(identities_text)
    matched = [(sha.upper(), name) for sha, name in available
               if name == codesign_key or sha.upper() == codesign_key.upper()]
    if not matched:
        raise ValueError("La signature Apple demandée n'existe pas avec sa clé privée dans le trousseau temporaire.")
    signing_sha, label = matched[0]
    if not (label.startswith("Apple Distribution:") or label.startswith("iPhone Distribution:")):
        raise ValueError("L'identité Apple sélectionnée n'est pas un certificat de distribution.")
    certs = profile.get("DeveloperCertificates") or []
    if not any(isinstance(der, bytes) and hashlib.sha1(der).hexdigest().upper() == signing_sha for der in certs):
        raise ValueError("Le certificat importé ne figure pas dans le profil Apple fourni.")

    name = profile.get("Name", "")
    return ProfileCheck(uuid=uuid, kind=kind, expires_on=expiration.date().isoformat(),
                        device_count=len(devices), name=name if isinstance(name, str) else "")


def main() -> int:
    ap = argparse.ArgumentParser(description="Vérifier le profil de signature iOS de SCOLIA.")
    ap.add_argument("--profile", type=Path, required=True)
    ap.add_argument("--keychain", type=Path, required=True)
    ap.add_argument("--bundle-id", default=IOS_BUNDLE_ID)
    ap.add_argument("--codesign-key", required=True)
    ap.add_argument("--channel", choices=["auto", "ad-hoc", "app-store"], default="auto")
    ap.add_argument("--expected-profile-name", default="")
    ap.add_argument("--github-env", type=Path)
    args = ap.parse_args()
    try:
        cms = subprocess.run(["security", "cms", "-D", "-i", str(args.profile)],
                             capture_output=True, check=True)
        profile = plistlib.loads(cms.stdout)
        result = subprocess.run(["security", "find-identity", "-v", "-p", "codesigning", str(args.keychain)],
                                capture_output=True, text=True, check=True)
        check = verify_profile(profile, args.bundle_id, args.codesign_key, result.stdout, args.channel)
        if args.expected_profile_name and args.expected_profile_name not in (check.name, check.uuid):
            raise ValueError("Le nom du profil demandé ne correspond pas au .mobileprovision fourni.")
        print(f"Signature vérifiée : {check.kind}, profil valide jusqu'au {check.expires_on}, "
              f"{check.device_count} appareils enregistrés.")
        if args.github_env:
            with args.github_env.open("a", encoding="utf-8") as f:
                f.write(f"IOS_PROFILE_UUID={check.uuid}\nIOS_DISTRIBUTION_CHANNEL={check.kind}\n")
        return 0
    except (ValueError, plistlib.InvalidFileException, subprocess.CalledProcessError, OSError) as exc:
        msg = str(exc) if isinstance(exc, ValueError) else "Impossible de lire le profil ou les identités de signature Apple."
        print(f"::error::{msg}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
