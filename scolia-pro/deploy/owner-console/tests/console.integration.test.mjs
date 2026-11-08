import test from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { DatabaseSync } from "node:sqlite";
import worker from "../src/index.mjs";
import { CONSENT_VERSION } from "../src/policy.mjs";

const HOST = "https://console.example.org";
function setup() {
  // Adaptateur local reproduisant les requêtes et transactions D1,
  // sans Cloudflare, données réelles ou secrets externes.
  const sqlite = new DatabaseSync(":memory:");
  sqlite.exec("PRAGMA foreign_keys=ON");
  for (const name of ["0001_init.sql", "0002_relays.sql",
    "0003_activity_idempotence.sql", "0004_license_controls.sql", "0005_cloud_continuity.sql"]) {
    sqlite.exec(readFileSync(new URL("../migrations/" + name, import.meta.url), "utf8"));
  }
  function statement(sql, params = []) {
    return {
      bind: (...values) => statement(sql, values),
      first: async () => sqlite.prepare(sql).get(...params) ?? null,
      all: async () => ({ results: sqlite.prepare(sql).all(...params) }),
      run: async () => ({ meta: sqlite.prepare(sql).run(...params) }),
    };
  }
  let queue = Promise.resolve();
  const db = {
    prepare: sql => statement(sql),
    batch: statements => {
      // SQLite utilise une connexion mémoire commune dans ce test.
      // Sérialiser les transactions simule l'arbitre D1 réel.
      const work = queue.then(async () => {
        sqlite.exec("BEGIN IMMEDIATE");
        try {
          const results = [];
          for (const stmt of statements) results.push(await stmt.run());
          sqlite.exec("COMMIT");
          return results;
        } catch (error) {
          sqlite.exec("ROLLBACK");
          throw error;
        }
      });
      queue = work.catch(() => {});
      return work;
    },
  };
  const env = {
    OWNER_DB: db, OWNER_ADMIN_TOKEN: "proprietaire-secret-de-test",
    OWNER_ACCESS_AUD: "test-application-owner-audience-2026"
  };
  async function request(path, method = "GET", body = null, auth = null) {
    const response = await worker.fetch(new Request(HOST + path, {
      method,
      headers: {
        ...(auth ? { Authorization: "Bearer " + auth } : {}),
        ...(body ? { "Content-Type": "application/json" } : {}),
      },
      body: body ? JSON.stringify(body) : undefined,
    }), env, auth === env.OWNER_ADMIN_TOKEN
      ? { access: { aud: env.OWNER_ACCESS_AUD } } : undefined);
    return { status: response.status, body: await response.json() };
  }
  const owner = (path, method = "GET", body = null) =>
    request(path, method, body, env.OWNER_ADMIN_TOKEN);
  async function register() {
    const invitation = await owner("/api/v1/owner/invitations", "POST",
      { school_name: "ITM Cœur de Dieu" });
    assert.equal(invitation.status, 201);
    const payload = {
      consent: true, consent_version: CONSENT_VERSION,
      school_name: "ITM Cœur de Dieu", server_device_name: "PC SECRETARIAT",
      installation_id: "server_2026_eastern_hk_123456", invite_code: invitation.body.invite_code,
    };
    const school = await request("/api/v1/register", "POST", payload);
    assert.equal(school.status, 201);
    return { school, payload };
  }
  return { sqlite, db, env, request, owner, register };
}

test("Invitation à usage unique et compte créé sans enregistrement partiel", async t => {
  const app = setup();
  t.after(() => app.sqlite.close());
  const invitation = await app.owner("/api/v1/owner/invitations", "POST",
    { school_name: "ITM Cœur de Dieu" });
  const input = {
    consent: true, consent_version: CONSENT_VERSION, school_name: "ITM Cœur de Dieu",
    server_device_name: "SCOLIA SERVEUR", installation_id: "server_2026_pro_hk_xyz123",
    invite_code: invitation.body.invite_code,
  };
  const wrongSchool = await app.request("/api/v1/register", "POST",
    {...input, school_name: "Ecole non invitée"});
  assert.equal(wrongSchool.status, 409);
  // La tentative refusée ne doit pas consommer l'invitation officielle.
  const [first, second] = await Promise.all([
    app.request("/api/v1/register", "POST", input),
    app.request("/api/v1/register", "POST", input),
  ]);
  assert.deepEqual([first.status, second.status].sort(), [201, 403]);
  assert.equal(app.sqlite.prepare("SELECT count(*) AS total FROM schools").get().total, 1);
  assert.equal(app.sqlite.prepare("SELECT count(*) AS total FROM school_tokens").get().total, 1);
  assert.equal(app.sqlite.prepare("SELECT count(*) AS total FROM server_devices").get().total, 1);
  assert.equal(app.sqlite.prepare("SELECT count(*) AS total FROM owner_invites WHERE used_at IS NOT NULL").get().total, 1);
});

test("L'activité répétée ne produit qu'un seul comptage, même après reprise", async t => {
  const app = setup();
  t.after(() => app.sqlite.close());
  const {school,payload} = await app.register();
  const item = {
    event_id: "3e6d0617-9386-4b5a-984c-5f6de2e41dc9",
    event: "login", installation_id: payload.installation_id,
    server_device_name: payload.server_device_name, occurred_at: new Date().toISOString(),
  };
  for (let i = 0; i < 3; i++) {
    const res = await app.request("/api/v1/heartbeat", "POST", item, school.body.school_token);
    assert.equal(res.status, 200);
    assert.equal(res.body.school_id, school.body.school_id);
  }
  const totals = app.sqlite.prepare("SELECT sum(login_count) AS logins,sum(heartbeat_count) AS heartbeats FROM school_activity").get();
  assert.equal(totals.logins, 1);
  assert.equal(totals.heartbeats, 1);
  assert.equal(app.sqlite.prepare("SELECT count(*) AS total FROM school_activity_events").get().total, 1);
});

