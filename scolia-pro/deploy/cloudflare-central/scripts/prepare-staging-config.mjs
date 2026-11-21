#!/usr/bin/env node
// Prépare uniquement une config staging locale à partir des variables
// d'environnement protégées. Aucun secret n'est écrit dans le fichier.
// Le déploiement est une opération séparée, manuelle et explicitement autorisée.
import { readFileSync, writeFileSync } from "node:fs";
import { resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { dirname } from "node:path";

const root = resolve(dirname(fileURLToPath(import.meta.url)), "..");
const template = resolve(root, "wrangler.staging.toml.example");
const target = resolve(root, "wrangler.staging.toml");

export function validateStagingSettings(env) {
  const id = String(env.SCOLIA_STAGING_D1_ID ?? "");
  const first = String(env.SCOLIA_STAGING_RL_INVITE_NS ?? "");
  const second = String(env.SCOLIA_STAGING_RL_SOURCE_NS ?? "");
  if (!/^[a-f0-9]{8}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{12}$/i.test(id))
    throw Error("staging D1 id: UUID required");
  for (const n of [first, second])
    if (!/^[1-9][0-9]*$/.test(n) || n.length > 18)
      throw Error("rate limit namespace: positive integer string required");
  if (first === second)
    throw Error("rate limit namespaces must be different");
  return { id, first, second };
}

export function renderStagingSettings(templateContent, settings) {
  const fields = [
    ["REPLACE_WITH_NEW_STAGING_D1_DATABASE_ID", settings.id],
    ["REPLACE_WITH_UNIQUE_NUMERIC_NAMESPACE_1", settings.first],
    ["REPLACE_WITH_UNIQUE_NUMERIC_NAMESPACE_2", settings.second],
  ];
  let content = templateContent;
  const declarations = content.split("\n")
    .filter(line => !line.trimStart().startsWith("#")).join("\n");
  if (!declarations.includes('name = "scolia-identity-staging"') ||
      !declarations.includes('database_name = "scolia-identity-staging"') ||
      /api\.jp-services\.org|scolia-test|scolia-api/.test(declarations))
    throw Error("not an isolated staging configuration");
  for (const [oldValue, actual] of fields) {
    if (content.split(oldValue).length !== 2)
      throw Error("required placeholder missing or repeated: " + oldValue);
    content = content.replace(oldValue, actual);
  }
  if (/REPLACE_WITH_/.test(content) ||
      /SCOLIA_AUTH_HMAC_KEY\s*=/m.test(content) ||
      /SCOLIA_PAIRING_AUTH_KEY\s*=/m.test(content) ||
      /CLOUDFLARE_API_TOKEN\s*=/m.test(content))
    throw Error("incomplete or secret-bearing configuration");
  return content;
}

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  try {
    const settings = validateStagingSettings(process.env);
    const content = renderStagingSettings(readFileSync(template, "utf8"), settings);
    writeFileSync(target, content, { mode: 0o600, flag: "wx" });
    console.log("STAGING config prepared locally (no secrets, no production deployment).");
  } catch (error) {
    console.error("Staging configuration blocked:", error.message);
    process.exitCode = 1;
  }
}
