-- SCOLIA Pro 1.2.86 : compatibilité des empreintes ASP.NET Core Identity V3.
-- MIGRATION PROTOTYPE : ne pas appliquer aux données réelles avant audit.
-- La colonne ne contient que des empreintes salées, jamais des mots de passe.
-- Les comptes existants doivent être migrés avec PasswordHash, SecurityStamp
-- et MustChangePassword selon leurs valeurs réelles, via un canal approuvé.
ALTER TABLE cloud_users ADD COLUMN password_hash TEXT
    CHECK (password_hash IS NULL OR length(password_hash) BETWEEN 32 AND 256);

ALTER TABLE cloud_users ADD COLUMN must_change_password INTEGER NOT NULL DEFAULT 1
    CHECK (must_change_password IN (0, 1));

-- Les tentatives sont limitées PAR COMPTE, y compris si 3 QR distincts
-- ont été émis. Cinq mots de passe faux -> verrouillage de deux minutes.
CREATE TABLE IF NOT EXISTS cloud_auth_attempts (
    school_id TEXT NOT NULL,
    user_id TEXT NOT NULL,
    failed_count INTEGER NOT NULL DEFAULT 0 CHECK (failed_count BETWEEN 0 AND 5),
    lockout_until INTEGER NOT NULL DEFAULT 0 CHECK (lockout_until >= 0),
    updated_at INTEGER NOT NULL DEFAULT (unixepoch()),
    PRIMARY KEY (school_id, user_id),
    FOREIGN KEY (school_id, user_id)
      REFERENCES cloud_users(school_id, id) ON DELETE RESTRICT
);

CREATE INDEX IF NOT EXISTS cloud_auth_attempts_lockout
    ON cloud_auth_attempts(lockout_until);

-- Révoquer automatiquement toutes les preuves et sessions fondées sur
-- une ancienne empreinte ou une ancienne autorisation. Le vérificateur du
-- QR compare permission_revision avant toute activation du téléphone.
-- Ce trigger ne s'exécute pas lorsque seule permission_revision est mise à jour.
CREATE TRIGGER IF NOT EXISTS cloud_auth_security_revision
AFTER UPDATE OF password_hash, must_change_password, role, enabled ON cloud_users
FOR EACH ROW
WHEN OLD.password_hash IS NOT NEW.password_hash OR
     OLD.must_change_password IS NOT NEW.must_change_password OR
     OLD.role IS NOT NEW.role OR OLD.enabled IS NOT NEW.enabled
BEGIN
    UPDATE cloud_users SET permission_revision =
        MAX(permission_revision, OLD.permission_revision + 1)
    WHERE school_id = NEW.school_id AND id = NEW.id;
    DELETE FROM cloud_auth_attempts
    WHERE school_id = NEW.school_id AND user_id = NEW.id;
END;
