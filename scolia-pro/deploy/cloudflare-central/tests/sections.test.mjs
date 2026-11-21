import { test } from "node:test";
import assert from "node:assert/strict";
import { webcrypto } from "node:crypto";
import { handleSchoolSections } from "../src/sections.mjs";
import { handleRequest } from "../src/index.mjs";

if (!globalThis.crypto) globalThis.crypto = webcrypto;
const sectionId = "d561b4c0-f5ac-4ad5-9d6d-b2ec34a41101";
const opId = "3ca2b5a4-770d-4f21-a857-4f5f0a1a1c6b";
const user = { school_id: "school-a", sub: "secretary", device_id: "pc-1", role: "secretariat" };
const post = {
  operation_id: opId, section_id: sectionId,
  code: "INF", name: "Sciences infirmières",
  is_active: true, expected_revision: 0,
};
const hash = async value => Buffer.from(await crypto.subtle.digest("SHA-256",
  new TextEncoder().encode(JSON.stringify(value)))).toString("hex");

function fakeD1() {
  const operations = new Map(), events = [], calls = [];
  let version = 0;
  const key = (school, operation) => school + "|" + operation;
  const DB = {
    prepare(sql) {
      return {
        bind(...params) {
          calls.push({ sql, params });
          return {
            async first() {
              if (!sql.startsWith("SELECT revision,request_sha256")) throw Error("unexpected first");
              const [school, operation, actor, device] = params;
              const event = operations.get(key(school, operation));
              return event?.actor_user_id === actor && event?.device_id === device ? event : null;
            },
            async all() {
              if (!sql.includes("FROM cloud_section_events")) throw Error("unexpected all");
              const [school, after, limit] = params;
              return { results: events.filter(e => e.school_id === school &&
                e.revision > after).sort((a,b)=>a.revision-b.revision).slice(0,limit) };
            },
            async run() {
              if (!sql.startsWith("INSERT INTO cloud_section_events")) throw Error("unexpected run");
              const [school, operation, section, actor, device,
                code, name, active, expected, request_sha256] = params;
              if (operations.has(key(school, operation))) throw Error("UNIQUE constraint failed");
              const prev = events.filter(e=>e.school_id===school && e.section_id===section)
                .at(-1)?.revision ?? 0;
              if (prev !== expected) throw Error("cloud_section_stale_revision");
              const event = { revision: ++version, school_id: school, section_id: section,
                operation_id: operation, actor_user_id: actor, device_id: device,
                code, name, is_active: active, request_sha256, created_at: 123 };
              operations.set(key(school, operation), event);
              events.push(event);
              return { meta: { changes: 1 } };
            },
          };
        },
      };
    },
  };
  return { DB, calls, events, operations };
}

async function call(db, method, pathname, body = undefined, actor = user) {
  const options = { method, headers: new Headers() };
  if (body) {
    options.headers.set("Content-Type", "application/json");
    options.body = JSON.stringify(body);
  }
  return handleSchoolSections(new Request("https://staging.test" + pathname, options),
    { DB: db }, actor);
}

test("création d'une section D1 puis rejouement de la même clé sans doublon", async () => {
  const { DB, events, calls } = fakeD1();
  const first = await call(DB, "POST", "/v1/catalog/sections", post);
  assert.equal(first.status, 201);
  assert.deepEqual(await first.json(), {
    operation_id: opId, section_id: sectionId,
    status: "applied", revision: 1, replayed: false,
  });
  const again = await call(DB, "POST", "/v1/catalog/sections", post);
  assert.equal(again.status, 200);
  assert.equal((await again.json()).replayed, true);
  assert.equal(events.length, 1);
  assert.equal(calls.find(x => x.sql.startsWith("INSERT INTO cloud_section_events")).params[0], "school-a");
});

