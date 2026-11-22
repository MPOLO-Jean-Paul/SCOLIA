"""Tests de la migration D1 des promotions SCOLIA, sans donnees reelles."""
from pathlib import Path
import sqlite3
import unittest

MIGRATIONS = Path(__file__).resolve().parents[1] / "deploy/cloudflare-central/migrations"
HASH = "d" * 64

class SchoolPromotionsD1Tests(unittest.TestCase):
    def setUp(self):
        self.db = sqlite3.connect(":memory:")
        self.db.execute("PRAGMA foreign_keys=ON")
        for name in ("0001_core.sql", "0002_invitations.sql",
                     "0003_password_identity.sql", "0004_device_sessions.sql",
                     "0005_school_sections.sql", "0006_school_promotions.sql"):
            self.db.executescript((MIGRATIONS / name).read_text(encoding="utf-8"))
        for school in ("a", "b"):
            self.db.execute("INSERT INTO cloud_schools(id,display_name) VALUES(?,?)",
                            (school, "Ecole " + school))
            self.db.execute(
                "INSERT INTO cloud_users(school_id,id,login_normalized,role) "
                "VALUES(?,'secretary','secretariat','secretariat')", (school,))
            self.db.execute(
                "INSERT INTO cloud_devices(school_id,id,user_id,approved_by) "
                "VALUES(?,'pc','secretary','secretary')", (school,))
            self.db.execute(
                """INSERT INTO cloud_section_events
                   (school_id,operation_id,section_id,actor_user_id,device_id,
                    code,name,is_active,expected_revision,request_sha256)
                   VALUES(?,? ,?,'secretary','pc','INF','Infirmiere',1,0,?)""",
                (school, "section-" + school, "sec-" + school, HASH),
            )

    def tearDown(self):
        self.db.close()

    def put(self, school="a", op="prom-1", promotion="inf1",
            section="sec-a", code="INF1", name="Premiere infirmiere",
            active=1, version=0, actor="secretary", device="pc"):
        return self.db.execute(
            """INSERT INTO cloud_promotion_events
               (school_id,operation_id,promotion_id,section_id,
                actor_user_id,device_id,code,name,is_active,
                expected_revision,request_sha256)
               VALUES(?,?,?,?,?,?,?,?,?,?,?)""",
            (school, op, promotion, section, actor, device,
             code, name, active, version, HASH),
        )

    def revision(self, school="a", promotion="inf1"):
        row = self.db.execute(
            "SELECT last_revision FROM cloud_promotions "
            "WHERE school_id=? AND id=?", (school, promotion),
        ).fetchone()
        return row[0] if row else 0

    def test_creation_event_applies_current_catalog_and_audit_atomically(self):
        self.put()
        rev = self.revision()
        self.assertGreater(rev, 0)
        self.assertEqual(("sec-a", "INF1", "Premiere infirmiere", 1, rev),
                         self.db.execute(
                             "SELECT section_id,code,name,is_active,last_revision "
                             "FROM cloud_promotions WHERE school_id='a'"
                         ).fetchone())
        self.assertEqual(("promotion_upsert", "applied"),
                         self.db.execute(
                             "SELECT operation_kind,outcome FROM cloud_operations "
                             "WHERE school_id='a' AND operation_id='prom-1'"
                         ).fetchone())
        self.assertEqual(("promotion", "inf1"),
                         self.db.execute(
                             "SELECT entity_kind,entity_id FROM cloud_change_log "
                             "WHERE school_id='a' AND operation_id='prom-1'"
                         ).fetchone())
        self.put(op="prom-2", code="INF1A", version=rev)
        self.assertGreater(self.revision(), rev)
        self.assertEqual(2, self.db.execute(
            "SELECT COUNT(*) FROM cloud_promotion_events "
            "WHERE school_id='a'").fetchone()[0])

    def test_conflict_or_reused_key_does_not_partially_apply(self):
        self.put()
        rev = self.revision()
        with self.assertRaises(sqlite3.IntegrityError):
            self.put(op="prom-stale", code="INF2")
        with self.assertRaises(sqlite3.IntegrityError):
            self.put(op="prom-1", code="INF2", version=rev)
        self.assertEqual(rev, self.revision())
        self.assertIsNone(self.db.execute(
            "SELECT outcome FROM cloud_operations "
            "WHERE operation_id='prom-stale'").fetchone())

    def test_promotion_must_reference_section_of_same_school(self):
        with self.assertRaises(sqlite3.IntegrityError):
            self.put(school="b", section="sec-a", op="wrong-school")
        with self.assertRaises(sqlite3.IntegrityError):
            self.put(section="unknown", op="unknown-section")
        self.put(school="b", section="sec-b")
        self.assertGreater(self.revision("b"), 0)
        self.put()
        self.assertGreater(self.revision("a"), 0)

    def test_inactive_section_cannot_receive_active_promotion(self):
        section_rev = self.db.execute(
            "SELECT last_revision FROM cloud_sections "
            "WHERE school_id='a' AND id='sec-a'").fetchone()[0]
        self.db.execute(
            """INSERT INTO cloud_section_events
               (school_id,operation_id,section_id,actor_user_id,device_id,
                code,name,is_active,expected_revision,request_sha256)
               VALUES('a','section-off','sec-a','secretary','pc',
                      'INF','Infirmiere',0,?,?)""", (section_rev, HASH))
        with self.assertRaises(sqlite3.IntegrityError):
            self.put(op="active-in-disabled")
        self.put(op="inactive-in-disabled", active=0)
        self.assertEqual(0, self.db.execute(
            "SELECT is_active FROM cloud_promotions "
            "WHERE school_id='a' AND id='inf1'").fetchone()[0])

    def test_unique_codes_and_immutable_events(self):
        self.put()
        with self.assertRaises(sqlite3.IntegrityError):
            self.put(op="duplicate-code", promotion="inf2", code="INF1")
        with self.assertRaises(sqlite3.IntegrityError):
            self.db.execute("UPDATE cloud_promotion_events "
                            "SET name='Modifie' WHERE school_id='a'")
        with self.assertRaises(sqlite3.IntegrityError):
            self.db.execute("DELETE FROM cloud_promotion_events "
                            "WHERE school_id='a'")
        with self.assertRaises(sqlite3.IntegrityError):
            self.put(op="wrong-device", promotion="inf3", device="other")
        self.assertEqual(1, self.db.execute(
            "SELECT COUNT(*) FROM cloud_change_log "
            "WHERE operation_id='prom-1'").fetchone()[0])

if __name__ == "__main__":
    unittest.main()
