import { test } from 'node:test';
import assert from 'node:assert/strict';
import { TARGET, findWorkerTag, plannedPatch } from '../scripts/repair-build-settings.mjs';

function config(overrides = {}) {
  return {
    git_repository: {
      provider_type: 'github', provider_account_name: TARGET.owner,
      repo_name: TARGET.repository, branch: 'main',
      ...(overrides.git_repository ?? {}),
    },
    production_settings: {
      build_token_uuid: 'uuid-existant-mais-non-expose',
      root_directory: '/', build_command: 'npm run build',
      deploy_command: 'npx wrangler deploy',
      ...(overrides.production_settings ?? {}),
    },
    previews_enabled: true,
  };
}

test('corrige uniquement branche, repertoire et commande de build', () => {
  const patch = plannedPatch(config());
  assert.deepEqual(patch, {
    git_repository: { branch: TARGET.branch },
    production_settings: {
      root_directory: TARGET.rootDirectory,
      build_command: TARGET.buildCommand,
    },
  });
  assert.equal(patch.production_settings.build_token_uuid, undefined);
  assert.equal(patch.previews_enabled, undefined);
});

test('configuration conforme : operation sans effet', () => {
  const cfg = config({
    git_repository: { branch: TARGET.branch },
    production_settings: {
      root_directory: TARGET.rootDirectory,
      build_command: TARGET.buildCommand,
      deploy_command: TARGET.deployCommand,
    },
  });
  assert.deepEqual(plannedPatch(cfg), {});
});

test('un repo et un compte differents sont bloques', () => {
  assert.throws(() => plannedPatch(config({ git_repository: {repo_name: 'AUTRE'} })), /depot lie/);
  assert.throws(() => plannedPatch(config({ git_repository: {provider_account_name: 'intrus'} })), /depot lie/);
  assert.throws(() => plannedPatch(config({ git_repository: {provider_type: 'gitlab'} })), /GitHub/);
  assert.throws(() => plannedPatch(config({ git_repository: {provider_account_name: ''} })), /depot lie/);
});

test('aucun jeton de build : pas de tentative de mutation', () => {
  assert.throws(() => plannedPatch(config({production_settings: {build_token_uuid: null}})), /Token de build/);
});

test('le tag du seul Worker attendu est utilise', () => {
  assert.equal(findWorkerTag([{id:'autre',tag:'deadbeef1234'},
    {id:'scolia-pro-relay',tag:'12345678abcd'}]), '12345678abcd');
  assert.throws(() => findWorkerTag([{id:'autre',tag:'deadbeef1234'}]), /introuvable/);
  assert.throws(() => findWorkerTag([
    {id:'scolia-pro-relay',tag:'12345678abcd'},
    {id:'scolia-pro-relay',tag:'aaaabbbbcccc'},
  ]), /ambigu/);
});

test('config illisible est refusee sans tentative', () => {
  assert.throws(() => plannedPatch(null), /vide/);
  assert.throws(() => plannedPatch({ git_repository: config().git_repository }), /absente/);
});
