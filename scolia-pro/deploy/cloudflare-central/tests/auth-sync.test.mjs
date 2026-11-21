import { test } from "node:test";
import assert from "node:assert/strict";
import { webcrypto } from "node:crypto";
import { handleRequest, verifyToken } from "../src/index.mjs";

if (!globalThis.crypto) globalThis.crypto = webcrypto;
const secret = Buffer.alloc(32, 0x64).toString("base64url");
const now = Math.floor(Date.now() / 1000);
const standard = {
  aud: "scolia-api", iss: "scolia-central",
  school_id: "school-a", sub: "secretary", device_id: "pc-1",
  permission_revision: 4, iat: now - 5, exp: now + 200,
};

async function token(claims = standard, signingKey = secret) {
  const header = Buffer.from(JSON.stringify({ alg: "HS256", typ: "JWT" })).toString("base64url");
  const payload = Buffer.from(JSON.stringify(claims)).toString("base64url");
  const unsigned = header + "." + payload;
  const key = await crypto.subtle.importKey("raw", Buffer.from(signingKey, "base64url"),
    { name: "HMAC", hash: "SHA-256" }, false, ["sign"]);
  const signature = await crypto.subtle.sign("HMAC", key, new TextEncoder().encode(unsigned));
  return unsigned + "." + Buffer.from(signature).toString("base64url");
}

function fakeEnvironment(options = {}) {
  const calls = [], cursorWrites = [];
  const env = {
    SCOLIA_AUTH_HMAC_KEY: secret,
    DB: {
      prepare(sql) {
        return {
          bind(...params) {
            calls.push({ sql, params });
            return {
              async first() {
                if (sql.includes("FROM cloud_users")) {
                  if (options.revoked || options.disabled || options.permissionRevisionChanged)
                    return null;
                  return { role: options.role ?? "Secretariat" };
                }
                if (sql.includes("MAX(revision)")) return { latest: options.latest ?? 20 };
                throw Error("unexpected first");
              },
              async all() {
                if (!sql.includes("FROM cloud_change_log")) throw Error("unexpected all");
                return { results: options.events ?? [
                  { revision: 11, entity_kind: "test", entity_id: "object-1",
                    change_kind: "upsert", payload_sha256: "a".repeat(64), created_at: now },
                ] };
              },
              async run() {
                if (!sql.includes("INSERT INTO cloud_sync_cursors")) throw Error("unexpected run");
                cursorWrites.push(params);
                return { success: true };
              },
            };
          },
        };
      },
    },
  };
  return { env, calls, cursorWrites };
}

async function call(path, { method = "GET", body, claims, env = fakeEnvironment().env, authorization } = {}) {
  const auth = authorization ?? "Bearer " + await token(claims);
  const headers = new Headers({ Authorization: auth });
  if (body !== undefined) headers.set("Content-Type", "application/json");
  return handleRequest(new Request("https://preview.example" + path,
    { method, headers, body: body === undefined ? undefined : JSON.stringify(body) }), env);
}

test("JWT exige signature HMAC, courte durée, audience et révision valides", async () => {
  assert.deepEqual((await verifyToken("Bearer " + await token()))?.school_id, "school-a");
  assert.equal(await verifyToken("Bearer " + await token(), Buffer.alloc(32, 8).toString("base64url")), null);
  assert.equal(await verifyToken("Bearer " + await token({ ...standard, exp: now - 1 })), null);
  assert.equal(await verifyToken("Bearer " + await token({ ...standard, aud: "other-api" })), null);
  assert.equal(await verifyToken("Bearer " + await token({ ...standard, permission_revision: 0 })), null);
  assert.equal(await verifyToken("Bearer " + await token({ ...standard, exp: now + 3000 })), null);
  assert.equal(await verifyToken("Bearer garbage"), null);
});

test("aucune route sensible accessible sans compte et appareil autorisés", async () => {
  assert.equal((await call("/v1/sync/events", { authorization: "Bearer broken" })).status, 401);
  assert.equal((await call("/v1/sync/events", { env: fakeEnvironment({ revoked: true }).env })).status, 401);
  assert.equal((await call("/v1/sync/events", { env: fakeEnvironment({ disabled: true }).env })).status, 401);
  assert.equal((await call("/v1/sync/events", { env: fakeEnvironment({ permissionRevisionChanged: true }).env })).status, 401);
  assert.equal((await call("/v1/sync/events", { env: fakeEnvironment({ role: "Enseignant" }).env })).status, 403);
  assert.equal((await call("/v1/sync/events", { env: fakeEnvironment({ role: "Promoteur" }).env })).status, 403);
  assert.equal((await call("/v1/sync/secret")).status, 404);
});

test("la lecture du journal utilise toujours le school_id du jeton vérifié", async () => {
  const { env, calls } = fakeEnvironment();
  const reply = await call("/v1/sync/events?after=10&limit=5&school_id=school-b", { env });
  assert.equal(reply.status, 200);
  const data = await reply.json();
  assert.equal(data.events.length, 1);
  assert.equal(data.next, 11);
  assert.equal(data.includes_payload, false);
  assert.equal(reply.headers.get("Cache-Control"), "no-store");
  assert.deepEqual(calls[0].params, ["school-a", "secretary", "pc-1", 4]);
  assert.deepEqual(calls[1].params, ["school-a", 10, 5]);
  assert.match(calls[1].sql, /WHERE school_id = \?/);
});

test("un curseur invalide ou trop grand est rejeté", async () => {
  for (const path of [
    "/v1/sync/events?after=-1",
    "/v1/sync/events?after=1.5",
    "/v1/sync/events?after=9007199254740999",
    "/v1/sync/events?limit=101",
    "/v1/sync/events?limit=0",
    "/v1/sync/events?limit=NaN",
  ]) assert.equal((await call(path)).status, 400, path);
});

test("ACK authentifié, borné par révision serveur et rattaché au seul appareil", async () => {
  const { env, calls, cursorWrites } = fakeEnvironment({ latest: 12 });
  const ok = await call("/v1/sync/ack", { method: "POST", body: { revision: 8 }, env });
  assert.equal(ok.status, 200);
  assert.deepEqual(cursorWrites[0], ["school-a", "pc-1", 8]);
  assert.match(calls[2].sql, /MAX\(cloud_sync_cursors.last_applied_revision, excluded.last_applied_revision\)/);
  assert.equal((await call("/v1/sync/ack",
    { method: "POST", body: { revision: 13 }, env })).status, 400);
  assert.equal((await call("/v1/sync/ack",
    { method: "POST", body: { revision: -1 }, env })).status, 400);
  assert.equal((await call("/v1/sync/ack",
    { method: "POST", body: { revision: "8" }, env })).status, 400);
  assert.equal(cursorWrites.length, 1);
});

test("la lecture échoue sans base ni clé et ne divulgue pas les données", async () => {
  const reply = await call("/v1/sync/events", { env: { DB: null } });
  assert.equal(reply.status, 503);
  const info = await reply.json();
  assert.deepEqual(info, { error: "not_configured" });
});

test("le compte non privilégié ne peut pas confirmer un curseur", async () => {
  const { env, cursorWrites } = fakeEnvironment({ role: "Enseignant" });
  const reply = await call("/v1/sync/ack", { method: "POST", body: { revision: 1 }, env });
  assert.equal(reply.status, 403);
  assert.equal(cursorWrites.length, 0);
});
