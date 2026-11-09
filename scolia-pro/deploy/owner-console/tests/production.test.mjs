import test from "node:test";
import assert from "node:assert/strict";
import { buildProductionConfig, provisionDatabase, DB_NAME, SCHOOL_DB_NAME } from "../src/production.mjs";

const uuid = "aaaaaaaa-bbbb-4ccc-8ddd-eeeeeeeeeeee";
const accountId = "0123456789abcdef0123456789abcdef";
const valid = { databaseId: uuid, domain: "licences.jpservices.cd",
  audience: "application-access-audience-2026" };

test("Configuration production : D1 réel, domaine Cloudflare, Access, workers.dev fermé", () => {
  const cfg = buildProductionConfig(valid);
  assert.equal(cfg.workers_dev, false);
  assert.equal(cfg.routes[0].pattern, valid.domain);
  assert.equal(cfg.routes[0].custom_domain, true);
  assert.equal(cfg.d1_databases[0].database_id, uuid);
  assert.equal(cfg.vars.OWNER_ACCESS_AUD, valid.audience);
  assert.equal(cfg.vars.CLOUDFLARE_TURN_ACCOUNT_ID, undefined);
});

test("Compte Cloudflare TURN injecté comme variable non secrète sans token", () => {
  const cfg = buildProductionConfig({...valid, turnAccountId: accountId});
  assert.equal(cfg.vars.CLOUDFLARE_TURN_ACCOUNT_ID, accountId);
  assert.ok(!Object.keys(cfg.vars).some(key => key.includes("TOKEN")));
  assert.throws(() => buildProductionConfig({...valid, turnAccountId: "mauvais"}));
});

test("Pilote : chemin public workers.dev activé seulement pour le Worker de la console", () => {
  const free = buildProductionConfig({...valid,
    domain:"scolia-pro-owner.jeanpaulmpolo242.workers.dev"});
  assert.equal(free.workers_dev, true);
  assert.equal(free.name, "scolia-pro-owner");
  assert.equal(free.routes, undefined);
  assert.equal(free.vars.OWNER_ACCESS_AUD, valid.audience);
});

test("Aucune URL de test ni D1 factice ne peut être déployé", () => {
  for (const invalid of [
    {...valid, databaseId: "00000000-0000-0000-0000-000000000000"},
    {...valid, databaseId: "wrong"},
    {...valid, domain: "console.example.org"},
    {...valid, domain: "localhost"},
    {...valid, domain: "https://licences.jpservices.cd"},
    {...valid, domain: "licence.jeanpaulmpolo242.workers.dev"},
    {...valid, domain: "scolia-pro-owner.workers.dev"},
    {...valid, audience: ""},
  ]) assert.throws(() => buildProductionConfig(invalid));
});

test("Provision D1 : réutiliser la base existante sans écraser les données", async () => {
  const requests = [];
  const fetcher = async (url, init) => {
    requests.push({url, method:init.method});
    return Response.json({success:true,result:[{name:DB_NAME,uuid}]});
  };
  const id = await provisionDatabase({accountId,apiToken:"token_de_test_au_moins_32_caracteres",fetcher});
  assert.equal(id,uuid);
  assert.deepEqual(requests.map(x=>x.method),["GET"]);
});

test("Provision D1 : créer uniquement lorsque la base n'existe pas", async () => {
  const methods = [];
  const fetcher = async (_url, init) => {
    methods.push(init.method);
    if (init.method === "GET") return Response.json({success:true,result:[]});
    assert.deepEqual(JSON.parse(init.body),{name:DB_NAME});
    return Response.json({success:true,result:{name:DB_NAME,uuid}});
  };
  assert.equal(await provisionDatabase({
    accountId,apiToken:"token_de_test_au_moins_32_caracteres",fetcher
  }),uuid);
  assert.deepEqual(methods,["GET","POST"]);
});

test("Provision D1 : refuser un compte ou une réponse non autorisée", async () => {
  await assert.rejects(provisionDatabase({accountId:"invalide",apiToken:"short"}));
  await assert.rejects(provisionDatabase({
    accountId,apiToken:"token_de_test_au_moins_32_caracteres",
    fetcher:async () => Response.json({success:false}, {status:403})
  }));
});

test("D1 scolaire est séparée de la D1 commerciale et les deux migrations sont isolées", () => {
  const secondary = "bbbbbbbb-cccc-4ddd-8eee-ffffffffffff";
  const cfg = buildProductionConfig({ ...valid, schoolDatabaseId: secondary });
  assert.equal(cfg.d1_databases.length, 2);
  assert.equal(cfg.d1_databases[0].binding, "OWNER_DB");
  assert.equal(cfg.d1_databases[1].binding, "SCHOOL_DB");
  assert.equal(cfg.d1_databases[1].database_name, SCHOOL_DB_NAME);
  assert.equal(cfg.d1_databases[1].migrations_dir, "school-migrations");
  assert.equal(cfg.d1_databases[1].database_id, secondary);
  assert.throws(() => buildProductionConfig({...valid,schoolDatabaseId:uuid}));
  assert.throws(() => buildProductionConfig({...valid,schoolDatabaseId:"00000000-0000-0000-0000-000000000000"}));
});

test("Provision D1 scolaire gratuite : réutiliser sa base dédiée sans modifier les licences", async () => {
  const secondary = "bbbbbbbb-cccc-4ddd-8eee-ffffffffffff";
  const calls = [];
  const fetcher = async (url, init) => {
    calls.push({url,method:init.method});
    return Response.json({ success:true, result:[{name:SCHOOL_DB_NAME,uuid:secondary}] });
  };
  assert.equal(await provisionDatabase({accountId,
    apiToken:"token_de_test_au_moins_32_caracteres",name:SCHOOL_DB_NAME,fetcher}),secondary);
  assert.deepEqual(calls.map(x=>x.method),["GET"]);
  assert.ok(calls[0].url.includes(encodeURIComponent(SCHOOL_DB_NAME)));
});
