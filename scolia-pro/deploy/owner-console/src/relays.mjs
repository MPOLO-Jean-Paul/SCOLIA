// Configuration des relais de connexion distante.
// Les serveurs scolaires n'activent jamais un relais uniquement parce que
// JP-Services a modifié sa liste : accord explicite et approbation de révision.
import { cleanLabel } from "./policy.mjs";
import { evaluateLicense } from "./licenses.mjs";

const ALLOWED = new Set(["active", "disabled"]);

export function safeRelayUrl(value) {
  if (typeof value !== "string" || value.length > 260) return null;
  try {
    const uri = new URL(value);
    if (uri.protocol !== "wss:" || uri.username || uri.password ||
        uri.search || uri.hash || uri.pathname !== "/" ||
        uri.hostname.length < 4 ||
        /^(localhost|127\.|10\.|192\.168\.|172\.(1[6-9]|2\d|3[01])\.)/i.test(uri.hostname))
      return null;
    return uri.toString();
  } catch { return null; }
}

async function schoolByToken(db, token, sha) {
  if (!token || token.length > 512) return null;
  return db.prepare(`SELECT s.* FROM school_tokens t
    JOIN schools s ON s.id=t.school_id
    WHERE t.token_hash=? AND t.revoked_at IS NULL`)
    .bind(await sha(token)).first();
}

// Retourne uniquement des URL autorisées ; aucun flux de données scolaires.
export async function schoolRelayRequest(request, env, url,
  { bearer, sha, jsonBody, fail, result, nowMs }) {
  const db = env.OWNER_DB;
  const school = await schoolByToken(db, bearer(request), sha);
  if (!school) return fail("Jeton de l'établissement requis.", 401);

  if (request.method === "POST" && url.pathname === "/api/v1/server/relay-consent") {
    const body = await jsonBody(request);
    if (typeof body?.enabled !== "boolean" || body?.disclosed_to_school_admin !== true)
      return fail("Confirmation explicite du responsable de l'établissement requise.", 400);
    const now = nowMs();
    await db.prepare(`INSERT INTO school_relay_preferences
      (school_id,allowed,approved_revision,changed_at)
      VALUES(?,?,NULL,?)
      ON CONFLICT(school_id) DO UPDATE SET
        allowed=excluded.allowed,approved_revision=NULL,changed_at=excluded.changed_at`)
      .bind(school.id, body.enabled ? 1 : 0, now).run();
    return result({ remote_relay_allowed: body.enabled, approval_needed: body.enabled,
      notice: "L'activation d'un fournisseur exige ensuite l'accord de l'établissement." });
  }

  if (request.method === "POST" && url.pathname === "/api/v1/server/relay-approve") {
    const body = await jsonBody(request);
    if (body?.confirm !== true || body?.disclosed_to_school_admin !== true ||
        !Number.isSafeInteger(body?.relay_revision))
      return fail("Approbation explicite du fournisseur et de la révision requise.", 400);
    const settings = await db.prepare("SELECT allowed FROM school_relay_preferences WHERE school_id=?")
      .bind(school.id).first();
    const active = await db.prepare(`SELECT revision,url FROM owner_relays
      WHERE state='active' ORDER BY updated_at DESC LIMIT 1`).first();
    if (!settings?.allowed || !active || active.revision !== body.relay_revision)
      return fail("Configuration de relais changée ou accès non autorisé.", 409);
    await db.prepare(`UPDATE school_relay_preferences
      SET approved_revision=?,changed_at=? WHERE school_id=?`)
      .bind(active.revision, nowMs(), school.id).run();
    return result({ accepted: true, revision: active.revision,
      notice: "La liaison distante de l'établissement peut utiliser ce relais." });
  }

  if (request.method === "GET" && url.pathname === "/api/v1/server/relay-settings") {
    const status = evaluateLicense(school).license_status;
    const pref = await db.prepare(`SELECT allowed,approved_revision
      FROM school_relay_preferences WHERE school_id=?`).bind(school.id).first();
    if (!pref?.allowed)
      return result({ license_status: status, remote_relay_allowed: Boolean(pref?.allowed),
        remote_relay_enabled: false,
        approval_required: true, relay: null });
    const active = await db.prepare(`SELECT id,label,url,revision
      FROM owner_relays WHERE state='active' ORDER BY updated_at DESC LIMIT 1`).first();
    if (!active) return result({ remote_relay_allowed: true,
      remote_relay_enabled: false, approval_required: true, relay: null });
    return result({
      license_status: status,
      remote_relay_allowed: true,
      remote_relay_enabled: pref.approved_revision === active.revision,
      approval_required: pref.approved_revision !== active.revision,
      relay: {
        id: active.id, label: active.label, endpoint_wss: active.url,
        revision: active.revision
      },
      update_mode: "school_admin_approval_required",
      notice: "L'application ne peut changer silencieusement le relais d'un établissement."
    });
  }
  return fail("Commande de relais scolaire inconnue.", 404);
}

export async function ownerRelayRequest(request, env, url,
  { jsonBody, fail, result, audit, nowMs }) {
  const db = env.OWNER_DB, now = nowMs();
  if (request.method === "GET" && url.pathname === "/api/v1/owner/relays") {
    const list = await db.prepare(`SELECT id,label,url,state,revision,created_at,updated_at
      FROM owner_relays ORDER BY updated_at DESC LIMIT 100`).all();
    return result({ relays: list.results });
  }

  if (request.method === "POST" && url.pathname === "/api/v1/owner/relays") {
    const data = await jsonBody(request);
    const label = cleanLabel(data?.label, 90);
    const endpoint = safeRelayUrl(data?.endpoint_wss);
    if (label.length < 3 || !endpoint) return fail("Nom et URL WSS publique valide requis.", 400);
    const id = crypto.randomUUID();
    const revision = now;
    // Un relais nouveau commence désactivé : jamais de routage implicite.
    await db.prepare(`INSERT INTO owner_relays
      (id,label,url,state,revision,created_at,updated_at)
      VALUES(?,?,?,'disabled',?,?,?)`)
      .bind(id,label,endpoint,revision,now,now).run();
    await audit(db,"relay_created",null,now);
    return result({ id,label,endpoint_wss:endpoint,state:"disabled",revision,
      notice:"Aucun établissement n'est reconnecté tant qu'il n'a pas approuvé ce relais." },201);
  }

  const match = /^\/api\/v1\/owner\/relays\/([a-f0-9-]{36})$/.exec(url.pathname);
  if (request.method === "PATCH" && match) {
    const data = await jsonBody(request), state = cleanLabel(data?.state, 20);
    if (!ALLOWED.has(state)) return fail("État inconnu (active ou disabled).", 400);
    const current = await db.prepare("SELECT id FROM owner_relays WHERE id=?")
      .bind(match[1]).first();
    if (!current) return fail("Relais inconnu.",404);
    const revision = now;
    // Invalider toutes les approbations de l'ancien relais en incrémentant sa révision.
    const rows = await db.batch([
      db.prepare("UPDATE owner_relays SET state='disabled',revision=?,updated_at=? WHERE state='active'")
        .bind(revision,now),
      db.prepare("UPDATE owner_relays SET state=?,revision=?,updated_at=? WHERE id=?")
        .bind(state,revision,now,match[1])
    ]);
    if (!rows[1].meta?.changes) return fail("Relais inconnu.",404);
    await audit(db,"relay_"+state,null,now);
    return result({ id:match[1],state,revision,
      notice:"Chaque serveur doit accepter explicitement cette nouvelle révision." });
  }
  return fail("Commande de relais propriétaire inconnue.",404);
}
