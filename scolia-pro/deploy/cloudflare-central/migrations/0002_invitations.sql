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
    FOREIGN KEY (school_id) REFERENCES cloud_schools(id) ON DELETE RESTRICT,
    FOREIGN KEY (school_id, target_user_id)
      REFERENCES cloud_users(school_id, id) ON DELETE RESTRICT,
    FOREIGN KEY (school_id, issued_by)
      REFERENCES cloud_users(school_id, id) ON DELETE RESTRICT,
    CHECK (expires_at > created_at AND expires_at <= created_at + 600),
    CHECK (
      (consumed_at IS NULL AND consumed_device_id IS NULL) OR
      (consumed_at IS NOT NULL AND consumed_device_id IS NOT NULL)
    ),
    CHECK (consumed_at IS NULL OR consumed_at >= created_at)
);

CREATE INDEX IF NOT EXISTS cloud_device_invitations_active
  ON cloud_device_invitations(school_id, target_user_id, expires_at);
