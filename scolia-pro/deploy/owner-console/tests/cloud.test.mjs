import test from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { createHash, randomBytes } from "node:crypto";
import { DatabaseSync } from "node:sqlite";
import worker from "../src/index.mjs";
import { CONSENT_VERSION } from "../src/policy.mjs";
import { retentionPlan, MAX_SCHOOL_BACKUP_BYTES } from "../src/cloud.mjs";

const HOST = "https://console.example.org";

// Base D1 en mémoire et bucket R2 simulé : aucune ressource Cloudflare réelle.
function setup() {
  const sqlite = new DatabaseSync(":memory:");
  sqlite.exec("PRAGMA foreign_keys=ON");
  for (const name of ["0001_init.sql", "0002_relays.sql", "0003_activity_idempotence.sql",
    "0004_license_controls.sql", "0005_cloud_continuity.sql", "0006_owner_settings_cloud_policy.sql"])
    sqlite.exec(readFileSync(new URL("../migrations/" + name, import.meta.url), "utf8"));
  const statement = (sql, params = []) => ({
    bind: (...values) => statement(sql, values),
    first: async () => sqlite.prepare(sql).get(...params) ?? null,
    all: async () => ({ results: sqlite.prepare(sql).all(...params) }),
    run: async () => ({ meta: sqlite.prepare(sql).run(...params) }),
  });
  let queue = Promise.resolve();
  const db = {
    prepare: sql => statement(sql),
    batch: statements => {
      const work = queue.then(async () => {
        sqlite.exec("BEGIN IMMEDIATE");
        try { const r = []; for (const s of statements) r.push(await s.run()); sqlite.exec("COMMIT"); return r; }
        catch (error) { sqlite.exec("ROLLBACK"); throw error; }
      });
      queue = work.catch(() => {});
      return work;
    },
  };
  const objects = new Map();
  const bucket = {
    put: async (key, value) => { objects.set(key, new Uint8Array(value)); },
    get: async key => objects.has(key) ? { body: objects.get(key) } : null,
    delete: async key => { objects.delete(key); },
  };
  const env = { OWNER_DB: db, BACKUPS: bucket, OWNER_ADMIN_TOKEN: "proprietaire-secret-de-test",
    OWNER_ACCESS_AUD: "test-application-owner-audience-2026" };
  async function call(path, method = "GET", body = null, auth = null, raw = false) {
    const binary = body instanceof Uint8Array;
    const response = await worker.fetch(new Request(HOST + path, {
      method,
      headers: { ...(auth ? { Authorization: "Bearer " + auth } : {}),
        ...(body && !binary ? { "Content-Type": "application/json" } : {}),
        ...(binary ? { "Content-Type": "application/octet-stream", "Content-Length": String(body.length) } : {}) },
      body: body ? (binary ? body : JSON.stringify(body)) : undefined,
    }), env, auth === env.OWNER_ADMIN_TOKEN ? { access: { aud: env.OWNER_ACCESS_AUD } } : undefined);
    if (raw) return response;
    return { status: response.status, body: await response.json() };
  }
  const owner = (path, method = "GET", body = null) => call(path, method, body, env.OWNER_ADMIN_TOKEN);
  async function register(installation = "server_2026_cloud_hk_123456", invite = null) {
    const code = invite ?? (await owner("/api/v1/owner/invitations", "POST", { school_name: "ITM Cœur de Dieu" })).body.invite_code;
    const school = await call("/api/v1/register", "POST", {
      consent: true, consent_version: CONSENT_VERSION, school_name: "ITM Cœur de Dieu",
      server_device_name: "PC SECRETARIAT", installation_id: installation, invite_code: code });
    return school;
  }
  return { sqlite, env, objects, call, owner, register };
}

const sha256 = bytes => createHash("sha256").update(bytes).digest("hex");
const payload = () => randomBytes(96).toString("base64url");

