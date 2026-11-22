// SCOLIA Central preview : catalogue des annees scolaires (sans donnees d'eleves).
// Les droits, le poste autorise et l'ecole sont revérifiés par index.mjs.
const encoder = new TextEncoder();
const adminRoles = new Set(["secretariat", "administrateur", "direction"]);
const uuid = /^[0-9a-fA-F]{8}(?:-[0-9a-fA-F]{4}){3}-[0-9a-fA-F]{12}$/;
const validId = value => typeof value === "string" && uuid.test(value) &&
  value.toLowerCase() !== "00000000-0000-0000-0000-000000000000";

const validDate = value => {
  if (typeof value !== "string" || !/^\d{4}-\d{2}-\d{2}$/.test(value))
    return false;
  const [year, month, day] = value.split("-").map(Number);
  const actual = new Date(Date.UTC(year, month - 1, day));
  return actual.toISOString().slice(0, 10) === value;
};

function respond(data, status = 200) {
  return new Response(JSON.stringify(data), {
    status, headers: {
      "Content-Type": "application/json; charset=utf-8",
      "Cache-Control": "no-store",
      "X-Content-Type-Options": "nosniff",
    },
  });
}
const fail = (status, code) => respond({ error: code }, status);

function validate(body) {
  if (!body || typeof body !== "object" || Array.isArray(body) ||
      Object.keys(body).sort().join(",") !==
      "academic_year_id,end_date,expected_revision,label,operation_id,start_date,status" ||
      !validId(body.academic_year_id) || !validId(body.operation_id) ||
      typeof body.label !== "string" ||
      !/^\d{4}-\d{4}$/.test(body.label) ||
      !validDate(body.start_date) || !validDate(body.end_date) ||
      body.end_date < body.start_date ||
      !["preparation", "open", "closed"].includes(body.status) ||
      !Number.isSafeInteger(body.expected_revision) || body.expected_revision < 0)
    return null;
  const first = Number(body.label.slice(0, 4));
  const last = Number(body.label.slice(5));
  if (first < 2000 || first > 2199 || last !== first + 1 ||
      Number(body.start_date.slice(0, 4)) !== first ||
      ![first, last].includes(Number(body.end_date.slice(0, 4))))
    return null;
  return {
    academic_year_id: body.academic_year_id.toLowerCase(),
    operation_id: body.operation_id.toLowerCase(),
    label: body.label, start_date: body.start_date, end_date: body.end_date,
    status: body.status, expected_revision: body.expected_revision,
  };
}

async function hashBody(body) {
  const canonical = JSON.stringify({
    academic_year_id: body.academic_year_id, label: body.label,
    start_date: body.start_date, end_date: body.end_date,
    status: body.status, expected_revision: body.expected_revision,
  });
  const digest = new Uint8Array(await crypto.subtle.digest("SHA-256",
    encoder.encode(canonical)));
  return Array.from(digest, b => b.toString(16).padStart(2, "0")).join("");
}

async function readOld(db, user, id) {
  return db.prepare(
    "SELECT revision,request_sha256 FROM cloud_academic_year_events " +
    "WHERE school_id=? AND operation_id=? AND actor_user_id=? AND device_id=? LIMIT 1"
  ).bind(user.school_id, id, user.sub, user.device_id).first();
}

async function duplicate(db, user, body, hash) {
  const old = await readOld(db, user, body.operation_id);
  if (!old) return null;
  return old.request_sha256 === hash ? respond({
    academic_year_id: body.academic_year_id, operation_id: body.operation_id,
    status: "applied", revision: old.revision, replayed: true,
  }) : fail(409, "operation_id_already_used");
}

export async function handleAcademicYears(request, env, user) {
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
        "SELECT revision,academic_year_id,label,start_date,end_date,status,created_at " +
        "FROM cloud_academic_year_events WHERE school_id=? AND revision>? " +
        "ORDER BY revision ASC LIMIT ?"
      ).bind(user.school_id, after, limit).all();
      const events = result.results ?? [];
      return respond({
        events,
        next: events.length ? events[events.length - 1].revision : after,
        has_more: events.length === limit,
      });
    } catch { return fail(503, "temporarily_unavailable"); }
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
  } catch { return fail(400, "invalid_json"); }
  if (!body) return fail(400, "invalid_academic_year");
  const hash = await hashBody(body);
  try {
    const old = await duplicate(env.DB, user, body, hash);
    if (old) return old;
    const inserted = await env.DB.prepare(
      "INSERT INTO cloud_academic_year_events " +
      "(school_id,operation_id,academic_year_id,actor_user_id,device_id," +
      "label,start_date,end_date,status,expected_revision,request_sha256) " +
      "VALUES (?,?,?,?,?,?,?,?,?,?,?)"
    ).bind(user.school_id, body.operation_id, body.academic_year_id,
      user.sub, user.device_id, body.label, body.start_date, body.end_date,
      body.status, body.expected_revision, hash).run();
    if (inserted.meta?.changes !== 1) return fail(503, "temporarily_unavailable");
    const saved = await readOld(env.DB, user, body.operation_id);
    if (!saved || saved.request_sha256 !== hash)
      return fail(503, "temporarily_unavailable");
    return respond({
      academic_year_id: body.academic_year_id, operation_id: body.operation_id,
      status: "applied", revision: saved.revision, replayed: false,
    }, 201);
  } catch (ex) {
    try {
      const old = await duplicate(env.DB, user, body, hash);
      if (old) return old;
      const reason = String(ex?.message || "");
      if (reason.includes("cloud_year_stale_revision"))
        return fail(409, "stale_academic_year_revision");
      if (reason.includes("cloud_year_closed_immutable"))
        return fail(409, "academic_year_closed");
      if (reason.includes("cloud_year_identity_immutable"))
        return fail(409, "academic_year_identity_immutable");
      if (reason.includes("UNIQUE constraint failed"))
        return fail(409, "duplicate_academic_year_or_operation");
    } catch { /* fermeture en cas d'indisponibilite */ }
    return fail(503, "temporarily_unavailable");
  }
}
