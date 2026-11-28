import test from "node:test";
import assert from "node:assert/strict";
import { createHash, randomBytes } from "node:crypto";
import { DatabaseSync } from "node:sqlite";
import { readFileSync } from "node:fs";
import worker from "../src/index.mjs";

// D1-like SQLite test binding. No real Cloudflare account, secrets or students.
function adapter(sqlite) {
  const stmt = (sql, args = []) => ({
    bind: (...values) => stmt(sql, values),
    first: async () => sqlite.prepare(sql).get(...args) ?? null,
    all: async () => ({ results: sqlite.prepare(sql).all(...args) }),
    run: async () => ({ meta: sqlite.prepare(sql).run(...args) }),
  });
  return { prepare: sql => stmt(sql) };
}
function setup() {
  const owner = new DatabaseSync(":memory:");
  const schools = new DatabaseSync(":memory:");
  owner.exec(`CREATE TABLE schools (id TEXT PRIMARY KEY);
    CREATE TABLE school_tokens (school_id TEXT, token_hash TEXT, revoked_at INTEGER);
    CREATE TABLE school_cloud (school_id TEXT PRIMARY KEY, enabled INTEGER, key_fingerprint TEXT);`);
  schools.exec(readFileSync(new URL("../school-migrations/0001_school_replica.sql", import.meta.url), "utf8"));
  const schoolId = "123e4567-e89b-42d3-a456-426614174001";
  const secret = "school-valid-opaque-token-" + randomBytes(20).toString("hex");
  const keyFingerprint = "a".repeat(32);
  owner.prepare("INSERT INTO schools (id) VALUES (?)").run(schoolId);
  owner.prepare("INSERT INTO school_tokens VALUES (?,?,NULL)")
    .run(schoolId, createHash("sha256").update(secret).digest("hex"));
  owner.prepare("INSERT INTO school_cloud VALUES (?,?,?)").run(schoolId, 1, keyFingerprint);
  const env = { OWNER_DB: adapter(owner), SCHOOL_DB: adapter(schools) };
  const endpoint = "https://scolia-pro-owner.example.org";
  async function send(path, method = "GET", input = null, token = secret) {
    const response = await worker.fetch(new Request(endpoint + path, {
      method,
      headers: { ...(token ? { Authorization: "Bearer " + token } : {}),
        ...(input ? { "Content-Type": "application/json" } : {}) },
      body: input ? JSON.stringify(input) : undefined,
    }), env);
    return { status: response.status, body: await response.json() };
  }
  return { owner, schools, schoolId, secret, keyFingerprint, env, send,
    close: () => { owner.close(); schools.close(); } };
}
const prefix = "/api/v1/server/cloud/mirror/";
const hex = bytes => createHash("sha256").update(bytes).digest("hex");
function frame() {
  const encrypted = Buffer.concat([Buffer.from("SCOLIA-NUAGE-1"),randomBytes(100)]);
  return { frame_id: randomBytes(16).toString("hex"),
    stream_id: randomBytes(32).toString("hex"), revision: 1,
    ciphertext: encrypted.toString("base64url"), cipher_sha256: hex(encrypted) };
}

test("School D1 pairing requires existing school-server auth and explicit consent", async t => {
  const s = setup(); t.after(s.close);
  const data = { installation_hash: "b".repeat(64),
    key_fingerprint: s.keyFingerprint, consent_confirmed: true };
  assert.equal((await s.send(prefix+"pair","POST",data,null)).status, 401);
  assert.equal((await s.send(prefix+"pair","POST",data,"not-authorized")).status, 401);
  assert.equal((await s.send(prefix+"pair","POST",{...data,consent_confirmed:false})).status,400);
  assert.equal((await s.send(prefix+"pair","POST",{...data,key_fingerprint:"c".repeat(32)})).status,400);
  const paired = await s.send(prefix+"pair","POST",data);
  assert.equal(paired.status,200);
  assert.equal(paired.body.paired,true);
  assert.equal(paired.body.cloud_authoritative,false);
  assert.equal((await s.send(prefix+"pair","POST",data)).status,200);
  assert.equal((await s.send(prefix+"pair","POST",{...data,installation_hash:"d".repeat(64)})).status,409);
});

test("School D1 ciphertext frames are idempotent, scoped and do not expose school records", async t => {
  const s = setup(); t.after(s.close);
  const pair = {installation_hash:"b".repeat(64),key_fingerprint:s.keyFingerprint,consent_confirmed:true};
  assert.equal((await s.send(prefix+"frames","POST",frame())).status,409); // no pairing
  await s.send(prefix+"pair","POST",pair);
  const item = frame();
  assert.equal((await s.send(prefix+"frames","POST",{...item,cipher_sha256:"e".repeat(64)})).status,422);
  assert.equal((await s.send(prefix+"frames","POST",{...item,ciphertext:"PLAINTEXT"})).status,422);
  const inserted = await s.send(prefix+"frames","POST",item);
  assert.equal(inserted.status,201);
  assert.equal((await s.send(prefix+"frames","POST",item)).body.duplicate,true);
  const conflict = await s.send(prefix+"frames","POST",{...frame(),stream_id:item.stream_id,revision:item.revision});
  assert.equal(conflict.status,409);
  const data = await s.send(prefix+"frames?after=0&limit=20");
  assert.equal(data.status,200);
  assert.equal(data.body.frames.length,1);
  assert.equal(data.body.frames[0].ciphertext,item.ciphertext);
  assert.equal((await s.send(prefix+"frames?after=99999")).body.frames.length,0);
  assert.equal((await s.send(prefix+"frames?after=-10")).status,400);
  assert.equal((await s.send(prefix+"status")).body.frames,1);
  assert.equal((await s.send(prefix+"frames", "GET",null,null)).status,401);
  const raw = s.schools.prepare("SELECT ciphertext FROM school_mirror_frames").get().ciphertext;
  assert.equal(raw,item.ciphertext);
  assert.ok(!raw.includes("identite-eleve"));
  s.owner.prepare("UPDATE school_cloud SET enabled=0 WHERE school_id=?").run(s.schoolId);
  assert.equal((await s.send(prefix+"frames?after=0")).status,409);
});

test("School mirror is unavailable until separate SCHOOL_DB binding exists", async t => {
  const s=setup(); t.after(s.close);
  delete s.env.SCHOOL_DB;
  const result=await s.send(prefix+"pair","POST",{installation_hash:"b".repeat(64),
    key_fingerprint:s.keyFingerprint,consent_confirmed:true});
  assert.equal(result.status,503);
});
