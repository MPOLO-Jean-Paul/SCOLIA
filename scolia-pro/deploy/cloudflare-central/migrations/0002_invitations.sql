-- Invitations de jumelage SCOLIA, non utilisables en production sans
-- authentification du destinataire et échange cryptographique avec l'appareil.
-- Conserver uniquement SHA-256(code) dans D1, jamais le code QR en clair.
CREATE TABLE IF NOT EXISTS cloud_device_invitations (
    id TEXT PRIMARY KEY NOT NULL,
    school_id TEXT NOT NULL,
    target_user_id TEXT NOT NULL,
    issued_by TEXT NOT NULL,
    token_sha256 TEXT NOT NULL UNIQUE
      CHECK (length(token_sha256) = 64),
    created_at INTEGER NOT NULL DEFAULT (unixepoch()),
    expires_at INTEGER NOT NULL,
    consumed_at INTEGER,
    consumed_device_id TEXT,
    -- Clé PUBLIQUE de l'appareil présentée avec une preuve de possession.
    consumed_device_public_key TEXT
      CHECK (consumed_device_public_key IS NULL OR
             length(consumed_device_public_key) BETWEEN 100 AND 512),
    FOREIGN KEY (school_id) REFERENCES cloud_schools(id) ON DELETE RESTRICT,
    FOREIGN KEY (school_id, target_user_id)
      REFERENCES cloud_users(school_id, id) ON DELETE RESTRICT,
    FOREIGN KEY (school_id, issued_by)
      REFERENCES cloud_users(school_id, id) ON DELETE RESTRICT,
    CHECK (expires_at > created_at AND expires_at <= created_at + 600),
    CHECK (
      (consumed_at IS NULL AND consumed_device_id IS NULL AND consumed_device_public_key IS NULL) OR
      (consumed_at IS NOT NULL AND consumed_device_id IS NOT NULL AND consumed_device_public_key IS NOT NULL)
    ),
    CHECK (consumed_at IS NULL OR consumed_at >= created_at)
);

CREATE INDEX IF NOT EXISTS cloud_device_invitations_active
  ON cloud_device_invitations(school_id, target_user_id, expires_at);

-- UNE SEULE UPDATE conditionnelle consomme le QR et crée l'appareil.
-- SQLite déclenche cette insertion dans la même transaction. Une erreur
-- (identifiant déjà attribué, FK, clé invalide) annule les DEUX changements.
CREATE TRIGGER IF NOT EXISTS cloud_pairing_register_approved_device
AFTER UPDATE OF consumed_at ON cloud_device_invitations
FOR EACH ROW
WHEN OLD.consumed_at IS NULL AND NEW.consumed_at IS NOT NULL
BEGIN
    INSERT INTO cloud_devices(school_id,id,user_id,approved_by,public_key_jwk)
    VALUES (NEW.school_id, NEW.consumed_device_id, NEW.target_user_id,
            NEW.issued_by, NEW.consumed_device_public_key);
END;
