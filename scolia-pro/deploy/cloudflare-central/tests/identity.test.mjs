import { test } from "node:test";
import assert from "node:assert/strict";
import { webcrypto, randomBytes } from "node:crypto";
import { verifyAspNetIdentityV3 } from "../src/identity.mjs";
import { verifyAccountPairingProof } from "../src/pairing.mjs";
import { handleRequest } from "../src/index.mjs";

if (!globalThis.crypto) globalThis.crypto = webcrypto;
const encode = new TextEncoder();
const signingKey = Buffer.alloc(32, 0x32).toString("base64url");
const inviteId = "db2240e2-f9e7-438c-bfd4-3e0bb6fb8cf1";
const password = "VraiMotDePasse-Acceptable!2026";

async function aspNetIdentityV3Hash(input, iterations = 100_000, prf = 2) {
  const salt = randomBytes(16);
  const key = await crypto.subtle.importKey("raw", encode.encode(input), "PBKDF2",
    false, ["deriveBits"]);
  const hash = new Uint8Array(await crypto.subtle.deriveBits({
    name: "PBKDF2", salt, iterations, hash: "SHA-512",
  }, key, 256));
  const result = Buffer.alloc(13 + 16 + 32);
  result[0] = 1;
  result.writeUInt32BE(prf, 1);
  result.writeUInt32BE(iterations, 5);
  result.writeUInt32BE(16, 9);
  salt.copy(result, 13);
  Buffer.from(hash).copy(result, 29);
  return result.toString("base64");
}

async function fixture(changes = {}) {
  const now = Math.floor(Date.now() / 1000);
  return {
    id: "teacher-01",
    school_id: "school-a",
    login_normalized: "ENSEIGNANT",
    password_hash: await aspNetIdentityV3Hash(password),
    permission_revision: 4,
    enabled: 1,
    must_change_password: 0,
    expires_at: now + 300,
    consumed_at: null,
    ...changes,
  };
}

function fakeDb(account) {
  const calls = [];
  const state = { failures: 0, locked: false, count: 0, generated: 0 };
  const env = {
    DB: {
      prepare(sql) {
        return {
          bind(...params) {
            calls.push({ sql, params });
            return {
              async first() {
                if (!sql.includes("FROM cloud_device_invitations"))
                  throw Error("unexpected first");
                return account;
              },
              async run() {
                if (sql.includes("INSERT INTO cloud_auth_attempts")) {
                  if (state.failures >= 5 || state.locked)
                    return { meta: { changes: 0 } };
                  state.failures++;
                  return { meta: { changes: 1 } };
                }
                if (sql.includes("UPDATE cloud_auth_attempts")) {
                  if (state.failures >= 5) state.locked = true;
                  return { meta: { changes: 1 } };
                }
                if (sql.includes("DELETE FROM cloud_auth_attempts")) {
                  if (sql.includes("JOIN cloud_device_invitations")) {
                    // La vérification finale doit être atomique et porter sur
                    // l'empreinte, la révision, les droits et la validité du QR.
                    assert.match(sql, /password_hash = \?/);
                    assert.match(sql, /permission_revision = \?/);
                    assert.match(sql, /expires_at > unixepoch\(\)/);
                    assert.match(sql, /u\.enabled = 1/);
                    assert.match(sql, /lockout_until = 0/);
                    assert.deepEqual(params, [
                      account.school_id, account.id, inviteId,
                      account.login_normalized, account.password_hash,
                      account.permission_revision,
                    ]);
                    if (state.credentialsChangedDuringHash || state.invitationRevokedDuringHash)
                      return { meta: { changes: 0 } };
                  }
                  state.failures = 0;
                  state.locked = false;
                  return { meta: { changes: 1 } };
                }
                throw Error("unexpected run");
              },
            };
          },
        };
      },
    },
    SCOLIA_PAIRING_AUTH_KEY: signingKey,
    SCOLIA_LOGIN_RATE_LIMITER: {
      async limit({ key }) {
        state.lastInviteLimitKey = key;
        return { success: !state.blockedInvite };
      },
    },
    SCOLIA_LOGIN_SOURCE_LIMITER: {
      async limit({ key }) {
        state.lastSourceLimitKey = key;
        return { success: !state.blockedSource };
      },
    },
  };
  return { env, calls, state };
}

