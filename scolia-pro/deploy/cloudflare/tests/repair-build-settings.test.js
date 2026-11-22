import { test } from 'node:test';
import assert from 'node:assert/strict';
import { TARGET, classifyBuildLog, findWorkerTag, latestFailedBuild, plannedPatch } from '../scripts/repair-build-settings.mjs';

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

test('diagnostic : selectionne le dernier build echoue uniquement', () => {
  const id1 = '12345678-1111-4111-8111-123456789abc';
  const id2 = '87654321-1111-4111-8111-123456789abc';
  const builds = [
    { build_uuid: id1, build_outcome: 'fail', created_on: '2026-11-21T18:00:00Z' },
    { build_uuid: id2, build_outcome: 'fail', created_on: '2026-11-22T01:00:00Z' },
    { build_uuid: 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa', build_outcome: 'success',
      created_on: '2026-11-22T04:00:00Z' },
    { build_uuid: 'not-an-id', build_outcome: 'fail', created_on: '2026-11-22T05:00:00Z' },
  ];
  assert.equal(latestFailedBuild(builds).build_uuid, id2);
  assert.equal(latestFailedBuild([]), null);
  assert.equal(latestFailedBuild(null), null);
});

test('diagnostic : les messages de logs restent prives, seule une classe fixe sort', () => {
  const secret = 'CLOUDFLARE_API_TOKEN=S3cret-Very-Private';
  const lines = [
    [1763778000, secret],
    [1763778001, 'Failed: root directory not found'],
  ];
  const message = classifyBuildLog(lines);
  assert.match(message, /racine introuvable/);
  assert.ok(!message.includes('S3cret'));
  assert.ok(!message.includes('CLOUDFLARE_API_TOKEN'));
});

test('diagnostic : reconnait erreur du point entree et erreur de jeton', () => {
  assert.match(classifyBuildLog([[1, 'Missing entry-point in your Worker']]),
    /Point d’entrée/);
  assert.match(classifyBuildLog([[1, 'Error: Invalid token']]), /Autorisation/);
  assert.match(classifyBuildLog([[1, 'permission denied while deploying']]), /Permissions/);
});

test('diagnostic : refuse les messages non reconnus sans publier leur contenu', () => {
  const secret = 'very-private-token-987654321';
  assert.match(classifyBuildLog([[1, secret]]), /Cause non reconnue/);
  assert.ok(!classifyBuildLog([[1, secret]]).includes(secret));
  assert.match(classifyBuildLog([]), /Cause non reconnue/);
  assert.match(classifyBuildLog(null), /Cause inconnue/);
});
