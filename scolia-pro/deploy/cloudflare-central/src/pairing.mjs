// Prototype du jumelage sécurisé SCOLIA. AUCUN service d'émission de
// preuves d'identité n'existe encore : ne pas déployer sur un Worker public.
// Le QR seul NE PEUT PAS activer un appareil.
const encode = new TextEncoder();
const decoder = new TextDecoder("utf-8", { fatal: true });
const deviceIdPattern = /^[A-Za-z0-9._:-]{1,128}$/;
const b64Pattern = /^[A-Za-z0-9_-]+$/;

function respond(data, status = 200) {
  return new Response(JSON.stringify(data), {
    status, headers: {
      "Content-Type": "application/json; charset=utf-8",
      "Cache-Control": "no-store",
      "X-Content-Type-Options": "nosniff",
    },
  });
}
const failure = (status, error) => respond({ error }, status);

function decode64(value, max = 8192) {
  if (typeof value !== "string" || !b64Pattern.test(value) || value.length > max)
    throw Error("invalid_base64url");
  const text = atob(value.replace(/-/g, "+").replace(/_/g, "/"));
  return Uint8Array.from(text, c => c.charCodeAt(0));
}
const b64 = bytes => btoa(String.fromCharCode(...bytes))
  .replace(/\+/g, "-").replace(/\//g, "_").replace(/=+$/g, "");

function parseJsonPart(encoded) {
  return JSON.parse(decoder.decode(decode64(encoded)));
}

// Signature vérifiée avec une clé DISTINCTE du JWT de la session d'un
// appareil approuvé. La preuve doit être émise après authentification réelle
// du compte destinataire et liée à CETTE invitation + CETTE clé publique.
// Le futur émetteur d'identité doit rester hors des APK.
export async function verifyAccountPairingProof(auth, signingKey, now = Math.floor(Date.now() / 1000)) {
  if (typeof auth !== "string" || !auth.startsWith("Bearer ") ||
      typeof signingKey !== "string") return null;
  try {
    const parts = auth.slice(7).split(".");
    if (parts.length !== 3) return null;
    const [headerEncoded, payloadEncoded, signatureEncoded] = parts;
    const header = parseJsonPart(headerEncoded);
    if (header?.alg !== "HS256" || header.typ !== "JWT" ||
        Object.keys(header).some(k => k !== "typ" && k !== "alg")) return null;
    const keyBytes = decode64(signingKey);
    if (keyBytes.length < 32) return null;
    const key = await crypto.subtle.importKey("raw", keyBytes,
      { name: "HMAC", hash: "SHA-256" }, false, ["verify"]);
    const verified = await crypto.subtle.verify("HMAC", key,
      decode64(signatureEncoded), encode.encode(headerEncoded + "." + payloadEncoded));
    if (!verified) return null;
    const claims = parseJsonPart(payloadEncoded);
    if (!claims || claims.iss !== "scolia-identity" ||
        claims.aud !== "scolia-pairing" ||
        typeof claims.school_id !== "string" || !claims.school_id ||
        typeof claims.sub !== "string" || !claims.sub ||
        typeof claims.invitation_id !== "string" || !claims.invitation_id ||
        !deviceIdPattern.test(claims.device_id) ||
        typeof claims.device_key_thumbprint !== "string" ||
        !/^[A-Za-z0-9_-]{43}$/.test(claims.device_key_thumbprint) ||
        !Number.isSafeInteger(claims.permission_revision) ||
        claims.permission_revision < 1 ||
        !Number.isSafeInteger(claims.iat) ||
        !Number.isSafeInteger(claims.exp) ||
        claims.exp <= now || claims.iat > now + 30 ||
        claims.exp <= claims.iat || claims.exp - claims.iat > 300 ||
        (claims.nbf != null &&
          (!Number.isSafeInteger(claims.nbf) || claims.nbf > now))) return null;
    return claims;
  } catch {
    return null;
  }
}

function normalizeKey(jwk) {
  if (!jwk || Array.isArray(jwk) || typeof jwk !== "object" ||
      Object.keys(jwk).sort().join(",") !== "crv,kty,x,y" ||
      jwk.kty !== "EC" || jwk.crv !== "P-256" ||
      !/^[A-Za-z0-9_-]{43}$/.test(jwk.x) ||
      !/^[A-Za-z0-9_-]{43}$/.test(jwk.y)) return null;
  return { crv: "P-256", kty: "EC", x: jwk.x, y: jwk.y };
}

async function keyFingerprint(key) {
  const data = await crypto.subtle.digest("SHA-256", encode.encode(JSON.stringify(key)));
  return b64(new Uint8Array(data));
}

async function possessedKey(key, signature, body) {
  if (typeof signature !== "string") return false;
  try {
    const sig = decode64(signature, 128);
    if (sig.length !== 64) return false;
    const imported = await crypto.subtle.importKey("jwk", key,
      { name: "ECDSA", namedCurve: "P-256" }, false, ["verify"]);
    const message = encode.encode(
      ["SCOLIA-PAIR-V1", body.invitation_id, body.device_id, body.token].join("\n"));
    return await crypto.subtle.verify(
      { name: "ECDSA", hash: "SHA-256" }, imported, sig, message);
  } catch {
    return false;
  }
}

// Accès refusé par défaut sans preuve d'un SECOND facteur d'autorisation :
// l'utilisateur doit prouver SON identité auprès du futur service d'identité,
// indépendamment du QR transmis par le secrétariat.
export async function acceptPairing(request, env) {
  if (!env?.DB || !env.SCOLIA_PAIRING_AUTH_KEY) return failure(503, "not_configured");
  const proof = await verifyAccountPairingProof(
    request.headers.get("X-Scolia-Account-Proof"), env.SCOLIA_PAIRING_AUTH_KEY);
  if (!proof) return failure(401, "account_authentication_required");

  if ((Number(request.headers.get("Content-Length")) || 0) > 2048)
    return failure(413, "too_large");

  let body;
  try {
    const raw = await request.text();
    if (encode.encode(raw).length > 2048) return failure(413, "too_large");
    body = JSON.parse(raw);
  } catch {
    return failure(400, "invalid_json");
  }
  if (!body || Array.isArray(body) || typeof body !== "object" ||
      Object.keys(body).sort().join(",") !==
        "device_id,device_signature,invitation_id,public_key_jwk,token" ||
      typeof body.invitation_id !== "string" ||
      !/^[a-fA-F0-9-]{36}$/.test(body.invitation_id) ||
      !deviceIdPattern.test(body.device_id) ||
      typeof body.token !== "string" ||
      !/^[A-Za-z0-9_-]{43}$/.test(body.token)) {
    return failure(400, "invalid_pairing_request");
  }
  const key = normalizeKey(body.public_key_jwk);
  if (!key) return failure(400, "invalid_public_key");

  if (proof.invitation_id !== body.invitation_id ||
      proof.device_id !== body.device_id ||
      proof.device_key_thumbprint !== await keyFingerprint(key))
    return failure(403, "proof_not_bound_to_device");

  if (!await possessedKey(key, body.device_signature, body))
    return failure(403, "device_private_key_required");

  try {
    const decoded = decode64(body.token);
    if (decoded.length !== 32) return failure(400, "invalid_pairing_request");
    const hash = await crypto.subtle.digest("SHA-256", decoded);
    const digest = Array.from(new Uint8Array(hash),
      byte => byte.toString(16).padStart(2, "0")).join("");

    // Une seule UPDATE conditionnelle : le trigger D1 enregistre le nouvel
    // appareil avec la même atomicité. Un QR expiré/utilisé ou un compte
    // désactivé ne crée JAMAIS de périphérique.
    const result = await env.DB.prepare(
      "UPDATE cloud_device_invitations SET consumed_at = unixepoch(), " +
      "consumed_device_id = ?, consumed_device_public_key = ? " +
      "WHERE school_id = ? AND id = ? AND target_user_id = ? " +
      "AND token_sha256 = ? AND consumed_at IS NULL " +
      "AND expires_at > unixepoch() " +
      "AND EXISTS (SELECT 1 FROM cloud_users u JOIN cloud_schools s " +
      "ON s.id = u.school_id WHERE u.school_id = cloud_device_invitations.school_id " +
      "AND u.id = cloud_device_invitations.target_user_id " +
      "AND u.enabled = 1 AND u.permission_revision = ? AND s.status = 'active')"
    ).bind(body.device_id, JSON.stringify(key), proof.school_id,
      body.invitation_id, proof.sub, digest, proof.permission_revision).run();

    if (result.meta?.changes !== 1)
      return failure(409, "invitation_used_expired_or_not_authorized");

    // L'appareil est approuvé, mais aucun jeton de session n'est délivré.
    // Le système d'identité doit le connecter séparément, après validation.
    return respond({ device_id: body.device_id, approved: true,
      session_issued: false }, 201);
  } catch {
    // Le trigger et l'UPDATE forment une même transaction SQLite.
    // Toute erreur d'insertion annule l'utilisation du code.
    return failure(409, "invitation_used_expired_or_not_authorized");
  }
}