test("Sauvegarde en ligne : activation explicite, envoi vérifié, liste et téléchargement identiques", async t => {
  const app = setup();
  t.after(() => app.sqlite.close());
  const school = (await app.register()).body;
  const token = school.school_token;

  const before = await app.call("/api/v1/server/cloud/backups?sha256=" + "a".repeat(64) + "&key=0123456789abcdef", "PUT", new Uint8Array(100), token);
  assert.equal(before.status, 409);
  assert.equal((await app.call("/api/v1/server/cloud/enable", "POST", { enabled: true }, token)).status, 400);
  const enabled = await app.call("/api/v1/server/cloud/enable", "POST",
    { enabled: true, disclosed_to_school_admin: true, key_fingerprint: "0123456789abcdef" }, token);
  assert.equal(enabled.status, 201);
  assert.ok(enabled.body.deposit_token.length >= 32);

  const file = randomBytes(4096);
  const wrong = await app.call("/api/v1/server/cloud/backups?sha256=" + "b".repeat(64) + "&key=0123456789abcdef", "PUT", file, token);
  assert.equal(wrong.status, 422);
  const sent = await app.call("/api/v1/server/cloud/backups?sha256=" + sha256(file) + "&key=0123456789abcdef", "PUT", file, token);
  assert.equal(sent.status, 201);
  const list = await app.call("/api/v1/server/cloud/backups", "GET", null, token);
  assert.equal(list.body.backups.length, 1);
  const downloaded = await app.call("/api/v1/server/cloud/backups/" + sent.body.id, "GET", null, token, true);
  assert.equal(downloaded.status, 200);
  assert.equal(sha256(new Uint8Array(await downloaded.arrayBuffer())), sha256(file));

  // Un autre établissement ne voit ni ne télécharge cette copie.
  const other = (await app.register("server_2026_other_hk_654321")).body;
  assert.equal((await app.call("/api/v1/server/cloud/backups/" + sent.body.id, "GET", null, other.school_token)).status, 409);

  // Le propriétaire voit la date et la taille, jamais le contenu ni un lien de téléchargement.
  const detail = await app.owner("/api/v1/owner/schools/" + school.school_id);
  assert.equal(detail.body.cloud.enabled, true);
  assert.equal(detail.body.cloud.backups.length, 1);
  assert.equal(detail.body.cloud.backup_bytes, file.length);
  assert.equal((await app.owner("/api/v1/owner/summary")).body.cloud_enabled, 1);
  assert.equal((await app.owner("/api/v1/owner/schools/" + school.school_id + "/backups/" + sent.body.id)).status, 404);
});

test("Relève : dépôt par les appareils, relevé par le serveur, issue connue de l'appareil", async t => {
  const app = setup();
  t.after(() => app.sqlite.close());
  const school = (await app.register()).body;
  const deposit = (await app.call("/api/v1/server/cloud/enable", "POST",
    { enabled: true, disclosed_to_school_admin: true, key_fingerprint: "0123456789abcdef" }, school.school_token)).body.deposit_token;

  // Le jeton de dépôt ne lit rien : ni copies, ni boîte.
  assert.equal((await app.call("/api/v1/server/cloud/mailbox", "GET", null, deposit)).status, 401);
  assert.equal((await app.call("/api/v1/server/cloud/backups", "GET", null, deposit)).status, 401);

  const op = { op_id: "a1b2c3d4e5f60718293a4b5c6d7e8f90", device_id: "telephone-caisse-01", sequence: 1, payload: payload() };
  const first = await app.call("/api/v1/device/mailbox", "POST", op, deposit);
  assert.equal(first.status, 201);
  assert.equal(first.body.server_online, true); // l'activation vaut signe de vie
  const again = await app.call("/api/v1/device/mailbox", "POST", op, deposit);
  assert.equal(again.body.duplicate, true);
  assert.equal((await app.call("/api/v1/device/mailbox", "POST", { ...op, payload: "court" }, deposit)).status, 400);
  const second = { ...op, op_id: "b1b2c3d4e5f60718293a4b5c6d7e8f91", sequence: 2, payload: payload() };
  await app.call("/api/v1/device/mailbox", "POST", second, deposit);

  let outcomes = await app.call("/api/v1/device/mailbox/outcomes", "POST", { op_ids: [op.op_id, second.op_id, "c1b2c3d4e5f60718293a4b5c6d7e8f92"] }, deposit);
  assert.deepEqual(outcomes.body.outcomes, { [op.op_id]: "pending", [second.op_id]: "pending", c1b2c3d4e5f60718293a4b5c6d7e8f92: "unknown" });

  const mailbox = await app.call("/api/v1/server/cloud/mailbox", "GET", null, school.school_token);
  assert.deepEqual(mailbox.body.operations.map(o => o.op_id), [op.op_id, second.op_id]);
  assert.equal(mailbox.body.operations[0].payload, op.payload);
  const ack = await app.call("/api/v1/server/cloud/mailbox/ack", "POST",
    { outcomes: [{ op_id: op.op_id, outcome: "applied" }, { op_id: second.op_id, outcome: "refused" }] }, school.school_token);
  assert.equal(ack.status, 200);
  assert.equal((await app.call("/api/v1/server/cloud/mailbox", "GET", null, school.school_token)).body.operations.length, 0);

  outcomes = await app.call("/api/v1/device/mailbox/outcomes", "POST", { op_ids: [op.op_id, second.op_id] }, deposit);
  assert.deepEqual(outcomes.body.outcomes, { [op.op_id]: "applied", [second.op_id]: "refused" });
  // Une opération déjà faite n'est jamais redéposée.
  const late = await app.call("/api/v1/device/mailbox", "POST", op, deposit);
  assert.equal(late.body.stored, false);
  assert.equal(late.body.outcome, "applied");

  // Arrêt : le jeton de dépôt ne sert plus.
  await app.call("/api/v1/server/cloud/enable", "POST", { enabled: false, disclosed_to_school_admin: true }, school.school_token);
  assert.equal((await app.call("/api/v1/device/status", "GET", null, deposit)).status, 401);
});

