// SCOLIA Pro 1.2.86 : service d'identité de PRÉ-jumelage, non déployé.
// Reconnaît les empreintes ASP.NET Core Identity V3 PBKDF2-HMAC-SHA512
// (format historique utilisé par Scolia.Infrastructure.PasswordService).
// Ne délivre jamais un JWT de session ni un accès aux données scolaires.
const encode = new TextEncoder();
const VALID_DEVICE = /^[A-Za-z0-9._:-]{1,128}$/;
const VALID_INVITE = /^[a-fA-F0-9-]{36}$/;
const VALID_THUMBPRINT = /^[A-Za-z0-9_-]{43}$/;
const VALID_USERNAME = /^[a-zA-Z0-9._-]{3,40}$/;
const dummySalt = new Uint8Array([
  23, 51, 45, 12, 76, 32, 18, 94, 8, 53, 77, 11, 18, 3, 44, 7,
]);
const MAX_REQUEST = 1024;
const MAX_PASSWORD_BYTES = 512;
const PBKDF2_MIN_ITERATIONS = 100_000;
// workerd limite actuellement PBKDF2 à 100 000 itérations. La version
// .NET 1.2.86 utilisée par SCOLIA emploie précisément cette valeur.
// Les empreintes de versions ultérieures devront passer par une migration
// d'identité approuvée si leur coût de dérivation augmente.
const PBKDF2_MAX_ITERATIONS = 100_000;