async function request(env, overrides = {}) {
  const body = {
    invitation_id: inviteId,
    user_name: "enseignant",
    password,
    device_id: "phone-204",
    device_key_thumbprint: "a".repeat(43),
    ...overrides,
  };
  return handleRequest(new Request("https://preview.test/v1/identity/pairing-proof", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(body),
  }), env);
}

test("vecteur de compatibilité connu vérifié dans les tests ASP.NET Identity du dépôt", async () => {
  const known = "AQAAAAIAAYagAAAAEAARIjNEVWZ3iJmqu8zd7v9PEBB4MUdff3xFpDXan7LAz4OPm6EQ8dZ4q73OiYwzLQ==";
  assert.equal(await verifyAspNetIdentityV3("Scolia-Interop-Fictional-Test-2026!", known), true);
  assert.equal(await verifyAspNetIdentityV3("Incorrect!", known), false);
});

test("le format réel ASP.NET Identity V3 HMAC-SHA512 100000 vérifie le mot de passe", async () => {
  const hash = await aspNetIdentityV3Hash(password);
  assert.equal(await verifyAspNetIdentityV3(password, hash), true);
  assert.equal(await verifyAspNetIdentityV3("Not-Correct!", hash), false);
  assert.equal(await verifyAspNetIdentityV3(password, "bad-hash"), false);
  assert.equal(await verifyAspNetIdentityV3(password, await aspNetIdentityV3Hash(password, 9_000)), false);
  assert.equal(await verifyAspNetIdentityV3(password, await aspNetIdentityV3Hash(password, 100_000, 1)), false);
});

test("le mot de passe du vrai compte autorisé émet SEULEMENT une preuve de jumelage", async () => {
  const account = await fixture();
  const { env, calls } = fakeDb(account);
  const result = await request(env);
  assert.equal(result.status, 200);
  const data = await result.json();
  assert.equal(data.use, "pairing_only");
  assert.equal(data.session_issued, false);
  assert.equal(data.expires_in, 120);
  assert.equal(result.headers.get("Cache-Control"), "no-store");
  const claim = await verifyAccountPairingProof(
    "Bearer " + data.pairing_proof, signingKey);
  assert.equal(claim?.school_id, "school-a");
  assert.equal(claim?.sub, "teacher-01");
  assert.equal(claim?.device_id, "phone-204");
  assert.equal(claim?.invitation_id, inviteId);
  assert.equal(claim?.device_key_thumbprint, "a".repeat(43));
  assert.equal(claim?.permission_revision, 4);
  assert.equal(claim?.exp - claim?.iat, 120);
  assert.deepEqual(calls[0].params, [inviteId]);
  assert.deepEqual(calls[1].params, ["school-a", "teacher-01"]);
  assert.doesNotMatch(JSON.stringify(data), /password_hash|school-a|EN SEIGNANT|VraiMotDePasse/i);
});

test("cinq échecs bloquent même si l'on recommence avec un autre QR du compte", async () => {
  const { env, calls, state } = fakeDb(await fixture());
  for (let i = 0; i < 5; i++) {
    const result = await request(env, { password: "mauvais-mot-de-passe" });
    assert.equal(result.status, 401);
    assert.deepEqual(await result.json(), { error: "invalid_credentials_or_invitation" });
  }
  assert.equal(state.failures, 5);
  assert.equal(state.locked, true);
  const blocked = await request(env);
  assert.equal(blocked.status, 429);
  assert.deepEqual(await blocked.json(), { error: "too_many_attempts" });
  assert.ok(calls.some(x => x.sql.includes("cloud_auth_attempts")));
});

