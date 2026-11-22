// Prototype de catalogue central SCOLIA : promotions, sans donnees nominatives.
// Le role et l'etablissement proviennent uniquement d'une session verifiee en D1.
const encoder = new TextEncoder();
const adminRoles = new Set(["secretariat", "administrateur", "direction"]);
const uuid = /^[0-9a-fA-F]{8}(?:-[0-9a-fA-F]{4}){3}-[0-9a-fA-F]{12}$/;
const validId = value => typeof value === "string" && uuid.test(value) &&
  value.toLowerCase() !== "00000000-0000-0000-0000-000000000000";
const validCode = /^[A-Z0-9_-]{2,16}$/;

function response(data, status = 200) {
  return new Response(JSON.stringify(data), { status, headers: {
    "Content-Type": "application/json; charset=utf-8",
    "Cache-Control": "no-store",
    "X-Content-Type-Options": "nosniff",
  } });
}
const fail = (status, code) => response({ error: code }, status);

function validate(body) {
  if (!body || typeof body !== "object" || Array.isArray(body) ||
      Object.keys(body).sort().join(",") !==
      "code,expected_revision,is_active,name,operation_id,promotion_id,section_id" ||
      !validId(body.promotion_id) || !validId(body.section_id) ||
      !validId(body.operation_id) ||
      typeof body.code !== "string" || !validCode.test(body.code) ||
      typeof body.name !== "string" || body.name !== body.name.trim() ||
      body.name.length < 1 || body.name.length > 120 ||
      /[\u0000-\u001f\u007f]/.test(body.name) ||
      typeof body.is_active !== "boolean" ||
      !Number.isSafeInteger(body.expected_revision) || body.expected_revision < 0)
    return null;
  return {
    promotion_id: body.promotion_id.toLowerCase(),
    section_id: body.section_id.toLowerCase(),
    operation_id: body.operation_id.toLowerCase(),
    code: body.code,
    name: body.name,
    is_active: body.is_active ? 1 : 0,
    expected_revision: body.expected_revision,
  };
}

async function digest(body) {
  const canonical = JSON.stringify({
    promotion_id: body.promotion_id, section_id: body.section_id,
    code: body.code, name: body.name, is_active: body.is_active,
    expected_revision: body.expected_revision,
  });
  return Array.from(new Uint8Array(await crypto.subtle.digest(
    "SHA-256", encoder.encode(canonical))),
  b => b.toString(16).padStart(2, "0")).join("");
}

async function readOperation(db, user, operationId) {
  return db.prepare(
    "SELECT revision,request_sha256 FROM cloud_promotion_events " +
    "WHERE school_id=? AND operation_id=? AND actor_user_id=? AND device_id=? LIMIT 1"
  ).bind(user.school_id, operationId, user.sub, user.device_id).first();
}

async function findDuplicate(db, user, body, hash) {
  const existing = await readOperation(db, user, body.operation_id);
  if (!existing) return null;
  return existing.request_sha256 === hash ?
    response({ operation_id: body.operation_id, promotion_id: body.promotion_id,
      status: "applied", revision: existing.revision, replayed: true }) :
    fail(409, "operation_id_already_used");
}

export async function handleSchoolPromotions(request, env, user) {
  if (!env?.DB || !user?.school_id || !user?.sub || !user?.device_id)
    return fail(503, "not_configured");
  const url = new URL(request.url);
  if (request.method === "GET") {
    const afterRaw = url.searchParams.get("after") ?? "0";
    const limitRaw = url.searchParams.get("limit") ?? "50";
    if (!/^(0|[1-9]\d*)$/.test(afterRaw) || !/^[1-9]\d*$/.test(limitRaw))
      return fail(400, "invalid_cursor");
    const after = Number(afterRaw), limit = Number(limitRaw);
    if (!Number.isSafeInteger(after) || !Number.isSafeInteger(limit) ||
        limit < 1 || limit > 100) return fail(400, "invalid_cursor");
    try {
      const result = await env.DB.prepare(
        "SELECT revision,promotion_id,section_id,code,name,is_active,created_at " +
        "FROM cloud_promotion_events WHERE school_id=? AND revision>? " +
        "ORDER BY revision ASC LIMIT ?"
      ).bind(user.school_id, after, limit).all();
      const events = result.results ?? [];
      return response({ events, next: events.length ?
        events[events.length - 1].revision : after, has_more: events.length === limit });
    } catch {
      return fail(503, "temporarily_unavailable");
    }
  }
  if (request.method !== "POST") return fail(405, "method_not_allowed");
  if (!adminRoles.has(user.role)) return fail(403, "forbidden");
  if ((Number(request.headers.get("Content-Length")) || 0) > 2048)
    return fail(413, "too_large");

  let body;
  try {
    const raw = await request.text();
    if (encoder.encode(raw).length > 2048) return fail(413, "too_large");
    body = validate(JSON.parse(raw));
  } catch {
    return fail(400, "invalid_json");
  }
  if (!body) return fail(400, "invalid_promotion");

  const hash = await digest(body);
  try {
    const duplicate = await findDuplicate(env.DB, user, body, hash);
    if (duplicate) return duplicate;
    // Un seul INSERT D1 declenche atomiquement l'etat courant, le resultat
    // de l'operation et le journal. La version attendue protege les conflits.
    const result = await env.DB.prepare(
      "INSERT INTO cloud_promotion_events " +
      "(school_id,operation_id,promotion_id,section_id,actor_user_id,device_id," +
      "code,name,is_active,expected_revision,request_sha256) " +
      "VALUES (?,?,?,?,?,?,?,?,?,?,?)"
    ).bind(user.school_id, body.operation_id, body.promotion_id,
      body.section_id, user.sub, user.device_id, body.code,
      body.name, body.is_active, body.expected_revision, hash).run();
    if (result.meta?.changes !== 1)
      return fail(503, "temporarily_unavailable");
    const saved = await readOperation(env.DB, user, body.operation_id);
    if (!saved || saved.request_sha256 !== hash)
      return fail(503, "temporarily_unavailable");
    return response({ operation_id: body.operation_id,
      promotion_id: body.promotion_id, status: "applied",
      revision: saved.revision, replayed: false }, 201);
  } catch (exception) {
    try {
      const duplicate = await findDuplicate(env.DB, user, body, hash);
      if (duplicate) return duplicate;
      const reason = String(exception?.message || "");
      if (reason.includes("cloud_promotion_stale_revision"))
        return fail(409, "stale_promotion_revision");
      if (reason.includes("cloud_promotion_invalid_section") ||
          reason.includes("FOREIGN KEY constraint failed"))
        return fail(409, "inactive_or_unknown_section");
      if (reason.includes("UNIQUE constraint failed"))
        return fail(409, "duplicate_promotion_code_or_operation");
    } catch { /* refusal on storage failure */ }
    return fail(503, "temporarily_unavailable");
  }
}
