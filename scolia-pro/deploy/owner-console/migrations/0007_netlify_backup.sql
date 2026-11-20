-- Netlify HTTPS backup metadata only. No school secrets.
CREATE TABLE IF NOT EXISTS owner_provider_netlify (
  id INTEGER PRIMARY KEY CHECK(id=1),
  label TEXT NOT NULL,
  base_url TEXT NOT NULL,
  state TEXT NOT NULL DEFAULT 'disabled' CHECK(state='disabled'),
  updated_at INTEGER NOT NULL
);
