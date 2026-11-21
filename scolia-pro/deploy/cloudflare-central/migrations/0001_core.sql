-- SCOLIA Pro 1.2.86 : fondation du futur serveur central D1.
-- AUCUN import de données métier ni déploiement automatique.
-- Les autorisations restent obligatoirement vérifiées dans l'API Worker.
PRAGMA foreign_keys = ON;

CREATE TABLE IF NOT EXISTS cloud_schools (
    id TEXT PRIMARY KEY NOT NULL,
    display_name TEXT NOT NULL CHECK (length(trim(display_name)) BETWEEN 1 AND 200),
    status TEXT NOT NULL DEFAULT 'active' CHECK (status IN ('active', 'suspended')),
    created_at INTEGER NOT NULL DEFAULT (unixepoch())
);

CREATE TABLE IF NOT EXISTS cloud_users (
    school_id TEXT NOT NULL,
    id TEXT NOT NULL,
    login_normalized TEXT NOT NULL CHECK (length(trim(login_normalized)) BETWEEN 1 AND 120),
    role TEXT NOT NULL CHECK (length(trim(role)) BETWEEN 1 AND 64),
    enabled INTEGER NOT NULL DEFAULT 1 CHECK (enabled IN (0, 1)),
    permission_revision INTEGER NOT NULL DEFAULT 1 CHECK (permission_revision >= 1),
    created_at INTEGER NOT NULL DEFAULT (unixepoch()),
    PRIMARY KEY (school_id, id),
    UNIQUE (school_id, login_normalized),
    FOREIGN KEY (school_id) REFERENCES cloud_schools(id) ON DELETE RESTRICT
);

CREATE TABLE IF NOT EXISTS cloud_devices (
    school_id TEXT NOT NULL,
    id TEXT NOT NULL,
    user_id TEXT NOT NULL,
    approved_by TEXT NOT NULL,
    approved_at INTEGER NOT NULL DEFAULT (unixepoch()),
    revoked_at INTEGER,
    PRIMARY KEY (school_id, id),
    UNIQUE (school_id, id, user_id),
    FOREIGN KEY (school_id, user_id) REFERENCES cloud_users(school_id, id) ON DELETE RESTRICT,
    FOREIGN KEY (school_id, approved_by) REFERENCES cloud_users(school_id, id) ON DELETE RESTRICT,
    CHECK (revoked_at IS NULL OR revoked_at >= approved_at)
);

-- Une opération n'est enregistrée qu'une fois par établissement. Son empreinte
-- permet de refuser une nouvelle tentative utilisant la même clé avec un corps
-- différent. La clé d'idempotence ne doit jamais être régénérée par le client.
CREATE TABLE IF NOT EXISTS cloud_operations (
    school_id TEXT NOT NULL,
    operation_id TEXT NOT NULL CHECK (length(operation_id) BETWEEN 1 AND 128),
    actor_user_id TEXT NOT NULL,
    device_id TEXT NOT NULL,
    operation_kind TEXT NOT NULL CHECK (length(operation_kind) BETWEEN 1 AND 120),
    request_sha256 TEXT NOT NULL CHECK (length(request_sha256) = 64),
    outcome TEXT NOT NULL DEFAULT 'pending' CHECK (outcome IN ('pending', 'applied', 'rejected')),
    result_json TEXT,
    received_at INTEGER NOT NULL DEFAULT (unixepoch()),
    settled_at INTEGER,
    PRIMARY KEY (school_id, operation_id),
    FOREIGN KEY (school_id, actor_user_id) REFERENCES cloud_users(school_id, id) ON DELETE RESTRICT,
    FOREIGN KEY (school_id, device_id, actor_user_id)
        REFERENCES cloud_devices(school_id, id, user_id) ON DELETE RESTRICT,
    CHECK ((outcome = 'pending' AND settled_at IS NULL)
        OR (outcome IN ('applied', 'rejected') AND settled_at IS NOT NULL))
);

CREATE INDEX IF NOT EXISTS cloud_operations_pending
    ON cloud_operations (school_id, outcome, received_at);

-- Journal serveur ordonné. Ne contient QUE des métadonnées (pas de noms,
-- notes, montants, documents ou secrets) tant que les routes métier et leurs
-- contrôles ne sont pas implémentés.
CREATE TABLE IF NOT EXISTS cloud_change_log (
    revision INTEGER PRIMARY KEY AUTOINCREMENT,
    school_id TEXT NOT NULL,
    operation_id TEXT NOT NULL,
    entity_kind TEXT NOT NULL CHECK (length(entity_kind) BETWEEN 1 AND 120),
    entity_id TEXT NOT NULL CHECK (length(entity_id) BETWEEN 1 AND 128),
    change_kind TEXT NOT NULL CHECK (change_kind IN ('upsert', 'tombstone')),
    payload_sha256 TEXT NOT NULL CHECK (length(payload_sha256) = 64),
    created_at INTEGER NOT NULL DEFAULT (unixepoch()),
    UNIQUE (school_id, operation_id, entity_kind, entity_id),
    FOREIGN KEY (school_id, operation_id)
        REFERENCES cloud_operations(school_id, operation_id) ON DELETE RESTRICT
);

-- Empêcher qu'un événement diffusé soit produit pour une opération rejetée
-- ou non encore validée par le moteur métier cloud.
CREATE TRIGGER IF NOT EXISTS cloud_change_requires_accepted_operation
BEFORE INSERT ON cloud_change_log
FOR EACH ROW
WHEN (SELECT outcome FROM cloud_operations
      WHERE school_id = NEW.school_id AND operation_id = NEW.operation_id) != 'applied'
BEGIN
    SELECT RAISE(ABORT, 'cloud_change_requires_applied_operation');
END;

CREATE INDEX IF NOT EXISTS cloud_changes_by_school_revision
    ON cloud_change_log (school_id, revision);

-- Le curseur est mis à jour seulement APRÈS application locale atomique
-- des changements du serveur, jamais au moment de leur téléchargement.
CREATE TABLE IF NOT EXISTS cloud_sync_cursors (
    school_id TEXT NOT NULL,
    device_id TEXT NOT NULL,
    last_applied_revision INTEGER NOT NULL DEFAULT 0 CHECK (last_applied_revision >= 0),
    updated_at INTEGER NOT NULL DEFAULT (unixepoch()),
    PRIMARY KEY (school_id, device_id),
    FOREIGN KEY (school_id, device_id) REFERENCES cloud_devices(school_id, id) ON DELETE RESTRICT
);
