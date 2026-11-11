import { Suspense, useCallback, useEffect, useMemo, useState } from 'react';
import { Outlet, useLocation, useNavigate } from 'react-router-dom';
import { useQueryClient } from '@tanstack/react-query';
import { Command, HandCoins, KeyRound, LogOut, RefreshCw, Search } from 'lucide-react';
import { api, openDocument } from '../api/client';
import { P, saveTheme, storedTheme, useSession, type Theme } from '../lib/session';
import { initials } from '../lib/format';
import { canOpenPath } from '../lib/access';
import { allCommands, buildTabs, type RibbonCommand } from '../lib/ribbon';
import { runCommand } from '../lib/commands';
import { Ribbon } from './Ribbon';
import { Backstage } from './Backstage';
import { StatusBar, ZOOM_MAX, ZOOM_MIN, ZOOM_STEP } from './StatusBar';
import { AboutDialog, PreferencesDialog, ShortcutsDialog } from './Dialogs';
import { CommandPalette } from './CommandPalette';
import { errorText, Loading, useToast } from './ui';
import '../ribbon.css';

type Density = 'confortable' | 'compact';
type TextSize = 'petit' | 'normal' | 'grand' | 'tres-grand';
type DialogId = 'raccourcis' | 'apropos' | 'preferences' | null;
export const TEXT_SIZES: Record<TextSize, number> = { petit: 0.9, normal: 1, grand: 1.15, 'tres-grand': 1.3 };
const TEXT_ORDER: TextSize[] = ['petit', 'normal', 'grand', 'tres-grand'];
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
/** Historique propre à chaque compte : aucun utilisateur ne voit les pages récentes d'un autre. */
function recentKey(user: string) { return `${RECENTS_KEY}.${encodeURIComponent(user)}`; }
function readRecents(user?: string): Recent[] {
  if (!user) return [];
  try {
    const items: unknown = JSON.parse(localStorage.getItem(recentKey(user)) ?? '[]');
    return Array.isArray(items) ? items.filter((x): x is Recent =>
      !!x && typeof x === 'object' && typeof x.to === 'string' && typeof x.label === 'string').slice(0, 6) : [];
  } catch { return []; }
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
  const [recents, setRecents] = useState<Recent[]>(() => readRecents(session?.userName));
  const [activeId, setActiveId] = useState('accueil');
  const [backstage, setBackstage] = useState(false);
  const [palette, setPalette] = useState(false);
  const [dialog, setDialog] = useState<DialogId>(null);
  const [textSize, setTextSize] = useState<TextSize>(() => (readPref('scolia.taille', 'normal') as TextSize) in TEXT_SIZES ? (readPref('scolia.taille', 'normal') as TextSize) : 'normal');
  const [motion, setMotion] = useState(() => readPref('scolia.animations', '1') === '1');
  const [tips, setTips] = useState(() => readPref('scolia.infobulles', '1') === '1');
  const [grid, setGrid] = useState(() => readPref('scolia.quadrillage', '0') === '1');
  const [fullscreen, setFullscreen] = useState(() => !!document.fullscreenElement);

  // Même si un ancien lien reste en cache, ne pas l'afficher à un compte sans le droit correspondant.
  const visibleRecents = useMemo(() => recents.filter(r =>
    canOpenPath(r.to.split('?')[0], session?.permissions ?? [])), [recents, session?.permissions]);
  const tabs = useMemo(() => buildTabs({ pathname: location.pathname, recents: visibleRecents, can }), [location.pathname, visibleRecents, can]);
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

  useEffect(() => { setRecents(readRecents(session?.userName)); }, [session?.userName]);

  // Mémorise uniquement les pages ouvertes par ce compte, sans mélanger les sessions.
  useEffect(() => {
    const label = pageLabels.get(location.pathname);
    if (!label || !session?.userName || !canOpenPath(location.pathname, session.permissions)) return;
    setRecents((current) => {
      const next = [{ to: location.pathname, label }, ...current.filter((r) => r.to !== location.pathname)].slice(0, 6);
      writePref(recentKey(session.userName), JSON.stringify(next));
      return next;
    });
  }, [location.pathname, pageLabels, session?.userName, session?.permissions]);

  useEffect(() => {
    const root = document.documentElement;
    root.dataset.density = density;
    root.dataset.motion = motion ? 'normal' : 'reduced';
    root.dataset.tips = tips ? 'on' : 'off';
    root.dataset.grid = grid ? 'on' : 'off';
    root.style.setProperty('--fs', String(TEXT_SIZES[textSize]));
  }, [density, motion, tips, grid, textSize]);

  useEffect(() => {
    const onChange = () => setFullscreen(!!document.fullscreenElement);
    document.addEventListener('fullscreenchange', onChange);
    return () => document.removeEventListener('fullscreenchange', onChange);
  }, []);

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

  const changeTextSize = useCallback((value: TextSize) => {
    setTextSize(value);
    writePref('scolia.taille', value);
  }, []);

  const toggleFlag = useCallback((key: string, set: (v: boolean) => void, current: boolean) => {
    set(!current);
    writePref(key, current ? '0' : '1');
  }, []);

  const toggleFullscreen = useCallback(async () => {
    try {
      if (document.fullscreenElement) await document.exitFullscreen();
      else await document.documentElement.requestFullscreen();
    } catch {
      toast('Le plein écran n’est pas disponible sur ce poste.', 'error');
    }
  }, [toast]);

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
      case 'imprimer': window.print(); return;
      case 'plein-ecran': await toggleFullscreen(); return;
      case 'taille-texte': changeTextSize(TEXT_ORDER[(TEXT_ORDER.indexOf(textSize) + 1) % TEXT_ORDER.length]); return;
      case 'animations': toggleFlag('scolia.animations', setMotion, motion); return;
      case 'info-bulles': toggleFlag('scolia.infobulles', setTips, tips); return;
      case 'quadrillage': toggleFlag('scolia.quadrillage', setGrid, grid); return;
      case 'export-recus-xlsx': openDocument('/api/paiements/export', { format: 'xlsx' }); return;
      case 'export-recus-pdf': openDocument('/api/paiements/export'); return;
      case 'export-debiteurs-xlsx': openDocument('/api/rapports/debiteurs/export', { format: 'xlsx' }); return;
      case 'export-debiteurs-pdf': openDocument('/api/rapports/debiteurs/export'); return;
      case 'export-situation-pdf': openDocument('/api/rapports/promotions/export'); return;
      case 'export-apprenants-xlsx': openDocument('/api/rapports/apprenants/export', { format: 'xlsx' }); return;
      case 'export-depenses-xlsx': openDocument('/api/depenses/export', { format: 'xlsx' }); return;
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
  }, [changeDensity, changeStatusBar, changeTextSize, changeTheme, changeZoom, density, grid, logout, motion, navigate, qc, statusBar, textSize, theme, tips, toast, toggleCollapsed, toggleFlag, toggleFullscreen, zoom]);

  const onExecute = useCallback((c: RibbonCommand) => { void run(c.id, c.to); }, [run]);

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (e.defaultPrevented || document.querySelector('[aria-modal="true"]')) return;
      if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === 'k') { e.preventDefault(); setPalette(true); return; }
      if (e.ctrlKey && e.key === 'F1') { e.preventDefault(); toggleCollapsed(); return; }
      if (e.ctrlKey && e.key.toLowerCase() === 'p') { e.preventDefault(); void run('imprimer'); return; }
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
    'plein-ecran': fullscreen,
    animations: motion,
    'info-bulles': tips,
    quadrillage: grid,
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
        <Suspense fallback={<Loading rows={5} />}>
          <Outlet />
        </Suspense>
      </main>

      {statusBar && <StatusBar pageTitle={pageTitle} zoom={zoom} onZoom={changeZoom} />}

      {palette && <CommandPalette groups={paletteGroups} onClose={() => setPalette(false)} onPreferences={() => setDialog('preferences')} canConfigureSchool={can(P.settingsWrite)} canSearchRecords={can(P.studentsRead) || can(P.paymentsRead)} />}
      {backstage && (
        <Backstage onClose={() => setBackstage(false)} onNavigate={(to) => { setBackstage(false); navigate(to); }} onRun={(id) => void run(id)} />
      )}
      {dialog === 'raccourcis' && <ShortcutsDialog onClose={() => setDialog(null)} />}
      {dialog === 'apropos' && <AboutDialog schoolName={session?.schoolName ?? 'SCOLIA'} onClose={() => setDialog(null)} />}
      {dialog === 'preferences' && (
        <PreferencesDialog
          theme={theme} onTheme={changeTheme}
          textSize={textSize} onTextSize={changeTextSize}
          density={density} onDensity={changeDensity}
          motion={motion} onMotion={(v) => { setMotion(v); writePref('scolia.animations', v ? '1' : '0'); }}
          tips={tips} onTips={(v) => { setTips(v); writePref('scolia.infobulles', v ? '1' : '0'); }}
          grid={grid} onGrid={(v) => { setGrid(v); writePref('scolia.quadrillage', v ? '1' : '0'); }}
          statusBar={statusBar} onStatusBar={changeStatusBar}
          onReset={() => { changeTheme('systeme'); changeTextSize('normal'); changeDensity('confortable'); changeStatusBar(true); setMotion(true); writePref('scolia.animations', '1'); setTips(true); writePref('scolia.infobulles', '1'); setGrid(false); writePref('scolia.quadrillage', '0'); changeZoom(100); }}
          onClose={() => setDialog(null)}
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
