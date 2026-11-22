// Prévisualisation du service central SCOLIA, non déployée.
import { acceptPairing } from "./pairing.mjs";
import { issuePairingIdentityProof } from "./identity.mjs";
import { issueCentralSession } from "./session.mjs";
import { handleSchoolSections } from "./sections.mjs";
import { handleSchoolPromotions } from "./promotions.mjs";
import { handleAcademicYears } from "./years.mjs";
// Seuls les catalogues des annees, sections et promotions admettent des mutations versionnees.
// Aucun dossier nominatif, paiement, note ou recu n'est modifiable ici.
const textEncoder = new TextEncoder();
const privilegedRoles = new Set(["secretariat", "administrateur", "direction"]);
const MAX_EVENTS = 100;

function json(data, status = 200) {
  return new Response(JSON.stringify(data), {
    status,
    headers: {
      "Content-Type": "application/json; charset=utf-8",
      "Cache-Control": "no-store",
      "X-Content-Type-Options": "nosniff",
    },
  });
}
const error = (status, code) => json({ error: code }, status);
const integer = (x) => Number.isSafeInteger(x) && x >= 0;

function fromBase64Url(input) {
  if (typeof input !== "string" || !/^[A-Za-z0-9_-]+$/.test(input) || input.length > 8192)
    throw new Error("bad encoding");
  const raw = atob(input.replace(/-/g, "+").replace(/_/g, "/"));
  return Uint8Array.from(raw, c => c.charCodeAt(0));
}

function parsePart(input) {
  return JSON.parse(new TextDecoder("utf-8", { fatal: true }).decode(fromBase64Url(input)));
}

// Jeton court émis uniquement par le futur service d'identité de confiance.
// Aucun endpoint public ne peut créer de jetons ni autoriser un appareil.
export async function verifyToken(authorization, signingKey, now = Math.floor(Date.now() / 1000)) {
  if (typeof authorization !== "string" || !authorization.startsWith("Bearer ") ||
      typeof signingKey !== "string" || !signingKey) return null;
  try {
    const parts = authorization.slice(7).split(".");
    if (parts.length !== 3 || parts.some(p => !p)) return null;
    const [h, p, s] = parts;
    const header = parsePart(h);
    if (!header || header.alg !== "HS256" || header.typ !== "JWT" ||
        Object.keys(header).some(k => !["alg", "typ"].includes(k))) return null;
    const keyBytes = fromBase64Url(signingKey);
    if (keyBytes.length < 32) return null;
    const key = await crypto.subtle.importKey("raw", keyBytes,
      { name: "HMAC", hash: "SHA-256" }, false, ["verify"]);
    if (!await crypto.subtle.verify("HMAC", key, fromBase64Url(s), textEncoder.encode(h + "." + p)))
      return null;

    const claims = parsePart(p);
    if (!claims || claims.aud !== "scolia-api" || claims.iss !== "scolia-central" ||
        typeof claims.school_id !== "string" || !claims.school_id ||
        typeof claims.sub !== "string" || !claims.sub ||
        typeof claims.device_id !== "string" || !claims.device_id ||
        !integer(claims.permission_revision) || claims.permission_revision < 1 ||
        !integer(claims.iat) || !integer(claims.exp) ||
        claims.iat > now + 30 || claims.exp <= now ||
        claims.exp <= claims.iat || claims.exp - claims.iat > 900 ||
        (claims.nbf !== undefined && (!integer(claims.nbf) || claims.nbf > now)))
      return null;
    return claims;
  } catch {
    return null;
  }
}

async function authorize(request, env) {
  const claims = await verifyToken(request.headers.get("Authorization"), env.SCOLIA_AUTH_HMAC_KEY);
  if (!claims) return null;
  const account = await env.DB.prepare(
    "SELECT u.role AS role FROM cloud_users AS u " +
    "JOIN cloud_schools AS s ON s.id = u.school_id " +
    "JOIN cloud_devices AS d ON d.school_id = u.school_id AND d.user_id = u.id " +
    "WHERE u.school_id = ? AND u.id = ? AND d.id = ? AND s.status = 'active' " +
    "AND u.enabled = 1 AND d.revoked_at IS NULL AND u.permission_revision = ? LIMIT 1"
  ).bind(claims.school_id, claims.sub, claims.device_id, claims.permission_revision).first();
  return account ? { ...claims, role: String(account.role || "").toLowerCase() } : null;
}

