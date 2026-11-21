/**
 * Reconciliation bornee de Workers Builds pour le SEUL relais WebSocket SCOLIA.
 * Aucune migration D1, aucun acces a une table scolaire, aucun deploiement.
 * Le mode par defaut lit seulement ; --apply exige confirmation dans Actions.
 */
import { pathToFileURL } from 'node:url';

export const TARGET = Object.freeze({
  workerName: 'scolia-pro-relay',
  owner: 'MPOLO-Jean-Paul',
  repository: 'SCOLIA',
  branch: 'release/direct-apk-scolia-pro-20261103',
  rootDirectory: 'scolia-pro/deploy/cloudflare',
  buildCommand: 'npm ci --no-audit --no-fund && npm run check',
  deployCommand: 'npx wrangler deploy',
});

function assertExpectedRepository(repository) {
  if (repository?.provider_type !== 'github')
    throw new Error('Ce Worker ne reference pas une integration GitHub reconnue. Aucun changement.');
  const name = String(repository.repo_name || '').toLowerCase();
  const owner = String(repository.provider_account_name || '').toLowerCase();
  if (!['scolia', 'mpolo-jean-paul/scolia'].includes(name) ||
      owner !== TARGET.owner.toLowerCase())
    throw new Error('Le depot lie au Worker ne correspond pas a MPOLO-Jean-Paul/SCOLIA. Aucun changement.');
}

/** Construit exclusivement le PATCH des 4 champs de configuration autorises. */
export function plannedPatch(current) {
  if (!current || typeof current !== 'object')
    throw new Error('Configuration Cloudflare vide. Aucune modification.');
  assertExpectedRepository(current.git_repository);
  const settings = current.production_settings;
  if (!settings || typeof settings !== 'object')
    throw new Error('Configuration production absente. Aucune modification.');
  if (!settings.build_token_uuid)
    throw new Error('Token de build non configure. Selectionner un build token dans Cloudflare.');

  const patch = {};
  if (current.git_repository.branch !== TARGET.branch)
    patch.git_repository = { branch: TARGET.branch };

  const expected = {
    root_directory: TARGET.rootDirectory,
    build_command: TARGET.buildCommand,
    deploy_command: TARGET.deployCommand,
  };
  const changed = Object.fromEntries(Object.entries(expected)
    .filter(([key, value]) => settings[key] !== value));
  if (Object.keys(changed).length) patch.production_settings = changed;
  // Deliberement NE PAS modifier build_token_uuid, previews, secrets, routes,
  // Durable Objects, domaines, fichiers ou bases de donnees.
  return patch;
}

export function findWorkerTag(scripts) {
  if (!Array.isArray(scripts)) throw new Error('Liste des Workers invalide.');
  const workers = scripts.filter(x => x?.id === TARGET.workerName);
  if (workers.length !== 1 || typeof workers[0].tag !== 'string' ||
      !/^[0-9a-z-]{8,64}$/i.test(workers[0].tag))
    throw new Error('Tag du Worker scolia-pro-relay introuvable ou ambigu. Aucun changement.');
  return workers[0].tag;
}

async function run() {
  const args = process.argv.slice(2);
  if (args.some(x => !['--inspect', '--apply'].includes(x)) ||
      (args.includes('--inspect') && args.includes('--apply')))
    throw new Error('Syntaxe : node scripts/repair-build-settings.mjs [--inspect|--apply]');
  const apply = args.includes('--apply');
  const token = process.env.CLOUDFLARE_BUILD_SETTINGS_TOKEN;
  const account = process.env.CLOUDFLARE_ACCOUNT_ID;
  if (!token || !account || !/^[0-9a-f]{32}$/i.test(account))
    throw new Error('Variables CLOUDFLARE_BUILD_SETTINGS_TOKEN et CLOUDFLARE_ACCOUNT_ID requises.');
  if (apply && process.env.SCOLIA_REPAIR_CONFIRMATION !== 'REPARER_RELAIS')
    throw new Error('Confirmation REPARER_RELAIS absente : aucun PATCH autorise.');

  const base = 'https://api.cloudflare.com/client/v4/accounts/' + account;
  async function request(route, method = 'GET', body) {
    const response = await fetch(base + route, {
      method,
      headers: {
        Authorization: 'Bearer ' + token,
        Accept: 'application/json',
        ...(body ? { 'Content-Type': 'application/json' } : {}),
      },
      ...(body ? { body: JSON.stringify(body) } : {}),
      signal: AbortSignal.timeout(20_000),
    });
    // Ne jamais loguer le corps d'erreur ni une valeur d'identifiant sensible.
    if (!response.ok) throw new Error('API Cloudflare ' + method + ' : HTTP ' + response.status + '.');
    const json = await response.json();
    if (!json || json.success !== true)
      throw new Error('API Cloudflare ' + method + ' : requete refusee (voir les permissions du token).');
    return json.result;
  }

  const tag = findWorkerTag(await request('/workers/scripts'));
  const route = '/builds/workers/' + encodeURIComponent(tag);
  const current = await request(route);
  const patch = plannedPatch(current);
  if (Object.keys(patch).length === 0) {
    console.log('Configuration Builders deja conforme. Si le build est rouge, inspecter son log (token, quota, git).');
    return;
  }
  // Seuls les NOMS de champs sont affiches pour ne jamais imprimer les secrets.
  console.log('Champs a corriger : ' + [...Object.keys(patch.git_repository || {}),
    ...Object.keys(patch.production_settings || {})].join(', ') + '.');
  if (!apply) {
    console.log('Mode inspection : aucune modification effectuee.');
    return;
  }

  await request(route, 'PATCH', patch);
  if (Object.keys(plannedPatch(await request(route))).length !== 0)
    throw new Error('PATCH termine mais verification de la configuration non conforme.');
  console.log('Configuration Workers Builds corrigee et reverifiee. Relancer un build Cloudflare.');
  console.log('Aucun deploiement du Worker ni migration de base n a ete effectue.');
}

if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  run().catch(err => {
    console.error('SCOLIA Cloudflare Builds : ' + err.message);
    process.exitCode = 1;
  });
}
