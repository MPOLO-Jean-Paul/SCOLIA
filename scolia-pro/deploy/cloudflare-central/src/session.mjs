// SCOLIA central preview : une session de 15 minutes pour un appareil déjà
// autorisé. La possession de sa clé P-256 remplace un mot de passe répété.
// Le registre D1 interdit le rejeu des signatures, même après redémarrage.
// PROTOTYPE : aucune API scolaire centrale n'utilise encore ce jeton.
const encoder = new TextEncoder();
const HEX_ID = /^[A-Za-z0-9._:-]{1,128}$/;
const B64_43 = /^[A-Za-z0-9_-]{43}$/;
const B64_86 = /^[A-Za-z0-9_-]{86}$/;

function response(data, status = 200) {
  return new Response(JSON.stringify(data), {
    status, headers: {
      "Content-Type": "application/json; charset=utf-8",
      "Cache-Control": "no-store",
      "X-Content-Type-Options": "nosniff",
    },
  });
}
const fail = (status, code) => response({ error: code }, status);
const b64 = bytes => btoa(String.fromCharCode(...bytes))
  .replace(/\+/g, "-").replace(/\//g, "_").replace(/=+$/g, "");

function from64(text) {
  if (typeof text !== "string" || !/^[A-Za-z0-9_-]+$/.test(text))
    throw Error("invalid_encoding");
  return Uint8Array.from(atob(text.replace(/-/g, "+").replace(/_/g, "/")),
    c => c.charCodeAt(0));
}

async function signSession(claims, encodedKey) {
  const raw = from64(encodedKey);
  if (raw.length < 32 || raw.length > 128) throw Error("invalid_signing_key");
  const key = await crypto.subtle.importKey("raw", raw,
    { name: "HMAC", hash: "SHA-256" }, false, ["sign"]);
  const head = b64(encoder.encode(JSON.stringify({ alg: "HS256", typ: "JWT" })));
  const body = b64(encoder.encode(JSON.stringify(claims)));
  const signature = await crypto.subtle.sign("HMAC", key,
    encoder.encode(head + "." + body));
  return head + "." + body + "." + b64(new Uint8Array(signature));
}

function publicKey(raw) {
  if (typeof raw !== "string" || raw.length > 512) return null;
  try {
    const key = JSON.parse(raw);
    if (!key || Array.isArray(key) ||
        Object.keys(key).sort().join(",") !== "crv,kty,x,y" ||
        key.crv !== "P-256" || key.kty !== "EC" ||
        !B64_43.test(key.x) || !B64_43.test(key.y)) return null;
    return key;
  } catch { return null; }
}

async function ownsDeviceKey(keyJwk, body) {
  try {
    const signature = from64(body.signature);
    if (signature.length !== 64) return false;
    const pk = await crypto.subtle.importKey("jwk", keyJwk,
      { name: "ECDSA", namedCurve: "P-256" }, false, ["verify"]);
    const challenge = encoder.encode(
      ["SCOLIA-SESSION-V1", body.school_id, body.device_id,
       body.nonce, String(body.issued_at)].join("\n"));
    return await crypto.subtle.verify(
      { name: "ECDSA", hash: "SHA-256" }, pk, signature, challenge);
  } catch { return false; }
}

export async function issueCentralSession(request, env) {
  if (!env?.DB || typeof env.SCOLIA_LOGIN_RATE_LIMITER?.limit !== "function" ||
      typeof env.SCOLIA_LOGIN_SOURCE_LIMITER?.limit !== "function" ||
      typeof env.SCOLIA_AUTH_HMAC_KEY !== "string" ||
      env.SCOLIA_AUTH_HMAC_KEY === env.SCOLIA_PAIRING_AUTH_KEY)
    return fail(503, "not_configured");
  const length = Number(request.headers.get("Content-Length") ?? "0");
  if (length > 1024) return fail(413, "too_large");

  let body;
  try {
    const raw = await request.text();
    if (encoder.encode(raw).length > 1024) return fail(413, "too_large");
    body = JSON.parse(raw);
  } catch { return fail(400, "invalid_json"); }
  if (!body || typeof body !== "object" || Array.isArray(body) ||
      Object.keys(body).sort().join(",") !==
      "device_id,issued_at,nonce,school_id,signature" ||
      !HEX_ID.test(body.school_id) || !HEX_ID.test(body.device_id) ||
      !B64_43.test(body.nonce) || !B64_86.test(body.signature) ||
      !Number.isSafeInteger(body.issued_at))
    return fail(400, "invalid_request");

  const now = Math.floor(Date.now() / 1000);
  if (Math.abs(now - body.issued_at) > 90)
    return fail(401, "expired_device_proof");

  try {
    // Blocage avant D1 et SHA : pas de vérification cryptographique illimitée.
    const sourceIp = request.headers.get("CF-Connecting-IP") || "unknown";
    const ipDigest = new Uint8Array(await crypto.subtle.digest(
      "SHA-256", encoder.encode(sourceIp)));
    const fromSource = await env.SCOLIA_LOGIN_SOURCE_LIMITER.limit({
      key: "sess:" + b64(ipDigest),
    });
    if (!fromSource.success) return fail(429, "too_many_attempts");
    const fromDevice = await env.SCOLIA_LOGIN_RATE_LIMITER.limit({
      key: "sess:" + body.school_id + ":" + body.device_id,
    });
    if (!fromDevice.success) return fail(429, "too_many_attempts");

    const device = await env.DB.prepare(
      "SELECT d.user_id, d.public_key_jwk, u.permission_revision " +
      "FROM cloud_devices d JOIN cloud_users u ON u.school_id = d.school_id " +
      "AND u.id = d.user_id JOIN cloud_schools s ON s.id = d.school_id " +
      "WHERE d.school_id = ? AND d.id = ? AND d.revoked_at IS NULL " +
      "AND d.public_key_jwk IS NOT NULL AND u.enabled = 1 AND s.status = 'active' LIMIT 1"
    ).bind(body.school_id, body.device_id).first();
    const jwk = publicKey(device?.public_key_jwk);
    if (!device || !jwk || !await ownsDeviceKey(jwk, body))
      return fail(401, "unauthorized");

    const nonceBytes = from64(body.nonce);
    if (nonceBytes.length !== 32) return fail(400, "invalid_request");
    const digest = new Uint8Array(await crypto.subtle.digest("SHA-256", nonceBytes));
    // Une seule requête D1 insert-or-ignore contre le rejeu, avec seconde
    // vérification des droits et de la révocation au moment de l'enregistrement.
    const inserted = await env.DB.prepare(
      "INSERT OR IGNORE INTO cloud_session_nonces " +
      "(school_id,device_id,nonce_sha256) SELECT ?, ?, ? " +
      "WHERE EXISTS (SELECT 1 FROM cloud_devices d " +
      "JOIN cloud_users u ON u.school_id = d.school_id AND u.id = d.user_id " +
      "JOIN cloud_schools s ON s.id = d.school_id " +
      "WHERE d.school_id = ? AND d.id = ? AND d.revoked_at IS NULL " +
      "AND d.public_key_jwk = ? AND u.enabled = 1 AND s.status = 'active' " +
      "AND u.permission_revision = ? AND u.id = ?)"
    ).bind(body.school_id, body.device_id, Array.from(digest,
      x => x.toString(16).padStart(2, "0")).join(""),
      body.school_id, body.device_id, device.public_key_jwk,
      device.permission_revision, device.user_id).run();
    if (inserted.meta?.changes !== 1)
      return fail(409, "device_proof_replayed_or_revoked");

    // Le jeton est un bearer court (15 min). L'API autorise uniquement les
    // métadonnées pour ce preview : aucune mutation métier n'est exposée.
    const jwt = await signSession({
      iss: "scolia-central", aud: "scolia-api",
      school_id: body.school_id, sub: device.user_id,
      device_id: body.device_id,
      permission_revision: device.permission_revision,
      iat: now, exp: now + 900,
    }, env.SCOLIA_AUTH_HMAC_KEY);
    return response({ access_token: jwt, token_type: "Bearer",
      expires_in: 900, school_id: body.school_id, device_id: body.device_id,
    }, 200);
  } catch (ex) {
    console.error("SCOLIA central session unavailable", String(ex?.name || "error"));
    return fail(503, "temporarily_unavailable");
  }
}
