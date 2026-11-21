"""Tests SQLite de la fondation D1 centrale (sans réseau ni données réelles)."""
from pathlib import Path
import sqlite3
import unittest

MIGRATION = (
    Path(__file__).resolve().parents[1]
    / "deploy/cloudflare-central/migrations/0001_core.sql"
)
HASH = "a" * 64


class CloudflareCentralSchemaTests(unittest.TestCase):
    def setUp(self):
        self.db = sqlite3.connect(":memory:")
        self.db.executescript(MIGRATION.read_text(encoding="utf-8"))
        self.db.execute("INSERT INTO cloud_schools(id,display_name) VALUES('a','École A')")
        self.db.execute("INSERT INTO cloud_schools(id,display_name) VALUES('b','École B')")
        for school in ("a", "b"):
            self.db.execute(
                """INSERT INTO cloud_users(school_id,id,login_normalized,role)
                   VALUES(?,?,?,?)""",
                (school, "secretary", "secretariat", "secretariat"),
            )
            self.db.execute(
                """INSERT INTO cloud_users(school_id,id,login_normalized,role)
                   VALUES(?,?,?,?)""",
                (school, "teacher", "enseignant", "enseignant"),
            )
            self.db.execute(
                """INSERT INTO cloud_devices(school_id,id,user_id,approved_by)
                   VALUES(?,?,?,?)""",
                (school, "phone", "teacher", "secretary"),
            )

    def tearDown(self):
        self.db.close()

    def add_operation(self, school, op_id, actor="teacher", device="phone"):
        self.db.execute(
            """INSERT INTO cloud_operations
               (school_id,operation_id,actor_user_id,device_id,
                operation_kind,request_sha256)
               VALUES(?,?,?,?,?,?)""",
            (school, op_id, actor, device, "marks.encode", HASH),
        )

    def test_same_idempotency_key_is_rejected_twice_per_school(self):
        self.add_operation("a", "once")
        with self.assertRaises(sqlite3.IntegrityError):
            self.add_operation("a", "once")
        # Chaque établissement conserve son propre namespace d'opérations.
        self.add_operation("b", "once")
        self.assertEqual(
            2, self.db.execute(
                "SELECT COUNT(*) FROM cloud_operations WHERE operation_id='once'"
            ).fetchone()[0]
        )

    def test_device_different_school_or_owner_cannot_impersonate_actor(self):
        self.add_operation("a", "valid")
        with self.assertRaises(sqlite3.IntegrityError):
            self.add_operation("a", "wrong-owner", actor="secretary")
        with self.assertRaises(sqlite3.IntegrityError):
            self.db.execute(
                """INSERT INTO cloud_devices(school_id,id,user_id,approved_by)
                   VALUES('a','wrong','teacher','someone-else')"""
            )

    def test_only_settled_and_applied_operations_emit_changes(self):
        self.add_operation("a", "changes")
        query = """INSERT INTO cloud_change_log
                   (school_id,operation_id,entity_kind,entity_id,change_kind,payload_sha256)
                   VALUES('a','changes','mark','mark-1','upsert',?)"""
        with self.assertRaises(sqlite3.IntegrityError):
            self.db.execute(query, (HASH,))

        self.db.execute(
            """UPDATE cloud_operations SET outcome='applied',settled_at=unixepoch()
               WHERE school_id='a' AND operation_id='changes'"""
        )
        self.db.execute(query, (HASH,))
        with self.assertRaises(sqlite3.IntegrityError):
            self.db.execute(query, (HASH,))
        self.assertEqual(1, self.db.execute(
            "SELECT COUNT(*) FROM cloud_change_log"
        ).fetchone()[0])

    def test_no_cross_school_change_link(self):
        self.add_operation("a", "a-only")
        self.db.execute(
            """UPDATE cloud_operations SET outcome='applied',settled_at=unixepoch()
               WHERE school_id='a' AND operation_id='a-only'"""
        )
        with self.assertRaises(sqlite3.IntegrityError):
            self.db.execute(
                """INSERT INTO cloud_change_log
                   (school_id,operation_id,entity_kind,entity_id,change_kind,payload_sha256)
                   VALUES('b','a-only','mark','m1','upsert',?)""",
                (HASH,),
            )

    def test_cursor_and_users_are_isolated_by_school(self):
        for school in ("a", "b"):
            self.db.execute(
                """INSERT INTO cloud_sync_cursors(school_id,device_id,last_applied_revision)
                   VALUES(?,?,?)""",
                (school, "phone", 1 if school == "a" else 9),
            )
        cursors = self.db.execute(
            "SELECT school_id,last_applied_revision FROM cloud_sync_cursors ORDER BY school_id"
        ).fetchall()
        self.assertEqual([("a", 1), ("b", 9)], cursors)

        with self.assertRaises(sqlite3.IntegrityError):
            self.db.execute(
                "INSERT INTO cloud_sync_cursors(school_id,device_id,last_applied_revision) "
                "VALUES('a','unknown',1)"
            )
        with self.assertRaises(sqlite3.IntegrityError):
            self.db.execute(
                "INSERT INTO cloud_sync_cursors(school_id,device_id,last_applied_revision) "
                "VALUES('a','new-phone',-1)"
            )

    def test_revocation_is_attributed_to_authorized_user_of_same_school(self):
        with self.assertRaises(sqlite3.IntegrityError):
            self.db.execute(
                "UPDATE cloud_devices SET revoked_at = unixepoch() "
                "WHERE school_id='a' AND id='phone'"
            )
        with self.assertRaises(sqlite3.IntegrityError):
            self.db.execute(
                "UPDATE cloud_devices SET revoked_at = unixepoch(), revoked_by = 'nonexistent' "
                "WHERE school_id='a' AND id='phone'"
            )
        self.db.execute(
            "UPDATE cloud_devices SET revoked_at = unixepoch(), revoked_by = 'secretary' "
            "WHERE school_id='a' AND id='phone'"
        )
        revoked = self.db.execute(
            "SELECT revoked_at,revoked_by FROM cloud_devices "
            "WHERE school_id='a' AND id='phone'"
        ).fetchone()
        self.assertIsNotNone(revoked[0])
        self.assertEqual("secretary", revoked[1])
        # L'appareil de l'école B reste approuvé : la requête est scindée par école.
        unaffected = self.db.execute(
            "SELECT revoked_at FROM cloud_devices WHERE school_id='b' AND id='phone'"
        ).fetchone()
        self.assertIsNone(unaffected[0])

    def test_reapplying_migration_is_additive_and_safe(self):
        self.add_operation("a", "survives")
        self.db.executescript(MIGRATION.read_text(encoding="utf-8"))
        self.assertEqual(
            1, self.db.execute(
                "SELECT COUNT(*) FROM cloud_operations WHERE operation_id='survives'"
            ).fetchone()[0]
        )


if __name__ == "__main__":
    unittest.main()
