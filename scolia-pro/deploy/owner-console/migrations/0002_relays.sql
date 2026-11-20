CREATE TABLE IF NOT EXISTS owner_relays(id TEXT PRIMARY KEY,label TEXT NOT NULL,url TEXT NOT NULL,state TEXT NOT NULL DEFAULT 'disabled' CHECK(state IN ('active','disabled')),revision INTEGER NOT NULL,created_at INTEGER NOT NULL,updated_at INTEGER NOT NULL);
CREATE UNIQUE INDEX IF NOT EXISTS ix_active_relay ON owner_relays(state) WHERE state='active';
CREATE TABLE IF NOT EXISTS school_relay_preferences (school_id TEXT PRIMARY KEY REFERENCES schools(id) ON DELETE CASCADE,allowed INTEGER NOT NULL DEFAULT 0 CHECK(allowed IN (0,1)),approved_revision INTEGER,changed_at INTEGER NOT NULL);
