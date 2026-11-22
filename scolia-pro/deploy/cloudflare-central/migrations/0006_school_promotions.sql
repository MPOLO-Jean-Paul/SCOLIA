-- Catalogue minimal des promotions : designation, section, activation et revision.
-- Aucune donnee nominative, note ou operation financiere.
-- Appliquer uniquement apres 0005_school_sections.sql, en preproduction.
CREATE TABLE IF NOT EXISTS cloud_promotions (
    school_id TEXT NOT NULL,
    id TEXT NOT NULL CHECK (length(id) BETWEEN 1 AND 128),
    section_id TEXT NOT NULL,
    code TEXT NOT NULL CHECK (length(code) BETWEEN 2 AND 16),
    name TEXT NOT NULL CHECK (length(trim(name)) BETWEEN 1 AND 120),
    is_active INTEGER NOT NULL CHECK (is_active IN (0,1)),
    last_revision INTEGER NOT NULL CHECK (last_revision > 0),
    PRIMARY KEY (school_id, id),
    UNIQUE (school_id, code),
    FOREIGN KEY (school_id, section_id)
      REFERENCES cloud_sections(school_id, id) ON DELETE RESTRICT
);
CREATE INDEX IF NOT EXISTS cloud_promotions_school_section
    ON cloud_promotions(school_id, section_id);

CREATE TABLE IF NOT EXISTS cloud_promotion_events (
    revision INTEGER PRIMARY KEY AUTOINCREMENT,
    school_id TEXT NOT NULL,
    operation_id TEXT NOT NULL CHECK (length(operation_id) BETWEEN 1 AND 128),
    promotion_id TEXT NOT NULL CHECK (length(promotion_id) BETWEEN 1 AND 128),
    section_id TEXT NOT NULL,
    actor_user_id TEXT NOT NULL,
    device_id TEXT NOT NULL,
    code TEXT NOT NULL CHECK (length(code) BETWEEN 2 AND 16),
    name TEXT NOT NULL CHECK (length(trim(name)) BETWEEN 1 AND 120),
    is_active INTEGER NOT NULL CHECK (is_active IN (0,1)),
    expected_revision INTEGER NOT NULL CHECK (expected_revision >= 0),
    request_sha256 TEXT NOT NULL CHECK (length(request_sha256) = 64),
    created_at INTEGER NOT NULL DEFAULT (unixepoch()),
    UNIQUE (school_id, operation_id),
    FOREIGN KEY (school_id, section_id)
      REFERENCES cloud_sections(school_id, id) ON DELETE RESTRICT,
    FOREIGN KEY (school_id, actor_user_id)
      REFERENCES cloud_users(school_id, id) ON DELETE RESTRICT,
    FOREIGN KEY (school_id, device_id, actor_user_id)
      REFERENCES cloud_devices(school_id, id, user_id) ON DELETE RESTRICT
);
CREATE INDEX IF NOT EXISTS cloud_promotion_events_school_revision
    ON cloud_promotion_events(school_id, revision);
CREATE INDEX IF NOT EXISTS cloud_promotion_events_promotion_revision
    ON cloud_promotion_events(school_id, promotion_id, revision DESC);

CREATE TRIGGER IF NOT EXISTS cloud_promotion_version_matches
BEFORE INSERT ON cloud_promotion_events
FOR EACH ROW
WHEN NEW.expected_revision != COALESCE(
    (SELECT last_revision FROM cloud_promotions
     WHERE school_id=NEW.school_id AND id=NEW.promotion_id), 0)
BEGIN
    SELECT RAISE(ABORT, 'cloud_promotion_stale_revision');
END;

-- Les nouvelles promotions actives doivent appartenir a une section active
-- de la MEME ecole. Une promotion existante peut etre desactivee meme si sa
-- section a ete desactivee, afin de permettre la fermeture administrative.
CREATE TRIGGER IF NOT EXISTS cloud_promotion_requires_active_section
BEFORE INSERT ON cloud_promotion_events
FOR EACH ROW
WHEN NEW.is_active=1 AND NOT EXISTS(
    SELECT 1 FROM cloud_sections
    WHERE school_id=NEW.school_id AND id=NEW.section_id AND is_active=1)
BEGIN
    SELECT RAISE(ABORT, 'cloud_promotion_invalid_section');
END;

-- Une seule transaction : version, resultat idempotent et journal de sync.
CREATE TRIGGER IF NOT EXISTS cloud_promotion_event_applied
AFTER INSERT ON cloud_promotion_events
FOR EACH ROW
BEGIN
    INSERT INTO cloud_promotions
        (school_id,id,section_id,code,name,is_active,last_revision)
    VALUES
        (NEW.school_id,NEW.promotion_id,NEW.section_id,NEW.code,
         NEW.name,NEW.is_active,NEW.revision)
    ON CONFLICT(school_id,id)
    DO UPDATE SET section_id=excluded.section_id, code=excluded.code,
                  name=excluded.name, is_active=excluded.is_active,
                  last_revision=excluded.last_revision;

    INSERT INTO cloud_operations
        (school_id,operation_id,actor_user_id,device_id,
         operation_kind,request_sha256,outcome,settled_at)
    VALUES
        (NEW.school_id,NEW.operation_id,NEW.actor_user_id,NEW.device_id,
         'promotion_upsert',NEW.request_sha256,'applied',unixepoch());

    INSERT INTO cloud_change_log
        (school_id,operation_id,entity_kind,entity_id,change_kind,payload_sha256)
    VALUES
        (NEW.school_id,NEW.operation_id,'promotion',
         NEW.promotion_id,'upsert',NEW.request_sha256);
END;

CREATE TRIGGER IF NOT EXISTS cloud_promotion_events_no_update
BEFORE UPDATE ON cloud_promotion_events
BEGIN
    SELECT RAISE(ABORT, 'cloud_promotion_event_immutable');
END;
CREATE TRIGGER IF NOT EXISTS cloud_promotion_events_no_delete
BEFORE DELETE ON cloud_promotion_events
BEGIN
    SELECT RAISE(ABORT, 'cloud_promotion_event_immutable');
END;
