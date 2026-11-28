// Continuité en ligne des établissements SCOLIA Pro.
//
// 1. Sauvegardes : le serveur scolaire envoie automatiquement une copie compressée et
//    CHIFFRÉE PAR L'ÉCOLE (AES-256-GCM) de sa base. Elle est rangée dans R2 ; D1 ne garde
//    que la date, la taille, l'empreinte et l'empreinte de clé. JP-Services ne peut pas la lire.
// 2. Relève : quand le serveur scolaire est injoignable, chaque appareil dépose ses
//    opérations (chiffrées pour le serveur) dans une boîte de dépôt. Au retour, le serveur
//    les reprend, les applique avec ses propres règles, puis publie leur issue ; l'appareil
//    l'apprend et ne les renvoie pas une seconde fois.

import { backupStore } from "./storage.mjs";
import { schoolMirrorRequest } from "./school-mirror.mjs";

export const MAX_BACKUP_BYTES = 90 * 1024 * 1024;          // limite d'une requête Worker gratuite : 100 Mo
export const MAX_SCHOOL_BACKUP_BYTES = 1024 * 1024 * 1024;  // 1 Gio par établissement
export const MAX_OPERATION_BYTES = 256 * 1024;
export const MAX_PENDING_OPERATIONS = 20000;
export const MAX_PENDING_BYTES = 200 * 1024 * 1024;
export const OUTCOME_RETENTION_MS = 180 * 86_400_000;
export const RECENT_WINDOW_MS = 48 * 3_600_000;
export const RECENT_COPIES = 24;
export const DAILY_COPIES = 30;
export const MONTHLY_COPIES = 12;
export const SERVER_ONLINE_MS = 5 * 60_000;

const HEX64 = /^[a-f0-9]{64}$/;
const FINGERPRINT = /^[a-f0-9]{16,64}$/;
const OP_ID = /^[a-zA-Z0-9_-]{16,100}$/;
const DEVICE = /^[a-zA-Z0-9_-]{8,96}$/;
const BASE64URL = /^[A-Za-z0-9_-]+$/;

/**
 * Copies à supprimer : on garde toujours la plus récente, les 24 copies des dernières 48 h,
 * la dernière de chacun des 30 derniers jours et de chacun des 12 derniers mois, dans la
 * limite de 1 Gio par établissement (les plus anciennes partent d'abord, jamais les 3 dernières).
 */
export function retentionPlan(rows, now = Date.now()) {
  const sorted = [...rows].sort((a, b) => b.created_at - a.created_at);
  const keep = new Set();
  sorted.slice(0, 1).forEach(r => keep.add(r.id));
  sorted.filter(r => now - r.created_at <= RECENT_WINDOW_MS).slice(0, RECENT_COPIES).forEach(r => keep.add(r.id));
  const days = new Set(), months = new Set();
  for (const r of sorted) {
    const iso = new Date(r.created_at).toISOString();
    const day = iso.slice(0, 10), month = iso.slice(0, 7);
    if (!days.has(day) && days.size < DAILY_COPIES) { days.add(day); keep.add(r.id); }
    if (!months.has(month) && months.size < MONTHLY_COPIES) { months.add(month); keep.add(r.id); }
  }
  let kept = sorted.filter(r => keep.has(r.id));
  let total = kept.reduce((sum, r) => sum + r.size_bytes, 0);
  for (let i = kept.length - 1; i >= 3 && total > MAX_SCHOOL_BACKUP_BYTES; i--) {
    total -= kept[i].size_bytes;
    keep.delete(kept[i].id);
  }
  return sorted.filter(r => !keep.has(r.id));
}

async function hex(buffer) {
  const digest = await crypto.subtle.digest("SHA-256", buffer);
  return Array.from(new Uint8Array(digest), n => n.toString(16).padStart(2, "0")).join("");
}

async function schoolByToken(db, token, sha) {
  if (!token || token.length > 512) return null;
  return db.prepare(`SELECT s.* FROM school_tokens t JOIN schools s ON s.id=t.school_id
    WHERE t.token_hash=? AND t.revoked_at IS NULL`).bind(await sha(token)).first();
}