test("Statistiques fondées sur la licence effective et ancien endpoint refusé", async t => {
  const app = setup();
  t.after(() => app.sqlite.close());
  const {school} = await app.register();
  const id = school.body.school_id;
  const path = "/api/v1/owner/schools/" + id;
  let overview = await app.owner("/api/v1/owner/summary");
  assert.equal(overview.body.suspended, 0);
  const policy = await app.owner(path + "/license", "PATCH",
    { billing_enabled: true, trial_days: 0 });
  assert.equal(policy.status, 200);
  assert.equal(policy.body.license.readonly, true);
  overview = await app.owner("/api/v1/owner/summary");
  assert.equal(overview.body.suspended, 1);
  const listing = await app.owner("/api/v1/owner/schools");
  assert.equal(listing.body.schools[0].license.license_status, "expired");
  const audit = await app.owner("/api/v1/owner/audit");
  assert.equal(audit.status, 200);
  assert.ok(audit.body.history.some(x => x.action === "license_policy_changed"));
  const noReason = await app.owner(path + "/license", "PATCH",
    { manual_override: "allow", override_until: Date.now() + 7*86400_000 });
  assert.equal(noReason.status, 400);
  const infinite = await app.owner(path + "/license", "PATCH",
    { manual_override: "allow", override_until: null, reason: "Dérogation" });
  assert.equal(infinite.status, 400);
  const old = await app.owner(path, "PATCH", {state:"active",license_expires_at:null});
  assert.equal(old.status, 410);
  const blocked = await app.owner(path + "/license", "PATCH",
    {manual_override:"read_only",reason:"Facture contestée"});
  assert.equal(blocked.status, 200);
  assert.equal(blocked.body.license.reason, "blocage_manuel");
  const restored = await app.owner(path + "/license", "PATCH",
    {billing_enabled:false,manual_override:"none",override_until:null,reason:"Fin de litige"});
  assert.equal(restored.body.license.readonly, false);
});

test("Désactiver un relais inactif ne coupe jamais le relais actif", async t => {
  const app = setup();
  t.after(() => app.sqlite.close());
  const {school} = await app.register();
  async function create(label, url) {
    const x = await app.owner("/api/v1/owner/relays", "POST",
      {label,endpoint_wss:url});
    assert.equal(x.status, 201);
    return x.body.id;
  }
  const first = await create("Relais A", "wss://relay-a.example.org/");
  const second = await create("Relais B", "wss://relay-b.example.org/");
  const patch = (id,state) => app.owner("/api/v1/owner/relays/" + id, "PATCH", {state});
  const a = await patch(first,"active");
  assert.equal(a.status, 200);
  await app.request("/api/v1/server/relay-consent", "POST",
    {enabled:true,disclosed_to_school_admin:true}, school.body.school_token);
  const approved = await app.request("/api/v1/server/relay-approve", "POST",
    {relay_revision:a.body.revision,confirm:true,disclosed_to_school_admin:true}, school.body.school_token);
  assert.equal(approved.status, 200);
  assert.equal((await app.request("/api/v1/server/relay-settings", "GET",null,school.body.school_token)).body.remote_relay_enabled,true);

  const unchanged = await patch(second,"disabled");
  assert.equal(unchanged.status, 200);
  let relays = (await app.owner("/api/v1/owner/relays")).body.relays;
  assert.equal(relays.find(x=>x.id===first).state,"active");
  assert.equal(relays.find(x=>x.id===second).state,"disabled");
  const b = await patch(second,"active");
  assert.ok(b.body.revision > a.body.revision);
  relays = (await app.owner("/api/v1/owner/relays")).body.relays;
  assert.equal(relays.find(x=>x.id===first).state,"disabled");
  assert.equal(relays.find(x=>x.id===second).state,"active");
  assert.equal((await app.request("/api/v1/server/relay-settings","GET",null,school.body.school_token)).body.remote_relay_enabled,false);
});

test("Accès propriétaire refusé sans secret et origine tierce bloquée", async t => {
  const app = setup();
  t.after(() => app.sqlite.close());
  assert.equal((await app.request("/api/v1/owner/summary")).status, 403);
  const noToken = await worker.fetch(new Request(HOST + "/api/v1/owner/summary"),
    app.env, { access: { aud: app.env.OWNER_ACCESS_AUD } });
  assert.equal(noToken.status, 401);
  const wrongAudience = await worker.fetch(new Request(HOST + "/console"), app.env,
    { access: { aud: "different-cloudflare-access-aud" } });
  assert.equal(wrongAudience.status, 403);
  const adminConsole = await worker.fetch(new Request(HOST + "/console"), app.env,
    { access: { aud: app.env.OWNER_ACCESS_AUD } });
  assert.equal(adminConsole.status, 200);
  const publicApi = await app.request("/api/v1/notice");
  assert.equal(publicApi.status, 200);
  const forbidden = await worker.fetch(new Request(HOST + "/api/v1/owner/summary", {
    headers: { Origin: "https://attacker.example", Authorization: "Bearer " + app.env.OWNER_ADMIN_TOKEN },
  }), app.env, { access: { aud: app.env.OWNER_ACCESS_AUD } });
  assert.equal(forbidden.status, 403);
});
