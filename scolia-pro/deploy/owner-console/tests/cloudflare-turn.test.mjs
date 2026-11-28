import test from "node:test";
import assert from "node:assert/strict";
import { discoverCloudflareTurn } from "../src/cloudflare-turn.mjs";

const account = "0123456789abcdef0123456789abcdef";
const token = "f".repeat(48);
const keyId = "c".repeat(32);

test("Sans clé de découverte, aucune connexion sortante ni secret exposé", async () => {
  let called = 0;
  const result = await discoverCloudflareTurn({}, async () => { called++; });
  assert.equal(result.status, "configuration_required");
  assert.equal(result.configured, false);
  assert.equal(result.relays.length, 0);
  assert.equal(called, 0);
});

test("Découverte uniquement via Cloudflare API et jeton Calls Read serveur", async () => {
  let capturedUrl = "", capturedAuth = "";
  const inventory = await discoverCloudflareTurn({
    CLOUDFLARE_TURN_ACCOUNT_ID: account,
    CLOUDFLARE_TURN_DISCOVERY_TOKEN: token
  }, async (url, options) => {
    capturedUrl = url;
    capturedAuth = options.headers.Authorization;
    assert.equal(options.method, "GET");
    assert.equal(options.redirect, "manual");
    return new Response(JSON.stringify({
      success: true,
      result: [{ uid: keyId, name: "SCOLIA RDC Principal",
        key: "LA_CLE_SECRETE_NE_DOIT_PAS_SORTIR", credential: "SECRET" }]
    }), { status: 200 });
  });
  assert.equal(capturedUrl,
    `https://api.cloudflare.com/client/v4/accounts/${account}/calls/turn_keys`);
  assert.equal(capturedAuth, "Bearer " + token);
  assert.equal(inventory.status, "available");
  assert.equal(inventory.relays[0].label, "SCOLIA RDC Principal");
  assert.equal(inventory.relays[0].transport, "webrtc_ice");
  assert.equal(inventory.relays[0].credentials, "credentials_short_lived_required");
  assert.equal(inventory.relays[0].configured_for_school, false);
  assert.equal(inventory.relays[0].available_urls[0], "stun:stun.cloudflare.com:3478");
  const exposed = JSON.stringify(inventory);
  assert.ok(!exposed.includes(token));
  assert.ok(!exposed.includes("LA_CLE_SECRETE_NE_DOIT_PAS_SORTIR"));
  assert.ok(!exposed.includes("credential\":\"SECRET"));
});

test("Échec d'autorisation ou Cloudflare indisponible ne supprime aucun relais manuel", async () => {
  const env = {
    CLOUDFLARE_TURN_ACCOUNT_ID: account,
    CLOUDFLARE_TURN_DISCOVERY_TOKEN: token
  };
  const forbidden = await discoverCloudflareTurn(env, async () => new Response("Non autorisé", { status: 403 }));
  assert.equal(forbidden.status, "read_permission_required");
  assert.deepEqual(forbidden.relays, []);
  const down = await discoverCloudflareTurn(env, async () => { throw new Error("réseau indisponible"); });
  assert.equal(down.status, "network_unavailable");
  assert.deepEqual(down.relays, []);
});

test("Réponse contaminée filtrée et URLs TURN distinctes de WSS", async () => {
  const result = await discoverCloudflareTurn({
    CLOUDFLARE_TURN_ACCOUNT_ID: account,
    CLOUDFLARE_TURN_DISCOVERY_TOKEN: token
  }, async () => new Response(JSON.stringify({ success: true, result: [
    {uid:"pas-un-identifiant", name:"Invalide"},
    {uid:keyId, name:"Relais\nSécurisé"}
  ] }), {status:200}));
  assert.equal(result.relays.length, 1);
  assert.equal(result.relays[0].label, "Relais Sécurisé");
  assert.ok(result.relays[0].available_urls.every(u => !u.startsWith("wss://")));
});

test("Redirection Cloudflare ne transfère jamais le Bearer vers une autre origine", async () => {
  let hops = 0;
  const outcome = await discoverCloudflareTurn({
    CLOUDFLARE_TURN_ACCOUNT_ID: account,
    CLOUDFLARE_TURN_DISCOVERY_TOKEN: token
  }, async (_url, options) => {
    hops++;
    assert.equal(options.redirect, "manual");
    return new Response(null, {
      status: 302,
      headers: { Location: "https://example.org/collect" }
    });
  });
  assert.equal(hops, 1);
  assert.equal(outcome.status, "unexpected_redirect");
  assert.deepEqual(outcome.relays, []);
  assert.ok(!JSON.stringify(outcome).includes(token));
});