test("Reprise sur un nouveau serveur : ancien serveur révoqué, copies récupérables, invitation unique", async t => {
  const app = setup();
  t.after(() => app.sqlite.close());
  const school = (await app.register()).body;
  await app.call("/api/v1/server/cloud/enable", "POST",
    { enabled: true, disclosed_to_school_admin: true, key_fingerprint: "0123456789abcdef" }, school.school_token);
  const file = randomBytes(2048);
  await app.call("/api/v1/server/cloud/backups?sha256=" + sha256(file) + "&key=0123456789abcdef", "PUT", file, school.school_token);

  const invitation = await app.owner("/api/v1/owner/schools/" + school.school_id + "/recovery-invitation", "POST", {});
  assert.equal(invitation.status, 201);
  const [a, b] = await Promise.all([
    app.register("server_2026_nouveau_pc_000001", invitation.body.invite_code),
    app.register("server_2026_nouveau_pc_000002", invitation.body.invite_code),
  ]);
  assert.deepEqual([a.status, b.status].sort(), [201, 403]);
  const recovered = (a.status === 201 ? a : b).body;
  assert.equal(recovered.school_id, school.school_id);
  assert.equal(recovered.recovered, true);
  assert.equal(app.sqlite.prepare("SELECT count(*) AS n FROM school_tokens WHERE revoked_at IS NULL").get().n, 1);
  assert.equal(app.sqlite.prepare("SELECT count(*) AS n FROM schools").get().n, 1);

  // L'ancien serveur est coupé ; le nouveau liste les copies mais doit réactiver avant d'envoyer.
  assert.equal((await app.call("/api/v1/server/cloud/backups", "GET", null, school.school_token)).status, 401);
  const list = await app.call("/api/v1/server/cloud/backups", "GET", null, recovered.school_token);
  assert.equal(list.body.backups.length, 1);
  const upload = await app.call("/api/v1/server/cloud/backups?sha256=" + sha256(file) + "&key=0123456789abcdef", "PUT", file, recovered.school_token);
  assert.equal(upload.status, 409);
});

