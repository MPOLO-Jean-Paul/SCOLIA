CREATE TABLE IF NOT EXISTS owner_invites (code_hash TEXT PRIMARY KEY,school_name TEXT NOT NULL,created_at INTEGER NOT NULL,expires_at INTEGER NOT NULL,used_at INTEGER);
CREATE TABLE IF NOT EXISTS schools (id TEXT PRIMARY KEY,name TEXT NOT NULL,state TEXT NOT NULL DEFAULT 'trial' CHECK(state IN ('trial','active','suspended','expired')),created_at INTEGER NOT NULL,last_seen INTEGER NOT NULL,license_expires_at INTEGER,consent_version TEXT NOT NULL);
CREATE TABLE IF NOT EXISTS school_tokens (token_hash TEXT PRIMARY KEY,school_id TEXT NOT NULL REFERENCES schools(id) ON DELETE CASCADE,created_at INTEGER NOT NULL,revoked_at INTEGER);
CREATE TABLE IF NOT EXISTS server_devices (school_id TEXT NOT NULL REFERENCES schools(id) ON DELETE CASCADE,installation_hash TEXT NOT NULL,device_name TEXT NOT NULL,first_seen INTEGER NOT NULL,last_seen INTEGER NOT NULL,last_ip TEXT,ip_seen_at INTEGER,PRIMARY KEY(school_id,installation_hash));
CREATE TABLE IF NOT EXISTS school_activity (school_id TEXT NOT NULL REFERENCES schools(id) ON DELETE CASCADE,activity_day TEXT NOT NULL,login_count INTEGER NOT NULL DEFAULT 0,heartbeat_count INTEGER NOT NULL DEFAULT 0,PRIMARY KEY(school_id,activity_day));
CREATE TABLE IF NOT EXISTS owner_audit (id TEXT PRIMARY KEY,action TEXT NOT NULL,school_id TEXT REFERENCES schools(id) ON DELETE SET NULL,created_at INTEGER NOT NULL);
CREATE INDEX IF NOT EXISTS ix_school_seen ON schools(last_seen);
CREATE INDEX IF NOT EXISTS ix_devices_seen ON server_devices(last_seen);
CREATE INDEX IF NOT EXISTS ix_activity_day ON school_activity(activity_day);
