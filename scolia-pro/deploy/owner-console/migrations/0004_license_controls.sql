-- Les droits commerciaux portent exclusivement sur l'établissement SERVEUR.
-- La facturation est désactivée par défaut : aucun verrouillage lors d'une installation.
ALTER TABLE schools ADD COLUMN billing_enabled INTEGER NOT NULL DEFAULT 0 CHECK(billing_enabled IN (0,1));
ALTER TABLE schools ADD COLUMN trial_days INTEGER NOT NULL DEFAULT 30;
ALTER TABLE schools ADD COLUMN paid_until INTEGER;
ALTER TABLE schools ADD COLUMN grace_until INTEGER;
ALTER TABLE schools ADD COLUMN manual_override TEXT NOT NULL DEFAULT 'none'
  CHECK(manual_override IN ('none','allow','read_only'));
ALTER TABLE schools ADD COLUMN override_until INTEGER;
ALTER TABLE schools ADD COLUMN override_reason TEXT NOT NULL DEFAULT '';
CREATE INDEX IF NOT EXISTS ix_license_paid_until ON schools(paid_until);
CREATE INDEX IF NOT EXISTS ix_license_billing_enabled ON schools(billing_enabled);

-- Préserver les anciennes décisions manuelles sans verrouillage involontaire des autres écoles.
UPDATE schools SET manual_override='read_only', override_reason='Suspension historique'
  WHERE state='suspended';