const response = (data, status = 200) => new Response(JSON.stringify(data), {
  status,
  headers: {
    "Content-Type": "application/json; charset=utf-8",
    "Cache-Control": "no-store",
    "X-Content-Type-Options": "nosniff",
  },
});
const fail = (status, error) => response({ error }, status);
const b64url = arr => btoa(String.fromCharCode(...arr))
  .replace(/\+/g, "-").replace(/\//g, "_").replace(/=+$/g, "");

function decodeBase64(value) {
  if (typeof value !== "string" ||
      !/^[A-Za-z0-9+/]+={0,2}$/.test(value) || value.length > 512 ||
      value.length % 4 !== 0) return null;
  try {
    const bytes = Uint8Array.from(atob(value), ch => ch.charCodeAt(0));
    if (btoa(String.fromCharCode(...bytes)) !== value) return null;
    return bytes;
  } catch {
    return null;
  }
}

function u32be(bytes, offset) {
  return (bytes[offset] * 0x1000000 + (bytes[offset + 1] << 16) +
    (bytes[offset + 2] << 8) + bytes[offset + 3]) >>> 0;
}

async function pbkdf2(password, salt, iterations, length) {
  const key = await crypto.subtle.importKey("raw", encode.encode(password),
    "PBKDF2", false, ["deriveBits"]);
  return new Uint8Array(await crypto.subtle.deriveBits({
    name: "PBKDF2", hash: "SHA-512", salt, iterations,
  }, key, length * 8));
}

// Le format Identity V3 :
// 0x01 | PRF(uint32 big endian=2 pour SHA512) | iterations(uint32 BE)
// | saltLength(uint32 BE) | salt | 32 octets de sous-clé.
// On refuse les anciens formats moins sûrs et les paramètres hostiles. Les
// comptes anciens doivent changer le mot de passe via le circuit approuvé.
export async function verifyAspNetIdentityV3(password, storedHash) {
  if (typeof password !== "string" || encode.encode(password).length > MAX_PASSWORD_BYTES)
    return false;
  const bytes = decodeBase64(storedHash);
  if (!bytes || bytes.length < 1 + 12 + 16 + 32 || bytes[0] !== 1) return false;
  if (u32be(bytes, 1) !== 2) return false; // HMAC-SHA512
  const iterations = u32be(bytes, 5);
  const saltLength = u32be(bytes, 9);
  if (iterations < PBKDF2_MIN_ITERATIONS || iterations > PBKDF2_MAX_ITERATIONS ||
      saltLength < 16 || saltLength > 64 ||
      bytes.length !== 13 + saltLength + 32) return false;

  const expected = bytes.subarray(13 + saltLength);
  const derived = await pbkdf2(password, bytes.subarray(13, 13 + saltLength),
    iterations, expected.length);
  // Comparaison sur 32 octets, sans sortie anticipée.
  let differences = 0;
  for (let i = 0; i < expected.length; i++)
    differences |= derived[i] ^ expected[i];
  return differences === 0;
}

async function obscureUnknownPassword(password) {
  // Le temps de réponse pour un QR introuvable reste du même ordre que
  // celui d'une mauvaise vérification PBKDF2 pour un vrai compte.
  await pbkdf2(password, dummySalt, PBKDF2_MIN_ITERATIONS, 32);
}

async function signPairingClaims(claims, base64urlKey) {
  if (typeof base64urlKey !== "string" ||
      !/^[A-Za-z0-9_-]{43,256}$/.test(base64urlKey)) throw Error("bad_signing_key");
  let bytes;
  try {
    bytes = Uint8Array.from(atob(base64urlKey.replace(/-/g, "+").replace(/_/g, "/")),
      ch => ch.charCodeAt(0));
  } catch { throw Error("bad_signing_key"); }
  if (bytes.length < 32) throw Error("bad_signing_key");
  const key = await crypto.subtle.importKey("raw", bytes,
    { name: "HMAC", hash: "SHA-256" }, false, ["sign"]);
  const header = b64url(encode.encode(JSON.stringify({ alg: "HS256", typ: "JWT" })));
  const payload = b64url(encode.encode(JSON.stringify(claims)));
  const unsigned = header + "." + payload;
  const signature = await crypto.subtle.sign("HMAC", key, encode.encode(unsigned));
  return unsigned + "." + b64url(new Uint8Array(signature));
}

// Le compteur est réservé AVANT tout travail PBKDF2 : 20 requêtes simultanées
// avec un même compte ne peuvent pas vérifier 20 mots de passe gratuitement.
// Un succès remet le compteur à zéro ; un cinquième échec bloque 2 minutes.
async function reserveAttempt(db, account) {
  const reservation = await db.prepare(
    "INSERT INTO cloud_auth_attempts " +
    "(school_id,user_id,failed_count,lockout_until,updated_at) " +
    "VALUES (?,?,1,0,unixepoch()) " +
    "ON CONFLICT(school_id,user_id) DO UPDATE SET " +
    "failed_count = CASE WHEN cloud_auth_attempts.lockout_until > 0 " +
    "AND cloud_auth_attempts.lockout_until <= unixepoch() THEN 1 " +
    "ELSE cloud_auth_attempts.failed_count + 1 END, " +
    "lockout_until = 0, updated_at = unixepoch() " +
    "WHERE (cloud_auth_attempts.lockout_until = 0 " +
    "AND cloud_auth_attempts.failed_count < 5) " +
    "OR (cloud_auth_attempts.lockout_until > 0 " +
    "AND cloud_auth_attempts.lockout_until <= unixepoch())"
  ).bind(account.school_id, account.id).run();
  if (reservation.meta?.changes === 1) return true;

  // Si le Worker s'est interrompu après la cinquième réservation mais
  // avant failedAttempt(), failed_count vaut déjà 5 avec lockout_until=0.
  // Réparer ce verrou incomplet avant de refuser la nouvelle tentative :
  // sans cela le compte resterait bloqué indéfiniment.
  await failedAttempt(db, account);
  return false;
}

async function failedAttempt(db, account) {
  await db.prepare(
    "UPDATE cloud_auth_attempts SET lockout_until = unixepoch() + 120, " +
    "updated_at = unixepoch() WHERE school_id = ? AND user_id = ? " +
    "AND failed_count >= 5 AND lockout_until = 0"
  ).bind(account.school_id, account.id).run();
}

async function clearAttempts(db, account) {
  await db.prepare(
    "DELETE FROM cloud_auth_attempts WHERE school_id = ? AND user_id = ?"
  ).bind(account.school_id, account.id).run();
}

// Valider à nouveau l'état côté D1 APRÈS PBKDF2 et juste avant la signature.
// Un changement de mot de passe/rôle, une désactivation, un QR expiré ou un
// verrouillage concurrent ne doit pas déboucher sur un nouveau jeton.
// Cette requête unique est atomique : si les droits ont changé, aucun compteur
// n'est remis à zéro. L'acceptation QR revérifie ensuite permission_revision.
async function finalizeSuccessfulAttempt(db, account, invitationId) {
  const completed = await db.prepare(
    "DELETE FROM cloud_auth_attempts WHERE school_id = ? AND user_id = ? " +
    "AND lockout_until = 0 AND EXISTS (" +
    "SELECT 1 FROM cloud_users u " +
    "JOIN cloud_schools s ON s.id = u.school_id " +
    "JOIN cloud_device_invitations i ON i.school_id = u.school_id " +
    "AND i.target_user_id = u.id " +
    "WHERE i.id = ? AND i.consumed_at IS NULL " +
    "AND i.expires_at > unixepoch() AND s.status = 'active' " +
    "AND u.school_id = cloud_auth_attempts.school_id " +
    "AND u.id = cloud_auth_attempts.user_id " +
    "AND u.enabled = 1 AND u.must_change_password = 0 " +
    "AND u.login_normalized = ? AND u.password_hash = ? " +
    "AND u.permission_revision = ?)"
  ).bind(account.school_id, account.id, invitationId,
    account.login_normalized, account.password_hash,
    account.permission_revision).run();
  return completed.meta?.changes === 1;
}

export async function issuePairingIdentityProof(request, env) {
  // Le service refuse de vérifier des mots de passe si ses deux garde-fous
  // anti-abus ne sont pas configurés sur Cloudflare. Pas de mode dégradé.
  if (!env?.DB || !env?.SCOLIA_PAIRING_AUTH_KEY ||
      typeof env.SCOLIA_LOGIN_RATE_LIMITER?.limit !== "function" ||
      typeof env.SCOLIA_LOGIN_SOURCE_LIMITER?.limit !== "function")
    return fail(503, "not_configured");
  const contentLength = Number(request.headers.get("Content-Length") ?? 0);
  if (contentLength > MAX_REQUEST) return fail(413, "too_large");

  let body;
  try {
    const text = await request.text();
    if (encode.encode(text).length > MAX_REQUEST) return fail(413, "too_large");
    body = JSON.parse(text);
  } catch { return fail(400, "invalid_json"); }

  if (!body || typeof body !== "object" || Array.isArray(body) ||
      Object.keys(body).sort().join(",") !==
        "device_id,device_key_thumbprint,invitation_id,password,user_name" ||
      typeof body.user_name !== "string" ||
      !VALID_USERNAME.test(body.user_name) ||
      typeof body.password !== "string" ||
      !body.password || encode.encode(body.password).length > MAX_PASSWORD_BYTES ||
      !VALID_INVITE.test(body.invitation_id) ||
      !VALID_DEVICE.test(body.device_id) ||
      !VALID_THUMBPRINT.test(body.device_key_thumbprint))
    return fail(400, "invalid_request");

  try {
    // Une limite par QR + une limite plus généreuse par provenance : une
    // suite de QR inexistants ne doit pas déclencher PBKDF2 sans restriction.
    // Les quotas Cloudflare sont approximatifs par emplacement : le verrou
    // D1 par compte reste le second garde-fou, et une WAF reste conseillée.
    const origin = request.headers.get("CF-Connecting-IP") || "unknown";
    const originDigest = new Uint8Array(await crypto.subtle.digest(
      "SHA-256", encode.encode(origin)));
    const sourceResult = await env.SCOLIA_LOGIN_SOURCE_LIMITER.limit({
      key: b64url(originDigest),
    });
    if (!sourceResult.success) return fail(429, "too_many_attempts");
    const inviteResult = await env.SCOLIA_LOGIN_RATE_LIMITER.limit({
      key: "pair:" + body.invitation_id.toLowerCase(),
    });
    if (!inviteResult.success) return fail(429, "too_many_attempts");

    const account = await env.DB.prepare(
      "SELECT u.id, u.school_id, u.login_normalized, u.password_hash, " +
      "u.permission_revision, u.enabled, u.must_change_password, " +
      "i.expires_at, i.consumed_at " +
      "FROM cloud_device_invitations i " +
      "JOIN cloud_users u ON u.school_id = i.school_id " +
      "AND u.id = i.target_user_id " +
      "JOIN cloud_schools s ON s.id = i.school_id " +
      "WHERE i.id = ? AND s.status = 'active' LIMIT 1"
    ).bind(body.invitation_id).first();

    const username = body.user_name.trim().toUpperCase();
    const allowed = account && account.enabled === 1 &&
      account.consumed_at == null && account.expires_at > Math.floor(Date.now() / 1000) &&
      account.login_normalized === username && typeof account.password_hash === "string";

    if (!allowed) {
      await obscureUnknownPassword(body.password);
      return fail(401, "invalid_credentials_or_invitation");
    }

    if (!await reserveAttempt(env.DB, account)) return fail(429, "too_many_attempts");

    let valid = false;
    try { valid = await verifyAspNetIdentityV3(body.password, account.password_hash); }
    catch { valid = false; }
    if (!valid) {
      await failedAttempt(env.DB, account);
      return fail(401, "invalid_credentials_or_invitation");
    }
    if (account.must_change_password !== 0) {
      // Validation positive mais aucun jeton de jumelage si le compte doit
      // changer son mot de passe lors d'une connexion au serveur d'origine.
      await clearAttempts(env.DB, account);
      return fail(409, "password_change_required");
    }

    if (!await finalizeSuccessfulAttempt(env.DB, account, body.invitation_id))
      return fail(401, "invalid_credentials_or_invitation");
    const now = Math.floor(Date.now() / 1000);
    const proof = await signPairingClaims({
      iss: "scolia-identity",
      aud: "scolia-pairing",
      school_id: account.school_id,
      sub: account.id,
      invitation_id: body.invitation_id,
      device_id: body.device_id,
      device_key_thumbprint: body.device_key_thumbprint,
      permission_revision: account.permission_revision,
      iat: now,
      nbf: now - 5,
      exp: now + 120,
    }, env.SCOLIA_PAIRING_AUTH_KEY);
    return response({ pairing_proof: proof, expires_in: 120,
      use: "pairing_only", session_issued: false });
  } catch (e) {
    // Ne jamais journaliser le mot de passe, le hash ou les données du corps.
    console.error("SCOLIA identity preview unavailable", String(e?.name || "error"));
    return fail(503, "temporarily_unavailable");
  }
}
