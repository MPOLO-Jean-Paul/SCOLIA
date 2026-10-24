import { useCallback, useEffect, useMemo, useState } from 'react';
import { Outlet, useLocation, useNavigate } from 'react-router-dom';
import { useQueryClient } from '@tanstack/react-query';
import { Command, HandCoins, KeyRound, LogOut, RefreshCw, Search } from 'lucide-react';
import { api } from '../api/client';
import { P, saveTheme, storedTheme, useSession, type Theme } from '../lib/session';
import { initials } from '../lib/format';
import { allCommands, buildTabs, type RibbonCommand } from '../lib/ribbon';
import { runCommand } from '../lib/commands';
import { Ribbon } from './Ribbon';
import { Backstage } from './Backstage';
import { StatusBar, ZOOM_MAX, ZOOM_MIN, ZOOM_STEP } from './StatusBar';
import { AboutDialog, PreferencesDialog, ShortcutsDialog } from './Dialogs';
import { CommandPalette } from './CommandPalette';
import { errorText, useToast } from './ui';
import '../ribbon.css';

type Density = 'confortable' | 'compact';
type DialogId = 'raccourcis' | 'apropos' | 'preferences' | null;
interface Recent { to: string; label: string }

const ALT_PAGES: Record<string, string> = { 'Alt+1': '/', 'Alt+2': '/apprenants', 'Alt+3': '/encaisser', 'Alt+4': '/recouvrement' };
const RECENTS_KEY = 'scolia.recents';

function readPref(key: string, fallback: string): string {
  try { return localStorage.getItem(key) ?? fallback; } catch { return fallback; }
}
function writePref(key: string, value: string) {
  try { localStorage.setItem(key, value); } catch { /* stockage indisponible : préférence non mémorisée */ }
}
function clampZoom(value: number): number {
  if (Number.isNaN(value)) return 100;
  return Math.min(ZOOM_MAX, Math.max(ZOOM_MIN, Math.round(value / ZOOM_STEP) * ZOOM_STEP));
}
function readRecents(): Recent[] {
  try { return JSON.parse(localStorage.getItem(RECENTS_KEY) ?? '[]') as Recent[]; } catch { return []; }
}
function isDark(theme: Theme): boolean {
  return theme === 'sombre' || (theme === 'systeme' && window.matchMedia('(prefers-color-scheme: dark)').matches);
}

