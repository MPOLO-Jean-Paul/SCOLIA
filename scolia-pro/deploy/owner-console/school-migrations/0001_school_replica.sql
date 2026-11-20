-- SCOLIA Pro : secondary SCHOOL_DB, separate from the commercial OWNER_DB.
-- Only authenticated school-server ciphertext is stored. No cleartext pupil,
-- payment, account, password, cookie, recovery code or personal data.
CREATE TABLE IF NOT EXISTS school_mirror_pairs (
  school_id TEXT PRIMARY KEY,
  installation_hash TEXT NOT NULL CHECK (length(installation_hash)=64),
  key_fingerprint TEXT NOT NULL CHECK (length(key_fingerprint)=32),
  created_at INTEGER NOT NULL,
  updated_at INTEGER NOT NULL
);

CREATE TABLE IF NOT EXISTS school_mirror_frames (
  seq INTEGER PRIMARY KEY AUTOINCREMENT,
  school_id TEXT NOT NULL,
  frame_id TEXT NOT NULL CHECK (length(frame_id)=32),
  stream_id TEXT NOT NULL CHECK (length(stream_id)=64),
  revision INTEGER NOT NULL CHECK (revision>0),
  ciphertext TEXT NOT NULL,
  cipher_sha256 TEXT NOT NULL CHECK (length(cipher_sha256)=64),
  created_at INTEGER NOT NULL,
  UNIQUE (school_id,frame_id),
  UNIQUE (school_id,stream_id,revision),
  FOREIGN KEY (school_id) REFERENCES school_mirror_pairs(school_id)
);

CREATE INDEX IF NOT EXISTS school_mirror_frames_seek
ON school_mirror_frames(school_id,seq);
