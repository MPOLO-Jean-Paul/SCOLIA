-- Continuité en ligne : sauvegardes chiffrées (R2) et boîte de dépôt des opérations
-- faites pendant une coupure du serveur scolaire. Le contenu est chiffré PAR L'ÉCOLE :
-- ni JP-Services ni Cloudflare ne détiennent la clé ; seules les métadonnées sont ici.
CREATE TABLE IF NOT EXISTS school_cloud(
  school_id TEXT PRIMARY KEY REFERENCES schools(id) ON DELETE CASCADE,
  enabled INTEGER NOT NULL DEFAULT 0 CHECK(enabled IN (0,1)),
  consented_at INTEGER,
  key_fingerprint TEXT,
  deposit_token_hash TEXT,
  deposit_token_at INTEGER,
  server_seen_at INTEGER,
  last_backup_at INTEGER,
  last_drain_at INTEGER,
  changed_at INTEGER NOT NULL);
CREATE UNIQUE INDEX IF NOT EXISTS ix_cloud_deposit_token ON school_cloud(deposit_token_hash) WHERE deposit_token_hash IS NOT NULL;

CREATE TABLE IF NOT EXISTS school_backups(
  id TEXT PRIMARY KEY,
  school_id TEXT NOT NULL REFERENCES schools(id) ON DELETE CASCADE,
  object_key TEXT NOT NULL UNIQUE,
  size_bytes INTEGER NOT NULL,
  sha256 TEXT NOT NULL,
  key_fingerprint TEXT NOT NULL,
  created_at INTEGER NOT NULL);
CREATE INDEX IF NOT EXISTS ix_backups_school ON school_backups(school_id, created_at);

CREATE TABLE IF NOT EXISTS school_mailbox(
  school_id TEXT NOT NULL REFERENCES schools(id) ON DELETE CASCADE,
  op_id TEXT NOT NULL,
  device_hash TEXT NOT NULL,
  sequence INTEGER NOT NULL,
  payload TEXT NOT NULL,
  size_bytes INTEGER NOT NULL,
  created_at INTEGER NOT NULL,
  PRIMARY KEY(school_id, op_id));
CREATE INDEX IF NOT EXISTS ix_mailbox_order ON school_mailbox(school_id, created_at, sequence);

-- Issue d'une opération (appliquée, refusée) gardée 180 jours : l'appareil qui l'avait
-- déposée apprend qu'elle est faite et ne la renvoie pas une seconde fois.
CREATE TABLE IF NOT EXISTS school_mailbox_outcomes(
  school_id TEXT NOT NULL REFERENCES schools(id) ON DELETE CASCADE,
  op_id TEXT NOT NULL,
  outcome TEXT NOT NULL CHECK(outcome IN ('applied','refused')),
  done_at INTEGER NOT NULL,
  PRIMARY KEY(school_id, op_id));
CREATE INDEX IF NOT EXISTS ix_outcomes_done ON school_mailbox_outcomes(done_at);

-- Reprise après sinistre : une invitation peut relier un NOUVEAU serveur à un établissement
-- existant ; les anciens jetons sont alors révoqués (jamais deux serveurs principaux).
ALTER TABLE owner_invites ADD COLUMN recover_school_id TEXT REFERENCES schools(id) ON DELETE CASCADE;
ALTER TABLE owner_invites ADD COLUMN recover_claim TEXT;