async function cloudOf(db, schoolId) {
  return db.prepare("SELECT * FROM school_cloud WHERE school_id=?").bind(schoolId).first();
}

async function readJson(request, limit) {
  const declared = Number(request.headers.get("Content-Length") || "0");
  if (declared > limit) return null;
  const raw = await request.text();
  if (raw.length > limit) return null;
  try { return JSON.parse(raw); } catch { return null; }
}

export async function applyRetention(env, schoolId, now, store = null) {
  const db = env.OWNER_DB;
  store ??= await backupStore(env).catch(() => null);
  // Sans stockage joignable, rien n'est supprimé : jamais de ligne sans fichier ni l'inverse.
  if (!store) return 0;
  const rows = (await db.prepare(`SELECT id,object_key,size_bytes,created_at FROM school_backups
    WHERE school_id=? ORDER BY created_at DESC`).bind(schoolId).all()).results;
  const expired = retentionPlan(rows, now);
  for (const row of expired) {
    // L'objet d'abord : une ligne D1 ne désigne jamais un fichier absent.
    if (store) await store.delete(row.object_key);
    await db.prepare("DELETE FROM school_backups WHERE id=? AND school_id=?").bind(row.id, schoolId).run();
  }
  return expired.length;
}

/** Routes du serveur scolaire, authentifiées par son jeton d'établissement. */
export async function serverCloudRequest(request, env, url, { bearer, sha, fail, result, nowMs, randomToken }) {
  const db = env.OWNER_DB;
  const school = await schoolByToken(db, bearer(request), sha);
  if (!school) return fail("Jeton de l'établissement requis.", 401);
  const now = nowMs();
  const path = url.pathname;

  if (request.method === "POST" && path === "/api/v1/server/cloud/enable") {
    const body = await readJson(request, 4096);
    if (typeof body?.enabled !== "boolean" || body?.disclosed_to_school_admin !== true)
      return fail("Décision explicite du responsable de l'établissement requise.", 400);
    if (body.enabled && !FINGERPRINT.test(body?.key_fingerprint ?? ""))
      return fail("Empreinte de la clé de chiffrement de l'école requise.", 400);
    if (!body.enabled) {
      // Arrêt : plus de dépôt possible ; les copies déjà envoyées restent récupérables.
      await db.prepare(`INSERT INTO school_cloud(school_id,enabled,changed_at) VALUES(?,0,?)
        ON CONFLICT(school_id) DO UPDATE SET enabled=0,deposit_token_hash=NULL,changed_at=excluded.changed_at`)
        .bind(school.id, now).run();
      return result({ enabled: false, notice: "Sauvegarde en ligne arrêtée ; les copies existantes sont conservées." });
    }
    const deposit = randomToken();
    await db.prepare(`INSERT INTO school_cloud(school_id,enabled,consented_at,key_fingerprint,deposit_token_hash,deposit_token_at,server_seen_at,changed_at)
      VALUES(?,1,?,?,?,?,?,?)
      ON CONFLICT(school_id) DO UPDATE SET enabled=1,consented_at=excluded.consented_at,
        key_fingerprint=excluded.key_fingerprint,deposit_token_hash=excluded.deposit_token_hash,
        deposit_token_at=excluded.deposit_token_at,server_seen_at=excluded.server_seen_at,changed_at=excluded.changed_at`)
      .bind(school.id, now, body.key_fingerprint, await sha(deposit), now, now, now).run();
    return result({ enabled: true, deposit_token: deposit,
      notice: "Jeton de dépôt des appareils : transmis aux postes par le serveur, jamais affiché." }, 201);
  }

  const cloud = await cloudOf(db, school.id);
  // Separate SCHOOL_DB: opaque frames ONLY. The same existing, revoked-aware
  // school bearer authentication is checked above before touching D1.
  if (path.startsWith("/api/v1/server/cloud/mirror/"))
    return schoolMirrorRequest(request, env, url,
      { school, cloud, fail, result, now: nowMs });

  // Lecture des copies : possible même après arrêt ou reprise sur un nouveau serveur
  // (elles restent chiffrées par la clé de l'école). Envoi et dépôt : seulement si active.
  const readOnlyRoute = request.method === "GET" && path.startsWith("/api/v1/server/cloud/backups");
  if (!cloud || (!cloud.enabled && !readOnlyRoute))
    return fail("Sauvegarde en ligne non activée pour cet établissement.", 409);

  if (request.method === "PUT" && path === "/api/v1/server/cloud/backups") {
    const store = await backupStore(env);
    if (!store) return fail("Stockage des sauvegardes non configuré par JP-Services.", 503);
    const expected = url.searchParams.get("sha256") ?? "";
    const fingerprint = url.searchParams.get("key") ?? "";
    if (!HEX64.test(expected) || !FINGERPRINT.test(fingerprint))
      return fail("Empreinte du fichier et de la clé requises.", 400);
    const declared = Number(request.headers.get("Content-Length") || "0");
    if (declared > MAX_BACKUP_BYTES) return fail("Sauvegarde trop volumineuse pour l'envoi en ligne.", 413);
    const bytes = await request.arrayBuffer();
    if (bytes.byteLength < 64 || bytes.byteLength > MAX_BACKUP_BYTES)
      return fail("Sauvegarde vide ou trop volumineuse.", 413);
    // Le fichier reçu doit être exactement celui que le serveur a chiffré.
    if (await hex(bytes) !== expected) return fail("Fichier altéré pendant l'envoi : renvoyer la sauvegarde.", 422);
    const id = crypto.randomUUID();
    const key = `schools/${school.id}/backups/${new Date(now).toISOString().replace(/[:.]/g, "-")}-${id}.scb`;
    await store.put(key, bytes, { sha256: expected,
      httpMetadata: { contentType: "application/octet-stream" },
      customMetadata: { school: school.id, key: fingerprint } });
    await db.batch([
      db.prepare(`INSERT INTO school_backups(id,school_id,object_key,size_bytes,sha256,key_fingerprint,created_at)
        VALUES(?,?,?,?,?,?,?)`).bind(id, school.id, key, bytes.byteLength, expected, fingerprint, now),
      db.prepare("UPDATE school_cloud SET last_backup_at=?,server_seen_at=? WHERE school_id=?").bind(now, now, school.id),
    ]);
    const removed = await applyRetention(env, school.id, now, store);
    return result({ id, created_at: now, size_bytes: bytes.byteLength, removed }, 201);
  }

  if (request.method === "GET" && path === "/api/v1/server/cloud/backups") {
    const rows = await db.prepare(`SELECT id,size_bytes,sha256,key_fingerprint,created_at FROM school_backups
      WHERE school_id=? ORDER BY created_at DESC LIMIT 200`).bind(school.id).all();
    return result({ backups: rows.results });
  }

  const download = /^\/api\/v1\/server\/cloud\/backups\/([a-f0-9-]{36})$/.exec(path);
  if (request.method === "GET" && download) {
    const store = await backupStore(env);
    if (!store) return fail("Stockage des sauvegardes non configuré par JP-Services.", 503);
    const row = await db.prepare("SELECT * FROM school_backups WHERE id=? AND school_id=?").bind(download[1], school.id).first();
    if (!row) return fail("Sauvegarde inconnue.", 404);
    const object = await store.get(row.object_key);
    if (!object) return fail("Sauvegarde introuvable dans le stockage.", 404);
    return new Response(object.body, { headers: {
      "Content-Type": "application/octet-stream", "Cache-Control": "no-store",
      "X-Content-Type-Options": "nosniff", "X-Scolia-Sha256": row.sha256,
      "X-Scolia-Key": row.key_fingerprint, "Content-Length": String(row.size_bytes) } });
  }

  if (request.method === "GET" && path === "/api/v1/server/cloud/mailbox") {
    const limit = Math.min(200, Math.max(1, Number(url.searchParams.get("limit") || "100") | 0));
    const rows = await db.prepare(`SELECT op_id,device_hash,sequence,payload,created_at FROM school_mailbox
      WHERE school_id=? ORDER BY created_at, device_hash, sequence LIMIT ?`).bind(school.id, limit).all();
    // Le relevé de la boîte vaut signe de vie du serveur : les appareils savent qu'il est revenu.
    await db.prepare("UPDATE school_cloud SET server_seen_at=?,last_drain_at=? WHERE school_id=?").bind(now, now, school.id).run();
    return result({ operations: rows.results, server_time: now });
  }

  if (request.method === "POST" && path === "/api/v1/server/cloud/mailbox/ack") {
    const body = await readJson(request, 64 * 1024);
    const items = Array.isArray(body?.outcomes) ? body.outcomes : null;
    if (!items || items.length === 0 || items.length > 200 ||
        items.some(x => !OP_ID.test(x?.op_id ?? "") || !["applied", "refused"].includes(x?.outcome)))
      return fail("Issues d'opérations invalides.", 400);
    // Issue et retrait de la boîte dans la même transaction : une opération n'est jamais perdue.
    await db.batch(items.flatMap(x => [
      db.prepare(`INSERT INTO school_mailbox_outcomes(school_id,op_id,outcome,done_at) VALUES(?,?,?,?)
        ON CONFLICT(school_id,op_id) DO UPDATE SET outcome=excluded.outcome,done_at=excluded.done_at`)
        .bind(school.id, x.op_id, x.outcome, now),
      db.prepare("DELETE FROM school_mailbox WHERE school_id=? AND op_id=?").bind(school.id, x.op_id),
    ]));
    return result({ acknowledged: items.length });
  }

  if (request.method === "POST" && path === "/api/v1/server/cloud/presence") {
    await db.prepare("UPDATE school_cloud SET server_seen_at=? WHERE school_id=?").bind(now, school.id).run();
    return result({ server_time: now });
  }

  if (request.method === "POST" && path === "/api/v1/server/cloud/deposit-token") {
    const deposit = randomToken();
    await db.prepare("UPDATE school_cloud SET deposit_token_hash=?,deposit_token_at=? WHERE school_id=?")
      .bind(await sha(deposit), now, school.id).run();
    return result({ deposit_token: deposit }, 201);
  }

  return fail("Ressource de continuité inconnue.", 404);
}

