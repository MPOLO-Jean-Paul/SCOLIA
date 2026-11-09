// Production-only configuration. Never commit generated wrangler.production.json.
// Keep the public school API separate from the Cloudflare Access protected /console
// and /api/v1/owner/* paths.
export const DB_NAME = "scolia-pro-owner";
export const SCHOOL_DB_NAME = "scolia-pro-school-sync";
// Sauvegardes chiffrées par les écoles (sauvegarde en ligne) : un seul bucket R2 privé.
export const BUCKET_NAME = "scolia-pro-backups";
const UUID = /^[a-f0-9]{8}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{12}$/i;
const DOMAIN = /^(?:[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?\.)+[a-z]{2,63}$/i;
// Le seul hostname workers.dev supporté est celui de CE Worker. Aucun routage global du compte.
const FREE_WORKER_HOST = /^scolia-pro-owner\.[a-z0-9-]+\.workers\.dev$/i;

export function buildProductionConfig({databaseId, schoolDatabaseId, domain, audience, turnAccountId}) {
  if (!UUID.test(databaseId ?? "") || /^0+$/.test(databaseId.replace(/-/g, "")))
    throw new Error("Identifiant Cloudflare D1 réel requis.");
  if (schoolDatabaseId !== undefined && (!UUID.test(schoolDatabaseId) ||
      /^0+$/.test(schoolDatabaseId.replace(/-/g, "")) || schoolDatabaseId === databaseId))
    throw new Error("Deuxieme D1 scolaire distincte obligatoire.");
  if (!DOMAIN.test(domain ?? "") ||
      /(?:^|\.)example\.(?:com|net|org)$|\.(?:test|invalid|local|localhost)$/i.test(domain))
    throw new Error("Domaine HTTPS Cloudflare réel requis.");
  if (domain?.toLowerCase().endsWith(".workers.dev") && !FREE_WORKER_HOST.test(domain))
    throw new Error("Le hostname workers.dev doit correspondre exactement au Worker scolia-pro-owner.");
  if (typeof audience !== "string" || !/^[a-zA-Z0-9_-]{20,128}$/.test(audience))
    throw new Error("Cloudflare Access AUD valide requis.");
  if (turnAccountId !== undefined && !/^[a-f0-9]{32}$/i.test(turnAccountId))
    throw new Error("Identifiant de compte Cloudflare Realtime invalide.");
  const freeWorkersDev = FREE_WORKER_HOST.test(domain);
  return {
    $schema: "node_modules/wrangler/config-schema.json",
    name: "scolia-pro-owner",
    main: "src/index.mjs",
    compatibility_date: "2026-11-08",
    // workers.dev = mise en service pilote. Un domaine propriétaire reste préférable en production.
    workers_dev: freeWorkersDev,
    ...(freeWorkersDev ? {} : { routes: [{ pattern: domain, custom_domain: true }] }),
    triggers: { crons: ["0 3 * * *"] },
    vars: { OWNER_ACCESS_AUD: audience,
      ...(turnAccountId ? { CLOUDFLARE_TURN_ACCOUNT_ID: turnAccountId } : {}) },
    observability: { enabled: true },
    d1_databases: [{
      binding: "OWNER_DB", database_name: DB_NAME,
      database_id: databaseId, migrations_dir: "migrations"
    }, ...(schoolDatabaseId ? [{
      binding: "SCHOOL_DB", database_name: SCHOOL_DB_NAME,
      database_id: schoolDatabaseId, migrations_dir: "school-migrations"
    }] : [])],
    r2_buckets: [{ binding: "BACKUPS", bucket_name: BUCKET_NAME }]
  };
}

export async function provisionDatabase({accountId, apiToken, fetcher = fetch, name = DB_NAME}) {
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
  const listing = await call(endpoint + "?name=" + encodeURIComponent(name), {method: "GET"});
  const databases = (listing.result ?? []).filter(x => x.name === name);
  if (databases.length > 1) throw new Error("Plusieurs bases portent le même nom.");
  if (databases.length === 1) return databases[0].uuid;
  const created = await call(endpoint, {method:"POST", body:JSON.stringify({name})});
  if (!created.result?.uuid) throw new Error("Cloudflare D1 n'a pas renvoyé d'identifiant.");
  return created.result.uuid;
}

/** Bucket R2 des sauvegardes : réutilisé s'il existe, créé sinon ; jamais vidé ni remplacé. */
export async function provisionBucket({accountId, apiToken, fetcher = fetch}) {
  if (!/^[a-f0-9]{32}$/i.test(accountId ?? "") || !apiToken || apiToken.length < 20)
    throw new Error("Compte et jeton API Cloudflare absents ou invalides.");
  const endpoint = "https://api.cloudflare.com/client/v4/accounts/" + accountId + "/r2/buckets";
  async function call(url, init) {
    const response = await fetcher(url, {
      ...init,
      headers: { Authorization: "Bearer " + apiToken, "Content-Type": "application/json" },
      redirect: "error"
    });
    if (!response.ok) throw new Error("Cloudflare R2 : appel API refusé (" + response.status + ").");
    const data = await response.json();
    if (!data.success) throw new Error("Cloudflare R2 : opération non autorisée.");
    return data;
  }
  const listing = await call(endpoint + "?name_contains=" + encodeURIComponent(BUCKET_NAME), {method: "GET"});
  const buckets = (listing.result?.buckets ?? []).filter(x => x.name === BUCKET_NAME);
  if (buckets.length === 1) return BUCKET_NAME;
  await call(endpoint, {method: "POST", body: JSON.stringify({name: BUCKET_NAME})});
  return BUCKET_NAME;
}
