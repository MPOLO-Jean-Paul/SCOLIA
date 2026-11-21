import { test } from "node:test";
import assert from "node:assert/strict";
import { webcrypto, randomBytes } from "node:crypto";
import { issueCentralSession } from "../src/session.mjs";
import { verifyToken, handleRequest } from "../src/index.mjs";

if (!globalThis.crypto) globalThis.crypto = webcrypto;
const signingKey = Buffer.alloc(32, 0x44).toString("base64url");
const pairingKey = Buffer.alloc(32, 0x55).toString("base64url");
const enc = new TextEncoder();

async function candidate() {
  const pair = await crypto.subtle.generateKey(
    { name: "ECDSA", namedCurve: "P-256" }, true, ["sign", "verify"]);
  const exported = await crypto.subtle.exportKey("jwk", pair.publicKey);
  const publicKey = JSON.stringify({
    crv: "P-256", kty: "EC", x: exported.x, y: exported.y,
  });
  const body = {
    school_id: "school-a",
    device_id: "phone-01",
    nonce: randomBytes(32).toString("base64url"),
    issued_at: Math.floor(Date.now() / 1000),
  };
  return { pair, publicKey, body };
}

async function sign(body, privateKey) {
  const message = enc.encode([
    "SCOLIA-SESSION-V1", body.school_id, body.device_id,
    body.nonce, String(body.issued_at),
  ].join("\n"));
  return {
    ...body,
    signature: Buffer.from(await crypto.subtle.sign(
      { name: "ECDSA", hash: "SHA-256" }, privateKey, message)).toString("base64url"),
  };
}

function fakeEnv(publicKey, options = {}) {
  const seen = new Set(), calls = [];
  const device = { user_id: "teacher-01", permission_revision: 7,
    public_key_jwk: publicKey };
  const env = {
    DB: {
      prepare(sql) {
        return {
          bind(...params) {
            calls.push({ sql, params });
            return {
              async first() {
                if (sql.includes("FROM cloud_devices d"))
                  return options.revoked ? null : device;
                if (sql.includes("FROM cloud_users AS u"))
                  return { role: "secretariat" };
                throw Error("unexpected query");
              },
              async run() {
                if (!sql.includes("INSERT OR IGNORE INTO cloud_session_nonces"))
                  throw Error("unexpected run");
                const key = params.slice(0, 3).join("|");
                if (seen.has(key) || options.revokedDuringRegistration)
                  return { meta: { changes: 0 } };
                seen.add(key);
                return { meta: { changes: 1 } };
              },
              async all() {
                if (!sql.includes("FROM cloud_change_log"))
                  throw Error("unexpected all");
                return { results: [] };
              },
            };
          },
        };
      },
    },
    SCOLIA_AUTH_HMAC_KEY: signingKey,
    SCOLIA_PAIRING_AUTH_KEY: pairingKey,
    SCOLIA_LOGIN_RATE_LIMITER: {
      async limit({ key }) {
        calls.push({ limiter: "device", key });
        return { success: !options.deviceLimited };
      },
    },
    SCOLIA_LOGIN_SOURCE_LIMITER: {
      async limit({ key }) {
        calls.push({ limiter: "source", key });
        return { success: !options.sourceLimited };
      },
    },
  };
  return { env, calls, seen };
}

async function request(body, env) {
  return issueCentralSession(new Request("https://staging.invalid/v1/identity/session", {
    method: "POST", headers: { "Content-Type": "application/json" },
    body: JSON.stringify(body),
  }), env);
}

