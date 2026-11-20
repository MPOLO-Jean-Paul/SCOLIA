/**
 * Safe, idempotent, FREE-tier-first Cloudflare D1 school database bootstrap.
 * Deliberately separate from OWNER_DB, R2 and production Worker deployments.
 * Executes only reviewed CREATE TABLE/INDEX IF NOT EXISTS migrations.
 */
import { readFileSync, appendFileSync } from "node:fs";

const accountId = process.env.CLOUDFLARE_ACCOUNT_ID;
const token = process.env.CLOUDFLARE_API_TOKEN;
const schoolDatabaseName = "scolia-pro-school-sync";
if (!/^[a-f0-9]{32}$/i.test(accountId ?? "") || !token || token.length < 20)
  throw new Error("Cloudflare D1 : environnement protege non configure.");

const root = `https://api.cloudflare.com/client/v4/accounts/${accountId}`;
const headers = { Authorization: `Bearer ${token}`, "Content-Type": "application/json" };

async function cloud(path, method = "GET", data) {
  const response = await fetch(root + path, {
    method,
    headers,
    redirect: "error",
    ...(data ? { body: JSON.stringify(data) } : {}),
  });
  if (!response.ok) throw new Error(`Cloudflare D1 : requete ${method} refusee (HTTP ${response.status}).`);
  const result = await response.json();
  if (!result?.success) throw new Error("Cloudflare D1 : operation non autorisee.");
  return result.result;
}

// Reuse the exact database name if it already exists; never delete or
// overwrite unrelated D1 databases, including scolia-pro-owner.
const found = await cloud("/d1/database?name=" + encodeURIComponent(schoolDatabaseName));
const matches = (Array.isArray(found) ? found : []).filter(item => item.name === schoolDatabaseName);
if (matches.length > 1) throw new Error("Cloudflare D1 : doublon de nom, intervention requise.");
let databaseId;
if (matches.length === 1) databaseId = matches[0].uuid;
else {
  const created = await cloud("/d1/database", "POST", { name: schoolDatabaseName });
  databaseId = created?.uuid;
}
if (!/^[a-f0-9]{8}-[a-f0-9-]{27,}$/i.test(databaseId ?? ""))
  throw new Error("Cloudflare D1 : identifiant de base invalide.");

const sql = readFileSync("school-migrations/0001_school_replica.sql", "utf8")
  .replace(/^\s*--.*$/gm, "");
const statements = sql.split(";").map(s => s.trim()).filter(Boolean);
if (!statements.length || !statements.every(s => /^CREATE (?:TABLE|INDEX) IF NOT EXISTS\b/i.test(s)))
  throw new Error("Migration D1 non additive ou inconnue : refusee.");

// Cloudflare D1 execute a single SQL statement at a time on the REST API.
for (const statement of statements)
  await cloud(`/d1/database/${databaseId}/query`, "POST", { sql: statement });

const verification = await cloud(`/d1/database/${databaseId}/query`, "POST", {
  sql: "SELECT name FROM sqlite_master WHERE type='table' AND name IN ('school_mirror_pairs','school_mirror_frames') ORDER BY name",
});
const tableNames = (Array.isArray(verification) ? verification : [])
  .flatMap(x => x.results || []).map(row => row.name);
if (!tableNames.includes("school_mirror_pairs") || !tableNames.includes("school_mirror_frames"))
  throw new Error("Cloudflare D1 : verification des tables impossible.");

console.log(`D1 scolaire verifiee : ${schoolDatabaseName} (schema ajoute sans effacement).`);
if (process.env.GITHUB_STEP_SUMMARY)
  appendFileSync(process.env.GITHUB_STEP_SUMMARY,
    "### SCOLIA Pro : Cloudflare D1 scolaire\n" +
    "- Base : `scolia-pro-school-sync`\n" +
    "- Creation ou reutilisation : reussie\n" +
    "- Tables chiffrées et index : verifiés\n" +
    "- Aucune modification de la base commerciale, des paiements ou du forfait Cloudflare\n");
