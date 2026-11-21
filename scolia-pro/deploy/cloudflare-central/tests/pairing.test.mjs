import { test } from "node:test";
import assert from "node:assert/strict";
import { webcrypto, randomBytes } from "node:crypto";
import { handleRequest } from "../src/index.mjs";
import { verifyAccountPairingProof } from "../src/pairing.mjs";

if (!globalThis.crypto) globalThis.crypto = webcrypto;

const pairSecret = Buffer.alloc(32, 0x59).toString("base64url");
const encoder = new TextEncoder();
const b64 = bytes => Buffer.from(bytes).toString("base64url");

async function signedIdentityProof(claims) {
  const h = b64(encoder.encode(JSON.stringify({ alg: "HS256", typ: "JWT" })));
  const p = b64(encoder.encode(JSON.stringify(claims)));
  const key = await crypto.subtle.importKey("raw", Buffer.from(pairSecret, "base64url"),
    { name: "HMAC", hash: "SHA-256" }, false, ["sign"]);
  const sig = await crypto.subtle.sign("HMAC", key, encoder.encode(h + "." + p));
  return "Bearer " + h + "." + p + "." + b64(sig);
}

async function fixture() {
  const pair = await crypto.subtle.generateKey(
    { name: "ECDSA", namedCurve: "P-256" }, true, ["sign", "verify"]);
  const exported = await crypto.subtle.exportKey("jwk", pair.publicKey);
  const publicKey = { crv: "P-256", kty: "EC", x: exported.x, y: exported.y };
  const thumbprint = b64(await crypto.subtle.digest("SHA-256",
    encoder.encode(JSON.stringify(publicKey))));
  const invitation = "bdfb0fe3-9464-42b5-a6a8-0c7520978843";
  const token = randomBytes(32).toString("base64url");
  const body = {
    invitation_id: invitation,
    device_id: "phone-a23",
    public_key_jwk: publicKey,
    token,
  };
  const msg = ["SCOLIA-PAIR-V1", invitation, body.device_id, token].join("\n");
  body.device_signature = b64(await crypto.subtle.sign(
    { name: "ECDSA", hash: "SHA-256" }, pair.privateKey, encoder.encode(msg)));
  const now = Math.floor(Date.now() / 1000);
  const claims = {
    iss: "scolia-identity", aud: "scolia-pairing",
    school_id: "school-a", sub: "teacher", invitation_id: invitation,
    device_id: body.device_id, device_key_thumbprint: thumbprint,
    permission_revision: 4, iat: now - 5, exp: now + 100,
  };
  return { body, claims, keyPair: pair };
}

async function call(env, body, proof) {
  const request = new Request("https://preview.invalid/v1/pair/accept", {
    method: "POST",
    headers: {
      "Content-Type": "application/json",
      "X-Scolia-Account-Proof": proof,
    },
    body: JSON.stringify(body),
  });
  return handleRequest(request, env);
}

function mockDb(shouldApply = true) {
  const queries = [];
  const env = {
    SCOLIA_PAIRING_AUTH_KEY: pairSecret,
    DB: {
      prepare(sql) {
        assert.match(sql, /UPDATE cloud_device_invitations/);
        assert.match(sql, /token_sha256 = \?/);
        assert.match(sql, /AND consumed_at IS NULL/);
        assert.match(sql, /expires_at > unixepoch\(\)/);
        assert.match(sql, /permission_revision = \?/);
        return {
          bind(...params) {
            queries.push(params);
            return {
              async run() { return { meta: { changes: shouldApply ? 1 : 0 } }; },
            };
          },
        };
      },
    },
  };
  return { env, queries };
}