test("les comptes inexistants, désactivés et les QR périmés refusent toute preuve", async () => {
  for (const account of [
    null, await fixture({ enabled: 0 }), await fixture({ consumed_at: 1000 }),
    await fixture({ expires_at: 1 }),
  ]) {
    const { env, state } = fakeDb(account);
    const result = await request(env);
    assert.equal(result.status, 401);
    assert.equal(state.failures, 0);
  }
});

test("le compte avec changement obligatoire de mot de passe n'est pas activé", async () => {
  const { env, state } = fakeDb(await fixture({ must_change_password: 1 }));
  const result = await request(env);
  assert.equal(result.status, 409);
  assert.deepEqual(await result.json(), { error: "password_change_required" });
  assert.equal(state.failures, 0);
});

test("un identifiant erroné, une empreinte absente ou une clé incorrecte ne donne pas de preuve", async () => {
  const current = await fixture();
  const { env } = fakeDb(current);
  assert.equal((await request(env, { user_name: "autre-personne" })).status, 401);
  assert.equal((await request(fakeDb(await fixture({ password_hash: null })).env)).status, 401);
  assert.equal((await request({ DB: env.DB })).status, 503);
  assert.equal((await request(env, { device_id: "" })).status, 400);
  assert.equal((await request(env, { device_key_thumbprint: "a".repeat(10) })).status, 400);
  assert.equal((await request(env, { password: "x".repeat(520) })).status, 400);
});

test("la vérification n'expose jamais le mot de passe ni une session réelle", async () => {
  const { env } = fakeDb(await fixture());
  const result = await request(env, { password: "mot-de-passe-faux" });
  const payload = await result.text();
  assert.equal(result.status, 401);
  assert.doesNotMatch(payload, /mot-de-passe-faux|password_hash|teacher-01|school-a/);
  assert.equal(result.headers.get("X-Content-Type-Options"), "nosniff");
});

test("les limiteurs de tentative Cloudflare sont obligatoires", async () => {
  const acc = await fixture();
  const { env } = fakeDb(acc);
  assert.equal((await request({ ...env, SCOLIA_LOGIN_RATE_LIMITER: undefined })).status, 503);
  assert.equal((await request({ ...env, SCOLIA_LOGIN_SOURCE_LIMITER: undefined })).status, 503);
});

test("limiteur QR : aucun hash ni accès D1 lorsqu'un quota est dépassé", async () => {
  const { env, state, calls } = fakeDb(await fixture());
  state.blockedInvite = true;
  const response = await request(env);
  assert.equal(response.status, 429);
  assert.equal(state.lastInviteLimitKey, "pair:" + inviteId);
  assert.equal(calls.length, 0);
});

test("limiteur de provenance : limite également les QR inconnus", async () => {
  const { env, state, calls } = fakeDb(null);
  state.blockedSource = true;
  const response = await request(env, { invitation_id: "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa" });
  assert.equal(response.status, 429);
  assert.equal(calls.length, 0);
  assert.match(state.lastSourceLimitKey, /^[A-Za-z0-9_-]{43}$/);
});

test("un mot de passe remplacé pendant PBKDF2 empêche l'émission d'une preuve", async () => {
  const { env, state, calls } = fakeDb(await fixture());
  state.credentialsChangedDuringHash = true;
  const response = await request(env);
  assert.equal(response.status, 401);
  assert.deepEqual(await response.json(), { error: "invalid_credentials_or_invitation" });
  assert.equal(state.failures, 1);
  assert.ok(calls.some(x => x.sql.includes("JOIN cloud_device_invitations") &&
    x.sql.includes("DELETE FROM cloud_auth_attempts")));
});

test("une invitation annulée pendant le calcul PBKDF2 empêche l'émission de preuve", async () => {
  const { env, state } = fakeDb(await fixture());
  state.invitationRevokedDuringHash = true;
  const response = await request(env);
  assert.equal(response.status, 401);
  assert.equal(state.failures, 1);
});
