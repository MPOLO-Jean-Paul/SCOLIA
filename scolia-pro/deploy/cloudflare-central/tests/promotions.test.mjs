import { test } from "node:test";
import assert from "node:assert/strict";
import { webcrypto } from "node:crypto";
import { handleSchoolPromotions } from "../src/promotions.mjs";
import { handleRequest } from "../src/index.mjs";

if (!globalThis.crypto) globalThis.crypto = webcrypto;

const promotionId = "816be80f-0623-4104-887a-7ee6ac57ee8a";
const sectionId = "d561b4c0-f5ac-4ad5-9d6d-b2ec34a41101";
const yearId = "5c2a9ccf-5425-460d-9026-f3e7611d1e65";
const opId = "3ca2b5a4-770d-4f21-a857-4f5f0a1a1c6b";
const user = { school_id: "school-a", sub: "secretary", device_id: "pc-1",
  role: "secretariat" };
const original = {
  operation_id: opId, promotion_id: promotionId,
  academic_year_id: yearId, section_id: sectionId,
  code: "INF1", name: "Premiere infirmiere",
  is_active: true, expected_revision: 0,
};

function fakeDb() {
  const events = [], operations = new Map(), calls = [];
  const sections = new Set(["school-a|" + sectionId]);
  const years = new Set(["school-a|" + yearId]);
  let revision = 0;
  const key = (school, op) => school + "|" + op;
  return {
    events, operations, calls, sections, years,
    DB: {
      prepare(sql) {
        return {
          bind(...params) {
            calls.push({ sql, params });
            return {
              async first() {
                if (!sql.startsWith("SELECT revision,request_sha256"))
                  throw Error("unexpected first");
                const [school, operation, actor, device] = params;
                const row = operations.get(key(school, operation));
                return row && row.actor_user_id === actor &&
                  row.device_id === device ? row : null;
              },
              async all() {
                if (!sql.includes("FROM cloud_promotion_events"))
                  throw Error("unexpected all");
                const [school, after, limit] = params;
                return { results: events.filter(row => row.school_id === school &&
                  row.revision > after).slice(0, limit) };
              },
              async run() {
                if (!sql.startsWith("INSERT INTO cloud_promotion_events"))
                  throw Error("unexpected run");
                const [school, operation, promotion, year, section, actor, device,
                  code, name, active, expected, request_sha256] = params;
                if (operations.has(key(school, operation)))
                  throw Error("UNIQUE constraint failed");
                if (!years.has(school + "|" + year))
                  throw Error("cloud_promotion_invalid_year");
                if (section !== null && active === 1 &&
                    !sections.has(school + "|" + section))
                  throw Error("cloud_promotion_invalid_section");
                if (section !== null && !sections.has(school + "|" + section))
                  throw Error("FOREIGN KEY constraint failed");
                const previous = events.filter(row => row.school_id === school &&
                  row.promotion_id === promotion).at(-1)?.revision ?? 0;
                if (previous !== expected)
                  throw Error("cloud_promotion_stale_revision");
                const row = {
                  school_id: school, revision: ++revision,
                  operation_id: operation, promotion_id: promotion,
                  academic_year_id: year, section_id: section,
                  actor_user_id: actor, device_id: device, code, name,
                  is_active: active, request_sha256, created_at: 123,
                };
                events.push(row);
                operations.set(key(school, operation), row);
                return { meta: { changes: 1 } };
              },
            };
          },
        };
      },
    },
  };
}

async function call(db, method, url, body = undefined, actor = user) {
  return handleSchoolPromotions(new Request("https://staging.test" + url, {
    method, headers: { "Content-Type": "application/json" },
    body: body === undefined ? undefined : JSON.stringify(body),
  }), { DB: db }, actor);
}

test("creation puis rejouement idempotent sans double ecriture", async () => {
  const f = fakeDb();
  const first = await call(f.DB, "POST", "/v1/catalog/promotions", original);
  assert.equal(first.status, 201);
  assert.deepEqual(await first.json(), {
    operation_id: opId, promotion_id: promotionId, status: "applied",
    revision: 1, replayed: false,
  });
  const replay = await call(f.DB, "POST", "/v1/catalog/promotions", original);
  assert.equal(replay.status, 200);
  assert.equal((await replay.json()).replayed, true);
  assert.equal(f.events.length, 1);
  assert.equal(f.calls.find(c=>c.sql.startsWith("INSERT INTO")).params[0], "school-a");
});

