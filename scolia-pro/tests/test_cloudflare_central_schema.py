"""Tests SQLite de la fondation D1 centrale (sans réseau ni données réelles)."""
from pathlib import Path
import sqlite3
import re
import unittest

MIGRATION = (
    Path(__file__).resolve().parents[1]
    / "deploy/cloudflare-central/migrations/0001_core.sql"
)
INVITATIONS_MIGRATION = MIGRATION.with_name("0002_invitations.sql")
IDENTITY_MIGRATION = MIGRATION.with_name("0003_password_identity.sql")
HASH = "a" * 64


class CloudflareCentralSchemaTests(unittest.TestCase):
    def setUp(self):
        self.db = sqlite3.connect(":memory:")
        self.db.executescript(MIGRATION.read_text(encoding="utf-8"))
        self.db.executescript(INVITATIONS_MIGRATION.read_text(encoding="utf-8"))
        self.db.executescript(IDENTITY_MIGRATION.read_text(encoding="utf-8"))
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

    def test_invitation_is_school_scoped_and_never_stores_the_raw_token(self):
        self.db.execute(
            """INSERT INTO cloud_device_invitations(
                   id,school_id,target_user_id,issued_by,token_sha256,expires_at)
               VALUES('inv-a','a','teacher','secretary',?,unixepoch()+300)""",
            (HASH,),
        )
        row = self.db.execute(
            "SELECT school_id,target_user_id,issued_by,token_sha256 "
            "FROM cloud_device_invitations WHERE id='inv-a'"
        ).fetchone()
        self.assertEqual(("a", "teacher", "secretary", HASH), row)
        with self.assertRaises(sqlite3.IntegrityError):
            self.db.execute(
                """INSERT INTO cloud_device_invitations(
                       id,school_id,target_user_id,issued_by,token_sha256,expires_at)
                   VALUES('inv-b','b','teacher','secretary',?,unixepoch()+300)""",
                (HASH,),
            )
        with self.assertRaises(sqlite3.IntegrityError):
            self.db.execute(
                """INSERT INTO cloud_device_invitations(
                       id,school_id,target_user_id,issued_by,token_sha256,expires_at)
                   VALUES('other-school','a','unknown','secretary',?,unixepoch()+300)""",
                ("b" * 64,),
            )

    def test_invitation_expiration_is_bounded_and_consumption_is_atomic(self):
        with self.assertRaises(sqlite3.IntegrityError):
            self.db.execute(
                """INSERT INTO cloud_device_invitations(
                       id,school_id,target_user_id,issued_by,token_sha256,expires_at)
                   VALUES('expired','a','teacher','secretary',?,unixepoch()-1)""",
                ("b" * 64,),
            )
        with self.assertRaises(sqlite3.IntegrityError):
            self.db.execute(
                """INSERT INTO cloud_device_invitations(
                       id,school_id,target_user_id,issued_by,token_sha256,expires_at)
                   VALUES('too-long','a','teacher','secretary',?,unixepoch()+700)""",
                ("c" * 64,),
            )
        self.db.execute(
            """INSERT INTO cloud_device_invitations(
                   id,school_id,target_user_id,issued_by,token_sha256,expires_at)
               VALUES('valid','a','teacher','secretary',?,unixepoch()+300)""",
            ("d" * 64,),
        )
        with self.assertRaises(sqlite3.IntegrityError):
            self.db.execute(
                """UPDATE cloud_device_invitations
                   SET consumed_at=unixepoch()
                   WHERE id='valid'"""
            )
        self.db.execute(
            """UPDATE cloud_device_invitations
               SET consumed_at=unixepoch(), consumed_device_id='new-phone',
                   consumed_device_public_key=?
               WHERE id='valid'""",
            ('{"crv":"P-256","kty":"EC","x":"' + 'a' * 43 +
             '","y":"' + 'b' * 43 + '"}',),
        )
        used = self.db.execute(
            "SELECT consumed_at,consumed_device_id "
            "FROM cloud_device_invitations WHERE id='valid'"
        ).fetchone()
        self.assertIsNotNone(used[0])
        self.assertEqual(used[1], "new-phone")
        registered = self.db.execute(
            "SELECT school_id,id,user_id,approved_by,public_key_jwk "
            "FROM cloud_devices WHERE school_id='a' AND id='new-phone'"
        ).fetchone()
        self.assertEqual(registered[:4], ("a", "new-phone", "teacher", "secretary"))
        self.assertIn('"kty":"EC"', registered[4])
        # Une seconde consommation ne peut pas réinscrire cet appareil.
        again = self.db.execute(
            """UPDATE cloud_device_invitations SET consumed_at=unixepoch()
               WHERE id='valid' AND consumed_at IS NULL"""
        )
        self.assertEqual(again.rowcount, 0)
        self.assertEqual(1, self.db.execute(
            "SELECT COUNT(*) FROM cloud_devices WHERE school_id='a' AND id='new-phone'"
        ).fetchone()[0])

    def test_trigger_rollback_if_device_registration_fails(self):
        self.db.execute(
            """INSERT INTO cloud_device_invitations
               (id,school_id,target_user_id,issued_by,token_sha256,expires_at)
               VALUES('conflict','a','teacher','secretary',?,unixepoch()+300)""",
            ("c" * 64,),
        )
        pub = '{"crv":"P-256","kty":"EC","x":"' + 'a' * 43 + '","y":"' + 'b' * 43 + '"}'
        with self.assertRaises(sqlite3.IntegrityError):
            self.db.execute(
                """UPDATE cloud_device_invitations
                   SET consumed_at=unixepoch(),consumed_device_id='phone',
                       consumed_device_public_key=?
                   WHERE school_id='a' AND id='conflict' AND consumed_at IS NULL""",
                (pub,),
            )
        values = self.db.execute(
            """SELECT consumed_at,consumed_device_id,consumed_device_public_key
               FROM cloud_device_invitations WHERE id='conflict'"""
        ).fetchone()
        self.assertEqual(values, (None, None, None))


    def test_password_hash_and_attempts_are_scoped_to_the_real_account(self):
        # Jamais d'identifiants ou de mots de passe en clair dans ce test.
        self.db.execute(
            "UPDATE cloud_users SET password_hash=?, must_change_password=0 "
            "WHERE school_id='a' AND id='teacher'",
            ("A" * 84,),
        )
        self.assertEqual(("A" * 84, 0), self.db.execute(
            "SELECT password_hash,must_change_password FROM cloud_users "
            "WHERE school_id='a' AND id='teacher'"
        ).fetchone())
        self.assertEqual((None, 1), self.db.execute(
            "SELECT password_hash,must_change_password FROM cloud_users "
            "WHERE school_id='b' AND id='teacher'"
        ).fetchone())
        with self.assertRaises(sqlite3.IntegrityError):
            self.db.execute(
                "INSERT INTO cloud_auth_attempts(school_id,user_id) VALUES('a','unknown')"
            )

    def test_five_attempts_lock_one_account_across_all_its_qr_invitations(self):
        reserve = (
            "INSERT INTO cloud_auth_attempts "
            "(school_id,user_id,failed_count,lockout_until,updated_at) "
            "VALUES (?, ?, 1, 0, unixepoch()) "
            "ON CONFLICT(school_id,user_id) DO UPDATE SET "
            "failed_count = CASE WHEN cloud_auth_attempts.lockout_until > 0 "
            "AND cloud_auth_attempts.lockout_until <= unixepoch() THEN 1 "
            "ELSE cloud_auth_attempts.failed_count + 1 END, "
            "lockout_until = 0, updated_at = unixepoch() "
            "WHERE (cloud_auth_attempts.lockout_until = 0 "
            "AND cloud_auth_attempts.failed_count < 5) "
            "OR (cloud_auth_attempts.lockout_until > 0 "
            "AND cloud_auth_attempts.lockout_until <= unixepoch())"
        )
        for _ in range(5):
            self.assertEqual(1, self.db.execute(reserve, ("a", "teacher")).rowcount)
        self.db.execute(
            "UPDATE cloud_auth_attempts SET lockout_until=unixepoch()+120 "
            "WHERE school_id='a' AND user_id='teacher' AND failed_count>=5 "
            "AND lockout_until=0"
        )
        self.assertEqual(0, self.db.execute(reserve, ("a", "teacher")).rowcount)
        self.assertEqual((5, True), self.db.execute(
            "SELECT failed_count,lockout_until>unixepoch() FROM cloud_auth_attempts "
            "WHERE school_id='a' AND user_id='teacher'"
        ).fetchone())
        # Une autre école et un autre compte conservent leur propre compteur.
        self.assertEqual(1, self.db.execute(reserve, ("b", "teacher")).rowcount)
        self.assertEqual(1, self.db.execute(reserve, ("a", "secretary")).rowcount)

    def test_successful_login_clears_only_its_own_failure_counter(self):
        self.db.execute(
            "INSERT INTO cloud_auth_attempts(school_id,user_id,failed_count) "
            "VALUES('a','teacher',4)"
        )
        self.db.execute(
            "INSERT INTO cloud_auth_attempts(school_id,user_id,failed_count) "
            "VALUES('b','teacher',5)"
        )
        self.db.execute(
            "DELETE FROM cloud_auth_attempts WHERE school_id='a' AND user_id='teacher'"
        )
        self.assertIsNone(self.db.execute(
            "SELECT failed_count FROM cloud_auth_attempts "
            "WHERE school_id='a' AND user_id='teacher'"
        ).fetchone())
        self.assertEqual((5,), self.db.execute(
            "SELECT failed_count FROM cloud_auth_attempts "
            "WHERE school_id='b' AND user_id='teacher'"
        ).fetchone())

    def test_password_or_role_change_invalidates_old_tokens(self):
        self.db.execute(
            "INSERT INTO cloud_auth_attempts(school_id,user_id,failed_count) "
            "VALUES('a','teacher',4)"
        )
        before = self.db.execute(
            "SELECT permission_revision FROM cloud_users "
            "WHERE school_id='a' AND id='teacher'"
        ).fetchone()[0]
        self.db.execute(
            "UPDATE cloud_users SET password_hash=?, must_change_password=0 "
            "WHERE school_id='a' AND id='teacher'",
            ("A" * 84,),
        )
        after = self.db.execute(
            "SELECT permission_revision FROM cloud_users "
            "WHERE school_id='a' AND id='teacher'"
        ).fetchone()[0]
        self.assertGreater(after, before)
        self.assertIsNone(self.db.execute(
            "SELECT failed_count FROM cloud_auth_attempts "
            "WHERE school_id='a' AND user_id='teacher'"
        ).fetchone())
        self.assertEqual(1, self.db.execute(
            "SELECT permission_revision FROM cloud_users "
            "WHERE school_id='b' AND id='teacher'"
        ).fetchone()[0])
        self.db.execute(
            "UPDATE cloud_users SET role='secretariat' "
            "WHERE school_id='a' AND id='teacher'"
        )
        role_revision = self.db.execute(
            "SELECT permission_revision FROM cloud_users "
            "WHERE school_id='a' AND id='teacher'"
        ).fetchone()[0]
        self.assertGreater(role_revision, after)
        # Une modification sans changement effectif du rôle ne révoque rien.
        self.db.execute(
            "UPDATE cloud_users SET role='secretariat' "
            "WHERE school_id='a' AND id='teacher'"
        )
        self.assertEqual(role_revision, self.db.execute(
            "SELECT permission_revision FROM cloud_users "
            "WHERE school_id='a' AND id='teacher'"
        ).fetchone()[0])

    def test_identity_finalization_uses_atomic_checks_from_real_worker_source(self):
        # Extraire la requête exacte du Worker, sans la réécrire ici.
        source = (MIGRATION.parent.parent / "src/identity.mjs").read_text(
            encoding="utf-8"
        )
        start = source.index("const completed = await db.prepare(")
        end = source.index(").bind(account.school_id", start)
        sql = "".join(re.findall(r'"([^"]*)"', source[start:end]))
        self.assertTrue(sql.startswith("DELETE FROM cloud_auth_attempts"))
        self.assertIn("u.password_hash = ?", sql)
        self.assertIn("u.permission_revision = ?", sql)
        self.assertIn("i.expires_at > unixepoch()", sql)

        self.db.execute(
            "UPDATE cloud_users SET password_hash=?, must_change_password=0 "
            "WHERE school_id='a' AND id='teacher'", ("A" * 84,)
        )
        revision = self.db.execute(
            "SELECT permission_revision FROM cloud_users "
            "WHERE school_id='a' AND id='teacher'"
        ).fetchone()[0]
        self.db.execute(
            """INSERT INTO cloud_device_invitations
               (id,school_id,target_user_id,issued_by,token_sha256,expires_at)
               VALUES('inv-123','a','teacher','secretary',?,unixepoch()+300)""",
            ("b" * 64,),
        )

        def try_finalize(expected_hash, expected_revision):
            return self.db.execute(sql, (
                "a", "teacher", "inv-123", "enseignant",
                expected_hash, expected_revision,
            )).rowcount

        self.db.execute(
            "INSERT INTO cloud_auth_attempts(school_id,user_id,failed_count) "
            "VALUES('a','teacher',2)"
        )
        self.assertEqual(0, try_finalize("old-password-hash", revision))
        self.assertEqual(2, self.db.execute(
            "SELECT failed_count FROM cloud_auth_attempts "
            "WHERE school_id='a' AND user_id='teacher'"
        ).fetchone()[0])
        self.assertEqual(1, try_finalize("A" * 84, revision))
        self.assertIsNone(self.db.execute(
            "SELECT failed_count FROM cloud_auth_attempts "
            "WHERE school_id='a' AND user_id='teacher'"
        ).fetchone())

        self.db.execute(
            "UPDATE cloud_users SET role='secretariat' "
            "WHERE school_id='a' AND id='teacher'"
        )
        # Changer un rôle efface l'ancien compteur ; en créer un nouveau
        # vérifie que la preuve fondée sur la révision précédente reste rejetée.
        self.db.execute(
            "INSERT INTO cloud_auth_attempts(school_id,user_id,failed_count) "
            "VALUES('a','teacher',1)"
        )
        self.assertEqual(0, try_finalize("A" * 84, revision))
        self.assertEqual(1, self.db.execute(
            "SELECT failed_count FROM cloud_auth_attempts "
            "WHERE school_id='a' AND user_id='teacher'"
        ).fetchone()[0])
        new_revision = self.db.execute(
            "SELECT permission_revision FROM cloud_users "
            "WHERE school_id='a' AND id='teacher'"
        ).fetchone()[0]
        self.db.execute(
            "UPDATE cloud_device_invitations "
            "SET created_at=unixepoch()-500, expires_at=unixepoch()-200 "
            "WHERE id='inv-123'"
        )
        self.assertEqual(0, try_finalize("A" * 84, new_revision))

    def test_reapplying_migration_is_additive_and_safe(self):
        self.add_operation("a", "survives")
        self.db.executescript(MIGRATION.read_text(encoding="utf-8"))
        self.db.executescript(INVITATIONS_MIGRATION.read_text(encoding="utf-8"))
        self.assertEqual(
            1, self.db.execute(
                "SELECT COUNT(*) FROM cloud_operations WHERE operation_id='survives'"
            ).fetchone()[0]
        )


if __name__ == "__main__":
    unittest.main()