export async function handleRequest(request, env) {
  const url = new URL(request.url);
  if (request.method === "GET" && url.pathname === "/health")
    return json({ service: "SCOLIA central preview", status: "ready" });
  const isEvents = url.pathname === "/v1/sync/events" && request.method === "GET";
  const isSectionCatalog = url.pathname === "/v1/catalog/sections" &&
    (request.method === "GET" || request.method === "POST");
  const isPromotionCatalog = url.pathname === "/v1/catalog/promotions" &&
    (request.method === "GET" || request.method === "POST");
  const isAcademicYearCatalog = url.pathname === "/v1/catalog/years" &&
    (request.method === "GET" || request.method === "POST");
  const isAck = url.pathname === "/v1/sync/ack" && request.method === "POST";
  // Une écriture 202 demeure provisoire. Le client peut vérifier sa validation
  // définitive, sans exposer ni les montants ni les données de l'établissement.
  const isOperationStatus = url.pathname.startsWith("/v1/sync/operations/") &&
    request.method === "GET";
  const isDeviceList = url.pathname === "/v1/admin/devices" && request.method === "GET";
  const isDeviceRevoke = url.pathname === "/v1/admin/devices/revoke" && request.method === "POST";
  const isDeviceInvite = url.pathname === "/v1/admin/devices/invite" && request.method === "POST";
  const isPairingAcceptance = url.pathname === "/v1/pair/accept" && request.method === "POST";
  const isPairingPasswordCheck = url.pathname === "/v1/identity/pairing-proof" &&
    request.method === "POST";
  const isCentralSession = url.pathname === "/v1/identity/session" &&
    request.method === "POST";
  // Session 15 minutes, uniquement si l'appareil existant prouve sa clé
  // non exportée par l'application et si le nonce est inédit en D1.
  if (isCentralSession) return issueCentralSession(request, env);
  // Vérification du mot de passe d'un compte PRÉEXISTANT. Cette route
  // n'émet pas de session : seulement une preuve de 2 minutes pour le QR.
  if (isPairingPasswordCheck) return issuePairingIdentityProof(request, env);
  // Le nouvel appareil n'a pas encore de session. Son contrôle se fait avec
  // une preuve de connexion au compte CIBLE (autre clé), puis une signature
  // de sa clé privée et une invitation du secrétariat non réutilisée.
  if (isPairingAcceptance) return acceptPairing(request, env);
  if (!isEvents && !isAck && !isDeviceList && !isDeviceRevoke &&
      !isDeviceInvite && !isOperationStatus && !isSectionCatalog &&
      !isPromotionCatalog && !isAcademicYearCatalog)
    return error(404, "not_found");
  if (!env?.DB || !env?.SCOLIA_AUTH_HMAC_KEY) return error(503, "not_configured");
  try {
    const user = await authorize(request, env);
    if (!user) return error(401, "unauthorized");
    if (isSectionCatalog)
      return handleSchoolSections(request, env, user);
    if (isPromotionCatalog)
      return handleSchoolPromotions(request, env, user);
    if (isAcademicYearCatalog)
      return handleAcademicYears(request, env, user);
    if (isOperationStatus) {
      const operationId = url.pathname.slice("/v1/sync/operations/".length);
      if (!/^[A-Za-z0-9_-]{1,128}$/.test(operationId))
        return error(400, "invalid_operation_id");
      // Lecture du seul accusé de l'opération déposée PAR CE compte et
      // DEPUIS CET appareil. Même un administrateur ne reçoit pas le détail
      // d'une écriture faite par un autre utilisateur via ce point d'accès.
      const row = await env.DB.prepare(
        "SELECT outcome, received_at, settled_at FROM cloud_operations " +
        "WHERE school_id = ? AND operation_id = ? AND actor_user_id = ? " +
        "AND device_id = ? LIMIT 1"
      ).bind(user.school_id, operationId, user.sub, user.device_id).first();
      if (!row) return error(404, "not_found");
      if (!["pending", "applied", "rejected"].includes(row.outcome))
        return error(503, "unexpected_status");
      return json({
        operation_id: operationId,
        status: row.outcome,
        finalized: row.outcome !== "pending",
        received_at: row.received_at,
        settled_at: row.settled_at,
      });
    }
    // Le journal ne dispose pas encore de filtres fins par cours / apprenant :
    // seuls les rôles avec accès global peuvent consulter les métadonnées.
    if (!privilegedRoles.has(user.role)) return error(403, "forbidden");

    if (isDeviceInvite) {
      // L'invitation n'est qu'une autorisation temporaire de DÉMARRER le
      // jumelage. Elle n'accorde aucun accès tant qu'une authentification
      // indépendante du compte cible n'a pas été vérifiée.
      if ((Number(request.headers.get("Content-Length")) || 0) > 512)
        return error(413, "too_large");
      let body;
      try {
        const raw = await request.text();
        if (textEncoder.encode(raw).length > 512) return error(413, "too_large");
        body = JSON.parse(raw);
      } catch {
        return error(400, "invalid_json");
      }
      if (!body || Array.isArray(body) || typeof body !== "object" ||
          Object.keys(body).length !== 1 ||
          typeof body.target_user_id !== "string" ||
          !/^[A-Za-z0-9._:-]{1,128}$/.test(body.target_user_id))
        return error(400, "invalid_target_user");

      const target = await env.DB.prepare(
        "SELECT id FROM cloud_users WHERE school_id = ? AND id = ? " +
        "AND enabled = 1 LIMIT 1"
      ).bind(user.school_id, body.target_user_id).first();
      if (!target) return error(404, "not_found");

      // 256 bits aléatoires ; D1 conserve seulement l'empreinte du secret.
      const bytes = crypto.getRandomValues(new Uint8Array(32));
      const secret = btoa(String.fromCharCode(...bytes))
        .replace(/\+/g, "-").replace(/\//g, "_").replace(/=+$/g, "");
      const hashBytes = await crypto.subtle.digest("SHA-256", bytes);
      const digest = Array.from(new Uint8Array(hashBytes),
        b => b.toString(16).padStart(2, "0")).join("");
      const invitationId = crypto.randomUUID();
      const expiresAt = Math.floor(Date.now() / 1000) + 600;
      // Limite de trois invitations actives par compte. Une unique instruction
      // INSERT ... SELECT garde le contrôle et l'écriture atomiques dans D1.
      const inserted = await env.DB.prepare(
        "INSERT INTO cloud_device_invitations " +
        "(id, school_id, target_user_id, issued_by, token_sha256, expires_at) " +
        "SELECT ?, ?, ?, ?, ?, ? WHERE " +
        "(SELECT COUNT(*) FROM cloud_device_invitations " +
        "WHERE school_id = ? AND target_user_id = ? " +
        "AND consumed_at IS NULL AND expires_at > unixepoch()) < 3"
      ).bind(invitationId, user.school_id, body.target_user_id,
        user.sub, digest, expiresAt, user.school_id, body.target_user_id).run();
      if (inserted.meta?.changes === 0)
        return error(429, "too_many_active_invitations");
      if (inserted.meta?.changes !== 1)
        return error(503, "temporarily_unavailable");
      return json({
        invitation_id: invitationId,
        token: secret,
        target_user_id: body.target_user_id,
        expires_at: expiresAt,
        activation: "requires_authenticated_claim",
      }, 201);
    }

    if (isDeviceList) {
      // Pagination stable par ID, même quand l'établissement dépasse 100 postes.
      // Le school_id est TOUJOURS celui du jeton, jamais un paramètre utilisateur.
      const after = url.searchParams.get("after") ?? "";
      const rawLimit = url.searchParams.get("limit") ?? "50";
      if ((after && !/^[A-Za-z0-9._:-]{1,128}$/.test(after)) ||
          !/^[1-9]\d*$/.test(rawLimit))
        return error(400, "invalid_cursor");
      const limit = Number(rawLimit);
      if (!Number.isSafeInteger(limit) || limit < 1 || limit > MAX_EVENTS)
        return error(400, "invalid_cursor");
      // Ne retourner aucun secret d'appareil, mot de passe ou jeton.
      const result = await env.DB.prepare(
        "SELECT id AS device_id, user_id, approved_by, approved_at, revoked_at, revoked_by " +
        "FROM cloud_devices WHERE school_id = ? AND id > ? ORDER BY id ASC LIMIT ?"
      ).bind(user.school_id, after, limit).all();
      const devices = result.results ?? [];
      return json({
        devices,
        next: devices.length ? devices[devices.length - 1].device_id : after,
        has_more: devices.length === limit,
      });
    }

    if (isDeviceRevoke) {
      if ((Number(request.headers.get("Content-Length")) || 0) > 512)
        return error(413, "too_large");
      let body;
      try {
        const raw = await request.text();
        if (textEncoder.encode(raw).length > 512) return error(413, "too_large");
        body = JSON.parse(raw);
      } catch {
        return error(400, "invalid_json");
      }
      if (!body || Array.isArray(body) || typeof body !== "object" ||
          Object.keys(body).length !== 1 ||
          typeof body.device_id !== "string" ||
          !/^[A-Za-z0-9._:-]{1,128}$/.test(body.device_id))
        return error(400, "invalid_device_id");
      if (body.device_id === user.device_id)
        return error(409, "cannot_revoke_current_device");

      // Enregistrer auteur + révocation dans une seule écriture atomique.
      // L'identité de l'école est toujours dérivée du jeton validé.
      const updated = await env.DB.prepare(
        "UPDATE cloud_devices SET revoked_at = unixepoch(), revoked_by = ? " +
        "WHERE school_id = ? AND id = ? AND revoked_at IS NULL"
      ).bind(user.sub, user.school_id, body.device_id).run();
      if (updated.meta?.changes === 1)
        return json({ device_id: body.device_id, revoked: true, already_revoked: false });
      if (updated.meta?.changes !== 0)
        return error(503, "temporarily_unavailable");
      const existing = await env.DB.prepare(
        "SELECT revoked_at FROM cloud_devices WHERE school_id = ? AND id = ? LIMIT 1"
      ).bind(user.school_id, body.device_id).first();
      if (!existing) return error(404, "not_found");
      if (existing.revoked_at != null)
        return json({ device_id: body.device_id, revoked: true, already_revoked: true });
      return error(503, "temporarily_unavailable");
    }

    if (isEvents) {
      const rawAfter = url.searchParams.get("after") ?? "0";
      const rawLimit = url.searchParams.get("limit") ?? "50";
      if (!/^(0|[1-9]\d*)$/.test(rawAfter) || !/^[1-9]\d*$/.test(rawLimit))
        return error(400, "invalid_cursor");
      const after = Number(rawAfter), limit = Number(rawLimit);
      if (!integer(after) || !Number.isSafeInteger(limit) || limit < 1 || limit > MAX_EVENTS)
        return error(400, "invalid_cursor");
      const result = await env.DB.prepare(
        "SELECT revision, entity_kind, entity_id, change_kind, payload_sha256, created_at " +
        "FROM cloud_change_log WHERE school_id = ? AND revision > ? " +
        "ORDER BY revision ASC LIMIT ?"
      ).bind(user.school_id, after, limit).all();
      const events = result.results ?? [];
      return json({
        events,
        next: events.length ? events[events.length - 1].revision : after,
        has_more: events.length === limit,
        includes_payload: false,
      });
    }

    if ((Number(request.headers.get("Content-Length")) || 0) > 256)
      return error(413, "too_large");
    let body;
    try {
      const input = await request.text();
      if (textEncoder.encode(input).length > 256) return error(413, "too_large");
      body = JSON.parse(input);
    } catch {
      return error(400, "invalid_json");
    }
    if (!body || !integer(body.revision)) return error(400, "invalid_revision");
    const last = await env.DB.prepare(
      "SELECT COALESCE(MAX(revision), 0) AS latest FROM cloud_change_log WHERE school_id = ?"
    ).bind(user.school_id).first();
    if (body.revision > Number(last?.latest ?? 0))
      return error(400, "invalid_revision");
    // ACK seulement après application locale atomique. Ne retire aucun événement.
    await env.DB.prepare(
      "INSERT INTO cloud_sync_cursors (school_id, device_id, last_applied_revision, updated_at) " +
      "VALUES (?, ?, ?, unixepoch()) ON CONFLICT(school_id, device_id) " +
      "DO UPDATE SET last_applied_revision = " +
      "MAX(cloud_sync_cursors.last_applied_revision, excluded.last_applied_revision), " +
      "updated_at = unixepoch()"
    ).bind(user.school_id, user.device_id, body.revision).run();
    return json({ acknowledged: true });
  } catch (cause) {
    console.error("SCOLIA preview request failed", String(cause?.name || "error"));
    return error(503, "temporarily_unavailable");
  }
}

export default { fetch(request, env) { return handleRequest(request, env); } };