test("un même op-id avec autre contenu ou révision périmée est rejeté", async () => {
  const { DB, events } = fakeD1();
  await call(DB, "POST", "/v1/catalog/sections", post);
  const conflict = await call(DB, "POST", "/v1/catalog/sections", { ...post, name: "Autre nom" });
  assert.equal(conflict.status, 409);
  assert.equal((await conflict.json()).error, "operation_id_already_used");
  const other = await call(DB, "POST", "/v1/catalog/sections", {
    ...post, operation_id: "91e36a53-69b0-41f1-8b25-e9e99bdf77a1", name: "Autre nom",
  });
  assert.equal(other.status, 409);
  assert.equal((await other.json()).error, "stale_section_revision");
  assert.equal(events.length, 1);
});

test("lecture du catalogue par école, y compris enseignant en lecture seule", async () => {
  const { DB, calls, events } = fakeD1();
  await call(DB, "POST", "/v1/catalog/sections", post);
  const teacher = { school_id: "school-a", sub: "teacher", device_id: "phone",
    role: "enseignant" };
  const allowed = await call(DB, "GET",
    "/v1/catalog/sections?school_id=school-b&after=0&limit=20", undefined, teacher);
  assert.equal(allowed.status, 200);
  const result = await allowed.json();
  assert.equal(result.events.length, 1);
  assert.equal(result.events[0].code, "INF");
  assert.equal(result.next, 1);
  assert.deepEqual(calls.at(-1).params, ["school-a", 0, 20]);

  const foreign = await call(DB, "GET",
    "/v1/catalog/sections?after=0", undefined, { ...teacher, school_id: "school-b" });
  assert.deepEqual((await foreign.json()).events, []);
  assert.equal(events.length, 1);
});

test("un enseignant ne peut jamais créer ou modifier une section", async () => {
  const { DB, calls } = fakeD1();
  const response = await call(DB, "POST", "/v1/catalog/sections", post, {
    ...user, role: "enseignant",
  });
  assert.equal(response.status, 403);
  assert.equal(calls.length, 0);
});

test("la validation rejette mutations malformées et limite les curseurs", async () => {
  const { DB, calls } = fakeD1();
  for (const input of [
    { ...post, code: "inf" }, { ...post, name: "Trop long".repeat(40) },
    { ...post, name: "Nom\ninvalide" }, { ...post, expected_revision: -1 },
    { ...post, is_active: "true" },
    { ...post, school_id: "school-b" }, { ...post, section_id: "random" },
    // Une chaîne de 36 caractères n'est pas nécessairement un UUID :
    // les clients .NET rejetteraient sinon tout le lot d'événements.
    { ...post, section_id: "a".repeat(36) },
    { ...post, operation_id: "a".repeat(36) },
    { ...post, section_id: "00000000-0000-0000-0000-000000000000" },
    { ...post, operation_id: "00000000-0000-0000-0000-000000000000" },
  ]) {
    const response = await call(DB, "POST", "/v1/catalog/sections", input);
    assert.equal(response.status, 400);
  }
  for (const path of [
    "/v1/catalog/sections?after=-1",
    "/v1/catalog/sections?limit=101",
    "/v1/catalog/sections?after=9007199254740993",
  ]) assert.equal((await call(DB, "GET", path)).status, 400);
  assert.equal(calls.length, 0);
});

test("les chemins inconnus et les routes d'écriture non authentifiées sont inaccessibles", async () => {
  assert.equal((await handleRequest(
    new Request("https://staging.test/v1/catalog/sections", { method: "POST",
      body: JSON.stringify(post) }), { DB: fakeD1().DB })).status, 503);
});

test("empreinte déterministe n'expose aucun secret et inclut la version attendue", async () => {
  const { DB, operations } = fakeD1();
  await call(DB, "POST", "/v1/catalog/sections", post);
  const record = operations.get("school-a|" + opId);
  assert.equal(record.request_sha256, await hash({
    section_id: sectionId, code: "INF", name: "Sciences infirmières",
    is_active: 1, expected_revision: 0,
  }));
  assert.equal(record.request_sha256.length, 64);
});
