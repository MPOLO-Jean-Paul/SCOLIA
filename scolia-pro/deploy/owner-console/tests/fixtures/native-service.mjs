// Vrai Worker et schéma D1 en mémoire ; accès Cloudflare simulé uniquement sur l'adresse de boucle locale.
// Identifiants publics et fictifs, aucune infrastructure ni donnée d'établissement utilisée.
import { createServer } from "node:http";
import { DatabaseSync } from "node:sqlite";
import { readFileSync, readdirSync } from "node:fs";
import worker from "../../src/index.mjs";
import { CONSENT_VERSION } from "../../src/policy.mjs";

const db = new DatabaseSync(":memory:");
db.exec("PRAGMA foreign_keys=ON");
for (const name of readdirSync(new URL("../../migrations/", import.meta.url)).filter(n => n.endsWith(".sql")).sort())
  db.exec(readFileSync(new URL("../../migrations/" + name, import.meta.url), "utf8"));
function statement(sql, values = []) {
  return {
    bind: (...parameters) => statement(sql, parameters),
    first: async () => db.prepare(sql).get(...values) ?? null,
    all: async () => ({ results: db.prepare(sql).all(...values) }),
    run: async () => ({ meta: db.prepare(sql).run(...values) })
  };
}
let queue = Promise.resolve();
const database = {
  prepare: statement,
  batch: statements => {
    const work = queue.then(async () => {
      db.exec("BEGIN IMMEDIATE");
      try {
        const result = [];
        for (const command of statements) result.push(await command.run());
        db.exec("COMMIT");
        return result;
      } catch (error) { db.exec("ROLLBACK"); throw error; }
    });
    queue = work.catch(() => {});
    return work;
  }
};
const env = { OWNER_DB: database, OWNER_ADMIN_TOKEN: "owner-console-integration-fictif-2026", OWNER_ACCESS_AUD: "owner-console-integration-audience-2026" };
// Réponses fournisseurs strictement locales : les tests ne contactent ni R2, ni TURN, ni Netlify.
const objects = new Map();
env.STORAGE_FETCH = async (input, init) => {
  const key = new URL(input).pathname;
  if (init.method === "PUT") { objects.set(key, new Uint8Array(init.body)); return new Response(null, { status: 200 }); }
  if (init.method === "DELETE") { objects.delete(key); return new Response(null, { status: 204 }); }
  return objects.has(key) ? new Response(objects.get(key)) : new Response(null, { status: 404 });
};
env.CLOUDFLARE_TURN_ACCOUNT_ID = "a".repeat(32);
env.CLOUDFLARE_TURN_DISCOVERY_TOKEN = "turn-decouverte-fictif-2026";
env.TURN_DISCOVERY_FETCH = async () => Response.json({ success: true,
  result: [{ uid: "b".repeat(32), name: "TURN Démonstration JP-Services" }] });
env.NETLIFY_HEALTH_FETCH = async () => Response.json({ service: "SCOLIA Netlify HTTPS Backup",
  protocol: "scolia-https-mailbox-v1", transport: "https", enabled: false });
const access = { access: { aud: env.OWNER_ACCESS_AUD } };
async function owner(path, body) {
  const response = await worker.fetch(new Request("https://console.example.org" + path, {
    method: body ? "POST" : "GET", headers: { Authorization: "Bearer " + env.OWNER_ADMIN_TOKEN, "Content-Type": "application/json" },
    body: body ? JSON.stringify(body) : undefined
  }), env, access);
  if (!response.ok) throw Error("Préparation de la console impossible : " + response.status);
  return response.json();
}
for (const [index, name] of ["École Démonstration JP-Services", "Institut Les Horizons"].entries()) {
  const invitation = await owner("/api/v1/owner/invitations", { school_name: name });
  const response = await worker.fetch(new Request("https://console.example.org/api/v1/register", {
    method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify({
      consent: true, consent_version: CONSENT_VERSION, school_name: name, invite_code: invitation.invite_code,
      server_device_name: "SERVEUR-DEMO-" + index, installation_id: "console_integration_2026_server_" + index
    })
  }), env, undefined);
  const school = await response.json();
  if (!response.ok) throw Error("Inscription de démonstration impossible.");
  db.prepare("UPDATE schools SET billing_enabled=1, paid_until=?, manual_override=?, override_reason=? WHERE id=?")
    .run(Date.now() + (index ? 40 : -4) * 86400000, index ? "none" : "read_only", index ? "" : "Blocage de démonstration", school.school_id);
}
await owner("/api/v1/owner/relays", { label: "Relais de démonstration", endpoint_wss: "wss://relais.example.org/" });

const server = createServer(async (input, output) => {
  try {
    const chunks = [];
    for await (const chunk of input) chunks.push(chunk);
    const headers = new Headers();
    for (const [key, value] of Object.entries(input.headers)) if (value !== undefined) headers.set(key, Array.isArray(value) ? value.join(",") : value);
    const authenticated = headers.get("x-scolia-test-access") === "1" ||
      headers.get("cf-access-client-id") === "abcd1234.access" && headers.get("cf-access-client-secret") === "service-secret-fictif-2026";
    const response = await worker.fetch(new Request("http://" + input.headers.host + input.url, {
      method: input.method, headers, body: input.method === "GET" || input.method === "HEAD" ? undefined : Buffer.concat(chunks)
    }), env, authenticated ? access : undefined);
    output.writeHead(response.status, Object.fromEntries(response.headers));
    output.end(Buffer.from(await response.arrayBuffer()));
  } catch (error) {
    console.error(error);
    output.writeHead(500, { "Content-Type": "application/json" });
    output.end(JSON.stringify({ error: "Erreur du service de test." }));
  }
});
server.listen(Number(process.env.SCOLIA_TEST_PORT ?? 0), "127.0.0.1", () => {
  console.log("http://127.0.0.1:" + server.address().port + "/");
});
process.on("SIGTERM", () => server.close(() => { db.close(); process.exit(); }));
