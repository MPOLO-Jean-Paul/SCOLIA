-- SCOLIA : catalogue D1 des annees et promotions, uniquement en preproduction.
-- Le modele natif rattache chaque promotion a UNE annee scolaire.
-- Les codes (INF1, P1...) sont reutilisables les annees suivantes.
-- Aucun apprenant, paiement, note ou document n'est transfere.

CREATE TABLE IF NOT EXISTS cloud_academic_years (
    school_id TEXT NOT NULL,
    id TEXT NOT NULL CHECK (length(id) BETWEEN 1 AND 128),
    label TEXT NOT NULL CHECK (
      length(label) = 9 AND
      label GLOB '[0-9][0-9][0-9][0-9]-[0-9][0-9][0-9][0-9]' AND
      CAST(substr(label, 6, 4) AS INTEGER) =
        CAST(substr(label, 1, 4) AS INTEGER) + 1
    ),
    start_date TEXT NOT NULL CHECK (length(start_date) = 10),
    end_date TEXT NOT NULL CHECK (length(end_date) = 10 AND end_date >= start_date),
    status TEXT NOT NULL CHECK (status IN ('preparation', 'open', 'closed')),
    last_revision INTEGER NOT NULL CHECK (last_revision > 0),
    PRIMARY KEY (school_id, id),
    UNIQUE (school_id, label),
    FOREIGN KEY (school_id) REFERENCES cloud_schools(id) ON DELETE RESTRICT
);

