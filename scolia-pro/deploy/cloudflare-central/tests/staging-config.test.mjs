import { test } from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";
import { dirname, resolve } from "node:path";
import {
  validateStagingSettings, renderStagingSettings,
} from "../scripts/prepare-staging-config.mjs";

const here = dirname(fileURLToPath(import.meta.url));
const template = readFileSync(resolve(here, "../wrangler.staging.toml.example"), "utf8");
const valid = {
  SCOLIA_STAGING_D1_ID: "d8c241c4-d744-4b5c-90c5-2ae95bddcf7f",
  SCOLIA_STAGING_RL_INVITE_NS: "600001",
  SCOLIA_STAGING_RL_SOURCE_NS: "600002",
};

test("la config staging exige une D1 isolée et deux limites distinctes", () => {
  const v = validateStagingSettings(valid);
  const text = renderStagingSettings(template, v);
  assert.match(text, /name = "scolia-identity-staging"/);
  assert.match(text, /database_name = "scolia-identity-staging"/);
  assert.match(text, /database_id = "d8c241c4-d744-4b5c-90c5-2ae95bddcf7f"/);
  assert.match(text, /namespace_id = "600001"/);
  assert.match(text, /namespace_id = "600002"/);
  assert.match(text, /workers_dev = true/);
  assert.doesNotMatch(text, /REPLACE_WITH_/);
  assert.doesNotMatch(text, /^SCOLIA_PAIRING_AUTH_KEY\s*=/m);
  assert.doesNotMatch(text, /^CLOUDFLARE_API_TOKEN\s*=/m);
});

test("les placeholders, namespace non numérique et ID non UUID sont rejetés", () => {
  for (const cfg of [
    { ...valid, SCOLIA_STAGING_D1_ID: "scolia-test" },
    { ...valid, SCOLIA_STAGING_D1_ID: "REPLACE_WITH_NEW_STAGING_D1_DATABASE_ID" },
    { ...valid, SCOLIA_STAGING_RL_INVITE_NS: "not-a-number" },
    { ...valid, SCOLIA_STAGING_RL_INVITE_NS: "-42" },
    { ...valid, SCOLIA_STAGING_RL_SOURCE_NS: "600001" },
    { ...valid, SCOLIA_STAGING_RL_SOURCE_NS: "" },
  ]) assert.throws(() => validateStagingSettings(cfg));
});

test("le domaine de production ou une clé stockée dans le TOML bloque le staging", () => {
  const settings = validateStagingSettings(valid);
  assert.throws(() => renderStagingSettings(
    template.replace('name = "scolia-identity-staging"', 'name = "scolia-api"'),
    settings));
  assert.throws(() => renderStagingSettings(
    template + '\nSCOLIA_PAIRING_AUTH_KEY = "not-a-secret"\n', settings));
  assert.throws(() => renderStagingSettings(
    template.replace('database_name = "scolia-identity-staging"',
      'database_name = "scolia-test"'), settings));
});

test("un fichier qui n'a plus tous ses marqueurs est rejeté", () => {
  const settings = validateStagingSettings(valid);
  assert.throws(() => renderStagingSettings(
    template.replace("REPLACE_WITH_UNIQUE_NUMERIC_NAMESPACE_2", "42"), settings));
});
