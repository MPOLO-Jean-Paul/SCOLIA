// Production-only configuration. Never commit generated wrangler.production.json.
// Keep the public school API separate from the Cloudflare Access protected /console
// and /api/v1/owner/* paths.
export const DB_NAME = "scolia-pro-owner";
const UUID = /^[a-f0-9]{8}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{12}$/i;
const DOMAIN = /^(?:[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?\.)+[a-z]{2,63}$/i;

export function buildProductionConfig({databaseId, domain, audience}) {
  if (!UUID.test(databaseId ?? "") || /^0+$/.test(databaseId.replace(/-/g, "")))
    throw new Error("Identifiant Cloudflare D1 réel requis.");
  if (!DOMAIN.test(domain ?? "") || /\.(?:test|example|invalid|local|localhost)$/i.test(domain))
    throw new Error("Domaine HTTPS Cloudflare réel requis.");
  if (typeof audience !== "string" || !/^[a-zA-Z0-9_-]{20,128}$/.test(audience))
    throw new Error("Cloudflare Access AUD valide requis.");
  return {
    $schema: "node_modules/wrangler/config-schema.json",
    name: "scolia-pro-owner",
    main: "src/index.mjs",
    compatibility_date: "2026-11-08",
    workers_dev: false,
    routes: [{ pattern: domain, custom_domain: true }],
    triggers: { crons: ["0 3 * * *"] },
    vars: { OWNER_ACCESS_AUD: audience },
    observability: { enabled: true },
    d1_databases: [{
      binding: "OWNER_DB", database_name: DB_NAME,
      database_id: databaseId, migrations_dir: "migrations"
    }]
  };
}

export async function provisionDatabase({accountId, apiToken, fetcher = fetch}) {
  if (!/^[a-f0-9]{32}$/i.test(accountId ?? "") || !apiToken || apiToken.length < 20)
    throw new Error("Compte et jeton API Cloudflare absents ou invalides.");
  const endpoint = "https://api.cloudflare.com/client/v4/accounts/" + accountId + "/d1/database";
  async function call(url, init) {
    const response = await fetcher(url, {
      ...init,
      headers: {
        Authorization: "Bearer " + apiToken,
        "Content-Type": "application/json"
      },
      redirect: "error"
    });
    if (!response.ok) throw new Error("Cloudflare D1 : appel API refusé (" + response.status + ").");
    const data = await response.json();
    if (!data.success) throw new Error("Cloudflare D1 : opération non autorisée.");
    return data;
  }
  const listing = await call(endpoint + "?name=" + encodeURIComponent(DB_NAME), {method: "GET"});
  const databases = (listing.result ?? []).filter(x => x.name === DB_NAME);
  if (databases.length > 1) throw new Error("Plusieurs bases portent le même nom.");
  if (databases.length === 1) return databases[0].uuid;
  const created = await call(endpoint, {method:"POST", body:JSON.stringify({name:DB_NAME})});
  if (!created.result?.uuid) throw new Error("Cloudflare D1 n'a pas renvoyé d'identifiant.");
  return created.result.uuid;
}
