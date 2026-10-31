import { createContext, useCallback, useContext, useEffect, useState, type ReactNode } from 'react';
import { api, setUnauthorizedHandler } from '../api/client';
import type { Session } from '../api/types';
import { setCurrency } from './format';

interface SessionState {
  session: Session | null;
  loading: boolean;
  configured: boolean | null;
  setSession: (s: Session | null) => void;
  refresh: () => Promise<void>;
  logout: () => Promise<void>;
  can: (permission: string) => boolean;
}

const Ctx = createContext<SessionState | null>(null);

export const P = {
  studentsRead: 'apprenants.lire', studentsWrite: 'apprenants.ecrire', paymentsRead: 'paiements.lire', paymentsCreate: 'paiements.encaisser',
  paymentsCancel: 'paiements.annuler', expensesWrite: 'depenses.ecrire', cashClose: 'caisse.arreter', reportsRead: 'rapports.lire',
  pilotageRead: 'pilotage.lire', settingsWrite: 'parametres.ecrire', usersManage: 'utilisateurs.gerer', auditRead: 'audit.lire', backupManage: 'sauvegardes.gerer',
  academicsWrite: 'scolarite.ecrire', academicsRead: 'scolarite.lire', staffManage: 'personnel.gerer',
} as const;

export function SessionProvider({ children }: { children: ReactNode }) {
  const [session, setSessionState] = useState<Session | null>(null);
  const [loading, setLoading] = useState(true);
  const [configured, setConfigured] = useState<boolean | null>(null);

  const setSession = useCallback((s: Session | null) => {
    setSessionState(s);
    if (s) setCurrency(s.currencySymbol);
  }, []);

  const refresh = useCallback(async () => {
    try {
      const status = await api.get<{ isConfigured: boolean }>('/api/installation');
      setConfigured(status.isConfigured);
      if (status.isConfigured) {
        try { setSession(await api.get<Session>('/api/session')); } catch { setSession(null); }
      }
    } finally {
      setLoading(false);
    }
  }, [setSession]);

  useEffect(() => {
    setUnauthorizedHandler(() => setSessionState(null));
    void refresh();
  }, [refresh]);

  const logout = useCallback(async () => {
    await api.del('/api/session');
    setSessionState(null);
  }, []);

  const can = useCallback((permission: string) => !!session?.permissions.includes(permission), [session]);

  return <Ctx.Provider value={{ session, loading, configured, setSession, refresh, logout, can }}>{children}</Ctx.Provider>;
}

export function useSession() {
  const ctx = useContext(Ctx);
  if (!ctx) throw new Error('SessionProvider manquant');
  return ctx;
}

// ---------- Thème ----------
export type Theme = 'clair' | 'sombre' | 'systeme';
export function applyTheme(theme: Theme) {
  const dark = theme === 'sombre' || (theme === 'systeme' && window.matchMedia('(prefers-color-scheme: dark)').matches);
  document.documentElement.dataset.theme = dark ? 'dark' : 'light';
}
export function storedTheme(): Theme {
  try { return (localStorage.getItem('scolia.theme') as Theme) || 'clair'; } catch { return 'clair'; }
}
export function saveTheme(theme: Theme) {
  try { localStorage.setItem('scolia.theme', theme); } catch { /* stockage indisponible */ }
  applyTheme(theme);
}