async function schoolByDepositToken(db, token, sha) {
  if (!token || token.length > 512) return null;
  return db.prepare("SELECT * FROM school_cloud WHERE deposit_token_hash=? AND enabled=1").bind(await sha(token)).first();
}

/** Routes des appareils (téléphones, postes) : dépôt seulement, jamais de lecture des opérations. */
export async function deviceCloudRequest(request, env, url, { bearer, sha, fail, result, nowMs }) {
  const db = env.OWNER_DB;
  const cloud = await schoolByDepositToken(db, bearer(request), sha);
  if (!cloud) return fail("Jeton de dépôt de l'établissement requis.", 401);
  const now = nowMs();
  const status = () => ({ server_seen_at: cloud.server_seen_at ?? null,
    server_online: !!cloud.server_seen_at && now - cloud.server_seen_at <= SERVER_ONLINE_MS, server_time: now });

  if (request.method === "GET" && url.pathname === "/api/v1/device/status")
    return result(status());

  if (request.method === "POST" && url.pathname === "/api/v1/device/mailbox") {
    const body = await readJson(request, MAX_OPERATION_BYTES + 4096);
    if (!body) return fail("Opération trop volumineuse ou illisible.", 413);
    const opId = body.op_id, device = body.device_id, payload = body.payload;
    if (!OP_ID.test(opId ?? "") || !DEVICE.test(device ?? "") || !Number.isSafeInteger(body.sequence) || body.sequence < 0 ||
        typeof payload !== "string" || payload.length < 32 || payload.length > MAX_OPERATION_BYTES || !BASE64URL.test(payload))
      return fail("Opération chiffrée invalide.", 400);
    const done = await db.prepare("SELECT outcome FROM school_mailbox_outcomes WHERE school_id=? AND op_id=?")
      .bind(cloud.school_id, opId).first();
    if (done) return result({ stored: false, outcome: done.outcome, ...status() });
    const load = await db.prepare("SELECT count(*) AS n, coalesce(sum(size_bytes),0) AS bytes FROM school_mailbox WHERE school_id=?")
      .bind(cloud.school_id).first();
    if ((load?.n ?? 0) >= MAX_PENDING_OPERATIONS || (load?.bytes ?? 0) + payload.length > MAX_PENDING_BYTES)
      return fail("Boîte de dépôt pleine : l'opération reste gardée sur l'appareil.", 507);
    const inserted = await db.prepare(`INSERT OR IGNORE INTO school_mailbox(school_id,op_id,device_hash,sequence,payload,size_bytes,created_at)
      VALUES(?,?,?,?,?,?,?)`).bind(cloud.school_id, opId, await sha(device), body.sequence, payload, payload.length, now).run();
    return result({ stored: true, duplicate: !inserted.meta?.changes, ...status() }, inserted.meta?.changes ? 201 : 200);
  }

  if (request.method === "POST" && url.pathname === "/api/v1/device/mailbox/outcomes") {
    const body = await readJson(request, 32 * 1024);
    const ids = Array.isArray(body?.op_ids) ? body.op_ids : null;
    if (!ids || ids.length === 0 || ids.length > 200 || ids.some(x => !OP_ID.test(x ?? "")))
      return fail("Liste d'opérations invalide.", 400);
    const marks = ids.map(() => "?").join(",");
    const [done, waiting] = await Promise.all([
      db.prepare(`SELECT op_id,outcome FROM school_mailbox_outcomes WHERE school_id=? AND op_id IN (${marks})`)
        .bind(cloud.school_id, ...ids).all(),
      db.prepare(`SELECT op_id FROM school_mailbox WHERE school_id=? AND op_id IN (${marks})`)
        .bind(cloud.school_id, ...ids).all(),
    ]);
    const outcomes = Object.fromEntries(ids.map(id => [id, "unknown"]));
    for (const x of waiting.results) outcomes[x.op_id] = "pending";
    for (const x of done.results) outcomes[x.op_id] = x.outcome;
    return result({ outcomes, ...status() });
  }

  return fail("Ressource d'appareil inconnue.", 404);
}

