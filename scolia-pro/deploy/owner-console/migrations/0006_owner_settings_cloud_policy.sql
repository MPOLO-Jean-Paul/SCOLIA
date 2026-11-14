-- Réglages de la console saisis par JP-Services (identifiants R2 chiffrés, jamais en clair).
CREATE TABLE IF NOT EXISTS owner_settings(key TEXT PRIMARY KEY, value TEXT NOT NULL, updated_at INTEGER NOT NULL);
-- Sauvegarde en ligne proposée par JP-Services à une école : son serveur l'active seul,
-- avec sa propre clé ; le responsable de l'école peut toujours l'arrêter.
ALTER TABLE schools ADD COLUMN cloud_requested INTEGER NOT NULL DEFAULT 0 CHECK(cloud_requested IN (0,1));