test("une preuve valide, le QR ET la clé privée du téléphone sont nécessaires", async () => {
  const { body, claims } = await fixture();
  const proof = await signedIdentityProof(claims);
  const { env, queries } = mockDb();
  const response = await call(env, body, proof);
  assert.equal(response.status, 201);
  assert.deepEqual(await response.json(), {
    device_id: "phone-a23", school_id: "school-a",
    approved: true, session_issued: false,
  });
  assert.equal(queries.length, 1);
  const params = queries[0];
  assert.equal(params.length, 7);
  assert.equal(params[0], body.device_id);
  assert.equal(params[2], claims.school_id);
  assert.equal(params[3], body.invitation_id);
  assert.equal(params[4], claims.sub);
  assert.equal(params[6], claims.permission_revision);
  assert.equal(params[5], Buffer.from(await crypto.subtle.digest(
    "SHA-256", Buffer.from(body.token, "base64url"))).toString("hex"));
  assert.ok(!JSON.stringify(params).includes(body.token));
});

test("le QR seul ne peut pas enregistrer un appareil", async () => {
  const { body } = await fixture();
  const { env, queries } = mockDb();
  const response = await call(env, body, "Bearer not-a-valid-token");
  assert.equal(response.status, 401);
  assert.deepEqual(queries, []);
});

test("une preuve expirée, au mauvais public ou trop longue est refusée", async () => {
  const { body, claims } = await fixture();
  const { env, queries } = mockDb();
  for (const bad of [
    { ...claims, exp: claims.iat - 1 },
    { ...claims, exp: claims.iat + 301 },
    { ...claims, aud: "scolia-api" },
    { ...claims, permission_revision: 0 },
  ]) {
    assert.equal((await call(env, body, await signedIdentityProof(bad))).status, 401);
  }
  assert.deepEqual(queries, []);
});

test("la preuve d'un autre appareil ou établissement n'est pas acceptée", async () => {
  const { body, claims } = await fixture();
  const { env, queries } = mockDb();
  for (const mismatch of [
    { ...claims, device_id: "some-other-phone" },
    { ...claims, invitation_id: "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa" },
    { ...claims, device_key_thumbprint: "A".repeat(43) },
  ]) {
    assert.equal((await call(env, body, await signedIdentityProof(mismatch))).status, 403);
  }
  assert.deepEqual(queries, []);
});

test("une fausse signature avec une vraie clé publique est refusée", async () => {
  const { body, claims } = await fixture();
  const other = await fixture();
  const fake = { ...body, device_signature: other.body.device_signature };
  const { env, queries } = mockDb();
  assert.equal((await call(env, fake, await signedIdentityProof(claims))).status, 403);
  assert.deepEqual(queries, []);
});

test("une invitation expirée ou réutilisée n'enregistre pas de téléphone", async () => {
  const { body, claims } = await fixture();
  const { env, queries } = mockDb(false);
  const response = await call(env, body, await signedIdentityProof(claims));
  assert.equal(response.status, 409);
  assert.equal(queries.length, 1);
});

test("aucune connexion ni identité ne sort de l'endpoint de jumelage", async () => {
  const { body, claims } = await fixture();
  const proof = await signedIdentityProof(claims);
  const { env } = mockDb(true);
  const response = await call(env, body, proof);
  const txt = await response.text();
  assert.doesNotMatch(txt, /token|jwt|secret|school_id|teacher/i);
  assert.equal(response.headers.get("Cache-Control"), "no-store");
  assert.equal(response.headers.get("X-Content-Type-Options"), "nosniff");
});

test("une clé de preuve absente interdit toute activation", async () => {
  const { body, claims } = await fixture();
  const proof = await signedIdentityProof(claims);
  const response = await call({ DB: mockDb().env.DB }, body, proof);
  assert.equal(response.status, 503);
});

test("la signature d'une preuve d'identité n'est pas réutilisable avec une autre clé", async () => {
  const { claims } = await fixture();
  const proof = await signedIdentityProof(claims);
  assert.equal((await verifyAccountPairingProof(proof, pairSecret))?.sub, "teacher");
  assert.equal(await verifyAccountPairingProof(
    proof, Buffer.alloc(32, 4).toString("base64url")), null);
});
