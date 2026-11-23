import test from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";

const read = name => JSON.parse(readFileSync(new URL("../" + name, import.meta.url), "utf8"));

test("Le relais gratuit a une configuration Wrangler disponible et un domaine explicite", () => {
  const standard = read("wrangler.jsonc");
  const production = read("wrangler.free.jsonc");
  assert.equal(production.name, standard.name);
  assert.equal(production.main, standard.main);
  assert.equal(production.compatibility_date, standard.compatibility_date);
  assert.deepEqual(production.durable_objects, standard.durable_objects);
  assert.deepEqual(production.migrations, standard.migrations);
  assert.equal(production.workers_dev, true);
  assert.deepEqual(production.routes, [{ pattern: "relay.jp-services.org", custom_domain: true }]);
  assert.equal("vars" in production, false, "Aucun secret ni configuration d'ecole dans le fichier");
});
