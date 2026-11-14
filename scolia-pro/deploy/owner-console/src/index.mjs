import {
  CONSENT_VERSION, cleanLabel, parseRegistration,
  parseHeartbeat, normalizeIp
} from "./policy.mjs";
import { ownerRelayRequest, schoolRelayRequest } from "./relays.mjs";
import { evaluateLicense, validateLicensePatch, MAX_GRACE_DAYS, DAY_MS } from "./licenses.mjs";
import { serverCloudRequest, deviceCloudRequest, ownerCloudOverview, cloudMaintenance } from "./cloud.mjs";
import { ownerStorageRequest } from "./storage.mjs";

// Stockage prêt : liaison R2 du déploiement, ou identifiants saisis dans la console.
async function storageConfigured(env) {
  if (env.BACKUPS) return true;
  return !!(await env.OWNER_DB.prepare("SELECT 1 AS ok FROM owner_settings WHERE key='storage.r2'").first());
}

const headers = {
  "Content-Type": "application/json; charset=utf-8",
  "Cache-Control": "no-store",
  "X-Content-Type-Options": "nosniff",
  "Referrer-Policy": "no-referrer"
};
const result = (data, status = 200) => new Response(JSON.stringify(data),
  { status, headers });
const fail = (error, status) => result({ error }, status);
const randomToken = () => {
  const bytes = crypto.getRandomValues(new Uint8Array(32));
  return btoa(String.fromCharCode(...bytes)).replace(/\+/g, "-")
    .replace(/\//g, "_").replace(/=+$/g, "");
};
async function sha(value) {
  const digest = await crypto.subtle.digest("SHA-256", new TextEncoder().encode(value));
  return Array.from(new Uint8Array(digest), n => n.toString(16).padStart(2, "0")).join("");
}
async function sameSecret(given, expected) {
  if (!expected || !given || given.length > 512) return false;
  const a = new Uint8Array(await crypto.subtle.digest("SHA-256", new TextEncoder().encode(given)));
  const b = new Uint8Array(await crypto.subtle.digest("SHA-256", new TextEncoder().encode(expected)));
  let difference = 0;
  for (let i = 0; i < a.length; i++) difference |= a[i] ^ b[i];
  return difference === 0;
}
async function jsonBody(request) {
  if (Number(request.headers.get("Content-Length") || "0") > 8192) return null;
  const raw = await request.text();
  if (raw.length > 8192) return null;
  try { return JSON.parse(raw); } catch { return null; }
}
const bearer = request => /^Bearer (.+)$/i.exec(request.headers.get("Authorization") || "")?.[1] || "";
const nowMs = () => Date.now();
// Cloudflare Access sets ctx.access only after authenticating the request.
// An HTTP header with the same name cannot forge this platform context.
function ownerAccessAllowed(env, ctx) {
  return typeof env.OWNER_ACCESS_AUD === "string" &&
    env.OWNER_ACCESS_AUD.length >= 20 &&
    ctx?.access?.aud === env.OWNER_ACCESS_AUD;
}
function allowedOrigin(request) {
  const origin = request.headers.get("Origin");
  return !origin || origin === new URL(request.url).origin;
}
async function prune(db, now) {
  // Ne jamais conserver les IP au-delà de 30 jours.
  await db.prepare("UPDATE server_devices SET last_ip=NULL, ip_seen_at=NULL WHERE ip_seen_at < ?")
    .bind(now - 30 * 86400_000).run();
}
async function audit(db, action, schoolId, now) {
  await db.prepare("INSERT INTO owner_audit(id,action,school_id,created_at) VALUES(?,?,?,?)")
    .bind(crypto.randomUUID(), action, schoolId, now).run();
}

async function serveOwner(request, env, url) {
  if (!env.OWNER_ADMIN_TOKEN || !(await sameSecret(bearer(request), env.OWNER_ADMIN_TOKEN)))
    return fail("Accès propriétaire refusé.", 401);
  if (url.pathname.startsWith("/api/v1/owner/relays"))
    return ownerRelayRequest(request, env, url, { jsonBody, fail, result, audit, nowMs });
  if (url.pathname === "/api/v1/owner/settings/storage")
    return ownerStorageRequest(request, env, { jsonBody, fail, result, audit, nowMs, fetcher: env.STORAGE_FETCH });
  const db = env.OWNER_DB, now = nowMs();
  // Sauvegarde en ligne proposée par JP-Services : le serveur de l'école l'active seul au prochain contrôle.
  const cloudPolicy = /^\/api\/v1\/owner\/schools\/([a-f0-9-]{36})\/cloud$/.exec(url.pathname);
  if (request.method === "PATCH" && cloudPolicy) {
    const body = await jsonBody(request);
    if (typeof body?.requested !== "boolean") return fail("Choix « requested » (vrai ou faux) requis.", 400);
    if (body.requested && !(await storageConfigured(env)))
      return fail("Configurez d'abord le stockage des sauvegardes (Cloudflare R2).", 409);
    const update = await db.prepare("UPDATE schools SET cloud_requested=? WHERE id=?").bind(body.requested ? 1 : 0, cloudPolicy[1]).run();
    if (!update.meta?.changes) return fail("Établissement inconnu.", 404);
    await audit(db, body.requested ? "cloud_requested" : "cloud_withdrawn", cloudPolicy[1], now);
    return result({ id: cloudPolicy[1], requested: body.requested,
      notice: body.requested ? "Le serveur de l'école activera la sauvegarde en ligne à son prochain contrôle (15 minutes au plus)."
        : "Proposition retirée : le serveur de l'école arrêtera la sauvegarde en ligne à son prochain contrôle." });
  }
  if (request.method === "POST" && url.pathname === "/api/v1/owner/cloud/request-all") {
    if (!(await storageConfigured(env))) return fail("Configurez d'abord le stockage des sauvegardes (Cloudflare R2).", 409);
    const update = await db.prepare("UPDATE schools SET cloud_requested=1 WHERE cloud_requested=0").run();
    await audit(db, "cloud_requested_all", null, now);
    return result({ updated: update.meta?.changes ?? 0,
      notice: "Toutes les écoles activeront la sauvegarde en ligne à leur prochain contrôle." });
  }
  if (request.method === "GET") {
    await prune(db, now);
    if (url.pathname === "/api/v1/owner/summary") {
      const [schools, devices, active, policies] = await Promise.all([
        db.prepare("SELECT count(*) AS n FROM schools").first(),
        db.prepare("SELECT count(*) AS n FROM server_devices").first(),
        db.prepare("SELECT count(*) AS n FROM schools WHERE last_seen >= ?")
          .bind(now - 30 * 86400_000).first(),
        // Ne pas confondre l'ancien champ « state » avec la licence effective.
        db.prepare(`SELECT created_at,billing_enabled,trial_days,paid_until,grace_until,
          manual_override,override_until FROM schools`).all()
      ]);
      const suspended = policies.results.filter(s => evaluateLicense(s, now).readonly).length;
      const cloud = await db.prepare(`SELECT
          (SELECT count(*) FROM school_cloud WHERE enabled=1) AS enabled,
          (SELECT count(*) FROM school_cloud WHERE enabled=1 AND (last_backup_at IS NULL OR last_backup_at < ?)) AS late,
          (SELECT coalesce(sum(size_bytes),0) FROM school_backups) AS bytes,
          (SELECT count(*) FROM school_mailbox) AS pending`).bind(now - 26 * 3_600_000).first();
      return result({ schools: schools?.n ?? 0, server_devices: devices?.n ?? 0,
        active_schools_30d: active?.n ?? 0, suspended,
        cloud_enabled: cloud?.enabled ?? 0, cloud_backup_late: cloud?.late ?? 0,
        storage_configured: await storageConfigured(env),
        cloud_backup_bytes: cloud?.bytes ?? 0, cloud_pending_operations: cloud?.pending ?? 0 });
    }
    if (url.pathname === "/api/v1/owner/audit") {
      const rows = await db.prepare(`SELECT a.action,a.school_id,s.name AS school_name,a.created_at
        FROM owner_audit a LEFT JOIN schools s ON s.id=a.school_id
        ORDER BY a.created_at DESC,a.id DESC LIMIT 100`).all();
      return result({ history: rows.results });
    }
    if (url.pathname === "/api/v1/owner/schools") {
      const rows = await db.prepare(`SELECT s.id,s.name,s.state,s.created_at,s.last_seen,
        s.license_expires_at,s.billing_enabled,s.trial_days,s.paid_until,s.grace_until,
        s.manual_override,s.override_until,s.cloud_requested,
        (SELECT count(*) FROM server_devices d WHERE d.school_id=s.id) AS device_count,
        coalesce(c.enabled,0) AS cloud_enabled, c.last_backup_at, c.server_seen_at,
        (SELECT count(*) FROM school_mailbox m WHERE m.school_id=s.id) AS pending_operations
        FROM schools s LEFT JOIN school_cloud c ON c.school_id=s.id
        ORDER BY s.last_seen DESC LIMIT 500`).all();
      return result({ schools: rows.results.map(s => ({...s, license: evaluateLicense(s, now),
        cloud_backup_late: !!s.cloud_enabled && (!s.last_backup_at || now - s.last_backup_at > 26 * 3_600_000)})) });
    }
    const match = /^\/api\/v1\/owner\/schools\/([a-f0-9-]{36})$/.exec(url.pathname);
    if (match) {
      const school = await db.prepare("SELECT * FROM schools WHERE id=?").bind(match[1]).first();
      if (!school) return fail("Établissement inconnu.", 404);
      const devices = await db.prepare(`SELECT device_name,first_seen,last_seen,last_ip,ip_seen_at,
        installation_hash FROM server_devices WHERE school_id=?
        ORDER BY last_seen DESC LIMIT 100`).bind(match[1]).all();
      const history = await db.prepare(`SELECT activity_day,login_count,heartbeat_count
        FROM school_activity WHERE school_id=? ORDER BY activity_day DESC LIMIT 90`)
        .bind(match[1]).all();
      return result({ school, license: evaluateLicense(school, now),
        devices: devices.results, activity: history.results,
        cloud: await ownerCloudOverview(db, match[1], now) });
    }
  }

  if (request.method === "POST" && url.pathname === "/api/v1/owner/invitations") {
    const body = await jsonBody(request);
    const name = cleanLabel(body?.school_name, 140);
    if (name.length < 3) return fail("Nom d'établissement requis.", 400);
    const invite = randomToken();
    const expires = now + 14 * 86400_000;
    await db.prepare(`INSERT INTO owner_invites(code_hash,school_name,created_at,expires_at)
      VALUES(?,?,?,?)`).bind(await sha(invite), name, now, expires).run();
    await audit(db, "invitation_created", null, now);
    return result({ invite_code: invite, school_name: name, expires_at: expires,
      notice: "Transmettre ce code uniquement au responsable du serveur SCOLIA." }, 201);
  }
  // Reprise après sinistre : relier un NOUVEAU serveur à cet établissement. À l'usage,
  // les jetons de l'ancien serveur sont révoqués : il n'y a jamais deux serveurs principaux.
  const recovery = /^\/api\/v1\/owner\/schools\/([a-f0-9-]{36})\/recovery-invitation$/.exec(url.pathname);
  if (request.method === "POST" && recovery) {
    const school = await db.prepare("SELECT id,name FROM schools WHERE id=?").bind(recovery[1]).first();
    if (!school) return fail("Établissement inconnu.", 404);
    const invite = randomToken();
    const expires = now + 3 * 86400_000;
    await db.prepare(`INSERT INTO owner_invites(code_hash,school_name,created_at,expires_at,recover_school_id)
      VALUES(?,?,?,?,?)`).bind(await sha(invite), school.name, now, expires, school.id).run();
    await audit(db, "recovery_invitation_created", school.id, now);
    return result({ invite_code: invite, school_name: school.name, expires_at: expires,
      notice: "Invitation de reprise : à saisir sur le NOUVEAU serveur, puis restaurer une copie en ligne avec le code de secours de l'école." }, 201);
  }
  if (request.method === "PATCH") {
    const advanced = /^\/api\/v1\/owner\/schools\/([a-f0-9-]{36})\/license$/.exec(url.pathname);
    if (advanced) {
      const patch = validateLicensePatch(await jsonBody(request));
      if (!patch) return fail("Paramètres de licence invalides.", 400);
      if (patch.manual_override === "read_only" && (!patch.reason || patch.reason.length < 5))
        return fail("Motif du blocage manuel requis (5 caractères minimum).", 400);
      if (patch.manual_override === "allow") {
        if (!patch.reason || patch.reason.length < 5 ||
            !Number.isSafeInteger(patch.override_until) ||
            patch.override_until <= now ||
            patch.override_until > now + MAX_GRACE_DAYS * DAY_MS)
          return fail("La dérogation exige un motif et une échéance future limitée à 120 jours.", 400);
      }
      const dbPatch = {};
      for (const [key, value] of Object.entries(patch))
        dbPatch[key === "reason" ? "override_reason" : key] = value;
      const columns = Object.keys(dbPatch);
      const values = columns.map(x => dbPatch[x]);
      const update = await db.prepare("UPDATE schools SET " +
        columns.map(x => x + "=?").join(",") + " WHERE id=?")
        .bind(...values, advanced[1]).run();
      if (!update.meta?.changes) return fail("Établissement inconnu.", 404);
      await audit(db, "license_policy_changed", advanced[1], now);
      const school = await db.prepare("SELECT * FROM schools WHERE id=?")
        .bind(advanced[1]).first();
      return result({ id: advanced[1], license: evaluateLicense(school, now),
        notice: "Les données, les connexions et les lectures restent accessibles ; les écritures dépendent du statut." });
    }
  }
  // L'ancien endpoint modifiait un statut historique sans changer la restriction réelle.
  // Refus explicite plutôt qu'une confirmation trompeuse de suspension/réactivation.
  if (request.method === "PATCH" && /^\/api\/v1\/owner\/schools\/[a-f0-9-]{36}$/.test(url.pathname))
    return fail("Ancienne commande retirée : utiliser /api/v1/owner/schools/{id}/license.", 410);
  return fail("Ressource propriétaire inconnue.", 404);
}

async function registerSchool(request, env) {
  const body = parseRegistration(await jsonBody(request));
  if (!body.ok) return fail(body.error, 400);
  const db = env.OWNER_DB, now = nowMs();
  const codeHash = await sha(body.code);
  const invited = await db.prepare(`SELECT school_name,recover_school_id FROM owner_invites
    WHERE code_hash=? AND used_at IS NULL AND expires_at>?`)
    .bind(codeHash, now).first();
  if (!invited) return fail("Invitation invalide, utilisée ou expirée.", 403);
  // Interdire qu'une invitation pour l'école A soit employée par un serveur B.
  const identity = value => cleanLabel(value,140).normalize("NFKD")
    .replace(/[\u0300-\u036f]/g,"").toLowerCase();
  if (identity(invited.school_name) !== identity(body.school))
    return fail("Le nom de l'établissement ne correspond pas à l'invitation JP-Services.", 409);
  if (invited.recover_school_id) return recoverSchool(request, db, body, codeHash, invited.recover_school_id, now);
  const id = crypto.randomUUID(), token = randomToken();
  const tokenHash = await sha(token);
  // Le nom enregistré est celui de l'invitation, non celui librement fourni par le client.
  // Les quatre écritures forment UNE transaction D1 : si l'une échoue,
  // l'invitation reste disponible et aucun compte incomplet n'est créé.
  const results = await db.batch([
    db.prepare(`INSERT INTO schools(id,name,state,created_at,last_seen,consent_version)
      SELECT ?,school_name,'trial',?,?,? FROM owner_invites
      WHERE code_hash=? AND used_at IS NULL AND expires_at>?`)
      .bind(id, now, now, CONSENT_VERSION, codeHash, now),
    db.prepare(`INSERT INTO school_tokens(token_hash,school_id,created_at)
      SELECT ?,id,? FROM schools WHERE id=?`).bind(tokenHash, now, id),
    db.prepare(`INSERT INTO server_devices(school_id,installation_hash,device_name,first_seen,last_seen,last_ip,ip_seen_at)
      SELECT id,?,?,?,?,?,? FROM schools WHERE id=?`)
      .bind(await sha(body.deviceId), body.device, now, now,
        normalizeIp(request.headers.get("CF-Connecting-IP")), now, id),
    db.prepare(`UPDATE owner_invites SET used_at=?
      WHERE code_hash=? AND used_at IS NULL AND expires_at>?`)
      .bind(now, codeHash, now)
  ]);
  if (results[0].meta?.changes !== 1 || results[3].meta?.changes !== 1)
    return fail("Invitation invalide, utilisée ou expirée.", 403);
  return result({ school_id: id, school_token: token, license_status: "free",
    notice: "Conservez le jeton dans le coffre sécurisé du serveur scolaire, jamais dans Git." }, 201);
}

// Nouveau serveur d'un établissement existant (machine remplacée, volée ou détruite).
// Une seule transaction : invitation consommée, anciens jetons révoqués, nouveau jeton,
// sauvegarde en ligne suspendue jusqu'à ce que le nouveau serveur l'active avec sa clé.
async function recoverSchool(request, db, body, codeHash, schoolId, now) {
  const token = randomToken();
  const claim = await sha(token);
  // Chaque écriture n'agit que si CETTE requête a réservé l'invitation (claim unique) :
  // deux usages simultanés ne peuvent jamais produire deux serveurs ni révoquer à tort.
  const owned = "EXISTS (SELECT 1 FROM owner_invites WHERE code_hash=? AND recover_claim=?)";
  const results = await db.batch([
    db.prepare(`UPDATE owner_invites SET used_at=?,recover_claim=? WHERE code_hash=? AND used_at IS NULL AND expires_at>?`)
      .bind(now, claim, codeHash, now),
    db.prepare(`UPDATE school_tokens SET revoked_at=? WHERE school_id=? AND revoked_at IS NULL AND ${owned}`)
      .bind(now, schoolId, codeHash, claim),
    db.prepare(`INSERT INTO school_tokens(token_hash,school_id,created_at) SELECT ?,?,? WHERE ${owned}`)
      .bind(claim, schoolId, now, codeHash, claim),
    db.prepare(`INSERT INTO server_devices(school_id,installation_hash,device_name,first_seen,last_seen,last_ip,ip_seen_at)
      SELECT ?,?,?,?,?,?,? WHERE ${owned} ON CONFLICT(school_id,installation_hash) DO UPDATE SET
      device_name=excluded.device_name,last_seen=excluded.last_seen,last_ip=excluded.last_ip,ip_seen_at=excluded.ip_seen_at`)
      .bind(schoolId, await sha(body.deviceId), body.device, now, now, normalizeIp(request.headers.get("CF-Connecting-IP")), now, codeHash, claim),
    db.prepare(`UPDATE school_cloud SET enabled=0,deposit_token_hash=NULL,changed_at=? WHERE school_id=? AND ${owned}`)
      .bind(now, schoolId, codeHash, claim),
  ]);
  if (results[0].meta?.changes !== 1) return fail("Invitation invalide, utilisée ou expirée.", 403);
  await audit(db, "school_server_recovered", schoolId, now);
  return result({ school_id: schoolId, school_token: token, license_status: "free", recovered: true,
    notice: "Serveur de reprise relié. Restaurez une copie en ligne avec le code de secours, puis réactivez la sauvegarde en ligne." }, 201);
}

async function heartbeat(request, env) {
  const token = bearer(request);
  if (!token || token.length > 512) return fail("Jeton de l'établissement requis.", 401);
  const validated = parseHeartbeat(await jsonBody(request));
  if (!validated) return fail("Événement de serveur invalide.", 400);
  const db = env.OWNER_DB, now = nowMs();
  const school = await db.prepare(`SELECT s.* FROM school_tokens t JOIN schools s ON s.id=t.school_id
    WHERE t.token_hash=? AND t.revoked_at IS NULL`).bind(await sha(token)).first();
  if (!school) return fail("Licence ou jeton inconnu.", 401);

  // Le serveur peut renvoyer plusieurs fois un même rapport après une coupure :
  // une seule inscription est comptabilisée par identifiant d'événement.
  if (validated.occurredAt > now + 5 * 60000 ||
      validated.occurredAt < now - 90 * 86400_000)
    return fail("Date d'activité hors de la fenêtre de reprise.", 400);
  const activityDay = new Date(validated.occurredAt).toISOString().slice(0, 10);
  // Compteur, marqueur d'idempotence et présence sont atomiques.
  // Le compteur est incrémenté AVANT d'insérer le marqueur, seulement si absent.
  // Une panne/reprise n'entraîne donc ni double comptage ni événement perdu.
  await db.batch([
    db.prepare(`INSERT INTO school_activity
      (school_id,activity_day,login_count,heartbeat_count)
      SELECT ?,?,?,1 WHERE NOT EXISTS (
        SELECT 1 FROM school_activity_events WHERE school_id=? AND event_id=?)
      ON CONFLICT(school_id,activity_day) DO UPDATE SET
        login_count=login_count+excluded.login_count,
        heartbeat_count=heartbeat_count+1`)
      .bind(school.id, activityDay, validated.event === "login" ? 1 : 0,
        school.id, validated.eventId),
    db.prepare(`INSERT OR IGNORE INTO school_activity_events
      (school_id,event_id,activity_day,created_at) VALUES(?,?,?,?)`)
      .bind(school.id, validated.eventId, activityDay, now),
    db.prepare("UPDATE schools SET last_seen=? WHERE id=?").bind(now, school.id),
    db.prepare("UPDATE school_cloud SET server_seen_at=? WHERE school_id=?").bind(now, school.id),
    db.prepare(`INSERT INTO server_devices
      (school_id,installation_hash,device_name,first_seen,last_seen,last_ip,ip_seen_at)
      VALUES(?,?,?,?,?,?,?)
      ON CONFLICT(school_id,installation_hash) DO UPDATE SET
        device_name=excluded.device_name,last_seen=excluded.last_seen,
        last_ip=excluded.last_ip,ip_seen_at=excluded.ip_seen_at`)
      .bind(school.id, await sha(validated.deviceId), validated.device, now, now,
        normalizeIp(request.headers.get("CF-Connecting-IP")), now)
  ]);
  return result({ school_id: school.id, ...evaluateLicense(school, now),
    local_data_access: "always_readable",
    // Proposition de JP-Services : le serveur active seul la sauvegarde en ligne (clé de l'école).
    cloud_requested: !!school.cloud_requested && await storageConfigured(env) });
}

function htmlDashboard() {
  return `<!doctype html><html lang="fr"><head><meta charset="utf-8">
<meta name="viewport" content="width=device-width,initial-scale=1">
<title>JP-Services · Console propriétaire SCOLIA Pro</title>
<style>
:root{font-family:system-ui,-apple-system,"Segoe UI",Arial,sans-serif;color:#18304e;background:#f5f7fb}
*{box-sizing:border-box}body{margin:0;padding:0}header{background:#fff;border-bottom:1px solid #e4eaf2;padding:24px max(20px,calc((100vw - 1100px)/2))}
h1{font-size:25px;margin:5px 0}small{color:#667b95}.wrap{max-width:1100px;margin:30px auto;padding:0 20px}
.cards{display:grid;grid-template-columns:repeat(auto-fit,minmax(180px,1fr));gap:14px}
.card,.panel{background:#fff;border:1px solid #e3eaf2;border-radius:18px;padding:20px;box-shadow:0 6px 20px #1234630d}
.metric{font-size:32px;font-weight:750;margin:8px 0}.panel{margin-top:18px}
button{background:#1769e6;color:#fff;border:0;border-radius:12px;padding:11px 15px;cursor:pointer;font-weight:650}
button.secondary{background:#eaf1fb;color:#204a80}input{width:100%;max-width:320px;border:1px solid #d5deeb;border-radius:10px;padding:12px;font:inherit}
.toolbar{display:flex;gap:10px;flex-wrap:wrap;margin:15px 0}
table{width:100%;border-collapse:collapse}th,td{border-bottom:1px solid #edf1f6;text-align:left;padding:14px 8px}th{font-size:12px;color:#637792}
.info{font-size:13px;color:#688099}.error{color:#ac2844;font-size:13px}#details{white-space:pre-wrap;font-size:13px;overflow:auto}
@media(max-width:640px){.wrap{margin:15px auto}table{display:block;overflow-x:auto}h1{font-size:21px}}
</style></head><body><header><small>JP-SERVICES · ADMINISTRATION COMMERCIALE</small>
<h1>SCOLIA Pro — Console propriétaire</h1>
<small>Établissements serveurs, licences et activité. Aucun dossier d'apprenant n'est accessible.</small><br>
<small>Assistance JP-Services : <a href="mailto:contact@jp-services.org">contact@jp-services.org</a></small></header>
<main class="wrap"><section class="panel" id="auth">
<h2>Accès réservé</h2><p>La console requiert le jeton administrateur secret de JP-Services.
Il n'est ni stocké dans le navigateur ni transmis à une autre origine.</p>
<div class="toolbar"><input id="token" type="password" placeholder="Jeton propriétaire" autocomplete="off">
<button id="connect">Ouvrir la console</button></div><p class="error" id="error"></p></section>
<section id="workspace" hidden>
<div class="cards"><article class="card"><small>Établissements</small><div class="metric" id="schools">—</div></article>
<article class="card"><small>Serveurs enregistrés</small><div class="metric" id="devices">—</div></article>
<article class="card"><small>Écoles actives (30 j)</small><div class="metric" id="active">—</div></article>
<article class="card"><small>Licences suspendues</small><div class="metric" id="suspended">—</div></article>
<article class="card"><small>Sauvegarde en ligne active</small><div class="metric" id="cloudOn">—</div><small id="cloudLate"></small></article>
<article class="card"><small>Copies chiffrées stockées</small><div class="metric" id="cloudBytes">—</div><small id="cloudPending"></small></article></div>
<section class="panel" id="storagePanel"><h2>Sauvegarde en ligne des écoles — stockage Cloudflare R2</h2>
<p class="info" id="storageStatus">État du stockage…</p>
<p class="info">Une seule fois : dans Cloudflare › R2, créez le bucket (ex. <b>scolia-pro-backups</b>), puis « Gérer les jetons API R2 » ›
jeton « Object Read &amp; Write » limité à ce bucket. Collez ici l'identifiant du compte, le nom du bucket, l'Access Key ID et la Secret Access Key.
La console écrit, relit et supprime un fichier témoin avant d'enregistrer ; la clé secrète est gardée chiffrée et n'est jamais réaffichée.</p>
<div class="toolbar">
<input id="r2Account" placeholder="Identifiant du compte Cloudflare (32 caractères)" autocomplete="off">
<input id="r2Bucket" placeholder="Nom du bucket R2" value="scolia-pro-backups" autocomplete="off">
<input id="r2Key" placeholder="Access Key ID" autocomplete="off">
<input id="r2Secret" type="password" placeholder="Secret Access Key" autocomplete="new-password">
</div>
<div class="toolbar"><button id="r2Save">Tester et enregistrer</button>
<button id="r2Test" class="secondary">Vérifier le stockage</button>
<button id="r2Remove" class="secondary">Supprimer les identifiants</button>
<button id="cloudAll" class="secondary">Activer la sauvegarde en ligne pour toutes les écoles</button></div>
<p class="info" id="storageNotice"></p></section>
<section class="panel"><h2>Établissements</h2>
<div class="toolbar"><button id="refresh">Actualiser</button><button id="logout" class="secondary">Verrouiller la console</button><input id="newschool" placeholder="Nom d'un nouvel établissement">
<button id="invite" class="secondary">Créer une invitation</button></div>
<p class="info" id="inviteResult"></p><div style="overflow-x:auto"><table><thead><tr>
<th>Établissement</th><th>Licence</th><th>Serveurs</th><th>Dernière activité</th><th>Sauvegarde en ligne</th><th>Gestion</th></tr></thead>
<tbody id="rows"></tbody></table></div></section>
<section class="panel"><h2>Fournisseurs de relais Internet</h2>
<p class="info">Configurer des adresses WSS publiques sans publier de clés sur GitHub.
Un nouveau relais reste désactivé ; chaque école doit approuver le changement.
Le suivi des licences utilise HTTPS directement, même depuis un Wi-Fi disposant d'Internet.</p>
<div class="toolbar">
<input id="relayLabel" placeholder="Nom du fournisseur (ex. Relais RDC)">
<input id="relayUrl" placeholder="wss://relais.exemple.org/" type="url">
<button id="relayCreate">Ajouter ce relais</button>
</div>
<p class="info" id="relayNotice"></p>
<div style="overflow-x:auto"><table><thead><tr>
<th>Fournisseur</th><th>URL WSS</th><th>État</th><th>Révision</th><th>Action</th>
</tr></thead><tbody id="relayRows"></tbody></table></div>
</section>
<section class="panel"><h2>Journal des interventions</h2>
<p class="info">100 dernières opérations commerciales, sans données scolaires.</p>
<div style="overflow-x:auto"><table><thead><tr>
<th>Date</th><th>Établissement</th><th>Action</th>
</tr></thead><tbody id="auditRows"></tbody></table></div></section>
<section class="panel" id="detailsPanel" hidden><h2>Fiche établissement</h2>
<div class="cards" id="cloudCards"></div>
<p class="info">Copies et opérations chiffrées par l'école : JP-Services voit leur date et leur taille, jamais leur contenu.</p>
<div class="toolbar"><button id="cloudOn">Activer la sauvegarde en ligne pour cette école</button>
<button id="cloudOff" class="secondary">Retirer la sauvegarde en ligne</button>
<button id="recoveryInvite" class="secondary">Invitation de reprise (nouveau serveur)</button></div>
<p class="info" id="recoveryResult"></p>
<details><summary class="info">Détails techniques</summary><pre id="details"></pre></details>
<p class="info" id="licenseSummary">Statut de licence</p>
<div class="toolbar">
  <button id="billingOn">Activer la facturation</button>
  <button id="billingOff" class="secondary">Désactiver la facturation</button>
</div>
<div class="toolbar">
<label>Essai (jours) <input id="trialDays" type="number" min="0" max="365" value="30"></label>
<button id="saveTrial" class="secondary">Définir l'essai</button>
<label>Abonnement valable jusqu'au <input id="paidTo" type="date"></label>
<button id="savePayment">Enregistrer le renouvellement</button>
</div>
<div class="toolbar">
  <button id="grace7" class="secondary">Dérogation 7 jours</button>
  <button id="grace30" class="secondary">Dérogation 30 jours</button>
  <button id="manualReadOnly" class="secondary">Programmer le blocage manuel</button>
  <button id="removeOverride" class="secondary">Lever la mesure manuelle</button>
</div>
<p class="info">Un blocage concerne les écritures de l'établissement et de tous ses postes.
Les comptes restent ouvrables et les archives consultables. La licence n'efface jamais les données.</p></section></section></main>
<script>
(() => {
let secret="", selected=null;
const $=id=>document.getElementById(id);
const request=async (path,method="GET",body)=>{
 const r=await fetch(path,{method,headers:{"Authorization":"Bearer "+secret,
 "Content-Type":"application/json"},body:body?JSON.stringify(body):undefined});
 const value=await r.json();if(!r.ok)throw Error(value.error||"Opération refusée");return value;
};
function size(n){n=Number(n||0);return n>=1073741824?(n/1073741824).toFixed(2)+" Gio":n>=1048576?(n/1048576).toFixed(1)+" Mio":Math.round(n/1024)+" Kio";}
function card(label,value,hint){const a=document.createElement("article");a.className="card";
 const s1=document.createElement("small");s1.textContent=label;const m=document.createElement("div");m.className="metric";m.textContent=value;
 const s2=document.createElement("small");s2.textContent=hint||"";a.append(s1,m,s2);return a;}
function textCell(tr,v){const td=document.createElement("td");td.textContent=String(v??"—");tr.appendChild(td);return td;}
async function reload(){
 const [stats,listing,relays,history]=await Promise.all([
   request("/api/v1/owner/summary"),
   request("/api/v1/owner/schools"),
   request("/api/v1/owner/relays"),
   request("/api/v1/owner/audit")
 ]);
 $("schools").textContent=stats.schools;$("devices").textContent=stats.server_devices;
 $("active").textContent=stats.active_schools_30d;$("suspended").textContent=stats.suspended;
 $("cloudOn").textContent=stats.cloud_enabled;
 $("cloudLate").textContent=stats.cloud_backup_late?stats.cloud_backup_late+" en retard (> 26 h)":"aucune en retard";
 $("cloudBytes").textContent=size(stats.cloud_backup_bytes);
 $("cloudPending").textContent=stats.cloud_pending_operations+" opération(s) en attente de serveur";
 $("rows").replaceChildren();
 $("relayRows").replaceChildren();
 $("auditRows").replaceChildren();
 for(const event of history.history){
   const tr=document.createElement("tr");
   textCell(tr,new Date(event.created_at).toLocaleString("fr"));
   textCell(tr,event.school_name??"JP-Services");
   textCell(tr,event.action);$("auditRows").appendChild(tr);
 }
 for(const relay of relays.relays){
   const row=document.createElement("tr");
   textCell(row,relay.label);textCell(row,relay.url);
   textCell(row,relay.state);textCell(row,relay.revision);
   const td=document.createElement("td"), btn=document.createElement("button");
   const activation=relay.state!=="active";
   btn.textContent=activation?"Activer":"Désactiver";
   btn.className="secondary";
   btn.onclick=async()=>{
     const action=activation?"activer":"désactiver";
     if(!confirm("Voulez-vous "+action+" ce relais ? Chaque école devra approuver un nouveau fournisseur."))return;
     try {
       const r=await request("/api/v1/owner/relays/"+relay.id,"PATCH",
         {state:activation?"active":"disabled"});
       $("relayNotice").textContent=r.notice;await reload();
     } catch(e){alert(e.message);}
   };
   td.appendChild(btn);row.appendChild(td);$("relayRows").appendChild(row);
 }
 for(const s of listing.schools){
   const tr=document.createElement("tr");textCell(tr,s.name);textCell(tr,s.license.license_status+(s.license.readonly?" · lecture seule":""));
   textCell(tr,s.device_count);textCell(tr,s.last_seen?new Date(s.last_seen).toLocaleDateString("fr"):"Jamais");
   textCell(tr,!s.cloud_enabled?(s.cloud_requested?"Activation demandée":"Inactive"):(s.cloud_backup_late?"EN RETARD":"À jour")+
     (s.last_backup_at?" · "+new Date(s.last_backup_at).toLocaleString("fr"):"")+
     (s.pending_operations?" · "+s.pending_operations+" en attente":""));
   const td=document.createElement("td"),btn=document.createElement("button");
   btn.textContent="Consulter";btn.className="secondary";btn.onclick=()=>openSchool(s.id);
   td.appendChild(btn);tr.appendChild(td);$("rows").appendChild(tr);
 }
}
async function storage(){
 try{const st=await request("/api/v1/owner/settings/storage");
 $("storageStatus").textContent=(st.ready?"Stockage prêt":"Stockage non prêt")+" · "+
   (st.mode==="binding"?"liaison R2 du déploiement":st.mode==="credentials"?"identifiants saisis ("+st.bucket+", clé "+st.access_key_id+")":"aucun stockage configuré")+
   (st.error?" · "+st.error:"")+(st.unreadable?" · identifiants illisibles (jeton propriétaire changé) : ressaisissez-les":"");
 if(st.account_id)$("r2Account").value=st.account_id;if(st.bucket)$("r2Bucket").value=st.bucket;
 }catch(e){$("storageStatus").textContent=e.message;}
}
async function openSchool(id){
 selected=id;const value=await request("/api/v1/owner/schools/"+id);
 $("detailsPanel").hidden=false;
 $("details").textContent=JSON.stringify(value,null,2);
 const c=value.cloud;$("recoveryResult").textContent="";
 $("cloudCards").replaceChildren(
   card("Sauvegarde en ligne",c.enabled?(c.backup_late?"En retard":"Active"):(value.school.cloud_requested?"Activation demandée":"Inactive"),c.consented_at?"activée le "+new Date(c.consented_at).toLocaleDateString("fr"):"non activée par l'école"),
   card("Dernière copie",c.last_backup_at?new Date(c.last_backup_at).toLocaleString("fr"):"—",c.backups.length+" copie(s) · "+size(c.backup_bytes)),
   card("Serveur de l'école",c.server_online?"En ligne":"Hors ligne",c.server_seen_at?"vu le "+new Date(c.server_seen_at).toLocaleString("fr"):"jamais vu"),
   card("Opérations en attente",String(c.pending_operations),c.oldest_pending_at?"depuis le "+new Date(c.oldest_pending_at).toLocaleString("fr"):"rien à reprendre"));
 const lic=value.license;
 $("licenseSummary").textContent="Licence : "+lic.license_status+
   (lic.days_remaining===null?"":(" · "+lic.days_remaining+" jour(s) restant(s)"))+
   (lic.readonly?" · LECTURE SEULE":" · accès complet")+
   (lic.billing_enabled?" · paiement activé":" · paiement inactif");
 $("trialDays").value=value.school.trial_days??30;
 $("detailsPanel").scrollIntoView({behavior:"smooth"});
}
$("connect").onclick=async()=>{secret=$("token").value;$("token").value="";
 try{await reload();storage();$("auth").hidden=true;$("workspace").hidden=false;$("error").textContent="";}
 catch(e){secret="";$("error").textContent=e.message;}};
$("refresh").onclick=()=>reload().catch(e=>alert(e.message));
 $("logout").onclick=()=>{
   // Oubli explicite du secret en mémoire et fermeture de l'interface.
   secret="";selected=null;$("token").value="";$("detailsPanel").hidden=true;
   $("rows").replaceChildren();$("relayRows").replaceChildren();
   $("auditRows").replaceChildren();$("details").textContent="";
   $("inviteResult").textContent="";$("relayNotice").textContent="";
   $("licenseSummary").textContent="";$("newschool").value="";
   $("trialDays").value=30;$("paidTo").value="";
   $("r2Secret").value="";$("r2Key").value="";$("storageNotice").textContent="";$("storageStatus").textContent="";
   $("workspace").hidden=true;$("auth").hidden=false;
 };
$("relayCreate").onclick=async()=>{
 try {
   const created=await request("/api/v1/owner/relays","POST",{
     label:$("relayLabel").value,endpoint_wss:$("relayUrl").value
   });
   $("relayNotice").textContent="Relais ajouté (désactivé). "+created.notice;
   $("relayLabel").value="";$("relayUrl").value="";await reload();
 } catch(e){alert(e.message);}
};
$("invite").onclick=async()=>{
 try{const result=await request("/api/v1/owner/invitations","POST",{school_name:$("newschool").value});
 $("inviteResult").textContent="Invitation à transmettre au responsable (affichée une seule fois) : "+result.invite_code;}
 catch(e){alert(e.message);}
};
async function updateLicense(patch,explanation){
 if(!selected)return;
 if(explanation&&!confirm(explanation))return;
 try{
   const result=await request("/api/v1/owner/schools/"+selected+"/license","PATCH",patch);
   $("licenseSummary").textContent=result.license.license_status+" · "+result.notice;
   await reload();await openSchool(selected);
 }catch(e){alert(e.message);}
}
$("r2Save").onclick=async()=>{
 try{const r=await request("/api/v1/owner/settings/storage","PUT",{account_id:$("r2Account").value,bucket:$("r2Bucket").value,
   access_key_id:$("r2Key").value,secret_access_key:$("r2Secret").value});
 $("r2Secret").value="";$("r2Key").value="";$("storageNotice").textContent=r.notice;await storage();}
 catch(e){$("storageNotice").textContent=e.message;}
};
$("r2Test").onclick=()=>storage();
$("r2Remove").onclick=async()=>{
 if(!confirm("Supprimer les identifiants R2 ? Les envois des écoles s'arrêteront jusqu'à une nouvelle saisie (les copies existantes restent)."))return;
 try{const r=await request("/api/v1/owner/settings/storage","DELETE");$("storageNotice").textContent=r.notice;await storage();}
 catch(e){alert(e.message);}
};
$("cloudAll").onclick=async()=>{
 if(!confirm("Activer la sauvegarde en ligne pour toutes les écoles ? Chaque serveur l'activera seul avec sa propre clé ; son responsable devra noter le code de secours."))return;
 try{const r=await request("/api/v1/owner/cloud/request-all","POST",{});$("storageNotice").textContent=r.updated+" école(s) : "+r.notice;await reload();}
 catch(e){alert(e.message);}
};
async function cloudPolicy(requested){
 if(!selected)return;
 try{const r=await request("/api/v1/owner/schools/"+selected+"/cloud","PATCH",{requested});
 $("recoveryResult").textContent=r.notice;await reload();await openSchool(selected);}
 catch(e){alert(e.message);}
}
$("cloudOn").onclick=()=>cloudPolicy(true);
$("cloudOff").onclick=()=>{if(confirm("Retirer la sauvegarde en ligne de cette école ? Les copies existantes restent récupérables."))cloudPolicy(false);};
$("recoveryInvite").onclick=async()=>{
 if(!selected)return;
 if(!confirm("Créer une invitation de reprise ? Son usage sur un nouveau serveur révoquera l'ancien serveur de cet établissement."))return;
 try{const r=await request("/api/v1/owner/schools/"+selected+"/recovery-invitation","POST",{});
 $("recoveryResult").textContent="Invitation de reprise (affichée une seule fois, valable 3 jours) : "+r.invite_code;}
 catch(e){alert(e.message);}
};
$("billingOn").onclick=()=>updateLicense({billing_enabled:true},
 "Activer le calendrier de paiement pour cet établissement ? Les restrictions automatiques seront déterminées par les dates enregistrées.");
$("billingOff").onclick=()=>updateLicense({billing_enabled:false},
 "Désactiver la facturation ? La lecture seule manuelle éventuelle demeure applicable.");
$("saveTrial").onclick=()=>updateLicense({trial_days:Number($("trialDays").value)},
 "Mettre à jour la durée de l'essai à partir de la date de création du serveur ?");
$("savePayment").onclick=()=>{
 const date=$("paidTo").value;
 if(!date){alert("Choisissez une date d'échéance.");return;}
 const timestamp=Date.parse(date+"T23:59:59Z");
 if(!Number.isSafeInteger(timestamp)){alert("Date incorrecte.");return;}
 return updateLicense({billing_enabled:true,paid_until:timestamp,manual_override:"none"},
   "Confirmer le renouvellement de la licence de cet établissement ?");
};
$("grace7").onclick=()=>updateLicense({manual_override:"allow",
 override_until:Date.now()+7*86400_000,reason:"Derogation commerciale 7 jours"},
 "Accorder 7 jours d'accès complet à titre exceptionnel ?");
$("grace30").onclick=()=>updateLicense({manual_override:"allow",
 override_until:Date.now()+30*86400_000,reason:"Derogation commerciale 30 jours"},
 "Accorder 30 jours d'accès complet à titre exceptionnel ?");
$("manualReadOnly").onclick=()=>updateLicense({manual_override:"read_only",
 override_until:null,reason:"Blocage manuel de licence"},
 "Demander la lecture seule pour cet établissement ? Elle s'appliquera à la prochaine vérification en ligne du serveur (normalement sous 15 minutes). Les consultations resteront possibles.");
$("removeOverride").onclick=()=>updateLicense({manual_override:"none",
 override_until:null,reason:"Mesure manuelle levee"},
 "Annuler la mesure manuelle et revenir au calcul automatique ?");
})();
</script></body></html>`;
}

export default {
  async scheduled(controller, env, ctx) {
    if (!env.OWNER_DB) return;
    ctx.waitUntil((async () => {
      // Nettoyage garanti tous les jours, pas uniquement lorsqu'un compte
      // administrateur ouvre la console.
      const now = nowMs();
      await prune(env.OWNER_DB, now);
      await env.OWNER_DB.prepare("DELETE FROM school_activity_events WHERE created_at < ?")
        .bind(now - 120 * 86400_000).run();
      await cloudMaintenance(env, now);
    })());
  },
  async fetch(request, env, ctx) {
    if (!env.OWNER_DB) return fail("Base de licences non configurée.", 503);
    const url = new URL(request.url);
    // Les serveurs d'établissements accèdent uniquement aux routes publiques
    // et aux routes protégées par leur propre jeton. Le propriétaire requiert
    // Cloudflare Access *et* son jeton commercial, jamais l'un sans l'autre.
    if (url.pathname === "/console" || url.pathname.startsWith("/api/v1/owner/")) {
      if (!env.OWNER_ACCESS_AUD)
        return fail("Accès propriétaire non configuré : console indisponible.", 503);
      if (!ownerAccessAllowed(env, ctx))
        return fail("Authentification Cloudflare Access obligatoire.", 403);
    }
    if (request.method === "GET" && url.pathname === "/health")
      return result({ service: "SCOLIA Pro Owner", mode: "metadata-only", consent_version: CONSENT_VERSION,
        continuity: "encrypted-by-school" });
    if (request.method === "GET" && url.pathname === "/")
      return Response.redirect(new URL("/console", url).toString(), 302);
    if (request.method === "GET" && url.pathname === "/console")
      return new Response(htmlDashboard(), { headers: {
        "Content-Type": "text/html; charset=utf-8", "Cache-Control": "no-store",
        "X-Content-Type-Options": "nosniff", "Referrer-Policy": "no-referrer",
        "Content-Security-Policy": "default-src 'none'; script-src 'unsafe-inline'; style-src 'unsafe-inline'; connect-src 'self'; form-action 'none'; base-uri 'none'; frame-ancestors 'none'"
      } });
    if (!allowedOrigin(request)) return fail("Origine interdite.", 403);
    if (request.method === "GET" && url.pathname === "/api/v1/notice")
      return result({ version: CONSENT_VERSION,
        purpose: "Gestion des licences, disponibilité déclarée des serveurs scolaires, choix des relais Internet à la demande et, sur activation par l'école, continuité en ligne (sauvegardes et opérations chiffrées par l'école, illisibles par JP-Services).",
        collected: ["nom d'établissement", "nom de l'appareil serveur", "identifiant d'installation pseudonyme", "adresse IP réseau (30 jours)", "dates d'activité et statut de licence"],
        excluded: ["élèves", "enseignants", "utilisateurs internes", "paiements", "documents", "mots de passe", "clé de chiffrement des sauvegardes"],
        encrypted_by_school: ["copies de la base (sauvegarde en ligne)", "opérations faites pendant une coupure du serveur"],
        activation: "accord explicite du responsable du serveur et invitation JP-Services ; une seconde approbation est exigée pour activer ou changer le relais",
        data_retention: "IP 30 jours ; autres métadonnées tant que la licence est suivie" });
    if (request.method === "POST" && url.pathname === "/api/v1/register")
      return registerSchool(request, env);
    if (request.method === "POST" && url.pathname === "/api/v1/heartbeat")
      return heartbeat(request, env);
    if (url.pathname.startsWith("/api/v1/server/cloud/"))
      return serverCloudRequest(request, env, url,
        { bearer, sha, fail, result, nowMs, randomToken });
    if (url.pathname.startsWith("/api/v1/device/"))
      return deviceCloudRequest(request, env, url, { bearer, sha, fail, result, nowMs });
    if (url.pathname.startsWith("/api/v1/server/relay-"))
      return schoolRelayRequest(request, env, url,
        { bearer, sha, jsonBody, fail, result, nowMs });
    if (url.pathname.startsWith("/api/v1/owner/")) return serveOwner(request, env, url);
    return fail("Not found", 404);
  }
};
