// Prévisualisation du service central SCOLIA, non déployée.
// Aucune route d'émission des jetons ni de mutation scolaire n'est exposée.
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
  const isAck = url.pathname === "/v1/sync/ack" && request.method === "POST";
  if (!isEvents && !isAck) return error(404, "not_found");
  if (!env?.DB || !env?.SCOLIA_AUTH_HMAC_KEY) return error(503, "not_configured");
  try {
    const user = await authorize(request, env);
    if (!user) return error(401, "unauthorized");
    // Le journal ne dispose pas encore de filtres fins par cours / apprenant :
    // seuls les rôles avec accès global peuvent consulter les métadonnées.
    if (!privilegedRoles.has(user.role)) return error(403, "forbidden");
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