test("Rétention : 24 copies récentes, une par jour sur 30 jours, une par mois sur 12 mois, plafond par école", () => {
  const now = Date.UTC(2026, 10, 8, 12);
  const rows = [];
  for (let h = 0; h < 24 * 400; h += 1) rows.push({ id: "h" + h, created_at: now - h * 3_600_000, size_bytes: 1000 });
  const removed = new Set(retentionPlan(rows, now).map(r => r.id));
  const kept = rows.filter(r => !removed.has(r.id));
  assert.ok(kept.some(r => r.id === "h0"));
  assert.ok(kept.length <= 24 + 30 + 12 + 1);
  assert.ok(kept.filter(r => now - r.created_at <= 48 * 3_600_000).length >= 24);
  const days = new Set(kept.map(r => new Date(r.created_at).toISOString().slice(0, 10)));
  assert.ok(days.size >= 30);
  const months = new Set(kept.map(r => new Date(r.created_at).toISOString().slice(0, 7)));
  assert.ok(months.size >= 12);

  const big = Array.from({ length: 10 }, (_, i) => ({ id: "b" + i, created_at: now - i * 86_400_000, size_bytes: MAX_SCHOOL_BACKUP_BYTES / 4 }));
  const left = big.filter(r => !new Set(retentionPlan(big, now).map(x => x.id)).has(r.id));
  assert.ok(left.reduce((s, r) => s + r.size_bytes, 0) <= MAX_SCHOOL_BACKUP_BYTES);
  assert.deepEqual(left.slice(0, 3).map(r => r.id), ["b0", "b1", "b2"]);
});

// R2 simulé derrière l'API S3 : vérifie aussi que chaque requête est signée SigV4.
function fakeS3() {
  const objects = new Map();
  const calls = [];
  const fetcher = async (url, init) => {
    calls.push(init.method);
    assert.match(init.headers.authorization, /^AWS4-HMAC-SHA256 Credential=AKIA0123456789ABCDEF\/\d{8}\/auto\/s3\/aws4_request/);
    const key = new URL(url).pathname;
    if (init.method === "PUT") { objects.set(key, new Uint8Array(init.body)); return new Response(null, { status: 200 }); }
    if (init.method === "GET") return objects.has(key) ? new Response(objects.get(key)) : new Response(null, { status: 404 });
    if (init.method === "DELETE") { objects.delete(key); return new Response(null, { status: 204 }); }
    return new Response(null, { status: 405 });
  };
  return { objects, calls, fetcher };
}

const r2 = { account_id: "0123456789abcdef0123456789abcdef", bucket: "scolia-pro-backups",
  access_key_id: "AKIA0123456789ABCDEF", secret_access_key: "s".repeat(40) };

test("Console : identifiants R2 saisis, testés, chiffrés, puis utilisés pour les sauvegardes des écoles", async t => {
  const app = setup();
  t.after(() => app.sqlite.close());
  delete app.env.BACKUPS; // pas de liaison de déploiement : tout passe par les champs de la console
  const s3 = fakeS3();
  app.env.STORAGE_FETCH = s3.fetcher;

  let st = await app.owner("/api/v1/owner/settings/storage");
  assert.equal(st.body.mode, "none");
  assert.equal(st.body.ready, false);
  assert.equal((await app.owner("/api/v1/owner/settings/storage", "PUT", { ...r2, account_id: "court" })).status, 400);
  // Sans le jeton propriétaire, ni lecture ni écriture des réglages.
  assert.equal((await app.call("/api/v1/owner/settings/storage", "PUT", r2)).status, 403);

  const saved = await app.owner("/api/v1/owner/settings/storage", "PUT", r2);
  assert.equal(saved.status, 200);
  assert.deepEqual(s3.calls.slice(0, 3), ["PUT", "GET", "DELETE"]); // fichier témoin
  assert.equal(s3.objects.size, 0);
  const stored = app.sqlite.prepare("SELECT value FROM owner_settings").get().value;
  assert.ok(!stored.includes(r2.secret_access_key) && !stored.includes(r2.access_key_id));
  st = await app.owner("/api/v1/owner/settings/storage");
  assert.equal(st.body.mode, "credentials");
  assert.equal(st.body.ready, true);
  assert.ok(!JSON.stringify(st.body).includes(r2.secret_access_key));
  assert.equal(st.body.access_key_id, "AKIA…CDEF");

  // Une école envoie sa copie : elle arrive dans le bucket par l'API S3.
  const school = (await app.register()).body;
  await app.call("/api/v1/server/cloud/enable", "POST",
    { enabled: true, disclosed_to_school_admin: true, key_fingerprint: "0123456789abcdef" }, school.school_token);
  const file = randomBytes(3000);
  const sent = await app.call("/api/v1/server/cloud/backups?sha256=" + sha256(file) + "&key=0123456789abcdef", "PUT", file, school.school_token);
  assert.equal(sent.status, 201);
  assert.equal(s3.objects.size, 1);
  const back = await app.call("/api/v1/server/cloud/backups/" + sent.body.id, "GET", null, school.school_token, true);
  assert.equal(sha256(new Uint8Array(await back.arrayBuffer())), sha256(file));

  // Identifiants supprimés : plus d'envoi possible, les copies restent dans le bucket.
  await app.owner("/api/v1/owner/settings/storage", "DELETE");
  const refused = await app.call("/api/v1/server/cloud/backups?sha256=" + sha256(file) + "&key=0123456789abcdef", "PUT", file, school.school_token);
  assert.equal(refused.status, 503);
  assert.equal(s3.objects.size, 1);
});

