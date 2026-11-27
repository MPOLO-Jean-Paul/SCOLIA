import test from "node:test";
import assert from "node:assert/strict";
import { Script } from "node:vm";
import worker from "../src/index.mjs";

test("La console servie contient un script exécutable et tous ses champs", async () => {
  const audience = "audience-de-test-console-2026";
  const response = await worker.fetch(new Request("https://console.example.org/console"),
    { OWNER_DB: {}, OWNER_ACCESS_AUD: audience }, { access: { aud: audience } });
  assert.equal(response.status, 200);
  const page = await response.text();
  const script = /<script>([\s\S]*?)<\/script>/.exec(page)?.[1];
  assert.ok(script);
  assert.doesNotThrow(() => new Script(script));
  const defined = new Set([...page.matchAll(/id="([^"]+)"/g)].map(match => match[1]));
  for (const [, name] of script.matchAll(/\$\("([^"]+)"\)/g)) assert.ok(defined.has(name), name);
});
