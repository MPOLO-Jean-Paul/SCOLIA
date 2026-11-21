// Synchronisation métier v0 : sections / options scolaires uniquement.
// Hors périmètre : élèves, notes, paiements, matrículas, secrets et documents.
// Les droits et l'école sont TOUJOURS issus d'une session JWT revérifiée en D1.
const encoder = new TextEncoder();
const adminRoles = new Set(["secretariat", "administrateur", "direction"]);
const validId = /^[0-9a-fA-F-]{36}$/;
const validCode = /^[A-Z0-9_-]{2,16}$/;

function respond(value, status = 200) {
  return new Response(JSON.stringify(value), {
    status,
    headers: {
      "Content-Type": "application/json; charset=utf-8",
      "Cache-Control": "no-store",
      "X-Content-Type-Options": "nosniff",
    },
  });
}
const fail = (status, code) => respond({ error: code }, status);

function normalized(input) {
  if (!input || typeof input !== "object" || Array.isArray(input) ||
      Object.keys(input).sort().join(",") !==
      "code,expected_revision,is_active,name,operation_id,section_id" ||
      !validId.test(input.section_id) || !validId.test(input.operation_id) ||
      typeof input.code !== "string" ||
      !validCode.test(input.code) ||
      typeof input.name !== "string" ||
      input.name !== input.name.trim() ||
      input.name.length < 1 || input.name.length > 120 ||
      /[\u0000-\u001f\u007f]/.test(input.name) ||
      typeof input.is_active !== "boolean" ||
      !Number.isSafeInteger(input.expected_revision) ||
      input.expected_revision < 0)
    return null;
  return {
    section_id: input.section_id.toLowerCase(),
    code: input.code, name: input.name,
    is_active: input.is_active ? 1 : 0,
    expected_revision: input.expected_revision,
    operation_id: input.operation_id.toLowerCase(),
  };
}

async function payloadHash(body) {
  const canonical = JSON.stringify({
    section_id: body.section_id, code: body.code, name: body.name,
    is_active: body.is_active, expected_revision: body.expected_revision,
  });
  const digest = new Uint8Array(await crypto.subtle.digest(
    "SHA-256", encoder.encode(canonical)));
  return Array.from(digest, b => b.toString(16).padStart(2, "0")).join("");
}

async function readOp(db, user, operationId) {
  // Ne jamais révéler l'opération d'un autre établissement ou appareil.
  return db.prepare(
    "SELECT revision,request_sha256 FROM cloud_section_events " +
    "WHERE school_id=? AND operation_id=? AND actor_user_id=? " +
    "AND device_id=? LIMIT 1"
  ).bind(user.school_id, operationId, user.sub, user.device_id).first();
}

async function duplicate(db, user, body, hash) {
  const old = await readOp(db, user, body.operation_id);
  if (!old) return null;
  return old.request_sha256 === hash ?
    respond({ operation_id: body.operation_id, section_id: body.section_id,
      status: "applied", revision: old.revision, replayed: true }, 200) :
    fail(409, "operation_id_already_used");
}

export async function handleSchoolSections(request, env, user) {
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
      // Seules les sections de l'école du JWT revérifié sont visibles.
      const result = await env.DB.prepare(
        "SELECT revision,section_id,code,name,is_active,created_at " +
        "FROM cloud_section_events WHERE school_id=? AND revision>? " +
        "ORDER BY revision ASC LIMIT ?"
      ).bind(user.school_id, after, limit).all();
      const events = result.results ?? [];
      return respond({
        events, next: events.length ? events[events.length - 1].revision : after,
        has_more: events.length === limit,
      });
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
    body = normalized(JSON.parse(raw));
  } catch {
    return fail(400, "invalid_json");
  }
  if (!body) return fail(400, "invalid_section");
  const hash = await payloadHash(body);
  try {
    const old = await duplicate(env.DB, user, body, hash);
    if (old) return old;

    // Un seul INSERT sous verrou d'écriture SQLite. Le trigger D1 produit :
    // section actualisée + operation applied + journal de changement.
    // Une version obsolète, un code de section dupliqué ou un op-id réutilisé
    // annulent TOUT ; aucun paiement ou dossier n'est touché.
    const insert = await env.DB.prepare(
      "INSERT INTO cloud_section_events " +
      "(school_id,operation_id,section_id,actor_user_id,device_id,code,name," +
      "is_active,expected_revision,request_sha256) " +
      "VALUES (?,?,?,?,?,?,?,?,?,?)"
    ).bind(
      user.school_id, body.operation_id, body.section_id, user.sub,
      user.device_id, body.code, body.name, body.is_active,
      body.expected_revision, hash
    ).run();
    if (insert.meta?.changes !== 1)
      return fail(503, "temporarily_unavailable");
    const saved = await readOp(env.DB, user, body.operation_id);
    if (!saved || saved.request_sha256 !== hash)
      return fail(503, "temporarily_unavailable");
    return respond({
      operation_id: body.operation_id, section_id: body.section_id,
      status: "applied", revision: saved.revision, replayed: false,
    }, 201);
  } catch (exception) {
    // En cas d'interruption entre INSERT et lecture du résultat : réponse
    // idempotente après vérification exacte de l'empreinte, du compte et
    // de l'appareil. Un même ID avec un autre contenu est rejeté.
    try {
      const old = await duplicate(env.DB, user, body, hash);
      if (old) return old;
      if (String(exception?.message || "").includes("cloud_section_stale_revision"))
        return fail(409, "stale_section_revision");
      if (String(exception?.message || "").includes("UNIQUE constraint failed"))
        return fail(409, "duplicate_section_code_or_operation");
    } catch { /* fail closed */ }
    return fail(503, "temporarily_unavailable");
  }
}
