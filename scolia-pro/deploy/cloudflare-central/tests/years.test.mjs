import { test } from "node:test";
import assert from "node:assert/strict";
import { webcrypto } from "node:crypto";
import { handleAcademicYears } from "../src/years.mjs";
import { handleRequest } from "../src/index.mjs";

if (!globalThis.crypto) globalThis.crypto = webcrypto;

const yearId = "5c2a9ccf-5425-460d-9026-f3e7611d1e65";
const opId = "471a2dcc-684d-40b8-a1e2-c1b50b62ec66";
const actor = { school_id: "school-a", sub: "secretary",
  device_id: "pc-1", role: "secretariat" };
const original = {
  operation_id: opId, academic_year_id: yearId,
  label: "2026-2027", start_date: "2026-09-01",
  end_date: "2027-07-31", status: "open", expected_revision: 0,
};

function fakeD1() {
  const events = [], ops = new Map(), calls = [];
  let counter = 0;
  const key = (school, id) => school + "|" + id;
  return {
    events, calls,
    DB: {
      prepare(sql) {
        return {
          bind(...params) {
            calls.push({ sql, params });
            return {
              async first() {
                if (!sql.startsWith("SELECT revision,request_sha256"))
                  throw Error("unexpected first");
                const [school, op, user, device] = params;
                const e = ops.get(key(school, op));
                return e && e.actor_user_id === user &&
                  e.device_id === device ? e : null;
              },
              async all() {
                if (!sql.includes("FROM cloud_academic_year_events"))
                  throw Error("unexpected all");
                const [school, after, limit] = params;
                return { results: events.filter(e => e.school_id === school &&
                  e.revision > after).slice(0, limit) };
              },
              async run() {
                if (!sql.startsWith("INSERT INTO cloud_academic_year_events"))
                  throw Error("unexpected run");
                const [school, op, year, user, device, label,
                  start, end, status, expected, hash] = params;
                if (ops.has(key(school, op)))
                  throw Error("UNIQUE constraint failed");
                const previous = events.filter(e => e.school_id === school &&
                  e.academic_year_id === year).at(-1);
                if ((previous?.revision ?? 0) !== expected)
                  throw Error("cloud_year_stale_revision");
                if (previous && previous.label !== label)
                  throw Error("cloud_year_identity_immutable");
                const record = {
                  school_id: school, operation_id: op, academic_year_id: year,
                  actor_user_id: user, device_id: device,
                  label, start_date: start, end_date: end, status,
                  revision: ++counter, request_sha256: hash, created_at: 123,
                };
                ops.set(key(school, op), record);
                events.push(record);
                return { meta: { changes: 1 } };
              },
            };
          },
        };
      },
    },
  };
}

function call(db, method, pathname, body = undefined, user = actor) {
  return handleAcademicYears(new Request("https://staging.test" + pathname, {
    method,
    headers: { "Content-Type": "application/json" },
    body: body === undefined ? undefined : JSON.stringify(body),
  }), { DB: db }, user);
}

test("creation puis rejeu de l'annee sans deux evenements", async () => {
  const f = fakeD1();
  const first = await call(f.DB, "POST", "/v1/catalog/years", original);
  assert.equal(first.status, 201);
  assert.deepEqual(await first.json(), {
    academic_year_id: yearId, operation_id: opId,
    status: "applied", revision: 1, replayed: false,
  });
  const replay = await call(f.DB, "POST", "/v1/catalog/years", original);
  assert.equal(replay.status, 200);
  assert.equal((await replay.json()).replayed, true);
  assert.equal(f.events.length, 1);
  assert.equal(f.calls.find(e => e.sql.startsWith("INSERT INTO")).params[0],
    "school-a");
});

test("version perimee et reuse de cle avec autre contenu bloques", async () => {
  const f = fakeD1();
  await call(f.DB, "POST", "/v1/catalog/years", original);
  const modified = await call(f.DB, "POST", "/v1/catalog/years",
    { ...original, status: "closed" });
  assert.equal(modified.status, 409);
  assert.equal((await modified.json()).error, "operation_id_already_used");
  const stale = await call(f.DB, "POST", "/v1/catalog/years", {
    ...original, operation_id: "034fc497-a19b-4a26-9f7c-6bbbc033ac05",
  });
  assert.equal(stale.status, 409);
  assert.equal((await stale.json()).error, "stale_academic_year_revision");
  assert.equal(f.events.length, 1);
});

test("changement de nom d'annee est interdit sans perdre l'ancien etat", async () => {
  const f = fakeD1();
  await call(f.DB, "POST", "/v1/catalog/years", original);
  const renamed = await call(f.DB, "POST", "/v1/catalog/years", {
    ...original, operation_id: "034fc497-a19b-4a26-9f7c-6bbbc033ac05",
    label: "2027-2028", start_date: "2027-09-01",
    end_date: "2028-07-31", expected_revision: 1,
  });
  assert.equal(renamed.status, 409);
  assert.equal((await renamed.json()).error, "academic_year_identity_immutable");
  assert.equal(f.events.length, 1);
});

test("validation des dates calendaires et de l'annee scolaire", async () => {
  const f = fakeD1();
  const bad = [
    { ...original, label: "2026-2026" },
    { ...original, label: "2026/2027" },
    { ...original, start_date: "2026-02-29" },
    { ...original, start_date: "2026-13-01" },
    { ...original, end_date: "2027-02-29" },
    { ...original, end_date: "2026-07-31" },
    { ...original, start_date: "2026-12-01", end_date: "2027-01-01", status: "archived" },
    { ...original, academic_year_id: "00000000-0000-0000-0000-000000000000" },
    { ...original, operation_id: "abc" },
    { ...original, expected_revision: -1 },
    { ...original, school_id: "school-b" },
    { ...original, is_current: true },
  ];
  for (const b of bad) assert.equal(
    (await call(f.DB, "POST", "/v1/catalog/years", b)).status, 400,
    JSON.stringify(b));
  assert.equal(f.calls.length, 0);
});

test("enseignement en lecture seulement, isolation et limites des curseurs", async () => {
  const f = fakeD1();
  const teacher = { ...actor, role: "enseignant", sub: "teacher" };
  const denied = await call(f.DB, "POST", "/v1/catalog/years", original, teacher);
  assert.equal(denied.status, 403);
  await call(f.DB, "POST", "/v1/catalog/years", original);
  const list = await call(f.DB, "GET", "/v1/catalog/years?after=0&limit=10",
    undefined, teacher);
  assert.equal(list.status, 200);
  assert.equal((await list.json()).events.length, 1);
  const foreign = await call(f.DB, "GET", "/v1/catalog/years?school_id=school-a",
    undefined, { ...teacher, school_id: "school-b" });
  assert.deepEqual((await foreign.json()).events, []);
  for (const p of ["/v1/catalog/years?after=-1",
                   "/v1/catalog/years?limit=101",
                   "/v1/catalog/years?after=9007199254740993"]) {
    assert.equal((await call(f.DB, "GET", p)).status, 400);
  }
});

test("une route de mutation n'est pas ouverte sans session", async () => {
  const res = await handleRequest(new Request(
    "https://staging.test/v1/catalog/years", {
      method: "POST", body: JSON.stringify(original),
    }), { DB: fakeD1().DB });
  assert.equal(res.status, 503);
  const wrongVerb = await handleRequest(
    new Request("https://staging.test/v1/catalog/years", { method: "PUT" }), {});
  assert.equal(wrongVerb.status, 404);
});
