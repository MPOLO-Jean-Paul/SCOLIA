-- Jetons de session SCOLIA central : anti-rejeu par appareil, multi-établissements.
-- Une preuve ECDSA signée est liée à un nonce aléatoire unique et à un délai
-- strict. Ce registre ne contient AUCUN jeton, secret ni mot de passe.
CREATE TABLE IF NOT EXISTS cloud_session_nonces (
    school_id TEXT NOT NULL,
    device_id TEXT NOT NULL,
    nonce_sha256 TEXT NOT NULL CHECK (length(nonce_sha256) = 64),
    seen_at INTEGER NOT NULL DEFAULT (unixepoch()),
    PRIMARY KEY (school_id, device_id, nonce_sha256),
    FOREIGN KEY (school_id, device_id)
        REFERENCES cloud_devices(school_id, id) ON DELETE RESTRICT
);

CREATE INDEX IF NOT EXISTS cloud_session_nonces_age
    ON cloud_session_nonces(seen_at);
-- Un entretien planifié devra purger les nonces anciens (> 24 h)
-- après validation du déploiement staging ; ne JAMAIS effacer les récents.
