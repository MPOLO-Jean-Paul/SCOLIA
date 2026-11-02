/** Permissions de navigation : masquer un bouton ne suffit pas à protéger une page. */
export const PAGE_RIGHTS: Record<string, string> = {
  '/pilotage': 'pilotage.lire',
  '/apprenants': 'apprenants.lire',
  '/reinscriptions': 'apprenants.ecrire',
  '/encaisser': 'paiements.encaisser',
  '/paiements': 'paiements.lire',
  '/recouvrement': 'rapports.lire',
  '/prises-en-charge': 'paiements.lire',
  '/depenses': 'paiements.lire',
  '/caisse': 'paiements.lire',
  '/notes': 'scolarite.lire',
  '/presences': 'scolarite.lire',
  '/discipline': 'scolarite.lire',
  '/personnel': 'personnel.gerer',
  '/parametres': 'parametres.ecrire',
  '/utilisateurs': 'utilisateurs.gerer',
  '/donnees': 'sauvegardes.gerer',
  '/audit': 'audit.lire',
};

export function requiredPermission(pathname: string): string | undefined {
  if (pathname.startsWith('/apprenants/')) return PAGE_RIGHTS['/apprenants'];
  if (pathname.startsWith('/paiements/recus/')) return PAGE_RIGHTS['/paiements'];
  return PAGE_RIGHTS[pathname];
}

export function canOpenPath(pathname: string, permissions: readonly string[]): boolean {
  if (pathname === '/' || pathname === '/profil') return true;
  const required = requiredPermission(pathname);
  return required !== undefined && permissions.includes(required);
}

export function accountHome(permissions: readonly string[]): string {
  return permissions.includes('pilotage.lire') && !permissions.includes('rapports.lire')
    ? '/pilotage' : '/';
}