CREATE TABLE IF NOT EXISTS cloud_academic_year_events (
    revision INTEGER PRIMARY KEY AUTOINCREMENT,
    school_id TEXT NOT NULL,
    operation_id TEXT NOT NULL CHECK (length(operation_id) BETWEEN 1 AND 128),
    academic_year_id TEXT NOT NULL CHECK (length(academic_year_id) BETWEEN 1 AND 128),
    actor_user_id TEXT NOT NULL,
    device_id TEXT NOT NULL,
    label TEXT NOT NULL,
    start_date TEXT NOT NULL,
    end_date TEXT NOT NULL,
    status TEXT NOT NULL,
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
CREATE INDEX IF NOT EXISTS cloud_academic_year_events_school_revision
    ON cloud_academic_year_events(school_id, revision);

CREATE TRIGGER IF NOT EXISTS cloud_year_version_matches
BEFORE INSERT ON cloud_academic_year_events
FOR EACH ROW
WHEN NEW.expected_revision != COALESCE(
    (SELECT last_revision FROM cloud_academic_years
     WHERE school_id=NEW.school_id AND id=NEW.academic_year_id), 0)
BEGIN
    SELECT RAISE(ABORT, 'cloud_year_stale_revision');
END;

-- Une annee ne peut pas etre rebaptisee apres creation : les promotions
-- historiques conservent leur rattachement.
CREATE TRIGGER IF NOT EXISTS cloud_year_identity_immutable
BEFORE INSERT ON cloud_academic_year_events
FOR EACH ROW
WHEN EXISTS (
    SELECT 1 FROM cloud_academic_years
    WHERE school_id=NEW.school_id AND id=NEW.academic_year_id
      AND label<>NEW.label
)
BEGIN SELECT RAISE(ABORT, 'cloud_year_identity_immutable'); END;

-- Une annee cloturee n'est pas rouverte dans le service natif SCOLIA :
-- son etat et ses dates restent immuables dans le catalogue central.
CREATE TRIGGER IF NOT EXISTS cloud_year_closed_immutable
BEFORE INSERT ON cloud_academic_year_events
FOR EACH ROW
WHEN EXISTS (
    SELECT 1 FROM cloud_academic_years
    WHERE school_id=NEW.school_id AND id=NEW.academic_year_id
      AND status='closed'
      AND (NEW.status<>'closed' OR
           NEW.start_date<>start_date OR NEW.end_date<>end_date)
)
BEGIN SELECT RAISE(ABORT, 'cloud_year_closed_immutable'); END;

CREATE TRIGGER IF NOT EXISTS cloud_year_event_applied
AFTER INSERT ON cloud_academic_year_events
FOR EACH ROW
BEGIN
    INSERT INTO cloud_academic_years
        (school_id,id,label,start_date,end_date,status,last_revision)
    VALUES
        (NEW.school_id,NEW.academic_year_id,NEW.label,NEW.start_date,
         NEW.end_date,NEW.status,NEW.revision)
    ON CONFLICT(school_id,id)
    DO UPDATE SET label=excluded.label,start_date=excluded.start_date,
                  end_date=excluded.end_date,status=excluded.status,
                  last_revision=excluded.last_revision;

    INSERT INTO cloud_operations
        (school_id,operation_id,actor_user_id,device_id,
         operation_kind,request_sha256,outcome,settled_at)
    VALUES
        (NEW.school_id,NEW.operation_id,NEW.actor_user_id,NEW.device_id,
         'academic_year_upsert',NEW.request_sha256,'applied',unixepoch());

    INSERT INTO cloud_change_log
        (school_id,operation_id,entity_kind,entity_id,change_kind,payload_sha256)
    VALUES
        (NEW.school_id,NEW.operation_id,'academic_year',
         NEW.academic_year_id,'upsert',NEW.request_sha256);
END;

CREATE TRIGGER IF NOT EXISTS cloud_academic_year_events_no_update
BEFORE UPDATE ON cloud_academic_year_events
BEGIN SELECT RAISE(ABORT, 'cloud_year_event_immutable'); END;
CREATE TRIGGER IF NOT EXISTS cloud_academic_year_events_no_delete
BEFORE DELETE ON cloud_academic_year_events
BEGIN SELECT RAISE(ABORT, 'cloud_year_event_immutable'); END;

CREATE TABLE IF NOT EXISTS cloud_promotions (
    school_id TEXT NOT NULL,
    id TEXT NOT NULL CHECK (length(id) BETWEEN 1 AND 128),
    academic_year_id TEXT NOT NULL,
    -- Section nullable dans le modele natif SCOLIA (ex. cursus superieur).
    section_id TEXT,
    code TEXT NOT NULL CHECK (length(code) BETWEEN 2 AND 16),
    name TEXT NOT NULL CHECK (length(trim(name)) BETWEEN 1 AND 120),
    is_active INTEGER NOT NULL CHECK (is_active IN (0,1)),
    last_revision INTEGER NOT NULL CHECK (last_revision > 0),
    PRIMARY KEY (school_id, id),
    UNIQUE (school_id, academic_year_id, code),
    FOREIGN KEY (school_id, academic_year_id)
      REFERENCES cloud_academic_years(school_id, id) ON DELETE RESTRICT,
    FOREIGN KEY (school_id, section_id)
      REFERENCES cloud_sections(school_id, id) ON DELETE RESTRICT
);
CREATE INDEX IF NOT EXISTS cloud_promotions_year_section
    ON cloud_promotions(school_id, academic_year_id, section_id);

CREATE TABLE IF NOT EXISTS cloud_promotion_events (
    revision INTEGER PRIMARY KEY AUTOINCREMENT,
    school_id TEXT NOT NULL,
    operation_id TEXT NOT NULL CHECK (length(operation_id) BETWEEN 1 AND 128),
    promotion_id TEXT NOT NULL CHECK (length(promotion_id) BETWEEN 1 AND 128),
    academic_year_id TEXT NOT NULL,
    section_id TEXT,
    actor_user_id TEXT NOT NULL,
    device_id TEXT NOT NULL,
    code TEXT NOT NULL CHECK (length(code) BETWEEN 2 AND 16),
    name TEXT NOT NULL CHECK (length(trim(name)) BETWEEN 1 AND 120),
    is_active INTEGER NOT NULL CHECK (is_active IN (0,1)),
    expected_revision INTEGER NOT NULL CHECK (expected_revision >= 0),
    request_sha256 TEXT NOT NULL CHECK (length(request_sha256) = 64),
    created_at INTEGER NOT NULL DEFAULT (unixepoch()),
    UNIQUE (school_id, operation_id),
    FOREIGN KEY (school_id, academic_year_id)
      REFERENCES cloud_academic_years(school_id, id) ON DELETE RESTRICT,
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

-- Interdit de changer l'annee d'une promotion existante : creer une
-- nouvelle promotion pour l'annee suivante, comme le client natif.
CREATE TRIGGER IF NOT EXISTS cloud_promotion_year_immutable
BEFORE INSERT ON cloud_promotion_events
FOR EACH ROW
WHEN EXISTS (
    SELECT 1 FROM cloud_promotions
    WHERE school_id=NEW.school_id AND id=NEW.promotion_id
      AND academic_year_id<>NEW.academic_year_id
)
BEGIN SELECT RAISE(ABORT, 'cloud_promotion_year_immutable'); END;

CREATE TRIGGER IF NOT EXISTS cloud_promotion_requires_open_year
BEFORE INSERT ON cloud_promotion_events
FOR EACH ROW
WHEN NOT EXISTS(
    SELECT 1 FROM cloud_academic_years
    WHERE school_id=NEW.school_id AND id=NEW.academic_year_id
      AND status IN ('preparation', 'open'))
BEGIN SELECT RAISE(ABORT, 'cloud_promotion_invalid_year'); END;

CREATE TRIGGER IF NOT EXISTS cloud_promotion_requires_active_section
BEFORE INSERT ON cloud_promotion_events
FOR EACH ROW
WHEN NEW.is_active=1 AND NEW.section_id IS NOT NULL AND NOT EXISTS(
    SELECT 1 FROM cloud_sections
    WHERE school_id=NEW.school_id AND id=NEW.section_id AND is_active=1)
BEGIN SELECT RAISE(ABORT, 'cloud_promotion_invalid_section'); END;

-- Promotion, operation et journal sont atomiques et idempotents.
CREATE TRIGGER IF NOT EXISTS cloud_promotion_event_applied
AFTER INSERT ON cloud_promotion_events
FOR EACH ROW
BEGIN
    INSERT INTO cloud_promotions
        (school_id,id,academic_year_id,section_id,
         code,name,is_active,last_revision)
    VALUES
        (NEW.school_id,NEW.promotion_id,NEW.academic_year_id,NEW.section_id,
         NEW.code,NEW.name,NEW.is_active,NEW.revision)
    ON CONFLICT(school_id,id)
    DO UPDATE SET academic_year_id=excluded.academic_year_id,
                  section_id=excluded.section_id,code=excluded.code,
                  name=excluded.name,is_active=excluded.is_active,
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
BEGIN SELECT RAISE(ABORT, 'cloud_promotion_event_immutable'); END;
CREATE TRIGGER IF NOT EXISTS cloud_promotion_events_no_delete
BEFORE DELETE ON cloud_promotion_events
BEGIN SELECT RAISE(ABORT, 'cloud_promotion_event_immutable'); END;
