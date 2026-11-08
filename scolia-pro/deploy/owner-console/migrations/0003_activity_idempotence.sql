CREATE TABLE IF NOT EXISTS school_activity_events(school_id TEXT NOT NULL REFERENCES schools(id) ON DELETE CASCADE,event_id TEXT NOT NULL,activity_day TEXT NOT NULL,created_at INTEGER NOT NULL,PRIMARY KEY(school_id,event_id));
CREATE INDEX IF NOT EXISTS ix_school_event_created_at ON school_activity_events(created_at);
