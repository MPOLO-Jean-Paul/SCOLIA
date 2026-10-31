/** No unknown global-search URL may fall back to the home screen. */
const pages = new Set(['/', '/pilotage', '/apprenants', '/reinscriptions', '/encaisser', '/paiements',
  '/recouvrement', '/prises-en-charge', '/caisse', '/depenses', '/notes', '/presences',
  '/discipline', '/personnel', '/parametres', '/utilisateurs', '/donnees', '/audit', '/profil']);
const actions: Record<string, ReadonlySet<string>> = {
  '/depenses': new Set(['nouvelle']), '/caisse': new Set(['arreter']),
  '/notes': new Set(['cours', 'evaluation']), '/discipline': new Set(['incident']),
  '/personnel': new Set(['nouveau']), '/utilisateurs': new Set(['nouveau']),
  '/donnees': new Set(['importer'])
};
const tabs = new Set(['etablissement', 'annees', 'sections', 'promotions']);
const tabActions: Record<string,string> = { promotions: 'promotion', annees: 'annee' };

export function validSearchDestination(url: string): boolean {
  if (!url.startsWith('/') || url.startsWith('//') || url.includes('#') || url.includes('\\')) return false;
  const [path, query, ...extra] = url.split('?');
  if (extra.length || !(pages.has(path) || /^\/apprenants\/[0-9a-f-]{36}$/i.test(path)
     || /^\/paiements\/recus\/[0-9a-f-]{36}$/i.test(path))) return false;
  if (query === undefined) return true;
  if (!query) return false;
  const params = new URLSearchParams(query);
  if (!params.size || new Set([...params.keys()]).size !== params.size
     || [...params].some(([k,v])=>!k || !v)) return false;
  if (path === '/apprenants') {
    return [...params].every(([k,v]) => ['q','promotionId','statut','nouveau'].includes(k)
      && v.length <= 120 && (k !== 'nouveau' || v === '1'));
  }
  if (path === '/parametres') {
    const tab = params.get('onglet') ?? '';
    if (!tabs.has(tab)) return false;
    const action = params.get('action');
    return params.size === 1 || (params.size === 2 && action === tabActions[tab]);
  }
  return params.size === 1 && !!params.get('action') && !!actions[path]?.has(params.get('action')!);
}