test("session: clé P-256 approuvée, preuve fraîche, JWT 15 min", async () => {
  const item = await candidate();
  const { env, calls } = fakeEnv(item.publicKey);
  const reply = await request(await sign(item.body, item.pair.privateKey), env);
  assert.equal(reply.status, 200);
  const content = await reply.json();
  assert.equal(content.expires_in, 900);
  assert.equal(content.token_type, "Bearer");
  assert.equal(content.school_id, "school-a");
  assert.equal(content.device_id, "phone-01");
  assert.equal(reply.headers.get("Cache-Control"), "no-store");
  const claims = await verifyToken("Bearer " + content.access_token, signingKey);
  assert.equal(claims?.sub, "teacher-01");
  assert.equal(claims?.permission_revision, 7);
  assert.equal(claims?.school_id, "school-a");
  assert.equal(claims?.device_id, "phone-01");
  assert.equal(claims?.exp - claims?.iat, 900);
  const nonceWrite = calls.find(x => x.sql?.includes("INSERT OR IGNORE"));
  assert.equal(nonceWrite.params[0], "school-a");
  assert.equal(nonceWrite.params[1], "phone-01");
  assert.equal(nonceWrite.params[2].length, 64);
  assert.match(nonceWrite.sql, /d.revoked_at IS NULL/);
  assert.match(nonceWrite.sql, /u.permission_revision = \?/);
  assert.match(nonceWrite.sql, /u.must_change_password = 0/);
  assert.ok(!JSON.stringify(nonceWrite.params).includes(item.body.nonce));
});

test("session: nonce déjà reçu ne délivre aucun second JWT", async () => {
  const item = await candidate();
  const { env } = fakeEnv(item.publicKey);
  const body = await sign(item.body, item.pair.privateKey);
  assert.equal((await request(body, env)).status, 200);
  const second = await request(body, env);
  assert.equal(second.status, 409);
  assert.deepEqual(await second.json(), { error: "device_proof_replayed_or_revoked" });
});

test("session: clé privée étrangère, appareil révoqué ou changement concurrent", async () => {
  const item = await candidate();
  const stranger = await candidate();
  const wrong = await sign(item.body, stranger.pair.privateKey);
  assert.equal((await request(wrong, fakeEnv(item.publicKey).env)).status, 401);
  const signed = await sign(item.body, item.pair.privateKey);
  assert.equal((await request(signed, fakeEnv(item.publicKey, { revoked: true }).env)).status, 401);
  assert.equal((await request(signed,
    fakeEnv(item.publicKey, { revokedDuringRegistration: true }).env)).status, 409);
});

test("session: horodatage, champs, secrets et limites manquants bloquent la requête", async () => {
  const item = await candidate();
  const signed = await sign(item.body, item.pair.privateKey);
  assert.equal((await request({ ...signed, issued_at: 1 }, fakeEnv(item.publicKey).env)).status, 401);
  assert.equal((await request({ ...signed, token: "extra" }, fakeEnv(item.publicKey).env)).status, 400);
  assert.equal((await request(signed,
    fakeEnv(item.publicKey, { deviceLimited: true }).env)).status, 429);
  assert.equal((await request(signed,
    fakeEnv(item.publicKey, { sourceLimited: true }).env)).status, 429);
  const unconfigured = fakeEnv(item.publicKey).env;
  delete unconfigured.SCOLIA_LOGIN_SOURCE_LIMITER;
  assert.equal((await request(signed, unconfigured)).status, 503);
  const sharedKey = fakeEnv(item.publicKey).env;
  sharedKey.SCOLIA_PAIRING_AUTH_KEY = signingKey;
  assert.equal((await request(signed, sharedKey)).status, 503);
});

test("session: le JWT central autorise une lecture de métadonnées, pas l'école étrangère", async () => {
  const item = await candidate();
  const { env, calls } = fakeEnv(item.publicKey);
  const tokenResponse = await request(await sign(item.body, item.pair.privateKey), env);
  assert.equal(tokenResponse.status, 200);
  const token = (await tokenResponse.json()).access_token;
  const read = await handleRequest(new Request(
    "https://staging.invalid/v1/sync/events?after=0&school_id=school-b",
    { headers: { Authorization: "Bearer " + token } }), env);
  // Seul le journal de l'école issue du jeton est parcouru.
  assert.equal(read.status, 200);
  const journal = calls.find(x => x.sql?.includes("FROM cloud_change_log"));
  assert.equal(journal?.params[0], "school-a");
});