test("Console : un test de stockage échoué n'enregistre rien", async t => {
  const app = setup();
  t.after(() => app.sqlite.close());
  delete app.env.BACKUPS;
  app.env.STORAGE_FETCH = async () => new Response("AccessDenied", { status: 403 });
  const saved = await app.owner("/api/v1/owner/settings/storage", "PUT", r2);
  assert.equal(saved.status, 422);
  assert.match(saved.body.error, /refusé \(403\)/);
  assert.equal(app.sqlite.prepare("SELECT count(*) AS n FROM owner_settings").get().n, 0);
});

test("Console : JP-Services active la sauvegarde en ligne d'une école ou de toutes ; le serveur l'apprend au contrôle", async t => {
  const app = setup();
  t.after(() => app.sqlite.close());
  const school = (await app.register()).body;
  const beat = () => app.call("/api/v1/heartbeat", "POST", {
    event_id: crypto.randomUUID(), event: "online", installation_id: "server_2026_cloud_hk_123456",
    server_device_name: "PC SECRETARIAT", occurred_at: new Date().toISOString() }, school.school_token);
  assert.equal((await beat()).body.cloud_requested, false);

  // Stockage absent : la demande est refusée plutôt que de promettre une sauvegarde impossible.
  const env = app.env.BACKUPS;
  delete app.env.BACKUPS;
  assert.equal((await app.owner("/api/v1/owner/schools/" + school.school_id + "/cloud", "PATCH", { requested: true })).status, 409);
  app.env.BACKUPS = env;

  const one = await app.owner("/api/v1/owner/schools/" + school.school_id + "/cloud", "PATCH", { requested: true });
  assert.equal(one.status, 200);
  assert.equal((await beat()).body.cloud_requested, true);
  const listing = await app.owner("/api/v1/owner/schools");
  assert.equal(listing.body.schools[0].cloud_requested, 1);
  await app.owner("/api/v1/owner/schools/" + school.school_id + "/cloud", "PATCH", { requested: false });
  assert.equal((await beat()).body.cloud_requested, false);

  const second = (await app.register("server_2026_other_hk_654321")).body;
  const all = await app.owner("/api/v1/owner/cloud/request-all", "POST", {});
  assert.equal(all.body.updated, 2);
  assert.equal(app.sqlite.prepare("SELECT count(*) AS n FROM schools WHERE cloud_requested=1").get().n, 2);
  assert.ok(second.school_id);
  assert.ok((await app.owner("/api/v1/owner/audit")).body.history.some(h => h.action === "cloud_requested_all"));
});

test("Signature S3 conforme à l'exemple officiel AWS (SigV4)", async () => {
  const { signS3 } = await import("../src/storage.mjs");
  const headers = await signS3({ method: "GET", host: "examplebucket.s3.amazonaws.com", path: "/test.txt",
    headers: { range: "bytes=0-9" }, payloadHash: "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",
    accessKeyId: "AKIAIOSFODNN7EXAMPLE", secretAccessKey: "wJalrXUtnFEMI/K7MDENG/bPxRfiCYEXAMPLEKEY",
    region: "us-east-1", amzDate: "20130524T000000Z" });
  assert.match(headers.authorization, /Signature=f0e8bdb87c964420e857bd35b5d6ed310bd44f0170aba48dd91039c6036bdb41$/);
});