/** Vue du propriétaire : état de la continuité, jamais le contenu. */
export async function ownerCloudOverview(db, schoolId, now) {
  const [cloud, backups, pending] = await Promise.all([
    cloudOf(db, schoolId),
    db.prepare(`SELECT id,size_bytes,created_at,key_fingerprint FROM school_backups WHERE school_id=?
      ORDER BY created_at DESC LIMIT 60`).bind(schoolId).all(),
    db.prepare("SELECT count(*) AS n, coalesce(sum(size_bytes),0) AS bytes, min(created_at) AS oldest FROM school_mailbox WHERE school_id=?")
      .bind(schoolId).first(),
  ]);
  return {
    enabled: !!cloud?.enabled,
    consented_at: cloud?.consented_at ?? null,
    key_fingerprint: cloud?.key_fingerprint ?? null,
    server_seen_at: cloud?.server_seen_at ?? null,
    server_online: !!cloud?.server_seen_at && now - cloud.server_seen_at <= SERVER_ONLINE_MS,
    last_backup_at: cloud?.last_backup_at ?? null,
    backup_late: !!cloud?.enabled && (!cloud?.last_backup_at || now - cloud.last_backup_at > 26 * 3_600_000),
    last_drain_at: cloud?.last_drain_at ?? null,
    backups: backups.results,
    backup_bytes: backups.results.reduce((s, r) => s + r.size_bytes, 0),
    pending_operations: pending?.n ?? 0,
    pending_bytes: pending?.bytes ?? 0,
    oldest_pending_at: pending?.oldest ?? null,
  };
}

/** Nettoyage quotidien : rétention des copies et issues anciennes. */
export async function cloudMaintenance(env, now) {
  const db = env.OWNER_DB;
  const store = await backupStore(env).catch(() => null);
  if (!store) return;
  const schools = (await db.prepare("SELECT DISTINCT school_id FROM school_backups").all()).results;
  for (const s of schools) await applyRetention(env, s.school_id, now, store);
  await db.prepare("DELETE FROM school_mailbox_outcomes WHERE done_at < ?").bind(now - OUTCOME_RETENTION_MS).run();
}
