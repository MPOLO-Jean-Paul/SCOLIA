"""D1 métier : sections scolaires, sans paiement, élève, note ni donnée réelle."""
from pathlib import Path
import sqlite3
import unittest

MIGRATIONS = Path(__file__).resolve().parents[1] / "deploy/cloudflare-central/migrations"
HASH = "d" * 64

class SchoolSectionsD1Tests(unittest.TestCase):
    def setUp(self):
        self.db = sqlite3.connect(":memory:")
        self.db.execute("PRAGMA foreign_keys=ON")
        for name in (
            "0001_core.sql", "0002_invitations.sql",
            "0003_password_identity.sql", "0004_device_sessions.sql",
            "0005_school_sections.sql",
        ):
            self.db.executescript((MIGRATIONS / name).read_text(encoding="utf-8"))
        for school in ("a", "b"):
            self.db.execute("INSERT INTO cloud_schools(id,display_name) VALUES(?,?)",
                            (school, "École " + school))
            self.db.execute(
                "INSERT INTO cloud_users(school_id,id,login_normalized,role) "
                "VALUES(?,'secretary','secretariat','secretariat')", (school,))
            self.db.execute(
                "INSERT INTO cloud_users(school_id,id,login_normalized,role) "
                "VALUES(?,'teacher','enseignant','enseignant')", (school,))
            self.db.execute(
                "INSERT INTO cloud_devices(school_id,id,user_id,approved_by) "
                "VALUES(?,'pc','secretary','secretary')", (school,))

    def tearDown(self):
        self.db.close()

    def put(self, school="a", op="op-001", section="sec-001", code="INF",
            name="Sciences infirmières", version=0, user="secretary", device="pc"):
        return self.db.execute(
            """INSERT INTO cloud_section_events
               (school_id,operation_id,section_id,actor_user_id,device_id,
                code,name,is_active,expected_revision,request_sha256)
               VALUES(?,?,?,?,?,?,?,1,?,?)""",
            (school, op, section, user, device, code, name, version, HASH),
        )

    def revision(self, school="a", section="sec-001"):
        item = self.db.execute(
            "SELECT last_revision FROM cloud_sections WHERE school_id=? AND id=?",
            (school, section),
        ).fetchone()
        return item[0] if item else 0

    def test_event_updates_current_section_and_both_operational_logs_atomically(self):
        self.put()
        current = self.revision()
        self.assertGreater(current, 0)
        row = self.db.execute(
            "SELECT code,name,is_active,last_revision "
            "FROM cloud_sections WHERE school_id='a' AND id='sec-001'"
        ).fetchone()
        self.assertEqual(row, ("INF", "Sciences infirmières", 1, current))
        operation = self.db.execute(
            "SELECT actor_user_id,device_id,outcome,request_sha256 "
            "FROM cloud_operations WHERE school_id='a' AND operation_id='op-001'"
        ).fetchone()
        self.assertEqual(operation, ("secretary", "pc", "applied", HASH))
        event = self.db.execute(
            "SELECT entity_kind,entity_id,payload_sha256 "
            "FROM cloud_change_log WHERE school_id='a' AND operation_id='op-001'"
        ).fetchone()
        self.assertEqual(event, ("section", "sec-001", HASH))

        self.put(op="op-002", code="INF2", name="Deuxième infirmière", version=current)
        self.assertGreater(self.revision(), current)
        self.assertEqual(2, self.db.execute(
            "SELECT count(*) FROM cloud_section_events WHERE school_id='a'"
        ).fetchone()[0])

    def test_stale_version_aborts_event_and_keeps_original_data(self):
        self.put()
        first = self.revision()
        with self.assertRaises(sqlite3.IntegrityError):
            self.put(op="stale", code="NEW", version=0)
        self.assertEqual(first, self.revision())
        self.assertIsNone(self.db.execute(
            "SELECT outcome FROM cloud_operations WHERE operation_id='stale'"
        ).fetchone())

    def test_same_operation_id_is_not_applied_twice(self):
        self.put()
        first = self.revision()
        with self.assertRaises(sqlite3.IntegrityError):
            self.put(op="op-001", code="ALT", version=first)
        self.assertEqual(first, self.revision())
        self.assertEqual(1, self.db.execute(
            "SELECT count(*) FROM cloud_change_log WHERE school_id='a'"
        ).fetchone()[0])

    def test_section_code_is_unique_within_one_school_but_not_across_schools(self):
        self.put()
        with self.assertRaises(sqlite3.IntegrityError):
            self.put(op="op-002", section="sec-002", code="INF")
        self.assertIsNone(self.db.execute(
            "SELECT 1 FROM cloud_operations WHERE operation_id='op-002'"
        ).fetchone())
        self.put(school="b")
        self.assertGreater(self.revision("b"), 0)
        self.assertNotEqual(self.revision("a"), self.revision("b"))

    def test_foreign_device_or_actor_fails_and_rolls_back(self):
        with self.assertRaises(sqlite3.IntegrityError):
            self.put(op="bad", device="nonexistent")
        with self.assertRaises(sqlite3.IntegrityError):
            self.put(op="bad2", user="unknown")
        self.assertEqual(0, self.db.execute(
            "SELECT count(*) FROM cloud_sections"
        ).fetchone()[0])

    def test_events_are_immutable_and_replay_requires_new_operation(self):
        self.put()
        with self.assertRaises(sqlite3.IntegrityError):
            self.db.execute("UPDATE cloud_section_events SET name='Alt' WHERE school_id='a'")
        with self.assertRaises(sqlite3.IntegrityError):
            self.db.execute("DELETE FROM cloud_section_events WHERE school_id='a'")
        self.assertEqual("Sciences infirmières", self.db.execute(
            "SELECT name FROM cloud_sections WHERE school_id='a'"
        ).fetchone()[0])

if __name__ == "__main__":
    unittest.main()
