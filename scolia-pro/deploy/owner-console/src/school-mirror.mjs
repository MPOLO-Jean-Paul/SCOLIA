// SCOLIA D1 pilot: a second, isolated SCHOOL_DB for *opaque* encrypted frames.
// This is server-to-cloud pairing only; no public client reads, no cloud payment
// validation or user/password mirroring. All routes are called AFTER verifying
// the existing school-server bearer token against OWNER_DB.
const H32 = /^[a-f0-9]{32}$/;
const H64 = /^[a-f0-9]{64}$/;
const B64 = /^[A-Za-z0-9_-]+$/;
const MAX_FRAME_BYTES = 24 * 1024;
const MAX_FRAMES_PER_SCHOOL = 1000;
const MAX_BODY = 35000;
const PREFIX = "/api/v1/server/cloud/mirror/";
const safeInt = value => Number.isSafeInteger(value) && value > 0;
const safeSchool = school => typeof school?.id === "string" && school.id.length >= 20;
const validCloud = cloud => cloud?.enabled === 1 && H32.test(cloud.key_fingerprint ?? "");

function frameBytes(encoded) {
  if (typeof encoded !== "string" || !encoded.length || encoded.length > 33000 || !B64.test(encoded)) return null;
  try {
    const raw = atob(encoded.replace(/-/g, "+").replace(/_/g, "/").padEnd(Math.ceil(encoded.length / 4) * 4, "="));
    if (raw.length < 40 || raw.length > MAX_FRAME_BYTES || !raw.startsWith("SCOLIA-NUAGE-1")) return null;
    return Uint8Array.from(raw, c => c.charCodeAt(0));
  } catch { return null; }
}
async function sha256(bytes) {
  const digest = await crypto.subtle.digest("SHA-256", bytes);
  return [...new Uint8Array(digest)].map(b => b.toString(16).padStart(2, "0")).join("");
}
async function bodyJson(request) {
  const length = Number(request.headers.get("Content-Length") || 0);
  if (!Number.isFinite(length) || length > MAX_BODY) return null;
  const raw = await request.text();
  if (raw.length > MAX_BODY) return null;
  try { return JSON.parse(raw); } catch { return null; }
}
const error = (fail, message, status) => fail(message, status);

export async function schoolMirrorRequest(request, env, url,
  { school, cloud, fail, result, now = () => Date.now() }) {
  const db = env.SCHOOL_DB;
  if (!db) return error(fail, "Base D1 scolaire non configuree.", 503);
  if (!safeSchool(school) || !validCloud(cloud))
    return error(fail, "Jumelage D1 non actif pour cet etablissement.", 409);
  const path = url.pathname;
  if (!path.startsWith(PREFIX)) return error(fail, "Route inconnue.", 404);

  if (path === PREFIX + "pair" && request.method === "POST") {
    const input = await bodyJson(request);
    if (!H64.test(input?.installation_hash ?? "") ||
        input?.key_fingerprint !== cloud.key_fingerprint ||
        input?.consent_confirmed !== true)
      return error(fail, "Identite, cle de l'ecole et accord du responsable requis.", 400);
    const old = await db.prepare("SELECT * FROM school_mirror_pairs WHERE school_id=?")
      .bind(school.id).first();
    if (old && (old.installation_hash !== input.installation_hash ||
      old.key_fingerprint !== input.key_fingerprint))
      return error(fail, "Serveur ou cle differents : migration supervisée requise.", 409);
    const timestamp = now();
    await db.prepare(`INSERT OR IGNORE INTO school_mirror_pairs
      (school_id,installation_hash,key_fingerprint,created_at,updated_at)
      VALUES(?,?,?,?,?)`)
      .bind(school.id, input.installation_hash, input.key_fingerprint, timestamp, timestamp).run();
    return result({ paired: true, school_id: school.id,
      transport: "https", database: "D1", cloud_authoritative: false,
      notice: "Jumelage du serveur uniquement. Aucune operation metier validee dans le cloud." });
  }

  const pair = await db.prepare("SELECT * FROM school_mirror_pairs WHERE school_id=?")
    .bind(school.id).first();
  if (!pair || pair.key_fingerprint !== cloud.key_fingerprint)
    return error(fail, "Serveur non jumele ou cle de chiffrement modifiee.", 409);

  if (path === PREFIX + "status" && request.method === "GET") {
    const row = await db.prepare("SELECT COUNT(*) AS total, MAX(seq) AS cursor FROM school_mirror_frames WHERE school_id=?")
      .bind(school.id).first();
    return result({ paired: true, cloud_authoritative: false, frames: row?.total ?? 0,
      cursor: row?.cursor ?? 0, limit: MAX_FRAMES_PER_SCHOOL });
  }

  if (path === PREFIX + "frames" && request.method === "GET") {
    const after = Number(url.searchParams.get("after") ?? "0");
    const requested = Number(url.searchParams.get("limit") ?? "20");
    if (!Number.isSafeInteger(after) || after < 0 || !Number.isSafeInteger(requested) ||
        requested < 1 || requested > 50)
      return error(fail, "Curseur ou limite incorrect.", 400);
    const rows = await db.prepare(`SELECT seq,frame_id,stream_id,revision,ciphertext,cipher_sha256
      FROM school_mirror_frames WHERE school_id=? AND seq>?
      ORDER BY seq LIMIT ?`).bind(school.id, after, requested).all();
    return result({ frames: rows.results, cursor: rows.results.at(-1)?.seq ?? after });
  }

  if (path === PREFIX + "frames" && request.method === "POST") {
    const input = await bodyJson(request);
    if (!H32.test(input?.frame_id ?? "") || !H64.test(input?.stream_id ?? "") ||
        !safeInt(input?.revision) || !H64.test(input?.cipher_sha256 ?? ""))
      return error(fail, "Identifiants de la trame ou revision invalides.", 400);
    const bytes = frameBytes(input.ciphertext);
    if (!bytes || await sha256(bytes) !== input.cipher_sha256)
      return error(fail, "Trame non chiffree, invalide ou alteree.", 422);
    const previous = await db.prepare(`SELECT frame_id,stream_id,revision,cipher_sha256
      FROM school_mirror_frames
      WHERE school_id=? AND (frame_id=? OR (stream_id=? AND revision=?))
      LIMIT 1`).bind(school.id, input.frame_id, input.stream_id, input.revision).first();
    if (previous) {
      if (previous.frame_id !== input.frame_id ||
          previous.stream_id !== input.stream_id ||
          previous.revision !== input.revision ||
          previous.cipher_sha256 !== input.cipher_sha256)
        return error(fail, "Revision concurrente : operation refusee.", 409);
      return result({ stored: true, duplicate: true });
    }
    // Atomic quota check also applies under concurrent submissions; never
    // overwrite historical school data or spill into commercial OWNER_DB.
    const saved = await db.prepare(`INSERT OR IGNORE INTO school_mirror_frames
      (school_id,frame_id,stream_id,revision,ciphertext,cipher_sha256,created_at)
      SELECT ?,?,?,?,?,?,?
      WHERE (SELECT COUNT(*) FROM school_mirror_frames WHERE school_id=?) < ?`)
      .bind(school.id, input.frame_id, input.stream_id, input.revision,
        input.ciphertext, input.cipher_sha256, now(), school.id, MAX_FRAMES_PER_SCHOOL).run();
    if (!saved.meta?.changes)
      return error(fail, "Quota atteint ou ecriture concurrente : controle requis.", 409);
    return result({ stored: true, duplicate: false }, 201);
  }
  return error(fail, "Methode ou route inconnue.", 404);
}
