-- Première ressource métier SCOLIA central : sections / options (sans PII).
-- Table d'état courant, versions et journal d'événements liés à un compte
-- approuvé. Les paiements, notes et inscriptions restent intentionnellement
-- hors périmètre tant que leurs validations métier ne sont pas migrées.
CREATE TABLE IF NOT EXISTS cloud_sections (
    school_id TEXT NOT NULL,
    id TEXT NOT NULL CHECK (length(id) BETWEEN 1 AND 128),
    code TEXT NOT NULL CHECK (length(code) BETWEEN 2 AND 16),
    name TEXT NOT NULL CHECK (length(trim(name)) BETWEEN 1 AND 120),
    is_active INTEGER NOT NULL CHECK (is_active IN (0,1)),
    last_revision INTEGER NOT NULL CHECK (last_revision > 0),
    PRIMARY KEY (school_id, id),
    UNIQUE (school_id, code),
    FOREIGN KEY (school_id) REFERENCES cloud_schools(id) ON DELETE RESTRICT
);

CREATE TABLE IF NOT EXISTS cloud_section_events (
    revision INTEGER PRIMARY KEY AUTOINCREMENT,
    school_id TEXT NOT NULL,
    operation_id TEXT NOT NULL CHECK (length(operation_id) BETWEEN 1 AND 128),
    section_id TEXT NOT NULL CHECK (length(section_id) BETWEEN 1 AND 128),
    actor_user_id TEXT NOT NULL,
    device_id TEXT NOT NULL,
    code TEXT NOT NULL CHECK (length(code) BETWEEN 2 AND 16),
    name TEXT NOT NULL CHECK (length(trim(name)) BETWEEN 1 AND 120),
    is_active INTEGER NOT NULL CHECK (is_active IN (0,1)),
    expected_revision INTEGER NOT NULL CHECK (expected_revision >= 0),
    request_sha256 TEXT NOT NULL CHECK (length(request_sha256) = 64),
    created_at INTEGER NOT NULL DEFAULT (unixepoch()),
    UNIQUE (school_id, operation_id),
    FOREIGN KEY (school_id) REFERENCES cloud_schools(id) ON DELETE RESTRICT,
    FOREIGN KEY (school_id, actor_user_id)
      REFERENCES cloud_users(school_id, id) ON DELETE RESTRICT,
    FOREIGN KEY (school_id, device_id, actor_user_id)
      REFERENCES cloud_devices(school_id, id, user_id) ON DELETE RESTRICT
);

CREATE INDEX IF NOT EXISTS cloud_section_events_school_revision
    ON cloud_section_events (school_id, revision);
CREATE INDEX IF NOT EXISTS cloud_section_events_section_revision
    ON cloud_section_events (school_id, section_id, revision DESC);

-- Le client ne doit pas créer d'événement sur un cache ancien. La condition
-- est évaluée dans l'instruction INSERT qui écrit le nouvel événement.
CREATE TRIGGER IF NOT EXISTS cloud_section_version_matches
BEFORE INSERT ON cloud_section_events
FOR EACH ROW
WHEN NEW.expected_revision != COALESCE(
    (SELECT last_revision FROM cloud_sections
     WHERE school_id=NEW.school_id AND id=NEW.section_id), 0)
BEGIN
    SELECT RAISE(ABORT, 'cloud_section_stale_revision');
END;

-- Un INSERT d'événement est un seul commit D1 : état courant, opération
-- idempotente et journal public sont mis à jour dans la MÊME transaction.
CREATE TRIGGER IF NOT EXISTS cloud_section_event_applied
AFTER INSERT ON cloud_section_events
FOR EACH ROW
BEGIN
    INSERT INTO cloud_sections
        (school_id, id, code, name, is_active, last_revision)
    VALUES
        (NEW.school_id, NEW.section_id, NEW.code, NEW.name,
         NEW.is_active, NEW.revision)
    ON CONFLICT(school_id, id)
    DO UPDATE SET code=excluded.code, name=excluded.name,
                  is_active=excluded.is_active,
                  last_revision=excluded.last_revision;

    INSERT INTO cloud_operations
        (school_id, operation_id, actor_user_id, device_id,
         operation_kind, request_sha256, outcome, settled_at)
    VALUES
        (NEW.school_id, NEW.operation_id, NEW.actor_user_id, NEW.device_id,
         'section_upsert', NEW.request_sha256, 'applied', unixepoch());

    INSERT INTO cloud_change_log
        (school_id, operation_id, entity_kind, entity_id, change_kind,
         payload_sha256)
    VALUES
        (NEW.school_id, NEW.operation_id, 'section', NEW.section_id, 'upsert',
         NEW.request_sha256);
END;

-- Une opération appliquée reste une preuve immuable. Pour changer une section,
-- enregistrer un nouvel événement avec la dernière version du serveur.
CREATE TRIGGER IF NOT EXISTS cloud_section_events_no_update
BEFORE UPDATE ON cloud_section_events
BEGIN
    SELECT RAISE(ABORT, 'cloud_section_event_immutable');
END;
CREATE TRIGGER IF NOT EXISTS cloud_section_events_no_delete
BEFORE DELETE ON cloud_section_events
BEGIN
    SELECT RAISE(ABORT, 'cloud_section_event_immutable');
END;
