import { writeFileSync } from "node:fs";
import { buildProductionConfig, provisionDatabase, provisionBucket } from "../src/production.mjs";

const {
  CLOUDFLARE_ACCOUNT_ID: accountId,
  CLOUDFLARE_API_TOKEN: apiToken,
  CF_OWNER_DOMAIN: domain,
  OWNER_ACCESS_AUD: audience,
  OWNER_ADMIN_TOKEN: adminToken
} = process.env;

if (!adminToken || adminToken.length < 32)
  throw new Error("Jeton propriétaire de production trop court ou absent.");
// Check domain and audience BEFORE creating any Cloudflare resource.
buildProductionConfig({
  databaseId:"11111111-1111-4111-8111-111111111111", domain, audience
});
const databaseId = await provisionDatabase({accountId,apiToken});
// Le jeton API doit aussi permettre « Workers R2 Storage : Edit ».
const bucket = await provisionBucket({accountId,apiToken});
const config = buildProductionConfig({databaseId,domain,audience});
writeFileSync("wrangler.production.json", JSON.stringify(config,null,2)+"\n", {
  encoding:"utf8", mode:0o600
});
console.log("Configuration de production générée, D1 identifié, workers.dev désactivé.");
console.log("Domaine :",domain);
console.log("Stockage des sauvegardes chiffrées (R2) :",bucket);
console.log("Le contrôle Cloudflare Access est obligatoire avant toute commande propriétaire.");
