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
