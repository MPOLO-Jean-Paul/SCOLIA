-- Credentials du futur service d'identité central (SCOLIA Pro 1.2.86).
-- Aucun mot de passe en clair, aucun profil de test, aucun compte généré.
-- Les hachages viennent EXCLUSIVEMENT d'une migration contrôlée des comptes
-- autorisés : ASP.NET Core IdentityV3 PBKDF2-HMAC-SHA512 (100 000 tours ou plus).
-- Toute migration des comptes requiert la vérification de l'intégrité SQLite,
-- le consentement institutionnel et des sauvegardes avant création de sessions.
CREATE TABLE IF NOT EXISTS cloud_auth_credentials (
    school_id TEXT NOT NULL,
    user_id TEXT NOT NULL,
    password_hash_v3 TEXT NOT NULL
        CHECK (length(password_hash_v3) BETWEEN 70 AND 260),
    must_change_password INTEGER NOT NULL DEFAULT 0
        CHECK (must_change_password IN (0, 1)),
    failed_attempts INTEGER NOT NULL DEFAULT 0
        CHECK (failed_attempts BETWEEN 0 AND 5),
    locked_until INTEGER NOT NULL DEFAULT 0
        CHECK (locked_until >= 0),
    changed_at INTEGER NOT NULL DEFAULT (unixepoch()),
    PRIMARY KEY (school_id, user_id),
    FOREIGN KEY (school_id, user_id)
        REFERENCES cloud_users(school_id, id) ON DELETE RESTRICT
);

-- Une invitation reste liée au compte visé. Le contrôle de mot de passe est
-- limité aux comptes déjà approvisionnés dans cette table.
-- Les preuves d'authentification utilisent une clé de signature différente
-- de celle du JWT de session utilisateur.
