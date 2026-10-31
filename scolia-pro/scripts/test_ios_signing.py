"""Regression checks for native iOS signing safeguards (no Apple credentials needed)."""
from datetime import datetime, timedelta, timezone
import hashlib
import unittest
from ios_signing import verify_profile

DER = b"fixture-for-test-only-not-a-real-certificate"
SHA = hashlib.sha1(DER).hexdigest().upper()
KEY = "Apple Distribution: SCOLIA (TEAM123456)"
IDENTITIES = f'  1) {SHA} "{KEY}"\n     1 valid identities found'
UUID = "12345678-1234-1234-1234-123456789abc"


def sample(devices=None, expiry_days=60):
    p = {
        "UUID": UUID,
        "Name": "SCOLIA Ad Hoc",
        "ExpirationDate": datetime.now(timezone.utc) + timedelta(days=expiry_days),
        "TeamIdentifier": ["TEAM123456"],
        "Entitlements": {"application-identifier": "TEAM123456.com.jpservices.scolia.ios", "get-task-allow": False},
        "DeveloperCertificates": [DER],
    }
    if devices is not None:
        p["ProvisionedDevices"] = devices
    return p


class IosDistributionPreflightTests(unittest.TestCase):
    def test_ad_hoc_matches_registered_device_and_certificate(self):
        info = verify_profile(sample(devices=["device-a"]), "com.jpservices.scolia.ios", KEY,
                              IDENTITIES, "ad-hoc")
        self.assertEqual("ad-hoc", info.kind)
        self.assertEqual(1, info.device_count)
        self.assertEqual(UUID, info.uuid)

    def test_app_store_distribution_without_registered_devices(self):
        info = verify_profile(sample(), "com.jpservices.scolia.ios", KEY, IDENTITIES, "app-store")
        self.assertEqual("app-store", info.kind)

    def test_rejects_expired_profile(self):
        with self.assertRaisesRegex(ValueError, "expiré"):
            verify_profile(sample(expiry_days=-1), "com.jpservices.scolia.ios", KEY, IDENTITIES)

    def test_rejects_wrong_bundle_id(self):
        with self.assertRaisesRegex(ValueError, "identifiant"):
            verify_profile(sample(), "autre.application", KEY, IDENTITIES)

    def test_rejects_development_profile(self):
        p = sample()
        p["Entitlements"]["get-task-allow"] = True
        with self.assertRaisesRegex(ValueError, "développement"):
            verify_profile(p, "com.jpservices.scolia.ios", KEY, IDENTITIES)

    def test_rejects_unknown_signing_certificate(self):
        with self.assertRaisesRegex(ValueError, "n'existe pas"):
            verify_profile(sample(), "com.jpservices.scolia.ios", KEY, '0 valid identities found')

    def test_rejects_profile_signed_by_another_certificate(self):
        p = sample()
        p["DeveloperCertificates"] = [b"different"]
        with self.assertRaisesRegex(ValueError, "ne figure pas"):
            verify_profile(p, "com.jpservices.scolia.ios", KEY, IDENTITIES)

    def test_rejects_wrong_distribution_channel(self):
        with self.assertRaisesRegex(ValueError, "canal"):
            verify_profile(sample(devices=["device-a"]), "com.jpservices.scolia.ios", KEY,
                           IDENTITIES, "app-store")

    def test_rejects_enterprise_profile(self):
        p = sample()
        p["ProvisionsAllDevices"] = True
        with self.assertRaisesRegex(ValueError, "Enterprise"):
            verify_profile(p, "com.jpservices.scolia.ios", KEY, IDENTITIES)

    def test_accepts_signature_fingerprint_as_key(self):
        info = verify_profile(sample(), "com.jpservices.scolia.ios", SHA, IDENTITIES)
        self.assertEqual("app-store", info.kind)


if __name__ == "__main__":
    unittest.main()