test("reutilisation de cle avec contenu different et version stale refusees", async () => {
  const f = fakeDb();
  await call(f.DB, "POST", "/v1/catalog/promotions", original);
  const changed = await call(f.DB, "POST", "/v1/catalog/promotions",
    { ...original, name: "Autre" });
  assert.equal(changed.status, 409);
  assert.equal((await changed.json()).error, "operation_id_already_used");
  const stale = await call(f.DB, "POST", "/v1/catalog/promotions", {
    ...original, operation_id: "1109e235-4d45-4fb0-9b13-2b38842111a4",
  });
  assert.equal(stale.status, 409);
  assert.equal((await stale.json()).error, "stale_promotion_revision");
  assert.equal(f.events.length, 1);
});

test("lecture paginee isolant les ecoles et autorisee aux enseignants", async () => {
  const f = fakeDb();
  await call(f.DB, "POST", "/v1/catalog/promotions", original);
  const teacher = { ...user, role: "enseignant", sub: "teacher", device_id: "phone" };
  const result = await call(f.DB, "GET",
    "/v1/catalog/promotions?school_id=school-b&after=0&limit=10", undefined, teacher);
  assert.equal(result.status, 200);
  const catalog = await result.json();
  assert.equal(catalog.events.length, 1);
  assert.equal(catalog.events[0].section_id, sectionId);
  assert.deepEqual(f.calls.at(-1).params, ["school-a", 0, 10]);

  const foreign = await call(f.DB, "GET",
    "/v1/catalog/promotions", undefined, { ...teacher, school_id: "school-b" });
  assert.deepEqual((await foreign.json()).events, []);
});

test("ecriture interdite aux enseignants et sections etrangeres", async () => {
  const f = fakeDb();
  const teacher = await call(f.DB, "POST", "/v1/catalog/promotions",
    original, { ...user, role: "enseignant" });
  assert.equal(teacher.status, 403);
  assert.equal(f.calls.length, 0);
  const foreign = await call(f.DB, "POST", "/v1/catalog/promotions",
    { ...original, section_id: "a144401b-eb7c-44aa-a681-57ef9bd5d51c" });
  assert.equal(foreign.status, 409);
  assert.equal((await foreign.json()).error, "inactive_or_unknown_section");
  assert.equal(f.events.length, 0);
});

test("la section peut etre absente mais une annee valide est obligatoire", async () => {
  const f = fakeDb();
  const noSection = await call(f.DB, "POST", "/v1/catalog/promotions", {
    ...original, section_id: null,
  });
  assert.equal(noSection.status, 201);
  assert.equal(f.events[0].section_id, null);
  const otherYear = await call(f.DB, "POST", "/v1/catalog/promotions", {
    ...original, operation_id: "c016d4e0-18bd-4934-a951-918103cdb7b9",
    academic_year_id: "03c275f1-8112-46ba-9dc3-5f7bd72475cc",
  });
  assert.equal(otherYear.status, 409);
  assert.equal((await otherYear.json()).error, "closed_or_unknown_academic_year");
  assert.equal(f.events.length, 1);
});

test("validation stricte des IDs, champs et curseurs", async () => {
  const f = fakeDb();
  for (const wrong of [
    { ...original, code: "inf1" },
    { ...original, name: " Nom" },
    { ...original, name: "A".repeat(121) },
    { ...original, name: "Line\nBreak" },
    { ...original, promotion_id: "a".repeat(36) },
    { ...original, operation_id: "00000000-0000-0000-0000-000000000000" },
    { ...original, is_active: 1 },
    { ...original, expected_revision: -1 },
    { ...original, section_id: "school-b:section" },
    { ...original, academic_year_id: null },
    { ...original, school_id: "school-b" },
  ]) {
    const response = await call(f.DB, "POST", "/v1/catalog/promotions", wrong);
    assert.equal(response.status, 400);
  }
  for (const route of [
    "/v1/catalog/promotions?after=-1",
    "/v1/catalog/promotions?limit=101",
    "/v1/catalog/promotions?after=9007199254740993",
  ]) {
    assert.equal((await call(f.DB, "GET", route)).status, 400);
  }
  assert.equal(f.calls.length, 0);
});

test("pas de mutation sans authentification et routage inconnu ferme", async () => {
  const response = await handleRequest(new Request(
    "https://staging.test/v1/catalog/promotions", {
      method: "POST", body: JSON.stringify(original),
    }), { DB: fakeDb().DB });
  assert.equal(response.status, 503);
  const unknown = await handleRequest(new Request(
    "https://staging.test/v1/catalog/promotions", { method: "PUT" }), {});
  assert.equal(unknown.status, 404);
});