export function Layout() {
  const { session, logout, can } = useSession();
  const navigate = useNavigate();
  const location = useLocation();
  const qc = useQueryClient();
  const toast = useToast();

  const [theme, setTheme] = useState<Theme>(storedTheme());
  const [density, setDensity] = useState<Density>(() => (readPref('scolia.densite', '') === 'compact' ? 'compact' : 'confortable'));
  const [statusBar, setStatusBar] = useState(() => readPref('scolia.barreetat', '1') === '1');
  const [collapsed, setCollapsed] = useState(() => readPref('scolia.ruban', '0') === '1');
  const [zoom, setZoom] = useState(() => clampZoom(Number(readPref('scolia.zoom', '100'))));
  const [recents, setRecents] = useState<Recent[]>(readRecents);
  const [activeId, setActiveId] = useState('accueil');
  const [backstage, setBackstage] = useState(false);
  const [palette, setPalette] = useState(false);
  const [dialog, setDialog] = useState<DialogId>(null);

  const tabs = useMemo(() => buildTabs({ pathname: location.pathname, recents, can }), [location.pathname, recents, can]);
  const pageLabels = useMemo(() => {
    const labels = new Map<string, string>();
    for (const c of allCommands()) if (c.to && !labels.has(c.to)) labels.set(c.to, c.label);
    return labels;
  }, []);
  const paletteGroups = useMemo(() => tabs.filter((t) => !t.context).map((t) => ({
    title: t.label,
    items: t.groups.flatMap((g) => g.commands).filter((c) => c.to).map((c) => ({ to: c.to!, label: c.label, icon: c.icon })),
  })).filter((g) => g.items.length > 0), [tabs]);

  // Une page contextuelle (reçu, apprenant) prend le focus dès qu'on y arrive.
  const contextId = tabs.find((t) => t.context)?.id;
  useEffect(() => { if (contextId) setActiveId(contextId); }, [contextId]);

  // Mémorise les pages récemment ouvertes pour le groupe « Récents ».
  useEffect(() => {
    const label = pageLabels.get(location.pathname);
    if (!label) return;
    setRecents((current) => {
      const next = [{ to: location.pathname, label }, ...current.filter((r) => r.to !== location.pathname)].slice(0, 6);
      writePref(RECENTS_KEY, JSON.stringify(next));
      return next;
    });
  }, [location.pathname, pageLabels]);

  useEffect(() => {
    document.documentElement.dataset.density = density;
  }, [density]);

  const changeTheme = useCallback((next: Theme) => {
    setTheme(next);
    saveTheme(next);
  }, []);

  const toggleCollapsed = useCallback(() => {
    setCollapsed((c) => { writePref('scolia.ruban', c ? '0' : '1'); return !c; });
  }, []);

  const changeZoom = useCallback((value: number) => {
    const next = clampZoom(value);
    setZoom(next);
    writePref('scolia.zoom', String(next));
  }, []);

  const changeDensity = useCallback((value: Density) => {
    setDensity(value);
    writePref('scolia.densite', value);
  }, []);

  const changeStatusBar = useCallback((value: boolean) => {
    setStatusBar(value);
    writePref('scolia.barreetat', value ? '1' : '0');
  }, []);

  const run = useCallback(async (id: string, to?: string) => {
    if (to) { setBackstage(false); navigate(to); return; }
    setBackstage(false);
    switch (id) {
      case 'rechercher': setPalette(true); return;
      case 'actualiser': await qc.invalidateQueries(); return;
      case 'theme': changeTheme(isDark(theme) ? 'clair' : 'sombre'); return;
      case 'densite': changeDensity(density === 'compact' ? 'confortable' : 'compact'); return;
      case 'barre-etat': changeStatusBar(!statusBar); return;
      case 'ruban': toggleCollapsed(); return;
      case 'zoom-moins': changeZoom(zoom - ZOOM_STEP); return;
      case 'zoom-plus': changeZoom(zoom + ZOOM_STEP); return;
      case 'zoom-reset': changeZoom(100); return;
      case 'raccourcis': setDialog('raccourcis'); return;
      case 'apropos': setDialog('apropos'); return;
      case 'preferences': setDialog('preferences'); return;
      case 'sauvegarder':
        try {
          await api.post('/api/sauvegardes');
          toast('Sauvegarde créée et vérifiée.', 'success');
          await qc.invalidateQueries({ queryKey: ['sauvegardes'] });
        } catch (e) { toast(errorText(e), 'error'); }
        return;
      case 'diag-base':
        try {
          const r = await api.get<{ intacte: boolean }>('/api/sauvegardes/integrite');
          toast(r.intacte ? 'Base de données intègre.' : 'Anomalie détectée : restaurez la dernière sauvegarde.', r.intacte ? 'success' : 'error');
        } catch (e) { toast(errorText(e), 'error'); }
        return;
      case 'diag-audit':
        try {
          const r = await api.get<{ isIntact: boolean }>('/api/audit/verification');
          toast(r.isIntact ? 'Journal d’audit intact : aucune modification détectée.' : 'Journal d’audit altéré : une entrée ne correspond plus à son empreinte.', r.isIntact ? 'success' : 'error');
        } catch (e) { toast(errorText(e), 'error'); }
        return;
      case 'deconnexion':
        await logout();
        navigate('/connexion');
        return;
      default:
        // Commande propre à la page affichée (imprimer un reçu, ouvrir une fiche…).
        runCommand(id);
    }
  }, [changeDensity, changeStatusBar, changeTheme, changeZoom, density, logout, navigate, qc, statusBar, theme, toast, toggleCollapsed, zoom]);

  const onExecute = useCallback((c: RibbonCommand) => { void run(c.id, c.to); }, [run]);

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === 'k') { e.preventDefault(); setPalette(true); return; }
      if (e.ctrlKey && e.key === 'F1') { e.preventDefault(); toggleCollapsed(); return; }
      if (e.key === 'F5') { e.preventDefault(); void run('actualiser'); return; }
      if (e.altKey && ALT_PAGES[`Alt+${e.key}`]) { e.preventDefault(); navigate(ALT_PAGES[`Alt+${e.key}`]); return; }
      if (e.key === 'F2' && can(P.paymentsCreate)) { e.preventDefault(); navigate('/encaisser'); }
    };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [can, navigate, run, toggleCollapsed]);

  const pressed: Record<string, boolean> = {
    theme: isDark(theme),
    densite: density === 'compact',
    'barre-etat': statusBar,
    ruban: collapsed,
  };
  const pageTitle = pageLabels.get(location.pathname) ?? '';
  const current = tabs.find((t) => t.id === activeId) ?? tabs[0];

  return (
    <div className="app">
      <header className="titlebar">
        <div className="qat" role="toolbar" aria-label="Barre d’accès rapide">
          <img
            className="qat-logo"
            src="/api/etablissement/logo"
            alt=""
            onError={(e) => { (e.currentTarget as HTMLImageElement).src = '/scolia.png'; }}
          />
          <button type="button" className="qat-btn" title="Actualiser (F5)" aria-label="Actualiser" onClick={() => void run('actualiser')}><RefreshCw /></button>
          {can(P.paymentsCreate) && (
            <button type="button" className="qat-btn" title="Nouvel encaissement (F2)" aria-label="Nouvel encaissement" onClick={() => navigate('/encaisser')}><HandCoins /></button>
          )}
          <button type="button" className="qat-btn" title="Ouvrir les commandes (Ctrl+K)" aria-label="Ouvrir les commandes (Ctrl+K)" onClick={() => setPalette(true)}><Command /></button>
        </div>
        <div className="titlebar-doc" title={session?.schoolName}>
          {session?.schoolName ?? 'SCOLIA'}{session?.currentYear && <span> · année {session.currentYear}</span>} <span>— SCOLIA Pro</span>
        </div>
        <button type="button" className="titlebar-search" onClick={() => setPalette(true)}>
          <Search /><span>Rechercher un apprenant, un reçu, une page…</span><kbd>Ctrl K</kbd>
        </button>
        <UserMenu name={session?.displayName ?? ''} role={session?.roleLabel ?? ''} onPalette={() => setPalette(true)} onLogout={async () => { await logout(); navigate('/connexion'); }} />
      </header>

      <Ribbon
        tabs={tabs}
        activeId={current?.id ?? 'accueil'}
        onSelect={setActiveId}
        onFile={() => setBackstage(true)}
        pressed={pressed}
        collapsed={collapsed}
        onToggleCollapsed={toggleCollapsed}
        onExecute={onExecute}
        onLaunch={(id) => void run(id)}
      />

      <main className="content" id="contenu" style={{ zoom: zoom / 100 }}>
        <Outlet />
      </main>

      {statusBar && <StatusBar pageTitle={pageTitle} zoom={zoom} onZoom={changeZoom} />}

      {palette && <CommandPalette groups={paletteGroups} onClose={() => setPalette(false)} />}
      {backstage && (
        <Backstage onClose={() => setBackstage(false)} onNavigate={(to) => { setBackstage(false); navigate(to); }} onRun={(id) => void run(id)} />
      )}
      {dialog === 'raccourcis' && <ShortcutsDialog onClose={() => setDialog(null)} />}
      {dialog === 'apropos' && <AboutDialog schoolName={session?.schoolName ?? 'SCOLIA'} onClose={() => setDialog(null)} />}
      {dialog === 'preferences' && (
        <PreferencesDialog
          theme={theme} onTheme={changeTheme} density={density} onDensity={changeDensity}
          statusBar={statusBar} onStatusBar={changeStatusBar} onClose={() => setDialog(null)}
        />
      )}
    </div>
  );
}

function UserMenu({ name, role, onLogout, onPalette }: { name: string; role: string; onLogout: () => void; onPalette: () => void }) {
  const [open, setOpen] = useState(false);
  const navigate = useNavigate();
  return (
    <div className="user-menu">
      <button type="button" className="user-chip" onClick={() => setOpen((o) => !o)} aria-haspopup="menu" aria-expanded={open}>
        <span className="avatar">{initials(name)}</span>
        <span className="who"><span className="who-name">{name}</span><small>{role}</small></span>
      </button>
      {open && (
        <div className="card user-dropdown" onMouseLeave={() => setOpen(false)}>
          <button type="button" className="btn ghost" onClick={() => { setOpen(false); navigate('/profil'); }}><KeyRound />Mon mot de passe</button>
          <button type="button" className="btn ghost" onClick={() => { setOpen(false); onPalette(); }}><Command />Commandes (Ctrl K)</button>
          <button type="button" className="btn ghost danger-text" onClick={onLogout}><LogOut />Se déconnecter</button>
        </div>
      )}
    </div>
  );
}
